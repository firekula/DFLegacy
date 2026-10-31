# Task 4：协议字段级覆盖度盘点的**独立复核**（守门）

- 复核者：`verify-task4`（**未参与 Task 1–3 的任何实现或审查**）
- 复核对象：worktree `G:\Game\DNF_ACT1\DFLegacy\.worktrees\plan-coverage-audit`（分支 `feat/protocol-field-coverage-audit`）
- 被复核产物：`.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/coverage-by-opcode.tsv`、
  `.../out/coverage-summary.md`、`.../out/tiers-external.tsv`、`.../coverage_tier.py`、`.../verify-login-tail.py`
- 复核时 worktree HEAD：`c9048c4`；`git status --short` 为空
- 纪律声明：**未修改任何交付文件**（`out/*`、`*.py` 全未动，见 §7）；所有试错脚本写在
  `%TEMP%` 下的一次性目录并在同一命令内删除；未派发任何 subagent；未引入新依赖
  （只用了标准库 + 产物本身已依赖的 `pycryptodome`）。

> **路径说明（不是缺陷）**：`task-4-brief.md` 的 Files 段写的是 `work/out/coverage-review.md`，
> 但该路径在 worktree 的 `sdd/` 里不存在（worktree 只有 `2026-10-05-gameplay-evidence-program`
> 与 `friends-protocol-recon` 两个计划目录）。我按下发指令把报告写到
> `2026-10-07-protocol-field-coverage-audit/coverage-review.md`。

---

## 1. Step 1：三个总数的**独立复算**（**未复用** `coverage_report.py`）

我用了**两条互不相同的独立路径**，并且两条都不是报告脚本。

### 路径 A：PowerShell 一行命令（与 brief Step 1 同形）

```powershell
$env:PYTHONIOENCODING='utf-8'
$O = "G:\Game\DNF_ACT1\DFLegacy\.worktrees\plan-coverage-audit\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work\out"
(Get-Content "$O\opcode-map.tsv").Count
(Get-Content "$O\coverage-by-opcode.tsv").Count
Get-Content "$O\coverage-by-opcode.tsv" | Select-Object -Skip 1 | ForEach-Object { ($_ -split "`t")[5] } | Group-Object | Sort-Object Count -Descending | Select-Object Count,Name
```

真实输出：

```
1172
1172

Count Name
----- ----
  743 T0_no-evidence
  187 T1_dispatcher-no-handlerdata
  109 T3_fields-read-only
   64 T2_handlerdata-no-fields
   52 T4_fields-read+write
   16 T3b_fields-write-only

body rows = 1171
tier sum  = 1171
```

列号校验（证明 `[5]` 确实是 `tier`，不是错列）：

```
header: kind	id	name	status	dispatcher	tier	read_count	write_count	rodata_va	ext_mark	ext_evidence
  idx[0] = kind        idx[1] = id          idx[2] = name
  idx[3] = status      idx[4] = dispatcher  idx[5] = tier      <-- 复算用的就是它
  idx[6] = read_count  idx[7] = write_count idx[8] = rodata_va
  idx[9] = ext_mark    idx[10] = ext_evidence
```

### 路径 B：另一把尺子（Python，独立于 PowerShell 的按行读取）

```powershell
python "$T\recount.py"     # 脚本在 %TEMP% 一次性目录，用完即删
```

真实输出：

```
opcode-map.tsv 物理行(非空) = 1172  数据行 = 1171
coverage-by-opcode.tsv 物理行(非空) = 1172  数据行 = 1171
opcode-map kinds  = {'CMD': 608, 'NOTI': 563}
opcode-map status = {'mapped': 428, 'no-dispatcher': 741, 'unused': 2}
tier counts = {'T3_fields-read-only': 109, 'T4_fields-read+write': 52,
               'T1_dispatcher-no-handlerdata': 187, 'T3b_fields-write-only': 16,
               'T2_handlerdata-no-fields': 64, 'T0_no-evidence': 743}
tier sum = 1171
set-membership  map == cov ? True    map_dupes = 0
order           map == cov ? True
ext_mark counts = {'name_only': 1168, 'byte_verified': 2, 'field_read': 1}
```

### 与 `coverage-summary.md` 逐项对账

| 项 | 我的独立复算 | `coverage-summary.md` | 一致 |
|---|---|---|---|
| opcode 行数（分母） | 1171（文件含表头 1172） | 1171 | ✅ |
| CMD / NOTI | 608 / 563 | 608 / 563 | ✅ |
| status=mapped | 428 | 428 | ✅ |
| status=no-dispatcher | 741 | 741 | ✅ |
| status=unused | 2 | 2 | ✅ |
| 分档表行数（含表头） | 1172 → 数据行 1171 | — | ✅ |
| T0_no-evidence | 743 | 743 | ✅ |
| T1_dispatcher-no-handlerdata | 187 | 187 | ✅ |
| T2_handlerdata-no-fields | 64 | 64 | ✅ |
| T3b_fields-write-only | 16 | 16 | ✅ |
| T3_fields-read-only | 109 | 109 | ✅ |
| T4_fields-read+write | 52 | 52 | ✅ |
| 各档之和 | **1171** | 1171 | ✅ |
| ext_mark: byte_verified / field_read / name_only | 2 / 1 / 1168 | 2 / 1 / 1168 | ✅ |

**额外**：`opcode-map.tsv` 与 `coverage-by-opcode.tsv` 的 `(kind,id)` 集合与**顺序**完全一致、
无重复键 —— 说明分档表没有静默丢行、也没有重排行序。

**Step 1 结论：三个总数全部复算一致，且分母/分档行数/各档之和自洽。**

---

## 2. Step 2：抽样反查（**8 条，覆盖全部 6 档**）

### 2.1 我用的判据

我在 `catalog_handlers_full.txt` 里按**家族名**（第 1 列）取出该家族的全部行，然后**手数**。
为了不让"手数"变成"把实现的正则再抄一遍"，我用**独立的表达方式**（显式字符串前缀）判定，
并把它与产物的正则逐条交叉验证，两者必须给出同一个数：

```
产物判据（coverage_tier.py:43-44）
  READ  = re.compile(r"name=PacketBuf::get_")
  WRITE = re.compile(r"name=(?:InterfacePacketBuf|PacketBuf)::put_")

我的独立判据（不用正则 alternation）
  sym(l) = l 里 'name=' 之后到下一个 \t 之间的子串
  READ   <=> sym(l).startswith("PacketBuf::get_")
  WRITE  <=> sym(l).startswith("InterfacePacketBuf::put_") or sym(l).startswith("PacketBuf::put_")
```

**假阳性向量已排除**：我检查过"一行里出现多个 `name=`"的可能性 ——
`含 >1 个 'name=' 的行数 = 0`，所以 `search` 式的定位不会被嵌套 `name=` 带偏。
（我第一版清单脚本用了 `name=([^\t]*)` 却把 `source=put_header` 之后的全部内容当成名字，
导致一度误判"1248 行都有 name="。这是我的命令错，见 §6 的踩坑记录，**不是产物的缺陷**。）

### 2.2 逐条对照表

抽样方式：每档至少 1 条；T4 取了 3 条（含特征最丰富的 `Login` 与最长的 `GM_Command`）。
"家族行数"= 该家族在 handlers 目录里的总行数（供你判断分母大小）。

| # | 档位 | opcode | 家族 | 家族行数 | 我手数 READ | 表 `read_count` | 我手数 WRITE | 表 `write_count` | 吻合 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | T3_fields-read-only | `CMD 0` CHECK_CONNECTION | `CheckConn` | 9 | 2 | 2 | 0 | 0 | ✅ |
| 2 | T3_fields-read-only | `CMD 17` SEND_MESSAGE | `SendMess` | 86 | 11 | 11 | 0 | 0 | ✅ |
| 3 | T1_dispatcher-no-handlerdata | `CMD 4` SELECT_CHARACTER | `SelectCharac` | **0** | 0 | 0 | 0 | 0 | ✅ |
| 4 | T2_handlerdata-no-fields | `CMD 21` BUY_ITEM | `BuyItem` | 8 | 0 | 0 | 0 | 0 | ✅ |
| 5 | T3b_fields-write-only | `CMD 73` CARD_SELECT_RIGHT_STATE | `Card_Select_Right_State` | 24 | 0 | 0 | 5 | 5 | ✅ |
| 6 | T4_fields-read+write | `CMD 1` LOGIN | `Login` | 129 | 15 | 15 | 16 | 16 | ✅ |
| 7 | T4_fields-read+write | `CMD 110` GM_COMMAND | `GM_Command` | 262 | 29 | 29 | 35 | 35 | ✅ |
| 8 | T4_fields-read+write | `CMD 375` FAIR_PVP_SCORE | `FairPvPScore` | 46 | 2 | 2 | 9 | 9 | ✅ |

**8/8 吻合**，且**每一条的"正则版"与"显式版"计数也相同**（脚本对每条都打印了
`正则/显式一致: ... True`），所以这不是"两个相同实现互相确认"。

逐条的观察要点（均来自上面同一批命令的真实输出）：

- **#1 `CheckConn`**：9 行里只有 2 行是读，且都是 `PacketBuf::get_int(int&)`（同址 `0x0858d0f0`）→ 有读无写 ⇒ T3。合理。
- **#2 `SendMess`**：11 行读、0 行写 ⇒ T3。11 个读里有 3 个 `get_str`、5 个 `get_int`、3 个 `get_byte`。合理。
- **#3 `SelectCharac`**：家族在 handlers 目录里**行数 = 0** ⇒ 落 T1（"有 dispatcher、无 handler 数据"），
  这正是 T1 的定义，而不是"有数据但没字段"。**这条是 T1 判据的关键抽检点。**
- **#4 `BuyItem`**：家族**有 8 行**、但读 0 写 0 ⇒ 落 T2（"有 handler 数据、无字段调用"）。
  **这条是 T1/T2 分界的关键抽检点**：它与 #3 形态相反，档位也不同，说明 T1/T2 不是被同一个条件吞掉。
- **#5 `Card_Select_Right_State`**：0 读、5 写 ⇒ T3b。5 个写**含 1 个 `put_header`**
  （已知口径，见 §5），即使把它排除也仍有 4 个写 ⇒ 仍是 T3b，**档位不因该口径而改变**。
- **#6 `Login`**：15 读 / 16 写。16 个写里 `put_header` 只占 1 个 ⇒ 口径影响很小。
- **#7 `GM_Command`**：家族行数 262（最大样本），29 读 / 35 写，手数与表完全一致 ——
  大分母下也没有出现"数错一格"。
- **#8 `FairPvPScore`**：2 读 / 9 写，其中写里有 3 个 `put_header`，排除后仍 6 个写 ⇒ 档位不变。

### 2.3 超出 brief 要求的一步：**全表逐行**独立重 derive

抽样只能证伪"抽到的那些行"。所以我把判据在**另一条独立实现**里重写一遍，
对**全部 1171 行**重算 `(tier, read_count, write_count)` 并与产物逐行比对：

```
handler 字段<3 被跳过的行 = 0  (summary 声称 0)
独立重derive tier 计数 = {'T3_fields-read-only': 109, 'T4_fields-read+write': 52,
                          'T1_dispatcher-no-handlerdata': 187, 'T3b_fields-write-only': 16,
                          'T2_handlerdata-no-fields': 64, 'T0_no-evidence': 743}
与表中 tier/read_count/write_count 逐行不符的行数 = 0
排除 `::put_header(` 后换档的 opcode 条数 = 0
name=InterfacePacketBuf::put_header 行数 = 129  涉家族 = 69
其中 source=dispatch_sig 的行数 = 129
```

**逐行不符 = 0 / 1171。** 这比"抽 8 条都对"强得多：它排除了"没抽到的那 1163 行里有错行"的可能。

同时**聚合量也对得上**：

```
handler 目录全局 READ 行数         = 634
handler 目录全局 WRITE 行数(含hdr)  = 565   其中 put_header = 129
coverage-by-opcode.tsv  read_count 列合计  = 658
coverage-by-opcode.tsv  write_count 列合计 = 555
distinct mapped 家族 = 423；被 >1 条 opcode 共用的家族 = 3
复用最多的 5 个: [('QuestAction', 4), ('StartGame', 2), ('BuyItem', 2), ('CheckConn', 1), ('Login', 1)]
```

列合计（658/555）与目录全局行数（634/565）**本来就不该相等** —— 一个家族会被多条 opcode 共用
（`QuestAction` 4 条、`StartGame` 2 条、`BuyItem` 2 条）。倍数 658/634 = 1.04 与 555/565 = 0.98
量级合理（写侧 <1，因为含 `put_header` 的家族行是按家族计的、而并非每个家族都映射到 opcode）。

---

## 3. Step 3：`CMD 1` 的 `byte_verified` 走**独立路径**验证

### 3.1 复跑既有脚本（brief 指定的那条）

```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\.worktrees\plan-coverage-audit\.superpowers\sdd\2026-10-05-gameplay-evidence-program\work
python verify-login-tail.py
```

真实输出（`streams-live\2343-10013` 那一组，逐字照抄）：

```
streams-live\2343-10013.bin SHA256=f442a4162c2e69b33616e7a7ca7c93e2f36bb7fde685fe65f6f2be8516f72730
FIELDS [('patch', 0, 4, 0), ('token_length', 4, 4, 344), ('token', 8, 344, 'sha256=828693702479dfaaedbddaffec49e517170710ef463346aa7cecd39254cd3dff'), ('string1_length', 352, 4, 0), ('string1', 356, 0, 'sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'), ('string2_length', 356, 4, 0), ('string2', 360, 0, 'sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'), ('flag', 360, 1, 0), ('mouse_length', 361, 4, 8), ('mouse_encrypted', 365, 8, '783e83cfae411959cd53d6b5ba5aae62263e2cbf72c8050cd631be1f272e2bcb'), ('compare_DataManager_0x3c0', 373, 4, 20080418), ('u32_after_version', 377, 4, 28000201), ('server_group', 381, 2, 3), ('local_ip', 383, 4, 'a9daea1ac04182cae8102d2b0266d94201105179c11277a72a66f59182291b82'), ('loginout_state', 387, 1, 1)]
CURSOR=388 PLAIN_LEN=400 UNCONSUMED=12 UNCONSUMED_ALL_ZERO=True
```

目标串 **`CURSOR=388 PLAIN_LEN=400 UNCONSUMED=12 UNCONSUMED_ALL_ZERO=True` 逐字出现** ✅
退出码 `0`。

我另外核了这条串**内部自洽**：`PLAIN_LEN - CURSOR = 400 - 388 = 12 = UNCONSUMED`，
且 `UNCONSUMED_ALL_ZERO=True` 与"明文末 24 个 hex 全 0"一致，`last_nonzero_abs=387` 与之相符。

### 3.2 我**没有**只依赖这一步：从原始抓包字节重算全部 10 条

`verify-login-tail.py` 只打印游标与哈希、不落明文；而 `byte_verified` 的门槛是
"线上字节被**逐字段钉住并落盘可复跑**"。所以我直接从 `captures/` 的原始 `.bin`
独立重算了产物 `out/login-plain-dump.json` 的每一个字段：

```
--- captures\streams-live\2343-10013.bin ---
  首帧=[1,1] len=413  cf[13:]=400  plain=400
  plain[:8] = 0000000058010000   (期望 0000000058010000)
  key_sha256   OK
  plain_sha256 OK
  plain_hex    OK
  token_sha256 OK
  patch=0 token_len=344 str1=0 str2=0 mouse=8 flag=0
  独立 CURSOR=388  tail=12  tail_all_zero=True  last_nonzero=387
（4 个可用的 live 配对全部同形，全部 OK）

独立重算与 dump 全字段不符的条目数 = 0 / 4
token 区长度 = 344, base64 形态 = 是合法 base64，解码后 256 字节
token 区末尾 4 字节 = 36413d3d   可见 ASCII 占比 = 344 / 344
```

**要点**：`key_sha256` / `plain_sha256` / `plain_hex` / `token_sha256` / 游标 / 尾部全零
**六项全部独立复现**，与落盘 JSON 逐字段一致；token 区是**合法 base64**（344 → 256 字节），
346 个字符全是可见 ASCII —— 这独立佐证了"字段边界确实钉在语义上"，不是把随机字节切成了 344。

**Step 3 结论：`CMD 1` 的 `ext_mark=byte_verified` 不是自封的 —— 它有可复跑的字节级产物，
并且该产物能被第三方（我）从原始抓包独立重算出来。**

---

## 4. 已裁决事项的"知情"（**按要求不作为新缺陷上报**）

这三条我**读过并核对过**，确认与实际数据相符，**不**视为缺陷：

1. **`NOTI 1` 的 `ext_mark = field_read`（不是 `byte_verified`）** —— Lead 的裁决，已在
   `out/tiers-external.tsv` 落地：该行 `mark=field_read`，
   `evidence = "login-tail-static.txt：Dispatcher_MouseRegister/decryptPassword/checkValiedPassword/PacketBuf::get_*"`。
   我复算的 `ext_mark` 分布正是 `byte_verified=2 / field_read=1 / name_only=1168`，
   与 `coverage-summary.md` §4 一致；且 summary §4 保留了 `[作废]` 原文并指向推翻它的证据，
   符合"被推翻的结论不删原文"的纪律。**我认同该裁决的档位口径**（只有"逐字段钉住并落盘可复跑"才配
   `byte_verified`），且 `NOTI 1` 在 tier 轴是 `T0`、在 ext 轴是 `field_read`，两轴正交，不矛盾。
2. **`name=InterfacePacketBuf::put_header` 会被写正则命中** —— 我独立量化确认为
   **129 行、全部 `source=dispatch_sig`、涉 69 个家族**，与裁决文本完全一致；
   并且我**独立复算"排除它之后换档 0 条"**（见 §2.3）⇒ **档位判据不受影响**，
   仅 `write_count` 展示列会偏大。
   另外确认 **`source=put_header` 的 1248 行不被命中**：这 1248 行的第 1 列家族恒为合成值
   `(sender)`，其 `name=` 之后没有任何 `InterfacePacketBuf::put_`/`PacketBuf::put_` 形态
   （`其中真正含 '::put_' 的 = 0`）⇒ 不会进入任何字段读/写计数。
   （我清单脚本第一版把 `name=` 读到了行尾，一度以为"1248 行都有 name=" —— 那是我的错，见 §6。）
3. **不传 `--external` 时 `coverage-by-opcode.tsv` 与 Task 2 逐字节相同** —— 我按"**实质零回归**"
   的形式独立验证了（因为该文件当前提交版**含** ext 两列，与 Task 2 的 9 列版必然不同）：

   ```
   old blob (39154c5) = 93685 B, sha256 = 205c1f0f1f1582b0ff08c03c2f744ff1b42a0602e96a0778c85cd5b1a6f522e4
   new blob (c9048c4) = 107096 B, sha256 = 4dde35397f201e4f5b74e901a16a187c6e8f4bc4124da096a7561d132bd1c7bb
   old 1172 行 / 9 列      new 1172 行 / 11 列
   old 表头 = kind id name status dispatcher tier read_count write_count rodata_va
   new 表头 = ... rodata_va ext_mark ext_evidence
   【实质零回归】new 前 9 列 == old ？ -> True
     投影 SHA256(9列) = 205c1f0f1f1582b0ff08c03c2f744ff1b42a0602e96a0778c85cd5b1a6f522e4
     old    SHA256    = 205c1f0f1f1582b0ff08c03c2f744ff1b42a0602e96a0778c85cd5b1a6f522e4
   ```

   ⇒ 新增两列是**纯追加**，前 9 列与 Task 2 基线**逐字节相同**（同 SHA256）；
   机制上 `coverage_tier.py:257` 也确实只在 `any("ext_mark" in r)` 时才追加列。
   **注**：任务给的字面校验和 `CC5767F5…3730` 我在仓库内**找不到出处**
   （对 `2026-10-05-…/work` 与计划目录 grep `CC5767F5` 均无匹配）。
   我无法验证那个具体数字，因此我改为验证它的**实质命题**（上表），并如实标注这一点。

---

## 5. 我发现的问题（逐条 + 严重级）

**Critical: 0 条。Important: 0 条。Minor: 2 条。**

### MINOR-1　`out/login-plain-dump.json` 的 10 条里，只有 2 条能在本仓库内做密码学复核
（其余 6 条的被复核对象 = 8 条，其中 4 条是同一对配对的重复记录）

- **证据**：JSON 引用 10 组 `login_file`，但 `work/captures/` 下实际存在的是
  `streams-live/`、`streams-live2/`、`streams-dbmw/`、`streams-dbmw2/`；
  以下目录**整个不存在**：`captures/streams-vm/`、`captures/streams/`、`captures/streams-e2e/`。

  ```
  captures\streams-live\2343-10013.bin     login=True   key=True
  captures\streams-live2\14805-10013.bin   login=True   key=True
  captures\streams-vm\13681-10013.bin      login=False  key=False
  captures\streams\33018-30011.bin         login=False  key=False
  captures\streams\46386-30011.bin         login=False  key=False
  captures\streams-e2e\33018-30011.bin     login=False  key=False   （以下 e2e 5 条同）
  ```

- **严重级：Minor**，理由：这不是产物**内部**的不一致。我核了全部 10 条的自洽性：
  `plain_hex` 长度全部 400 B = 声明的 `plain_len` = `payload_len`（`自洽性不符 = 0`）、
  `tail_region_bytes=12`/`nonzero_in_tail=0`/`last_nonzero_abs=387`/`selfcheck=PASS` 全部一致；
  而**可复核的 2 条 live 配对，六项字段 100% 复现**（§3.2）。缺失的是"其余 8 条所引用的抓包文件"
  （很可能是生成该 JSON 的环境里的 `streams-vm`/`streams-e2e`，未随 worktree 落盘）。
- **可证伪方式**（谁想推翻我）：把 `captures/streams-vm/13681-10013.bin` 等文件放回 worktree，
  跑 §3.2 那套独立重算，若仍与前 9 条对不上，则本条应升级为 Important。

### MINOR-2　`CMD 1` 的 `byte_verified` 证据串写的是"10/10 自检 PASS"，但其中 8/10 在本仓库内不可复跑

- **证据**：`out/tiers-external.tsv` 第 1 行（也合并进 `coverage-by-opcode.tsv` 的 `ext_evidence` 列）：
  `work/out/login-plain-dump.json 10/10 自检 PASS；CURSOR=388/PLAIN_LEN=400（verify-login-tail.txt）`。
  而 `login-plain-dump.json` 的 10 条里有 8 条引用不存在的抓包（MINOR-1）⇒ 这个"10/10"
  在新环境里**不可独立复现**，只能读到 JSON 里自带的 `selfcheck: PASS`。
- **严重级：Minor**，理由：`CMD 1 = byte_verified` 这个**结论**本身站得住 —— 我独立复现了
  可复核的 2 条配对（§3.2），且 `verify-login-tail.py` 复跑命中目标串（§3.1）。
  受影响的只是证据串里"10/10"这个**计数表述**的可复跑范围。
- **建议（不阻塞通过）**：把证据串改为写清"本仓库可复跑 N 条 + 其余引用的抓包未落盘"，
  或在 `captures/` 下补齐那些 `.bin`。这属于 Task 2/3 的产物措辞，**我不代改**（复核者不改交付文件）。

### 附：一条限定语句（**不是缺陷**，但它限定 §3 的强度）

`verify-login-tail.py` 自己的最后一行写着：

```
LIMIT: unconsumed bytes not named padding without producer proof; mouse plaintext and downstream login acceptance not verified.
```

我认同这个限定：`[388:400]` 那 12 B 全零、以及 `mouse_encrypted` 的明文，在本计划内**没有生产者证明**。
所以 `byte_verified` 应当读作"**该帧 400 B 明文的字段边界与内容可复跑复现**"，
**不**读作"每个字段都由服务端生产者钉住"。这也与 `coverage-summary.md` §3 的限定语句一致
（T3/T4 只表示"能看到读/写调用"，字段布局仍须逐家族反编译）。**我没有把它当缺陷，只是把它标清楚。**

---

## 6. 复核过程中的踩坑记录（**工具错 → 差点读成结论**，按项目纪律留痕）

本项目最警惕的失败形状是"工具错 → 空输出 → 被读成结论"。我在本次复核里**自己踩了三次**，
全部是**我的命令错、不是产物的错**，记录在此以说明上面每条结论都不是被误读出来的：

1. **`key_sha256` 假不符**：我第一版独立重算把 AES 密钥取自
   `10013-2343.bin`（S→C 首帧），得 `key_sha256=40b8a823…`，与 JSON 的 `b7f51029…` 不符，
   一度以为发现 Critical。读 `dump-login-plain.py:19` 后确认口径是
   **密钥材料 = 同一对里 C→S 首帧（`2343-10013.bin`）的帧偏移 `[15:349]`**
   （不是另一条流的 `[15:]`）。按正确口径复算，`key_sha256` 完全一致。
   **教训**：文件名方向在本项目是反的（`2343-10013.bin` 装的是 C→S 字节，脚本 docstring 已留档），
   我先怀疑了自己的命令，才没有报出假缺陷。
2. **`1248 行都有 name=` 假象**：我第一版清单脚本用 `re.search(r"name=([^\t]*)", line)`，
   在 `source=put_header` 的行上把 `source=put_header` **之后**的全部内容都当成了名字，
   于是打印出 `含 name= : 1248`。改用 `name=` 之后到下一个 `\t` 的精确切分后，真实结果是
   `其中真正含 '::put_' 的 = 0`。**这正是"正则锚点没锚住"造成的自造证据。**
3. **零回归对比两次被 PowerShell 破坏**：我先后用 `git show ... > file` 与
   `git cat-file blob ... | Set-Content -NoNewline` 落盘 blob，
   结果 PowerShell 把行合并成一行（`old[0]` 竟以 `0x08aeb9d8CMD` 结尾），
   得出"逐字节相同 = False、差异行数 = 1"的**假**结论。最后改为**全程在 Python 里
   `subprocess.run(capture_output=True)` 取字节**再比，才得到
   `投影 SHA256 = old SHA256 = 205c1f0f…` 的 True。
   **教训**：跨工具传字节流时，别让外壳的文本管线碰它。

---

## 7. 复核者未改动任何交付文件

```
git -C <worktree> status --short   ->   (空)
```

- 未修改 `out/coverage-by-opcode.tsv`、`out/coverage-summary.md`、`out/tiers-external.tsv`；
- 未修改 `work/coverage_tier.py`、`work/coverage_report.py`、`work/verify-login-tail.py`；
- 未运行 `coverage_report.py`（brief 允许，但我判断它会写 `out/*`，
  而我的产出比对全部可用只读路径完成，故**不跑**，以避免任何"复核动作改动了被复核对象"的风险）；
- 所有试错脚本均在 `%TEMP%` 下一次目录内创建、同命令内 `Remove-Item -Recurse -Force` 删除；
- 唯一新增文件 = 本报告。

### 关于 brief Step 5 的提交（**我选择不提交，理由如下**）

- 本报告按其指定路径落在**主仓库**的 `.superpowers/sdd/2026-10-07-protocol-field-coverage-audit/`，
  而 `.gitignore:16` 是 `/.superpowers/` ⇒ **整个目录默认不被跟踪**。
- 本仓库的既有惯例（我核过）：`task-1-review.md`、`task-3-review.md`、
  `2026-10-05-…/evidence-closure-signoff.md` 在 main 上**均为未跟踪**；
  被 `git add -f` 强推进 feature 分支的只有 `work/out/*` **产物**
  （如 `2026-10-05-…/work/out/coverage-by-opcode.tsv` 存在于 `feat/protocol-field-coverage-audit`）。
- 且 brief Step 5 给的路径是 worktree 的 `.superpowers/sdd/**2026-10-05**-gameplay-evidence-program/work/out/coverage-review.md`，
  与本计划（`2026-10-07-protocol-field-coverage-audit`）不是同一个目录；
  worktree 分支上**没有** `2026-10-07-…` 目录，往 `work/out/` 提交会把"计划评审文档"
  塞进另一个计划的工作目录，属噪音。
- 最关键的是：主仓库 `main` 目前有大量**与本任务无关的未提交改动**
  （`src/`、`tests/`、`STATUS.md`、`.recon/onboarding.md` 等 20+ 项）。
  在这种状态下替 Lead 做 `git add -f` 提交，会把我的文件混进一堆无关改动里，
  有误提交他人半成品的风险。
- ⇒ **报告以文件形式交付（该目录在本项目的既定形态就是这样），不代为提交。**
  若需要入库，请 Lead 决定是提交到 main 还是迁到 worktree 的 feature 分支；
  两者我都能再做，但**不擅自选**。

---

## 8. 复核结论

```
复核结论：通过。独立复算的三个总数与 coverage-summary.md 一致；抽样 8 条（覆盖全部 6 档）逐条吻合，
且额外对全部 1171 行做了独立重 derive（0 行不符）；CMD 1 的 byte_verified 有可复跑字节级证据
（verify-login-tail.py 复跑命中 CURSOR=388 PLAIN_LEN=400 UNCONSUMED=12 UNCONSUMED_ALL_ZERO=True，
并可由我独立从原始抓包重算出落盘 JSON 的全部字段）。未发现静默丢弃或档位越界。
附 2 条 Minor：login-plain-dump.json 的 10 条中 8 条所引用抓包未落盘（故其中 2 条不可复跑），
据此 byte_verified 证据串里的"10/10"表述可复跑范围应收窄 —— 两者均不推翻结论。
```

**明确判定：通过（PASS）**，无 Critical / Important，2 条 Minor（MINOR-1、MINOR-2，
均针对证据串表述与抓包落盘范围，不涉及任何数字错误、不涉及静默丢弃、不涉及档位越界）。

关于 brief 给的模板，逐句落实如下（**没有**照抄不成立的句子）：

| 模板句 | 是否成立 | 我的实际写法 |
|---|---|---|
| "独立复算的三个总数与 coverage-summary.md 一致" | ✅ 成立 | 逐字保留（§1，两条独立路径 + 逐项对账表） |
| "抽样 N 条逐条吻合" | ✅ 成立（N=8） | 写成"抽样 8 条（覆盖全部 6 档）逐条吻合"，并补"全表 1171 行独立重 derive 0 行不符" |
| "CMD 1 的 byte_verified 有可复跑字节级证据" | ✅ 成立 | 逐字保留，并注明证据范围（2 条配对可复跑、8 条抓包缺失） |
| "未发现静默丢弃或档位越界" | ✅ 成立 | 逐字保留（依据：`(kind,id)` 集合/顺序全同、无重复、跳过行数 0、逐行不符 0、排除 put_header 换档 0） |
| 模板**没有**提到的 | — | 我补了 2 条 Minor，因为"通过"不等于"无瑕疵" |
