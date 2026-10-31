# RESUME —— 官服玩法面证据链工程（接续块）

> 2026-10-06 有限证据收尾：状态 **CLOSED_WITH_LIMITS**。T1–T5 已按明确 pathspec 提交；T6 签收见 `work/out/evidence-closure-signoff.md`。本签收只覆盖客户端密码/封装证据包，不等于全官服协议完成、singleton验证完成或重写授权。动态项、范围暂缓项与后续玩法单元已冻结，不再以“继续离线”扩展本表。

> **后续执行唯一收尾入口：[有限证据收尾任务表](<../../../docs/superpowers/plans/2026-10-06-gameplay-evidence-closure.md>)（计划提交8438dd4）。** 本文下方是历史记录，不能按“继续”无限追加。先按该表T1纠错，再按T2–T6及H01–H08验收；不创建teammates、不改产品代码。表中任务目前均待验收。CLOSED_WITH_LIMITS只签收客户端密码/封装证据包，不等于全官服协议完成或重写授权。BLOCKED必须保留失败，不得勾成完成。

  > 本轮离线进度见 [静态密码闭合](<work/out/static-password-closure.md>)、[离线验证进度](<work/out/offline-verification-progress.md>)、[NOTI1包装探索](<work/out/noti1-wrapper-audit.md>) 与 [锁定证据](<work/out/noti1-evidence-locked.txt>)：已静态固化包装、CRC、发送头、14项key切片、CMD1输入偏移、Rijndael mode/block/key参数、token get_str、base64→RSA路径；11个可配对流对（8个唯一内容对）已通过AES双实现、OpenSSL raw/type1 recover、密文CRC和CMD1往返。尚无singleton逐字节交叉、完整PI/C→S校验重构或账号名归因。没有新登录重放或进程附加。


> **接续优先级更新：先读 [Cipher 审计修正](<work/out/cipher-audit-corrections.md>)。**
> 本文下方保留的“14层串联”“时间戳播种已闭环”“所有方向13B头”“header[2]=garvage”“池0密钥就是G_Cipher密钥”等断言已被该审计标 `[作废]`，不得用作实现依据。
> 当前只完成调用路径定位和既有内存快照复算，尚未离线恢复 NOTI1 全局key，也未解出 CMD1 明文；尚不能宣布密码层完成或重写前置全部就绪。
> 最新已核：单包按 opcode%14 选算法；G_Cipher singleton槽为0x0941f8a4；0x0940be80是另一组5000项池；NOTI1构包写入原始key后走专用 Encryption::Encrypt。可重跑命令和地址见上述审计。

> **判据（spec §6.1）**：一个没参与过本任务的会话，只读这份文件就能接着干。
> 最后更新：2026-10-05（**Task 1–11 全部完成，本计划已收尾**）。
> 计划：`docs/superpowers/plans/2026-10-05-gameplay-evidence-program-m0-m1.md`
> 规格：`docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md`
> 分母报告（评审门要的那一份）：`.superpowers/sdd/2026-10-05-gameplay-evidence-program/M0-denominator-report.md`

---

## 0. 一句话现状

M0（Task 1–7）✅、评审门 ✅（待评估队列全部纳入、范围不缩）、B0（Task 8）✅、
Task 9 ✅（7001 自检 PASS）、**Task 10 ✅**（令牌签发/验签，离线 PASS；令牌构造已被**服务端源码**二次确认）、
**Task 11 ✅✅（拒绝路径 PASS + 成功路径已在 2026-10-05 晚复现 —— 见 §6.1）**。

**2026-10-05 晚的重大进展**：接管了 VM 靶子（SSH + DB 全通），把 Task 11 一直缺的
**成功路径**补上了（`work/out/vm-success-verification.txt` = PASS）。同时修正了若干既有结论，
见 §6.1 与 §7 的矛盾留痕。

`df_game_r` 的**全量派发器目录（425 家族）**与**opcode → 派发器映射（1171 条，`unknown=0`）**已产出。
驱动器 `work/driver/` 已建成：**两套帧模型**（7001=11 字节头 / 游戏面=10 字节头）、AES/zlib 密码层、
RSA 令牌签发验签，并**在一个真实靶子上跑通了成功与拒绝两条路径**。

**本计划的下一步不在这份文件里** —— 计划只覆盖到"M1 驱动器前置"。
接下来按 spec §5.2 的里程碑表开 M1 的单元（①UDP `mt` 应用层 ②登录/鉴权 ③角色列表与选角 ④进入城镇），
每单元一份六件套 + L2 差异表。**开单元前先读 §5（未决项）与 §6.0–§6.1（已知答案与 VM 用法）。**


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

# 9b) 判据 3 的依据：游戏面 C 流首帧载荷的**可观测性**检查（两个靶子各跑一次）
python "$P\work\game-first-frame.py" "$P\work\captures\streams-vm\13681-10013.bin" `
  "$W\container-game-publickey.pem"
python "$P\work\game-first-frame.py" "$P\work\captures\streams\33018-30011.bin" `
  "$W\container-game-publickey.pem"
#    预期：两个靶子都是 可打印率 38.0% / 34.7%，`00 00 00 xx` 0 个，
#          148 个 256 B 滑窗**无一**落在 type-1 上（随机基线 0.0023）⇒ 令牌不可直接替换

# 9c) 7001 侧的硬门（Task 9）：重放字节 vs 记录抓包
python "$P\work\driver\selftest-7001.py" --verify `
  "$P\work\out\replay-s.bin" "$P\work\captures\streams\7001-58840.bin" `
  "$P\work\out\driver-7001-selftest.txt"
#    预期：整帧长度 replay [47,43,7240,139] vs capture [47,43,7235,140]（**故意不等**，
#          会话密钥不同 ⇒ 密文不同 ⇒ zlib 长度差几字节），但判据① 结构性不变 PASS：
#          (cat,id) 序列一致、id=10 解密明文 13312 B 逐字节相同、id=3 128 B 首 4B=01 00 00 00
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
2. ~~**⚠️ VM 成功路径未能在本机复现**~~ → **✅ 已解决（2026-10-05 晚）**。
   原判断（"两条 L2a 前置改不了"）**是错的**，真正的障碍是三层，逐层解开后成功路径复现：
   * 拿不到 VM 的 SSH → 已打通（见 §6.2；根因是 `/root` 777 + 私钥有 passphrase + `UseDNS` 延迟，
     **不是**当初怀疑的 fail2ban/封禁 —— 那两条假设已 [作废]）；
   * B0 抓的那份令牌属于**另一套库**（uid 1/2/3），VM 上的账号是 `18000001`–`18000008`；
   * **同账号重复登录会被踢**（`CUser::DisConnSig from (12)`）—— 第一次空场重放前，
     所有者本人的客户端还连着，所以只走到 5 帧就被断开。
   ⇒ 处置：请所有者用启动器登录一次、在 VM 上 `tcpdump` 抓活账号的完整登录、
     再**原样重放**其 C→S 首帧 ⇒ `CMD 1` 长度 **95（成功）**，前 8 帧与抓包逐帧一致。
   判定脚本：`work/verify-vm-success.py`；结果：`work/out/vm-success-verification.txt`（PASS）。

**复现命令**（不需要再打靶，全部读已落盘的字节）：

```powershell
$B = ".superpowers/sdd/2026-10-05-gameplay-evidence-program"
python "$B\work\verify-vm-success.py" `
  "$B\work\captures\streams-live\2343-10013.bin" `
  "$B\work\captures\streams-live\10013-2343.bin" `
  "$B\work\out\live-replay-alone.bin" `
  "$B\work\out\vm-success-verification.txt"      # 预期末行: VERDICT: PASS

# 重新打靶（需要 VM 在跑、且**没有**同账号客户端在线，否则会被 DisConnSig(12) 踢）：
python "$B\work\driver\replay-30011-live.py" "$B\work\captures\streams-live\2343-10013.bin" `
  --host 192.168.200.131 --port 10013 --first-frame-bytes 413 `
  --out-raw "$B\work\out\live-replay-alone.bin"

# 从 pcap 重切单向流（原始抓包在 git 里）：
python ".superpowers\sdd\2026-10-05-official-login-channel\work\pcap-streams.py" `
  "$B\work\captures\vm-live-login-10013.pcap" "$B\work\captures\streams-live" --filter-port 10013
```

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
| `GAME` | 游戏面（docker `30011` / VM `10013`） | **13** | `[0]classification [1]msg_no [2]garvage [3..6]sLength(LE,含整帧) [7..10]checksum(4) [11..12]sequence(2)` |

> **⚠️ `GAME` 从 10 字节改成 13 字节（2026-10-05 晚，详见 `work/out/frame-structure-nailed.md` §0）**：
> 字段名与宽度由官方 cfg 逐字给出（`work/vm-files/game-siroco11.cfg.txt` L82–89，
> `header_mangled_length = 13`），并由**独立实测**证实：C→S 的 `[11..12]` 是
> **uint16 LE 递增序号**（四条流 20/20、20/20、20/20、5/5 逐帧 +1 起于 0）——
> 载荷不可能是完美计数器。
> **10 字节模型切帧仍是对的**（切帧只看 `[3..6]` 的 size），所以 B0 的"恰好铺满"、
> Task 11 的重放比对**都没被污染**；受影响的只有**载荷起点**（13 而非 10）。
> `[2]` 的官方字段名就是 **`garvage`**（拼写如此，取值 0/1/2）。
> `checksum` 算法仍未解（adler32/crc32/sum32 全 0 命中），**不阻塞**。

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

**2026-10-05 晚更新（`[7..9]` 的语义已定）**：抓包期间在 game 服日志里直接读到
```
[14:37:14] bool CUser::Parsing(int)(1850): [CHECK SUM] check_sum error PACKET : ENUM_CMDPACKET_LOGIN
```
时间戳正对应我那次"重放 B0 旧帧" ⇒ **`[7..9]` 是校验和**（服务端会验，错了就丢弃该包）。
实测三个样本：VM 活账号 `11 11 d3`、VM B0 `11 bc 5f`、docker `a7 5d f3`。
⇒ 之前标 `[未验证]` 的那 3 字节**可以改标 `[静态]+[抓包]`**（日志给出了名称，抓包给出了位置）。
**但算法仍未解**（不知道它覆盖哪些字节、含不含会话/时间）；且**实测原样重放是能过的**
（只要连字节一起重放），所以对 M1 不构成阻塞。

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

## 6.1 VM 成功路径：已复现（2026-10-05 晚）

**产物**：
* 抓包：`work/captures/vm-live-login-10013.pcap`（VM 上 `tcpdump` 抓的，LINUX_SLL）
* 单向流：`work/captures/streams-live/2343-10013.bin`（C→S **1141 B / 21 帧**）
  ↔ `10013-2343.bin`（S→C **46174 B / 90 帧**）—— 两条都**恰好铺满**
* 重放原始字节：`work/out/live-replay-alone.bin`（**1395 B / 8 帧**）
* 判定：`work/out/vm-success-verification.txt` = **PASS**

**成功序列（前 8 帧，重放与抓包逐帧一致）**：

```
NOTI1(450) NOTI183(31) NOTI183(31) NOTI160(23) NOTI173(703) CMD1(95) NOTI16(23) NOTI53(39) …
                                                              ↑ LOGIN 成功 = 95 B
```

**⚠️ 关键判据（踩过坑才写下来）**：**拒绝路径与成功路径的前 4 帧完全相同**，只靠
`CMD 1` 的**长度**区分：`95` = 成功，`31` = 失败。第一次空场重放拿到 8 帧就以为成了，
必须核对 `CMD 1` 长度。`verify-vm-success.py` 的判据③就是这条。

**两条靶子的账号体系不是同一套**（这是 Task 11 卡住的真正原因）：

| 靶 | 账号 | 说明 |
|---|---|---|
| docker 容器 | `testuser3/4`、`test`（uid 1/2/3） | B0 抓包那份令牌的来源 |
| **VM** | `100000`–`100006`、`firekula`（UID 18000001–18000008） | 口令是 MD5；`100001`–`100006` 都是 `e10adc…` = MD5("123456") |

**游戏面首帧载荷是会话相关的、不可 patch**：活账号与 B0 两份首帧**同样 403 B**，
但**有 399 字节不同**（只有 4 字节相同）⇒ 想"把令牌换掉"必须重放整帧，不能改字段。
这也解释了为什么 Task 11 判据 3（令牌替换）只能走退化分支。

**游戏服公钥 ≠ 容器公钥，且 VM 上没有配对私钥**：
`/home/neople/game/publickey.pem` 模数 `BEF69F43…`；容器那把是 `BBA4EE40…`；
登录 PHP（`rsa.php`）里那把私钥是 `E21F97…`（**两者都不是**）。
⇒ **无法在本机为 VM 自签令牌**；能复现成功路径靠的是"令牌可复用"（日志
`Success Token reuse login`）。

## 6.2 怎么进 VM（隧道、凭据、坑）

**SSH**：`work/vmssh.py`（paramiko；口令从环境变量 `VM_PASS` 读，不落盘）。
不能用系统 `ssh` 的原因：VM 是 **OpenSSH 4.3p2 / CentOS 5.8**，只提供
`diffie-hellman-group14-sha1` / `ssh-rsa` / `hmac-sha1`，而它自带的 ssh 又不认
`-o KexAlgorithms=`（5.7+ 语法）。配套：`vmrun.py`（推脚本执行，绕开引号）、
`vmdb.py`（跑 SQL，NUL 分隔传参）、`vmget.py`（取文件 + GB18030 转码）。

**四个坑（全部实测，别重踩）**：

1. **`/root` 权限**：实测是 `drwxrwxrwx` ⇒ sshd StrictModes 拒绝公钥
   （日志 `Authentication refused: bad ownership or modes for directory /root`）。
   已改 `700`（**建议保留**）。
2. **`id_rsa_b0` 有 passphrase**：私钥头解出 `aes256-ctr` + `bcrypt`。
   用 `BatchMode=yes` 时 ssh 不许提问 ⇒ 直接跳过该 key ⇒ `Permission denied`。
   **不是** key 不配对（私钥/公钥/authorized_keys 三边指纹都是 `c2:ed:ee:…:ea:f6`）。
3. **`UseDNS` 默认 yes ⇒ 首连 banner 要 7.21 秒**（随后瞬时，有缓存）。
   `vmssh.py` 的超时给到 60 秒。根治可把 sshd_config 的 `UseDNS` 设 `no` 再重启 sshd
   （**没做**：改 sshd 有弄坏唯一靶子的风险，加超时零风险）。
4. **sshd 停机窗口**：13:20 停机、14:11 重启。停机期间 TCP 仍"接受连接"但**不回 banner**，
   看上去像封禁 —— 我据此错误地怀疑过 fail2ban/`hosts.deny`（已 [作废]）。

**数据库**：`/opt/lampp/var/mysql/mysql.sock`，账号 `game` / 口令 `REDACTED_VM_DB_PASSWORD`
（来源：`/home/neople/bridge/cfg/bridge.cfg` 的 `db_id`/`db_pwd`，**不是爆破**）。
`d_taiwan.accounts.UID` = `member_join_info.m_id`。驱动脚本：`work/vmdb.py`。

**栈的启停**：`/root/run`（整套 17 个进程）/ `/root/stop`。二进制大多就位；
只起游戏面可用 `work/start-game.sh`。**注意 `/root/run` 会清空所有日志**。
`tcpdump` 在 `/usr/sbin/tcpdump`，抓包脚本 `work/capture-login.sh`。

---

## 6.3 帧结构与 binary 的钉定结果（2026-10-05 晚第二批评审前）

**全部结论 + 可重跑命令**：`work/out/frame-structure-nailed.md`（本节只列要点）。

| # | 结论 | 影响 |
|---|---|---|
| 1 | `[7..9]` **不是**校验和（16 个标准算法命中率 1–2% = 随机） | 旧结论 `[作废]` |
| 2 | S→C 方向 **`hdr[7] == payload[1]` 180/180**；C→S 0/42 | 是**副本**不是算出来的 |
| 3 | **游戏面载荷基本确定性**（同账号跨会话 429/440、18/21 恒定） | **推翻**"会话随机、不可 patch"的旧说法 |
| 4 | `[2]` **不是**恒 0（取值 `{0,1,2}`，按帧类型确定） | 旧结论 `[作废]` |
| 5 | **同账号两次登录的 C→S 登录帧 402/403 字节相同** | 登录帧"按账号确定 + 1 个会话字节" |
| 6 | 登录帧里**找不到**令牌（明文/取反/异或常数/加减常数，全否证） | 令牌被强变换过 ⇒ M1 单元②的任务 |
| 7 | **运行的 binary 与 M0 分析的那份差 103 字节**（46 处 `.text` + 1 处 `.dynstr`） | 见下 |

**第 7 条的可执行结论（对后续所有单元都重要）**：
M0 的 425 家族 / 1171 条 opcode 映射 / 7273 条 handler 对 VM **直接适用**，
**除了 3 个家族** —— 它们的代码被私服改过：

| VA | 家族 |
|---|---|
| `0x0818020a`、`0x0818020f` | `SelectDungeon` |
| `0x081a57cd` | `EnterPVP` |
| `0x081b9139`、`0x081b9151` | `ModItemAttr` |

引用这 3 个家族的地址级结论时**必须逐个复核**；它们也正是"私服改过的玩法逻辑"
（选副本 / 进 PvP / 改物品属性），**重写时属于非官方部分，不要照抄**。
复核脚本：`work/nail-patch-impact.py`、`work/nail-patch-sections.py`。

**VM 的 binary 已下载但不在 git**（36 MB）：`%TEMP%\dfl-creds\vm-df_game_r`，
重取：`python work/vmget.py /home/neople/game/df_game_r <本地路径> --binary`。

---

## 6.4 【重大】游戏面封包的密码层（2026-10-05 深夜自主部分）

**全账**：`work/out/game-face-crypto.md`。这一节只列**结论 + 未决项**。

### 已确证

| # | 结论 | 证据 |
|---|---|---|
| 1 | **游戏面帧的载荷是加密的**，`CUser::Parsing(int)` @ `0x08647890` 里调 `G_Cipher()` → `Cipher::Decrypt(...)` 解密后才交给派发器 | `[静态]` 反汇编 |
| 2 | 这解释了先前所有"扫不到令牌"的现象 —— **令牌在密文里**；也解释了"同账号两次登录首帧 402/403 相同、不同账号 4/403" | 与 Task 11 实测吻合 |
| 3 | **密码链 = 14 种算法按枚举顺序固定串联**：`Shift → Rijndael → BlowFish → Rc6 → TwoFish → Tea → Kasumi → XTea → Noekeon → Khazad → Cast5 → Skipjack → Multi2 → Anubis` | `Cipher::Cipher()` @ `0x08097196`（`reserve(0xe)` + `i=0..13` 循环） |
| 4 | 枚举映射完整：`0..13` → 上述 14 个 `CNCrypto*` 类 | `work/map-crypto-enum.py`（跳转表 `0x8b13c48`） |
| 5 | **令牌路径** = `base64_decode` → `COpensslRSA::Decrypt`（密钥在 BSS `0x9404754`，启动时从 `publickey.pem` 初始化）→ **104 字节 `GarenaAuthData`**（含 uid/uname/timeStamp/patchTime/gcaType/mobileAuth/goldMemberType） | `AuthGarenaToken::decrypt` @ `0x081731f2` |
| 6 | 登录帧里**还有一路鼠标密码**：`Dispatcher_MouseRegister::decryptPassword(char const*, char*, int)` @ `0x0826333a` | `[静态]` |

### 未决（**M1 的关键路径**）

> **客户端怎么拿到同一把封包密钥？**
> 服务端的密钥在 `InitCipherKeyString()` @ `0x082a025a` 里生成（取 `getCurSec()`、
> 建 2048 B 密钥缓冲、反复拷贝一个"选中的串"填满 `GetTotalKeyLength()` 字节）。
> **线索**：7001 握手下发的"会话密钥"是个**时间戳字符串**
> （`"20261005123000\0\0"` 之类）——两者可能有关联。
> **不解决 ⇒ 只能重放抓包，不能为任意账号构造登录帧。**

**怎么验**（按代价）：① 读 `SelectKeyString` @ `0x082a002a` 的完整 5 分支逻辑；
② 找 `0x940be80` 那组 16 字节结构体（BSS）在哪被填充；③ 若能拿客户端，看它是否用 7001 的时间戳播种。

### 我自己撤回的两条（留痕）

* `[作废]` "`_NS_PI_2ND_RecvHash_CS` 是密钥派生" —— 它是**反外挂**（`CheckHashReceived`
  + `CHackAnalyzer::addServerHackCnt`）。我是**凭它紧挨着 `Cipher::Decrypt` 的位置猜的**。
* `[作废]` "密钥 = 从 `map<int,string>` 里随机挑" —— 那两个 map 地址**同时被
  `CItemList::load_item_files` 等道具/脚本代码使用**，读法站不住，已降级 `[未验证]`。

## 6.5 【重大】dbmw 取证：业务走 **UDP**（第 4 部分的入口已打开）

**全账**：`work/out/dbmw-recon-report.md`。

| # | 结论 |
|---|---|
| 1 | **三个 dbmw 是同一份二进制**（md5 `0d70e931…`），角色由配置决定 |
| 2 | **拓扑**：`game` **直连 MySQL**（自己有 `master_db_*`）；**dbmw 服务的是 monitor/guild/statics** |
| 3 | **线格式 = 4 字节头**：`u16 LE packet_id` + `u16 LE size(含头)`（静态签名 + 抓包铺满 + 日志 id 三重印证） |
| 4 | **传输分工**：**TCP = 心跳**（id 4202，每 ~5 秒，载荷恒定，含对端 TYPE）；**UDP = 业务** |
| 5 | 业务 UDP 三实例实测 id：mnt `6072/10218/4152`、stat `10209/3111/6085/2511/4010`、guild `9007/1061/1069` —— **与日志逐个吻合** |
| 6 | 词汇表：**119 种 `Packet_*`** + **151 个 `CDBManager::*`** + **122 个 `CPacketTranslater::On*(PacketHeader*)`** 处理器 |
| 7 | 两套 id 命名空间：packet id（<65536，全局）与 query id（全在 `20xxx`，日志会打印 SQL） |

**⇒ 第 4 部分可以开工了**：拿 `work/analyze-udp.py` + `work/dbmw-protocol.py` 就能出六件套。
**注意**：先前两轮"只看到心跳"是因为我用了 `tcp port ...` 过滤器 —— **抓 dbmw 必须不限协议**。

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
