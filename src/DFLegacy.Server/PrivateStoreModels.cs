using System.Text.Json.Serialization;

namespace DFLegacy.Server;

public sealed record PrivateStoreSourcePart(ushort Slot, uint Quantity);
public sealed record PrivateStoreListing(ushort Slot, uint UnitPrice, ushort SourceSlot, Guid SourceInstanceId,
    CharacterItemRecord Item, uint Quantity, uint AddInfo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<PrivateStoreSourcePart>? SourceParts = null);
public sealed record PrivateStoreStock(Guid StoreId, List<PrivateStoreListing> Listings);
public sealed record PrivateStoreGoldCredit(Guid SaleId, uint Amount);
internal sealed record PrivateStoreMutationResult(DungeonRewardLedgerStatus Status, byte Error = 0, CharacterRecord? Character = null);
internal sealed record PrivateStorePurchaseResult(DungeonRewardLedgerStatus Status, byte Error = 0,
    CharacterRecord? Buyer = null, CharacterRecord? Seller = null, CharacterItemRecord? Item = null,
    ushort DestinationSlot = 0, uint Gross = 0, uint Fee = 0, uint SoldQuantity = 0, uint ProjectedSellerGold = 0, uint Cash = 0);
