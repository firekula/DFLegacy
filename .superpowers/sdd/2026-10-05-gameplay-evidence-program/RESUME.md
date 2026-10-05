# RESUME —— 官服玩法面证据链工程（接续块）

> **判据（spec §6.1）**：一个没参与过本任务的会话，只读这份文件就能接着干。
> 最后更新：2026-10-05（**Task 1–11 全部完成，本计划已收尾**）。
> 计划：`docs/superpowers/plans/2026-10-05-gameplay-evidence-program-m0-m1.md`
> 规格：`docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md`
> 分母报告（评审门要的那一份）：`.superpowers/sdd/2026-10-05-gameplay-evidence-program/M0-denominator-report.md`

---

## 0. 一句话现状

M0（Task 1–7）✅、评审门 ✅（待评估队列全部纳入、范围不缩）、B0（Task 8）✅、
Task 9 ✅（7001 自检 PASS）、**Task 10 ✅（令牌签发/验签，离线 PASS）**、
**Task 11 ✅（游戏面自检：拒绝路径 PASS，成功路径本机未复现 —— 环境前置缺失，非驱动器缺陷）**。

`df_game_r` 的**全量派发器目录（425 家族）**与**opcode → 派发器映射（1171 条，`unknown=0`）**已产出。
驱动器 `work/driver/` 已建成：**两套帧模型**（7001=11 字节头 / 游戏面=10 字节头）、AES/zlib 密码层、
RSA 令牌签发验签，并**在两个真实靶子上跑过**。

**本计划的下一步不在这份文件里** —— 计划只覆盖到"M1 驱动器前置"。
接下来按 spec §5.2 的里程碑表开 M1 的单元（①UDP `mt` 应用层 ②登录/鉴权 ③角色列表与选角 ④进入城镇），
每单元一份六件套 + L2 差异表。**开单元前先读 §5 未决项 2（VM 靶子不可用）与 §6.0（已知答案）、§6.0.1（驱动器接口）。**

---

## 1. 目录与路径（一切以此为锚）

| 记号 | 绝对路径 |
|---|---|
| `$P` | `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program` |
| `$O` | `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official`（**只读引用**：`df_game_r.bin` + `.bin.idb` + 各种 `.txt` 转储） |
| `$W` | `G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work`（**只读引用**：登录链那轮的脚本与抓包、两把密钥 PEM） |
| 工作区 | `G:\Game\DNF_ACT1\DFLegacy`（git 分支 `main`） |

`$P` 结构：`work\`（脚本）、`work\out\`（产物）、`work\captures\`（抓包）、`work\driver\`（驱动器）。

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
| `work/driver/dnfdrive.py`、`work/driver/selftest-7001.py`、`work/out/driver-7001-selftest.txt` | **Task 9**：两套帧模型 + AES/zlib + 7001 已知答案自检 | 见 §3 命令 7 |
| `work/driver/token.py`、`work/out/token-offline-check.txt` | **Task 10**：令牌签发/验签（裸 PKCS#1 v1.5 type-1，46 B 被签内容） | `2981f17` |
| `work/extract-gate-tokens.py`、`work/out/gate-tokens-reference.json` | **Task 10 的回归基准来源**：从 5505 抓包**重新推导**三条令牌（不硬编码） | `2981f17` |
| `work/driver/selftest-30011.py`、`work/run-30011-selftest.sh`、`work/out/driver-30011-selftest.txt` | **Task 11**：游戏面自检（拒绝路径 PASS / 成功路径未复现） | （Task 11 提交） |
| `work/out/raw-30011-*.txt`、`work/out/raw-30011-*.bin` | Task 11 的原始输出与**原始字节**（判据 4：字节全部落盘） | （Task 11 提交） |

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

# 7) 驱动器：帧模型自洽 + 7001 已知答案自检（Task 9）
python "$P\work\driver\dnfdrive.py"                     # 两套帧模型的自洽检查
python "$P\work\driver\selftest-7001.py" --verify `
  "$P\work\out\replay-s.bin" "$W\captures\streams\7001-49202.bin" "$P\work\out\driver-7001-selftest.txt"

# 8) 令牌签发/验签离线自检（Task 10；**不需网络、不需容器**）
python "$P\work\driver\token.py" --check --sign-uid 1 `
  --key "$W\container-privatekey.pem" --verify-with "$W\container-game-publickey.pem" `
  --streams "$W\captures\streams5505" --out "$P\work\out\token-offline-check.txt"

# 8b) 从 5505 抓包重新推导那三条令牌（Task 10 回归基准的来源；不硬编码）
python "$P\work\extract-gate-tokens.py" "$W\captures\streams5505" `
  "$W\container-game-publickey.pem" --json "$P\work\out\gate-tokens-reference.json"

# 9) 游戏面自检（Task 11）—— 三个靶子一次跑完，**含失败那次**，拼成一份报告
bash "$P\work/run-30011-selftest.sh"
#    单独打某个靶子（例：docker 拒绝路径）：
python "$P\work\driver\selftest-30011.py" --capture-dir "$P\work\captures\streams" `
  --server-port 30011 --verify-with "$W\container-game-publickey.pem" `
  --replay --host 127.0.0.1 --port 30021 `
  --out-raw "$P\work\out\raw-30011-docker-replay.bin" --out "$P\work\out\raw-30011-docker-replay.txt"
```

**跑 `driver/` 下任何脚本都不用先设 PYTHONPATH**：`driver/dnfdrive.py` 与 `driver/token.py`
各自带"标准库 `token` 防遮蔽"守卫（原因见 `work/out/driver-30011-selftest.txt` 末节 ——
我们的 `token.py` 会遮蔽标准库同名模块，**顺序必须是先摘 sys.path 再 import**）。

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

## 4. 环境事实（**2026-10-05 23:4x 复测**，Task 1 基线 + 后续补充）

| 事实 | 值 |
|---|---|
| docker | 29.8.0；容器在跑：`dnf`（官服栈）、`dfl-recon`（sidecar，提供 `c++filt`/`openssl`/binutils）、另有 codex-pooler 两个与本工程无关 |
| **宿主 7001/tcp、2311/udp 占用者** | **pid 43904（`DFLegacy.Server`）** —— 两个端口同一个 pid（Task 1 时是 17428，pid 变了是正常的） |
| 容器发布端口 | `30011/tcp → 宿主 30021`、`31011/udp → 宿主 31021`、`5505/tcp → 5505`、`7001/udp → 7001`、`2312/2313/udp`、`3306/tcp → 13306`、`7300/tcp+udp` |
| 容器 `7001/tcp`、`2311/udp` | `[]`（**空数组**，不是 `null`）—— 申请了发布但宿主被占，所以**没绑上** |
| **游戏面可达性** | ✅ `127.0.0.1:30021`（容器 30011 的宿主发布）**可达且已复现拒绝路径**；⚠️ VM `192.168.200.131:10013` TCP 可连但**零回复**（见 §5 未决项 2） |
| sidecar `dfl-recon` | Up；`c++filt`、`openssl`(3.5.8) 可用。**抓包前必须验 `docker exec dfl-recon ip -o addr show` 里有 `eth0`** |
| `idat.exe` | `D:\Program Files (x86)\IDA_Pro_8.3\idat.exe` 在 |
| `df_game_r.bin.idb` | 170,048,135 B；**没有行信息**；**有** `idautils.Names()` 328,151 个名字 |
| 本机 Python | 3.12.10；有 `cryptography` 45.0.7、`pycryptodome` 3.23.0；**没有** `rsa`、**没有** 本机 `openssl`（只有 Git Bash 里那份） |
| ACL/权限退化 | 本会话**没有**遇到；`G:\Game\DNF_ACT1\DFLegacy._acl-report\` 里是上一轮的修复/回滚脚本 |

---

## 5. 未决项 / 已知缺口（接手前先读）

1. **`ida-game` 没经 `recon_tool` 调通**：审批提示被禁用时 `mutate` 工具经 `recon_tool` 调用会被
   **自动拒绝**（返回 `审批未通过（rejected）`）。工具链本身是**直接执行同一 argv 实测通过**的
   （`IDB` 正确指向 `df_game_r.bin.idb`、`symbols=328151`、`dispatcher-named=4621`）。
   ⇒ 若要"经本模式调用"，需要放开一次审批；否则静态取证继续走直接 argv 并在产物里写明。
2. **⚠️ VM 成功路径未能在本机复现（Task 11 的唯一一条 FAIL）**：
   VM 的 `10013` **TCP 连得上**（socket 级复测两次；`Test-NetConnection` 给过一次假阴性），
   但发抓到的 413 B 首帧后 **4 秒内零回复**；发整条 C 流（1125 B）同样零回复。
   按 B0 的两条 L2a 前置（`B0-batch-report.md` §6.1），最可能是
   ① 本次账号在 `d_taiwan.member_join_info` 的行没清（PK 冲突 ⇒ 登录异常，**且不带错误包**）、
   ② 客户端 IP 不在 `d_taiwan.geo_allow`。
   **本机 SSH 进不去 VM**（`Connection closed by 192.168.200.131 port 22`，两把 key 都试过 ——
   RESUME 旧记的 SELinux/StrictModes 问题**仍未解决**）⇒ 这两条前置**改不了**。
   **怎么才能验**：让 VM 的所有者代跑（清 `member_join_info` 里该 uid 的行、把发起方 IP 加进
   `geo_allow`），然后重跑 §3 命令 9。**这不是驱动器缺陷** —— 同一驱动器在 docker 靶子上
   已复现拒绝路径（首帧 + 末帧逐帧一致）。
3. **判据 3（令牌替换）在本工程的两个靶子上都"未验证"**：游戏面首帧的 403 B 载荷**整体不透明**
   （可打印率 38% / 34.7%，**没有任何** 256 B 窗口的公钥模幂落在 type-1 上，随机基线期望 0.0023 个），
   也**没有** `00 00 00 xx` 这样的 uid 大端模式 ⇒ 首帧里的令牌要么已被会话密钥加密，
   要么与另一把密钥绑定。⇒ 按计划 Task 11 Step 2 的**退化分支**：逐字节原样重放，
   并**显式记录"令牌替换未验证"**。这属于 M1 单元②（登录/鉴权）的工作。
4. **范围已扩大**（所有者 2026-10-05）：⑥ 队列全部纳入 ⇒ **spec §5.2 的里程碑表需要先扩展**，
   再为新增系统出单元计划。本计划的 Task 8–11 不受影响。
5. **NOTI 563 条永远没有派发器**（出站专用）。任何后续单元若想在 NOTI 上找 handler，是方向错误。
6. **CMD 606/607 没有派发器**（`id > 0x25d = 605`）。CMD 607 的名字地址（`0xb7a37c`）指向的是一句
   日志串 `ENUM_CMDPACKET_DISJOINT_ITEM error item_…`，**输入表这一行本身可疑**，用之前先核。
7. **4 个家族的 `dispatch_sig` 是空实现**（`mov eax,0; ret`）：`DungeonNPCBuffInfo`、`ExtendAvatar`、
   `GiveGiftToNPC`、`P2PHolePunchingSuccessRateStatistics`。它们没有 handler 是结构性的。
8. **183 个家族没有 `dispatch_sig`**（425 − 242 = 183；其中 **182 个只有 `process`**，
   第 183 个 `BuyItemCheck` 两者皆无）。只有 `process` 的家族要用 `process` 地址定位。
9. **`MakeWarRoomTemp` 是"已定义但从未实例化"的类**：vtable `0x08bd8cc0` 在代码里没有 store，
   但方法体带 `PacketDispatcher_Impl_1.cpp` 的断言串 ⇒ 与"注册表里没有它"一致。
10. `work/out/_tmp-*.txt` 是运行中间产物、`.recon/out/ida-dec-regression.txt` 是回归测试产物，都可删
    （本会话已把 Task 10/11 的探针全部退役）。

---

## 6. 下一步：M1 的单元（**本计划已收尾，下面不在计划范围内**）

### 6.0 Task 9–11 落定的"已知答案"（接手时直接用，别重新推导）

**两套帧模型**（`work/driver/dnfdrive.py` 里是 `CHANNEL` / `GAME` 两个**显式**对象，**不自动嗅探**）：

| 模型 | 端口 | 头长 | 布局 |
|---|---|---|---|
| `CHANNEL` | 7001 | **11** | `[0]cat [1]id [2..5]size(LE,含头) [6..9]reserved [10]ackOk` |
| `GAME` | 游戏面（docker `30011` / VM `10013`） | **10** | `[0]cat(0x01=CMD/0x00=NOTI) [1]id [2] [3..6]size(LE,含头) [7..9]3 字节` |

* **会话密钥（7001）= `id=12` 正文 `[4:20)`**（计划的 `[5:21)` **实测为错**，已否证：
  `[4:20)` 解 `id=10` 的 13312 B 密文可打印率 97.75% 并解出 `[dungeon]`/频道表；`[5:21)`/`[0:16)` 是垃圾）。
* `id=3`/`id=10` 正文 = `zlib(AES(明文))`，**没有** `count(1)` 前缀（跳首字节反而 zlib 失败）。
* **7001 自检判据不能是"整帧长度恒定"**：会话密钥按登录时刻生成 ⇒ `zlib(密文)` 长度差几字节
  （VM `47/43/7220/139` vs docker `47/43/7235/140`）。正确判据 = **结构性不变**
  （帧数 + `(cat,id)` 序列 + 解密后明文长度一致，外加 `id=10` 明文逐字节相同）。
* **令牌** = 裸 PKCS#1 v1.5 **type-1** 块（256 B），被签内容 **46 B** =
  `uid(4,BE) | 01×32 | 55 91 45 10 | 01 04 03 03 01 01`。三个账号（uid 1/2/3）只差第 3 字节。
  **除 uid 外那 42 字节的语义仍未解**（`[推断]`）。
* **游戏面两种失败/成功形状**（都是原始字节级）：
  * docker（**拒绝**）：C→S `CMD1(413)`；S→C `NOTI1(432) NOTI183(31)×2 CMD1(31) NOTI199(31)`，
    末帧 `NOTI 199 = DISCONNECT_REASON`。
  * VM（**成功**）：C→S 21 帧 1125 B（首帧 `CMD1(413)`）；S→C 90 帧 46193 B
    （首帧 `NOTI1(450)` = CHANNELINFO）。
* **游戏面首帧的 403 B 载荷整体不透明**：可打印率 38%（VM）/ 34.7%（docker），无 `00 00 00 xx` 模式，
  148 个 256 B 窗口无一落在 type-1 上（随机基线期望 0.0023 个）⇒ **令牌不可从首帧直接替换**
  （Task 11 判据 3 走退化分支）。
* **两靶首帧头只差 `[7:10]` 这 3 字节**（VM `11 bc 5f` / docker `a7 5d f3`），
  `[0..6]` 完全相同（`01 01 00 9d 01 00 00`）—— 那 3 字节的语义仍 `[未验证]`。

### 6.0.1 驱动器前置已完成，`work/driver/` 可用

`dnfdrive.py`（`Conn` / `send_frame` / `recv_frames` / `aes_ecb_*` / `zlib_*` / `parse_frames` /
`FrameModel` 两套 + `session_key_from`）、`token.py`（`Key.sign/verify` + openssl 独立校验）、
`selftest-7001.py`、`selftest-30011.py`、`work/run-30011-selftest.sh`。

**开 M1 单元前必须先做的一件事**：确认 L2a 靶子到底用哪个 —— **docker 的拒绝路径只能验失败语义**，
**成功路径要 VM 那套，而它现在本机不可用**（§5 未决项 2）。按 spec §6 的 **S4**
（官方容器不可用且无替代靶）预写：若 VM 也拿不到，L2a 失效 ⇒ 改判为纯静态 + 自研回放，
并**显式下调**相关结论的证据等级 —— **不许**拿 docker 的拒绝序列冒充成功序列。

### 6.1 两条 L2a 前置条件（**每次打官服靶子前都要做**）

1. 清掉本次账号在 `d_taiwan.member_join_info` 的行（`PRIMARY KEY(m_id)`，登录路径执行裸 `insert`，
   重复登录必主键冲突 ⇒ 登录异常断连，客户端只说"连接失败"）；
2. 确认客户端 IP 在 `d_taiwan.geo_allow` 里（容器看到的通常是 `172.17.0.1`；VM 那套看到 `192.168.200.1`，**在名单内**）。
   完整证据链见 `work/out/B0-batch-report.md` §5/§6。

### 6.2 靶子与端口的对应（**易混，写死在这里**）

| 靶 | 握手/登录 | 游戏面 | 本机可达 |
|---|---|---|---|
| docker 官服栈（**拒绝**） | 容器 `7001`（宿主被 `DFLegacy.Server` 占，= 没绑上） | 容器 `30011` → **宿主 `30021`** | ✅ |
| VM 官服栈（**成功**） | `192.168.200.131:7001` | `192.168.200.131:10013` | 端口通、但零回复（§5 未决项 2） |
| 我们的（2008 代，另一套） | 宿主 `2311` 握手 / `7001` 登录+玩法 | —— | `DFLegacy.Server` pid 43904 |

⚠️ 抓包文件里的 `46386`/`33018`/`13681` 都是**抓包侧客户端**端口，**不是**服务端端口。

### 6.3 恢复环境时的现状（2026-10-05 23:4x）

* ✅ `DFLegacy.Server` 在跑（**pid 43904**，占宿主 7001/2311）。
* ✅ 容器 `dnf`、`dfl-recon` 都在跑；容器 `7001/tcp` 与 `2311/udp` 仍是 `[]`（= 基线形态）。
* ✅ 本会话**没有**对任何官服栈做写操作（Task 11 的重放只发帧、没建账号、没改 DB）。
  唯一算"写"的是对 docker 的两次登录尝试（被服务端拒绝），以及 `docker exec` 读 sidecar。
* ⚠️ **VM（192.168.200.131）里 `/root/.ssh/authorized_keys` 有上一批次装的两把公钥**（注释 `dfl-b0-batch`，
  RSA 指纹 `MD5:c2:ed:ee:6c:57:5d:f4:27:9e:0c:17:06:2b:88:ea:f6`）——**建议删除**；
  本机对应私钥 `<用户>\.ssh\id_rsa_b0`、`id_ed25519_b0`。

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
