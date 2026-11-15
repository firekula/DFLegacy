using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// R4 的落库机制（会话侧唯一可自动化的那一半）：由**成员自己角色的精确基线**构造
/// <see cref="DungeonRewardCommitRequest"/>，经 <see cref="JsonGameStore.CommitDungeonRewardAsync"/>
/// 提交；同一个幂等键重放不再发第二份。跨成员基线（拿别人的金币/背包当基线）必须被拒。
/// 会话本地量（currentGold/mainInventory 的采用）不在本文件范围内。
/// </summary>
internal static class ClearRewardGrantPlannerSmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-card-grant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonGameStore(
                new ServerOptions { DataPath = Path.Combine(root, "store.json") },
                NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            using var fixture = new DungeonFixture();
            var inventory = fixture.Items;

            var alpha = (await store.GetCharactersAsync("test"))[0];
            check(await store.CreateAccountAsync(new CreateAccountRequest("beta", "password", OicqCode: "123456")) is { Created: true },
                "fixture creates a second account for the other member");
            var bravo = (await store.CreateCharacterAsync("beta", new CreateCharacterRequest("布拉沃", Level: 60))).Character!;

            var rollA = new DungeonClearRewardRoll(0, false, 111, null, null, null);
            var rollB = new DungeonClearRewardRoll(0, false, 222, null, null, null);

            // ① 计划：金币卡不动物品（无物品 id）→ 背包基线原样、不产生邮件、容量不变。
            var goldOnlyPlan = ClearRewardGrantPlanner.Plan(
                alpha.Inventory?.ToDictionary(item => item.Slot) ?? [],
                alpha.WarehouseCapacity,
                alpha.Gold + (int)rollA.FreeGoldAmount,
                [rollA.FreeItemId, rollA.PremiumItemId],
                inventoryWeightLimit: 100_000,
                inventory);
            check(goldOnlyPlan.Mails.Count == 0 && !goldOnlyPlan.AnyGranted,
                "gold-only free card plans no items and no mail");
            check(goldOnlyPlan.WarehouseCapacity == alpha.WarehouseCapacity,
                "gold-only free card keeps the warehouse capacity");
            check(goldOnlyPlan.Inventory.Count == (alpha.Inventory?.Count ?? 0),
                "gold-only free card keeps the inventory baseline intact");

            // ② 请求：指纹自洽（服务端用同一函数复算），planned 装备与基线一致。
            var settlementId = Guid.NewGuid();
            var otherSettlementId = Guid.NewGuid();
            var key = ClearRewardGrantPlanner.IdempotencyKey(settlementId, alpha.Id, PartyCardType.Free, 0);
            check(key == ClearRewardGrantPlanner.IdempotencyKey(settlementId, alpha.Id, PartyCardType.Free, 0)
                && key != ClearRewardGrantPlanner.IdempotencyKey(settlementId, alpha.Id, PartyCardType.Free, 1)
                && key != ClearRewardGrantPlanner.IdempotencyKey(otherSettlementId, alpha.Id, PartyCardType.Free, 0)
                && key != ClearRewardGrantPlanner.IdempotencyKey(settlementId, alpha.Id, PartyCardType.Paid, 0),
                "idempotency key is stable per settlement/character/type/column only");
            var requestA = ClearRewardGrantPlanner.BuildCommitRequest(
                key, "test", alpha.Id, 7, 0, PartyCardType.Free, 0,
                alpha.Gold, alpha.Gold + (int)rollA.FreeGoldAmount,
                (alpha.Inventory ?? []).ToArray(), (alpha.Equipment ?? []).ToArray(),
                alpha.WarehouseCapacity, goldOnlyPlan);
            check(requestA.PayloadFingerprint.Length == 64
                && requestA.PayloadFingerprint == JsonGameStore.ComputeDungeonRewardFingerprint(requestA),
                "request fingerprint is the server-side fingerprint of the frozen payload");
            check((requestA.PlannedEquipment ?? []).SequenceEqual(alpha.Equipment ?? []),
                "planned equipment stays the baseline equipment (cards grant no equipment change)");

            // ③ 真落库：自己的基线 → Committed；同键重放不再发第二份。
            var first = await store.CommitDungeonRewardAsync(requestA);
            check(first.Status == DungeonRewardLedgerStatus.Committed && first.GoldDelta == 111,
                "free card commit succeeds against the owner's own baseline");
            var replay = await store.CommitDungeonRewardAsync(requestA);
            check(replay.Status == DungeonRewardLedgerStatus.Committed
                && replay.UpdatedCharacter?.Gold == first.UpdatedCharacter?.Gold,
                "replaying the same card key never grants the reward twice");

            // ④ 另一个成员用自己的基线提交自己的奖励（R1′：绝不共用一次掷取/一份基线）。
            var planB = ClearRewardGrantPlanner.Plan(
                bravo.Inventory?.ToDictionary(item => item.Slot) ?? [],
                bravo.WarehouseCapacity,
                bravo.Gold + (int)rollB.FreeGoldAmount,
                [rollB.FreeItemId, rollB.PremiumItemId],
                inventoryWeightLimit: 100_000,
                inventory);
            var requestB = ClearRewardGrantPlanner.BuildCommitRequest(
                ClearRewardGrantPlanner.IdempotencyKey(settlementId, bravo.Id, PartyCardType.Free, 1),
                "beta", bravo.Id, 7, 1, PartyCardType.Free, 1,
                bravo.Gold, bravo.Gold + (int)rollB.FreeGoldAmount,
                (bravo.Inventory ?? []).ToArray(), (bravo.Equipment ?? []).ToArray(),
                bravo.WarehouseCapacity, planB);
            var second = await store.CommitDungeonRewardAsync(requestB);
            check(second.Status == DungeonRewardLedgerStatus.Committed && second.GoldDelta == 222,
                "the other member commits its own roll with its own baseline");

            // ④b 同一角色**第二次进同一个副本**（新实例、代次又从 1 开始）：幂等键必须区分两局，
            //     否则第二局会撞上第一局留下的冻结载荷（idempotency-payload-mismatch）→ 奖励永久发不出去。
            var alphaAgain = (await store.GetCharacterAsync("test", alpha.Id))!;
            var secondRunPlan = ClearRewardGrantPlanner.Plan(
                alphaAgain.Inventory?.ToDictionary(item => item.Slot) ?? [],
                alphaAgain.WarehouseCapacity,
                alphaAgain.Gold + 7,
                [null, null],
                inventoryWeightLimit: 100_000,
                inventory);
            var secondRunRequest = ClearRewardGrantPlanner.BuildCommitRequest(
                ClearRewardGrantPlanner.IdempotencyKey(Guid.NewGuid(), alpha.Id, PartyCardType.Free, 0),
                "test", alpha.Id, 1, 0, PartyCardType.Free, 0,
                alphaAgain.Gold, alphaAgain.Gold + 7,
                (alphaAgain.Inventory ?? []).ToArray(), (alphaAgain.Equipment ?? []).ToArray(),
                alphaAgain.WarehouseCapacity, secondRunPlan);
            var secondRun = await store.CommitDungeonRewardAsync(secondRunRequest);
            check(secondRun.Status == DungeonRewardLedgerStatus.Committed && secondRun.GoldDelta == 7,
                "a later settlement of the same character and column still commits (per-settlement keys)");

            var forgedBaseline = requestB with
            {
                IdempotencyKey = ClearRewardGrantPlanner.IdempotencyKey(Guid.NewGuid(), bravo.Id, PartyCardType.Free, 3),
                ExpectedGold = alpha.Gold,
                ExpectedInventory = (alpha.Inventory ?? []).ToArray()
            };
            forgedBaseline = forgedBaseline with
            {
                PayloadFingerprint = JsonGameStore.ComputeDungeonRewardFingerprint(forgedBaseline)
            };
            var forged = await store.CommitDungeonRewardAsync(forgedBaseline);
            check(forged.Status == DungeonRewardLedgerStatus.Rejected
                && forged.RejectionCode == "character-state-conflict",
                "another member's baseline cannot be used to commit this member's card");

            // ⑤ 付费卡：自己扣自己的费（金币下降，物品走自己那份掷取）。
            var paidCharacter = (await store.GetCharactersAsync("beta"))[0];
            var paidRoll = new DungeonClearRewardRoll(37, true, 0, null, null, null);
            var paidPlan = ClearRewardGrantPlanner.Plan(
                paidCharacter.Inventory?.ToDictionary(item => item.Slot) ?? [],
                paidCharacter.WarehouseCapacity,
                paidCharacter.Gold - (int)paidRoll.GoldCardCost,
                [paidRoll.GoldItemId],
                inventoryWeightLimit: 100_000,
                inventory);
            var paidRequest = ClearRewardGrantPlanner.BuildCommitRequest(
                ClearRewardGrantPlanner.IdempotencyKey(settlementId, paidCharacter.Id, PartyCardType.Paid, 2),
                "beta", paidCharacter.Id, 7, 1, PartyCardType.Paid, 2,
                paidCharacter.Gold, paidCharacter.Gold - (int)paidRoll.GoldCardCost,
                (paidCharacter.Inventory ?? []).ToArray(), (paidCharacter.Equipment ?? []).ToArray(),
                paidCharacter.WarehouseCapacity, paidPlan);
            var paid = await store.CommitDungeonRewardAsync(paidRequest);
            check(paid.Status == DungeonRewardLedgerStatus.Committed && paid.GoldDelta == -37,
                "paid card commit charges exactly the buyer's own roll cost");
            check((await store.CommitDungeonRewardAsync(paidRequest)).Status == DungeonRewardLedgerStatus.Committed
                && (await store.GetCharactersAsync("beta"))[0].Gold == paidCharacter.Gold - 37,
                "replaying the paid key never charges twice");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
