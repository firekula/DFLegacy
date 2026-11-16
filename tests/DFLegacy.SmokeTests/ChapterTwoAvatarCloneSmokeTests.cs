using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ChapterTwoAvatarCloneSmokeTests
{
    private const ushort OriginalHat = 39032;
    private const ushort CloneHat = 42601;
    private const byte CloneSlot = 44;
    private const byte HatSlot = 0;
    private const string Account = "cloneavatar";

    internal static async Task RunAsync(Action<bool, string> check, string? pvfPath = null)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-c2-avatar-clone-");
        const string trialCipher = "DFL_S1C2_TRIAL_CHANNEL_CIPHER";
        var previousCipher = Environment.GetEnvironmentVariable(trialCipher);
        Environment.SetEnvironmentVariable(trialCipher, "1");
        try
        {
            if (pvfPath is null)
            {
                var equipment = root.CreateSubdirectory("equipment");
                File.WriteAllText(Path.Combine(equipment.FullName, "equipment.lst"),
                    $"{OriginalHat} `original.equ`\n{CloneHat} `clone.equ`\n");
                const string hat = "[equipment type] `[hat avatar]`\n[usable job]\n`[swordman]`\n[/usable job]\n";
                File.WriteAllText(Path.Combine(equipment.FullName, "original.equ"), hat);
                File.WriteAllText(Path.Combine(equipment.FullName, "clone.equ"), hat + "[clear avatar] 1\n");
            }

            var options = new ServerOptions
            {
                DataPath = Path.Combine(root.FullName, "state.json"),
                ScriptPvfPath = pvfPath ?? "",
                SkillScriptPath = root.FullName,
                SkipUnsupportedChapterTwoCashContainers = pvfPath is not null,
                SeedTestAccount = false,
                Channel = new() { Port = 47001 }
            };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
            async Task<JsonGameStore> OpenStoreAsync()
            {
                var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
                await store.InitializeAsync();
                return store;
            }

            var store = await OpenStoreAsync();
            await store.CreateAccountAsync(new(Account, "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync(Account, new("CloneAvatar", Level: 60))).Character!;
            await store.SaveCharacterItemSpacesAsync(Account, character.Id, 0, [], [new(HatSlot, OriginalHat, 1)], [],
                avatarInventory: [new(CloneSlot, CloneHat, 1, AvatarAbilityIndex: 4)]);

            await using (var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items))
            await using (var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(
                ActivatorUtilities.CreateInstance<EntranceService>(services), options, 47001))
            {
                await connection.LoginAsync(Account, character.Slot, chapterTwo: true);
                var body = new byte[18];
                body[0] = 1;
                body[9] = 3;
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), CloneSlot);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(3), CloneHat);
                await connection.SendAsync(connection.Command(GameProtocolEngine.MoveItemSpaceCommand, body));
                var reply = await connection.ReadAsync(1, GameProtocolEngine.MoveItemSpaceCommand);
                check(reply.Payload[0] == 1, "Chapter Two equips the clone over the worn hat");
                await CheckTownAsync(connection, OriginalHat);
                var equipped = await store.GetCharacterAsync(Account, character.Id);
                check(equipped?.Equipment?.SingleOrDefault() is { ItemId: CloneHat, AvatarAbilityIndex: 4 }
                      && equipped.AvatarInventory?.SingleOrDefault() is { Slot: CloneSlot, ItemId: OriginalHat },
                    "the clone keeps its selected ability and the replaced hat returns to its bag slot");
            }

            store = await OpenStoreAsync();
            await CheckRelogAsync(OriginalHat);
            var saved = (await store.GetCharacterAsync(Account, character.Id))!;
            await store.SaveCharacterItemSpacesAsync(Account, character.Id, 0, saved.Inventory!, saved.Equipment!, [],
                avatarInventory: []);
            await CheckRelogAsync(0);

            async Task CheckRelogAsync(ushort expectedAppearance)
            {
                await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
                await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(
                    ActivatorUtilities.CreateInstance<EntranceService>(services), options, 47001);
                var loginPackets = new List<GameServerPacket>();
                await connection.LoginAsync(Account, character.Slot, chapterTwo: true, observe: loginPackets.Add);
                var town = loginPackets.Last(packet => IsUserInfo(packet, 0));
                var detail = loginPackets.Last(packet => IsUserInfo(packet, 1));
                CheckAppearance(town, detail, expectedAppearance);

                await connection.SendAsync(connection.Command(GameProtocolEngine.GetUserInfoCommand, 0xff, 0xff, 2));
                var roster = await connection.ReadAsync(0, GameProtocolEngine.UserInfoNotification);
                check(IsUserInfo(roster, 2) && HasHat(CompactEquipment(roster), expectedAppearance),
                    $"character selection projects clone hat {CloneHat} with appearance {expectedAppearance}");
            }

            async Task CheckTownAsync(NewCharacterDefaultsSmokeTests.Connection connection, ushort expectedAppearance)
            {
                var town = await connection.ReadAsync(0, GameProtocolEngine.UserInfoNotification);
                var detail = await connection.ReadAsync(0, GameProtocolEngine.UserInfoNotification);
                CheckAppearance(town, detail, expectedAppearance);
            }

            void CheckAppearance(GameServerPacket town, GameServerPacket detail, ushort expectedAppearance)
            {
                check(IsUserInfo(town, 0) && HasHat(CompactEquipment(town), expectedAppearance),
                    $"town appearance projects clone hat {CloneHat} with appearance {expectedAppearance}");
                var statLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(detail.Payload.AsSpan(9));
                var row = detail.Payload.AsSpan(14 + statLength);
                var detailAppearance = expectedAppearance == 0 ? ushort.MaxValue : expectedAppearance;
                check(IsUserInfo(detail, 1) && row[0] == HatSlot
                      && BinaryPrimitives.ReadUInt16LittleEndian(row[1..]) == CloneHat
                      && BinaryPrimitives.ReadUInt16LittleEndian(row[8..]) == 4
                      && BinaryPrimitives.ReadUInt16LittleEndian(row[10..]) == detailAppearance,
                    $"worn equipment projects the clone's selected ability and appearance {expectedAppearance}");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(trialCipher, previousCipher);
            root.Delete(recursive: true);
        }
    }

    private static bool IsUserInfo(GameServerPacket packet, byte mode) =>
        packet.Type == 0 && packet.ProtocolId == GameProtocolEngine.UserInfoNotification && packet.Payload[0] == mode;

    private static ReadOnlySpan<byte> CompactEquipment(GameServerPacket packet)
    {
        var nameLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(packet.Payload.AsSpan(5));
        return packet.Payload.AsSpan(10 + nameLength + (packet.Payload[0] == 0 ? 6 : 5));
    }

    private static bool HasHat(ReadOnlySpan<byte> row, ushort appearance) =>
        row[0] == HatSlot && BinaryPrimitives.ReadUInt16LittleEndian(row[1..]) == CloneHat
        && BinaryPrimitives.ReadUInt16LittleEndian(row[4..]) == appearance;
}
