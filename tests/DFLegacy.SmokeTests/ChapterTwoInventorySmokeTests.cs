using DFLegacy.Protocol;
using DFLegacy.Server;

internal static class ChapterTwoInventorySmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        GameInventoryEntry[] items = [new(9, 10400, 1), new(10, 27093, 1, Durability: 35)];
        CheckRecords(GameProtocolEngine.CreateMainItemList(49752, items), 3, 4, 12, [0, 0]);
        CheckRecords(GameProtocolEngine.CreateItemList(2, items, 24), 5, 2, 12, [0, 0]);
        CheckRecords(GameProtocolEngine.CreateUpdateItemList(3, [new(9, ushort.MaxValue, 0)]),
            3, 1, 12, [0, 0]);
        CheckRecords(GameProtocolEngine.CreateCurrentCharacterDetails(1, equippedItems: items),
            95, 2, 10, [255, 255, 0, 0]);
        CheckRecords(GameProtocolEngine.CreateRequestedUserInfoDetail(1, equippedItems: items),
            95, 2, 10, [255, 255, 0, 0]);
        var peers = new GameServerPacket(GameProtocolEngine.NotificationPacketType,
            GameProtocolEngine.UdpPeerInfoNotification,
            Convert.FromHexString("0201007F0000017F00000113C70A000000010003000002007F0000017F000001E03C0B0000000100030000"));
        CheckRecords(peers, 1, 2, 21, [0]);

        var summary = new GameCharacterSummary(0, "Test"u8.ToArray(), 0, 0, 60);
        var actor = GameProtocolEngine.CreateCurrentCharacterInfo(summary, 1, items,
            includeChapter2ActorFields: true).Payload;
        var firstItem = 5 + sizeof(uint) + summary.NameBytes.Length + 7;
        check(actor.AsSpan(firstItem, 16).SequenceEqual(
                new byte[] { 9, 0xa0, 0x28, 0, 0, 0, 0, 0, 10, 0xd5, 0x69, 0, 0, 0, 0, 0 }),
            "Chapter Two compact actor appearance uses eight-byte records");
        var card = GameProtocolEngine.CreateUserInfo(
            [summary with { AppearanceItems = [items[0]] }], includeUseLockDays: true).Payload;
        check(card.AsSpan(firstItem - 1, 8).SequenceEqual(new byte[] { 9, 0xa0, 0x28, 0, 0, 0, 0, 0 })
            && card.Length == firstItem - 1 + 8 + 13 && card.AsSpan(card.Length - 4).IndexOfAnyExcept((byte)0) < 0,
            "Chapter Two character selection keeps UseLock days aligned when a weapon is equipped");

        var otherPacket = GameProtocolEngine.CreateUserInfo([]);
        check(ReferenceEquals(otherPacket, ChapterTwoPacketProjection.Apply(otherPacket)),
            "character selection keeps its own packet format");

        var legacyExperience = GameProtocolEngine.CreateExperienceGained(3, 160, 0, 2, 15);
        var chapterTwoExperience = GameProtocolEngine.CreateExperienceGained(3, 160, 0, 2, 15,
            chapterTwoVictoryPoints: 0x78563412);
        var zeroVictoryPoints = GameProtocolEngine.CreateExperienceGained(3, 160, 0, 2, 15,
            chapterTwoVictoryPoints: 0);
        check(legacyExperience.Payload.AsSpan().SequenceEqual(Convert.FromHexString("03A000000000000000020000000F00")),
            "ACT1 keeps the original monster experience body");
        check(chapterTwoExperience.Payload.AsSpan().SequenceEqual(Convert.FromHexString("03A000000000000000020000000F0012345678"))
            && zeroVictoryPoints.Payload.AsSpan().SequenceEqual(Convert.FromHexString("03A000000000000000020000000F0000000000")),
            "Chapter Two monster experience carries the full victory point balance including zero");

        var purchase = Convert.FromHexString("09000001FF0000DF86010000000000000000");
        check(GameProtocolEngine.TryReadCeraShopPurchase(purchase, out var parsed, true)
            && parsed.Products.Single().CommodityNo == 100063,
            "Chapter Two accepts the captured zero-padded warehouse purchase");
        check(!GameProtocolEngine.TryReadCeraShopPurchase(purchase, out _)
            && GameProtocolEngine.TryReadCeraShopPurchase(purchase[..11], out _),
            "ACT1 purchase length remains exact");
        purchase[^1] = 1;
        check(!GameProtocolEngine.TryReadCeraShopPurchase(purchase, out _, true),
            "Chapter Two rejects nonzero purchase padding");
        purchase[^1] = 0;
        check(!GameProtocolEngine.TryReadCeraShopPurchase([.. purchase, .. new byte[16]], out _, true),
            "Chapter Two rejects an unnecessary purchase padding block");

        var pickup = Convert.FromHexString("3700010000023B029500F95D774700000000");
        check(GameProtocolEngine.TryParseGetItemCommand(pickup, out var item, chapterTwo: true)
            && item is { GroundId: 1, RouteRequested: false, ClientRarity: 2 },
            "Chapter Two parses the captured CRC-verified pickup without interpreting its client check data");
        check(!GameProtocolEngine.TryParseGetItemCommand(pickup, out _)
            && !GameProtocolEngine.TryParseGetItemCommand(pickup[..8], out _, chapterTwo: true),
            "pickup dialects reject each other's wire length");
        pickup[^1] = 1;
        check(!GameProtocolEngine.TryParseGetItemCommand(pickup, out _, chapterTwo: true),
            "Chapter Two rejects an unverified nonzero pickup tail");

        var drop = Convert.FromHexString("0900FA00FA00000900010000000000000000");
        check(GameProtocolEngine.TryParseDropItemCommand(drop, out var dropped, chapterTwo: true)
            && dropped is { X: 250, Y: 250, ItemSpace: 0, Slot: 9, Count: 1 },
            "Chapter Two accepts an ordinary item drop with its additional byte and AES padding");
        check(!GameProtocolEngine.TryParseDropItemCommand(drop, out _)
            && GameProtocolEngine.TryParseDropItemCommand(drop[..13], out _),
            "ACT1 item drop keeps its exact length");
        var overweightDrop = Convert.FromHexString("75006E030E01001D00010000000100000000");
        check(GameProtocolEngine.TryParseDropItemCommand(overweightDrop, out var returned, chapterTwo: true)
            && returned is { X: 878, Y: 270, ItemSpace: 0, Slot: 29, Count: 1 },
            "Chapter Two accepts the captured automatic overweight drop");
        check(!GameProtocolEngine.TryParseDropItemCommand(overweightDrop, out _)
            && GameProtocolEngine.TryParseDropItemCommand(overweightDrop[..13], out var legacyReturn)
            && legacyReturn is { X: 878, Y: 270, ItemSpace: 0, Slot: 29, Count: 1 },
            "ACT1 uses the same drop fields without the overweight flag");
        overweightDrop[13] = 2;
        check(!GameProtocolEngine.TryParseDropItemCommand(overweightDrop, out _, chapterTwo: true),
            "Chapter Two rejects an unknown drop flag");
        overweightDrop[13] = 1;
        overweightDrop[^1] = 1;
        check(!GameProtocolEngine.TryParseDropItemCommand(overweightDrop, out _, chapterTwo: true),
            "Chapter Two rejects nonzero drop padding");

        var groundDrop = GameProtocolEngine.CreateDropItem(1, 878, 270,
            new GameDungeonDrop(1, 33301, 1, 35, 1));
        check(groundDrop.Payload.AsSpan().SequenceEqual(
                Convert.FromHexString("01006E030E010100158200010000002300")),
            "ACT1 active drop ends at durability");
        check(ChapterTwoPacketProjection.Apply(groundDrop).Payload.AsSpan().SequenceEqual(
                Convert.FromHexString("01006E030E0101001582000100000023000000")),
            "Chapter Two active drop includes the final item-constructor field");

        var tradeOffer = GameProtocolEngine.CreateChangeItemTradeItem(
            new GameItemTradeEntry(3, 18000, 1, 0, 35));
        var projectedTradeOffer = ChapterTwoPacketProjection.Apply(tradeOffer);
        check(tradeOffer.Payload.AsSpan().SequenceEqual(Convert.FromHexString("0300504601000000002300"))
            && projectedTradeOffer.Payload.AsSpan().SequenceEqual(Convert.FromHexString("03005046010000000023000000"))
            && ReferenceEquals(projectedTradeOffer, ChapterTwoPacketProjection.Apply(projectedTradeOffer)),
            "Chapter Two trade offers append the default item field once and preserve the ACT1 offer");
        var clearTradeOffer = GameProtocolEngine.CreateChangeItemTradeItem(
            new GameItemTradeEntry(3, ushort.MaxValue, 0, 0, 0));
        check(ChapterTwoPacketProjection.Apply(clearTradeOffer).Payload.AsSpan().SequenceEqual(
                Convert.FromHexString("0300FFFF000000000000000000")),
            "Chapter Two trade-slot clearing consumes the same extra field as an item offer");

        PrivateStoreGoodsRow[] storeGoods = [new(0, 100, 10400, 1, 0, 35), new(1, 110, 27093, 2, 0, 30)];
        CheckRecords(GameProtocolEngine.CreatePrivateStoreEnterReply(123, storeGoods), 4, 2, 15, [0, 0]);
        CheckRecords(GameProtocolEngine.CreatePrivateStoreInfo(0, storeGoods), 2, 2, 15, [0, 0]);
        CheckRecords(GameProtocolEngine.CreatePrivateStoreBuyReply(10, 20, 30, 9, 10400, 1, 0, 35),
            13, 1, 11, [0, 0]);
        var storeFailure = GameProtocolEngine.CreatePrivateStoreReply(GameProtocolEngine.BuyPrivateStoreCommand, 1);
        check(ReferenceEquals(storeFailure, ChapterTwoPacketProjection.Apply(storeFailure)),
            "Chapter Two private-store errors keep their own short format");

        GameMailEntry[] mailbox =
        [
            new(1, "Seria"u8.ToArray(), "one"u8.ToArray(), 0, new(3037, 25, 3, 400, 1), 1000, 2000),
            new(2, "Seria"u8.ToArray(), "two"u8.ToArray(), 500, null, 3000, 4000),
            new(3, "Seria"u8.ToArray(), "archive"u8.ToArray(), 0, null, 5000, 6000, State: 3)
        ];
        var legacyMailbox = GameProtocolEngine.CreateMailboxSnapshot(mailbox);
        var chapterTwoMailbox = GameProtocolEngine.CreateMailboxSnapshot(mailbox, chapterTwo: true);
        check(legacyMailbox.Count == 2 && chapterTwoMailbox.Count == 2
            && MatchesExtendedMailbox(legacyMailbox[0], chapterTwoMailbox[0], packageCount: 1)
            && MatchesExtendedMailbox(legacyMailbox[1], chapterTwoMailbox[1], packageCount: 2),
            "Chapter Two keeps attachment, gold-only, and archive mail rows aligned with the text records");

        check(!EntranceService.PrivateStoreOwnerCommandAllowed(GameProtocolEngine.MailboxOpenCommand)
            && EntranceService.PrivateStoreOwnerCommandAllowed(GameProtocolEngine.MailboxOpenCommand, chapterTwo: true)
            && !EntranceService.PrivateStoreOwnerCommandAllowed(GameProtocolEngine.MailboxExtractItemCommand, chapterTwo: true)
            && !EntranceService.PrivateStoreOwnerCommandAllowed(GameProtocolEngine.MailboxSendCommand, chapterTwo: true),
            "Chapter Two store owner can open the mailbox without allowing claims or sends while trading");

        CheckWarehouseAndOversizeMail(check);

        var gold = GameProtocolEngine.CreateGoldItemPickup(2, 1, [22u, 0u, 300u, 0u]);
        check(gold.Payload.AsSpan().SequenceEqual(
                Convert.FromHexString("02000100160000000000000000002C010000000000000000")),
            "ACT1 gold notification preserves amounts and adjustment counts");
        check(ChapterTwoPacketProjection.Apply(gold).Payload.AsSpan().SequenceEqual(
                Convert.FromHexString("020001000116000000000000000000012C010000000000000000")),
            "Chapter Two gold notification supplies presence, amount, and adjustment count for credited slots");
        var itemPickup = GameProtocolEngine.CreateInventoryItemPickup(2, 1, 43);
        check(ReferenceEquals(itemPickup, ChapterTwoPacketProjection.Apply(itemPickup)),
            "Chapter Two item pickup keeps the shared notification format");

        var chat = Convert.FromHexString("23000202000000000008000000B5F4C2E4B2E2CAD400000000000000000000000000");
        check(EntranceService.TryReadSendMessage(chat, out var message, chapterTwo: true)
            && message.MessageType == 2 && message.MessageBytes.AsSpan().SequenceEqual(chat.AsSpan(13, 8)),
            "Chapter Two accepts the captured padded dungeon chat");
        check(!EntranceService.TryReadSendMessage(chat, out _)
            && EntranceService.TryReadSendMessage(chat[..21], out _),
            "ACT1 chat still requires the text to end at the packet boundary");
        chat[^1] = 1;
        check(!EntranceService.TryReadSendMessage(chat, out _, chapterTwo: true),
            "Chapter Two rejects nonzero chat padding");

        void CheckRecords(GameServerPacket original, int start, int count, int stride, byte[] extension)
        {
            var projected = ChapterTwoPacketProjection.Apply(original);
            var source = original.Payload.AsSpan();
            var target = projected.Payload.AsSpan();
            var projectedStride = stride + extension.Length;
            var valid = target.Length == source.Length + count * extension.Length
                && target[..start].SequenceEqual(source[..start]);
            for (var index = 0; valid && index < count; index++)
            {
                var offset = start + index * projectedStride;
                valid = target.Slice(offset, stride).SequenceEqual(source.Slice(start + index * stride, stride))
                    && target.Slice(offset + stride, extension.Length).SequenceEqual(extension);
            }

            check(valid && target[(start + count * projectedStride)..].SequenceEqual(source[(start + count * stride)..]),
                $"Chapter Two projects type {original.Type} id {original.ProtocolId} mode {source[0]} without shifting later fields");
        }

        static bool MatchesExtendedMailbox(GameServerPacket legacy, GameServerPacket chapterTwo, int packageCount)
        {
            var source = legacy.Payload.AsSpan();
            var target = chapterTwo.Payload.AsSpan();
            if (target.Length != source.Length + 2 * packageCount || !source[..2].SequenceEqual(target[..2])) return false;
            for (var index = 0; index < packageCount; index++)
            {
                const int packageSize = 35;
                const int fieldOffset = 27;
                var row = 2 + index * packageSize;
                var newRow = row + index * 2;
                if (!source.Slice(row, fieldOffset).SequenceEqual(target.Slice(newRow, fieldOffset))
                    || target[newRow + fieldOffset] != 0 || target[newRow + fieldOffset + 1] != 0
                    || !source.Slice(row + fieldOffset, packageSize - fieldOffset)
                        .SequenceEqual(target.Slice(newRow + fieldOffset + 2, packageSize - fieldOffset))) return false;
            }

            return source[(2 + packageCount * 35)..].SequenceEqual(target[(2 + packageCount * 37)..]);
        }
    }

    private static void CheckWarehouseAndOversizeMail(Action<bool, string> check)
    {
        var root = Directory.CreateTempSubdirectory("dflegacy-chapter-two-inventory-");
        try
        {
            TradeFixtures.Create(root.FullName);
            var catalog = TradeFixtures.CreateItems(root.FullName);
            var itemId = TradeFixtures.PotionItemId;
            var warehouse = new Dictionary<ushort, CharacterItemRecord>
            {
                [1] = new(1, itemId, 1),
                [3] = new(3, itemId, 99),
                [5] = new(5, itemId, 75),
                [7] = new(7, itemId, 1, SealState: 1)
            };
            var ordered = CharacterInventorySorter.SortWarehouse(warehouse, catalog, 8, mergeStacks: true);
            check(ordered.Count == 3 && ordered[0].CountOrValue == 100 && ordered[1].CountOrValue == 75
                && ordered[2] is { CountOrValue: 1, SealState: 1 }
                && CharacterInventorySorter.SortWarehouse(warehouse, catalog, 8).Count == 4,
                "Chapter Two warehouse sorting merges only compatible stacks up to the item cap");
            check(EntranceService.TryPlanReverseWarehouseMove(warehouse, 8, 0, 5, out var reordered)
                && reordered[0] == (warehouse[5] with { Slot = 0 }) && !reordered.ContainsKey(5)
                && warehouse.ContainsKey(5) && !warehouse.ContainsKey(0)
                && !EntranceService.TryPlanReverseWarehouseMove(warehouse, 8, 3, 5, out _)
                && !EntranceService.TryPlanReverseWarehouseMove(warehouse, 8, 8, 5, out _),
                "Chapter Two empty-endpoint warehouse moves preserve the item and its quantity");

            var inventory = new Dictionary<ushort, CharacterItemRecord>();
            check(EntranceService.TryPlanMailAttachment(inventory, inventory, inventory,
                    new CharacterMailAttachmentRecord(itemId, 245), catalog, uint.MaxValue,
                    out var placed, out _, out _, out var destinationList, out _)
                && placed.Count == 3 && placed.Values.Select(item => item.CountOrValue).Order().SequenceEqual(new uint[] { 45, 100, 100 })
                && destinationList == 0 && inventory.Count == 0,
                "a single oversized mail attachment is planned across multiple legal bag stacks");
        }
        finally { root.Delete(recursive: true); }
    }
}
