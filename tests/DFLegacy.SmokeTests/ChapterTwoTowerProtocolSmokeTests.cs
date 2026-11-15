using DFLegacy.Protocol;

internal static class ChapterTwoTowerProtocolSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        // Ver_OB_216 NOTI154 reads u32 dungeon index then u16 last floor at 0x423A42.
        var arrival = GameProtocolEngine.CreateChapterTwoTowerInfo(11001, 25);
        check(arrival.Type == 0 && arrival.ProtocolId == 154
            && arrival.Payload.SequenceEqual(Convert.FromHexString("F92A00001900")),
            "Chapter Two tower start carries only the dungeon ID and last floor");

        // NOTI155 has no maze coordinates or normal start-map room-state byte.
        var room = GameProtocolEngine.CreateChapterTwoTowerFloor(
            1, 0x12345678, 31001,
            [new(2, 0x1234, 4, 30, 1, 1, 5), new(64, 0x1324, 1001, 40, 5)],
            [new(4, 0x7654, 1, 0x12345678, 0x4321)]);
        check(room.Type == 0 && room.ProtocolId == 155
            && room.Payload.SequenceEqual(Convert.FromHexString(
                "01007856341219790202341204001E010105402413E90328050000010454760100785634122143")),
            "Chapter Two tower floor encodes level before type, then counted ground item rows");

        var nextRoom = GameProtocolEngine.CreateChapterTwoTowerFloor(2, 0, 31002, []);
        check(nextRoom.Payload.SequenceEqual(Convert.FromHexString("0200000000001A790000")),
            "Chapter Two tower floor uses the same counted map payload on the next floor");

        var result = GameProtocolEngine.CreateChapterTwoTowerResult(25, 0x1234, true, 0x5678);
        check(result.Type == 0 && result.ProtocolId == 156
            && result.Payload.Length == 184
            && result.Payload.AsSpan(0, 14).SequenceEqual(Convert.FromHexString("3412000019000000017856000000"))
            && result.Payload.AsSpan(14).SequenceEqual(new byte[170]),
            "Chapter Two tower result sends time before floor, followed by five empty ranking records");

        var unranked = GameProtocolEngine.CreateChapterTwoTowerResult(9, 0x000A9D69, false);
        check(unranked.Payload.AsSpan(0, 14).SequenceEqual(Convert.FromHexString("699D0A000900000000FFFFFFFF00")),
            "A defeat after nine cleared floors reads as nine floors with an unranked result");

        var ownBest = new GameChapterTwoTowerRankRecord([new("Lion"u8.ToArray(), 4, 2)], 24, 15000, 9);
        var publicEntry = new GameChapterTwoTowerRankRecord(
            [new("Fox"u8.ToArray(), 2, 1), new("Eel"u8.ToArray(), 3, 3)], 25, 14000, 1);
        var ranked = GameProtocolEngine.CreateChapterTwoTowerResult(21, 16000, false, 12, ownBest, [publicEntry]);
        var header = "803E000015000000000C00000001";
        var personal = "040000004C696F6E0402" + new string('0', 36) + "1800983A000009000000";
        var top = "03000000466F7802010300000045656C0303" + new string('0', 24) + "1900B036000001000000";
        check(ranked.Payload.SequenceEqual(Convert.FromHexString(header + personal + top + new string('0', 4 * 68))),
            "Chapter Two tower sends distinct attempt and personal ranks, a populated party row and four empty public rows");

        var reward = GameProtocolEngine.CreateChapterTwoTowerReward(
            0x12345678, [new(0, 0x1234), new(42, 3)], partySlot: 2);
        check(reward.Type == 0 && reward.ProtocolId == 157
            && reward.Payload.SequenceEqual(Convert.FromHexString("785634120000020000341200002A000300000000")),
            "Chapter Two tower reward contains experience and reward rows for four party slots");

        var oversizedRewardRejected = false;
        try
        {
            GameProtocolEngine.CreateChapterTwoTowerReward(0, new GameChapterTwoTowerRewardItem[11]);
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "rewards")
        {
            oversizedRewardRejected = true;
        }
        check(oversizedRewardRejected, "Chapter Two tower refuses more rows than the client's ten card slots");

        var enabled = GameProtocolEngine.CreateChapterTwoTowerSettlementOptions(true);
        var disabled = GameProtocolEngine.CreateChapterTwoTowerSettlementOptions(false);
        check(enabled.Type == 0 && enabled.ProtocolId == 158 && enabled.Payload.SequenceEqual(new byte[] { 1 })
            && disabled.Type == 0 && disabled.ProtocolId == 158 && disabled.Payload.SequenceEqual(new byte[] { 0 }),
            "Chapter Two tower settlement carries one more-challenges byte");

        var townReturn = GameProtocolEngine.CreateEplpReply(state: 1, option: 2);
        check(townReturn.Type == 1 && townReturn.ProtocolId == 75
            && townReturn.Payload.SequenceEqual(Convert.FromHexString("010102")),
            "Chapter Two tower confirms town return with the shared EPLP reply");
    }
}
