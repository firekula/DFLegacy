# DFLegacy 接入说明（导航，不是文档）

面向"下一个接手的人"。模块职责见仓库 `README.md`，实现状态与验收结论见 `STATUS.md`（88KB，权威），
本文件只讲**怎么在这里干活、东西在哪、哪里会踩坑**。

## 1. 这个项目是什么 / 现在到哪了

面向 **DNF 赛里斯服公测 ACT1 客户端（`G:/Game/DNF_ACT1/DF/DNF.exe`，2008 年 1.0.1.9 版本）** 的
本地模拟服务端（C#/.NET 10）+ 启动器 + 客户端补丁。换句话说：**我们在没有服务端源码的情况下，
把这个客户端当年连的那个服务端重新实现出来**，并与两个"真相来源"对照——
**官方服务端二进制**（符号完整，静态实证）与**参考实现 OpenDNF**（可跑，抓包对照）。

阶段：核心玩法已实现（账号/角色/副本/掉落/任务/物品/商城/宠物/邮件/决斗数据），
社交与多人系统按"逆向 → 实现 → 自动化 → 真机双客户端验收"逐个落地：城镇同屏、好友/黑名单、
交易已真机验收；**组队进副本正在真机验收中**（2026-09-29 起）。

分支 `main`，HEAD `81e8ecc`，领先 `origin/main` **38 个提交**。
**工作区里有未提交的改动**：`src/DFLegacy.Server/GameplayDatagramService.cs`（+135 行，
疑为 STATUS §2.7 第 9 条那个 `DFLEGACY_UDP_AUDIT`：审计玩法 UDP 的去向）。接手前先看这处 diff。

## 2. 知识资产 vs 机器产物

| 路径 | 是什么 | 提交? |
| --- | --- | --- |
| `STATUS.md` | **最权威的状态/结论/验收表**，含真机验收矩阵与已作废结论 | ✅ |
| `.superpowers/sdd/*.md` | 各系统的逆向勘察报告（协议依据、证据地址、勘误卷） | ❌ 被忽略 |
| `.superpowers/sdd/*/work/` | 勘察与真机的**脚本 + 原始输出**（见 §4、§5） | ❌ 被忽略 |
| `.superpowers/sdd/2026-09-29-dungeon-party/work/official/` | **官服逆向产物**：命令号总表、符号全量、派发器目录、STATIC-FINDINGS | ❌ 被忽略 |
| `docs/superpowers/{specs,plans}/` | 设计稿与实现计划（按日期，先看最新） | ❌ 被忽略 |
| `docs/superpowers/handoff-*.md` | 交易/组队/副本组队的交接说明 | ❌ 被忽略 |
| `src/`、`tests/`、`tools/`（官方脚本） | 真正的产品代码与冒烟测试 | ✅ |
| `dist/`、`artifacts/`、`DF/town-test/` | 构建与验收的**机器产物**（另有 `DF/town-test/data/state.json` 是活存档） | ❌ |
| `DNF2008/Script.pvf`（126MB） | 客户端数据资产（PVF），非代码 | ❌ |

⚠️ `/docs`、`/.superpowers/`、`DNF2008/` 都在 `.gitignore` 里：**这些知识资产没有版本控制**，
删了就没了，改动前先备份。

## 3. 外部依赖（逐条给出"还在不在"的校验方式）

| 依赖 | 位置 | 校验 |
| --- | --- | --- |
| 真实客户端 | `G:/Game/DNF_ACT1/DF/DNF.exe` | `Test-Path G:\Game\DNF_ACT1\DF\DNF.exe` |
| 客户端 IDA 标注库 | **`G:/Game/DNF_ACT1/DF/DNF.exe.idb`**（约 100MB；本客户端是 **32 位**，IDA 按目标位数命名库 ⇒ `.idb`） | `Test-Path ...\DNF.exe.idb`（同目录的 `DNF.exe.i64` 是旧副本，见 §5.3） |
| IDA Pro 8.3 | `D:/Program Files (x86)/IDA_Pro_8.3/idat.exe` | `Test-Path`；`ida-dec` 跑一次即可 |
| **Git Bash** | `D:/Program Files/Git/bin/bash.exe` | **必须**：PATH 里的 `bash` 是 WSL 的，`/d/...` 路径会直接失败 |
| Python 3.12 | `D:/Program Files/Python312/python.exe`（`python` 已解析到它） | `python --version`；官服脚本另需 `capstone`(5.0.7 已装)、`pefile` |
| 参考实现 OpenDNF | `G:/Work/OpenDNF/_work/opendnf-win-0.0.3` | `Test-Path`；`capture_status.py` 里写死了这个路径 |
| 官服容器镜像 | `llnut/dnf:debian13-qf1031-latest`（relay+stun，组队副本必需） | `docker images`（Docker 已装；WSL 发行版 `ubuntu-26-04` 当前 Stopped） |
| 官服二进制（已抠出） | `.superpowers/.../work/official/df_game_r.bin` + `.idb`（162MB） | 目录内 `catalog_*.txt`/`opcode_*.txt` 是否还在 |
| 客户端 PVF | `DF/Script.pvf`（63MB）、`DFLegacy/DNF2008/Script.pvf` | `Test-Path` |
| 验收环境 | `G:/Game/DNF_ACT1/DF/town-test`（发布件 + `data/state.json` + `server-party*.log`） | 目录是否存在；日志按 `partyN` 递增 |
| 工作区外的中转工具 | `G:/Game/DNF_ACT1/pvf-dump`（地图出生点探针，**路径硬编码**） | `Test-Path` |

**缺的**：`msbuild` 不在 PATH（全解决方案含 `.vcxproj`，构建原生模块需要 VS 的 MSBuild；但
`dotnet run` 跑冒烟测试没问题）、**没有 cmake**（`build-native.ps1` 用不了，原生模块走 `.vcxproj`）。

## 4. 证据类别 → 本项目的产出方式

六类标记固定为 `[静态]`/`[抓包]`/`[真机]`/`[回放]`/`[推断]`/`[未验证]`，外加 `[作废]`（被推翻时加，
并指向推翻它的证据）。**本项目的"哪个工具产出算什么类别"如下**（写在知识层，不写进 keys.yml）：

| 类别 | 在本项目里由什么产出 |
| --- | --- |
| `[静态]` | `ida-dec`/`ida-script` 反编译 `DNF.exe`（基址 **0x400000**）；官服侧用 `.superpowers/.../official/` 的符号表与 `game_r.syms.txt`、`elf_syms.py`/`calls_in.py` 等 |
| `[抓包]` | `ref-proxy`（金标准逐包）+ `capture-analyze`；产物 `tcp-<port>-c<N>.{C,S}.bin`、`udp-<port>.log` |
| `[真机]` | 双客户端跑在 `DF/town-test`，服务端日志 `server-partyN.log`（`EnablePacketTracing=true`）；客户端侧现场用 `client-stacks` |
| `[回放]` | `smoke-tests`（协议字节级断言）；服务端确定性重放 |
| `[推断]`/`[未验证]` | 写进 `STATUS.md` 时必须标清楚 —— 这是常态，不是失败 |

## 5. 已知的坑

1. **地址口径**：采样器打印的 `模块+0xNNN` 是 `VA − 模块基址`。IDA 里要**加回基址**
   （`DNF.exe` = `0x400000`）。早先按偏移直接反编译得到的是另一个函数（`STATUS.md:430`）。
2. **必须用 Git Bash**：`run-ida.sh` 用 `/d/...` 形式调用 IDA，WSL bash 会报
   "No such file or directory"（症状像"IDA 没装"）。
3. **IDA 数据库是单写的**：`idat.exe` 退出时把库写回活动库；同一时刻只允许一个 IDA 任务。
   **活动库是哪个文件（2026-09-30 更正）**：本项目客户端是 **32 位**，IDA 按**被分析目标的位数**命名数据库
   （32 位 `.idb`、64 位 `.i64`）⇒ 活动库 = `G:/Game/DNF_ACT1/DF/DNF.exe.idb`（约 100MB，每次运行都被重写；
   在 IDA 里 `idaapi.get_path(PATH_TYPE_IDB)` 报的就是它）。目录里那个 `DNF.exe.i64`（99MB、mtime 停在 9/29）
   是 `work/build_db.py` 早期把 `save_database()` 的目标硬编码成 `"DNF.exe.i64"` 另存出来的**旧副本**，
   **不是**活动库；它之所以"看起来还能用"，只是因为同目录还有 `DNF.exe` 与 `DNF.exe.idb`，IDA 自己解析回了
   活动库——单独把 `.i64` 拷到别处喂给 idat 会被直接拒收：
   `You can't disassemble file with such an extension: ...i64`。`work/run_ida.sh` 的默认库已改为 `.idb`
   （要覆盖仍可用 `DNF_IDB=`）。
4. **沙箱**：本项目工作区曾被 DSH 自身的 workspace 授权卡住（`SetNamedSecurityInfoW ... Win32 5`），
   已用 `diagnose-windows-sandbox-acl` 修复（`G:/Game/DNF_ACT1/DFLegacy._acl-report/` 留了回滚脚本）。
   另：枚举线程的 Toolhelp 在受限模式下会被过滤，故采样器改用 PowerShell 取线程 id。
5. **配置里的 PVF 路径是错的**：仓库 `server.json` 的 `ScriptPvfPath` 指向
   `D:\DFLegacy\DF2008\Script.pvf`（**不存在**）；本机实际在 `DNF2008/Script.pvf`。README 也提醒
   发布前必须改回本机路径。
6. **工作区正在被并发写入**（见下），盘点/构建前先确认没有别的进程在跑。
7. **不要发 `CMD 11` 成功包给组队**、**`NOTI 56` 每会话只能发一次**、**`NOTI 9` 的 uid 是队伍 id**
   等硬约束见 `STATUS.md` §2.6 —— 这些是踩过真机坑才得到的，改协议前必读。
8. **PowerShell 里不能有叫 `-Pid` 的参数**：`$PID` 是只读自动变量（当前 shell 的进程 id），
   变量名大小写不敏感 → `param([int]$Pid)` 在**绑定阶段**就抛"无法覆盖变量 Pid"，脚本一行都跑不到。
   这不是"函数体里别写 `$Pid`"能绕开的，名字本身不能用（`collect-dnf-stacks.ps1` 用 `-TargetPid`）。
9. **给 IDA 的路径必须是 Windows 形式（`G:/...`）**：`idat.exe` 里跑的是原生 Windows Python，
   没有 MSYS 根目录概念，`/g/...` 会 `OSError: [Errno 22]`。同理 `-S` 只接受**一个**字符串并由
   IDA 自己按空格切分，所以路径与参数都**不能带空格**。这两条合起来意味着：`/d/Program Files` 这类
   路径只能在 shell 里用（`IDA_EXE`），不能出现在传进 `-S` 的字符串里。

## 6. 工作区活跃度（每次盘点都要重新评估）

- 最近提交：**2026-09-30 11:33**（就在本次接入前几十分钟），内容是 party15 崩溃现场文档。
- 近 24 小时被修改的文件：**230** 个（除 `bin`/`obj`/`.git`），绝大多数是
  `.superpowers/.../work/` 下的 IDA 脚本与输出 —— 说明**有人正在做客户端静态分析**。
- 验收服务端日志 `DF/town-test/server-party15.log` 在本次接入期间（15:05）仍在增长。
- **结论：这个项目正在被活跃使用，不是静止的。** 任何"重新盘点"都要假设文件在变。

## 7. 建议归档（能力重复，不要接线）

按能力（不是文件名）归并后，以下重复项建议归档，**保留代表**：

| 能力 | 代表（保留） | 建议归档 | 依据 |
| --- | --- | --- | --- |
| 32 位客户端线程栈采样 | `work/wow64_stack.py`（`Wow64GetThreadContext` + 原始栈转储 + `--watch`） | `work/stack_sample.py`（早期版，64 位 `GetThreadContext` 路线） | `watch_clients.py:16` 只 import 前者 |
| 无头 IDA 取数 | `work/run_ida.sh`（**权威实现**，包装 `ida_tool.py`） | —— 不归档，见下 | `.recon/run-ida.sh` 是它的薄包装，逻辑不重复 |
| 客户端内存/符号读取 | `party-protocol-recon/work/dnf.py`（PE 映射 + capstone） | `friends-protocol-recon/work/dnf.py`、`friends-ui-gaps/work/dnf.py`、`friends-round4/work/dnf.py` | 四份 **MD5 完全相同**（`47E26022…`），纯重复 |
| 反汇编流读取 | `friends-protocol-recon/work/ia.py` | `friends-ui-gaps/work/ia.py` | MD5 相同（`8504FF46…`）；两者各带一份 52MB `insns.txt` + 53MB `insns.pkl`，可只留一份 |
| IDA 一次性探针 | `work/` 下 ~20 个 `ida_*.py` | 由 `ida_tool.py` 的 dec/callers/callees 覆盖 | 命令范围被覆盖 |

**同一能力的"两份"怎么处置（2026-09-30 定）**：`work/run_ida.sh` 与 `.recon/run-ida.sh`
是**一条路**而不是两条 —— 后者只做"按项目根解析路径 + 转 Windows 路径"，然后 `exec` 前者，
逻辑一份都不重复。**权威副本是 `work/run_ida.sh`**；要改行为改它，不要改包装。
（反面教材：接入时我新写了一份包装，缺陷留在原件里，等于制造了"同一能力两份、不知道哪份对"。）

⚠️ **归档前必须确认依赖方向**：`watch_clients.py` → `wow64_stack.py`、
`calls_in.py`/`packet_senders.py`/`packet_layout.py`/`field_refs.py`/`find_dispatch_table.py` → `elf_syms.py`、
friends/party/trade 三族探针 → `dnf.py` + `ia.py`。这些**代表脚本不能删**。

**未申报成工具的一次性脚本**（已确认是死的，不要接线）：`pvf-dump`（PVF 路径硬编码、只吃 map id）、
`tools/key-magenta.ps1`（对单张皮肤图做色键，一次性）、`tools/patch-client-pay-auth.ps1`（一次性改客户端）、
`work/proxy-selftest/`、`work/captured-run*/`、`work/framing-check/`（这些是产物不是工具）。

## 8. 检索键实测计数

本节分两部分：**（A）方法有缺陷的旧测量**（保留在案，不可当基线）与
**（B）去重后的可复现基线**。引用数字时必须说清是哪一个。

### A. 旧测量（作者已确认是方法缺陷，降格保留）

**一次含重复计数的测量**：九个 pattern 在语料上的命中如下。**计数日期 2026-09-30，
语料规模记为 351 个文件 / 6,481,934 字符**，但作者已核实该文件数**含重复计数**，
绝对值偏高约 **6%**，**不是权威基线**；**相对量级可用**（各键之间的比例关系仍成立）。

| pattern | 命中 | 去重值 |
|---|---|---|
| `\bCMD\s*(\d+)\b` | 2544 | 68 |
| `\bNOTI\s*(\d+)\b` | 2787 | 59 |
| `protocol\s+(\d+)` | 25 | 7 |
| `\bsub_([0-9A-Fa-f]{6,8})\b` | 108 | 37 |
| `\b(Dis[Pp]atcher_[A-Za-z0-9_]+)\b` | 39 | 13 |
| `\b(C[A-Z][A-Za-z0-9]*::[A-Za-z0-9_]+)\b` | 271 | 77 |
| `\b(0x[0-9A-Fa-f]{6,8})\b` | 10895 | 3348 |
| `\+\s*(0x[0-9A-Fa-f]{1,4})\b` | 2986 | 166 |
| `\b([a-z][a-z0-9_]*/[a-z][a-z0-9_]*/[a-z0-9_]+\.[a-z0-9]+)\b` | 87 | 54 |

**重复计数从哪来**：`corpus:` 里的 `.superpowers/sdd/*.md`（19 个）被
`.superpowers/sdd/**/*.md`（163 个）**完全包含**（作者逐条核对过文件名）——
19 个文件名同时出现在两条 glob 的结果里，各被数了一次，于是 351 里多算了 **19** 个。
去重后是 **332**（= 351 − 19）。**该行已从 `corpus:` 删除**（见 §10）。

### B. 去重后的基线（口径固定、可复现）

- **计数日期 2026-09-30 15:2x**（本节同期）。
- **复现命令**：`pwsh -NoProfile -File .recon/count-corpus.ps1` —— **口径写死在脚本里，corpus 名单
  从 `recon.config.yml` 读**（唯一真相源，不内联）。这一点是硬要求：此前每次现写枚举，
  先后得到 340 / 355 / 351 三个数字，分不清是"语料在漂"还是"口径不同"。
- **依赖**：`node` + `yaml` 模块（用绝对路径 require；解析顺序 `$env:RECON_YAML_MODULE` →
  recon 包自带的 yaml@2.9.1）。**找不到就报错退出**，不退回内联列表。
- **硬失败（不许打印 0 当结果）**：YAML 解析失败 / `corpus:` 缺失或为空 / 任意一条 glob 命中 0 个文件。
  最后一条尤其重要 —— 拼错的 glob 会**静默地什么都不贡献**，总数只少一点、脚本 exit 0，
  正是"数字不再对应真实输入"那类失败。三条都已实测会报错退出。
- **语料规模：340 个文件，6,035,426 字符（= 7,018,077 字节）**。被 git 忽略的 **208** 个。

⚠️ **两个规模口径不要混用**：`CHARS` 是**解码后的字符数**（正则跑在字符串上，
**检索键计数以此为准**）；`BYTES` 是文件字节数之和。中文是多字节，两者差得远
（例：`STATUS.md` 88,521 字节 = 46,092 字符）。**旧测量那个"6,481,934 字符"的口径没有记录**，
无法判定它按字节还是按字符 —— 这也是它不能当基线的原因之一。

| kind | pattern | 命中 | 去重值 |
|---|---|---|---|
| `cmd` | `\bCMD\s*(\d+)\b` | 1991 | 68 |
| `noti` | `\bNOTI\s*(\d+)\b` | 2131 | 59 |
| `protocol` | `protocol\s+(\d+)` | 20 | 7 |
| `client_function` | `\bsub_([0-9A-Fa-f]{6,8})\b` | 61 | 37 |
| `server_symbol`（Dispatcher） | `\b(Dis[Pp]atcher_[A-Za-z0-9_]+)\b` | 23 | 13 |
| `server_symbol`（C++ 限定名） | `\b(C[A-Z][A-Za-z0-9]*::[A-Za-z0-9_]+)\b` | 157 | 77 |
| `address` | `\b(0x[0-9A-Fa-f]{6,8})\b` | 6341 | 3203 |
| `field_offset` | `\+\s*(0x[0-9A-Fa-f]{1,4})\b` | 1616 | 141 |
| `resource` | `\b([a-z][a-z0-9_]*/[a-z][a-z0-9_]*/[a-z0-9_]+\.[a-z0-9]+)\b` | 76 | 54 |

**两次测量**都保留在案（证据纪律：推翻旧结论时不删除，标明被什么推翻）。去重后的数据仍从
**同一条原则**读出，且结论未变：

- **去重 7–13 的键（`protocol` 7、`Dispatcher` 13）指向性最好**：命中即落在很小的候选集里，
  适合直接当"从现象反查位置"的入口。
- **`cmd` 68 / `noti` 59 / `sub_` 37 / `resource` 54 / `CClass::` 77** 是中等粒度：
  适合当漏斗第一层，但单靠它定位不了。
- **`cmd` 与 `noti` 虽然命中量相近，但绝不能合并成一个键**（2026-09-30 实测）：官服顺序表里
  CMD 608 项、NOTI 563 项，NOTI 的 563 个号全部落在 CMD 范围内，但 563 个共享号里**只有 2 个**
  两侧同名（`0 CHECK_CONNECTION`、`104 RESPONSE_CREATURE`），其余 **561 个完全不同义** ——
  本项目内部就有实例：`NOTI 7` 在 STATUS §2.5 是交易应答窗、§2.6 是组队邀请应答窗；
  `CMD 26` 是交易锁定/确认，`NOTI 26` 是 UDP 主机。所以 `keys.yml` 拆成 `cmd:N` / `noti:N`，
  各自的理由与代价写在 `keys.yml` 的注释里。
- **`address` 去重 3203（命中 6341）几乎不具备指向性**：6–8 位十六进制在这个语料里到处都是
  （地址、常量、哈希片段都有），只能用于交叉验证，不要拿它当检索入口。
- **`field_offset` 命中 1616 / 去重 141**：一个偏移值在语料里被反复引用（同一个字段在报告、
  日志、烟测里各出现一次），去重后才是真正的字段数。

### C. 给阶段 2 的契约要求：重叠 glob 必须去重

**`corpus:` 允许重叠 glob，而重叠会让任何天真索引器把同一文件收两遍** ——
本例两条 glob 重叠 19 个文件，索引就会凭空多出 19 份文档的"论断"，且**查询结果看起来完全正常**，
只是同一个论断被列了两次、看起来像两条独立证据。所以：

> **`recon_index_build()` 必须按规范化后的绝对路径去重后再抽论断**；
> 去重不能依赖"glob 写得干净"，因为配置是可改的，重叠迟早会再出现。

`count-corpus.ps1` 已把这条实现为一个**检查**：命中多条 glob 的文件会被单独列出（当前
`STATIC-FINDINGS.md` 同时被 `.superpowers/sdd/**/*.md` 与显式条目命中 —— 保留是有意的，
显式列出是为可读性，去重由脚本兜底）。

### D. 复现方法（口径定义，写死在 `count-corpus.ps1` 里）

1. glob **从 `recon.config.yml` 的 `corpus:` 读**（脚本不内联名单 —— 见下）；
2. `**/` **允许匹配零层目录**（因此 `a/**/*.md` 也含 `a/` 下的直接子文件）——
   注意 PowerShell 的 `-Include` **不吃**路径 glob（会返回 0 个文件），必须自己按段展开；
3. 按规范化绝对路径**去重**；
4. 文件按**路径字典序**拼接，分隔符 `` `n ``；
5. `CHARS` = 拼接后字符串的 `.Length`；`BYTES` = 文件长度之和；UTF-8 读取。

**为什么不内联 corpus 名单**（2026-09-30 决定）：内联 = 两个真相源记同一件事，失败模式是
"corpus 改了、脚本没改 → 脚本成功、打印一个数字、而那个数字不再对应真实输入" ——
与 `run_ida.sh` 那个 `${@:3}` 同类（exit 0、看起来正常、结果是错的）。
"多一个 YAML 解析依赖"的代价有上界，"静默错数"的没有。
**已实测脚本真的在读配置**：临时往 `corpus:` 插一条 `tools/*.ps1`（命中 2 个文件）→
`FILES` 340→342、`BYTES` 7,018,077→7,028,395；撤掉后恢复 340 / 7,018,077。

⚠️ **这一段是纠错过程，保留在案**：复跑时我先得到 340 个文件 / 6,035,390 字符、命中普遍低于
A 表约 20%，当时写成"差异来源未查明"。**现已查明**：就是 §A 说的重叠 glob 重复计数
（旧测量多算 19 个文件、约 +6%），加上**规模口径不同**（旧值的"字符"口径未记录，
可能是字节 —— 本例 6,035,426 字符对应 7,018,077 字节，差 16%）。会话中那个 355 的中间计数
同样是枚举口径差异（`.superpowers/sdd/*.md` 与 `**/*.md` 的朴素相加）。**结论：不是语料在漂，
是口径没固定** —— 这正是要求把脚本落进 `.recon/` 的原因。

## 9. 当前最值得做的三件事

1. **追平 party15 崩溃**（`STATUS.md` §2.7 第 7 条，唯一在查的活跃 bug）：五局复现，
   **队员**客户端在收到自己那份 `CMD 73` 后恰好 15 秒被 RST、进程退出；崩溃现场在 D3D8 绘制调用内
   （`d3d8.dll+0x99B4A`，画装备/称号立绘）。仪器已就位（`client-stacks` + `analyze_hang.py` +
   `scan_exception.py`），缺的是**一次带客户端在跑**的采样（本工具需要 `DNF.exe` 在运行）。
2. **把组队进副本的真机验收清单跑完**（`STATUS.md` §2.7：P8 单人零回归逐字节比对、P2/P12 等未验项）。
   这是当前功能能否算"完成"的分界线。
3. **补上仍属"未验证"的既有验收项**：S2（客户端是否接受 TCP 转发的 `USER_POSITION`）、
   A1/A4/A5 剩余项、交易 T8/T9（取消/掉线/换区/冻结抽查）。

## 10. 本模式的配置在哪

`.recon/recon.config.yml`（6 个工具：`ida-dec`、`ida-script`、`ref-proxy`、`capture-analyze`、
`client-stacks`、`smoke-tests`；corpus 13 条 glob）、`.recon/keys.yml`（检索键，8 个 keyKind）、
`.recon/run-ida.sh` + `.recon/collect-dnf-stacks.ps1`（两个包装脚本）、
`.recon/count-corpus.ps1`（语料与检索键计数，**口径写死在这里**，见 §8）。

**corpus 名单变动（2026-09-30）**：删掉了 `.superpowers/sdd/*.md` 一行（被
`.superpowers/sdd/**/*.md` 完全包含，见 §8A）。glob 数 14 → 13。
`count-corpus.ps1` **直接读** `recon.config.yml` 的 `corpus:`（不内联），所以改配置后
计数会自动跟上，不存在"两处要同时改"的问题。

**`ui` 维为什么是空的（有意，不是漏做）**：界面/行为无法从二进制或语料可靠推断，是只能由人给的词典。
接入时问过项目所有者，答复是**他本人也还没梳理，留到真正需要该维的分析阶段再补**
（2026-09-30 明确）。所以 `keys.yml` 里没有 `ui` 条目 —— 先塞一批没人确认过的名字等于往索引里灌
`[未验证]` 噪声，之后分不清"确认过"和"猜的"。**补进来的条件**：由人在会话里给出原词，
原样写进 `ui.terms`，并在本文件记明"这批界面名由用户于 <日期> 给出"——这是它唯一的来源凭据。
在此之前，任何依赖界面名的结论只能标 `[推断]` 或 `[未验证]`，不得当事实用。

**本模式落地时改了三处工具行为**（原因记在各工具 caveats 里）：`run-ida.sh` 修掉了
`${@:3}` 在引号内不展开的缺陷并按 argv 逐项传参；`collect-dnf-stacks.ps1` 把参数从 `-Pid`
改名为 `-TargetPid`（`$PID` 是 PowerShell 只读自动变量）；两者都补了 UTF-8 输出。
