# Task 9 + 10 完成交接 · I5 前半（2026-10-22）

> 当前视图。本波目标 = 打通「两个客户端完美联机」的验收能力（S2S 台账 + 多进程验收平台）。
> Task 11（Tier 2/Tier 3 真机双客户端）是下一波，进入前有三件必办（见 §6）。
> 换会话续跑：先读本文件 §2–§5，再按标题定位计划的 `## Task 11:` 段。

## 1. 基线与提交链

- 分支 `main`。本波 BASE `51f5d83`（计划刷新）→ HEAD `8aadfda`。
- `6aeec02` Task 9（protocol/ 7 文件，+49/−16）。
- `98499e9` Task 10（tools/acceptance/ 10 文件，+291/−34）。
- `893383f` / `8aadfda` 两次收尾提交（计划裁定回填 + ledger）。
- 工作区他人的未提交改动（`src/`、`STATUS.md`、`BUILDING.md` 等）**全程未触碰**，仍是 ` M` 状态。
- 计划/交接/ledger 均已提交（`git add -f`，逐文件 pathspec）。

## 2. 本波交付

| 文件 | 内容 |
|---|---|
| `protocol/map/s2s-map.tsv` | 4 条 S2S 报文（4/128/129/130），列序 = MAP_COLUMNS |
| `protocol/verify.py` / `render.py` | 第 6 个受跟踪文件 + **独立** `s2s=4` 基线；`opcodes_total` 保持 **1171** |
| `protocol/tests/test_verify.py` / `test_render.py` | 夹具与契约断言同步 6 文件 |
| `tools/acceptance/model.py` | `Manifest.server` → `servers`（逐进程名记账，不覆写） |
| `tools/acceptance/runner.py` | `SERVER_SPECS`/端点解析从 server.json 派生（含 GameplayDatagram、LinkPort、AdditionalPorts）；逐进程预检；拆分模式分别等 channel(Entrance)/game(Channel.Port) |
| `tools/acceptance/verdict.py` | `server_dir` 真读：`evidence=[静态]` 从 `server-*.log` 搜 pattern |
| `tools/acceptance/cli.py` | orchestrate 逐进程日志 `server-game.log` |
| `tools/acceptance/scenarios/handshake-login-town.yaml` | 本刀门场景（3 条 `[存在]`/`[抓包]`/`[静态]` + 1 条 `[语义]`） |
| `tools/acceptance/tests/test_multi_server.py` | 4 新用例（先红后绿） |

### 关键实现口径（Task 11 不要改错）

- **S2S 是孤立 kind**：不进 `opcode-map.tsv`、不进 `ledger/opcodes.tsv`、不进 importer 流水线；
  `_KIND_ORDER`/`BASELINE_OPCODES_TOTAL` 未动；`render_index` 不读 s2s-map（index.md 字节不变属预期）。
- **disjoint 判据按 (host,port,protocol)**：2311 在 game 是 UDP、channel 是 TCP，合法共存。
- **逐进程 manifest**：`mf.servers[name]` 按进程名写，`server`（单数）键已消灭（grep 0 命中）。

## 3. 本波 Ruling（每条带代价）

1. **S2S 独立文件 + 独立基线**（取代草案「1171→1175」）：`_check_baselines`/`build_manifest` 只读 opcode-map，
   混入会污染官方六档/status/mark，且 `import_map` 下次会静默删掉无原始来源的 S2S。
   **代价**：verify/render 各加一段孤立计数逻辑。
2. **裁定清单两处漏项（实施发现，已回填计划）**：Task 9 漏 `test_render.py`（不修 pytest 6 红）；
   Task 10 漏 4 个既有测试（`test_model`/`test_cli_dry_run`/`test_cli_orchestrate`/`test_runner`，不交则提交即红）。
   **代价**：两个提交的文件数都比裁定 git add 清单多，复审面略大。
3. **门槛修订（D10-ENV-2311）**：本机 Windows 排除端口段 **2280–2379 覆盖 TCP 2311**，`bind 127.0.0.1:2311` 恒报
   `WinError 10013`（与是否有服务端在跑无关）。`test_cli_orchestrate.py` 三用例在本波前后同样红
   （Lead 在 base 提交 `893383f` 的独立 worktree 复跑证实 + netsh 证实排除段）。
   「全绿」改读作「**118 passed + 3 个已归因环境红**」。**代价**：若归因错误会掩盖真回归——
   已两级钉死证据；解决环境问题后必须复跑到 **121 passed** 才算恢复全绿。

## 4. 门槛结果（Lead 独立复跑）

| 命令 | 结果 |
|---|---|
| `python -m protocol.s1 verify` | `PASS (5/5)`，exit 0 |
| `python -m pytest protocol -q` | `96 passed`，exit 0 |
| manifest 抽查 | files 恰 6 项、`s2s=4`、`opcodes_total=1171` |
| `python -m pytest acceptance/tests -q` | `3 failed, 118 passed`（3 红已归因 D10-ENV-2311，base worktree 同样红） |
| `python -m pytest acceptance/tests/test_multi_server.py -q` | `4 passed`（首跑 4 红，均为预期缺失） |
| .NET smoke | **未复跑**：本波 0 个 C# 文件改动（Task 8 的 41.3s 全绿证据可沿用） |

## 5. 遗留项当前视图

单一来源：`progress.md` 的 Task 9/10 段。变化：

- **新增 open：D10-ENV-2311** —— Windows 排除端口段 2280–2379 覆盖 TCP 2311；测试三用例恒红；
  **Task 11 真机服务端绑 2311 同样会 10013**。触发条件：Task 11 真机开跑前必须解决
  （重启观察换段 / `netsh int ipv4 set dynamic tcp start=49152 num=16384` 收窄动态段 / 管理员保留），
  解决后复跑 acceptance 预期 **121 passed**。
- **D7-NOTICE 范围澄清**：4 条 S2S 报文已进台账（Task 9）；「服务端是否真发 NOTICE」仍 open，归 Task 11 真机。
- 其余 D5-*/D6-*/D7-*/D8-* 状态不变（D5-REAL、D5-RAW-COMPARE 等 Task 11 项仍 open）。

## 6. 下一波（Task 11）开跑前必办

1. **解决 D10-ENV-2311**（否则真机服务端起不来 + acceptance 三用例仍红）。
2. **刷新计划 Task 11 段**（仍 `needs-refresh`）：重点核 Tier 2 例外/归一化清单（D5-RAW-COMPARE，
   随机会话密钥导致原始密文不同）与 D5-REAL 取数步骤。
3. 只读：本文件 §2–§5、计划 `## Task 11:` 段、`progress.md` 末两段、
   `tools/acceptance/`（多进程编排）与 `protocol/map/s2s-map.tsv`。
