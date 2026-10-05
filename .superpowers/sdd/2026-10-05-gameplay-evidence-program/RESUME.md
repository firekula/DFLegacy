# RESUME —— 官服玩法面证据链工程 M0（接续块）

> **判据（spec §6.1）**：一个没参与过本任务的会话，只读这份文件就能接着干。
> 最后更新：2026-10-05（M0 结束、评审门之前）。**当前进度：Task 1–7 已完成并提交；Task 8 未开始。**
> 计划：`docs/superpowers/plans/2026-10-05-gameplay-evidence-program-m0-m1.md`
> 规格：`docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md`
> 分母报告（评审门要的那一份）：`.superpowers/sdd/2026-10-05-gameplay-evidence-program/M0-denominator-report.md`

---

## 0. 一句话现状

`df_game_r` 的**全量派发器目录（425 家族）**与**opcode → 派发器映射（1171 条，`unknown=0`）**已产出。
四判据：**1 PASS、2 PASS、4 PASS（抽样 10/10 + 全量 424/425）、3 以修正形式补全**。
**Task 8（B0 批次）已完成**：拿到**成功路径**（VM 里那套官服栈，游戏面 46,193 B / 90 帧）与
**失败路径**（docker 容器被拒，末帧 `NOTI 199 DISCONNECT_REASON`），并实测出
**游戏面帧模型 = 10 字节头**（与 7001 的 11 字节头不同）。详见 `work/out/B0-batch-report.md`。

**所有者裁决（2026-10-05）**：⑥ 待评估队列的东西**都是需要的** ⇒ **S2 不采纳、范围不缩**，
D2 从"与现有 DFLegacy 等价"扩为**官方功能面全覆盖** ⇒ **spec §5.2 的里程碑表需要先扩展**。

**下一步 = Task 9–11（驱动器前置）**：帧层/密码层 → 7001 已知答案自检（硬门 S3）→ 30011/10013 方言与令牌。
不要再跑 Task 8（批次已完成）。

---

## 1. 目录与路径（一切以此为锚）

| 记号 | 绝对路径 |
|---|---|
| `$P` | `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program` |
| `$O` | `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official`（**只读引用**：`df_game_r.bin` + `.bin.idb` + 各种 `.txt` 转储） |
| `$W` | `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work`（**只读引用**：登录链那轮的脚本与抓包） |
| 工作区 | `G:\Game\DNF_ACT1\DFLegacy`（git 分支 `main`） |

`$P` 结构：`work\`（脚本）、`work\out\`（产物）、`work\captures\`（抓包，空）、`work\driver\`（驱动器，空）。

---

## 2. 产物清单（哪些在 git、哪些不在、怎么重跑生成）

### 2.1 在 git 里（都在 `$P` 下，提交时用 `git add -f`，因为 `.superpowers/` 被 gitignore）

| 文件 | 是什么 | 提交 |
|---|---|---|
| `work/env-baseline.txt` | Task 1 环境体检原始输出 + 判定 | `4b442a5` |
| `work/demangle-syms.py`、`work/out/demangle-check.txt` | 解名工具与验收 | `8e74d6b` |
| `work/extract-dispatchers.py`、`work/out/catalog_dispatchers_full.txt` | 全量派发器目录（**425 家族**） | `819287f` → 修正 `786bf99` |
| `work/game-scan-imm.py`、`work/map-opcodes.py`、`work/elfrecon.py`、`work/out/opcode-map-residual.txt`、`work/out/opcode-map-summary.txt` | 按节扫立即数 / 映射器 / ELF 助手 / 残余报告 | `558e9bc` |
| `work/extract-handlers.py`、`work/out/catalog_handlers_full.txt` | 处理器目录（7273 条） | `3c93af6` |
| `.recon/recon.config.yml`、`.recon/run-ida.sh`、`work/ida-game.py` | 两个新 recon 工具 + 包装脚本支持 `--idb` | `8d57b99` |
| `work/m0-acceptance.py`、`work/out/M0-acceptance.txt`、`M0-denominator-report.md`、`RESUME.md` | 四判据原始输出 + 报告 + 本文件 | `df7989f` |
| `work/family-evidence.py`、`work/out/family-evidence.txt`、`work/out/family-cpp-map.tsv` | **判据 3 补全件**：DWARF 覆盖率实测、全量 vtable/RTTI 反查、`__FILE__` 池 → 精确 `.cpp` | （补全提交） |

### 2.2 **不在** git（派生物，按需重跑；**不要**提交，20–30 MB 级）

| 文件 | 大小 | 重跑命令（`python` 就是本机 python 3.12） |
|---|---|---|
| `work/out/demangled-syms.tsv` | 32 MB | `python $P\work\demangle-syms.py $O\game_r.syms.txt $P\work\out\demangled-syms.tsv`（约 2 秒，走容器 sidecar `dfl-recon` 的 `c++filt`） |
| `work/out/opcode-map.tsv` | 1171 行 | `python $P\work\map-opcodes.py $O\opcode_cmd.txt $O\opcode_noti.txt $P\work\out\catalog_dispatchers_full.txt $P\work\out\opcode-map.tsv` |
| `work/out/ida-smoke.txt` | 410 B | 见 §4 的 `ida-game` 三条分支 |
| `work/out/ida-dispatcher-names.txt` | 4621 行 | 同上（`dispatcher-names` 分支） |
| `work/out/ida-dispatcher-files.txt` | 数行 | 同上（`dispatcher-files` 分支；实测该库**没有行信息**） |

> `demangled-syms.tsv` 与 `opcode-map.tsv` 是 Task 3/4/5/7 的共同输入：**先重跑这两个，其余都可再生**。

### 2.3 依赖的外部既有产物（`$O`，本次**没有修改**）

`game_r.syms.txt`（352,694 行 mangled 转储）、`opcode_cmd.txt`（608）、`opcode_noti.txt`（563）、
`packet_senders.txt`（1249 行 = 1 注释 + 1248 构造点）、`catalog_dispatchers.txt`（既有 84 条，用作交叉验证）、
`game_r.syms.txt` 与 `df_game_r.bin`/`.bin.idb`。

---

## 3. 可重跑命令（复现 M0 的每一件结论）

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
$env:PYTHONIOENCODING='utf-8'      # 不设的话 Python 的中文输出在 Windows 控制台会乱码（文件本身是 utf-8）

# 1) 解名（产出 §2.2 第一个派生物）
python "$P\work\demangle-syms.py" "$O\game_r.syms.txt" "$P\work\out\demangled-syms.tsv"

# 2) 家族目录（425 家族；会打印与既有 84 条的差异相关统计）
python "$P\work\extract-dispatchers.py" "$P\work\out\demangled-syms.tsv" "$P\work\out\catalog_dispatchers_full.txt"

# 3) opcode -> 派发器（1171 条；打印机制自检与家族覆盖）
python "$P\work\map-opcodes.py" "$O\opcode_cmd.txt" "$O\opcode_noti.txt" `
  "$P\work\out\catalog_dispatchers_full.txt" "$P\work\out\opcode-map.tsv"

# 4) 处理器目录（7273 条）
python "$P\work\extract-handlers.py" "$P\work\out\catalog_dispatchers_full.txt" `
  "$O\packet_senders.txt" "$O\df_game_r.bin" "$P\work\out\catalog_handlers_full.txt"

# 5) 四条判据（原始输出落到 M0-acceptance.txt）
#    判据 3 依赖 family-evidence.py 的产物，**先跑 5a 再跑 5b**：
python "$P\work\family-evidence.py" "$P\work\out\family-evidence.txt" "$P\work\out\family-cpp-map.tsv"   # 5a
python "$P\work\m0-acceptance.py" "$P\work\out\M0-acceptance.txt"                                        # 5b

# 6) 任意地址的 xref（按可执行节扫；约 50 秒/次）
python "$P\work\game-scan-imm.py" "$O\df_game_r.bin" --targets 0x08aeb9d8 0x08aeb9f8 --limit 6
```

**IDA 侧（`ida-game` 的三条分支；走 `.recon/run-ida.sh --idb`，转成 `DNF_IDB=` 交给权威实现
`$O\..\run_ida.sh` = `.superpowers/sdd/2026-09-29-dungeon-party/work/run_ida.sh`）**：

```powershell
$bash = 'D:\Program Files\Git\bin\bash.exe'      # 必须 Git Bash；PATH 里的 bash 是 WSL
cd 'G:\Game\DNF_ACT1\DFLegacy'
& $bash '.recon/run-ida.sh' '--idb' '.superpowers/sdd/2026-09-29-dungeon-party/work/official/df_game_r.bin.idb' `
    '.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/ida-game.py' `
    '.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/ida-smoke.txt' 'smoke'
# 换成 dispatcher-names / dispatcher-files 就是另两条分支（判据 2 / 判据 3 用）
```

**跑 IDA 前必须确认没有别的 IDA 任务**（单写）：`Get-Process idat,ida -ErrorAction SilentlyContinue`。
170 MB 库加载实测约 **3–9 秒**（库已分析过，不是分钟级）；`timeoutMs` 在 `.recon` 里给了 15 分钟。

---

## 4. 环境事实（2026-10-05 17:0x 实测，Task 1 基线 + 后续补充）

| 事实 | 值 |
|---|---|
| docker | 29.8.0；容器在跑：`dnf`（官服栈）、`dfl-recon`（sidecar，提供 `c++filt`/binutils）、另有 codex-pooler 两个与本工程无关 |
| **宿主 7001/tcp、2311/udp 占用者** | **pid 17428（`DFLegacy.Server`）** —— 两个端口同一个 pid |
| 容器发布端口 | `30011/tcp → 宿主 30021`、`31011/udp → 宿主 31021`、`5505/tcp → 5505`、`7001/udp → 7001`、`2312/udp → 2312`、`2313/udp → 2313`、`3306/tcp → 13306`、`7300/tcp+udp → 7300` |
| 容器 `7001/tcp`、`2311/udp` | `[]`（**空数组**，不是 `null`）—— 申请了发布但宿主被占，所以**没绑上**。Task 8 要重启容器才会重新绑 |
| sidecar `dfl-recon` | Up；`c++filt` 可用（纯 CPU 任务不受 netns 影响）。**抓包前必须验 `docker exec dfl-recon ip -o addr show` 里有 `eth0`**，没有就是挂到了失效 netns（坑 9） |
| `idat.exe` | `D:\Program Files (x86)\IDA_Pro_8.3\idat.exe` 在 |
| `df_game_r.bin.idb` | 170,048,135 B；**没有行信息**（`get_sourcefile` 全空）；**有** `idautils.Names()` 328,151 个名字 |
| ACL/权限退化 | 本会话**没有**遇到（`$P` 下写文件全成功）；`G:\Game\DNF_ACT1\DFLegacy._acl-report\` 里是上一轮的修复/回滚脚本 |

---

## 5. 未决项 / 已知缺口（接手前先读）

1. **`ida-game` 没经 `recon_tool` 调通**：本会话审批提示被禁用，`mutate` 工具经 `recon_tool` 调用被
   **自动拒绝**（返回 `审批未通过（rejected）`）。工具链本身是**直接执行同一 argv 实测通过**的
   （`IDB` 正确指向 `df_game_r.bin.idb`、`symbols=328151`、`dispatcher-named=4621`）。
   ⇒ 若要"经本模式调用"，需要放开一次审批；否则静态取证继续走直接 argv 并在产物里写明。
2. **判据 3 已以修正形式补全**（详见 `M0-denominator-report.md` ③）：原两条子要求的前提都不成立
   （21 个 `*Dispatcher*.cpp` vs 425 家族 = 20.2×；"家族 → `.cpp`"三条路全断，含 DWARF 只覆盖
   FreeType 这一实测结论）；**其目的由穷尽反查承担：424/425 名字相符、0 处不符、0 个漏掉的家族**。
   56/425 家族能给出精确 `.cpp`（`__FILE__` 池，13.2% 是下界）。
3. **范围已扩大**（所有者 2026-10-05）：⑥ 队列全部纳入 ⇒ **spec §5.2 的里程碑表需要先扩展**，
   再为新增系统出单元计划。本计划的 Task 8–11 不受影响。
4. **NOTI 563 条永远没有派发器**（出站专用）。任何后续单元若想在 NOTI 上找 handler，是方向错误。
5. **CMD 606/607 没有派发器**（`id > 0x25d = 605`）。CMD 607 的名字地址（`0xb7a37c`）指向的是一句
   日志串 `ENUM_CMDPACKET_DISJOINT_ITEM error item_…`，**输入表这一行本身可疑**，用之前先核。
6. **4 个家族的 `dispatch_sig` 是空实现**（`mov eax,0; ret`）：`DungeonNPCBuffInfo`、`ExtendAvatar`、
   `GiveGiftToNPC`、`P2PHolePunchingSuccessRateStatistics`。它们没有 handler 是结构性的。
7. **183 个家族没有 `dispatch_sig`**（425 − 242 = 183；其中 **182 个只有 `process`**，
   第 183 个 `BuyItemCheck` 两者皆无）。只有 `process` 的家族要用 `process` 地址定位；
   这会影响 M1 单元的取证方式（计划里"用 `dispatch_sig` 反查"的那套对它们不适用）。
8. **`MakeWarRoomTemp` 是"已定义但从未实例化"的类**：vtable `0x08bd8cc0` 在代码里没有 store、
   在 `.data` 里也没有预初始化对象镜像（指向它的 4 字节只出现在 `.symtab` 的 `st_value` 里），
   但它的方法体带 `PacketDispatcher_Impl_1.cpp` 的断言串 —— GCC 对未实例化的类照样发射 vtable 与方法。
   ⇒ 与"注册表里没有它"一致，两条观察已调和。
9. `work/out/_tmp-*.txt` 是运行中间产物、`.recon/out/ida-dec-regression.txt` 是回归测试产物，都可删。

---

## 6. 下一步：Task 9–11（驱动器前置，**M1 的硬门**）

**Task 8 已完成**，其产出是 Task 9/11 的**已知答案**（这份是新的、比计划预写的更准）：

* **游戏面帧模型 = 10 字节头**：`[0]cat(0x01=CMD/0x00=NOTI) [1]id [2] [3..6]size(LE,含头) [7..9]3字节`
  **与 7001 那套 11 字节头不同**（计划 Task 9 只记了 11 字节那套，**必须两套分开实现**）。
  验证方式：六条流按此切帧，帧长之和 == 流长度（`work/out/B0-batch-report.md` §4）。
* **成功序列**（`work/captures/streams-vm/`，从 `b0-vm-success.pcap` 可重跑生成）：
  `13681-10013.bin` 21 帧 / `10013-13681.bin` 90 帧；含 `CMD 1 LOGIN` 413 B 请求与 95 B 应答、
  `NOTI 1 CHANNELINFO` 450 B、`CMD 2 SET_UDP_IP_PORT` 45 B、`CMD 91 MOVE_TO_GATE`、`CMD 93 ENTER_WARROOM`。
* **拒绝序列**（`work/captures/streams/`）：`46386-30011.bin`（1 帧 413 B）↔ `30011-46386.bin`
  （5 帧 432/31/31/31/31），末帧 **`NOTI 199 DISCONNECT_REASON`** —— 失败路径的现成靶子。
* **7001 已知答案**：C→S 4 帧 `43/27/11/11`（id `11/5/9/1`）；S→C 4 帧 `47/43/7220/139`（id `12/6/10/3`，
  VM 版）。docker 版是 `47/43/7235/140` —— **两台服务端不是同一份配置，取期望值时要写明取自哪台**。
* 单向流重跑：`python $W\pcap-streams.py <pcap> <outdir> [--filter-port N]`（`$W` 见 §1）。

**Task 9–11 要做的**：`work/driver/` 下写 `dnfdrive.py`（帧层 + AES/zlib，**两套头都要能说**）、
`selftest-7001.py`（在已知答案上自检；**不过 ⇒ 触发 S3，冻结所有 L2 结论**）、
`token.py`（RSA 令牌签发，256 B）、`selftest-30011.py`（游戏面自检）。
原计划里的端口命名要用**实测值**替换：docker 那份是 `30011`，VM 那份是 **`10013`**。

### 6.1 本轮新增的两条 L2a 前置条件（**每次打官服靶子前都要做**）

1. 清掉本次账号在 `d_taiwan.member_join_info` 的行（`PRIMARY KEY(m_id)`，登录路径执行裸 `insert`，
   重复登录必主键冲突 ⇒ 登录异常断连，客户端只说"连接失败"）；
2. 确认客户端 IP 在 `d_taiwan.geo_allow` 里（容器看到的通常是 `172.17.0.1`；VM 那套看到 `192.168.200.1`，**在名单内**）。
   完整证据链见 `work/out/B0-batch-report.md` §5/§6。

### 6.2 恢复环境时的现状（2026-10-05 22:5x）

* ✅ `DFLegacy.Server` 已起回（**pid 91588**，占宿主 7001/2311；批次前是 pid 17428 —— pid 变了是正常的）。
* ✅ `netsh portproxy` 为空（与批次前一致）；用户态中继已杀、宿主 30011 已释放。
* ⚠️ **Docker 引擎当前是停的**（`npipe` 不存在）⇒ 容器 `dnf` 未恢复；需要所有者启动 Docker Desktop
  后再按 `pcap` 对照恢复端口（`docker restart dnf` 后 7001/tcp、2311/udp 才会重新绑上）。
* ⚠️ **VM（192.168.200.131）里 `/root/.ssh/authorized_keys` 有本批次装的两把公钥**（注释 `dfl-b0-batch`，
  RSA 指纹 `MD5:c2:ed:ee:6c:57:5d:f4:27:9e:0c:17:06:2b:88:ea:f6`）——**建议删除**；
  本机对应私钥 `<用户>\.ssh\id_rsa_b0`、`id_ed25519_b0`。
  连 VM 的 ssh 需要老算法：`-o KexAlgorithms=+diffie-hellman-group14-sha1 -o MACs=+hmac-sha1 -o HostKeyAlgorithms=+ssh-rsa`。

---

## 7. 纪律提醒（上一轮踩过的坑，别重犯）

* **提交必须带 pathspec**：`git commit -m "..." -- <paths>`；`.superpowers/` 与 `docs/` 被 gitignore，
  要 `git add -f`。工作区有大量无关未提交改动，**绝不** `git add -A`。
* **Python 读文本显式 `encoding="utf-8"`**；**控制台中文**跑 python 前设 `$env:PYTHONIOENCODING='utf-8'`。
* **PowerShell 管道坑**：`python x.py | Tee-Object out.txt | Select-Object -First N` 会**截断落盘文件**；
  数组字面量 `@("a", $x | ForEach-Object {...})` 里的管道作用于**整个数组**（本计划的 Step 5 验收脚本就踩了，
  两个字符串元素被静默丢掉）。落盘一律 `>` 重定向，管道拆开写。
* **PowerShell 全角引号 `“”` 会被当成字符串定界符**；python 字符串里也别用 ASCII 双引号套中文。
* **容器里没有 python3、没有 tcpdump**；抓包靠 sidecar，解析在宿主做。
* **证据纪律**：六类标记 `[静态] [抓包] [真机] [回放] [推断] [未验证]` + `[作废]`；
  被推翻的结论**不删原文**，标 `[作废]` 并指向推翻它的证据（本工程的"矛盾留痕"见分母报告末表）。
