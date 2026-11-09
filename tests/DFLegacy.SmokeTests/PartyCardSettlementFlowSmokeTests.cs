using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// 会话接线顺序的唯一一份（<see cref="PartyCardSettlementFlow"/>）的端到端证据：两个**都不操作**的队员
/// 各自走自动翻牌，必须拿到**不同**的自由卡列、各用**自己**的掷取与基线落库、并且收到**同一份**整表
/// CMD 74 字节（R1′/R3′/R4/R5/R7′）。落库用真 <see cref="JsonGameStore"/>，掷取用每个成员自己那份
/// <see cref="DungeonClearRewardRoll"/>。
/// 覆盖范围：Flow + 计划器 + 实例模型 + 真存档。**不覆盖**：EntranceService 里把闭包接上去的那几行
/// （见 task-2-report.md 的"未覆盖范围"）。
/// </summary>
internal static class PartyCardSettlementFlowSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        await PartyPremiumCardSmokeTests.RunAsync(check);
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-card-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonGameStore(
                new ServerOptions { DataPath = Path.Combine(root, "store.json") },
                NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            using var fixture = new DungeonFixture();
            var alphaId = (await store.GetCharactersAsync("test"))[0].Id;
            await store.CreateAccountAsync(new CreateAccountRequest("beta", "password", OicqCode: "123456"));
            var bravoId = (await store.CreateCharacterAsync("beta", new CreateCharacterRequest("布拉沃", Level: 60))).Character!.Id;

            await CheckTwoIdleMembersAsync(check, store, fixture, alphaId, bravoId);
            await CheckManualPickAsync(check, store, fixture, alphaId, bravoId);
            await CheckPaidCardAsync(check, store, fixture, alphaId, bravoId);
            await CheckUnknownColumnAsync(check, store, fixture, alphaId, bravoId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 每个用例开头重新读一次存档：会话侧的金币/背包基线必须与存档同步（R4 的 CAS 前提），
    /// 用例之间共享一个存档，所以"重新读"模拟的就是会话自己那份内存基线已经跟上的状态。
    /// </summary>
    private static async Task<(CharacterRecord Alpha, CharacterRecord Bravo)> ReadMembersAsync(
        JsonGameStore store, Guid alphaId, Guid bravoId) =>
        ((await store.GetCharacterAsync("test", alphaId))!, (await store.GetCharacterAsync("beta", bravoId))!);

    private static DungeonInstance CreateParty(DungeonFixture fixture, ushort partyId) =>
        fixture.CreatePartyInstance(partyId);

    private static MemberHarness AddMember(
        DungeonInstance instance,
        JsonGameStore store,
        DungeonFixture fixture,
        CharacterRecord character,
        byte slot,
        string userName,
        DungeonClearRewardRoll roll)
    {
        var member = new MemberHarness(store, fixture.Items, character, slot, userName, roll);
        instance.AddMember(new(character.Id, checked((ushort)(slot + 1)), slot, character.Name, _ => ValueTask.CompletedTask));
        instance.RegisterPartyCardRewardAgent(character.Id, member.CreateAgent());
        return member;
    }

    /// <summary>两个都不操作的队员：各拿各的列、各发各的奖励、整表字节一致、触发者不重复收。</summary>
    private static async Task CheckTwoIdleMembersAsync(
        Action<bool, string> check, JsonGameStore store, DungeonFixture fixture, Guid alphaId, Guid bravoId)
    {
        var (alpha, bravo) = await ReadMembersAsync(store, alphaId, bravoId);
        var instance = CreateParty(fixture, 9001);
        var memberA = AddMember(instance, store, fixture, alpha, 0, "test", new DungeonClearRewardRoll(0, false, 111, null, null, null));
        var memberB = AddMember(instance, store, fixture, bravo, 1, "beta", new DungeonClearRewardRoll(0, false, 222, null, null, null));
        memberA.Peers.Add(memberB);
        memberB.Peers.Add(memberA);

        var resultA = await memberA.SettleAutomaticAsync(instance);
        var resultB = await memberB.SettleAutomaticAsync(instance);

        check(resultA.Active && resultB.Active, "both idle members run inside one party settlement");
        check(resultA.Column != resultB.Column, "the two idle members never claim the same card column");
        check(memberA.Commits == 1 && memberB.Commits == 1, "each member commits exactly one own-column reward");
        check(memberA.Character.Gold == alpha.Gold + 111, "member A's own roll is what A is granted");
        check(memberB.Character.Gold == bravo.Gold + 222, "member B's own roll is what B is granted (never shared)");
        check(resultA.Published && resultB.Published, "both members publish as the owner set grows");
        check(memberA.Self.Count == 1 && memberA.Received.Count == 1 && memberB.Self.Count == 1 && memberB.Received.Count == 1,
            "every member receives each publication exactly once (triggerer included, never twice)");

        // 第一次发布（A 只填了自己那列）：另一列必须还是 0xFF（客户端 0xFF 不写入 → 之后可安全补齐）。
        var firstTable = memberA.Self[^1].Payload;
        check(FreeOwnerAt(firstTable, resultA.Column) == memberA.Slot
            && FreeOwnerAt(firstTable, resultB.Column) == PartyCardSelectionState.Unassigned,
            "the first publication fills only the settled column and masks the pending one");
        check(memberB.Received[^1].Payload.SequenceEqual(firstTable),
            "the first publication reaches the other member byte-identically");

        // 第二次发布（B 补上自己那列）：整表两列都在，且 A 收到的字节与 B 自己那份完全相同。
        var finalTable = memberB.Self[^1].Payload;
        check(FreeOwnerAt(finalTable, resultA.Column) == memberA.Slot
            && FreeOwnerAt(finalTable, resultB.Column) == memberB.Slot,
            "the final table carries both owners in their own columns");
        check(memberA.Received[^1].Payload.SequenceEqual(finalTable),
            "the final owner table reaches the other member byte-identically");
        var display = finalTable.AsSpan(finalTable.Length - 34);
        check(display[..6].SequenceEqual("DFCR\u0001\u0004"u8)
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(display.Slice(7 + 7 * resultA.Column, 4)) == 111
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(display.Slice(7 + 7 * resultB.Column, 4)) == 222,
            "the real shared publication displays both owners' actual distinct committed gold amounts");
        check(!instance.TryTakePartyCardPublication(resultB.Generation, out _),
            "no further publication after the owner set stops growing");

        // 重放：不再落库、不再发第二份（宁可不发也不发错）。
        var beforeSelfCount = memberA.Self.Count;
        var replay = await memberA.SettleAutomaticAsync(instance);
        check(memberA.Commits == 1 && memberA.Character.Gold == alpha.Gold + 111,
            "re-running the automatic path never grants a second time");
        check(!replay.Published && memberA.Self.Count == beforeSelfCount && memberA.Received.Count == 1,
            "re-running the automatic path never publishes a duplicate table");
        var alreadySettled = await memberA.SettleAutomaticAsync(instance, alreadySettled: true);
        check(alreadySettled.Column == resultA.Column && memberA.Commits == 1,
            "a session that already settled its own column is a no-op");
    }

    /// <summary>手动选列先占先得，自动补全只填剩下的列；抢别人的列不生效、不发奖励。</summary>
    private static async Task CheckManualPickAsync(
        Action<bool, string> check, JsonGameStore store, DungeonFixture fixture, Guid alphaId, Guid bravoId)
    {
        var (alpha, bravo) = await ReadMembersAsync(store, alphaId, bravoId);
        var instance = CreateParty(fixture, 9002);
        var memberA = AddMember(instance, store, fixture, alpha, 0, "test", new DungeonClearRewardRoll(0, false, 50, null, null, null));
        var memberB = AddMember(instance, store, fixture, bravo, 1, "beta", new DungeonClearRewardRoll(0, false, 60, null, null, null));
        memberA.Peers.Add(memberB);
        memberB.Peers.Add(memberA);

        var manual = await memberA.PickFreeCardAsync(instance, 2);
        check(manual.Accepted && manual.Column == 2, "manual free card pick reserves the requested column");
        var automatic = await memberB.SettleAutomaticAsync(instance);
        check(automatic.Column != 2, "automatic completion never takes a manually reserved column");
        check(memberA.Character.Gold == alpha.Gold + 50 && memberB.Character.Gold == bravo.Gold + 60,
            "manual and automatic members are each granted their own roll");

        var beforeGold = memberA.Character.Gold;
        var stolen = await memberA.PickFreeCardAsync(instance, automatic.Column);
        check(!stolen.Accepted && memberA.Commits == 1 && memberA.Character.Gold == beforeGold,
            "picking a column owned by another member grants nothing");
    }

    /// <summary>付费卡：先占列再落库，只有买家自己扣费；补发的那一版保留免费列字节不变（R3′）。</summary>
    private static async Task CheckPaidCardAsync(
        Action<bool, string> check, JsonGameStore store, DungeonFixture fixture, Guid alphaId, Guid bravoId)
    {
        var (alpha, bravo) = await ReadMembersAsync(store, alphaId, bravoId);
        var instance = CreateParty(fixture, 9003);
        var rollA = new DungeonClearRewardRoll(37, true, 40, null, null, 5001);
        var memberA = AddMember(instance, store, fixture, alpha, 0, "test", rollA);
        var memberB = AddMember(instance, store, fixture, bravo, 1, "beta", new DungeonClearRewardRoll(0, false, 20, null, null, null));
        memberA.Peers.Add(memberB);
        memberB.Peers.Add(memberA);

        await memberA.SettleAutomaticAsync(instance);
        await memberB.SettleAutomaticAsync(instance);
        var freeTable = memberB.Self[^1].Payload;

        var bought = await memberA.BuyPaidCardAsync(instance, 3, canAfford: true);
        check(bought.Accepted && bought.Published, "paid card purchase publishes a new table version");
        check(memberA.Character.Gold == alpha.Gold + 40 - 37, "the buyer pays its own paid-card cost once");
        var paidTable = memberA.Self[^1].Payload;
        check(FreeOwnerBytes(paidTable).SequenceEqual(FreeOwnerBytes(freeTable)),
            "the later publication keeps the already published free columns byte-identical");
        check(PaidOwnerAt(paidTable, 3) == memberA.Slot, "the paid column now carries the buyer's own slot");
        check(PaidItemAt(paidTable, 3) == rollA.GoldItemId,
            "the paid column carries the buyer's own paid item id");
        check(memberB.Received[^1].Payload.SequenceEqual(paidTable),
            "the paid publication reaches the other member byte-identically");

        var goldAfterPurchase = memberA.Character.Gold;
        var again = await memberA.BuyPaidCardAsync(instance, 3, canAfford: true);
        check(!again.Published && memberA.Commits == 2 && memberA.Character.Gold == goldAfterPurchase,
            "reserving the same paid column again never charges twice");

        var otherBuyerGold = memberB.Character.Gold;
        var contested = await memberB.BuyPaidCardAsync(instance, 3, canAfford: true);
        check(!contested.Accepted && memberB.Character.Gold == otherBuyerGold,
            "another member cannot buy an already owned paid column");

        var unaffordable = await memberB.BuyPaidCardAsync(instance, 1, canAfford: false);
        check(!unaffordable.Accepted && instance.SnapshotPartyCards().PaidOwners[1] == PartyCardSelectionState.Unassigned,
            "an unaffordable paid card is refused before it consumes the column");
        check(memberB.Commits == 1, "the automatic path never buys a paid card on its own");
    }

    /// <summary>Unknown 列：屏蔽、不发布、不重试、留审计（同代次其他人的列照常发布）。</summary>
    private static async Task CheckUnknownColumnAsync(
        Action<bool, string> check, JsonGameStore store, DungeonFixture fixture, Guid alphaId, Guid bravoId)
    {
        var (alpha, bravo) = await ReadMembersAsync(store, alphaId, bravoId);
        var instance = CreateParty(fixture, 9004);
        var memberA = AddMember(instance, store, fixture, alpha, 0, "test", new DungeonClearRewardRoll(0, false, 90, null, null, null));
        memberA.CommitStatus = DungeonRewardLedgerStatus.Unknown;
        var memberB = AddMember(instance, store, fixture, bravo, 1, "beta", new DungeonClearRewardRoll(0, false, 70, null, null, null));
        memberA.Peers.Add(memberB);
        memberB.Peers.Add(memberA);

        var resultA = await memberA.SettleAutomaticAsync(instance);
        var resultB = await memberB.SettleAutomaticAsync(instance);
        check(memberA.Character.Gold == alpha.Gold && memberB.Character.Gold == bravo.Gold + 70,
            "an Unknown column is never granted while the other member's column is");
        var table = memberB.Self[^1].Payload;
        check(FreeOwnerAt(table, resultA.Column) == PartyCardSelectionState.Unassigned,
            "the Unknown column stays 0xFF in the published table");
        check(FreeOwnerAt(table, resultB.Column) == memberB.Slot,
            "the known column still publishes next to the Unknown one");
        check(memberA.Audit.Any(entry => entry.Reason == "commit-unknown"),
            "the Unknown outcome is audited for the operator");
        var commitsAfterUnknown = memberA.Commits;
        await memberA.SettleAutomaticAsync(instance);
        check(memberA.Commits == commitsAfterUnknown && memberA.Character.Gold == alpha.Gold,
            "an Unknown column is never retried automatically");
    }

    private static byte FreeOwnerAt(byte[] payload, byte column) => payload[1 + (3 * column)];

    private static byte PaidOwnerAt(byte[] payload, byte column) => payload[2 + (3 * column)];

    private static byte[] FreeOwnerBytes(byte[] payload) =>
        [payload[1], payload[4], payload[7], payload[10]];

    /// <summary>付费列的物品 id：基础偏移 = 1 + 3*列；有记录时先写 u16 金币、u32 金币，第二条记录才是物品 u16。</summary>
    private static ushort? PaidItemAt(byte[] payload, byte column)
    {
        var baseOffset = 1 + (3 * column);
        if (payload[baseOffset + 2] < 2)
        {
            return null;
        }

        var itemOffset = baseOffset + 3 + 2 + 4;
        return (ushort)(payload[itemOffset] | (payload[itemOffset + 1] << 8));
    }

    /// <summary>
    /// 会话侧那一份"自己角色的基线 + 自己的掷取"的测试镜像：用**真存档**按 R4 提交，
    /// Committed 后采用存档返回的角色状态（对应会话的 AdoptSavedWarehouseUpgradeState）。
    /// </summary>
    private sealed class MemberHarness(
        JsonGameStore store,
        ItemCatalog itemCatalog,
        CharacterRecord character,
        byte slot,
        string userName,
        DungeonClearRewardRoll roll)
    {
        private CharacterRecord _character = character;

        public Guid CharacterId => _character.Id;
        public byte Slot => slot;
        public int Commits { get; private set; }
        public DungeonRewardLedgerStatus CommitStatus { get; set; } = DungeonRewardLedgerStatus.Committed;
        public CharacterRecord Character => _character;
        public List<MemberHarness> Peers { get; } = [];
        public List<GameServerPacket> Self { get; } = [];
        public List<GameServerPacket> Received { get; } = [];
        public List<PartyCardAuditEntry> Audit { get; } = [];

        public Task<PartyCardSettlementResult> SettleAutomaticAsync(DungeonInstance instance, bool alreadySettled = false) =>
            PartyCardSettlementFlow.SettleAutomaticFreeCardAsync(
                instance, CharacterId, alreadySettled, "smoke",
                DeliverToSelf, BroadcastToPeers, Audit.Add, CancellationToken.None);

        public Task<PartyCardSettlementResult> PickFreeCardAsync(DungeonInstance instance, byte column) =>
            PartyCardSettlementFlow.PickFreeCardAsync(
                instance, CharacterId, column, "smoke",
                DeliverToSelf, BroadcastToPeers, Audit.Add, CancellationToken.None);

        public Task<PartyCardSettlementResult> BuyPaidCardAsync(DungeonInstance instance, byte column, bool canAfford) =>
            PartyCardSettlementFlow.BuyPaidCardAsync(
                instance, CharacterId, column, () => canAfford, "smoke",
                DeliverToSelf, BroadcastToPeers, Audit.Add, CancellationToken.None);

        private Task DeliverToSelf(GameServerPacket packet)
        {
            Self.Add(packet);
            return Task.CompletedTask;
        }

        private Task BroadcastToPeers(GameServerPacket packet)
        {
            foreach (var peer in Peers)
            {
                peer.Received.Add(packet);
            }

            return Task.CompletedTask;
        }

        public PartyCardContent RollOwnColumn(Guid settlementId, PartyCardType type, byte column, long generation) =>
            new(type, column, slot, roll, PartyCardCommitStatus.Pending);

        /// <summary>生产那一份会话代理适配器（会话侧同一个类型），把两件事交回"它自己的会话"。</summary>
        public IPartyCardRewardAgent CreateAgent() =>
            new SessionPartyCardRewardAgent(CharacterId, RollOwnColumn, CommitOwnColumnAsync);

        public async Task<PartyCardCommitOutcome> CommitOwnColumnAsync(
            Guid settlementId,
            PartyCardType type,
            byte column,
            long generation,
            PartyCardContent content,
            CancellationToken cancellationToken)
        {
            Commits++;
            if (CommitStatus != DungeonRewardLedgerStatus.Committed)
            {
                return new PartyCardCommitOutcome(CommitStatus, "smoke-defined");
            }

            var expectedGold = _character.Gold;
            // 走**生产**那一份落库机制（会话侧同一个调用）：内容与基线全部来自这个成员自己。
            var commit = await PartyCardOwnColumnCommitter.CommitAsync(
                store,
                userName,
                _character,
                slot,
                type,
                column,
                generation,
                settlementId,
                roll,
                expectedGold,
                _character.Inventory?.ToDictionary(item => item.Slot) ?? [],
                (_character.Equipment ?? []).ToArray(),
                _character.WarehouseCapacity,
                inventoryWeightLimit: 100_000,
                itemCatalog,
                cancellationToken);
            if (commit.InstalledCharacter is { } installed)
            {
                _character = installed;
            }

            return commit.Outcome;
        }
    }
}
