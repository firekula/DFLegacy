using DFLegacy.Server;
using DFLegacy.Protocol;

/// <summary>
/// 会话接线的模型契约（C2）：幂等开局 <see cref="DungeonInstance.EnsurePartyCardSettlement"/>、
/// "读自己那一列" <see cref="DungeonInstance.TryGetOwnPartyCardColumn"/>、整表 CMD 74 的付费列取值
/// <see cref="PartyCardPublishedSnapshot.PaidItems"/>。
/// 这三样是 EntranceService 入口唯一需要的模型接口；会话侧调用点本身不在自动化范围内
/// （见 task-2-report.md 的"未覆盖范围"）。
/// </summary>
internal static class PartyCardEntryPointSmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        CheckEnsureIsIdempotentPerSettlement(check);
        CheckEnsureFollowsLifecycle(check);
        CheckSoloStaysInactive(check);
        CheckOwnColumnLookup(check);
        CheckPaidItemsMapping(check);
        CheckRejectedPaidColumnPublishesWithoutItem(check);
        CheckSoloPacketLayoutUnchanged(check);
        CheckFreeCardDisplayEncoding(check);
    }

    private static void CheckFreeCardDisplayEncoding(Action<bool, string> check)
    {
        var packet = GameProtocolEngine.CreateSelectCardReply(
            new byte[] { 2, 255, 255, 0 },
            new byte[] { 255, 255, 1, 255 },
            new ushort?[] { null, null, 5001, null },
            new GameClearCardReward?[] { new(111, null), null, null, new(222, 32200) });
        byte[] expected = [
            1, 2,255,0, 255,255,0, 255,1,2, 0,0,0,0,0,0, 0x89,0x13,0,0,0,0, 0,255,0,
            68,70,67,82,1,4,
            1,111,0,0,0,0,0, 0,0,0,0,0,0,0, 0,0,0,0,0,0,0,
            1,222,0,0,0,0xC8,0x7D];
        check(packet.Payload.SequenceEqual(expected),
            "party display trailer preserves native paid records and carries each free column's own gold/item");
        var fixturePath = Environment.GetEnvironmentVariable("DFLEGACY_CARD_DISPLAY_FIXTURE");
        if (!string.IsNullOrEmpty(fixturePath)) File.WriteAllBytes(fixturePath, packet.Payload);
        var committed = new DungeonClearRewardRoll(0, false, 222, 32200, 5001, null);
        var snapshot = new PartyCardPublishedSnapshot(1,
            new(1, true, [2, 255, 255, 0], [255, 255, 255, 255], [0, 2]),
            [new(PartyCardType.Free, 0, 2, committed, PartyCardCommitStatus.Rejected),
             new(PartyCardType.Free, 1, 0, committed, PartyCardCommitStatus.Committed),
             new(PartyCardType.Free, 3, 0, committed, PartyCardCommitStatus.Committed)]);
        var rewards = snapshot.FreeRewards();
        check(rewards[0] is null && rewards[1] is null && rewards[2] is null
            && rewards[3] == new GameClearCardReward(222, 32200),
            "free display only exposes committed matching owners and excludes premium equipment");
    }

    /// <summary>
    /// R5 + 评审 F2：`Rejected` 是确定性终态、该列**可以带着归属**发布（玩家看到"这列是他的、他没拿到东西"），
    /// 但**不能**把没发出去的付费物品 id 写进整表 —— 存档里没扣钱也没进背包，客户端却会看到那张卡的面。
    /// </summary>
    private static void CheckRejectedPaidColumnPublishesWithoutItem(Action<bool, string> check)
    {
        var (instance, a, _) = CreateParty();
        var generation = instance.EnsurePartyCardSettlement();
        var roll = new DungeonClearRewardRoll(37, true, 0, null, null, 5001);
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 1, generation).Accepted,
            "paid column reserved for the rejected-content check");
        var report = typeof(DungeonInstance).GetMethod(
            "ReportPartyCardCommitFromAgent",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        check((bool)report.Invoke(instance, [instance.PartyCardSettlementId, a, PartyCardType.Paid, (byte)1, generation,
                new PartyCardContent(PartyCardType.Paid, 1, 0, roll, PartyCardCommitStatus.Pending),
                new PartyCardCommitOutcome(DungeonRewardLedgerStatus.Rejected, "smoke")])!,
            "rejected paid content is recorded as a deterministic terminal state");
        check(instance.TryTakePartyCardPublication(generation, out var snapshot),
            "a rejected paid column still publishes its owner");
        check(snapshot!.Selection.PaidOwners[1] == 0,
            "the rejected paid column keeps its owner in the shared table");
        check(snapshot.PaidItems()[1] is null,
            "a rejected paid column never advertises an item it did not grant");
        var packet = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(
            snapshot.Selection.FreeOwners,
            snapshot.Selection.PaidOwners,
            snapshot.PaidItems());
        check(packet.Payload[2 + (3 * 1)] == 0 && packet.Payload[3 + (3 * 1)] == 1,
            "the rejected paid column emits exactly one empty record");
    }

    /// <summary>
    /// R6：单人路径继续用会话本地那一个构图器（<c>CreateSelectCardReply(freeCardIndex, goldCardIndex, …)</c>），
    /// 字节形状一字不变 —— 组队的新构图器是**另一个重载**，新增逻辑一律 IsParty 门控。
    /// </summary>
    private static void CheckSoloPacketLayoutUnchanged(Action<bool, string> check)
    {
        var free = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(
            freeCardIndex: 1,
            goldCardIndex: null,
            goldRewardRevealed: false,
            goldRewardItemId: null,
            localPartyIndex: 0);
        check(free.Payload.SequenceEqual(new byte[]
            {
                1,
                0xFF, 0xFF, 0, 0, 0xFF, 0, 0xFF, 0xFF, 0, 0xFF, 0xFF, 0
            }),
            "solo free-card CMD 74 keeps its exact 13-byte layout");

        var paid = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(
            freeCardIndex: null,
            goldCardIndex: 2,
            goldRewardRevealed: true,
            goldRewardItemId: 4321,
            localPartyIndex: 0);
        check(paid.Payload.SequenceEqual(new byte[]
            {
                1,
                0xFF, 0xFF, 0, 0xFF, 0xFF, 0,
                0xFF, 0x00, 2, 0, 0, 0, 0, 0, 0, 0xE1, 0x10, 0, 0, 0, 0,
                0xFF, 0xFF, 0
            }),
            "solo paid-card CMD 74 keeps its exact 23-byte layout");
    }

    private static (DungeonInstance Instance, Guid A, Guid B) CreateParty()
    {
        var instance = new DungeonInstance(1, Guid.Empty, null!, null!);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        instance.AddMember(new(a, 1, 0, "A", _ => ValueTask.CompletedTask));
        instance.AddMember(new(b, 2, 1, "B", _ => ValueTask.CompletedTask));
        return (instance, a, b);
    }

    /// <summary>同一个结算里两个会话各自 Ensure → 同一代次；第二个到达者不会清掉第一个的预留。</summary>
    private static void CheckEnsureIsIdempotentPerSettlement(Action<bool, string> check)
    {
        var (instance, a, b) = CreateParty();
        var generation = instance.EnsurePartyCardSettlement();
        check(generation > 0, "party settlement opens a non-zero generation");
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 2, generation).Accepted,
            "first member reserves inside the ensured generation");
        check(instance.EnsurePartyCardSettlement() == generation,
            "second member's ensure returns the same generation");
        check(instance.SnapshotPartyCards().FreeOwners[2] == 0,
            "re-ensure never wipes an already reserved column");
        instance.CompletePartyCardSelection(generation);
        check(instance.EnsurePartyCardSettlement() == generation,
            "ensure after auto completion still returns the same generation");
        check(instance.SnapshotPartyCards().FreeOwners.SequenceEqual(new byte[] { 1, 255, 0, 255 }),
            "auto completion keeps the manual column and never assigns a second column to the same slot");
        var published = new byte[] { 1, 255, 0, 255 };
        check(instance.SnapshotPartyCards().FreeOwners.SequenceEqual(published)
            && instance.EnsurePartyCardSettlement() == generation,
            "published owners survive any later ensure");
        check(instance.TryReservePartyCard(b, PartyCardType.Free, 1, generation).Status
            == PartyCardSelectionStatus.Rejected,
            "auto completion closes free columns for everyone");
    }

    /// <summary>开局 / 换房 / 停局都让旧代次失效；下一次 Ensure 必须开新代次（旧代次归属不再生效）。</summary>
    private static void CheckEnsureFollowsLifecycle(Action<bool, string> check)
    {
        var (instance, a, _) = CreateParty();
        var first = instance.EnsurePartyCardSettlement();
        instance.TryReservePartyCard(a, PartyCardType.Free, 3, first);
        instance.StopRun();
        var afterStop = instance.EnsurePartyCardSettlement();
        check(afterStop != first && afterStop > 0, "StopRun forces a fresh settlement generation");
        check(instance.SnapshotPartyCards().FreeOwners.All(owner => owner == 255),
            "fresh generation starts with no owners");
        instance.EnterDetachedRoom(DungeonFixture.Room([]));
        var afterRoom = instance.EnsurePartyCardSettlement();
        check(afterRoom != afterStop, "EnterDetachedRoom forces a fresh settlement generation");
        check(!instance.TryReservePartyCard(a, PartyCardType.Free, 0, afterStop).Accepted,
            "stale generation cannot reserve inside the new settlement");
    }

    /// <summary>单人实例：Ensure 恒 0（模型不参与），单人路径继续走会话本地选卡。</summary>
    private static void CheckSoloStaysInactive(Action<bool, string> check)
    {
        var solo = new DungeonInstance(0, Guid.NewGuid(), null!, null!);
        check(solo.EnsurePartyCardSettlement() == 0, "solo instance never opens a settlement generation");
        check(solo.EnsurePartyCardSettlement() == 0, "solo ensure is stable at zero");
    }

    /// <summary>每个成员读到的必须是**自己**那一列；没归属/已离队 → false（会话据此审计，不自己挑列）。</summary>
    private static void CheckOwnColumnLookup(Action<bool, string> check)
    {
        var (instance, a, b) = CreateParty();
        var generation = instance.EnsurePartyCardSettlement();
        check(instance.TryReservePartyCard(a, PartyCardType.Free, 3, generation).Accepted,
            "manual reservation before auto completion");
        instance.CompletePartyCardSelection(generation);
        check(instance.TryGetOwnPartyCardColumn(a, PartyCardType.Free, out var columnA) && columnA == 3,
            "member A reads back its own manually reserved column");
        check(instance.TryGetOwnPartyCardColumn(b, PartyCardType.Free, out var columnB) && columnB == 0,
            "member B reads its own auto assigned column");
        check(columnA != columnB, "two members never read the same column");
        check(!instance.TryGetOwnPartyCardColumn(Guid.NewGuid(), PartyCardType.Free, out _),
            "absent member has no column");
        check(!instance.TryGetOwnPartyCardColumn(a, PartyCardType.Paid, out _),
            "no paid column is assigned by the free auto completion");
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 1, generation).Accepted,
            "paid reservation accepted for A");
        check(instance.TryGetOwnPartyCardColumn(a, PartyCardType.Paid, out var paidColumn) && paidColumn == 1,
            "member A reads back its own paid column");
        check(!instance.TryGetOwnPartyCardColumn(b, PartyCardType.Paid, out _),
            "member B has no paid column while A owns one");
        instance.RemoveMember(b);
        check(!instance.TryGetOwnPartyCardColumn(b, PartyCardType.Free, out _),
            "departed member can no longer read a column");
    }

    /// <summary>整表 CMD 74 的付费列：归属列取**该列归属成员自己掷的**付费物品 id；无内容/未归属 → null。</summary>
    private static void CheckPaidItemsMapping(Action<bool, string> check)
    {
        var (instance, a, b) = CreateParty();
        var generation = instance.EnsurePartyCardSettlement();
        var roll = new DungeonClearRewardRoll(10, true, 50, 100, 101, 102);
        check(instance.TryReservePartyCard(a, PartyCardType.Paid, 2, generation).Accepted,
            "paid column reserved for the item mapping check");
        var report = typeof(DungeonInstance).GetMethod(
            "ReportPartyCardCommitFromAgent",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        bool Report(Guid characterId, PartyCardType type, byte column, PartyCardContent content) =>
            (bool)report.Invoke(instance, [instance.PartyCardSettlementId, characterId, type, column, generation, content, new PartyCardCommitOutcome(DungeonRewardLedgerStatus.Committed, null)])!;
        // 付费列还没有内容 → 该列不可发布（保持 0xFF），所以先看未归属/未终态两种取值为 null。
        check(!Report(b, PartyCardType.Paid, 2, new PartyCardContent(PartyCardType.Paid, 2, 1, roll, PartyCardCommitStatus.Pending)),
            "another member cannot fill A's paid column");
        check(Report(a, PartyCardType.Paid, 2, new PartyCardContent(PartyCardType.Paid, 2, 0, roll, PartyCardCommitStatus.Pending)),
            "owner fills its paid column content");
        check(instance.TryTakePartyCardPublication(generation, out var snapshot), "paid column publishes with content");
        if (snapshot is null)
        {
            return;
        }

        var items = snapshot.PaidItems();
        check(items.Length == 4, "paid item mapping covers four columns");
        check(items[2] == 102, "assigned paid column carries its owner's own paid item id");
        check(items[0] is null && items[1] is null && items[3] is null,
            "columns without an owner carry no paid item");
        var packet = DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(
            snapshot.Selection.FreeOwners,
            snapshot.Selection.PaidOwners,
            items);
        check(packet.Payload.SequenceEqual(DFLegacy.Protocol.GameProtocolEngine.CreateSelectCardReply(
                snapshot.Selection.FreeOwners,
                snapshot.Selection.PaidOwners,
                snapshot.PaidItems()).Payload),
            "whole-table CMD 74 bytes are deterministic for one snapshot");
        check(packet.Payload[9] == 2, "paid column with item emits two records");
    }
}
