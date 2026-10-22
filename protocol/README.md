# protocol/ —— 协议地图与进度账（S1）

把仓库里已有的协议事实（opcode 地图、六档覆盖度、人工外部证据、S0 真机 run）变成
**入库、可 diff、可换机自检**的正式资产。**不新增逆向**：字段布局属 S3。

- 帧头口径（本目录 `pattern` 反解依赖它）：**10 字节**传输头
  `type(u8) | protocol(u8) | total_length(u32 LE) | crc32(u32 LE)`。
  证据两路：服务端实现 `src/DFLegacy.Protocol/PacketFrame.cs:6-9`，以及真机日志互证
  （见 `protocol/ledger.py` 的 `parse_frame` 文档：`000A6637…` ⇒ protocol 10 / length 14182，
  与 `server.log` 的 `protocol=10 length=14182` 逐字吻合）。解析时**必须凑满 10 字节帧头**才允许解码。

## 三套词表（不得混用）

1. **证据六类**（事实从哪来）：`[静态]` `[抓包]` `[真机]` `[回放]` `[推断]` `[未验证]`；`[作废]` 用于被推翻的旧结论（保留原文，不删）。
2. **分档六档**（字段可见性）：`T0_no-evidence` `T1_dispatcher-no-handlerdata` `T2_handlerdata-no-fields` `T3b_fields-write-only` `T3_fields-read-only` `T4_fields-read+write`。
3. **判定三分 + fail**（一次 run 的结论）：`pass` `needs-human` `missing-evidence`，外加 `fail`（仅"证据在、但不符"）。

三套记号互相正交；任何一处混用都视为缺陷。外部证据轴 `ext_mark ∈ {byte_verified, field_read, name_only}`
是**人工**标注轴，与六档正交（`NOTI 1` 可以是 `T0` 却 `byte_verified`）。

## 目录

| 路径 | 内容 |
|---|---|
| `map/opcode-map.tsv` | 规范数据：1171 条 opcode（`kind`+`id` 主键，CMD 先、id 升序） |
| `map/s2s-map.tsv` | 进程间（S2S）报文表：4 条（`kind`+`id` 主键），人工维护、不进 import 流水线（无官方 `.rodata`） |
| `map/index.md` | **生成物**：六档 / kind / status / ext_mark 分布 + 七条边界（由 `render` 生成，不手改） |
| `ledger/opcodes.tsv` | 账①：地图全部列 + `ext_evidence` / `runtime_evidence` / `notes` |
| `ledger/scenarios.tsv` | 账②：S0 四场景的 12 条期望 ↔ opcode ↔ 哪次 run 验过 |
| `ledger/runs.tsv` | 真机 run 台账（当前 5 条，`created_utc` 升序） |
| `manifest.json` | **生成物**：每份文件的 sha256 + `rows` + `baselines`，无时间戳、键有序、可逐字节复现；清单**恰好** 6 份受跟踪产物（`map/index.md` 记 `rows: 0`，但仍计 sha256） |
| `importer.py` / `ledger.py` / `render.py` / `verify.py` / `s1.py` | 导入 / 两本账 / 报告+清单 / 只读自检 / CLI |
| `tests/` | pytest（`python -m pytest protocol -q`） |

> `README.md` **不在** `manifest.json` 的 6 份受跟踪产物里：文档漂移只由 `tests/test_readme.py` 的
> token 断言兜底 —— **文档不是规范数据**，这是取舍（若要纳入清单须改 spec §6）。

## 命令

```powershell
$env:PYTHONIOENCODING='utf-8'
python -m protocol.s1 import     # 原始产物 -> map/opcode-map.tsv（需 .superpowers 原始产物）
python -m protocol.s1 ledger     # 摄取一次真机 run，按主键合并进 ledger/*.tsv
python -m protocol.s1 render     # 生成 map/index.md 与 manifest.json（幂等）
python -m protocol.s1 verify     # 只读自检；退出码 0=通过 1=不符 2=用法错
```

`ledger` 一次只摄取一个 run（幂等、可复跑；要补历史就逐条跑）：

```powershell
python -m protocol.s1 ledger --verified-run <runId> --verified-scenario <scenario_id> `
  --verdict runs/<runId>/verdict.json --run-manifest runs/<runId>/manifest.json `
  --verdict-digest tools/acceptance/verdicts/<scenario_id>.json
```

**退出码**：`verify` 为 `0` = 五条断言全过；`1` = 任一断言不符（**含 `manifest.json` 缺失或非法**）；
`2` = 用法错（argparse）。`verify` 只依赖入库文件：`git clone` 后可直接跑，不需要客户端、
不需要 `.superpowers/`，也不需要 `runs/`。

## 五条断言（`verify`）

1. `sha256`：清单**恰好** 6 份受跟踪产物（缺一项/多一项都失败）；清单里每个文件与磁盘一致；
   `map/`、`ledger/` 下（**递归**）不得有清单外文件（防止绕过清单加文件）。
2. `rows_and_keys`：每份 TSV 的数据行数 = 清单 `rows`；表头与契约列序一致、每行字段数一致；主键唯一。
3. `baselines`：总数 / `kind` / `status` / 六档 / `ext_mark` / `s2s` 与 `manifest.baselines` 一致，六档之和 = 数据总行数，
   且官方 opcode 总数 = **1171**（写死的基线，防"删一行再刷清单"这种整体缩水绕过）；S2S 独立基线 = **4**
   （读 `s2s-map.tsv`，不并入官方六档/status/mark）。
4. `internal`：账①与地图的 `(kind,id)` 集合相等；场景账引用的 opcode、`verified_run_id`、`runtime_evidence` 里的 runId 都存在。
5. `s0_crosscheck`（§8.5）：每个场景只把 `created_utc` 最晚的 `[真机]` 行与
   `tools/acceptance/verdicts/<scenario_id>.json` 核对 sha256 + 四计数；更早的历史行只校验摘要是 64 位十六进制。

## 边界（不得误读）

1. `T3/T4` 只表示"能在 handlers 目录里看到字段读/写调用"，**不等于**已读出有序字段布局。
2. `write_count` 含 `put_header`（129 行）；排除后换档数为 0，但家族级计数偏大。
3. handlers 目录只覆盖 239/425 家族 ⇒ 186 个家族无字段调用数据（落 T1）：是"没有数据"，不是"没有字段"。
4. **`[存在]` 只证明帧在场**：S0 真机已实测"握手在场 ≠ 登录成功 ≠ 进城"；`pass` 不得读作"该 opcode 已实现"。
5. `runs.tsv` 的 `created_utc` 是控制端时钟，**不是**服务端 accept 时刻。
6. `party-invite` / `trade-open` / `town-position-sync` 是占位骨架：有 netdump 时必然 red，不是产品缺陷。
7. `ext_mark` 是人工轴，与六档正交；当前 `byte_verified`/`field_read` 极少，不得引用为"字段级进度"。

## 怎么加一条证据

1. **人工外部证据**：编辑原始 `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/tiers-external.tsv`
   后重跑 `import`（按 `byte_verified > field_read > name_only` 合并成 `ext_mark`）。
2. **运行时证据**：不用手改 TSV —— `ledger` 会从 `verdict.json` + `manifest.json` 自动写回
   `verified_run_id` / `verdict_result` / `runtime_evidence`，并把 `ext_evidence` 原文写进账①。
   摄取顺序无关（按 `created_utc` 判新旧），同一 run 重跑幂等。
3. **改完必须** `render` 再 `verify`：`render` 刷新 `manifest.json` 的 sha256；`verify` 只读、绝不修任何东西。

## 真机 run 台账约定

1. **runId 第三段是场景 id**：格式 `<UTC>-<git短哈希>-<scenario_id>`（`tools/acceptance/model.py:106-110`
   的 `new_run_id`）。`runs.tsv` **没有** `scenario_id` 列，`verify` 靠 `runId` 的这个约定还原场景
   （`protocol/verify.py` 的 `scenario_of_run`）；将来若要改 runId 格式，**必须给表加列**，不能只改生成逻辑。
2. **§8.5 只把最新一次 run 钉到 tracked 判定**：`tools/acceptance/verdicts/<scenario>.json` 是"该场景当前结论"，
   每次 run 覆盖提升；因此每个场景只把 created_utc 最晚的 `[真机]` 行与它核对 sha256 + 四计数，
   更早的历史行只校验摘要格式。**历史 run 摘要相同是正常的**（tracked 文件内容那几次恰好一致），不是造假。
3. **换机不需要 `runs/`**：`runs.tsv` 已入库，`verify` 只读入库文件；`runs/` 被 gitignore，
   也**不是**重建台账的前提（重建只需 `import` + `render`，加历史 run 才需要当次的 `runs/<runId>/`；
   若某份受跟踪产物**根本不存在**，`render` 会报错而不是生成缺项清单）。
4. **单 run 摄取**：`ledger` 一次一个 run，幂等、可复跑；要补历史就逐条跑，不要手工拼 TSV。
5. **run 的 `status` 是四值**：`green`（全部 pass）/ `green-with-human`（有 `needs-human` 或 `missing-evidence`）/
   `red`（有 `fail`）/ `invalid`（场景没有能失败的期望）。它与账②的
   `pass` / `needs-human` / `missing-evidence` / `fail` **不是同一套词表**：前者是"一次 run 的整体结论"，
   后者是"单条期望的判定"。
6. **`runtime_evidence` 的 `A:4` 记法**：`[真机] <runId> A:4,B:4` 读作"actor A 的**证据行号** 4、
   actor B 的**证据行号** 4"（行号取自 `verdict.json` 的 `<证据文件>:<行号>`），**不是**"4 个字段/4 帧"。
7. **`verified_run_id` 的语义**：表示"该 run 对这条期望**出过结论**"，**不必然 `pass`** ——
   账②的 `[语义]` 行同时有 `verified_run_id` 与 `verdict_result=needs-human` 是正常的（spec §5.3）。

## 进度账的两个已知边界

1. **`offsets` / `created_utc` 都是控制端视角**：`server_t0` 用 `manifest.created_utc` 近似（= 控制端时钟，
   `tools/acceptance/cli.py:12`），各 actor 的 `offsets` 只是该时钟的偏移；服务端 accept 时刻需从
   `server.log` 的 `Entrance listener ready` / `connected` 行取（S0 的 I-6）。别把账里的时间当服务端时刻。
2. **`[存在]` 只证明帧在场**：握手在场 ≠ 登录成功 ≠ 进城，`pass` 不代表该 opcode 已实现。

## 测试在各自目录跑

| 套件 | 命令（cwd） |
|---|---|
| S1（本目录） | 仓库根：`python -m pytest protocol -q` |
| S0 验收平台 | `(cd tools) python -m pytest acceptance/tests -q`（应 117 passed） |

仓库根裸跑 `pytest` 会有 **8 个既有收集错误**（`tools/acceptance/tests/*` 需要 `acceptance` 可导入），
**与 protocol 无关**；不要据此判断 S1 回归失败，也不要用 protocol 的改动去"修"它。
