using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// These tests run the actual channel receive loop, including its cipher state,
// validation, session snapshot, JSON save and ACK. They own no external process.
internal static class SkillSlotPersistenceSmokeTests
{
    private static readonly CharacterSkillRecord[] Initial = [new(0, 1, 1), new(1, 3, 1), new(48, 2, 1)];
    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(2);

    internal static async Task RunAsync(Action<bool, string> check)
    {
        await Case("fragmented next frame", FragmentedAsync);
        await Case("corrupt next frame", CorruptAsync);
        await Case("short next command", ShortAsync);
        await Case("independent illegal next command", IndependentAsync);
        await Case("compound coalesced and delayed/reopened", CompoundAsync);
        await Case("null save and retained session", NullAsync);
        await Case("real rename failure rollback", RenameFailureAsync);
        await Case("post-commit logger failure", LoggerAsync);
        await Case("steal skill SP cost", StealSkillCostAsync);

        async Task Case(string name, Func<Action<bool, string>, Task> run)
        {
            try { await run(check); }
            catch (Exception ex) { check(false, $"skill-slot {name}: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private static async Task FragmentedAsync(Action<bool, string> check)
    {
        await using var fixture = await Fixture.CreateAsync();
        var wire = await fixture.ConnectAsync();
        var first = wire.Command(30, 0, 2);
        var second = wire.Command(30, 2, 3);
        await wire.SendAsync([.. first, second[0]]);
        // Do not complete the second header until the first result is observed.
        var ack = await wire.ReadAsync(1, 30, ReplyDeadline);
        check(Success(ack, 0, 2), "skill-slot first ACK does not wait for a partial next header");
        await fixture.CheckStateAsync(check, [new(2, 1, 1), new(1, 3, 1), new(48, 2, 1)],
            "first fragmented-frame edit is committed before the next header completes");
        await wire.SendAsync(second[1..]);
        check(Success(await wire.ReadAsync(1, 30), 2, 3), "skill-slot completed next frame receives its own ACK");
        await fixture.CheckStateAsync(check, [new(3, 1, 1), new(1, 3, 1), new(48, 2, 1)],
            "fragmented next frame applies in wire order");
    }

    private static async Task CorruptAsync(Action<bool, string> check)
    {
        await using var fixture = await Fixture.CreateAsync();
        var wire = await fixture.ConnectAsync();
        var first = wire.Command(30, 0, 2);
        var corrupt = wire.CorruptCommand(30, 2, 3);
        await wire.SendAsync([.. first, .. corrupt]);
        check(Success(await wire.ReadAsync(1, 30, ReplyDeadline), 0, 2),
            "skill-slot undecodable adjacent frame cannot discard the valid first ACK");
        await fixture.CheckStateAsync(check, [new(2, 1, 1), new(1, 3, 1), new(48, 2, 1)],
            "undecodable adjacent command cannot discard or alter the valid first save");
    }

    private static async Task ShortAsync(Action<bool, string> check)
    {
        await using var fixture = await Fixture.CreateAsync();
        var wire = await fixture.ConnectAsync();
        await wire.SendAsync([.. wire.Command(30, 0, 2), .. wire.Command(30, 2)]);
        check(Success(await wire.ReadAsync(1, 30), 0, 2), "skill-slot valid command before short command succeeds");
        check(Error(await wire.ReadAsync(1, 30, ReplyDeadline)), "skill-slot short adjacent command receives its own error");
        await fixture.CheckStateAsync(check, [new(2, 1, 1), new(1, 3, 1), new(48, 2, 1)],
            "short adjacent command has no state effect");
    }

    private static async Task IndependentAsync(Action<bool, string> check)
    {
        await using var fixture = await Fixture.CreateAsync();
        var wire = await fixture.ConnectAsync();
        await wire.SendAsync([.. wire.Command(30, 0, 2), .. wire.Command(30, 250, 0)]);
        check(Success(await wire.ReadAsync(1, 30), 0, 2), "skill-slot independent invalid command cannot reject the legal first edit");
        check(Error(await wire.ReadAsync(1, 30)), "skill-slot independent invalid command is rejected separately");
        await fixture.CheckStateAsync(check, [new(2, 1, 1), new(1, 3, 1), new(48, 2, 1)],
            "coalescing unrelated commands does not roll back an accepted edit");
    }

    private static async Task CompoundAsync(Action<bool, string> check)
    {
        CharacterSkillRecord[] expected = [new(1, 1, 1), new(174, 3, 1), new(48, 2, 1)];
        foreach (var coalesced in new[] { true, false })
        {
            await using var fixture = await Fixture.CreateAsync();
            var wire = await fixture.ConnectAsync();
            var first = wire.Command(30, 174, 1);
            var second = wire.Command(30, 1, 0);
            if (coalesced) await wire.SendAsync([.. first, .. second]);
            else await wire.SendAsync(first);
            check(Success(await wire.ReadAsync(1, 30), 174, 1),
                $"skill-slot compound coalesced={coalesced} first ACK preserves native order");
            if (!coalesced)
            {
                // Receiving the first ACK already forces distinct handling; the
                // additional gap is deliberately greater than the old 5 ms wait.
                await Task.Delay(20);
                await wire.SendAsync(second);
            }
            check(Success(await wire.ReadAsync(1, 30), 1, 0),
                $"skill-slot compound coalesced={coalesced} second ACK preserves native order");
            await fixture.CheckStateAsync(check, expected, $"compound coalesced={coalesced} commits both class-specific placements");
            await fixture.CloseConnectionAsync();
            // A fresh store and fresh service get the same DataPath, with no reseed.
            await fixture.ReopenAsync();
            var relog = await fixture.ConnectAsync();
            check(LayoutEquals(relog.LoginSkills, expected) && relog.LoginSp == 123,
                $"skill-slot compound coalesced={coalesced} actual relog NOTI19 restores explicit slots, levels and SP");
            await fixture.CheckStateAsync(check, expected, "reopened store preserves the compound layout");
        }
    }

    private static async Task NullAsync(Action<bool, string> check)
    {
        await using var fixture = await Fixture.CreateAsync();
        var wire = await fixture.ConnectAsync();
        var original = await fixture.CharacterAsync();
        var deleted = await fixture.Store.DeleteCharacterAsync("u09x", original.Slot);
        if (!deleted.Deleted) throw new InvalidOperationException("private null-save fixture did not delete its character");
        await wire.SendAsync(wire.Command(30, 0, 2));
        check(Error(await wire.ReadAsync(1, 30)), "skill-slot real null save cannot produce success ACK");
        var restored = await fixture.Store.ReplaceAllCharactersAsync("u09x", [original]);
        if (!restored.Replaced) throw new InvalidOperationException("private null-save fixture did not restore its character");
        // If the rejected candidate was installed in the session, source 0 is
        // now empty and this request cannot produce the required skill at 3.
        await wire.SendAsync(wire.Command(30, 0, 3));
        check(Success(await wire.ReadAsync(1, 30), 0, 3), "skill-slot session continues after a rejected null save");
        await fixture.CheckStateAsync(check, [new(3, 1, 1), new(1, 3, 1), new(48, 2, 1)],
            "null save preserves the session's previous slot mapping");
    }

    private static async Task RenameFailureAsync(Action<bool, string> check)
    {
        // Windows sharing flags make the temporary write succeed and the final
        // replacement fail. Do not silently substitute a pre-serialization fault.
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("SKIP: skill-slot real rename-sharing failure requires Windows");
            return;
        }
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.CharacterAsync();
        var baseline = await File.ReadAllBytesAsync(fixture.Options.DataPath);
        CharacterSkillRecord[] candidate = [new(2, 1, 1), new(1, 3, 1), new(48, 2, 1)];
        using (var blocker = new FileStream(fixture.Options.DataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { await fixture.Store.SaveCharacterSkillsAsync("u09x", original.Id, 99, candidate); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            check(failed, "skill-slot real destination sharing lock rejects atomic rename");
            check(File.Exists(fixture.Options.DataPath + ".tmp"), "skill-slot failed rename occurs after a temporary snapshot was written");
            check((await File.ReadAllBytesAsync(fixture.Options.DataPath)).SequenceEqual(baseline),
                "skill-slot failed rename preserves the previous committed file");
            var cached = await fixture.CharacterAsync();
            check(cached.Sp == original.Sp && LayoutEquals(cached.Skills ?? [], original.Skills ?? []),
                "skill-slot failed rename rolls back cached skills and SP");
        }
        var unrelated = await fixture.Store.SaveCharacterLocationAsync("u09x", original.Id,
            original.TownId, original.AreaId, (short)(original.X + 1), original.Y, original.Direction);
        check(unrelated is not null, "skill-slot fixture completes an unrelated save after rename failure");
        await fixture.ReopenAsync();
        var reopened = await fixture.CharacterAsync();
        check(reopened.Sp == original.Sp && LayoutEquals(reopened.Skills ?? [], original.Skills ?? []),
            "skill-slot unrelated later save cannot resurrect a failed skill candidate");
    }

    private static async Task LoggerAsync(Action<bool, string> check)
    {
        var logger = new ThrowAfterSnapshotLogger();
        await using var fixture = await Fixture.CreateAsync(logger);
        var original = await fixture.CharacterAsync();
        CharacterSkillRecord[] expected = [new(2, 1, 1), new(1, 3, 1), new(48, 2, 1)];
        logger.Armed = true;
        CharacterRecord? result = null;
        Exception? failure = null;
        try { result = await fixture.Store.SaveCharacterSkillsAsync("u09x", original.Id, 123, expected); }
        catch (Exception ex) { failure = ex; }
        finally { logger.Armed = false; }
        check(logger.Throws == 1, "skill-slot fixture exercises a logger exception after atomic replacement");
        check(failure is null && result is not null && LayoutEquals(result.Skills ?? [], expected),
            "skill-slot post-commit diagnostic failure returns the successful committed record");
        await fixture.CheckStateAsync(check, expected, "post-commit logger failure does not undo committed memory or disk");
        await fixture.ReopenAsync();
        await fixture.CheckStateAsync(check, expected, "post-commit logger failure stays committed after reopen");
    }

    private static async Task StealSkillCostAsync(Action<bool, string> check)
    {
        foreach (var (growType, level, sp, charged, error) in new[]
        {
            (2, 23, 60, 0, 18),
            (2, 24, 59, 0, 2),
            (2, 24, 60, 60, 0),
            (1, 20, 60, 30, 0)
        })
        {
            await using var fixture = await Fixture.CreateAsync(growType: growType, level: level, sp: sp);
            var wire = await fixture.ConnectAsync();
            await wire.SendAsync(wire.Command(GameProtocolEngine.BuySkillCommand, 4));
            var reply = (await wire.ReadAsync(1, GameProtocolEngine.BuySkillCommand)).Payload;
            var saved = (await fixture.Store.GetCharactersAsync("u09x"))[0];
            if (error != 0)
            {
                check(reply.SequenceEqual(new byte[] { 0, (byte)error }) && saved.Sp == sp
                    && saved.Skills!.All(s => s.SkillId != 4),
                    $"steal skill is refused at character level {level} with {sp} SP without altering the save");
                continue;
            }

            check(reply.Length == 6 && reply[0] == 1 && reply[4] == 4 && reply[5] == 1
                && BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(1)) == sp - charged
                && saved.Sp == sp - charged && saved.Skills!.Any(s => s.SkillId == 4 && s.Level == 1),
                $"buy-skill ACK and save charge exactly {charged} SP for grow type {growType}");
            await fixture.ReopenAsync();
            var relog = await fixture.ConnectAsync();
            check(relog.LoginSp == sp - charged && relog.LoginSkills.Any(s => s.SkillId == 4 && s.Level == 1),
                "buy-skill SP and learned level survive a store reload");
        }
    }

    private static bool Success(GameServerPacket packet, byte source, byte destination) =>
        packet.Payload.SequenceEqual(new byte[] { 1, source, destination });
    private static bool Error(GameServerPacket packet) => packet.Payload.Length >= 2 && packet.Payload[0] == 0;
    private static bool LayoutEquals(IEnumerable<CharacterSkillRecord> actual, IEnumerable<CharacterSkillRecord> expected) =>
        actual.OrderBy(x => x.Slot).ThenBy(x => x.SkillId).SequenceEqual(expected.OrderBy(x => x.Slot).ThenBy(x => x.SkillId));

    private sealed class Fixture : IAsyncDisposable
    {
        internal required string Root;
        internal required ServerOptions Options;
        internal JsonGameStore Store = null!;
        private ItemCatalog _items = null!;
        private ServiceProvider? _provider;
        private Connection? _connection;

        internal static async Task<Fixture> CreateAsync(
            ILogger<JsonGameStore>? logger = null, int growType = 0, int level = 60, int sp = 123)
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "DFLegacy.Emulator.slnx")))
                repository = repository.Parent;
            if (repository is null) throw new DirectoryNotFoundException("cannot locate repository for private skill-slot fixtures");
            var root = Path.Combine(repository.FullName, ".tmp", "skill-slot-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "skill", "Swordman"));
            await File.WriteAllTextAsync(Path.Combine(root, "skill", "skilllist.lst"), "0 `SwordmanSkill.lst`\n");
            await File.WriteAllTextAsync(Path.Combine(root, "skill", "SwordmanSkill.lst"),
                "1 `Swordman/A.skl`\n2 `Swordman/P.skl`\n3 `Swordman/C.skl`\n4 `Swordman/Steal.skl`\n");
            foreach (var (name, kind, group) in new[] { ("A", "active", 0), ("P", "passive", 1), ("C", "active", 4) })
                await File.WriteAllTextAsync(Path.Combine(root, "skill", "Swordman", name + ".skl"),
                    $"[type] `[{kind}]`\n[skill class] {group}\n[maximum level] 10\n[required level] 1\n");
            await File.WriteAllTextAsync(Path.Combine(root, "skill", "Swordman", "Steal.skl"),
                "[type] `[active]`\n[skill class] 0\n[maximum level] 10\n[required level] 20\n"
                + "[growtype maximum level] 0 10 5 5 5\n[skill fitness growtype]\n1\n[/skill fitness growtype]\n"
                + "[purchase cost]\n30\n[/purchase cost]\n[steel learning skill]\n1\n50\n200\n4\n");
            var fixture = new Fixture { Root = root, Options = new ServerOptions
                { DataPath = Path.Combine(root, "state.json"), ScriptPvfPath = "", SkillScriptPath = root } };
            try
            {
                fixture._items = new ItemCatalog(new ScriptFileSystem(fixture.Options, NullLogger<ScriptFileSystem>.Instance),
                    NullLogger<ItemCatalog>.Instance);
                fixture.Store = fixture.OpenStore(logger);
                await fixture.Store.InitializeAsync();
                await fixture.Store.CreateAccountAsync(new CreateAccountRequest("u09x", "password", OicqCode: "123456"));
                var created = await fixture.Store.CreateCharacterAsync("u09x", new CreateCharacterRequest("SkillProbe", Job: 0, GrowType: growType, Level: level));
                var character = created.Character ?? throw new InvalidOperationException("cannot create skill fixture character");
                var replaced = await fixture.Store.ReplaceAllCharactersAsync("u09x", [character with
                    { TutorialStarted = true, Sp = sp, Skills = Initial.ToList(), Inventory = [], Equipment = [],
                        Warehouse = [], AvatarInventory = [], CreatureInventory = [] }]);
                if (!replaced.Replaced) throw new InvalidOperationException("cannot seed skill fixture roster");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal async Task<Connection> ConnectAsync()
        {
            if (_connection is not null) throw new InvalidOperationException("fixture already connected");
            var services = new ServiceCollection();
            services.AddLogging();
            var sessionLogger = new SessionLogger();
            services.AddSingleton<ILogger<EntranceService>>(sessionLogger);
            services.AddSingleton(Options);
            services.AddSingleton(Store);
            services.AddSingleton(_items);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new AreaUserIdPool(16));
            // Same concrete dependency graph as production Program; no hosted
            // workers or external listeners are started by this provider.
            Type[] types = [typeof(ScriptFileSystem), typeof(RuntimeState), typeof(CharacterSessionRegistry),
                typeof(FriendService), typeof(MentorService), typeof(PvpMapCatalog), typeof(PvpService),
                typeof(PrivateStoreCatalog), typeof(PrivateStoreService), typeof(ItemTradeService), typeof(PartyService),
                typeof(DungeonInstanceRegistry), typeof(DungeonPartyService), typeof(TownSessionDirectory),
                typeof(SkillCatalog), typeof(QuestCatalog), typeof(CharacterExperienceCatalog), typeof(DungeonExperienceCatalog),
                typeof(PvpExperienceCatalog), typeof(CharacterSpCatalog), typeof(CharacterStatCatalog),
                typeof(CreatureExperienceCatalog), typeof(StaminaRecoveryCatalog), typeof(CompoundCatalog),
                typeof(AvatarCompoundCatalog), typeof(EquipmentReinforcementCatalog), typeof(ResealCatalog), typeof(DisjointCatalog),
                typeof(CeraShopCatalog), typeof(PremiumBenefitCatalog), typeof(DungeonCatalog), typeof(WorldMapCatalog),
                typeof(WorldDropCatalog), typeof(GoldDropCatalog), typeof(MonsterDropCatalog), typeof(MonsterChampionDropCatalog),
                typeof(DungeonDropGenerator), typeof(HellDropCatalog), typeof(ObjectDropCatalog), typeof(ObjectDropGenerator),
                typeof(ClearRewardCatalog), typeof(ClearRewardGenerator), typeof(GameplayDatagramService)];
            foreach (var type in types) services.AddSingleton(type);
            _provider = services.BuildServiceProvider();
            var catalog = _provider.GetRequiredService<SkillCatalog>();
            if (!catalog.TryGetDefinition(0, 1, out var active) || !active.IsActive
                || !catalog.TryGetDefinition(0, 3, out var common) || common.RawGroup != 4
                || !catalog.TryGetDefinition(0, 2, out var passive) || passive.IsActive)
                throw new InvalidOperationException("private skill metadata fixture did not load");
            var service = ActivatorUtilities.CreateInstance<EntranceService>(_provider);
            _connection = await Connection.CreateAsync(service, Options, sessionLogger);
            await _connection.LoginAsync();
            return _connection;
        }

        internal async Task<CharacterRecord> CharacterAsync() => (await Store.GetCharactersAsync("u09x")).Single();
        internal async Task CheckStateAsync(Action<bool, string> check, CharacterSkillRecord[] expected, string description)
        {
            var character = await CharacterAsync();
            // Reopening checks actual committed JSON rather than session/store cache.
            var disk = OpenStore();
            await disk.InitializeAsync();
            var committed = (await disk.GetCharactersAsync("u09x")).Single();
            check(character.Sp == 123 && committed.Sp == 123 && LayoutEquals(character.Skills ?? [], expected)
                && LayoutEquals(committed.Skills ?? [], expected), "skill-slot " + description);
        }
        internal async Task CloseConnectionAsync()
        {
            if (_connection is not null) { await _connection.DisposeAsync(); _connection = null; }
            if (_provider is not null) { await _provider.DisposeAsync(); _provider = null; }
        }
        internal async Task ReopenAsync()
        {
            await CloseConnectionAsync();
            Store = OpenStore();
            await Store.InitializeAsync();
        }
        private JsonGameStore OpenStore(ILogger<JsonGameStore>? logger = null) =>
            new(Options, logger ?? NullLogger<JsonGameStore>.Instance, itemCatalog: _items);
        public async ValueTask DisposeAsync()
        {
            await CloseConnectionAsync();
            // Root is a newly created GUID child of the repository's private
            // test directory, never a configured user save or deployment path.
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Connection : IAsyncDisposable
    {
        private readonly TcpClient _peer;
        private readonly EntranceService _service;
        private readonly SessionLogger _logger;
        private readonly CancellationTokenSource _stop = new();
        private readonly Channel<GameServerPacket> _packets = Channel.CreateUnbounded<GameServerPacket>();
        private readonly List<PacketFrame> _sent = [];
        private readonly Task _session;
        private readonly Task _pump;
        private uint _seed = 0x4321;
        private ushort _sequence;
        internal CharacterSkillRecord[] LoginSkills = [];
        internal ushort LoginSp;

        private Connection(TcpClient peer, TcpClient accepted, EntranceService service, int port, SessionLogger logger)
        {
            _peer = peer;
            _service = service;
            _logger = logger;
            var handler = typeof(EntranceService).GetMethod("HandleClientAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(EntranceService), "HandleClientAsync");
            _session = (Task)handler.Invoke(service, [accepted, port, _stop.Token])!;
            _pump = PumpAsync();
        }
        internal static async Task<Connection> CreateAsync(EntranceService service, ServerOptions options, SessionLogger logger)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var peer = new TcpClient { NoDelay = true };
            try
            {
                options.Channel.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                await peer.ConnectAsync(IPAddress.Loopback, options.Channel.Port);
                var accepted = await listener.AcceptTcpClientAsync();
                return new Connection(peer, accepted, service, options.Channel.Port, logger);
            }
            catch { peer.Dispose(); service.Dispose(); throw; }
            finally { listener.Stop(); }
        }

        internal byte[] Command(byte id, params byte[] arguments)
        {
            var plain = new byte[2 + arguments.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(plain, ++_sequence);
            arguments.CopyTo(plain, 2);
            var wire = plain.ToArray();
            GameClientPayloadCipher.Encrypt(wire.AsSpan(2), ref _seed);
            var frame = new PacketFrame(1, id, Crc32.Compute(plain), wire);
            _sent.Add(frame);
            return frame.Encode();
        }
        internal byte[] CorruptCommand(byte id, params byte[] arguments)
        {
            _ = Command(id, arguments);
            var valid = _sent[^1];
            _sent.RemoveAt(_sent.Count - 1);
            var decoder = new GameClientCipherState();
            foreach (var preceding in _sent)
                if (!decoder.TryDecode(preceding, out _)) throw new InvalidOperationException("fixture cipher history is invalid");
            var corrupt = valid with { DeclaredCrc32 = valid.DeclaredCrc32 ^ 1u };
            if (corrupt.DeclaredCrc32 == 0 || decoder.TryDecode(corrupt, out _))
                throw new InvalidOperationException("fixture corrupted CRC unexpectedly decodes");
            _sent.Add(corrupt);
            return corrupt.Encode();
        }
        internal async Task SendAsync(byte[] bytes) => await _peer.GetStream().WriteAsync(bytes, _stop.Token);
        internal async Task<GameServerPacket> ReadAsync(byte? type, byte id, TimeSpan? timeout = null)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
            try
            {
                while (await _packets.Reader.WaitToReadAsync(deadline.Token))
                    while (_packets.Reader.TryRead(out var packet))
                        if (packet.ProtocolId == id && (type is null || packet.Type == type)) return packet;
                throw new EndOfStreamException($"channel ended before type={type} id={id}; {_logger.Diagnostics}");
            }
            catch (Exception error) when (error is ChannelClosedException or EndOfStreamException)
            {
                // The handler disposes its stream before its outer catch logs
                // the cause, so EOF can race ahead of the diagnostic message.
                try { await _session.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { }
                throw new IOException($"channel closed before type={type} id={id}; session={_session.Status} {_session.Exception}; {_logger.Diagnostics}; reader={error}", error);
            }
            catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
            {
                throw new TimeoutException($"no reply type={type} id={id} within {timeout ?? TimeSpan.FromSeconds(5)}; {_logger.Diagnostics}");
            }
        }
        internal async Task LoginAsync()
        {
            await ReadAsync(null, 0); // Real channel greeting has a built-in 750 ms delay.
            using var credentials = new MemoryStream();
            using (var writer = new BinaryWriter(credentials, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(4); writer.Write(Encoding.ASCII.GetBytes("u09x"));
                writer.Write(8); writer.Write(Encoding.ASCII.GetBytes("password"));
            }
            await SendAsync(Command(1, credentials.ToArray()));
            var login = await ReadAsync(1, 1);
            if (login.Payload.Length == 0 || login.Payload[0] != 1) throw new InvalidOperationException("fixture channel authentication rejected");
            await SendAsync(Command(4, 0));
            var info = await ReadAsync(0, 19);
            LoginSp = BinaryPrimitives.ReadUInt16LittleEndian(info.Payload);
            var count = info.Payload[2];
            if (info.Payload.Length != 4 + count * 3) throw new InvalidDataException("unexpected NOTI19 shape");
            LoginSkills = Enumerable.Range(0, count).Select(i => new CharacterSkillRecord(
                info.Payload[3 + i * 3], info.Payload[4 + i * 3], info.Payload[5 + i * 3])).ToArray();
            // Direct CHECK_CONNECTION stays plaintext and does not advance the
            // rolling cipher. Its reply is a barrier after all selection hooks.
            var body = new byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(body, ++_sequence);
            var barrier = new PacketFrame(1, 0, Crc32.Compute(body), body);
            _sent.Add(barrier);
            await SendAsync(barrier.Encode());
            await ReadAsync(0, GameProtocolEngine.ChannelInfoNotification);
        }

        private async Task PumpAsync()
        {
            Exception? failure = null;
            try
            {
                var stream = _peer.GetStream();
                while (!_stop.IsCancellationRequested)
                {
                    var header = new byte[6];
                    await stream.ReadExactlyAsync(header, _stop.Token);
                    var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2)));
                    if (length < 6 || length > PacketFrame.DefaultMaximumLength) throw new InvalidDataException("invalid server frame length");
                    var payload = new byte[length - 6];
                    await stream.ReadExactlyAsync(payload, _stop.Token);
                    GamePayloadCipher.DecryptServerPayload(payload);
                    await _packets.Writer.WriteAsync(new(header[0], header[1], payload), _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception ex) { failure = ex; }
            finally { _packets.Writer.TryComplete(failure); }
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _peer.Dispose();
            try { await _session.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                await _pump;
                _service.Dispose();
                _stop.Dispose();
            }
        }
    }

    private sealed class SessionLogger : ILogger<EntranceService>
    {
        private readonly ConcurrentQueue<string> _warnings = new();
        internal string Diagnostics => string.Join(" | ", _warnings.TakeLast(16));
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Information)
                _warnings.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
        }
    }

    private sealed class ThrowAfterSnapshotLogger : ILogger<JsonGameStore>
    {
        internal bool Armed;
        internal int Throws;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Armed && formatter(state, exception).StartsWith("Saved emulator state", StringComparison.Ordinal))
            {
                Throws++;
                throw new IOException("controlled skill-save diagnostic failure after atomic replacement");
            }
        }
    }
}
