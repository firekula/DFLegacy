# Store 统一写事务入口实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `JsonGameStore` 的账户写入统一到可 quarantine 的候选快照事务边界，使副本奖励 Unknown 状态不会被任何旧内存写入口覆盖。

**Architecture:** 保留现有 `_gate` 作为唯一 store 状态锁；新增统一写事务包装器，在锁内检查 quarantine、复制/规划候选状态、锁外执行文件写入、成功后重新安装内存。所有账户写入口通过该边界进入；读取入口不受 quarantine 影响，专用恢复入口可在锁内读取正式 JSON 并按 ledger 决定解除隔离。副本奖励 ledger/翻牌接线不在本计划内。

**Tech Stack:** C#/.NET 10、System.Text.Json、`JsonGameStore`、现有 `DFLegacy.SmokeTests`、隔离 TEMP/TMP 测试。

**Spec:** `docs/superpowers/specs/2026-10-02-dungeon-reward-idempotent-commit-design.md`

## Global Constraints

- Unknown 写入后，任何旧内存账户写入口不得继续写盘。
- 不以 `.tmp` 文件存在判断提交成功；只有正式 JSON ledger 可证明 `Committed`。
- Store `_gate` 不跨 await 持有；实例 `DungeonInstance.Gate` 不参与 store 写事务。
- 写入失败不得静默转换为成功；明确拒绝和 Unknown 分开。
- 不修改翻牌接线、普通掉落、Special Item Routing 或单人协议。
- 不修改文件权限；默认 Temp 失败时使用进程级 TEMP/TMP 测试并记录。
- 未经崩溃/掉电实验，不声称 durable flush 或 exactly-once durability。

---

## 文件分工

- Modify: `src/DFLegacy.Server/JsonGameStore.cs`：统一候选快照写事务、quarantine 状态、恢复入口、写入口迁移。
- Modify: `src/DFLegacy.Server/DungeonRewardLedgerModels.cs`：仅在需要时补充事务状态/恢复结果类型，不重复定义 ledger。
- Create: `tests/DFLegacy.SmokeTests/StoreWriteTransactionSmokeTests.cs`：统一写边界、quarantine、恢复和代表性入口测试。
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`：增加 `--store-write-transaction` 定向入口。
- Create/Modify: `knowledge/decisions/2026-10-02-store-write-transaction.md`：记录覆盖入口清单、未覆盖入口和证据边界。

---

### Task 1: 建立统一候选快照写事务和 quarantine 核心

**Files:**
- Modify: `src/DFLegacy.Server/JsonGameStore.cs`
- Create: `tests/DFLegacy.SmokeTests/StoreWriteTransactionSmokeTests.cs`
- Modify: `tests/DFLegacy.SmokeTests/Program.cs`

**Interfaces:**

```csharp
internal enum StoreWriteOutcomeStatus
{
    Committed,
    Rejected,
    Unknown
}

internal readonly record struct StoreWriteOutcome(
    StoreWriteOutcomeStatus Status,
    string? Reason = null);

internal sealed class StoreWriteQuarantinedException : InvalidOperationException;

internal Task<StoreWriteOutcome> ExecuteCandidateWriteAsync(
    Func<JsonGameStoreState, StoreMutationResult> mutation,
    CancellationToken cancellationToken = default);

internal Task<StoreRecoveryResult> RecoverWriteQuarantineAsync(
    string? idempotencyKey = null,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: 写 RED 测试**

覆盖：

```text
正常候选写入 -> Committed + 内存安装
writer 在替换前失败 -> Unknown/quarantine
writer 模拟替换后异常 -> Unknown/quarantine
quarantine 下第二次写入口被拒绝且内存不变
读取入口仍可读
```

测试 writer 必须是 internal 注入 seam，不改公开生产 API；fixture 使用 worktree TEMP/TMP。

- [ ] **Step 2: 运行定向测试确认失败**

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --store-write-transaction
```

Expected：FAIL，因为统一事务、异常状态和 quarantine 尚不存在。

- [ ] **Step 3: 实现候选状态事务**

实现固定顺序：

```text
_gate 内检查 quarantine
→ 深复制当前状态
→ mutation 只改 candidate
→ 锁内完成确定性校验
→ 锁外 snapshot writer 写临时文件并替换正式 JSON
→ writer 正常返回后重新获取 _gate
→ 安装 candidate 内存
→ 返回 Committed
```

若 mutation 明确拒绝且未进入写入阶段，返回 Rejected；若进入写入阶段后异常，设置 quarantine 并返回 Unknown。禁止在 candidate writer 期间持有 `_gate`。

- [ ] **Step 4: 实现防御性 quarantine 检查**

所有统一写入口必须在任何 `_accounts` 修改前调用同一 guard；`SaveLockedAsync` 不能作为唯一 guard，因为写入前内存可能已经被旧入口修改。读取入口不调用 guard。

- [ ] **Step 5: 运行 RED→GREEN 与 mutation**

临时移除 quarantine guard，定向测试必须失败；恢复后：

```text
Store write transaction smoke: 0 failures
server Release build: 0 warnings, 0 errors
```

- [ ] **Step 6: 提交**

```powershell
git add src/DFLegacy.Server/JsonGameStore.cs tests/DFLegacy.SmokeTests/Program.cs tests/DFLegacy.SmokeTests/StoreWriteTransactionSmokeTests.cs
git commit -m "存档：建立统一候选快照写事务边界"
```

---

### Task 2: 迁移高风险账户写入口

**Files:**
- Modify: `src/DFLegacy.Server/JsonGameStore.cs`
- Modify: `tests/DFLegacy.SmokeTests/StoreWriteTransactionSmokeTests.cs`

**Interfaces:**
- Consumes: Task 1 `ExecuteCandidateWriteAsync` and quarantine guard.
- Produces: migrated character/inventory/gold/mail/trade write paths with no direct `_accounts` mutation outside transaction.

- [ ] **Step 1: 盘点并分类全部写入口**

在 `JsonGameStore.cs` 建立静态清单，分类：

```text
角色/账号创建删除
库存/装备/仓库/金币
邮件/奖励
交易
技能/经验/任务/副本/疲劳/位置
商城/现金/点券/钻石/会员
好友/黑名单/社交
```

每个入口标记：是否修改 `_accounts`、是否调用 `SaveLockedAsync`、是否已有原子事务。

- [ ] **Step 2: 迁移库存/金币/邮件/奖励相关入口**

优先迁移：

```text
SaveCharacterInventoryAsync
SaveCharacterItemSpacesAsync
SaveCharacterCeraShopPurchaseAsync
CommitCompoundItemAsync
邮件领取/奖励落库入口
```

在 mutation 前 guard；候选状态通过统一 writer 保存；成功后才更新内存。

- [ ] **Step 3: 增加代表性 quarantine 测试**

Unknown 后分别调用：

```text
库存写入
金币/商城写入
邮件或奖励写入
```

每个入口必须拒绝且不能改变正式 JSON 或 live memory。

- [ ] **Step 4: 迁移交易入口**

保留交易已有原子语义，但接入统一 quarantine guard；交易成功前后分别验证：

```text
正常情况下仍一次提交
Unknown 后拒绝新交易提交
不会使用旧内存覆盖奖励 JSON
```

- [ ] **Step 5: 运行定向测试和完整 server build**

```powershell
$tmp=Join-Path (Get-Location) '.tmp\\store-write'; New-Item -ItemType Directory -Force -Path $tmp | Out-Null; $env:TEMP=$tmp; $env:TMP=$tmp; dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --store-write-transaction
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-incremental
```

- [ ] **Step 6: 提交**

```powershell
git add src/DFLegacy.Server/JsonGameStore.cs tests/DFLegacy.SmokeTests/StoreWriteTransactionSmokeTests.cs
git commit -m "存档：迁移高风险账户写入口"
```

---

### Task 3: 迁移剩余写入口并完成恢复

**Files:**
- Modify: `src/DFLegacy.Server/JsonGameStore.cs`
- Modify: `tests/DFLegacy.SmokeTests/StoreWriteTransactionSmokeTests.cs`
- Create: `knowledge/decisions/2026-10-02-store-write-transaction.md`

- [ ] **Step 1: 迁移角色、任务、技能、副本、位置、社交、商城入口**

逐类将所有会修改 `_accounts` 的入口接入统一事务；每迁移一类，更新覆盖清单和一条 quarantine 测试。只读查询不得无故进入事务。

- [ ] **Step 2: 完成恢复入口**

实现：

```text
重新读取正式 JSON
→ 如果目标 ledger 为 Committed：安装正式状态、清除 quarantine
→ 如果没有 Committed 或状态 Unknown：保持 quarantine、返回人工恢复所需原因
→ .tmp 不作为成功证据
```

恢复入口本身允许读正式 JSON 和安装状态，但不得自动授予未知奖励。

- [ ] **Step 3: 重启与覆盖测试**

覆盖：

```text
Committed ledger 重启后可安全重放
Unknown ledger 重启后不能自动发奖
仅有 .tmp 不解除 quarantine
所有已盘点写入口在 quarantine 下拒绝
恢复成功后代表性入口恢复可写
```

- [ ] **Step 4: 生成覆盖报告**

`knowledge/decisions/2026-10-02-store-write-transaction.md` 必须列出：

```text
已迁移入口
每类代表性测试
未迁移入口及原因
Unknown/恢复证据等级
掉电 durability 未验证声明
```

- [ ] **Step 5: 运行最终验证**

```powershell
$tmp=Join-Path (Get-Location) '.tmp\\store-write'; New-Item -ItemType Directory -Force -Path $tmp | Out-Null; $env:TEMP=$tmp; $env:TMP=$tmp; dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --store-write-transaction
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-incremental
```

完整 smoke 默认 Temp 若被现有 ACL 阻断，记录失败位置并使用隔离 TEMP/TMP 重跑；不改权限。

- [ ] **Step 6: 提交**

```powershell
git add src/DFLegacy.Server tests/DFLegacy.SmokeTests knowledge/decisions/2026-10-02-store-write-transaction.md
git commit -m "存档：完成统一写事务入口与恢复验证"
```

---

## 完成门槛

在统一事务 Task 1–3 全部评审通过前，不得重新接入翻牌 Task 2。通过后仍需单独评审副本翻牌与 Store 的接口，不得把本计划自动视作翻牌完成。

## 计划自审

- 覆盖了 Task 3 暴露的“约 40 个入口”问题，而不是只保护库存保存函数。
- Unknown guard 在内存 mutation 前和 writer 后均有约束。
- 恢复基于正式 JSON ledger，不信任 `.tmp`。
- 测试包含库存、金币/商城、邮件/奖励、交易和恢复；掉电 durability 明确未验证。
- 没有把翻牌、普通掉落或 routing 混入本计划。
