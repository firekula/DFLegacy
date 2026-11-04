using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class NewCharacterDefaultsSmokeTests
{
    // Catches inherited currency defaults, UID-based gifts, lost persisted possessions,
    // and accidental clearing of the shared wallet when another character is created.
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await AccountKindSmokeTests.RunAsync(check);
        var checks = 0;
        var recordCheck = check;
        check = (passed, message) => { checks++; recordCheck(passed, message); };
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-new-character-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new ServerOptions { DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false,
            ScriptPvfPath = "", SkillScriptPath = root };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        JsonGameStore Open() => new(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
        var store = Open();
        await store.InitializeAsync();
        var normal = await store.CreateAccountAsync(new("ordinary", "password", OicqCode: "123456"));
        var test = await store.CreateAccountAsync(new("testing", "password", TestAccount: true, OicqCode: "123456"));
        check(normal.Account!.Cash == 0, "a newly registered ordinary account starts with zero shared Cash");
        check(test.Account!.Cash == 50000, "an explicit test account keeps its funded shared wallet");
        var byName = (await store.CreateCharacterAsync("ordinary", new("ByName"))).Character!;
        var byUid = (await store.CreateCharacterAsync(normal.Account.AccountUid, new("ByUid"))).Character!;
        var manual = (await store.CreateCharacterAsync("testing", new("ManualTest"))).Character!;
        Empty(check, byName, "name creation");
        Empty(check, byUid, "UID creation");
        Empty(check, manual, "manual test-account creation");
        var preset = (await store.GetCharactersAsync("testing")).Single(c => c.IsPreset);
        check(preset.Gold == 50000 && preset.Equipment!.Count > 0 && preset.AvatarInventory!.Count > 0
            && preset.CreatureInventory!.Count > 0, "explicit test preset retains its gold and stocked item spaces");

        // Fund and stock a real existing character through the store's administrative
        // boundaries before creating another character on the same shared wallet.
        await store.SaveCharacterItemSpacesAsync("ordinary", byName.Id, 12345,
            [new(9, 8, 7)], [new(10, 26043, 1)], [new(0, 8, 3)],
            avatarInventory: [new(0, 1000, 1)], creatureInventory: [new(0, 2000, 1)]);
        await store.GrantCeraToAccountAsync("ordinary", 23400);
        var funded = (await store.GetCharactersAsync("ordinary")).Single(c => c.Id == byName.Id);
        var third = (await store.CreateCharacterAsync(normal.Account.AccountUid, new("AfterFunding"))).Character!;
        Empty(check, third, "creation on a funded account");
        check(third.Cash == 23400 && (await store.GetAccountAsync("ordinary"))!.Cash == 23400,
            "creating another character preserves and mirrors existing shared Cash");
        var reloaded = Open();
        await reloaded.InitializeAsync();
        foreach (var c in (await reloaded.GetCharactersAsync("ordinary")).Where(c => c.Id != byName.Id))
            Empty(check, c, "persisted ordinary character " + c.Name);
        Empty(check, (await reloaded.GetCharactersAsync("testing")).Single(c => !c.IsPreset), "persisted manual test character");
        var preserved = (await reloaded.GetCharactersAsync("ordinary")).Single(c => c.Id == byName.Id);
        check(preserved.Gold == 12345 && preserved.Cash == 23400
            && preserved.Inventory!.SequenceEqual(funded.Inventory!) && preserved.Equipment!.SequenceEqual(funded.Equipment!)
            && preserved.Warehouse!.SequenceEqual(funded.Warehouse!) && preserved.AvatarInventory!.SequenceEqual(funded.AvatarInventory!)
            && preserved.CreatureInventory!.SequenceEqual(funded.CreatureInventory!)
            && preserved.Inventory is [{ ItemId: 8, CountOrValue: 7 }]
            && preserved.Equipment is [{ ItemId: 26043, CountOrValue: 1 }]
            && preserved.Warehouse is [{ ItemId: 8, CountOrValue: 3 }]
            && preserved.AvatarInventory is [{ ItemId: 1000, CountOrValue: 1 }]
            && preserved.CreatureInventory is [{ ItemId: 2000, CountOrValue: 1 }],
            "reload preserves all existing funded character possessions and the shared wallet");
        store = reloaded;
        var shanghaiOffset = TimeSpan.FromHours(8);
        var resetBoundary = new DateTimeOffset(2026, 11, 4, 6, 0, 0, shanghaiOffset);
        check(JsonGameStore.GetFatigueDayKey(resetBoundary.AddSeconds(-1).ToUniversalTime()) == 20261103
            && JsonGameStore.GetFatigueDayKey(resetBoundary.ToUniversalTime()) == 20261104,
            "a UTC host observes the 06:00 Beijing fatigue boundary");
        await store.SetCharacterUsedFatigueAsync(byUid.Id, 40);
        await Select("ordinary", byUid.Slot, exerciseFatigueReset: true);
        Empty(check, (await store.GetCharactersAsync("ordinary")).Single(c => c.Id == byUid.Id), "first ordinary selection");
        await Select("testing", manual.Slot);
        Empty(check, (await store.GetCharactersAsync("testing")).Single(c => c.Id == manual.Id), "first manual test selection");
        await Select("testing", preset.Slot, expectEmpty: false);
        var selectedPreset = (await store.GetCharactersAsync("testing")).Single(c => c.IsPreset);
        check(selectedPreset.Inventory!.Any(i => i.ItemId == 8 && i.CountOrValue == 100)
            && selectedPreset.Inventory!.Any(i => i.ItemId == 3037 && i.CountOrValue == 1000),
            "explicit preset still receives its intentional first-selection stock");
        // Older ordinary records can still have a null Inventory. They must not
        // regain gifts at login, including when their account is in the test UID range.
        var state = JsonNode.Parse(await File.ReadAllTextAsync(options.DataPath))!;
        foreach (var account in state["Accounts"]!.AsArray())
            foreach (var c in account!["Characters"]!.AsArray())
                if (c!["Name"]!.GetValue<string>() == "ByUid")
                    c["Inventory"] = new JsonArray();
                else if (c["Name"]!.GetValue<string>() == "ManualTest")
                    c["Inventory"] = null;
                else if (c["Name"]!.GetValue<string>() == "AfterFunding")
                    c.AsObject().Remove("Gold");
        await File.WriteAllTextAsync(options.DataPath, state.ToJsonString());
        store = Open();
        await store.InitializeAsync();
        await Select("ordinary", byUid.Slot, expectedRevivalCoins: 0);
        await Select("testing", manual.Slot, expectedRevivalCoins: 0);
        Empty(check, (await store.GetCharactersAsync("ordinary")).Single(c => c.Id == byUid.Id), "legacy null ordinary selection", allowNullInventory: true);
        Empty(check, (await store.GetCharactersAsync("testing")).Single(c => c.Id == manual.Id), "legacy null manual test selection", allowNullInventory: true);
        var afterSelection = Open();
        await afterSelection.InitializeAsync();
        Empty(check, (await afterSelection.GetCharactersAsync("ordinary")).Single(c => c.Id == byUid.Id), "ordinary selection reload", allowNullInventory: true);
        Empty(check, (await afterSelection.GetCharactersAsync("testing")).Single(c => c.Id == manual.Id), "manual test selection reload", allowNullInventory: true);
        check((await afterSelection.GetCharactersAsync("ordinary")).Single(c => c.Id == third.Id).Gold == 50000,
            "legacy missing-Gold records retain their historical deserialization default");
        check((await afterSelection.GetAccountAsync("ordinary"))!.Cash == 23400,
            "selection and reload preserve existing account Cash");
        Console.WriteLine($"New-character defaults checks: {checks}");
        Console.WriteLine($"New-character defaults artifacts: {root}");

        async Task Select(string userName, byte slot, bool expectEmpty = true, uint expectedRevivalCoins = 10, bool exerciseFatigueReset = false)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(options);
            services.AddSingleton(store);
            services.AddSingleton(items);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new AreaUserIdPool(16));
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
            await using var provider = services.BuildServiceProvider();
            var service = ActivatorUtilities.CreateInstance<EntranceService>(provider);
            await using var connection = await Connection.CreateAsync(service, options);
            await connection.LoginAsync(userName, slot);
            if (exerciseFatigueReset)
            {
                var now = DateTimeOffset.UtcNow.ToOffset(shanghaiOffset);
                var tomorrow = new DateTimeOffset(now.Date.AddDays(1).AddHours(6), shanghaiOffset);
                var reset = await store.ResetFatigueForCurrentDayAsync(tomorrow);
                check(reset.CharacterIds.Contains(byUid.Id), "fatigue reset persists the new day before notifying online players");
                var sessions = provider.GetRequiredService<CharacterSessionRegistry>();
                check(sessions.NotifyFatigueReset(byUid.Id), "the online fatigue reset reaches the selected character session");
                try
                {
                    var packet = await connection.ReadAsync(0, GameProtocolEngine.FatigueNotification, TimeSpan.FromSeconds(2));
                    check(BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload) == 0,
                        "an idle online player receives the refreshed fatigue bar without sending a packet");
                }
                catch (TimeoutException)
                {
                    check(false, "an idle online player receives the refreshed fatigue bar without sending a packet");
                }
                await store.SetCharacterUsedFatigueAsync(byUid.Id, 3);
                var sameDay = await store.ResetFatigueForCurrentDayAsync(tomorrow.AddMinutes(1));
                var clockBack = await store.ResetFatigueForCurrentDayAsync(tomorrow.AddSeconds(-1));
                check(!sameDay.NewFatigueDay && !clockBack.NewFatigueDay
                    && (await store.GetCharacterAsync(userName, byUid.Id))?.UsedFatigue == 3,
                    "rechecking or rolling back the clock does not refresh fatigue twice");
                check(sessions.NotifyFatigueReset(byUid.Id), "delayed fatigue notification still reaches the session");
                var refreshed = await connection.ReadAsync(0, GameProtocolEngine.FatigueNotification);
                check(BinaryPrimitives.ReadUInt16LittleEndian(refreshed.Payload) == 3,
                    "a delayed refresh uses current persisted fatigue instead of clearing new usage");
            }
            if (expectEmpty)
            {
                var expectedCount = expectedRevivalCoins == 0 ? 2 : 3;
                check(connection.MainList!.Payload.Length == 3 + expectedCount * 12
                    && BinaryPrimitives.ReadUInt16LittleEndian(connection.MainList.Payload.AsSpan(1)) == expectedCount
                    && BinaryPrimitives.ReadUInt32LittleEndian(connection.MainList.Payload.AsSpan(7)) == 0
                    && (expectedCount == 2 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(connection.MainList.Payload.AsSpan(27)) == 1
                        && BinaryPrimitives.ReadUInt16LittleEndian(connection.MainList.Payload.AsSpan(29)) == 1
                        && BinaryPrimitives.ReadUInt32LittleEndian(connection.MainList.Payload.AsSpan(31)) == expectedRevivalCoins),
                    $"{userName} slot {slot} login sends zero gold and the stored revival coins without refilling legacy inventories");
            }
        }
    }

    private static void Empty(Action<bool, string> check, CharacterRecord c, string boundary, bool allowNullInventory = false) =>
        check(c.Gold == 0 && (allowNullInventory
            ? c.Inventory is null or { Count: 0 }
            : c.Inventory is [{ Slot: 1, ItemId: 1, CountOrValue: 10 }]) && c.Equipment is { Count: 0 }
            && c.Warehouse is { Count: 0 } && c.AvatarInventory is { Count: 0 } && c.CreatureInventory is { Count: 0 },
            boundary + " has zero gold, " + (allowNullInventory ? "no refill" : "ten revival coins")
                + " and no other main, equipment/title, warehouse, avatar or creature gifts");

    private sealed class Connection : IAsyncDisposable
    {
        private readonly TcpClient _peer;
        private readonly EntranceService _service;
        private readonly CancellationTokenSource _stop = new();
        private readonly Channel<GameServerPacket> _packets = Channel.CreateUnbounded<GameServerPacket>();
        private readonly Task _session;
        private readonly Task _pump;
        internal GameServerPacket? MainList;
        private uint _seed = 0x4321;
        private ushort _sequence;

        private Connection(TcpClient peer, TcpClient accepted, EntranceService service, int port)
        {
            _peer = peer;
            _service = service;
            var handler = typeof(EntranceService).GetMethod("HandleClientAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(EntranceService), "HandleClientAsync");
            _session = (Task)handler.Invoke(service, [accepted, port, _stop.Token])!;
            _pump = PumpAsync();
        }
        internal static async Task<Connection> CreateAsync(EntranceService service, ServerOptions options)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var peer = new TcpClient { NoDelay = true };
            try
            {
                options.Channel.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                await peer.ConnectAsync(IPAddress.Loopback, options.Channel.Port);
                var accepted = await listener.AcceptTcpClientAsync();
                return new Connection(peer, accepted, service, options.Channel.Port);
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
            return frame.Encode();
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
                throw new EndOfStreamException($"channel ended before type={type} id={id}");
            }
            catch (Exception error) when (error is ChannelClosedException or EndOfStreamException)
            {
                try { await _session.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { }
                throw new IOException($"channel closed before type={type} id={id}; session={_session.Status} {_session.Exception}; reader={error}", error);
            }
            catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
            {
                throw new TimeoutException($"no reply type={type} id={id} within {timeout ?? TimeSpan.FromSeconds(5)}");
            }
        }
        internal async Task LoginAsync(string userName, byte slot)
        {
            await ReadAsync(null, 0); // Real channel greeting has a built-in 750 ms delay.
            using var credentials = new MemoryStream();
            using (var writer = new BinaryWriter(credentials, Encoding.ASCII, leaveOpen: true))
            {
                writer.Write(userName.Length); writer.Write(Encoding.ASCII.GetBytes(userName));
                writer.Write(8); writer.Write(Encoding.ASCII.GetBytes("password"));
            }
            await SendAsync(Command(1, credentials.ToArray()));
            var login = await ReadAsync(1, 1);
            if (login.Payload.Length == 0 || login.Payload[0] != 1) throw new InvalidOperationException("fixture channel authentication rejected");
            await SendAsync(Command(4, slot));
            do { MainList = await ReadAsync(0, 13); } while (MainList.Payload[0] != 0);
            await ReadAsync(0, 19);
            // Direct CHECK_CONNECTION stays plaintext and does not advance the
            // rolling cipher. Its reply is a barrier after all selection hooks.
            var body = new byte[10];
            BinaryPrimitives.WriteUInt16LittleEndian(body, ++_sequence);
            var barrier = new PacketFrame(1, 0, Crc32.Compute(body), body);
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
}
