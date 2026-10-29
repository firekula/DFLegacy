# Active Drop Four-Client Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for root implementation. Independent native-contract research and server-only regression coverage may run in parallel; root exclusively owns live operation. One independent final review before completion.

**Goal:** Complete the next U04 group with deterministic four-member tests and data-driven native discard acceptance.

**Architecture:** Reuse the gated DLTC112/lease architecture and authentic ACT1 CMD50 frontend function. Server rules and persistence stay authoritative; native ACK, source inventory changes, new ground and final recipient persistence are separate checks.

**Tech Stack:** C++ Win32, C# .NET10, Python standard-library CLI.

**Spec:** `docs/superpowers/specs/2026-10-29-active-drop-four-design.md`.

## Global constraints

- Existing worktree/branch; normal state E0E2FB4B… and original resources unchanged.
- Never write either sealed `.tmp/test-control-20261029` directory.
- No implicit screenshots; no external gameplay packets or arbitrary memory writes.
- Default off, fresh lease, exact PID/start/UID/run/room and inventory-object binding.
- All four test clients arranged A/B over C/D on startup; skillbar persistence stays last.

## Review focus

1. Slot contents replaced/reordered after observation: refuse the obsolete object, even when ID matches.
2. Native pending, missing item, bad quantity, unsupported container or UI policy: no call.
3. Request dispatched but server rejects or room changes: no false successful discard.
4. Concurrent claim and old room publication: exactly one persisted owner, no outsider leakage.
5. Partial native snapshot or file replacement: fail closed or bounded retry, never merge epochs.

### Task 1: Four-member managed regressions

**Files:** new `tests/DFLegacy.SmokeTests/ActiveDropFourClientSmokeTests.cs`, `Program.cs`; production only on demonstrated failure.

- [x] Build shuffled UID/party-slot four-member fixtures and exact original item metadata cases.
- [x] Cover reverse transfers, seal/quantity refusal, duplicate claim, room/instance isolation and 23-gold conservation.
- [x] Run explicit Release focused and relevant regressions; existing passing behavior is added coverage, not fabricated RED.

### Task 2: Native bounded discard

**Files:** `src/DFLegacy.Debug/src/client_test_control*`, native tests and project files if a focused helper is needed.

- [x] Freeze original PE caller/function/ABI and secure container/item fields.
- [x] Write failing op5 validation and inventory binding tests; implement only the verified original function path.
- [x] Export bounded inventory observation and distinguish request execution from server mutation.
- [x] Build Release and run relevant native tests.

### Task 3: CLI and integration

**Files:** `tools/debug/test_control.py`, `tests/test_debug_test_control.py`, README.

- [x] RED tests for inventory snapshot freshness/binding, op5 packing, source count and final source/ground checks.
- [x] Implement `inventory drop --uid --slot --item --count`, preserving partial evidence and refusing obsolete epochs.
- [x] Focused Python and managed/native integration checks; update usage.

### Task 4: Isolated four-client acceptance and closeout

**Files:** new private runtime, bounded `knowledge/decisions` report/assets and backlog.

- [x] Choose real PVF blue/sealed items, prepare capacity through authorized native actions, bind four processes and deploy only the isolated runtime.
- [x] Validate native discard source persistence + four-way ground sync, then four-direction recipient commit and metadata.
- [x] Capture key UI only, independently review, stop owned processes, restore configs, verify normal hashes and publish exact limits.

## Execution ledger

- Documentation catch-up committed as `370aec8`; previous group remains `7f8ddf5`.
- Ruling: U04 is next because it is the earliest incomplete user-list item; new overload-unit observation and skillbar remain later.
- Ruling: extend the existing gated command with a normal native discard function, not a raw packet endpoint or direct save edit; this preserves the authorized data-driven test workflow.

- 完成：12次装备交接、材料与金币守恒、四活人换房返回；首轮准备失败与第二轮一次陈旧读取保留。报告 `knowledge/decisions/2026-10-30-active-drop-four-verification.md`，非零属性／拒绝／并发仅自动回归范围。独立审查的partial归类Important已修复复验。两轮进程关闭、配置恢复、正常存档保持；完整Python6项既有失败未隐藏。
