# Task 11 目标交接 · 终局（Tier 2/Tier 3 双客户端验收）（2026-10-22）

> 本文件取代 Task 9/10 完成交接作为**当前视图**。
> 最终目标：**用官方形状的拆分架构，让两个客户端完美联机**——这一步是 Tier 2（单体/拆分解密后内容逐字节一致）
> + Tier 3（真机双客户端进城）。本波**必须用户在场**（起客户端 + UI 动作 + 解决 2311 端口）。

## 1. 基线

- HEAD `36850a6`（I5 前半完成交接）。工作区他人的未提交改动（`src/`、`net_dump.cpp`、`STATUS.md` 等）**绝不动**。
- 本波触达：`tools/acceptance/`（orchestrate 双进程接线）、`docs/`（runbook）、`server.json`（Tier 2 临时切换，验完改回）、`STATUS.md`（结论）。

## 2. 进波先办的 4 件事（已写进计划 Task 11 段「现行 Step 0」）

| # | 事项 | 谁 | 状态 |
|---|---|---|---|
| 1 | **D10-ENV-2311：2311 仍被 Windows 排除段 2280–2379 占住，bind 恒 10013** | **用户** | **阻塞中，需重启/管理员 netsh** |
| 2 | orchestrate 只起 game、不起 channel（`cli.py:184`）→ 拆分档跑不起来 | Lead 派发小实现 | 待派发 |
| 3 | Tier 2 对比口径 = 解密后明文（不是逐字节比密文，随机会话密钥） | Lead 落裁定 | 已进计划 |
| 4 | net_dump 构建/输入版本固定（D5-REAL） | 取样前 | 待办 |

## 3. 关键裁定（Task 11 刷新，已进计划）

- **Tier 2 比解密后明文**：握手帧用随机会话密钥加密（Task 4 实录 4 密钥同明文），原始密文两档必然不同。
  复用 Task 5 I2 口径：`11/5/9/1 → 12/6/10/3`，解密/解压后对协议 10/3/6 逐字节比明文；原始帧只比结构。
- **orchestrate 双进程接线是 Task 11 第一个实现步骤**（Task 10 只给了原语、没接 orchestrate）。
- **验收平台「全绿」= 121 passed**（D10-ENV-2311 解决后），当前是 118 passed + 3 个已归因环境红。

## 4. 派发顺序

1. 先派 `impl-task-11-wiring`：orchestrate 拆分模式起 channel+game、分别等就绪 + `test_multi_server.py` 扩展用例。
   这一步不依赖 2311（测试用假服务端 + 空闲端口），可与用户重启并行。
2. 用户重启后：Lead 复跑 `pytest acceptance/tests -q` 确认 121 passed（环境恢复的验证）。
3. 写 runbook（解密后明文对比脚本 + Tier 3 真机流程），`git add -f` 提交。
4. **真机双客户端 run**：Lead 与用户配合（起服务端/双客户端 → UI 选角/进城 → 归集 netdump → 判定）。
5. 改回 `server.json`、跑全量回归（dotnet smoke + protocol verify + acceptance pytest）、更新 STATUS.md、提交。

## 5. 门槛（本波）

| 命令 | 期望 |
|---|---|
| `python -m pytest acceptance/tests -q` | **121 passed**（D10-ENV-2311 解决后） |
| `dotnet run --project tests/DFLegacy.SmokeTests` | 0 failures |
| `python -m protocol.s1 verify` | 5/5 PASS |
| Tier 2 两档对比 | 解密后协议 10/3/6 逐字节一致（runbook 脚本） |
| Tier 3 真机 | 双客户端进城 + 同屏（标 `[真机]`） |

## 6. 下一步只读

本文件 §2–§5、计划 `## Task 11:` 段（已刷新）、`progress.md` Task 9/10 段、`tools/acceptance/cli.py`/`runner.py`。
