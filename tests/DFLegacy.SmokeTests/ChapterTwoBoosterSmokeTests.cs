using System.Buffers.Binary;
using System.Security.Cryptography;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoBoosterSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check, string pvf, string? replyPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dflegacy-c2-booster-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var options = new ServerOptions
        {
            ScriptPvfPath = pvf,
            DataPath = Path.Combine(root, "state.json"),
            SeedTestAccount = false,
            SkipUnsupportedChapterTwoCashContainers = true,
            Channel = new() { Port = 47001 }
        };
        var catalog = new ItemCatalog(new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance),
            NullLogger<ItemCatalog>.Instance, options);
        check(catalog.TryGetDefinition(1128, out var gift) && gift is { TypeTag: "booster", CeraBooster: null, Lottery: null }
            && gift.GiftBooster is { Groups: [{ DrawCount: 1, TotalWeight: 10000 }] }
            && catalog.TryGetDefinition(38, out var cera) && cera is { GiftBooster: null, CeraBooster: not null },
            "the actual PVF maps the gift-box pool separately from Cera boosters and jars");
        var failSave = false;
        var replacement = new AtomicSnapshotReplacement((source, target) =>
        {
            if (failSave) throw new IOException("Gift-box test snapshot was not replaced.");
            File.Move(source, target, true);
        }, (_, _) => Task.CompletedTask);
        var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, replacement, itemCatalog: catalog);
        await store.InitializeAsync();
        const string account = "boxtest";
        await store.CreateAccountAsync(new(account, "password", OicqCode: "123456"));
        var character = (await store.CreateCharacterAsync(account, new("PVFBox", Level: 60))).Character!;
        await store.SaveCharacterItemSpacesAsync(account, character.Id, 100,
            [new(41, 1128, 2), new(42, 38, 1), new(43, 7268, 1)], [], []);

        var previousCipherSetting = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        try
        {
            await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, catalog);
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
            await using var client = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options, 47001);
            await client.LoginAsync(account, character.Slot);
            await client.EnableChapterTwoAsync();

            var before = await File.ReadAllBytesAsync(options.DataPath);
            var oldRoute = await Open(42, 41, GameProtocolEngine.UseStackableItemCommand);
            var wrongContainer = await Open(43, 42);
            var wrongSlot = await Open(44, 72);
            var afterWrongItems = await File.ReadAllBytesAsync(options.DataPath);
            check(oldRoute is { Payload: [0, 17, 0] } && wrongContainer is { Payload: [0, 17] }
                && wrongSlot is { Payload: [0, 17] }
                && before.SequenceEqual(afterWrongItems),
                "CMD47 town use and CMD171 on the wrong item or slot leave the snapshot untouched");

            failSave = true;
            var failed = await Open(45, 41);
            failSave = false;
            var afterFailed = await File.ReadAllBytesAsync(options.DataPath);
            check(failed is { Payload: [0, 17] } && before.SequenceEqual(afterFailed),
                "CMD171 returns a native failure for a rejected save and preserves the snapshot");

            var reply = await Open(45, 41);
            check(reply?.Payload is [1, 41, 0, 1, 0, 0, 0, 1, 0, ..] && reply.Payload.Length == 15,
                "C2 AES/CRC CMD171 opens actual gift box 1128 and returns its native item-count vector");
            if (reply?.Payload is not [1, ..] || reply.Payload.Length != 15) return;
            if (replyPath is not null) await File.WriteAllBytesAsync(replyPath, reply.Payload);

            var rewardId = BinaryPrimitives.ReadUInt16LittleEndian(reply.Payload.AsSpan(9));
            var rewardCount = BinaryPrimitives.ReadUInt32LittleEndian(reply.Payload.AsSpan(11));
            var expectedCount = rewardId switch { 1 => 3u, 36 or 15 or 26104 => 1u, 1085 => 30u, _ => 0u };
            var saved = (await store.GetCharacterAsync(account, character.Id))!;
            check(expectedCount == rewardCount && rewardCount > 0 && saved.Gold == 100
                && saved.Inventory!.Single(item => item.Slot == 41).CountOrValue == 1
                && saved.Inventory!.Single(item => item.Slot == 42) is { ItemId: 38, CountOrValue: 1 }
                && saved.Inventory!.Where(item => item.ItemId == rewardId).Sum(item => item.CountOrValue) == rewardCount,
                "gift-box 1128 consumes one box and awards exactly one row of its real PVF group");

            var committed = await File.ReadAllBytesAsync(options.DataPath);
            var retry = await Open(45, 41);
            var afterRetry = await File.ReadAllBytesAsync(options.DataPath);
            check(retry is { Payload: [0, 17] } && committed.SequenceEqual(afterRetry),
                "replaying the same encrypted gift-box command cannot consume or award twice");

            var reopened = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: catalog);
            await reopened.InitializeAsync();
            var persisted = (await reopened.GetCharacterAsync(account, character.Id))!;
            check(persisted.Inventory!.SequenceEqual(saved.Inventory!),
                "the consumed stack and its award survive loading a new store");

            var flagOne = await Open(46, 43);
            var flagOneSaved = (await store.GetCharacterAsync(account, character.Id))!;
            var rows = new (ushort Id, uint Count)[] { (7256, 1), (7257, 1), (7258, 1), (7259, 1), (7260, 3), (15, 2), (36, 2) };
            check(flagOne?.Payload is [1, 43, 0, 0, 0, 0, 0, 7, 0, ..] && flagOne.Payload.Length == 51
                && flagOneSaved.Inventory!.All(item => item.ItemId != 7268)
                && rows.All(row => flagOneSaved.Inventory!.Where(item => item.ItemId == row.Id).Sum(item => item.CountOrValue)
                    == saved.Inventory!.Where(item => item.ItemId == row.Id).Sum(item => item.CountOrValue) + row.Count)
                && flagOneSaved.Cash == saved.Cash,
                "real flag-1 gift 7268 credits all seven groups as items, including its two cera-tagged rows");

            async Task<GameServerPacket?> Open(ushort sequence, ushort slot, byte opcode = 171)
            {
                var plain = new byte[18];
                BinaryPrimitives.WriteUInt16LittleEndian(plain, sequence);
                BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(2), slot);
                var wire = plain.ToArray();
                using var aes = Aes.Create();
                aes.Key = Convert.FromHexString("46494A4946474546444A44404A424758");
                aes.EncryptEcb(plain.AsSpan(2), wire.AsSpan(2), PaddingMode.None);
                await client.SendAsync(new PacketFrame(1, opcode, Crc32.Compute(plain), wire).Encode());
                try { return await client.ReadAsync(1, opcode, TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { return null; }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", previousCipherSetting);
        }
    }
}
