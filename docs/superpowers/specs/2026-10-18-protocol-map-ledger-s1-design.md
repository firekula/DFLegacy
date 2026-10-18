# S1 设计稿 —— 协议地图与进度账（`protocol/`）

- 日期：2026-10-18
- 归属：S0 设计 §8 路线图的 **S1**（"机读 + 人读的协议地图、覆盖面账、校验和、可换机自检"）
- 状态：待所有者审阅（本稿经 brainstorming 五问定稿：范围 / 落地位置 / 数据格式 / 自检边界 / 账的粒度）
- 前置：S0 验收平台已交付（`tools/acceptance/`，117 passed，真机 run `20261018T091951Z-2724238-town-two-clients`）

## 1. 背景与问题

仓库里已经存在大量**协议事实**，但它们散落在被 gitignore 的临时工作区里，读者与后续会话都无法当成资产使用：

| 已有素材 | 现状 | 证据 |
|---|---|---|
| `opcode-map.tsv`（1171 条 opcode → name/rodata_va/dispatcher/status） | `.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/`，**未入库** | 实测 1172 行（含表头） |
| `coverage-by-opcode.tsv`（+ 六档、读写计数、`ext_mark`） | 同上，未入库 | 同上 |
| `tiers-external.tsv`（5 条人工外部证据） | 同上，未入库 | 6 行（含表头注释） |
| `coverage-summary.md`（五条对账基线 + 分档表） | 同上，未入库 | 分档 743/187/64/16/109/52 = 1171 |
| `coverage_tier.py` / `coverage_report.py`（六档判据、对账即非零退出） | 同上，未入库 | 逻辑与基线均在此 |
| S0 真机 run 与三分判定 | `runs/`（gitignore）+ `tools/acceptance/verdicts/town-two-clients.json`（入库） | `pass 3 / needs-human 1 / missing 0 / fail 0` |
| 真机 netdump 字节 | `runs/<runId>/actor-*/netdump-<pid>.log`（gitignore） | 首帧 `000B2B…` / `000C2F…` |

后果：**同一份"我们知道了什么"每次都要重新推导**；新会话无法判断某 opcode 究竟处于哪一档、有没有运行时证据；也没有任何机制能发现"账目被悄悄改了"。

S1 把上述素材转成**入库、可 diff、可换机自检**的"协议地图 + 两本账"。

## 2. 目标与非目标

### 2.1 目标

1. **一份规范数据**：1171 条 opcode 的地图，TSV，可 diff、可机读。
2. **两本账**：opcode 账（每条 opcode 的证据状态）+ 场景账（S0 四场景的期望 ↔ opcode ↔ 哪次 run 验过）。
3. **人读报告**由脚本从规范数据生成（不手写，避免漂移）。
4. **`protocol verify` 可换机自检**：只依赖入库文件，`git clone` 后直接跑，任一不符即非零退出。
5. 把"S0 真机结果"作为**运行时证据**精确落回具体 opcode（不靠记忆）。

### 2.2 非目标（明确不做）

- **不新增逆向**：不反编译 handler、不补字段布局、不扩大 opcode 分母（这是 S3）。
- 不改产品代码（`src/`）、不改 S0 的 `tools/acceptance/`、不动 `STATUS.md`。
- 不要求 `.superpowers/` 原始产物在目标机器上存在（`verify` 与它无关）。
- 不把 `[语义]` 项自动判成通过（它仍然是 `needs-human`）。
- 不做多机/局域网（S4）。

## 3. 术语与词表（不得混用）

三套记号互相正交，**任何一处混用都视为缺陷**：

1. **证据六类**（描述"这条事实从哪来"）：`[静态]` / `[抓包]` / `[真机]` / `[回放]` / `[推断]` / `[未验证]`，另有 `[作废]` 用于被推翻的旧结论（**保留原文**，不删）。
2. **分档六档**（描述"字段可见性"，来自 `coverage_tier.py`）：`T0_no-evidence` / `T1_dispatcher-no-handlerdata` / `T2_handlerdata-no-fields` / `T3b_fields-write-only` / `T3_fields-read-only` / `T4_fields-read+write`。
3. **判定三分 + fail**（描述"一次 run 的结论"）：`pass` / `needs-human` / `missing-evidence`，外加 `fail`（仅用于"证据在、但不符"）。

外部证据轴 `ext_mark ∈ {byte_verified, field_read, name_only}` 由**人工**标注，与六档**正交**：`NOTI 1` 可以是 `T0`（没有可达 dispatcher）却 `byte_verified`。

> **口径警示（必须写进 README）**：`T3`/`T4` 只表示"能在 handlers 目录里看到字段读/写调用"，**不等于**"已读出有序字段布局"。原文见 `coverage_tier.py` 已知边界段，逐字沿用。

## 4. 目录结构

```
protocol/
  README.md                 人读入口：用途、三套词表、边界、常见误读、如何加一条证据
  map/
    opcode-map.tsv          规范数据：1171 条
    index.md                生成物：分档表 + 证据分布 + 五条基线
  ledger/
    opcodes.tsv             账①：opcode 级
    scenarios.tsv           账②：场景级
    runs.tsv                真机 run 台账
  manifest.json             确定性清单：每份文件 sha256 + 行数 + 基线计数（**无时间戳**）
  s1.py                     CLI：import / render / verify
  tests/                    pytest
```

新增顶层目录 `protocol/`（受追踪）。`docs/`、`.superpowers/` 仍被 gitignore，因此**规范数据不放那里**。

## 5. 数据契约

### 5.1 `map/opcode-map.tsv`（规范数据，主键 `kind + id`）

| 列 | 类型 | 取值 / 约束 |
|---|---|---|
| `kind` | 枚举 | `CMD` \| `NOTI` |
| `id` | 十进制整数 | 与 `kind` 组成主键 |
| `name` | 字符串 | 空串允许（"只有编号"）；不得含制表符 |
| `rodata_va` | `0x` 十六进制 | 原样保留 |
| `dispatcher` | 字符串 | 空串 = 无 dispatcher |
| `status` | 枚举 | `mapped` \| `no-dispatcher` \| `unused` |
| `tier` | 枚举 | 六档之一（判据见 §3.2 与 `coverage_tier.py`） |
| `read_count` / `write_count` | 非负整数 | 字段读/写调用条数（写计数含 `put_header`，见边界） |
| `ext_mark` | 枚举 | `byte_verified` \| `field_read` \| `name_only` |

**`ext_mark` 的合并规则（消除歧义）**：`tiers-external.tsv` 实测有 **5 条数据行但只覆盖 3 个 opcode**
（同一 opcode 可有多条证据，例如 `CMD 1` 同时有 `byte_verified` 与 `field_read`）。因此导入时：

- `ext_mark` 取该 opcode 全部证据里**优先级最高**者：`byte_verified > field_read > name_only`
  （与 `coverage_tier.py` 的 `EXTERNAL_MARKS` 顺序一致）；
- 该 opcode 的**全部**证据原文只落在账①的 `ext_evidence`（§5.2），用 `"; "` 合并，不丢弃低优先级那条；
- 未出现在 `tiers-external.tsv` 的 opcode 一律 `name_only`。

行顺序：`kind`（CMD 先）→ `id` 升序，**稳定排序**，以便 diff 与逐字节自检。

### 5.2 `ledger/opcodes.tsv`（账①，主键 `kind + id`）

在 §5.1 全部列之后追加：

| 列 | 说明 |
|---|---|
| `ext_evidence` | 人工外部证据原文（来自 `tiers-external.tsv`），空串 = 无 |
| `runtime_evidence` | 运行时证据：`[真机] <runId> A:4,B:4` 形式；多条用 `;` 分隔；空串 = 无 |
| `notes` | 自由备注（`[作废]` 说明、已知坑）；不得含制表符 |

### 5.3 `ledger/scenarios.tsv`（账②，主键 `scenario_id + step_index + expect_index`）

| 列 | 说明 |
|---|---|
| `scenario_id` | `town-two-clients` / `party-invite` / `trade-open` / `town-position-sync` |
| `step_index` / `expect_index` | 从 1 开始 |
| `actor` | `A` / `B` |
| `action` | 场景里的中文动作描述（逐字） |
| `level` | `[存在]` / `[字节]` / `[语义]` |
| `pattern` | 期望的匹配串；`[语义]` 项为空 |
| `evidence_class` | 该期望声明的证据类（如 `[抓包]`），空 = 未声明 |
| `mapped_opcodes` | 由 pattern 反解出的 `CMD:<id>`（无则空） |
| `verified_run_id` | 真正验过它的 run（空 = 未验） |
| `verdict_result` | 该 run 里的结果：`pass` / `needs-human` / `missing-evidence` / `fail` |

**`pattern → opcode` 的反解机制**（硬事实，不是猜）：

DNF 帧头是 **10 字节**：`type(u8) | protocol(u8) | total_length(u32 LE) | crc32(u32 LE)`，
头后第一字节通常是记录数。因此：

- `000B2B0000000000000001` ⇒ `type=0, protocol=0x0B=11, length=43, crc=0, body=01`
- `000C2F0000000000000001` ⇒ `type=0, protocol=0x0C=12, length=47, crc=0, body=01`

两条都与 S0 真机 `server.log` 逐字吻合：`RX … protocol=11 length=43`、`TX … protocol=12 length=47`
（`runs/20261018T091951Z-2724238-town-two-clients/server/server.log`）。这就是
"`[抓包]` 的 hex → `CMD` opcode"的可复核桥。

> `[作废]` 本节初稿（`c0d2120`）写的是"DNF 帧头 4 字节 = `u16 id + u16 size`（大端）"，
> 被以下证据推翻，**原文保留在此**：
> 1. `[静态]` `src/DFLegacy.Protocol/PacketFrame.cs:5-13`：`packed ten-byte transport header:
>    type(1), protocol id(1), total length(4), CRC32(4)`，`HeaderLength = 10`；
> 2. `[真机]` 首帧按 10 字节解释得 `protocol=11 length=43`，与同 run 的 `server.log` 一致；
>    按旧的 `u16+u16` 解释会得到 `size=11008`（错）；
> 3. `[真机]` 另有两类 hex 行并非帧（例如 `636F6E6E6563743D6F6B` = ASCII `connect=ok`），
>    反解只对场景里的帧 pattern 使用，遇到非帧行不应猜。

**`verified_run_id` 的语义**：表示"该 run 对它**出过结论**"，不必然是 `pass`；
`[语义]` 项在同一 run 里的结论就是 `needs-human`，两者同时出现是正确的，不是矛盾。

### 5.4 `ledger/runs.tsv`（真机 run 台账，主键 `run_id`）

| 列 | 说明 |
|---|---|
| `run_id` | 全自动 runId（`<UTC>-<git短哈希>-<场景>`） |
| `git_sha` | 该 run 使用的代码版本 |
| `created_utc` | 控制端时钟（= `manifest.created_utc`） |
| `status` | `green` / `green-with-human` / `red` / `invalid` |
| `pass` / `needs_human` / `missing_evidence` / `fail` | 四个计数 |
| `evidence_class` | `[真机]`（真实客户端）或 `[回放]`（桩/合成）——**必须显式区分** |
| `verdict_digest_sha256` | 该 run 归一化摘要的 sha256（可与 `tools/acceptance/verdicts/<scenario>.json` 交叉核对） |

**收录边界**：只收**产出了 verdict 的 run**。没有判定产物的失败尝试（例如 S0 期间因平台缺陷中止的几次）
不入本账，它们的事实留在 S0 账本里；这样 `runs.tsv` 与 `verify` 的 §8.5 交叉核对才能一一对应。

## 6. `manifest.json`（确定性）

```json
{
  "schema": 1,
  "files": {
    "map/opcode-map.tsv": {"sha256": "…", "rows": 1171},
    "ledger/opcodes.tsv": {"sha256": "…", "rows": 1171},
    "ledger/scenarios.tsv": {"sha256": "…", "rows": 12},
    "ledger/runs.tsv": {"sha256": "…", "rows": 1},
    "map/index.md": {"sha256": "…", "rows": 0}
  },
  "baselines": {
    "opcodes_total": 1171, "cmd": 608, "noti": 563,
    "status": {"mapped": 428, "no-dispatcher": 741, "unused": 2},
    "tiers": {"T0_no-evidence": 743, "T1_dispatcher-no-handlerdata": 187,
              "T2_handlerdata-no-fields": 64, "T3b_fields-write-only": 16,
              "T3_fields-read-only": 109, "T4_fields-read+write": 52},
    "ext_marks": {"byte_verified": 1, "field_read": 2, "name_only": 1168}
  }
}
```

以上 `baselines` 全部取自真实产物（实测 `coverage-by-opcode.tsv` 1171 行：`kind` 608/563、`status` 741/428/2、六档 743/187/64/16/109/52、`ext_mark` 1/2/1168）。

**确定性要求**：不含时间戳、不含绝对路径、键按字典序、JSON 以 `indent=2, sort_keys=True` 输出并**以单个换行结尾**。理由：`verify` 要能逐字节复现，且换机 clone 后必须一致。

**5 文件契约**：`files` 必须**恰好**包含这 5 份受跟踪产物（`map/opcode-map.tsv`、`ledger/opcodes.tsv`、
`ledger/scenarios.tsv`、`ledger/runs.tsv`、`map/index.md`）。缺少任一份、或多出别的条目，
`verify` 都必须在 §8.1 失败——否则"删掉整本台账 + 从清单移除"就能让自检通过（终审 M5）。

> `rows` 对 TSV 指**数据行**（不含表头）；`index.md` 为生成物，记 `rows: 0` 但**仍计入 sha256**。

## 7. CLI 契约（`protocol/s1.py`）

| 子命令 | 作用 | 依赖 | 退出码 |
|---|---|---|---|
| `import` | 从 `.superpowers/.../work/out/` 读原始产物 → 写 `map/opcode-map.tsv`（**只写地图**；账目由 `ledger` 生成） | 需原始产物 | 0 成功 / 2 输入缺失 |
| `ledger` | 逐 run 摄取：生成/合并 `ledger/{opcodes,scenarios,runs}.tsv`（幂等、可复跑） | 需 `map/`、场景 YAML、该 run 的 `verdict.json`/`manifest.json`、`verdicts/<scenario>.json` | 0 成功 / 2 输入缺失/不符 |
| `render` | 由规范数据生成 `map/index.md` + 刷新 manifest | 无 | 0 / 1 |
| `verify` | 只读校验（§8），**不写任何文件** | 无 | 0 通过 / 1 断言不符（含 `manifest.json` 缺失或非法——数据不可信属"不符"，不是用法错） / 2 用法错（子命令/参数本身不合法） |

约定：`verify` 输出逐条断言结果（`OK <断言名>` / `FAIL <断言名>: <细节>`），末尾一行
`verify: PASS|FAIL (<通过数>/<本次实际执行的断言数>)`。
正常情况执行全部 **5** 条；若 `manifest.json` 缺失或结构非法，则只执行第 1 条（完整性）并输出
`verify: FAIL (0/1)` —— 分母是**实际执行的条数**，不是硬编码的 5。

## 8. `verify` 的五条断言（任一不符即非零退出）

1. **完整性与防篡改**：manifest 里每个文件的 sha256 与磁盘一致；manifest 未列出的文件出现在 `map/` 或 `ledger/` ⇒ 失败（防止绕过清单加文件）。
2. **形状**：每份 TSV 的数据行数 = manifest 记录；`(kind,id)` 唯一；`scenarios.tsv` 主键唯一；列数一致。
3. **基线**：六档计数之和 = 1171；各档、`status` 分布、`kind` 分布与 `manifest.baselines` 完全一致。
4. **内部一致**：`ledger/opcodes.tsv` 的 `(kind,id)` 集合 == `map/opcode-map.tsv` 的集合；`scenarios.tsv` 的 `mapped_opcodes` 全部存在于 map；`runtime_evidence` 里出现的 runId 必须存在于 `runs.tsv`。
5. **与 S0 交叉核对**：对**每个场景**，`runs.tsv` 里该场景 `created_utc` **最晚**的 `[真机]` 行，
   其 `verdict_digest_sha256` 与四个计数必须等于
   `tools/acceptance/verdicts/<scenario_id>.json` 的 sha256 与 `counts`。
   其余（更早的）`[真机]` 行是**历史记录**，只要求 `verdict_digest_sha256` 是 64 位十六进制，
   **不要求**与当前 tracked 文件相符。

   > 为什么不是"每一行都要相符"：`verdicts/<scenario>.json` 是每次 run 时**覆盖提升**的
   > "该场景当前结论"，只对应最新一次 run。若要求历史行也相符，一旦入账一次非全绿 run，
   > 交叉核对就会假失败（复审 finding N1）。当前 5 个 run 的摘要恰好相同（都是 `a6b53129…`），
   > 掩盖了这个矛盾，但规格不能依赖巧合。

## 9. 边界与已知缺口（写进 README）

1. **`T3`/`T4` ≠ 已读出字段布局**（逐字保留 `coverage_tier.py` 的原文边界）。
2. **`write_count` 含 `put_header`**（129 行）：实测排除后换档数为 0，但家族级 `write_count` 会偏大。
3. **`handlers` 目录只覆盖 239/425 家族** ⇒ 186 个家族无字段调用数据（落 T1），是"没有数据"，不是"没有字段"。
4. **`[存在]` 只证明帧在场**：S0 真机已实测"握手在场 ≠ 登录成功 ≠ 进城"（`[语义]` 项仍为 `needs-human`）。账里的 `pass` **不得**被读作"该 opcode 已实现"。
5. **T0 依据**：`runs.tsv` 的 `created_utc` 是控制端时钟，**不是**服务端 accept 时刻；服务端时刻需从 `server.log` 的 `Entrance listener ready` / `connected` 行取（S0 的 I-6）。
6. **骨架场景恒红**：`party-invite` / `trade-open` / `town-position-sync` 的期望用 description 当匹配串，有 netdump 时必然 `red`；这是占位，不是产品缺陷。
7. **`ext_mark` 是人工轴**：本版 `byte_verified`/`field_read` 各 1 条，不得引用为"字段级已完成多少"。

## 10. 测试策略

`protocol/tests/`（pytest）：

1. **真实数据自检**：对入库文件跑 `verify()` ⇒ 必须 PASS。
2. **防篡改（非恒真）**：把 `opcode-map.tsv` 某一行改一个字节、或删一行、或加一个未清单文件 ⇒ `verify()` 必须 FAIL 且指出对应断言（逐条覆盖 §8 的 5 条）。
3. **分档判据**：用 `coverage_tier.py` 的同一判据对若干样例行（T0/T1/T2/T3/T3b/T4 各一）断言档位，防止导入时错档。
4. **pattern → opcode 反解**：`000B2B…` ⇒ `CMD:11`；`000C2F…` ⇒ `CMD:12`；非法/过短输入 ⇒ 明确报错不猜。
5. **render 幂等**：连续两次 `render` 产出逐字节相同（含 manifest）。

## 11. 验收判据（DoD）

1. `git clone` 到干净目录后，`python -m protocol.s1 verify` 返回 0，且输出 5/5 PASS。
2. 改任一入库数据文件 1 字节 ⇒ `verify` 返回 1 并指出是哪条断言。
3. `map/index.md` 由 `render` 生成，内容含：六档表、`kind`/`status` 分布、`ext_mark` 分布、七条边界（§9）。
4. `ledger/scenarios.tsv` 中 `town-two-clients` 的三条 `[存在]` 与一条 `[语义]` 齐备，且 `verified_run_id` 指向一次 `[真机]` run。
5. `protocol/tests` 全绿；仓库根 `pytest` 不因新增目录而失败。

## 12. 风险

| 风险 | 应对 |
|---|---|
| 导入时错档（手动 join 出错） | 用 `coverage_tier.py` 同一判据复算，样例断言 + 基线断言双保险 |
| 账目随时间漂移（有人手工改 TSV） | `verify` 的 sha256 + 行数 + 基线三重；`render` 幂等 |
| `manifest.json` 含时间戳导致换机不一致 | 明令禁止时间戳；`render` 幂等测试兜底 |
| 把 `pass` 误读为"已实现" | §9.4 边界 + README 词表 + `scenarios.tsv` 显式保留 `level`/`result` 两列 |
| 将来 S3 扩字段时与本数据冲突 | `schema: 1` 版本号；扩展走新列 + 版本+1，不重排已有列 |
