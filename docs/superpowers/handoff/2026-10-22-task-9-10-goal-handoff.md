# 下一波目标交接 · I5 前半（Task 9 + Task 10）（2026-10-22）

> 本文件是「目标设定」交接：当前视图，取代 Task 8 交接的「下一波只读」。
> 最终目标不变：**用官方形状的拆分架构（channel 进程 + game 进程 + 官方形状链路），让两个客户端完美联机**。
> I5 = Task 9（S2S 报文表）+ Task 10（验收平台多进程）+ Task 11（Tier 2/Tier 3 双客户端验收）。
> 本波只做 **Task 9 + Task 10**；Task 11（真机双客户端）作为紧随其后的收尾波。

## 1. 基线、目标与范围

- 仓库 `G:/Game/DNF_ACT1/DFLegacy`，分支 `main`，本波 BASE = `34b9dd1`（Task 8 提交，I4 完成）。
- 工作区有**他人并行改动**（`EntranceService.cs`、`net_dump.cpp`、`STATUS.md`、`src/DFLegacy.Protocol/*`、
  `tests/DFLegacy.SmokeTests/*` 等）与未跟踪文件。**本波只碰 `protocol/` 与 `tools/acceptance/` 两个目录，
  绝不动他人的改动。** 提交一律逐文件 pathspec，绝不 `git add -A`。
- 目标：**打通 I5 前半的「验收能力」** —— S2S 报文进可自检台账 + 验收平台能编排多进程，
  为 Task 11 的「两档逐字节对比 + 真机双客户端」铺路。
- `protocol/` 与 `tools/acceptance/` 是普通受跟踪目录（不在 `.gitignore`），`git add <路径>` 正常。

## 2. 计划刷新裁定（本波已落进计划，实现者以它为准）

计划 `docs/superpowers/plans/2026-10-19-s2-multiprocess-split-first-slice.md` 的 Task 9 / Task 10 段
已从 `needs-refresh` 刷成「2026-10-22 刷新裁定」，草案代码保留作历史。核心裁定：

### Task 9（S2S 台账）
- **S2S 独立成 `map/s2s-map.tsv`，不并入 `opcode-map.tsv`，`opcodes_total` 保持 1171**；新增独立基线 `s2s=4`。
  理由：`_check_baselines`/`build_manifest` 只从 opcode-map 计数；混入会污染官方六档/status/ext_mark、
  且 `import_map` 下次会把无原始来源的 S2S 静默删掉、`_check_internal` 的 opcode-map==ledger 对称被打破。
- **第 6 个受跟踪文件的下游清单**：`render.TRACKED`、`verify.TRACKED_FILES`、`verify.KEY_COLUMNS`、
  `verify.EXPECTED_COLUMNS`、`_check_baselines`（独立 s2s 计数）、`render.build_manifest`（s2s 基线）、
  `test_verify.py` 的 `_build` 夹具 + `test_manifest_must_list_exactly_the_five_tracked_files`（改 6 文件）、
  **`test_render.py` 的 `_base` 夹具 + `len(files)==5→6`**（2026-10-22 实施回填：初稿漏列，不修则 pytest 6 红）。
  `test_baseline_total_is_pinned_to_1171` **不变**。
- **S2S 不进 importer 流水线**（人工维护台账，无官方 `.rodata`）；**NOTICE_CHANNEL_SERVER 只登记不接线**（D7-NOTICE）。

### Task 10（验收平台多进程）
- `server` → `servers` 重命名：`model.py:99`、`model.py:120`、`runner.py:438` 三处 + `grep` 扫清调用点。
- `SERVER_SPECS` 端口**从 server.json 派生**（不是硬编码），disjoint 判据按 **(host,port,protocol)**。
- 多进程就绪**分别等各自端点**（channel 等 Entrance、game 等 Channel.Port），不能只等 Entrance。
- `judge` 的 `server_dir` 要**真的被读**（`evidence=[静态]` 的服务端侧期望从 `server-*.log` 搜 pattern）。

## 3. 派发（两个独立子代理并行）

- **impl-task-9**：只碰 `protocol/`。写红→实现→render→verify 5/5→`pytest protocol -q` 全绿→逐文件提交。
- **impl-task-10**：只碰 `tools/acceptance/`。写红→实现→`pytest acceptance/tests -q` 全绿→逐文件提交。
- 两任务目录不相交、无共享状态，可并行；写域已登记在共享任务板上。

## 4. 门槛（各自执行）

| 任务 | 命令 | 期望 |
|---|---|---|
| Task 9 | `python -m protocol.s1 render` 后 `python -m protocol.s1 verify` | 5/5 PASS（含 s2s 基线） |
| Task 9 | `python -m pytest protocol -q` | 全绿 |
| Task 10 | `(cd tools) python -m pytest acceptance/tests -q` | 全绿 |

## 5. 遗留项当前视图（本波不改变，仅承接）

单一来源：`.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/progress.md` 末「Task 8」段。
本波（Task 9/10）会消费/推进的 open 项：
- **D7-NOTICE** → Task 9 只登记 4 条 S2S 报文到台账，**不**让服务端真发 NOTICE（仍 open，归 Task 11 真机）。
- **D5-REAL / D5-RAW-COMPARE** → Task 11 真机 + Tier 2 原始帧对比，本波不碰（仍 open）。
- 其余 D5-*/D6-*/D7-*/D8-* 状态不变。

## 6. 下一波（Task 11）只读这些

1. 本文件 §2 的裁定、计划 Task 11 段（届时仍需刷新「原始帧对比的例外/归一化」）。
2. `protocol/`（Task 9 落盘后的 s2s-map.tsv + manifest）与 `tools/acceptance/`（Task 10 落盘后的多进程编排）。
3. `progress.md` 末段（本波完成后的 ledger 追加）。
