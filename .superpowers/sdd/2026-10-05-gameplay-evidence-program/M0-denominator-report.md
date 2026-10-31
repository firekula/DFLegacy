# M0 分母报告 —— 官服玩法面证据链工程

> **给评审门看的那一份。** 日期：2026-10-05。范围：spec 的 M0（工具与分母）。
> **执行者**：本会话（未参与设计对话，只读 spec + 计划）。
> **原始输出**：`work/out/M0-acceptance.txt`（四判据全文）、`work/out/opcode-map-residual.txt`（映射残余）、
> `work/out/catalog_dispatchers_full.txt`（家族目录）、`work/out/catalog_handlers_full.txt`（处理器目录）、
> `work/out/opcode-map.tsv`（1171 行的逐条映射，未进 git，可重跑生成）。
> **被分析对象**：`df_game_r.bin`，md5 `7b2b22caea80752deaa403356ea867c0`，36,360,954 B，ELF32 i386，**带 DWARF**。

---

## ① 派发器家族总数 N、其中带 `dispatch_sig` 的 M

| 量 | 值 | 来源 / 判据 |
|---|---|---|
| **N（家族总数）** | **425** | `work/out/catalog_dispatchers_full.txt`（表头第 6 行的运行时统计） |
| **M（有真实 `dispatch_sig`）** | **242** | 同上；`dispatch_sig` 地址非 0 |
| 有 `process` | 424 | 同上 |
| 两者皆无 | 1（`BuyItemCheck`） | 它是服务间派发器：只有 `db_dispatch`/`inter_dispatch`，没有 `process`/`dispatch_sig`；其 vtable `0x08b98118` 在整个可执行节里**没有任何指令引用**（阳性对照见判据 4 一节） |

**与既有目录的关系（同源交叉，地址级）**：既有 `catalog_dispatchers.txt` 的 **84** 条在新目录里一条不缺
（`missing in new` 为空），且 **84/84 条的 `process` 与 `dispatch_sig` 逐字节一致（0 差异）**。
⇒ 这次扩容是**纯增量**，没有把上一轮验证过的结论改掉。

**家族数是 84 → 425（不是计划里预写的"422"）**，因为计划的三条拼写 pattern 漏了三种变体，实测发现：

| 变体 | 例子 | 服务的 CMD |
|---|---|---|
| `Dispather_`（字母换位） | `Dispather_RegenerationRandomOption` | 407 `REGENERATION_RANDOM_OPTION` |
| `Dispatcer_`（少一个 h） | `Dispatcer_polic_one_to_one_chat_disable` | 330 `ONE_TO_ONE_CHAT_STATE` |
| 前缀后**无下划线** | `DispatcherResetRandomOption` | 456 `RESET_RANDOM_OPTION` |

三者的符号都齐全（ctor/dtor/`dispatch_sig`/`read`/`process`），是**真实家族**。
Task 3 首版（commit `819287f`）按计划的 pattern 只得到 422 个，已单独提交修正（commit `786bf99`，422 → 425）。
**首版结论未删**，改动原因写在目录表头。

> 另有一处不是"漏"而是**归类依据**：`ARAD::Arad_Dispatcher_X_Impl` 与 `DisPatcher_X` 按计划约定归并为
> 同一个家族名 X（目录第 4 列保留原始拼写）。仅 `BuyItemCheck` 是"只有 Arad 拼写"的家族。

---

## ② `CMD` / `NOTI` 各多少条、`mapped` / `unknown` / 其余各多少

`work/out/opcode-map.tsv`，1171 行 = CMD 608 + NOTI 563。状态分布（**unknown 显式计数**）：

| 状态 | 条数 | 构成 |
|---|---|---|
| `mapped` | **428** | 全部是 CMD；`id 0..605` 里注册了派发器的槽位 |
| `no-dispatcher` | **741** | CMD 178（`id 0..605` 中未注册的槽位）+ NOTI 563（**出站专用**，见下） |
| `unused` | **2** | CMD 606 `END`、CMD 607 `DISJOINT_ITEM`：`id > 查表上界 0x25d = 605` |
| `inter-server` | **0** | CMD 表里没有任何服务间派发器（见 ①） |
| `unknown` | **0** | 没有一条判不出来 |
| **合计** | **1171** | ✓ 与 CMD 608 + NOTI 563 相等，**无余数** |

**映射机制（这是 M0 最重要的单个发现，取代了 spec 里"`.rodata` 地址 → 查表代码"的猜想）**：

1. 查表函数 `PacketDispatcher::get_dispatcher(int)` @**0x085948e2**（63 B）：
   `cmp [ebp+0xc], 0x25d` → 大于 605 返回 0；`cmp [ebp+0xc], 0` → 小于 0 返回 0；
   然后 `mov eax, [this + id*4]` —— **表是 `PacketDispatcher` 单例的成员数组 `m_table[id]`，4 字节指针，基址 = 对象 + 0**。
2. 表由 `PacketDispatcher::PacketDispatcher()` @**0x08590a2e**（15,890 B）填充：先把 `id 0..0x25d` 清零，
   再做 **428 个注册块**，每块形如
   `mov [esp],4` → `call operator new(0x08724450)` → `call <家族构造函数>` → `mov [this + IMM], edx`（`IMM/4 = id`）。
3. **`<家族构造函数>` 的类名就是家族名**（如 `Dispatcher_BingoQuiz::Dispatcher_BingoQuiz()`）。
   ⇒ CMD 的 id → 家族来自**编译期注册表**，与 `.rodata` 里的名字数组无关 ——
   那两个数组（`g_szCmdPacketName` @0x0936d920、通知的 @0x0936d040）**各只有一个引用点**
   （`GetCommandPacketName` / `GetNotifyPacketName`，@0x082a21c5 / @0x082a2184），是**日志取名**用的。

**NOTI 563 条为什么是 `no-dispatcher`（结构性，不是"查不到"）**：

* `get_dispatcher` **只按 id 取表、没有 class 参数** ⇒ 一张 id 索引的表不可能同时服务 CMD(0..605)
  与 NOTI(0..562) 两套 id 空间；
* `ENUM_NOTIPACKET` 的消费者全是**发送/取名**侧（符号表实测）：`CUser::SendNotiPacket(CUser::eSendTarget, ENUM_NOTIPACKET, int)`、
  `ARAD::DISPATCHER::make_noti_packetheader_jpn(PacketGuard&, ENUM_NOTIPACKET, bool)`、
  `GameWorld::send_chat_msg(..., ENUM_NOTIPACKET)`、`GetNotifyPacketName(ENUM_NOTIPACKET)` ——
  **没有任何 `dispatch*` 函数以 ENUM_NOTIPACKET 为参数**；
* **独立佐证**（另一份既有产物 `packet_senders.txt`，1248 个构造点）：
  `class=0`（通知）748 行、370 个不同 id、**最大 561**（全在 NOTI 的 0..562 内）；
  `class=1`（命令）497 行、277 个不同 id、**最大 602**（全在 CMD 的 0..607 内）。

---

## ③ 四条判据各自的结论 + 原始输出位置

**原始输出（全文）**：`work/out/M0-acceptance.txt`。可重跑：`python work/m0-acceptance.py work/out/M0-acceptance.txt`。

| # | 判据 | 结论 | 关键数字 |
|---|---|---|---|
| 1 | 命令映射无余数 | **PASS** | 428+741+2+0 = **1171** = CMD 608 + NOTI 563；`unknown=0`（显式计数），远低于 spec §2.3 的 200 改判线 |
| 2 | 两个 demangler 结果一致 | **PASS** | 我方（symtab + c++filt 那一侧）唯一地址 **4622**，IDA **4621**；**仅 IDA 有 0 个**；两边共有的 4621 个名字**地址集合全同（0 处不同）**；唯一的"仅我方"是 1 行 **FILE**（一个含 Dispatcher 的 `.cpp` 文件名，本来就不是符号地址） |
| 3 | 模块清单交叉 | **补全（修正形式）** | 原两条子要求的**前提**都不成立（见下）；其目的改由**穷尽反查**承担：**424/425 家族名字被 vtable/RTTI 独立确认、0 处不符、0 个漏掉的家族**；56/425 家族能给出精确 `.cpp` |
| 4 | 抽样反证（随机 10 家族） | **PASS 10/10** + 全量 | 抽样的 10 个家族全部命中；**全量**（424/425）由判据 3 的 (b) 承担。反查读的是 `.rodata` 原始字节，不经过符号表 |

**判据 3 的补全件**（原始输出：`work/out/family-evidence.txt`，结构化数据 `work/out/family-cpp-map.tsv`；
可重跑：`python work/family-evidence.py work/out/family-evidence.txt work/out/family-cpp-map.tsv`）：

**(a) 为什么"家族 → 所属 `.cpp`"在本二进制上没有权威来源** —— 三条路各自实测，且互为印证：

1. **symtab 的 STT_FILE 顺序邻接**：证伪。4184 条 dispatcher 行里 **3668 条（87.7%）落在同一个
   `XorRand.cpp` 窗口**（该区域没有 FILE 标记，是 catch-all 窗口，不是编译单元边界；次多
   `PacketDispatcher_Impl_1.cpp` 461 条）。我的初版目录曾写入 `file=` 列，实测后**撤掉**。
2. **`.idb` 的行信息**：不可用。`get_sourcefile`（`ida_lines`/`idaapi`/`idc` 三处都有该 API）
   对 **7649 个** dispatcher-ish 名字**全部返回空**。
3. **二进制自带 DWARF**：**不覆盖游戏代码**。`.debug_info` 只有 **21 个 CU**，`.debug_line` 的
   CU 是 FreeType 自己的源码（`ftinit.c`/`ftcalc.c`/`ttdriver.c`/`ftgrays.c`，目录 `/root/f248/src/…`、
   `./builds/unix`）—— 那是**静态链接的第三方库**的调试信息。**这同时解释了第 2 条为什么是空的。**

**(b) 替代证据 1 —— 全量（不是抽样）vtable/RTTI 反查**：扫 `.rodata`/`.data.rel.ro`/`.data` 得到
**1020 条** vtable 记录（`+0`=0、`+4`→RTTI、`+20`→`dispatch_sig` 在 `.text`），其中 **522 条**的 RTTI 名
含 dispatcher。逐家族比对：

| 结果 | 数 |
|---|---|
| 家族名被独立确认（RTTI 名含该家族名） | **424 / 425** |
| 找到 vtable 但名字不符 | **0** |
| 找不到 vtable | 1（`BuyItemCheck`：服务间派发器，本就没有 `process`/`dispatch_sig`） |
| **反向**：RTTI 里有、目录里没有的家族 | **0** ⇒ **没有漏家族** |

**(c) 替代证据 2 —— `__FILE__` 串池给出的精确 `.cpp`**：`.rodata` 里 166 个源路径串，
**56/425 家族（13.2%）**能指到精确 `.cpp`，集中在 5 个文件：
`PacketDispatcher_Impl_1.cpp`(39)、`PacketDispatcher_Impl_2.cpp`(8)、
`localjapan/Arad_PacketDispatcher.cpp`(7)、`localjapan/Arad_Dispatcher_BuyItemCheck_Impl.cpp`(1)、
`PacketDispatcher_Secu.cpp`(1)。如 `Login`/`SelectCharac`/`CreateCharac`/`MailBox_Send` →
`PacketDispatcher_Impl_1.cpp`，`MouseRegister` → `PacketDispatcher_Secu.cpp`，`BuyItemCheck` →
`localjapan/Arad_Dispatcher_BuyItemCheck_Impl.cpp`。
**限度（写在前面）**：本方法只扫家族自身函数体里的直接立即数，经 helper 间接引用 `.cpp` 串的情形
不会被捕获 ⇒ **13.2% 是下界**；其余 369 个家族在二进制里没有留下源码路径。

**(d) 子要求一（"同量级"）的修正量法**：spec 拿**类**与**文件**直接比 —— 21 个 `*Dispatcher*.cpp`
vs 425 个家族 = **20.2×**，不成立。修正后的量法：确实承载派发器代码的源文件 = **23 个**
（STT_FILE 名含 Dispatcher 的 21 ∪ `__FILE__` 池里被家族代码引用到的 5）= **425/23 ≈ 18.5 家族每文件**。
**一个 TU 承载多个家族是常态**（`PacketDispatcher_Impl_1.cpp` 一个 FILE 窗口内就有 461 条 dispatcher
符号行），所以该子要求的前提对本二进制不适用 —— **不是提取出错**。

**(e) 一个被调和掉的矛盾（留痕）**：`MakeWarRoomTemp` 早先被判为"死类"（其 vtable `0x08bd8cc0`
在整个可执行节里**没有任何指令引用**），但它又出现在 (c) 里（成员函数带 `PacketDispatcher_Impl_1.cpp`
的断言串）。补查：指向该 vtable 的 4 字节在全文件只出现 **1 处，且落在 `.symtab` 里**（就是它自己的
`st_value` 字段）—— **既没有代码里的 store，也没有 `.data` 里的预初始化对象镜像**。
⇒ 结论：它是**已定义但从未实例化**的类，GCC 照样会发射它的 vtable 与方法体（因此方法里有断言、
`__FILE__` 指向它所在的 TU），但没有任何对象被构造 ⇒ 与"注册表里没有它"一致。
**旧结论（"死类"）不必作废，只是补上了机制。**

**判据 4 的做法与计划不同（记录在案）**：计划写的是"用 `game-scan-imm` 找 `dispatch_sig` 的调用点，
再反汇编看是否引用含家族名的字符串"。**这条路走不通** —— 家族名串只被 `GetCommandPacketName` 引用，
`dispatch_sig` 的调用点并不引用它。改用 Itanium ABI 的 vtable 结构反查（`dispatch_sig` 必在 `+20` 槽，
`+4` 是 RTTI，`type_info+4` 是类名串），**直接读 `.rodata` 原始字节**。10 个样本里出现了三种命名族与命名空间变体：
`DisPatcher_DebugCommand`、`N12advancealtar16Dispatcher_PauseE`、`N4ARAD10DISPATCHER28Dispatcher_AradAvatarConvertE` ——
全部命中，说明反查对拼写变体也有效。抽样用**固定随机种子 20261005**，可重跑得到同一批样本。

---

## ④ S2 判定：客户端可达命令数 vs "预期"

**"预期"（spec §5.2 的写法，写死）**：M1–M10 的单元总共需要覆盖约 **300–600** 条命令；S2 的触发线是它的 **2×＝1200**。

| 量 | 值 |
|---|---|
| `mapped` | 428 |
| 减 `inter-server`（0）、减 `unused`（2，且这 2 条本来就不在 `mapped` 里） | **客户端可达 = 428 条** |
| "预期"带 | 300–600 |
| 2× 线 | 1200 |
| **判定** | **428 < 1200 ⇒ S2 不触发**；且 428 **落在预期带 300–600 之内**（不是"比预期大得多"，也不是"远小于"） |

**结论**：spec §6 风险 1（"分母比预期大得多"）**没有发生**；
spec §5.4 要求的"真实分母"是：**423 个客户端可达家族**（425 家族中有 CMD 号可挂的）、**428 条客户端可达命令**。

**⚠️ 但所有者已判定：S2 不采纳，范围不缩。** 2026-10-05 所有者对 ⑥ 的裁决是
"待评估队列里提到的东西**都是需要的**" ⇒ 重推导的目标从 spec 的 D2（"与现有 DFLegacy 功能等价"）
扩为**官方功能面全覆盖**。这条裁决的后果写在 ⑤ 与 ⑥。

**参考量（不是判据，供排期用）**：425 家族 / 1171 条命令；其中 242 家族有 `dispatch_sig`、
**183 家族没有**（182 个只有 `process`，1 个 `BuyItemCheck` 两者皆无）；4 个家族的 `dispatch_sig`
是空实现（`mov eax,0; ret`）。

---

## ⑤ 能否进 M1

**能 —— 依据是本报告 ①–④ 与 `M0-acceptance.txt`**：四判据里 **1/2/4 PASS**，
**3 以修正形式补全**（原两条子要求前提不成立、原因已定位到 spec 的预设与本二进制结构不符，
其目的由穷尽反查承担：424/425 名字相符、0 处不符、0 个漏掉的家族）；
分母 428 条落在预期带内；M0 的五件产物齐全，`.recon` 8 个工具、配置无问题。

**进 M1 前必须知道的三件事**（都不是 M0 的判据，但会影响 M1 排期）：

1. **范围已扩大（所有者 2026-10-05 裁决）**：⑥ 的待评估队列**全部纳入**，D2 的边界从
   "与现有 DFLegacy 等价"扩为**官方功能面全覆盖**。spec §5.2 的里程碑表（M1–M10）**不覆盖**
   拍卖行/公会/商城/成就/怪物卡/自动贩卖机/小游戏/决斗这些系统 ⇒ **里程碑表需要先扩展，
   再为新增系统出单元计划**；本计划的 Task 8–11（驱动器前置）不受影响，可照跑。
2. **驱动器的自检（计划 Task 9–11）还没做** —— 那是 M1 的第一道硬门（spec §4.2 / S3），
   本计划把 Task 8（人机批次）排在 Task 9 之前，所以**下一步是 Task 8，需要项目所有者在键盘前**。
3. **`ida-game` 这一层没有经 `recon_tool` 调通**：本会话审批提示已禁用，`mutate` 工具经 `recon_tool`
   调用被自动拒绝（`审批未通过（rejected）`）。它作为工具链是**直接执行同一 argv 实测通过**的
   （IDB 正确指向 `df_game_r.bin.idb`、`symbols=328151`、`dispatcher-named=4621`），
   但"经本模式调用"这一步**未通过**。要么放开一次审批，要么接受"静态取证走直接 argv + 记录"。
4. **`df_game_r.bin.idb` 没有行信息**，且二进制的 DWARF 只属于静态链接的第三方库（FreeType）
   ⇒ 任何"函数 → 源文件/行号"的需求在本库上做不到；`__FILE__` 串池只能覆盖 13.2%（下界）。

---

## ⑥ 待评估队列 —— **所有者裁决：全部纳入**（2026-10-05）

**裁决**：项目所有者 2026-10-05 判定"待评估队列里提到的东西**都是需要的**"。
⇒ 这些系统**不再走"只登记、不纳入"的 D2 路径**，而是**纳入重推导范围**。
后果：spec §5.2 的里程碑表（M1–M10）不覆盖它们，**需要先扩展里程碑表**，
再为新增系统各自出单元计划（六件套 + L2 差异表 + L3 批次）。

**方法与限度（给下一份计划用，不要让下面这张表被当成结论）**：
`STATUS.md` 的章节覆盖的是**登录/选服/进频道、账号鉴权与角色、城镇同屏与附近聊天、好友、交易、组队、
同队进副本**（外加验证方法与待办）。下表按**家族名的词级比对**得到，是 `[推断]` 级线索 ——
`STATUS.md` 用中文描述系统、家族名是英文，所以"名字没出现 ≠ 该功能不存在"
（例：`AddFriend` 的名字没出现，但 §2.4 就是好友系统）。**归并是按名字做的，边界需在单元计划里重划。**

| 系统 | 代表家族（CMD） | 量级 |
|---|---|---|
| **拍卖行** | `AuctionRegistItem(186)`、`AuctionBidding(188)`、`AuctionSearchByItemKey(189)`、`AuctionMyBiddingInfo(192)`、`AuctionBuyItemApiece(335)`… | ≥ 8 个家族 |
| **公会** | `ApproveJoinGuild(351)`、`CheckGuildNameDouble`、`CheckGuildAddressDouble`… | ≥ 3 |
| **商城 / Cera / 时装** | `BuyCeraShopItem(67)`、`BuyCeraShopLimitItem(256)`、`AvartarRoulette(586)`、`AradAvatarConvert(591)`、`AvartarHiddenOptionChange(588)`、`RentAvatar`、`GenCeraTicket` | ≥ 7 |
| **成就 / 勋章** | `AchievementTrigger(417)`、`AradEmblemCompound(590)` | ≥ 2 |
| **怪物卡 / 附魔 / 打孔 / 分解** | `MonstercardBind(414)`、`AgreeEnchant(171)`、`AddSocketToAvatar(209)`、`ExpertExtraction(416)` | ≥ 4 |
| **邮件** | `MailBox_Send` | ≥ 1 |
| **自动贩卖机 / 私店** | `UseVendingMachine(218)`、`BuyPrivateStoreItem(85)` | ≥ 2 |
| **技能** | `BuySkill(31)`、`SkillInit(492)`、`Skill_Command_Customizing`、`RequestCharacSkillInfo(485)` | ≥ 4 |
| **任务** | `QuestAction`、`SaveQuestNotify(507)`、`Break_Away_Quest_Check` | ≥ 3 |
| **小游戏 / 活动** | `BingoQuiz(515)`、`BingoReward(514)`、`BlueMarble(496)`、`BlueMarbleConfirmInfo(508)`、`EventCreateDnfRequest` | ≥ 5 |
| **决斗 / PvP 数据** | `AskRematch(368)`、`ConnectP2PPvP(300)`、`EndPvPResult` | ≥ 3 |
| **反外挂 / 安全** | `BVHackInfo(153)`、`Antibot*`、`SecurityCardAuthRpy`、`SecurityLog`、`MouseRegister` | ≥ 5 |
| **GM / 调试** | `DebugCommand`（52 处符号）、`New_Gmdebug_Command`、`GM_Command` | ≥ 3 |
| **其它**（未逐个归并） | `CharacSlotExtendEffect`、`UseLimitCube`、`UseRightOfChangeGrowType`、`StackableActionUse(516)`、`DisJointItem`、`AbnormalUseStackable(437)`… | 剩余 |

**可重跑**：`work/out/opcode-map.tsv`（家族 → CMD 号）× `STATUS.md`（系统清单）；
"名字级未命中"的完整候选有 354 个家族，本表是按系统归并后的**可裁决**形态。

---

## 本阶段推翻/修正的既有结论（矛盾留痕）

| 旧说法 | 新证据 | 处置 |
|---|---|---|
| 计划 Task 3：三条拼写 pattern 足够 | `Dispather_`/`Dispatcer_`/无下划线三种变体实测存在，且有完整符号 | 已修正 pattern 并重生成目录（422 → 425），首版未删 |
| 计划 Task 4 Step 3：`opcode_*.txt` 第三列是 `.rodata` 地址（VA） | 它是 **.rodata 文件偏移**；转 VA 公式 `va = 0x08ae5840 + (off - 0xa9d840)`。按 VA 扫必定 0 命中 —— 计划预写的"改用 IDA xref"分支会因此白跑 | 已在 `map-opcodes.py` 里转换并写进 docstring 差异 A |
| 计划 Task 4：用 `"::dispatch_sig(" in name` 判定成员 | 会命中 `…::dispatch_sig(…)::__PRETTY_FUNCTION__` 这类 **OBJECT**（实测 318 行），且骨架是"后出现者覆盖" ⇒ `dispatch_sig` 会指向字符串对象 | 已改为只认 `FUNC` 且参数表后无尾随垃圾；同候选取"派发器签名优先、其次 size 大" |
| 计划 Task 4 骨架：同候选取 size 最大 | 实测会选错重载（`SecurityCardAuthCancel` 的 1 参 `process` 316 B vs 派发器 3 参 10 B），与既有 84 条冲突 1/84 | 已改为签名优先（docstring 差异 A2） |
| 计划 Task 3：`file=<所属 .cpp>` 可给 | FILE 顺序邻接实测证伪（87.7% 落在一个 catch-all 窗口） | 撤掉该列，原因写进目录表头 |
| 计划 Task 7 判据 4 的做法（找调用点看是否引用名字串） | 名字数组各只有一个引用点（日志取名），该路走不通 | 改用 vtable/RTTI 反查，更强且独立 |
| spec §2.2 判据 3 的预设（家族数与 `*Dispatcher*.cpp` 同量级） | 21 vs 425 = 20.2×，且家族集中在少数框架 TU | 判据 3 记 FAIL + 原因 + 替代证据 |
| 计划 Task 2 Step 5 的验收脚本 | `@( ... , $sel \| ForEach-Object {...})` 里的管道作用于**整个数组** ⇒ 两个字符串元素被 `$_.Line` 变成 `$null` 而**静默丢失** | 已拆开管道重跑；该 bug 不影响结论但会让验收记录缺行 |
| 计划 Task 6 Step 5 的提交清单（2 个文件） | `run-ida.sh` 必须能传库（`invoke` 无法设环境变量） | 增加可选 `--idb`（转 `DNF_IDB=`），提交 3 个文件；`ida-dec` 已回归实测不破 |
