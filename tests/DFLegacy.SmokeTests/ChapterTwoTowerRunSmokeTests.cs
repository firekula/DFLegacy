using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoTowerRunSmokeTests
{
    private const ushort TowerId = 11001;
    private const ushort InvitationId = 4183;
    private const byte TowerCreateNotification = 154;
    private const byte TowerStartMapNotification = 155;
    private const byte Town = 3, Area = 7;
    private const short X = 29, Y = 276;

    internal static async Task RunAsync(Action<bool, string> check, string pvfPath)
    {
        using var fixture = new TemporaryDirectory();
        var options = new ServerOptions
        {
            ScriptPvfPath = Path.GetFullPath(pvfPath),
            DataPath = Path.Combine(fixture.Path, "state.json"),
            SeedTestAccount = false,
            SkipUnsupportedChapterTwoCashContainers = true,
            ChapterTwoRemote = true,
            EnablePacketTracing = true,
            RejectBadCrc32 = true
        };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
        await store.InitializeAsync();
        const string user = "towerwire";
        const string password = "password";
        await store.CreateAccountAsync(new(user, password, OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync(user, new("TowerWire", Level: 50))).Character!;
        await store.CompleteCharacterTutorialAsync(user, character.Id);
        await store.SaveCharacterItemSpacesAsync(user, character.Id, 0, [new(73, InvitationId, 2)], [], []);

        await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
        var worldMaps = services.GetRequiredService<WorldMapCatalog>();
        var dungeons = services.GetRequiredService<DungeonCatalog>();
        check(scripts.IsPvf && scripts.FileExists("map/illusiontower/025f.map")
            && worldMaps.TryGetForGate(Town, Area, out var towerGate)
            && towerGate.Id == 100 && towerGate.DungeonIds.SequenceEqual(new ushort[] { TowerId }),
            "the actual C2 PVF binds West Coast area 7 to world map 100 and tower 11001");
        check(items.TryGetDefinition(InvitationId, out _), "the isolated character has a real PVF tower invitation");
        check(!dungeons.TryGetDefinition(TowerId, out _) && !dungeons.TryCreateLayout(TowerId, out _),
            "ordinary catalog does not admit 11001 as an ordinary maze");
        ChapterTwoTowerPartySmokeTests.Run(check, dungeons, items);

        var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
        await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options);
        await connection.ReadAsync(0, GameProtocolEngine.CheckConnectionNotification);
        await connection.EnableChapterTwoAsync();
        using var credentials = new MemoryStream();
        using (var writer = new BinaryWriter(credentials, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(user.Length);
            writer.Write(Encoding.ASCII.GetBytes(user));
            writer.Write(password.Length);
            writer.Write(Encoding.ASCII.GetBytes(password));
        }
        ushort sequence = 1;
        await Send(GameProtocolEngine.LoginCommand, credentials.ToArray());
        var login = await connection.ReadAsync(1, GameProtocolEngine.LoginCommand);
        check(login.Payload is [1, ..], "C2 CHECK_CONNECTION and encrypted login succeed with CRC enforcement");
        await Send(GameProtocolEngine.SelectCharacterCommand, [character.Slot]);
        var select = await connection.ReadAsync(1, GameProtocolEngine.SelectCharacterCommand);
        check(select.Payload is [1, ..], "encrypted C2 selection loads the real fixture character");
        await connection.ReadAsync(0, GameProtocolEngine.UserInfoNotification);

        var location = new byte[6] { Town, Area, 0, 0, 0, 0 };
        BinaryPrimitives.WriteInt16LittleEndian(location.AsSpan(2), X);
        BinaryPrimitives.WriteInt16LittleEndian(location.AsSpan(4), Y);
        await Send(GameProtocolEngine.SetUserAreaCommand, location);
        var arrival = await ReadTowerArea();
        var uid = BinaryPrimitives.ReadUInt16LittleEndian(arrival.Payload);
        await ReadTowerRoster();
        check(IsTowerArea(arrival), "encrypted CMD38 places the fixture at the actual 3/7 tower gate");
        await EnterSelector();
        await Send(GameProtocolEngine.BackToVillageCommand);
        await VerifyReturn("CMD142 before confirmation", TimeSpan.FromSeconds(5));

        await EnterSelector();
        var started = Stopwatch.StartNew();
        await Send(GameProtocolEngine.SelectDungeonCommand, [0xf9, 0x2a, 1, 0]);
        var observed = new List<GameServerPacket>();
        await VerifyReturn("Immediate recovery from unsupported CMD16", TimeSpan.FromSeconds(35), observed.Add);
        check(started.Elapsed < TimeSpan.FromSeconds(5), "unsupported tower request immediately restores controls and returns to the gate");
        check(!observed.Any(packet => packet is { Type: 1, ProtocolId: GameProtocolEngine.SelectDungeonCommand }
            or
            {
                Type: 0,
                ProtocolId: GameProtocolEngine.DungeonInfoNotification or GameProtocolEngine.StartMapNotification
                    or TowerCreateNotification or TowerStartMapNotification
            }),
            "an invalid difficulty sends neither normal nor tower map packets");
        var durable = await store.GetCharacterAsync(user, character.Id);
        check(durable is { TownId: Town, AreaId: Area, UsedFatigue: 0 }
            && durable.Inventory?.SingleOrDefault(item => item.ItemId == InvitationId)?.CountOrValue == 2,
            "selection cancellation and rejected entries consume neither an invitation nor fatigue");

        await EnterSelector();
        var enterSequence = await Send(GameProtocolEngine.SelectDungeonCommand, [0xf9, 0x2a, 0, 0]);
        var create = await connection.ReadAsync(0, TowerCreateNotification);
        check(create.Payload.SequenceEqual(new byte[] { 0xf9, 0x2a, 0, 0, 25, 0 }),
            "CMD16 enters native tower mode with dungeon 11001 and 25 floors");
        var wire = services.GetRequiredService<RuntimeState>().RecentPackets();
        var expectedPrefix = Convert.ToHexString([(byte)enterSequence, (byte)(enterSequence >> 8), 0xf9, 0x2a]);
        check(wire.Any(packet => packet.Direction == "RX" && packet.ProtocolId == GameProtocolEngine.SelectDungeonCommand
            && packet.HasValidCrc32 && packet.BodyHex.StartsWith(expectedPrefix, StringComparison.Ordinal)),
            "the actual AES packet reached the server as a valid 11001 request");
        var seenActorIds = new HashSet<ushort>();
        for (ushort floor = 1; floor <= 25; floor++)
        {
            var map = await connection.ReadAsync(0, TowerStartMapNotification);
            var count = map.Payload[8];
            check(BinaryPrimitives.ReadUInt16LittleEndian(map.Payload) == floor
                && BinaryPrimitives.ReadUInt16LittleEndian(map.Payload.AsSpan(6)) == 31000 + floor
                && count > 0 && map.Payload.Length == 10 + 9 * count, $"floor {floor:D2} has its real map and actor rows");
            var actorIds = Enumerable.Range(0, count)
                .Select(index => BinaryPrimitives.ReadUInt16LittleEndian(map.Payload.AsSpan(10 + index * 9))).ToArray();
            check(actorIds.All(seenActorIds.Add), $"floor {floor:D2} cannot reuse prior actor IDs");

            await Send(GameProtocolEngine.FinishLoadingCommand, new byte[8]);
            if (floor == 1)
            {
                var loadingReply = await connection.ReadAsync(1, GameProtocolEngine.FinishLoadingCommand);
                check(loadingReply.Payload is [1], "the first floor receives the standard loading reply");
            }
            await connection.ReadAsync(0, GameProtocolEngine.FinishLoadingNotification);

            if (floor == 1)
            {
                var earlyKill = new byte[21];
                BinaryPrimitives.WriteUInt16LittleEndian(earlyKill, actorIds[0]);
                await Send(GameProtocolEngine.DieMonsterCommand, earlyKill);
                check((await connection.ReadAsync(1, GameProtocolEngine.DieMonsterCommand)).Payload is [0, 0x13],
                    "a loaded floor still requires the native start-dialogue stage event");
            }
            await Send(GameProtocolEngine.ChapterTwoTowerStageCommand, [1]);
            if (floor == 1)
            {
                await Send(GameProtocolEngine.ChapterTwoTowerStageCommand, [2]);
                await Send(GameProtocolEngine.MoveMapCommand, [0, 0]);
                var locked = await connection.ReadAsync(1, GameProtocolEngine.MoveMapCommand);
                check(locked.Payload is [0, 0x13], "the tower blocks movement before the floor is cleared");
            }

            foreach (var uidToKill in actorIds)
            {
                var arguments = new byte[21];
                BinaryPrimitives.WriteUInt16LittleEndian(arguments, uidToKill);
                BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(2), uid);
                if (floor == 2 && uidToKill == actorIds[0])
                {
                    var replay = arguments.ToArray();
                    BinaryPrimitives.WriteUInt16LittleEndian(replay, 1);
                    await Send(GameProtocolEngine.DieMonsterCommand, replay);
                }
                await Send(GameProtocolEngine.DieMonsterCommand, arguments);
                var death = await connection.ReadAsync(0, GameProtocolEngine.MonsterDieNotification);
                check(death.Payload.Length == 6 && BinaryPrimitives.ReadUInt16LittleEndian(death.Payload) == uidToKill,
                    $"floor {floor:D2} acknowledges actual actor {uidToKill} once without ordinary-map drops");
            }

            if (floor < 25)
            {
                if (floor == 1)
                {
                    await Send(GameProtocolEngine.MoveMapCommand, [0, 0]);
                    check((await connection.ReadAsync(1, GameProtocolEngine.MoveMapCommand)).Payload is [0, 0x13],
                        "premature end dialogue cannot unlock the ladder before the valid end dialogue");
                }
                await Send(GameProtocolEngine.ChapterTwoTowerStageCommand, [2]);
                await Send(GameProtocolEngine.MoveMapCommand, [0, 0]);
            }
        }

        var result = await connection.ReadAsync(0, GameProtocolEngine.ChapterTwoTowerResultNotification);
        var reward = await connection.ReadAsync(0, GameProtocolEngine.ChapterTwoTowerRewardNotification);
        var optionsPacket = await connection.ReadAsync(0, GameProtocolEngine.ChapterTwoTowerSettlementOptionsNotification);
        check(reward.Payload[4] == 4, "an ordinary Chapter Two session receives four 25F cards");
        check(result.Payload.Length == 227 && BinaryPrimitives.ReadUInt32LittleEndian(result.Payload.AsSpan(4)) == 25
            && optionsPacket.Payload is [1], "final hostile death emits the native personal best and remaining-ticket options");
        var settledCharacter = await store.GetCharacterAsync(user, character.Id);
        var rewardExperience = BinaryPrimitives.ReadUInt32LittleEndian(reward.Payload);
        var rewardCardCount = reward.Payload[4];
        var rewards = Enumerable.Range(0, rewardCardCount).Select(index =>
            new GameChapterTwoTowerRewardItem(
                BinaryPrimitives.ReadUInt16LittleEndian(reward.Payload.AsSpan(5 + index * 6)),
                BinaryPrimitives.ReadUInt32LittleEndian(reward.Payload.AsSpan(7 + index * 6)))).ToArray();
        check(settledCharacter is not null && rewardExperience > 0
            && settledCharacter.Experience == character.Experience + rewardExperience
            && settledCharacter.Gold == rewards.Where(card => card.ItemId == 0).Sum(card => (long)card.QuantityOrValue)
            && rewards.Where(card => card.ItemId is > 0 and < ushort.MaxValue).GroupBy(card => card.ItemId).All(group =>
                settledCharacter.Inventory?.Where(item => item.ItemId == group.Key).Sum(item => item.CountOrValue) == group.Count()),
            "ordinary tower XP and drawn PVF rewards are already persisted when native settlement arrives");
        await Send(GameProtocolEngine.EplpCommand, [2, 2]);
        check((await connection.ReadAsync(1, GameProtocolEngine.EplpCommand)).Payload is [1, 2, 2],
            "settlement hovering does not leave the tower");
        await Send(GameProtocolEngine.EplpCommand, [1, 0]);
        check((await connection.ReadAsync(1, GameProtocolEngine.EplpCommand)).Payload is [1, 1, 0],
            "settlement acknowledges a request to challenge the tower again");
        durable = await store.GetCharacterAsync(user, character.Id);
        check(durable is { TownId: Town, AreaId: Area, UsedFatigue: 0 }
            && durable.Inventory?.SingleOrDefault(item => item.ItemId == InvitationId)?.CountOrValue == 1,
            "one completed run durably consumes exactly one invitation and no fatigue");
        await EnterSelector(expectDirectReselect: true);
        await Send(GameProtocolEngine.SelectDungeonCommand, [0xf9, 0x2a, 0, 0]);
        await connection.ReadAsync(0, TowerCreateNotification);
        for (var floor = 1; floor <= 10; floor++)
        {
            var map = await connection.ReadAsync(0, TowerStartMapNotification);
            check(BinaryPrimitives.ReadUInt16LittleEndian(map.Payload) == floor,
                $"the second run reaches floor {floor} before the reported 10F defeat");
            await Send(GameProtocolEngine.FinishLoadingCommand, new byte[8]);
            await connection.ReadAsync(0, GameProtocolEngine.FinishLoadingNotification);
            await Send(GameProtocolEngine.ChapterTwoTowerStageCommand, [1]);
            if (floor == 10) break;
            for (var index = 0; index < map.Payload[8]; index++)
            {
                var arguments = new byte[21];
                map.Payload.AsSpan(10 + index * 9, 2).CopyTo(arguments);
                BinaryPrimitives.WriteUInt16LittleEndian(arguments.AsSpan(2), uid);
                await Send(GameProtocolEngine.DieMonsterCommand, arguments);
                await connection.ReadAsync(0, GameProtocolEngine.MonsterDieNotification);
            }
            await Send(GameProtocolEngine.ChapterTwoTowerStageCommand, [2]);
            await Send(GameProtocolEngine.MoveMapCommand, [0, 0]);
        }
        await Send(GameProtocolEngine.DieCharacterCommand);
        var deathState = await connection.ReadAsync(0, GameProtocolEngine.DieStateNotification);
        check(deathState.Payload.SequenceEqual(GameProtocolEngine.CreateDieState(uid, false).Payload),
            "a tower death acknowledges the same character as dead");
        await Send(GameProtocolEngine.DieCharacterCommand);
        var repeatedDeath = await connection.ReadAsync(1, GameProtocolEngine.DieCharacterCommand);
        check(repeatedDeath.Payload is [0, 0x12], "repeating a tower death returns the native error");
        var target = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(target, uid);
        await Send(GameProtocolEngine.UseCoinCommand, target);
        var revival = await connection.ReadAsync(1, GameProtocolEngine.UseCoinCommand);
        check(revival.Payload is [0, 0x13], "normal resurrection coins cannot be used in the tower");
        var failedResult = await connection.ReadAsync(0, GameProtocolEngine.ChapterTwoTowerResultNotification,
            TimeSpan.FromSeconds(8));
        check(failedResult.Payload.Length == 227 && failedResult.Payload[8] == 0
            && failedResult.Payload[13] == 1 && BinaryPrimitives.ReadUInt32LittleEndian(failedResult.Payload.AsSpan(14)) == 9
            && failedResult.Payload.AsSpan(18, 9).SequenceEqual("TowerWire"u8)
            && failedResult.Payload[27] == character.Job && failedResult.Payload[28] == (character.GrowType & 0x0F)
            && BinaryPrimitives.ReadUInt16LittleEndian(failedResult.Payload.AsSpan(47)) == 25
            && BinaryPrimitives.ReadUInt32LittleEndian(failedResult.Payload.AsSpan(53)) == uint.MaxValue,
            "defeat emits the existing 25F personal best with real name, job and growth type");
        var failedReward = await connection.ReadAsync(0, GameProtocolEngine.ChapterTwoTowerRewardNotification);
        var noRetry = await connection.ReadAsync(0, GameProtocolEngine.ChapterTwoTowerSettlementOptionsNotification);
        check(BinaryPrimitives.ReadUInt32LittleEndian(failedResult.Payload.AsSpan(4)) == 9
            && BinaryPrimitives.ReadUInt32LittleEndian(failedResult.Payload.AsSpan(9)) == uint.MaxValue
            && noRetry.Payload is [0], "a 10F defeat settles nine cleared floors without a rank or retry ticket");
        var failureExperience = BinaryPrimitives.ReadUInt32LittleEndian(failedReward.Payload);
        var failureGold = BinaryPrimitives.ReadUInt16LittleEndian(failedReward.Payload.AsSpan(5)) == 0
            ? BinaryPrimitives.ReadUInt32LittleEndian(failedReward.Payload.AsSpan(7))
            : 0;
        check(failureExperience == 13244 && failedReward.Payload[4] == 1,
            "a level-50 defeat at the 10F awards 13244 XP and one card in an ordinary session");
        await Send(GameProtocolEngine.EplpCommand, [1, 2]);
        await connection.ReadAsync(1, GameProtocolEngine.EplpCommand);
        await VerifyReturn("tower defeat settlement", TimeSpan.FromSeconds(5));

        await EnterSelector();
        await Send(GameProtocolEngine.SelectDungeonCommand, [0xf9, 0x2a, 0, 0]);
        var missingTicket = new List<GameServerPacket>();
        await VerifyReturn("entry without an invitation", TimeSpan.FromSeconds(5), missingTicket.Add);
        check(!missingTicket.Any(packet => packet.Type == 0
            && packet.ProtocolId is TowerCreateNotification or TowerStartMapNotification),
            "a character without an invitation remains in town without receiving a tower map");
        durable = await store.GetCharacterAsync(user, character.Id);
        check(durable is { UsedFatigue: 0 } && durable.Inventory?.Any(item => item.ItemId == InvitationId) != true,
            "the second attempt keeps its entry cost and the rejected third entry changes no resources");
        check(durable?.Experience == settledCharacter?.Experience + failureExperience
            && durable?.Gold == settledCharacter?.Gold + failureGold,
            "hovering, retrying and repeated death do not duplicate either tower reward");
        check(durable?.ChapterTwoTowerPersonalBests is [{ ClearedFloor: 25 }],
            "the personal best survives both a normal reward and return from defeat");
        var members = new[] { ChapterTwoTowerRankMember.Capture(character), new ChapterTwoTowerRankMember("Partner", 45, 2) };
        var firstParty = await store.SaveChapterTwoTowerPersonalBestAsync(user, character.Id, new(members, 20, 5000), default);
        var equal = await store.SaveChapterTwoTowerPersonalBestAsync(user, character.Id, new(members, 20, 5000), default);
        var lower = await store.SaveChapterTwoTowerPersonalBestAsync(user, character.Id, new(members, 19, 1000), default);
        var faster = await store.SaveChapterTwoTowerPersonalBestAsync(user, character.Id, new(members, 20, 4000), default);
        var higher = await store.SaveChapterTwoTowerPersonalBestAsync(user, character.Id, new(members, 21, 6000), default);
        check(!firstParty.Improved && !equal.Improved && !lower.Improved && faster.Improved && higher.Improved
            && higher.Best is { ClearedFloor: 21, TotalMilliseconds: 6000 },
            "original personal ranking keeps ties and lower floors, then accepts faster peers and higher floors");
        var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
        await reloaded.InitializeAsync();
        var ranked = await reloaded.GetCharacterAsync(user, character.Id);
        check(ranked?.ChapterTwoTowerPersonalBests is { Count: 2 } persisted
            && persisted.Single(best => best.Members.Length == 1).ClearedFloor == 25
            && persisted.Single(best => best.Members.Length == 2).Members.SequenceEqual(members),
            "a fresh store reads separate C2 records for solo and two-player entrants");
        await CheckRewardWriteFailuresAsync(fixture.Path, items, check);

        async Task<ushort> Send(byte command, byte[]? arguments = null)
        {
            var next = ++sequence;
            await connection.SendAsync(Encode(command, next, arguments ?? []));
            return next;
        }

        async Task EnterSelector(bool expectDirectReselect = false)
        {
            var packets = new List<GameServerPacket>();
            await Send(GameProtocolEngine.StartGameCommand);
            var state = await connection.ReadAsync(0, GameProtocolEngine.UserStateNotification, observe: packets.Add);
            var permissions = await connection.ReadAsync(0, GameProtocolEngine.DungeonPermissionNotification);
            check(state.Payload.AsSpan().SequenceEqual(new byte[] { 1, (byte)uid, (byte)(uid >> 8), 1 }),
                "native no-body CMD15 enters the dungeon selector for the same actor");
            if (expectDirectReselect)
            {
                check(!packets.Any(packet => packet.Type == 0 && packet.ProtocolId == GameProtocolEngine.UserAreaNotification),
                    "native restarting goes straight to selection without forcing a town transition");
            }
            var body = permissions.Payload;
            var count = body.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(body) : 0;
            check(count > 0 && body.Length == 2 + count * 3
                && Enumerable.Range(0, count).All(index =>
                    BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2 + index * 3)) != TowerId),
                "NOTI5 has ordinary permissions and omits tower 11001");
        }

        async Task VerifyReturn(string reason, TimeSpan timeout, Action<GameServerPacket>? observe = null)
        {
            var state = await connection.ReadAsync(0, GameProtocolEngine.UserStateNotification, timeout, observe);
            var area = await connection.ReadAsync(0, GameProtocolEngine.UserAreaNotification, observe: observe);
            var roster = await connection.ReadAsync(0, GameProtocolEngine.AreaUsersNotification, observe: observe);
            check(state.Payload.AsSpan().SequenceEqual(new byte[] { 1, (byte)uid, (byte)(uid >> 8), 0 })
                && IsTowerArea(area) && BinaryPrimitives.ReadUInt16LittleEndian(area.Payload) == uid
                && IsTowerRoster(roster), $"{reason} emits NOTI3/23/24 and restores 3/7 with the original position");
        }

        async Task<GameServerPacket> ReadTowerArea()
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var packet = await connection.ReadAsync(0, GameProtocolEngine.UserAreaNotification);
                if (IsTowerArea(packet)) return packet;
            }
            throw new InvalidDataException("The tower area was never acknowledged.");
        }

        async Task ReadTowerRoster()
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var packet = await connection.ReadAsync(0, GameProtocolEngine.AreaUsersNotification);
                if (IsTowerRoster(packet)) return;
            }
            throw new InvalidDataException("The tower area roster was never acknowledged.");
        }
    }

    private static async Task CheckRewardWriteFailuresAsync(
        string directory, ItemCatalog items, Action<bool, string> check)
    {
        const string user = "toweratomic";
        foreach (var afterReplace in new[] { false, true })
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(directory, $"tower-atomic-{afterReplace}.json"),
                SeedTestAccount = false
            };
            var armed = false;
            var writer = new AtomicSnapshotReplacement((source, target) =>
            {
                if (!armed || afterReplace) File.Move(source, target, true);
                if (armed) throw new IOException("tower injected snapshot failure");
            }, (_, _) => Task.CompletedTask);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, writer, itemCatalog: items);
            await store.InitializeAsync();
            await store.CreateAccountAsync(new(user, "password", OicqCode: "123456"));
            var created = (await store.CreateCharacterAsync(user, new("TowerAtomic", Level: 50))).Character!;
            var original = (await store.GetCharacterAsync(user, created.Id))!;
            var added = original.Inventory?.ToDictionary(item => item.Slot) ?? [];
            added[73] = new(73, InvitationId, 1);
            var plan = new ClearRewardGrantPlan(original.Gold + 30, added, original.WarehouseCapacity, [], [true]);
            var progression = new CharacterExperienceGrant(original.Level, original.Experience,
                original.Level, original.Experience + 40, 40);
            var attemptId = Guid.NewGuid();
            armed = true;
            var returned = await store.SaveChapterTwoTowerRewardAsync(user, attemptId, original, plan, progression,
                original.Sp + 1, CancellationToken.None);
            var unchanged = await store.GetCharacterAsync(user, original.Id);
            check(returned is null && unchanged?.Gold == original.Gold && unchanged?.Sp == original.Sp
                && unchanged?.Experience == original.Experience && unchanged?.Inventory?.Any(item => item.ItemId == InvitationId) != true,
                $"an uncertain store write publishes no new tower reward state afterReplace={afterReplace}");

            var reloaded = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await reloaded.InitializeAsync();
            var persisted = await reloaded.GetCharacterAsync(user, original.Id);
            check(persisted?.Gold == original.Gold + (afterReplace ? 30 : 0)
                && persisted?.Sp == original.Sp + (afterReplace ? 1 : 0)
                && persisted?.Experience == original.Experience + (afterReplace ? 40 : 0)
                && (persisted?.Inventory?.Any(item => item.ItemId == InvitationId) == true) == afterReplace
                && persisted?.LastChapterTwoTowerRewardAttempt == (afterReplace ? attemptId : (Guid?)null),
                $"fresh store reads one complete prior or committed tower reward snapshot afterReplace={afterReplace}");
            var retry = await reloaded.SaveChapterTwoTowerRewardAsync(user, attemptId, persisted!, plan, progression,
                original.Sp + 1, CancellationToken.None);
            check(afterReplace ? retry is null : retry?.LastChapterTwoTowerRewardAttempt == attemptId,
                $"a restarted store pays the same attempt only if the earlier snapshot did not commit afterReplace={afterReplace}");
        }
    }

    private static bool IsTowerArea(GameServerPacket packet) => packet.Payload.Length == 10
        && packet.Payload[2] == Town && packet.Payload[3] == Area
        && BinaryPrimitives.ReadInt16LittleEndian(packet.Payload.AsSpan(4)) == X
        && BinaryPrimitives.ReadInt16LittleEndian(packet.Payload.AsSpan(6)) == Y
        && packet.Payload[9] == 1;

    private static bool IsTowerRoster(GameServerPacket packet) => packet.Payload.Length >= 4
        && packet.Payload[0] == Town && packet.Payload[1] == Area;

    private static byte[] Encode(byte command, ushort sequence, ReadOnlySpan<byte> arguments)
    {
        // Native no-argument CMD15/142 are sequence-only. For nonempty bodies,
        // C2 pads and encrypts everything after the two-byte sequence number.
        var plain = new byte[2 + ((arguments.Length + 15) & ~15)];
        BinaryPrimitives.WriteUInt16LittleEndian(plain, sequence);
        arguments.CopyTo(plain.AsSpan(2));
        var wire = plain.ToArray();
        if (!arguments.IsEmpty)
        {
            using var aes = Aes.Create();
            aes.Key = Convert.FromHexString("46494A4946474546444A44404A424758");
            aes.EncryptEcb(plain.AsSpan(2), wire.AsSpan(2), PaddingMode.None);
        }
        return new PacketFrame(1, command, Crc32.Compute(plain), wire).Encode();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("dflegacy-c2-tower-");
        public string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
