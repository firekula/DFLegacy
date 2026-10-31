# 副本奖励幂等存档提交实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为副本奖励建立同一 JSON 快照内的幂等提交、明确拒绝、未知提交和恢复隔离边界，之后再安全接入组队翻牌。

**Architecture:** 扩展 `JsonGameStoreState` 增加 `DungeonRewardLedger`；新增专用 `CommitDungeonRewardAsync`，在 store `_gate` 下完成业务校验、候选快照构造和 ledger 记录，只有正式快照替换成功后才安装内存状态。写入阶段异常进入 `Unknown` 和写入隔离，禁止自动重试和旧内存覆盖。翻牌 `EntranceService` 接线不在本计划内。

**Tech Stack:** C#/.NET 10、`JsonGameStore`、System.Text.Json、现有 `DFLegacy.SmokeTests`、隔离 TEMP/TMP 测试运行。

**Spec:** `docs/superpowers/specs/2026-10-02-dungeon-reward-idempotent-commit-design.md`

## Global Constraints

- 同一奖励 key 重放不能重复修改账户；payload fingerprint 不一致必须拒绝。
- 明确业务拒绝与写入阶段未知必须分离；未知不得自动重试。
- ledger 与账户状态必须位于同一 JSON 快照，不能第二文件双写。
- 不持有 `DungeonInstance.Gate` 跨 await；store `_gate` 只在专用提交路径内协调候选快照。
- 不修改翻牌 `EntranceService` 接线、普通掉落、Special Item Routing 或单人协议路径。
- 不修改文件权限；默认 Temp 失败时使用进程级 TEMP/TMP 仅作为测试基础设施并分别记录。
- 未经崩溃/掉电实验，不宣称 durable flush 或 OS 掉电 exactly-once。

---

## 文件分工

- Modify `src/DFLegacy.Server/JsonGameStore.cs`：ledger 状态、请求/结果、候选快照提交、恢复隔离。
- Modify `src/DFLegacy.Server/JsonGameStoreState.cs` 或其实际定义文件：若状态根对象不在 store 文件中，则在实际定义处增加 ledger 字段和旧存档默认值。
- Create `tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs`：纯 store 提交、重放、异常注入、恢复测试。
- Modify `tests/DFLegacy.SmokeTests/Program.cs`：增加 `--dungeon-reward-commit` 定向入口。
- Modify `tests/DFLegacy.SmokeTests/` existing fixture helpers only when needed to create isolated store paths; do not change unrelated AdminAuth assertions.
- Documentation: append actual evidence and any deferred recovery limitation to `knowledge/decisions/2026-10-02-dungeon-reward-idempotent-commit.md`.

---

### Task 1: 定义 ledger、请求、结果和迁移边界

**Files:**
- Modify actual `JsonGameStoreState` definition.
- Modify `src/DFLegacy.Server/JsonGameStore.cs`.
- Create `tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs`.
- Modify `tests/DFLegacy.SmokeTests/Program.cs`.

**Interfaces:**

```csharp
public enum DungeonRewardLedgerStatus : byte
{
    Committed,
    Rejected,
    Unknown
}

public sealed record DungeonRewardLedgerEntry(
    string IdempotencyKey,
    string PayloadFingerprint,
    Guid CharacterId,
    long DungeonGeneration,
    byte PartySlot,
    byte CardType,
    byte CardColumn,
    DungeonRewardLedgerStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ushort? RewardItemId,
    int GoldDelta,
    string? RejectionCode,
    string? UnknownReason);

public sealed record DungeonRewardCommitRequest(
    string IdempotencyKey,
    string PayloadFingerprint,
    string UserName,
    Guid CharacterId,
    long DungeonGeneration,
    byte PartySlot,
    byte CardType,
    byte CardColumn,
    int TargetGold,
    IReadOnlyList<CharacterItemRecord> PlannedInventory,
    IReadOnlyList<CharacterItemRecord> PlannedEquipment,
    IReadOnlyList<CreateCharacterMailRequest> GeneratedMails,
    ushort? WarehouseCapacity,
    ushort? ExpectedWarehouseCapacity);

public readonly record struct DungeonRewardCommitResult(
    DungeonRewardLedgerStatus Status,
    CharacterRecord? UpdatedCharacter,
    ushort? RewardItemId,
    int GoldDelta,
    string? RejectionCode,
    string? UnknownReason);
```

- [ ] **Step 1: Add red tests for old-save migration and result shape**

Create a temporary store JSON without `DungeonRewardLedger`, load it, and assert it behaves as an empty ledger. Assert enum/result serialization does not require a second file.

- [ ] **Step 2: Run directed test to confirm RED**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --dungeon-reward-commit
```

Expected: FAIL because ledger types/field/entry point do not exist.

- [ ] **Step 3: Add ledger types and old-save defaulting**

Add the types at the project’s existing model boundary. Missing JSON field must deserialize as `[]`; do not rewrite old saves during read-only load.

- [ ] **Step 4: Add directed test entry and run GREEN for migration only**

Expected: `Dungeon reward commit smoke: 0 failures` for migration/result shape.

- [ ] **Step 5: Commit**

```powershell
git add src/DFLegacy.Server tests/DFLegacy.SmokeTests
git commit -m "存档：增加副本奖励账本模型"
```

---

### Task 2: Implement deterministic business validation and candidate snapshot

**Files:**
- Modify `src/DFLegacy.Server/JsonGameStore.cs`.
- Modify `tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs`.

**Interfaces:**
- Produces `CommitDungeonRewardAsync(DungeonRewardCommitRequest, CancellationToken)`, but this task may keep the snapshot writer internal.
- A valid request must produce a candidate state without mutating live `_accounts` until the writer succeeds.

- [ ] **Step 1: Add RED tests for explicit rejection**

Cover:

```text
missing account
missing character
pending trade delivery
warehouse expected-capacity conflict
invalid negative/overflow gold
same key + different fingerprint
```

Each test reloads the store and proves account inventory/gold are unchanged.

- [ ] **Step 2: Implement ledger lookup and validation**

Under `_gate`:

```text
validate key/fingerprint
lookup key
Committed + same fingerprint -> return committed
Committed/Rejected/Unknown + mismatch -> rejected mismatch
Unknown -> return Unknown, no retry
run account/character/trade/capacity validation
```

Do not write a committed ledger entry for a failed validation. If a rejected ledger entry is retained, it must not mutate account state and must be deterministic on replay.

- [ ] **Step 3: Build candidate account + committed ledger snapshot**

Clone the relevant state graph before changing it. Add a single `DungeonRewardLedgerEntry(Committed)` to the candidate snapshot. Do not call the existing `SaveLockedAsync` if it mutates `_accounts` before writing; introduce an internal snapshot writer that accepts a complete candidate state.

- [ ] **Step 4: Run explicit rejection and candidate tests**

Expected: all rejection tests pass and no live memory changes occur before commit.

- [ ] **Step 5: Commit**

```powershell
git add src/DFLegacy.Server/JsonGameStore.cs tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs
git commit -m "存档：实现副本奖励请求校验与候选快照"
```

---

### Task 3: Implement committed result and idempotent replay

**Files:**
- Modify `src/DFLegacy.Server/JsonGameStore.cs`.
- Modify `tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs`.

- [ ] **Step 1: Add RED tests for first commit and replay**

Use one deterministic request and assert:

```text
first call -> Committed
account inventory/gold changes once
ledger has one committed entry
same key+fingerprint -> Committed
second call does not call snapshot writer again
second call does not duplicate item/mail/gold
returned CharacterRecord matches committed state
```

Expose a test-only writer counter/delegate at the store fixture boundary; do not add production test hooks.

- [ ] **Step 2: Install candidate only after snapshot replacement succeeds**

The internal sequence must be:

```text
candidate = clone + apply account + ledger
write candidate temp
replace official store path
install candidate into live memory
return Committed
```

If the writer returns normally, update in-memory `_accounts` and ledger exactly once.

- [ ] **Step 3: Implement fingerprint mismatch behavior**

Same key with a different fingerprint returns `Rejected("idempotency-payload-mismatch")` and leaves both disk and memory untouched.

- [ ] **Step 4: Run replay and mismatch tests**

Expected: first commit changes once; replay is side-effect free; mismatch is rejected.

- [ ] **Step 5: Commit**

```powershell
git add src/DFLegacy.Server/JsonGameStore.cs tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs
git commit -m "存档：完成副本奖励幂等提交与重放"
```

---

### Task 4: Implement Unknown state and write quarantine

**Files:**
- Modify `src/DFLegacy.Server/JsonGameStore.cs`.
- Modify all account-mutating store entry points only through a narrow shared quarantine guard.
- Modify `tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs`.

- [ ] **Step 1: Add failure-injection RED tests**

Inject failures at:

```text
serialization before temp close
File.Move before replacement
File.Move after replacement but before caller receives normal result
cancellation during write
```

Expected: `Unknown`, no automatic retry, reservation remains an external caller concern, and future ordinary account writes are blocked until recovery.

- [ ] **Step 2: Add `storeWriteQuarantined` guard**

When write-stage uncertainty occurs:

```text
storeWriteQuarantined = true
ledger in memory is not guessed as Committed
all account-mutating APIs return explicit quarantine failure
```

Do not silently return the old `null` shape from the new API. Existing APIs may map quarantine to their existing failure result only after logging the quarantine reason.

- [ ] **Step 3: Add explicit recovery operation**

Under `_gate`, reload official JSON, inspect ledger key:

```text
Committed -> install official state, clear quarantine
Rejected -> install official state, clear quarantine
Unknown/no entry -> remain quarantined and require explicit operator decision
```

`.tmp` existence alone must never clear quarantine.

- [ ] **Step 4: Run failure injection and recovery tests**

Cover replacement-before and replacement-after uncertainty separately. Assert no automatic retry and no stale-memory overwrite.

- [ ] **Step 5: Commit**

```powershell
git add src/DFLegacy.Server/JsonGameStore.cs tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs
 git commit -m "存档：处理副本奖励未知提交与写入隔离"
```

---

### Task 5: Restart migration and final store verification

**Files:**
- Modify `src/DFLegacy.Server/JsonGameStore.cs` only for load/upgrade behavior.
- Modify `tests/DFLegacy.SmokeTests/DungeonRewardCommitSmokeTests.cs`.
- Create `knowledge/decisions/2026-10-02-dungeon-reward-idempotent-commit.md`.

- [ ] **Step 1: Add restart RED tests**

Create separate store instances against the same temporary path:

```text
committed ledger -> replay after restart has no new write
rejected ledger -> original rejection retained
unknown ledger -> no automatic grant
missing ledger field -> old save loads normally
.tmp only -> not treated as committed
```

- [ ] **Step 2: Implement load normalization**

Deserialize missing ledger as empty. Keep `Unknown` entries visible for recovery; never convert them to committed automatically.

- [ ] **Step 3: Run restart tests in isolated TEMP/TMP**

```powershell
$tmp=Join-Path (Get-Location) '.tmp\\reward-commit'; New-Item -ItemType Directory -Force -Path $tmp | Out-Null; $env:TEMP=$tmp; $env:TMP=$tmp; dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --dungeon-reward-commit
```

- [ ] **Step 4: Run server build**

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-incremental
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Record evidence and limitations**

Write actual test output, commit hashes, failure injection scope, and the explicit limitation that OS crash/power-loss durability remains `[未验证]`.

- [ ] **Step 6: Commit**

```powershell
git add src/DFLegacy.Server tests/DFLegacy.SmokeTests knowledge/decisions/2026-10-02-dungeon-reward-idempotent-commit.md
git commit -m "存档：完成副本奖励账本恢复验证"
```

---

## Task 2 unblocking gate

Only after Tasks 1–5 pass review may the earlier party-card Task 2 be re-dispatched. It must then consume `CommitDungeonRewardAsync`; it may not reintroduce `SaveCharacterInventoryAsync` for shared party rewards or infer commit status from `null`/exception.

## Plan self-review

- No implementation placeholders remain; every task has exact files, API, tests, and commands.
- Store commit and ledger are same-snapshot; unknown writes are quarantined, not retried.
- No translation of unknown into rejected or committed.
- No changes to ordinary loot, routing, or solo packet paths.
- Durable power-loss semantics are explicitly excluded and remain unverified.
