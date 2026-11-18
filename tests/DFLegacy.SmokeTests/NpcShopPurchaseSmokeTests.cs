using System.Buffers.Binary;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// End-to-end NPC shop purchase over a real session: Sinda's reinforced katana
/// offer (27601) must arrive with state +10 while an ordinary purchase keeps
/// state 0. Requires a real Chapter Two PVF (run with <c>--shop-purchase</c>).
/// </summary>
internal static class NpcShopPurchaseSmokeTests
{
    private const ushort ReinforcedItem = NpcShopOffers.ReinforcedKatanaItemId;
    private const ushort PlainItem = 27600; // damaged katana, no shop offer
    private const int StartingGold = 100_000;
    private const int ReinforcedPrice = 2_100;
    private const int PlainPrice = 1_400;

    public static async Task RunAsync(Action<bool, string> check, string pvfPath)
    {
        var directory = Directory.CreateTempSubdirectory("dflegacy-shop-purchase-");
        try
        {
            var options = new ServerOptions
            {
                ScriptPvfPath = Path.GetFullPath(pvfPath),
                DataPath = Path.Combine(directory.FullName, "state.json"),
                SkipUnsupportedChapterTwoCashContainers = true,
                SeedTestAccount = false
            };
            var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
            var items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance, options);
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance, itemCatalog: items);
            await store.InitializeAsync();
            const string user = "shopwire";
            await store.CreateAccountAsync(new(user, "password", OicqCode: "123456"));
            var character = (await store.CreateCharacterAsync(user, new("ShopWire", Level: 20))).Character!;
            await store.SaveCharacterItemSpacesAsync(user, character.Id, StartingGold, [], [], []);

            await using var services = NewCharacterDefaultsSmokeTests.CreateSessionServices(options, store, items);
            var entrance = ActivatorUtilities.CreateInstance<EntranceService>(services);
            await using var connection = await NewCharacterDefaultsSmokeTests.Connection.CreateAsync(entrance, options);
            await connection.LoginAsync(user, character.Slot);

            check(await BuyAsync(ReinforcedItem) is { } reinforced
                    && reinforced.State == NpcShopOffers.ReinforcedKatanaLevel,
                "the Sinda katana purchase arrives in the bag with state +10");
            var afterReinforced = (await store.GetCharacterAsync(user, character.Id))!;
            check(afterReinforced.Inventory?.SingleOrDefault(item => item.ItemId == ReinforcedItem) is
                        { State: NpcShopOffers.ReinforcedKatanaLevel, CountOrValue: 1 },
                "the +10 state is persisted, not just projected on the wire");
            check(afterReinforced.Gold == StartingGold - ReinforcedPrice,
                $"the reinforced katana costs its template price ({afterReinforced.Gold})");

            check(await BuyAsync(PlainItem) is { } plain && plain.State == 0,
                "an item without a shop offer keeps state 0 on purchase");
            var afterPlain = (await store.GetCharacterAsync(user, character.Id))!;
            check(afterPlain.Gold == StartingGold - ReinforcedPrice - PlainPrice,
                "the ordinary purchase deducts only its own price");

            async Task<(ushort Slot, ushort ItemId, byte State)?> BuyAsync(ushort itemId)
            {
                var arguments = new byte[8];
                BinaryPrimitives.WriteInt32LittleEndian(arguments, itemId);
                BinaryPrimitives.WriteInt32LittleEndian(arguments.AsSpan(4), 1);
                await connection.SendAsync(connection.Command(GameProtocolEngine.BuyItemCommand, arguments));
                var reply = await connection.ReadAsync(1, GameProtocolEngine.BuyItemCommand);
                if (reply.Payload.Length == 0 || reply.Payload[0] != 1)
                {
                    check(false, $"shop purchase {itemId} was accepted (reply {Convert.ToHexString(reply.Payload)})");
                    return null;
                }

                var bag = await connection.ReadAsync(0, GameProtocolEngine.ItemListNotification);
                var count = BinaryPrimitives.ReadUInt16LittleEndian(bag.Payload.AsSpan(1));
                for (var index = 0; index < count; index++)
                {
                    var entry = bag.Payload.AsSpan(3 + index * 12, 12);
                    if (BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]) == itemId)
                    {
                        return (BinaryPrimitives.ReadUInt16LittleEndian(entry),
                            itemId,
                            entry[8]);
                    }
                }

                check(false, $"the purchased item {itemId} appears in the main inventory list");
                return null;
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
