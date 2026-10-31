# E2 · 客户端 LOGIN 出站构造处（第一轮：入口定位，未到载荷）

日期：2026-10-07 · 工作区：`G:\Game\DNF_ACT1\DFLegacy`
上游：handoff §4.C（LOGIN 尾 12 B 生产者）· 本轮 C 项已把**消费侧**钉死（见 `dynamic-evidence-progress-2026-10-07.md` §3）

## 0. 状态

**未完成**：没有定位到客户端写那 400 B 载荷的代码，因此 **§4.C 仍未闭合**（尾 12 B 依然只能记为
"与 AES 块对齐吻合的零字节"，**不命名 padding**）。

**两轮共排除/确认了 6 条路径**（§2、§3、§4.1、§4bis、§4ter、§4quater），
并把"下一刀"收敛到**发送路径/载荷结构**（不能再用字段值当锚点 —— 见 §4q.3）。

**已完成**（全部 `[静态]`，可复跑）：见各节的"可证伪结论"与 §6 的脚本清单。

## 1. 客户端身份与工具链（先说清楚，免得后人重复踩）

| 项 | 值 | 来源 |
|---|---|---|
| 目标客户端 | `G:\Game\DNF_ACT1\DF\DNF.exe`，`9,846,272 B`，`SHA256 6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5` | `[静态]` 本机实测 |
| 与 handoff 的一致性 | 该 exe 里含 `'dnf_game'` 字符串（文件偏移 `0x684b1c`）；`Client_test2\DNF.exe` 同为 `9,846,272 B` ⇒ 目标客户端二进制在手 | `[静态]` |
| **PE 结构** | `image_base=0x400000`；节名是 `nsn`、`nsn t`、`nsn a`、`.vmp0`、`.SCY` ⇒ **被 VMProtect 加壳/改名** | `[静态]` `pefile` |
| 陷阱（我踩了） | **不能自己手写 PE 解析**：我第一版读出的 `image_base=0x598000`、节名 `nsn\x00t`，扫出 **0 个** `BeginCommand`，差点得出"客户端不发 CMD1"的错误结论。正确做法是用 `pefile` 或**项目既有的全量反汇编** | 留档 |
| **权威反汇编（推荐入口）** | `.superpowers\sdd\friends-protocol-recon\work\insns.txt`（54 MB，2,073,479 条，地址 `0x401000`–`0x9977fe`，2026-09-27 既有产物） | `[静态]` |

> 结论：**E2 后续都该走 `insns.txt`**（或 `DNF.exe.idb`），不要去解析加壳后的 PE 节表。

## 2. 排除项一：LOGIN **不是** `BeginCommand(1)` 发出的

用项目既有方法（`b-senders.py` 同规则：`call 0x8B2650` 往回找最近的 `push imm`）扫全部
**234 个 `BeginCommand` 发射点**，命令号直方图里 **没有 `cmd=1`**：

```
#   cmd=None: 26   cmd=65: 20   cmd=118: 8   cmd=136: 8   cmd=8: 5   cmd=13: 5
#   cmd=19: 5      cmd=57: 5    cmd=101: 5   cmd=10: 4   cmd=26: 4  …
#   （234 项全表见 work/out/e2-cmd1-sites.txt 的直方图）
```

而且那 26 个 `cmd=None` 的点逐个看下来，**最近的 `push` 其实是"字段值"而不是命令号**
（例：`0x008525f6 call BeginCommand` 之后才 `push 1; mov byte [ebp-0x10], 0xc`）。

⇒ **`CMD 1`（游戏面 `msg_no=1`）不走 `BeginCommand`**。这与项目里"组队/交易/好友那些 CMD
都走 `BeginCommand`"并不矛盾，而是说明**登录包走另一套构造路径**（见 §3）。

脚本与明细：`work/e2-cmd1-sites.py` → `work/out/e2-cmd1-sites.txt`
（另有 `work/client-begincommand-scan.py` 是我那版失败的 PE 解析，**保留在案作反面教材**）。

## 3. 排除项二：`DataManager=20080418` 在客户端**只被比较**，不是一个被写入的常量

`0x01326722`（BCD `22 67 32 01`）在 2,073,479 条指令里**只有 1 处**：

```asm
0x004199cc  call 0x404a60
0x004199d1  cmp  eax, 0x1326722      ; ★ 唯一的引用，而且是"比较"
0x004199d6  je   0x419a36
```

`DNF.exe` 里字节串 `22 67 32 01` 也只有 **1 处**（就是这条 `cmp` 的立即数）。
⇒ 登录帧里的 `DataManager` 字段值在客户端是**运行期算出来的**（或来自另一个内存位置），
**不能**拿它当"写入点"的锚点。脚本：`work/e2-find-login-sender.py` → `work/out/e2-login-anchors.txt`。

同理 `token_length` 的服务端上限 `344 (0x158)` 在客户端**不作为容量常量**出现
（16 处 `0x158` 里没有一处与 `0x18`/`0x84` 同窗）。

## 4. 已确认的"包构造框架"与下一刀（**这是本轮最有用的产出**）

### 4.1 客户端有一张"按 id 选包类/处理器"的分发表

在 `0x00573C50`–`0x00573D40` 一带有一段**连续的同构代码**，每个分支的形状完全一致：

```asm
0x00573ccb  push 0x834                     ; 分配大小
0x00573cd0  call 0x91857a                  ; operator new
0x00573cd8  mov dword ptr [ebp+8], eax
0x00573cdb  test eax, eax
0x00573cdd  mov dword ptr [ebp-4], 0x158   ; ★ 类 id = 344
0x00573ce4  je  0x575c2a                   ; 失败 → 走"未知类"路径
0x00573cf1  call 0x566b40                  ; 该类的构造函数
```

相邻分支的 id 是连续的：`0x156`(分配 `0x7e8`) / `0x157`(`0x80c`) / **`0x158`(`0x834` = 2100 B)** /
`0x159`(`0x7a0`) / `0x15f`(`0x7a4`) / `0x15d`(`0x7c0`) …
⇒ 这是**"按 id new 出对应长度的包对象"**的工厂。

**为什么这与 E2 有关**：游戏面登录首帧的载荷是 **400 B**，而 `0x158` 类的分配是 **2100 B**；
如果登录包就是这一类，那"400 B 载荷缓冲 / 是否 388 字节后全零"的答案就在 `0x566b40` 这个构造函数里。

### 4.2 客户端另有一张"命令号 → 处理器"的跳转表 `0x00417480`

用 `pefile` 读 `0x00417480` 起的连续 dword，得到一串**代码段地址**。与项目既有结论交叉验证：

| 表下标 | 值 | 项目既有结论 |
|---|---|---|
| 8 | `0x004102F6` | **CMD 10 的 handler**（`party-protocol-recon.md`，✓ 完全一致） |
| 10 | `0x00410799` | CMD 12 handler ✓ |
| 11 | `0x004107D8` | CMD 13 handler ✓ |
| 12 | `0x00410834` | CMD 14 handler ✓ |
| 13 | `0x004108AD` | CMD 15 handler ✓ |
| 14 | `0x004109BF` | CMD 16 handler ✓ |
| 17 | `0x00410D15` | CMD 19 result handler ✓ |
| 100 | `0x0041744A` | CMD 100 handler |
| **109** | **`0x00417467`** | **CMD 100 的 remap 目标 = 默认分支**（`party-protocol-recon-b-membership.md:603` 记 `CMD 100 remapIdx=109 handler=0x00417467`）✓ |

⇒ **两张口径同时成立但含义不同**：`0x158→0x4102F6` 说明 `0x4102F6` **既是** 下标 344 的值、
**又是** CMD 10 的 handler —— 因为 **CMD 10 的 remap 下标是 344**（`0x158`）。
也就是说：**表是 0-based 的"remap 下标 → handler"表**；`cmd→remapIdx` 另有映射
（CMD 100→109 已被项目记过），**不要把下标当命令号**（我第一版就是这么误读的，已留档）。

⚠️ 表在 `idx≈110` 之后就不再是 dword 指针数组了（`idx 110 -> 0x03020100` 明显是字节数据）
⇒ **表的边界没有钉住**，这也是下一刀要小心的地方。

### 4.3 E2 的下一刀（收敛到 2 个候选，二选一）

1. **`cmd→remapIdx` 映射表**：项目已知 CMD 100→109；把 `1→?` 读出来，再取 `table[remapIdx]`
   就是 **CMD 1 的处理器**（它一定在 `insns.txt` 里，且会读 400 B 载荷 →
   这正是"400 B 从哪来"的最近入口）。
2. **`0x566b40`（id `0x158`=344 的构造函数）**：若"COM(344)"与登录相关，构造函数里就是载荷缓冲。
   反编译它只需一次 IDA 调用：
   `idat -A -S"<ida_tool.py> <out> dec 566b40" DNF.exe.idb`

**判据（定案条件）**：在候选函数里读到"写入 token / 8 B mouse / 那 6 个尾字段"的代码，
并能读出**写入的总字节数**与**缓冲长度常量**；两者相等 ⇒ 400 B 载荷没有尾 12 B 生产端，
那 12 B 就是"缓冲余量/块对齐零填"（仍需与加密层的 `AdjustDataSize` 对齐才能定名）。

## 4bis. 修正：`0x00417480` 的下标**就是命令号**（本报告 §4.2 的"remap 下标"说法作废）

用**命令名表**（`0xB54430`）一读就清楚了 —— 它是**按命令号索引**的指针数组，而且第 1 项正是 LOGIN：

```
CMD 名表 @ 0xb54430
  [  0] 0x00a88210  ENUM_CMDPACKET_CHECK_CONNECTION
  [  1] 0x00a881f8  ENUM_CMDPACKET_LOGIN              ← ★
  [  2] 0x00a881d8  ENUM_CMDPACKET_SET_UDP_IP_PORT
  [  3] 0x00a881c4  ENUM_CMDPACKET_EXIT
  [  4] 0x00a881a4  ENUM_CMDPACKET_SELECT_CHARACTER
  [  5] 0x00a88184  ENUM_CMDPACKET_CREATE_CHARACTER
  [  6] 0x00a88164  ENUM_CMDPACKET_DELETE_CHARACTER
  [  7] 0x00a8813c  ENUM_CMDPACKET_RETURN_SELECT_CHARACTER
  [  8] 0x00a88120  ENUM_CMDPACKET_GET_USERINFO
  ...
NOTI 名表 @ 0xb54170
  [  0] 0x00a895fc  ENUM_NOTIPACKET_CHECK_CONNECTION
  [  1] 0x00a895e0  ENUM_NOTIPACKET_CHANNELINFO      ← ★ 与抓包 NOTI1 完全对上
  ...
```

**这一步把 §4.2 的误读纠正了**：`0x00417480` 表的下标**就是命令号**。
交叉验证同时成立：

| 表 | 下标 | 值 | 对照 |
|---|---|---|---|
| `0x00417480` | **1** | `0x00416EFF` | 名表 `[1] = ENUM_CMDPACKET_LOGIN` ⇒ **CMD 1 的处理器 = `0x00416EFF`** |
| `0x00417480` | 2 | `0x0040FAEF` | 名表 `[2] = SET_UDP_IP_PORT` |
| `0x00417480` | 10 | `0x004102F6` | 名表 `[10] = REQUEST_PEER`；项目记"CMD 10 handler `0x4102F6`" ⇒ **那次报告写的"CMD 10"其实是"下标 10"**，两者在 10 这一格巧合一致 |
| `0x00417480` | 109 | `0x00417467` | 项目记 `CMD 100 remapIdx=109 handler=0x417467` ⇒ 与"下标即命令号"**不冲突**（100 的 remap 落到 109 是另一层映射） |

> **教训（与 §1 的 PE 坑同类）**：`0x4102F6` 在项目里被记成"CMD 10 的 handler"，
> 我据此一度推出"下标 − 2 = 命令号"；**正确做法是先读名表**（它才是命名来源），
> 而不是从别处的结论反推索引。差 2 的那套说法已作废。

## 4ter. `0x00416EFF` 是什么（读过了，**不是**载荷构造处）

`0x00416EFF` 起是一段**跳转表的内联小分支**：每格形状都是

```asm
0x00416eff  mov  al, [ebp+0xc]      ; 入参：某个子类型/结果字节
0x00416f02  test al, al
0x00416f04  jne  0x417467           ; 不匹配 → 默认分支
0x00416f0a  lea  edx, [ebp-0xb7]    ; 栈上小缓冲
0x00416f11  call 0x8f5120           ; 读缓冲里的 1 字节
0x00416f1f  cmp  al, 7 / 0x41 ...
0x00416f30  mov  [eax+0x15e1e4], 0xd ; 写一个"状态/子模式"
0x00416f3a  jmp  0x417467
```

紧接着 `0x00416F54 / 0x00416F9E / 0x00416FDB / 0x00416FF2 …` 是同构的兄弟格，
而 `0x4173xx`/`0x41745f`/`0x417467` 是它们共同的汇合点。
⇒ **这一段是"按结果字节分派/置状态"的入口族，不是"把 token/mouse 写进 400 B 载荷"的地方。**

**所以 E2 的下一步不该从 `0x00416EFF` 往里挖**，而应该去找**真正构造载荷的那一侧**：

| 候选入口 | 地址 | 理由 |
|---|---|---|
| `0x566b40` | id `0x158`(344) 的包类构造函数（分配 `0x834`=2100 B） | "按 id new 出对应长度包对象"的工厂（§4.1）；若登录载荷装在这个类里，缓冲长度就在这里 |
| 名表 `[1]` 对应的**发送侧** | 待定 | 已知 `BeginCommand(1)` **不存在**（§2）⇒ LOGIN 的发送走的是**另一套 API**；应在 `insns.txt` 里找"写 `msg_no=1` 到 13 B 头"的代码，或找 `G_Cipher`/加密层的调用者（载荷是先加密再发的） |

**判据不变**：读到"写 token / 8 B mouse / 那 6 个尾字段"的代码，并读出**写入总字节数**与**缓冲长度常量**。


| 文件 | 作用 |
|---|---|
| `work/e2-cmd1-sites.py`、`work/out/e2-cmd1-sites.txt` | 234 个 `BeginCommand` 发射点 + 命令号直方图 + `cmd=None` 上下文 |
| `work/e2-find-login-sender.py`、`work/out/e2-login-anchors.txt` | 三个锚点（DataManager/token 上限/string 上限）的全扫与上下文 |
| `work/e2-frame-fingerprint.py` | 按字段上限组合聚类候选构造点（29 个簇，**未收敛**，留档） |
| `work/e2-packet-class.py` | 用 `pefile` 解析客户端 + 读 `0x00417480` 跳转表（含自动搜表） |
| `work/e2-context.py` | 按地址印上下文（E2 的"读代码"工具） |
| `work/client-begincommand-scan.py` | **反面教材**：手写 PE 解析在 VMProtect 客户端上失败 |
| `work/vmdb-direct.py` | 早期尝试（VM 3306 直连被 grant 拒），保留说明为何走 SSH |

## 4quater. 第二轮：`0x566b40` 不是登录包类；`20080418` 的"数据段命中"是**指令立即数**（重要更正）

### 4q.1 `0x566b40`（id `0x158`=344 的构造函数）读过了 —— **不是登录包**

```
0x00566b40  push ebp / mov ebp,esp / push -1 / push 0x96ae02 / mov eax,fs:[0] …   ; SEH 序言
0x00566b6a  call 0x555a90                    ; 基类构造
0x00566b7a  mov dword ptr [esi], 0x9da194    ; ★ 该类的 vtable
0x00566b71/0x00566b85  lea ecx,[esi+0x7a4] / [esi+0x7e4] ; call 0x405790   ; 两个子对象（各 64 B 窗口）
0x00566b94..  mov dword [esi+0x828/0x82c/0x830], 0
0x00566bac  call 0x5c9b10 ; 0x566bb3 call 0x5cb900
```

`0x834`=2100 B 的分配对应 `this` 大小；`+0x7a4`/`+0x7e4` 是子对象，`+0x828..0x830` 是 3 个指针字段。
**没有任何 400 B 级别的载荷缓冲**（要装 400 B 至少要 `+0x190` 这样的大块，且会被逐字节写）。
⇒ 这一类不是登录包，**这一刀落空**（记录在案，避免重复）。

### 4q.2 `20080418` 的唯一引用是**指令立即数**，不是数据常量

第一轮我按"数据段里出现 `22 67 32 01`"去找读它的代码。第二轮把地址算清楚了：

```
file offset 0x18dd2  属于节 'nsn t'（VA 0x401000+…），换算 VA = 0x4199D2
该地址的前后 48 字节： 4d0083c404b96c91b700e88fb0feff 3d 22673201 745e6a01ff15…
                                              ^^ ^^^^^^^^
                                         0x4199D1 = 3D 22 67 32 01 = `cmp eax, 0x1326722`
```

⇒ `0x18dd2` **就是那条 `cmp` 的立即数字节**（`cmp eax, imm32` 的 opcode `3D` + LE imm32），
**不是数据段里的一个常量变量**。所以：

* 客户端**没有**把 `20080418` 存成可读的数据；
* 它**只有一处**用到这个值 —— `0x004199d1 cmp eax, 0x1326722`（在 `0x4199C7` 那个版本检查块里）；
* **客户端从不"写"这个值**。

### 4q.3 由此得到的一条**很强的负结论**（对 §4.C 直接有用）

对**两个部署的 version 值**做全二进制扫描：

| 常量 | 十进制 | `DNF.exe` 里 |
|---|---|---|
| DataManager | 20080418 (`0x01326722`) | 仅指令立即数 1 处（上面那条 `cmp`） |
| 容器 version | 30000401 (`0x01C9C511`) | **二进制里没有（任何形态）** |
| VM version | 28000201 (`0x01AB3FC9`) | **二进制里没有（任何形态）** |

⇒ **登录帧里的 `version` 字段值不在客户端二进制里**（两种形态都没有）。
这意味着客户端那个字段**不是**代码里的字面量，而更可能：
① 来自登录应答/NOTI1 里服务端下发的值（客户端原样回填）；或
② 从本地数据文件（pvf/ini/配置）读出。**两者都还没证实**，标 `[未验证]`。
脚本：`work/e2-version-consts.py`。

**这对 §4.C 的意义**：想找"写 400 B 载荷的代码"，**不能**靠这些字段值当锚点
（它们是运行期数据，不是字面量）。锚点必须换成**载荷结构本身**
（token 长度字段 / 8 B mouse / 那 6 个尾字段的写入顺序），或**发送路径**（见 §4ter 的下一刀）。

### 4q.4 试过并失败的锚点（都留档，避免重跑）

| 尝试 | 结果 | 文件 |
|---|---|---|
| `mov word ptr [reg], 0x101` / 相邻两条 `mov byte [r],1`+`[r+1],1` | **0 命中**（按 `insns.txt` 的真实操作数形态核对过：`mov byte ptr [R], N` 只有 35 处，`mov byte ptr [R + N], N` 2410 处；编译器没生成这两种形状） | `work/e2-frame-header-sites.py` |
| 按字段上限组合聚类（`0x18`+`0x84`+`8`） | 29 个簇，**无 1 个含 `0x158`(344)** ⇒ 未收敛 | `work/e2-frame-fingerprint.py` |
| id `0x158`(344) 的包类构造函数 | 读了，不是登录包（§4q.1） | `work/e2-context.py` |
| 手写 PE 解析 | 加壳客户端上失败（§1） | `work/client-begincommand-scan.py` |

## 6. 本轮产物

| 文件 | 作用 |
|---|---|
| `work/e2-cmd1-sites.py`、`work/out/e2-cmd1-sites.txt` | 234 个 `BeginCommand` 发射点 + 命令号直方图 + `cmd=None` 上下文 |
| `work/e2-find-login-sender.py`、`work/out/e2-login-anchors.txt` | 三个锚点（DataManager/token 上限/string 上限）的全扫与上下文 |
| `work/e2-frame-fingerprint.py` | 按字段上限组合聚类候选构造点（29 个簇，**未收敛**，留档） |
| `work/e2-packet-class.py` | 用 `pefile` 解析客户端 + 读 `0x00417480` 跳转表（含自动搜表） |
| `work/e2-cmdname-table.py` | dump 命令名表 `0xB54430` / 通知名表 `0xB54170`（**本轮定案的关键**） |
| `work/e2-context.py` | 按地址印上下文（E2 的"读代码"工具） |
| `work/client-begincommand-scan.py` | **反面教材**：手写 PE 解析在 VMProtect 客户端上失败 |
| `work/vmdb-direct.py` | 早期尝试（VM 3306 直连被 grant 拒），保留说明为何走 SSH |

## 7. 纪律

- 只读：没有运行客户端、没有改客户端文件、没有挂调试器。
- 未把"候选"写成"结论"：§2/§3 是**排除性**结论（可证伪），§4.1 明确标"未定案"；
  §4.2 的"remap 下标"误读已在 §4bis **显式作废并给出推翻证据**（不删原文）。
- 我第一版手写 PE 解析得出的 `image_base=0x598000` 与"0 个 BeginCommand"**保留在案**，
  因为它正是"工具错 → 空输出 → 差点变成错误结论"的实例（与 `open-questions.md` 坑 23 同类）。
