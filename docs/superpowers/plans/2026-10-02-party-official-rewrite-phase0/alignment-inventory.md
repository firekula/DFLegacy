# 对齐清单 — 多人副本官服对齐重写（PD-xxxx）

> Phase 0 产出物。此后每个切片的实施细节都以本清单的条目为输入（设计稿 §5.5）。
> 结构校验：`python work/lint_inventory.py alignment-inventory.md`
> 命令复核：`python work/verify_inventory.py alignment-inventory.md`（默认工作目录 `../2026-09-29-dungeon-party/work`）
> 统计：`python work/lint_inventory.py alignment-inventory.md --stats`

## 使用规则

每条结论带**来源**，类别只用六种：`[静态]` `[抓包]` `[真机]` `[回放]` `[推断]` `[未验证]`。
两层证据（设计稿 §3）：**语义层** = 官服 `df_game_r`（"应该做什么"）；**可达层** = 本客户端 `DNF.exe` 1.0.1.9
（"做了有没有用"）。冲突处必须给裁决，不静默选边。

| 裁决 | 含义 | 落地要求 |
| --- | --- | --- |
| `对齐` | 两层都成立 | 按官服实现；单测钉字段与状态转移 |
| `保留` | 客户端可达层不成立/冲突，但为本客户端可用性保留 | 写理由、用户可见后果、官服原语义、可移除时间点 |
| `无法实现` | 官服机制在本客户端无落点 | 记录用户可见后果；不写"试错补包" |
| `待真机` | 静态已定，客户端行为未观测 | 给可判定观察点（日志关键字/屏幕现象） |

### 条目模板（**不要**留下 `TODO(填)`：lint 会拒绝）

```markdown
### PD-3001 一句话标题（动宾结构：谁在什么条件下发什么/改什么）
- 官服行为: …
- 官服证据: 函数@0x地址(大小)；关键指令地址；包布局 class/cmd + 字段表（无证据就写"无"）
- 可重跑命令:
  ```
  <自证命令：以 findstr /C:"<期望子串>" 结尾>
  ```
- 客户端可达性: 通知 → case 号 + 处理器地址 + 读到哪些字段；命令 → 客户端是否真的发；未处理写 default
- 现状: 文件:行 + 与官服的差异
- 裁决: 对齐 | 保留 | 无法实现 | 待真机
- 验收判据: 单测断言 + 真机观察点（屏幕现象 / 日志关键字）
- 归属切片: S1..S8
```

### 编号规则

`PD-<块 1..6><三位序号>`：`PD-3001` = ③ 块第 1 条、`PD-3012` = ③ 块第 12 条。
块号：① = 1、② = 2、③ = 3、④ = 4、⑤ = 5、⑥ = 6。

### 工具（`work/` 下，全部带 `--selftest`）

| 工具 | 用途 |
| --- | --- |
| `client_surface.py noti <id>` / `noti-fields <id>` | 客户端通知：处理器地址、是否 default、线性读取序列 |
| `client_surface.py callers <0xVA>` / `global-refs <0xVA>` | 客户端调用点 / 全局访问点 |
| `client_surface.py cmds --log <server.log>` | 上行命令表：官服期望 vs 真机实测 vs 我们的常量 |
| `official_surface.py func` / `layout` / `packets` / `callers` / `skeleton` | 官服侧：地址、包布局、`packet_senders` 索引、调用点、条目骨架 |

---

## ① 组队本体（S1）

### PD-1001 NOTI 26 是"主机（队长）槽位"，全队同一个值
- 官服行为: `CParty::set_host` 记录主机槽位；`send_host_info` 按槽位循环，命中主机槽时 `put_byte(槽位号)` 后 `send_to_party`（全队同值）
- 官服证据: CParty::send_host_info@0x0859cdd2(207)、CParty::set_host@0x0859cdbc(22)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::send_host_info | findstr /C:"0x0859CDD2"
  ```
- 客户端可达性: NOTI 26 → case 26 = 0x41F144（非 default）；客户端把 1 字节存进全局并当 `0..7` **槽位号**用（使用点 0x78D2B1，`cmp esi,8; jge skip`）
- 现状: **已按官服改好**（提交 `99d55e1`）：三处组队路径都发**队长槽位** —— 进选图 `EntranceService.cs:2779-2782`、队员入场 `:2884-2887`、live-join `:13001`；单人路径传 0（`:12379`，= 主机就是自己）。**`[作废]`** Phase 0 初稿在这里写的"队员主入场路径发的是自己的槽位（两条路径不一致）"来自前身设计稿的差距清单 v1（写于 `99d55e1` **之前**），已过时；S1 勘察实测更正（见台账"S1 勘察更正"）
- 裁决: 对齐
- 验收判据: ① 单测：`TryGetLeaderSlot` 对**非队长队员**必须返回队长槽位（`PartySmokeTests.CheckHostSlotIsSharedByEveryMemberAsync`；变异验证：改成"返回自己的槽位" → **仅该用例**变红，既有用例全部存活）；② 报文规划器侧由 `DungeonPartySmokeTests.cs:690-720` 钉住 hostSlot 会进包序；③ **真机 ✅ 已完成（2026-10-03 run #43）**：组队一轮里两个会话**同一秒**各收一条 `NOTI 26 payload=00`（`346f8a55`=`test` 队长 slot 0、`34765968`=圣骑士 队员 **slot 1**）⇒ 队员收到的不是自己的槽位（那会是 `01`）而是队长槽位；单人对照一轮 `test` 单独进图仍收 `payload=00`。证据：`real-client-run-43-server.log` 的 `01:17:13`（两条）/`01:17:18`（队员入场）/`01:19:58`（单人）
- 归属切片: S1

### PD-1002 NOTI 11 每成员 7 字段（客户端只读 7）
- 官服行为: `CParty::send_party_ipinfo` 逐成员写 uid/内网IP/公网IP/端口/acc_id/nat_type/mtu（官服 2012 版多 1 字节）
- 官服证据: CParty::send_party_ipinfo@0x0859cea2(683)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::send_party_ipinfo | findstr /C:"0x0859CEA2"
  ```
- 客户端可达性: NOTI 11 → 0x41C7E7；线性读取序列 = `byte(计数), short, int, int, short, int, byte, int`（每成员 7 字段，无第 8 个）
- 现状: `CreateUdpPeerInfo` 每成员 7 字段 ✓（与客户端一致）
- 裁决: 保留
- 验收判据: `client_surface.py --selftest` 已钉住 noti-fields 11；不改布局
- 归属切片: S1

### PD-1003 NOTI 153 + CMD 169（队伍实时信息）：客户端收不到
- 官服行为: `send_party_realtime_info` 按槽位写 uid + 1 字节状态（HUD 血量百分比）+ …；服务端还会用 `CMD 169` 向客户端索要实时信息
- 官服证据: CParty::send_party_realtime_info@0x0859cd24(152)、CParty::get_party_realtime_info@0x0859cbac(376)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::send_party_realtime_info | findstr /C:"0x0859CD24"
  ```
- 客户端可达性: **NOTI 153 out-of-table**（153 > 0x97）；CMD 169 在真机日志里 **0 次**（`client_surface.py cmds` 的 seen_as_cmd）
- 现状: 两者都未实现
- 裁决: 无法实现
- 验收判据: 不实现；用户可见后果记录为"队伍 HUD 的成员血量/状态永远停在进队那一刻"（STATUS §2.7 第 16⑨ 条的"队长半血"即此）；真机确认 HUD 数值不动 = 符合预期
- 归属切片: S1

### PD-1004 CMD 13 LEAVE_PARTY 与 CMD 45 GIVEUP_GAME 走同一个 `leave_user(party,user,2)`
- 官服行为: 主动退队与"副本内擅自退出"在官服是同一形态：`CParty::leave_user(party, user, 2)`
- 官服证据: DisPatcher_LeaveParty::process@0x081c4256(208)；DisPatcher_GiveUpGame::process@0x081c40b4(307) → CParty::giveup_game@0x085b2baa(2422) → CParty::leave_user@0x0859c114(1822)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_GiveUpGame::process | findstr /C:"0x081C40B4"
  ```
- 客户端可达性: 客户端确实会发（`STATUS.md:700-702` 记录 `protocol=45` 5 次；CMD 13 见旧日志）
- 现状: `PartyService` 退队 + `DungeonPartyService.AbandonPartyForDungeonGiveUpAsync`（走同一个静默 leave）
- 裁决: 对齐
- 验收判据: 单测（已有 `CheckGiveUpLeavesPartyAsync` 9 条断言）；真机 `protocol=45` 后名册只剩他人、队伍保留
- 归属切片: S1

### PD-1005 最后一人"擅自退出"时队伍保留（不解散）
- 官服行为: `leave_user` 只在 `IsSinglePlay` 分支 `CParty::destroy`；玩家自建的单人队不走那条 ⇒ 1 人队保留
- 官服证据: CParty::leave_user@0x0859c114(1822)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::leave_user | findstr /C:"0x0859C114"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: `PartyService.TryLeavePartySilentlyIfOthersRemainAsync`（只有队里还有别人才真移出）
- 裁决: 对齐
- 验收判据: 单测已覆盖；真机 `party kept (he stays its leader)` 日志
- 归属切片: S1

### PD-1006 `leave_user` 不发 NOTI 10；NOTI 10 只在 `walkout_member`/`SendWalkout` 里
- 官服行为: 退队路径（含 CMD 13/45）不广播 NOTI 10；被踢/走人走 `CParty::walkout_member` → `SendWalkout`
- 官服证据: CParty::walkout_member@0x0859ca4a(182)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::walkout_member | findstr /C:"0x0859CA4A"
  ```
- 客户端可达性: NOTI 10 → case 10 = 0x41C41A（非 default，客户端会处理）
- 现状: 我们走 `PartyLeaveNotice.Silent`
- 裁决: 对齐
- 验收判据: 单测钉住"退队时没有人收到 NOTI 10"（已有两条断言）；变异验证：换成 `Kicked` 必须变红
- 归属切片: S1

### PD-1007 CMD 329/336 队伍位置变更（336 队长专属）
- 官服行为: 队伍成员位置变更由 `Dispatcher_Change_PartyMemberPosition` 处理（CMD 336 限队长）
- 官服证据: Dispatcher_Change_PartyMemberPosition::dispatch_sig@0x0822759c(709)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func Dispatcher_Change_PartyMemberPosition::dispatch_sig | findstr /C:"0x0822759C"
  ```
- 客户端可达性: run #42 日志里 CMD 329/336 均 **0 次**（`cmds --log`）
- 现状: 未实现
- 裁决: 待真机
- 验收判据: 先确认客户端在什么操作下会发 329/336；若从不发 → 改判 `无法实现` 并从 S1 移出
- 归属切片: S1

### PD-1008 CMD 4 SELECT_CHARACTER（同会话换角色）
- 官服行为: 换角色由 `DisPatcher_SelectCharac::process` 处理（需清掉旧角色在队伍/副本里的痕迹）
- 官服证据: DisPatcher_SelectCharac::process@0x081c6380(1189)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_SelectCharac::process | findstr /C:"0x081C6380"
  ```
- 客户端可达性: 客户端会发（换角色时）；run #42 未出现
- 现状: 已修（换角色照 CMD 7 同序解绑旧角色；STATUS §2.7 已知限制段）
- 裁决: 待真机
- 验收判据: 真机：副本内/回城后换角色，旧角色不再挂在队伍里、新角色收不到旧队伍的副本包（日志 `Party member … left dungeon instance` 带角色名）
- 归属切片: S1

### PD-1009 队长继任 / 主机迁移
- 官服行为: `leave_user` 内部调用 `change_manager`（队长继任 + 换主机）后广播 NOTI 9 / NOTI 153
- 官服证据: CParty::leave_user@0x0859c114(1822) 的内部调用序列（用 `callers`/`calls_in.py` 展开）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py callers CParty::leave_user | findstr /C:"callers of"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: `PartyService` 的继任逻辑（不搬槽位）
- 裁决: 对齐
- 验收判据: 单测：队长退出后队员继任且槽位不变；真机：HUD 主机位切换
- 归属切片: S1

### PD-1010 队伍名册（NOTI 9）按槽位广播
- 官服行为: 队伍变化后 `send_party_info_to_all` 类型的名册广播（按槽位逐条 uid + leaderSlot）
- 官服证据: CParty::leave_user@0x0859c114(1822) 内的名册广播调用点（`calls_in.py` 展开可见）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py callers CParty::leave_user | findstr /C:"0x"
  ```
- 客户端可达性: NOTI 9 → case 9 = 0x41B7A1（非 default）；真机已观察两端各收一份
- 现状: `PartyService.GetRosterPacketFor` 逐人取件
- 裁决: 对齐
- 验收判据: 单测：名册对每个收件人按他自己的通道 uid 生成；真机：两端槽位一致
- 归属切片: S1

## ② 进副本与换房（S3·S4）

### PD-2001 `CParty::game_start` 建三个包：NOTI 2(mode1) → NOTI 3 → NOTI 27，随后才 `set_host`/`send_host_info`
- 官服行为: `game_start` 依次建 ①`class=0 cmd=2`（NOTI 2 USERINFO，`put_byte(1)`=mode1 + `put_short(member_count)`）②`class=0 cmd=3`（USER_STATE：`put_byte` + `put_short(uid)` + `put_byte(1)`）③`class=0 cmd=27`（ENTER_SELECT_DUNGEON）；②③ 经 `CParty::send_to_party` 广播；之后 `set_host` → `send_host_info`(NOTI 26)；尾部还有 `GameWorld::GetWorldMapIndex`/`CWorldMapList::find_world_map`/地狱脚本值/`make_blood_dungeon_info`
- 官服证据: CParty::game_start@0x0859d718(2489)；put_header 点 `0x0859d75e`(NOTI 2)/`0x0859d7ec`(NOTI 3)/`0x0859dd4f`(NOTI 27)；`set_host`@0x0859dcc7、`send_host_info`@0x0859dcd2；发送者 `CParty::send_to_party`@0x0859dc50 与 @0x0859e031
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py packets CParty::game_start | findstr /C:"cmd=27"
  python calls_in.py 0x0859d718+0x9b9 | findstr /C:"send_host_info"
  ```
  （**口径坑**：`calls_in.py` 的 `+size` 按**十六进制**解析，2489 要写 `0x9b9`；写 `+2489` 会被当成 9353 字节）
- 客户端可达性: NOTI 2/3/27 的处理器都在跳转表内（2→0x419A57、3→0x41B0D3、27→0x41F16C）；但 **NOTI 27 的 body 必须是本客户端的三字段形态**，见 PD-2011
- 现状: 我们的入场**不是**"队长动作触发的队伍广播"形态，而是**队员自己的会话发自己的入场包**（`DungeonPartyService.PlanMemberEntry`）；官服那 3 个包在我们这侧分散在：外观（NOTI 2 mode0+mode1，由他人推送点发）、NOTI 3/27 各自的本地路径
- 裁决: 保留
- 验收判据: 官服 3 包与我们的对应关系已写明；队员入场逐包顺序由 run #43 真机核对（PD-2003）；**不要**照抄官服 NOTI 27 的 body（PD-2011 的护栏）；形态改动（改成队长统一 `goto_dungeon`）不在本片范围——它会动到会话所有权，价值低风险高
- 归属切片: S3

### PD-2002 同区域是硬前提（每个成员的 `(vill, area)` 必须等于队长的，快速组队例外）
- 官服行为: `game_start` 按槽位循环，`_checkValidUser` 通过后取该成员的 `getCurCharacVill()`/`get_area()`，与队长的逐字段比较；**不等**时再看 `CParty::is_quick_party`（快速组队才例外）
- 官服证据: CParty::game_start@0x0859d718(2489) 的门禁指令：`0x0859d8b9`（取队长 vill）、`0x0859d8bd`（`cmp al,[成员 vill]`）、`0x0859d8c2`（取队长 area）、`0x0859d8c5`（`cmp eax,[成员 area]`）、`0x0859d8ca`（不等 → `CParty::is_quick_party`）
- 可重跑命令:
  ```
  python dis_range.py 0x0859d718+0x9b9 --show 0x0859d8b0,0x0859d8d0 | findstr /C:"0x0859d8bd"
  ```
- 客户端可达性: 不适用（服务端判定）
- 现状: 任一队员不在与队长相同的 `(town, area)` → 整队中止（只记 `party entry aborted`；我们只有普通组队，没有"快速组队"这条例外）
- 裁决: 对齐
- 验收判据: 单测（已有 `CheckRequestTargetingAsync` 的"异区整队不投递"）；真机 P7：异区组队点进图 → 日志有中止原因、没人进图
- 归属切片: S3

### PD-2003 队员入场报文序列（本客户端形态；run #43 真机逐包核对）
- 官服行为: 官服**没有**"队员自己入场"这个独立入口——队员的入图由队长那次 `game_start` 的 `goto_dungeon` + 队伍广播统一完成；我们的"队长投待入队请求 + 队员自己执行"是**实现手段**，不是官服形态
- 官服证据: 无（对应的是 PD-2001 的 `game_start`；序列证据来自 [真机] 探针 `3d5bb45` + 本轮 run #43）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 26 | findstr /C:"handler=0x0041F144"
  ```
  （序列本身的复现：grep run #43 归档日志的队员会话 `TX 34765968`，见下面的验收判据）
- 客户端可达性: NOTI 26/11/28/29 全部在跳转表内（逐条已验）
- 现状: `DungeonPartyService.PlanMemberEntry`（`DungeonPartyService.cs:14-43`）硬顺序 = 名册 → NOTI 26 → NOTI 11 → NOTI 28 → NOTI 29
- 裁决: 保留
- 验收判据: **run #43 真机实测（队员会话 `34765968`）**：`01:17:13` 进选图 = `3(9B) → 11(28B) → 26(7B) → 5(119B) → 27(8B)`；`01:17:18` 进副本 = `23(16B) → 11(28B) → 9(26B)×2 → 2(87B) → 2(203B,mode1) → 9(26B)×2 → 26(7B) → 11(49B) → 28(14B) → 29(54B) → 36(12B)` —— **外观先于名册、名册先于 26/11/28/29** 与规划器逐条吻合 ✓
- 归属切片: S3

### PD-2011 NOTI 27 的 body 必须保持本客户端的三字段形态（禁止照抄官服 game_start 那份）
- 官服行为: `game_start` 在 `0x0859dd4f` 给全队发 NOTI 27，body = `put_int` + `put_byte` + `put_short` + `put_byte(0)` + `put_byte(0)` + `put_byte(1)` + `put_byte(0)`
- 官服证据: CParty::game_start@0x0859d718 的 `0x0859dd4f` 站点（`official_surface.py layout CParty::game_start` 直读）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py layout CParty::game_start | findstr /C:"cmd=27"
  ```
- 客户端可达性: NOTI 27 → 处理器 `0x41F16C`；读取序列 = `byte, byte, short`（读取点 `0x41F1CF`/`0x41F27A`/`0x41F2BE`）⇒ **官服那份以 4 字节 int 开头，本客户端读不了**（会把 int 的低字节当地狱任务标志，其余字段全部错位）
- 现状: `GameProtocolEngine.CreateEnterSelectDungeon`（`GameProtocolEngine.cs:3303-3326`）写 `bool hellQuestsCompleted` + `byte count` + `u16 槽位数组`；注释 `3307-3309` 已写明"不要用更晚服务端的 NOTI 27 布局"，但没有把"官服形态"变成可执行约束 ⇒ 我们的三字段形态是本客户端**唯一可读**的形态（官服那份读不了，所以不发）
- 裁决: 保留
- 验收判据: 主护栏是 `Program.cs` 里既有 4 条 NOTI 27 用例；本轮补 `PartySmokeTests.CheckEnterSelectDungeonLayout`（显式断言"绝不以 int 开头"+ 缺失槽位 `[2,3]` 组合）。**变异验证（实测）**：把 builder 改成先写 `int` → 全量冒烟 **7 条**断言变红（本用例 3 条 + 既有 4 条），回滚后全绿。**真机交叉**：run #43 队员会话 `protocol=27 length=8` ⇒ body 2 字节 = `[1, 0]` ✓
- 归属切片: S3

### PD-2004 CMD 48 官服 reader 读 10 个字段（附字段用途：坐标/自检/分层图/座位）
- 官服行为: `DisPatcher_MoveMap::read` 依次读 byte,byte,uint,uint,byte,ushort,ushort,uint,ushort,int；`move_map` 里对各字段的用法（**同一份伪码**）：`a3[13]`/`a3[14]` = 目标 tx/ty（char）、`a3[15]`/`a3[19]`（dword）进反外挂 `CheckMoveMapFitInPathHackCnt`、`a3[23] == 1` 时先 `CBattle_Field::checkLayeredMap(field, a3[13], a3[14])`、`a3[26]` 必须 == 1（否则记 hack 602）、`a3[57]` = 座位号（`SetCurSeatNo`）
- 官服证据: DisPatcher_MoveMap::read@0x081c47ba(726)；字段用途见 `official\ida_targeted_out.txt:362-454`（`CParty::move_map` 伪码）与 `DisPatcher_MoveMap::process@0x081c5330`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_MoveMap::read | findstr /C:"0x081C47BA"
  findstr /C:"a3[26] != 1" official\ida_targeted_out.txt
  ```
- 客户端可达性: 客户端确实会发（run #42 `seen_as_cmd=12`）
- 现状: 我们只按需读若干字段（`EntranceService.cs:13095-13160` 的 CMD 48 段），反外挂/分层图/座位三个用途都未实现
- 裁决: 对齐
- 验收判据: 逐字段与我们的读取对齐（至少钉住目标 tx/ty = `a3[13]`/`a3[14]` 与自检字节 `a3[26]`）；`a3[26] != 1` 的 hack 分支按"我们的客户端总是发 1"处理并记录
- 归属切片: S4

### PD-2005 `CParty::move_map` 的推进判据 = **一次合法请求推进整队，没有"全员到门口"**
- 官服行为: ①`party[106] != 2` → `CMD 48` 错误码 **19** 返回；②`!CDungeon::IsEnterEachMap(dungeon) && CBattle_Field::IsClearDungeon(field)` → 同样错误码 19；③按**请求者自己**的 `(tx,ty)` 调 `CBattle_Field::MoveMap(field, tx, ty, &v9)` 作唯一判定；④成功（返回 ≤ 0）则 `MakePacket(29 或 211)` → **`CParty::send_to_party` 广播全队**、`send_loadingcheck_msg`、**`resetFinishLoadingVar`（清零全员 loaded）**、成员信息与战斗数据更新；⑤失败则以 `MoveMap` 的返回码回 `CMD 48` 错误。**全程没有任何"全员到门口/等人"的条件**
- 官服证据: CParty::move_map@0x085a8f4e(987)，伪码 `official\ida_targeted_out.txt:362-454`；入口 `DisPatcher_MoveMap::process@0x081c5330(432)`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::move_map | findstr /C:"0x085A8F4E"
  findstr /C:"IsEnterEachMap" official\ida_targeted_out.txt
  ```
- 客户端可达性: 不适用（服务端判定）；广播用的 `NOTI 29` 在跳转表内，`NOTI 211` **不在**（见 PD-2012/PD-2013）
- 现状: `DungeonDoorAssembly`（189 行：`Register`/到达登记/`Grace` 2s/`Countdown` 10s/`TryAdvanceOnCountdown`/`PendingTarget`）+ `EntranceService.cs:13095-13160` 的窗口包装 + `ServerOptions.Dungeon.DoorGraceSeconds` 与 `ResolveDoorMemberCount`（`memberCount:1` 退化）+ `DungeonInstance.cs:512` 附近的门状态 —— **全部是自研，官服无对应物**
- 裁决: 对齐
- 验收判据: ① 模型层唯一入口 `TryAdvanceRoom`：**第一个合法请求即推进**（不等第二人）、陈旧请求不重复推进、已清房且非逐图进 → 错误 19、非法目标 → 该错误码；② `DungeonDoorAssembly.cs` 与两个选项**从代码里消失**（删除而非绕过）；③ **变异验证**：把"第一个请求即推进"改成"等第二个人"→ 第①条必红；④ **真机 ✅ 已完成（2026-10-03 run #44）**：删掉集结机制后门照开 —— 两次换房都由"先到门的队长"一个人上报，服务端**一次请求推进整队**（`10:11:50`/`10:12:10`：`RX bb470e5f protocol=48` → `TX bb470e5f protocol=48 len=7`（回包）→ **`TX bb470e5f` 与 `TX 72c4b04d` 各一条 `protocol=29`**）；`Door assembly`/`arrived=`/`grace` 行数**均为 0**、无 `Rejected party MOVE_MAP`。证据：`real-client-run-44-server.log`。单人路径零回归（既有结构性断言 + 本轮单人未涉及）
- 归属切片: S4

### PD-2012 CMD 48 的失败要回错误包（19 = 状态门；MoveMap 返回码 = 目标不合法）
- 官服行为: 失败时 `send_to_party_cmd_error(party, 48, code)` 广播错误：队伍状态门（`party[106] != 2`）与"已清房且非 `IsEnterEachMap`"门用 **19**，目标不合法用 `CBattle_Field::MoveMap` 返回码的低字节
- 官服证据: CParty::move_map@0x085a8f4e 的 `LABEL_2`（伪码 `official\ida_targeted_out.txt:391-394`：`send_to_party_cmd_error(a1, 48, 19)`）与失败分支（`:448`：`(u8)v11`）
- 可重跑命令:
  ```
  findstr /C:"send_to_party_cmd_error" official\ida_targeted_out.txt
  ```
- 客户端可达性: CMD 48 是客户端上行命令，其错误回包走 class=1/cmd=48（客户端在等门开时收到错误码；我们目前一个错误包都不发）
- 现状: `GameProtocolEngine` 里 `MoveMapCommand = 48` **只有常量**，没有任何 CMD 48 错误包构造器；失败时我们只是"不推进"（客户端一直等）
- 裁决: 对齐
- 验收判据: 单测：队伍状态门与已清房门回 `[flags=0, 19]`；目标非法回 `[flags=0, code]`；单人路径**不发**任何 CMD 48 错误（单人立即推进）
- 归属切片: S4

### PD-2013 官服成功路径的 `NOTI 211` 分支对本客户端不可达 ⇒ 永不发
- 官服行为: 换房成功后按 `dungeon[2207] > 0` 选择发 `NOTI 211` 或 `NOTI 29`
- 官服证据: CParty::move_map@0x085a8f4e 的 `MakePacket(..., 211, ...)`（伪码 `official\ida_targeted_out.txt:414-417`）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 211 | findstr /C:"out-of-table"
  ```
- 客户端可达性: **`NOTI 211` out-of-table**（211 > 0x97 = 151，分发器 `cmp …,0x97; ja default`）⇒ 本客户端不处理；`NOTI 29` 在表内（→ `0x41F7D3`）
- 现状: 我们只发 `NOTI 29`（本来就没有 211 路径）
- 裁决: 无法实现
- 验收判据: 断言全代码库**没有** NOTI 211 的构包/发送路径（`grep 211 src/` 无命中）；真机：换房后客户端正常加载新房间（29 足够）
- 归属切片: S4

### PD-2006 全员加载门槛：CMD 40 只标自己，齐了才广播
- 官服行为: 每个成员上报 `CMD 40` 只置自己那一位；`check_allmember_loading` 全员齐了才向全队广播 `NOTI 30`；换房时清零全员标志
- 官服证据: DisPatcher_FinishLoad::read@0x08264b66(151)、CParty::check_allmember_loading@0x085b17a4(1729)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::check_allmember_loading | findstr /C:"0x085B17A4"
  ```
- 客户端可达性: 客户端确实会发 CMD 40（run #42 `seen_as_cmd=32`）；NOTI 30 → 0x4200B6
- 现状: 已实现全员门槛 + `NOTI 30` 广播（本轮之前的修复）
- 裁决: 对齐
- 验收判据: 单测（全员齐才广播一次）；**真机 ✅ 已完成（2026-10-03 run #44）**：`10:12:12` 两个会话各发 `CMD 40`（`RX 72c4b04d` / `RX bb470e5f`）并各收自己的回包，**随后**才 `TX bb470e5f protocol=30` 与 `TX 72c4b04d protocol=30` ⇒ `NOTI 30` 确实在全员报完之后广播；重复 CMD 40 幂等（既有单测）。证据：`real-client-run-44-server.log`
- 归属切片: S4

### PD-2007 换房广播 NOTI 29 的载荷构造（**官服字段序不能照抄**）
- 官服行为: 过图广播由 `CBattle_Field::MakePacket` 生成（NOTI 29 START_MAP）；官服有两条分支：`*(BYTE*)(mapInfo+1)` 为真走"**缓存地图**"（`put_byte(0)` + 骑乘物件 + `put_byte(255)` 后 `finalize`），否则写地图/怪/物件三段
- 官服证据: CBattle_Field::MakePacket@0x083090a4(4163)，Hex-Rays 伪码 `work/out/makepacket29.txt:319-360`（`else if (a2 == 29)` 分支）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti-fields 29 | findstr /C:"byte, byte, int"
  findstr /C:"a2 == 29" out\makepacket29.txt
  ```
- 客户端可达性: NOTI 29 → 0x41F7D3（非 default）；**读取序列** = `byte, byte, int, byte, short, byte, byte, short, short, byte, byte, byte, byte, byte, byte, short, short, int, short, byte`
- 现状: `CreateStartMap`（字段序 `roomX, roomY, seed(u32), roomStateFlag(u8), mapId(u16), 怪数…`）；"缓存地图"走 `CreateCachedStartMap`（只写 `roomX, roomY, seed, state=0` 收尾）
- 裁决: 对齐
- 验收判据: **✅ 已完成（2026-10-03，Hex-Rays 只反编译该函数）**。三份并列：

  | 来源 | 前 6 个字段 |
  | --- | --- |
  | 官服 `case 29`（qf1031，**更晚构建**） | `b(curX), b(curY), b(bool@mapInfo+232), i32(mapInfo+72), b(this+460), b(this+461), …` |
  | 本客户端 1.0.1.9 读取 | `b, b, i32, b, u16, b, …` |
  | 我们 `CreateStartMap` | `roomX, roomY, seed(u32), roomStateFlag(u8), mapId(u16), 怪数…` |

  ⇒ **与 NOTI 27 同型：官服把第 3/4 字段写成 `byte,int`，本客户端按 `int,byte` 读**；我们的顺序与客户端一致 ⇒ **NOTI 29 不得照抄官服字段序**。真机旁证：run #42/#43/#44 换房全部正常（`protocol=29` len=54/103 被接受）。官服的"缓存地图"分支同时**印证了我们 `CreateCachedStartMap` 的形态**。
- 归属切片: S4

### PD-2008 空 boss 房可结算（官方侧无函数级证据）
- 官服行为: 空 boss 房（行为房/战争房）何时允许结算——官服侧在脚本/表，未取到函数级证据
- 官服证据: 无（仅在脚本/表侧；我们的等价物是"`.map` 没有静态怪的行为房/战争房"）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 31 | findstr /C:"handler=0x0041FE50"
  ```
- 客户端可达性: NOTI 31（ENABLE_CLEAR_DUNGEON）→ 0x41FE50（非 default）
- 现状: `EnableEmptyBossRoomSettlementAsync`（组队/单人共用；`TryEnableClear` 从关到开只一次）
- 裁决: 保留
- 验收判据: 真机：空 boss 房能开门结算且 `NOTI 31` 只发一次（不重复广播）
- 归属切片: S4

### PD-2009 怪物死亡的三个状态转移（我方三态无官服对应证据）
- 官服行为: 普通死亡/ Boss 死亡 / ALLDIE 在官服由战斗场地与队伍侧共同处理；我们未取到与"三态"一一对应的官服证据
- 官服证据: 无（`CBattle_Field` 侧入口待补；玩家死亡见 PD-6001）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 38 | findstr /C:"handler=0x004217CE"
  ```
- 客户端可达性: NOTI 38（MONSTER_DIE）→ 0x4217CE（非 default）
- 现状: `NormalMonsterDeath` / `BossDeath` / `ForceAllDie` 三态（`DungeonInstance` 锁内摘除 + 锁外广播）
- 裁决: 保留
- 验收判据: 单测：重复死亡不重复广播/奖励、ALLDIE 只处理仍存活集合、级联死亡不给普通经验/掉落；真机：ALLDIE 后重新判定清房
- 归属切片: S4


## ③ 掉落与拾取（S5）

> **S5 收尾（2026-10-03）——本块 3 条裁决被改写、4 条细化。** 取证：`work/ida_s5_loot.py` →
> `work/out/ida_s5_loot.txt`（官服 7 函数 Hex-Rays）、`work/ida_s5_routing.py` →
> `work/out/ida_s5_routing.txt`（`CItem` 判定谓词）、`work/client_surface.py cmd <id>`（新增子命令：
> class=1 命令接收面，分派器 `0x40F290` ⇒ `handler(46)=0x414090`）、`work/s5_cmd46_bodies.py`
> （5 份真机日志 42 条 `CMD 46` 逐条配对）。两个工具的 `--selftest` 均 **0 failures**。
>
> | 条目 | 旧 → 新 | 一句话依据 |
> | --- | --- | --- |
> | `PD-3004` | 保留 → **对齐** | 官服 `get_item` 入口 `memset(v136,0,16)`；4 字节在直取路径就是全 0 |
> | `PD-3006` | 待真机 → **保留** | 42/42 恒 8 字节；`+2` = groundId 被 35/35 交叉证实 |
> | `PD-3010` | 待真机 → **对齐** | 门控是 `check_item_routing = isEpicRoutingItem \|\| rarity > 0`；"阈值 > 3"的说法作废 |
>
> 细化：`PD-3001`（载荷 u32 = **物品 id**，不是"索引"）、`PD-3005`（**三态**：ok / error /
> `0x7FFFFFFF` 静默；客户端对官方成功包是空操作）、`PD-3007`（三出参逐字段结案）、
> `PD-3009`（归属规则取证完毕，通知仍不可实现）。用户 2026-10-03 三条裁决见
> `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/S5-brief.md` §6。
> **已落地**：`NOTI 86(5)` 构包 + 5 个拾取成功点全场广播 + `CMD 46` 成功回包 + `LootRouting`
> 纯规则与单测；**`T3b`（2026-10-03，分支 `feat/s5-loot-t3b`）把 `Decide` 接进会话层并开启掷骰归属**
> ——得主由**得主自己的会话**收进背包（跨会话只搬运"这次拾取"，见 `DungeonLootPickupAttempt`），
> `CMD 46` 四态落地，`--loot-routing` 11 组单测、变异验证 3/3。
> **未落地**：收窄后的真机复验；两处 `[未验证]` 缺口见 `LootRouting` 类注释。
> **用户可见变化**：队伍里的**可路由**物品改为掷骰定归属（可能归别人、拾取者无提示），
> `NOTI 39` 的 4 个槽位字节发真实骰子分；**单人逐字节不变**。
> **门控已收窄**（2026-10-03 晚，提交 `961cc27`）：官服语义是 `check_item_routing = isEpicRoutingItem
> || get_rarity() > 0`，但本客户端无骰子 UI，"判给别人"= **点了毫无反应、东西还在地上**（run #46 真机）⇒
> 用户裁决收窄到 **`rarity > 3`**（`LootRouting.RoutableRarityFloor`，官服语义的 `IsRoutable` 原样保留
> 并注明只是语义层证据）。**这是一处有意偏离**（台账按"部分对齐"记），放开时把 3 改成 0。

### PD-3001 官服拾取成功后向全场广播 NOTI 86 SERVER_BROADCAST（子类型 5）
- 官服行为: 非 routing 的拾取成功后，服务端构造 class0/cmd86（= NOTI 86 `SERVER_BROADCAST`）、载荷 u8 子类型 5 + u16 拾取者 uid + u32 索引（7 字节），并 `GameWorld::send_all` 广播给**全场**
- 官服证据: DisPatcher_GetItem::process@0x081c35c0(1832)；put_header@0x081c3a58（class 0/cmd 0x56）、put_byte(5)@0x081c3a6b、GameWorld::send_all@0x081c3ac1
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py layout DisPatcher_GetItem::process | findstr /C:"cmd=86"
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py packets DisPatcher_GetItem::process | findstr /C:"cmd=86"
  ```
- 客户端可达性: NOTI 86 → case 86 = 处理器 0x427104；只实现子类型 1/2（文本广播），官服发的 **5 落 default(0x42A069) = 空操作**
- 现状: 我们没有任何 86 包；拾取只发 NOTI 39 给拾取者本人（GameProtocolEngine.cs:3655-3711）
- 裁决: 对齐
- 验收判据: 单测断言 NOTI 86 载荷 = `05` + uid(u16) + 索引(u32) 且投递给全员（含拾取者）；真机 grep `protocol=86`（**并发说明**：本客户端画面不会因它消失，这是已知用户可见后果，见 PD-3002/3003）
- 归属切片: S5

### PD-3002 本客户端不实现 NOTI 86 的子类型 5（所以"补一条移除包"是空操作）
- 官服行为: 官服用子类型 5 表达"地面物已被取走"（PD-3001）
- 官服证据: 同 PD-3001：DisPatcher_GetItem::process@0x081c35c0 的广播站点 0x081c3a58
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 86 --insns 20 | findstr /C:"0x42740f"
  ```
- 客户端可达性: 处理器 0x427104 只比较子类型 1 与 2（`cmp byte ptr [ebp-0x61f8], 1|2`），其余一律 `jmp 0x42740F` → `jmp 0x42A069`（公共收尾：`mov eax,1; pop…; ret`）
- 现状: `GameProtocolEngine.cs:3682-3690` 的注释要求"照官服 7 字节形态另发一条移除包"——**该结论作废**：它在本客户端是空操作
- 裁决: 无法实现
- 验收判据: `client_surface.py --selftest` 已钉住（`noti86 jumps to default`）；真机反证：发 86(5) 后队友端画面无变化——**不得**把它当成 P4 的修法
- 归属切片: S5

### PD-3003 客户端删地面物只有两处，且都以"载荷 uid == 我的 uid"为门
- 官服行为: 官服只负责通知（NOTI 39 给当事人 / NOTI 86 给全场）；"谁的地面 actor 该消失"完全由客户端自己按包内 uid 决定
- 官服证据: DisPatcher_GetItem::process@0x081c35c0（通知）+ CParty::get_item@0x085a3b98（归属裁决）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py callers 0x417c40 | findstr /C:"0x004224CB"
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py callers 0x417c40 | findstr /C:"0x004229BD"
  ```
- 客户端可达性: 删除函数 `0x417c40`（→ `0x46ed60`）**只有**两个调用点：`0x4224CB`（NOTI 39 处理器内）与 `0x4229BD`（NOTI 93 `WARROOM_GET_ITEM` 处理器内）；两处之前都有 `cmp <载荷 uid>, myUid; jne <尾部>`，其中 myUid 来自全局 `0xb75a40`（经 `0x4052d0` → `registry->Find(uid)` 证实是 uid）
- 现状: 我们由服务端决定归属，客户端只是执行者（未实现官方 routing）
- 裁决: 无法实现
- 验收判据: `client_surface.py --selftest` 钉住两个调用点；真机观察点：队友端的幽灵物件会一直留到换房（整间房重建）才消失
- 归属切片: S5

### PD-3004 NOTI 39 的 4 个"路由字节"是按队伍槽位的数量，非零就会打印"[名字][数量]"行
- 官服行为: NOTI 39 的 item 形态载荷 = u16 groundId + u16 uid + 4×u8（按槽位的数量/路由结果）+ u16 winner + u16 slot
- 官服证据: 客户端 NOTI 39 处理器 0x421ea7；item 分支 @0x422275 依次读 u16,u16,4×u8,u16,u16；逐槽格式串 `" %s[%d]"`@0x9998bc
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti-fields 39 | findstr /C:"short, short"
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 39 --insns 2 | findstr /C:"0x00421EA7"
  ```
- 客户端可达性: 4 字节里非 0 的槽位会被追加一行"[该槽成员名][数量]"（0x422313–0x422420 的循环 + 打印 @0x42244D）；**全 0 则一行都不打印**
- 现状: `CreateInventoryItemPickup` 写 4 个 0（GameProtocolEngine.cs:3691-3711）；S5 已按官服给成"直取全 0 / routing 写真实加权分"两形态
- 裁决: 对齐
- 验收判据: 单测钉住两种形态（全 0 与按槽位分）；官服**非 routing** 路径**已取证**：`CParty::get_item` 入口 `memset(v136,0,16)`，该数组只在 routing 块内被写 ⇒ 直取路径官服也是全 0（`ida_s5_loot.txt:188/817-856/973-978`）。**本条 2026-10-03 由"保留"改写**（清单顶部 S5 收尾块）
- 归属切片: S5

### PD-3005 官服对 CMD 46 回 ok / error（cmd=0x2e），我们重复拾取时不回包
- 官服行为: `DisPatcher_GetItem::send` 按结果码回 `CUser::SendCmdOkPacket(cmd=0x2e)`；失败回 `SendCmdErrorPacket(cmd=0x2e, code)`（code 取结果低字节）
- 官服证据: DisPatcher_GetItem::send@0x081c3ce8(93)：0x081c3d19 调 `CUser::SendCmdOkPacket`、0x081c3d3b 调 `CUser::SendCmdErrorPacket`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_GetItem::send | findstr /C:"0x081C3CE8"
  ```
- 客户端可达性: CMD 46 的接收面 **S5 已取证**：分派器 `0x40F290`（remap 表 `0x417638` + 跳转表 `0x417480`，合法 id 1..0x9F）⇒ `handler(46)=0x414090`；其首条 `test al,al; jne 0x417467` ⇒ **body[0]（resultFlag）非 0 直接返回**，即官方成功包（body `[1]`）在本客户端是空操作。错误分支按 code：4/0x16 → 成功回执音效；0x15 与"其它任何值" → `0x414198`（重拉地面物列表）；0x51 / 0x63 → 两种文本（`python client_surface.py cmd 46 --insns 30`）
- 现状: 只在"超重"时回 `CreateGetItemSilentRejection`（error 0x15；EntranceService.cs:14012 附近，注释记录了既有结论"客户端已乐观隐藏，0x15 会回滚"）；**重复/失效拾取直接 continue、不回包**（EntranceService.cs:13860-13864）——S5 已定性为**官服语义**（见裁决）
- 裁决: 对齐
- 验收判据: 单测钉三态载荷（`[1]` / `[0,code]` / 不发）；官服证据：`SendCmdOkPacket@0x0867BEA0` = `put_header(1,cmd)+put_byte(1)`，`SendCmdErrorPacket@0x0867C022` = `put_header(1,cmd)+put_byte(0)+put_byte(code)+put_byte(paytype)`，**`0x7FFFFFFF` 哨兵 = 什么都不发**，而 `get_item` 返回 false 恰好走该哨兵；真机旁证：run #42 七次超重各发一条 `TX protocol=46 payload=0015`。**本条 2026-10-03 由"待真机"改写**（清单顶部 S5 收尾块）
- 归属切片: S5

### PD-3006 官服 CMD 46 reader 读 9 个字段，而本客户端实测包体只有 8 字节
- 官服行为: 官服 reader 依次读 u16, u8, u8, u16, u16, u16, u16, u16, u16（共 9 字段）
- 官服证据: DisPatcher_GetItem::read@0x081c335c(592)；首字段读取点 0x081c3378
- 可重跑命令:
  ```
  python packet_reads.py 0x081c335c+592 | findstr /C:"DisPatcher_GetItem::read"
  ```
- 客户端可达性: 真机日志 `RX … protocol=46 length=18 … body=2000010000009709`（8 字节包体）。**S5 已用 5 份归档日志 42 条包体逐条配对定案**（`python ../…/work/s5_cmd46_bodies.py --selftest` ⇒ 0 failures）：`+0` = 帧的 records 计数（42/42）、`+1` = 0（42/42）、**`+2` = groundId（35/35 与结果行声明的 groundId 相等）**、`+4/+6` 未知（标 `[未验证]`）。旧注"首个 u16 = groundId"**作废**——groundId 在 `+2`（我们 `Body.AsSpan(2,2)` 读的就是它）
- 现状: 我们只读 `Body.AsSpan(2, 2)` 当 groundId（EntranceService.cs:13848-13849），其余字节忽略——**读法被 35/35 交叉证实**
- 裁决: 保留
- 验收判据: 8 字节形态稳定性以 `work/s5_cmd46_bodies.py --selftest` 为准（42/42，0 failures）；官服 9 字段 = 更晚构建的布局，**不照抄**；`+4/+6` 语义未取证，不得依赖。**本条 2026-10-03 由"待真机"改写**（清单顶部 S5 收尾块）
- 归属切片: S5

### PD-3007 CParty::get_item 是归属裁决器，其 int 出参就是 NOTI 86 的 u32 载荷
- 官服行为: 拾取归属由 `CParty::get_item(CUser*, int, bool, char, int&, unsigned char&, bool&)` 裁决；第 5 个出参（int）被 `noti-86` 广播当作载荷的 u32 写出去
- 官服证据: CParty::get_item@0x085a3b98(10332)；实参装配在 0x081c38d7-0x081c3905；出参 `[ebp-0x28]` 在 0x081c38a5 清零、0x081c3a8d 被 put_int 使用
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::get_item | findstr /C:"0x085A3B98"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 我们用"全队共享、先到先得"（`DungeonInstance.TryClaimGroundItem` 锁内摘除，DungeonInstance.cs 地面物账本）
- 裁决: 对齐
- 验收判据: S5 需给出 3 个出参的逐字段语义（`[推断]` 直到反编译证实）与 noti-86 载荷的对应；单测：同一件物品只产生一次归属，跨成员拾取幂等
- 归属切片: S5

### PD-3008 CParty::IsRoutingState 决定"直接拾取 vs 掷骰路由"分支
- 官服行为: `DisPatcher_GetItem::process` 先问 `CParty::IsRoutingState()`；为真则走 routing 分支（0x081c3891 起：置结果 0xd8 并返回），为假才走普通拾取 + noti-86 广播
- 官服证据: CParty::IsRoutingState@0x08145840(40)（体内调 `CParty::CItemRoutingData::GetRoutingState`@0x0814575a，即 `*(int*)party != 0`）；调用点 0x081c3888
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::IsRoutingState | findstr /C:"0x08145840"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 我们没有 routing 状态，拾取一律"先到先得"
- 裁决: 对齐
- 验收判据: 单测：routing 状态为真时直接拾取被拒（错误码 0xd8）；真机：routing 物品无法直接捡起
- 归属切片: S5

### PD-3009 routing 生命周期与专用包（NOTI 281/282、CMD 301）
- 官服行为: routing 是独立生命周期：`StartRouting` →（成员选择 `CMD 301 BIDDING_ROUTING_ITEM`）→ `ArriveMemberSelection` → 归属；过程/失败/超时/放弃各发 `NOTI 282 ROUTING_RESULT`，开始发 `NOTI 281 SELECT_ROUTING`
- 官服证据: StartRouting@0x0860a9d4(346) 发 class0 cmd281 @0x0860aab9；processRouting@0x0860ac6a(1623) 发 cmd282 @0x0860ad5a；ProcessFailEtc@0x0860b332(298) 发 cmd282 @0x0860b3a6；Dispatcher_BiddingRoutingItem::dispatch_sig 发 class1 cmd301 @0x0822570c；命令表 281=SELECT_ROUTING、282=ROUTING_RESULT、301=BIDDING_ROUTING_ITEM
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py packets CSpecialItemRoutingManager::StartRouting | findstr /C:"cmd=281"
  ```
- 客户端可达性: **NOTI 281/282 out-of-table**（id > 0x97=151，分发器 `cmp …,0x97; ja default`）⇒ 本客户端根本不处理这两条；CMD 301 在 run #42 日志里 **0 次**（`seen_as_cmd=0`）
- 现状: 未实现（STATUS §2.7 第 20 条把"蓝装掷骰"记为待实现）
- 裁决: 无法实现
- 验收判据: S5 只需决定**服务端**如何处置 routing 物品（是否掷骰、归属怎么给），**不**实现 routing 通知/UI；单测：routing 物品只产生一次最终归属、超时/放弃不重复归属。前置：6 个函数伪码只用于确定归属规则，不再依赖客户端表现
- 归属切片: S5

### PD-3010 CItem::IsRoutingItem 只是读字段 +0xbd —— 品质阈值不在这里
- 官服行为: 物品级 routing 判定 = `return *(unsigned char*)(this + 0xbd)`；函数体内没有品质/等级比较
- 官服证据: CItem::IsRoutingItem@0x08150f18(15)：`mov eax,[ebp+8] / movzx eax, byte ptr [eax+0xbd] / ret`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CItem::IsRoutingItem | findstr /C:"0x08150F18"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: `LootRouting.IsRoutable(rarity, isEpicRoutingItem)`（纯规则已落地、9 组单测）；**接进会话层并开启掷骰归属尚未落地**（需跨会话交付，见台账"本片剩余"）
- 裁决: 对齐
- 验收判据: S5 前置**已闭合**：真正决定掷骰的是 `CItem::check_item_routing@0x0850D756` = `isEpicRoutingItem || get_rarity() > 0`（`get_rarity@0x080F12D6` = `*(int*)(this+56)`、`isEpicRoutingItem@0x0846996A` = `*(u8*)(this+121)`）；`get_item` 里的 `get_rarity(item) > 3` 只是把"是否允许路由"从默认值（入口 `v149 = 1`）换成请求方许可 `a4`。`CItem+0xbd` 在 `get_item` 里**一次都没被读**。用户 2026-10-03 知情后裁决 B1：忠实实现该门控。**本条 2026-10-03 由"待真机"改写，"蓝装以上/阈值 > 3"的口径作废**（清单顶部 S5 收尾块）
- 归属切片: S5

### PD-3011 NOTI 40 掉地：客户端处理器与我们的构包字段
- 官服行为: 掉落物以 NOTI 40 `DROP_ITEM` 下发，客户端据包内字段建立地面 actor
- 官服证据: 官服掉落入口 CBattle_Field::drop_item@0x0830db84(89)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 40 | findstr /C:"handler=0x00422CC3"
  ```
- 客户端可达性: NOTI 40 → 处理器 0x422CC3（非 default）
- 现状: `CreateDropItem`（GameProtocolEngine.cs:3630-3653）；生成点 EntranceService.cs:6600（被动掉落）与 12026（主动丢弃）；组队共享广播的逐点接线待 S5 核对
- 裁决: 对齐
- 验收判据: S5 先跑 `client_surface.py noti-fields 40` 取客户端读取序列，与 `CreateDropItem` 的 7 字段逐字段对照；真机：队友端能看到同一件掉落（run #42 已观察）
- 归属切片: S5

### PD-3012 掉落生成规则的证据边界（脚本/表侧，不在二进制里）
- 官服行为: 房间被动掉落由战斗场地生成并入地面物集合（`CBattle_Field::drop_item(map_item)`）；**生成规则本身**来自脚本/表
- 官服证据: CBattle_Field::drop_item@0x0830db84(89)；逐条掉落规则无函数级证据
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CBattle_Field::drop_item | findstr /C:"0x0830DB84"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 我们用 `DropCatalog`（src/DFLegacy.Server/DropCatalog.cs）+ 房间定义生成；参考实现日志有 `generate_dungeon_drops mon_lv=… role=0/1`（[推断] 级证据）
- 裁决: 保留
- 验收判据: 单测：同一房间的被动掉落集合在"换房/重进"时一致；真机：掉落数量与等级区间合理（不作逐条对齐要求）
- 归属切片: S5


## ④ 结算与翻牌（S6）

### PD-4001 结算状态机 `dungeon_clear_state` 2→3→4→5 全部由服务端定时器推进
- 官服行为: state 2 → `TimerClearDungeonReward`（key 17）广播 CMD 72 → state 3；→ `TimerClearDungeonScoreScoll`（key 18）广播 CMD 73 → state 4（并挂 30s key 19 与 4s key 16）；`TimerCardSelect`（key 16，4 秒）做 `CompleteCardSelect` + `SendEachCardInfo`（CMD 74 全队）→ state 5 + `dungeon_clear`；`TimerClearDungeonCardSelectRight`（key 19，30 秒）兜底 state 4→5
- 官服证据: TimerClearDungeonReward::dispatch_sig@0x0863449c(309)、TimerClearDungeonScoreScoll::dispatch_sig@0x086345d2(500)、TimerCardSelect::dispatch_sig@0x086343ba(226)、TimerClearDungeonCardSelectRight::dispatch_sig@0x086347c6(147)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func TimerCardSelect::dispatch_sig | findstr /C:"0x086343BA"
  ```
- 客户端可达性: CMD 72/73/74 是 class=1 命令，客户端必然处理（翻牌 UI 的驱动）
- 现状: 会话内自研的结算流程（`Settlement` 状态分散在 EntranceService/PartyCardSettlementFlow）
- 裁决: 对齐
- 验收判据: 单测：四个状态按序推进且每个定时器只触发一次；真机：两端都到达翻牌页（run #42 已达成，重写后需重验）
- 归属切片: S6

### PD-4002 CMD 73 上行：每槽位一个 ushort = 该成员的疲劳状态
- 官服行为: `Dispatcher_Card_Select_Right_State` 处理 CMD 73，要求 `CheckRecvFromAllMember` 为真；回包 `send_to_party`，每槽位 ushort：`0`=疲劳已满 / `1`=还有疲劳 / `0xFFFF`=空槽位
- 官服证据: Dispatcher_Card_Select_Right_State::dispatch_sig@0x081ff504(729)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func Dispatcher_Card_Select_Right_State::dispatch_sig | findstr /C:"0x081FF504"
  ```
- 客户端可达性: 客户端确实会发（run #42 `seen_as_cmd=8`）
- 现状: 我们把"非自己的槽位"一律写 `0xFFFF`（等于宣称队友是空位），与官服语义不符
- 裁决: 对齐
- 验收判据: 单测：每槽位写该成员真实疲劳状态（空槽才 0xFFFF）；真机：翻牌页两端的卡位权限一致
- 归属切片: S6

### PD-4003 CMD 74 是"全员卡表快照"，由 `SendEachCardInfo` 广播（class=1/cmd=74 → `send_to_party`）
- 官服行为: `CParty::SendEachCardInfo` 构造含所有卡列 owner/内容/选中状态的 class1/cmd74，交给 `CParty::send_to_party` 发给全队
- 官服证据: CParty::SendEachCardInfo@0x085b44d6(606)；`put_header(class=1, cmd=74)@0x085b450a`；发送者 `CParty::send_to_party@0x085b4700`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py layout CParty::SendEachCardInfo | findstr /C:"cmd=74"
  ```
- 客户端可达性: 客户端会发 CMD 74 上行（run #42 `seen_as_cmd=11`）；广播的快照驱动两端 UI
- 现状: 已实现（`BroadcastCardSelectionSnapshotAsync`，实例级卡表 `PartyCardSelectionState`），run #42 四局真机验收通过
- 裁决: 对齐
- 验收判据: 单测：快照对两端逐字节相同、列归属来自实例卡表；真机：两端看到不同列（run #42 已验，重写后需重验）
- 归属切片: S6

### PD-4004 `CompleteCardSelect` 在共享卡表上补全未分配卡列
- 官服行为: 自动翻牌 = 在 party 卡表上遍历未分配卡列，按有效成员槽位补全（不是每个会话各找本地最低位）
- 官服证据: CParty::CompleteCardSelect@0x085b4786(278)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::CompleteCardSelect | findstr /C:"0x085B4786"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 已实现（原缺陷"两端都认领第 0 列"即由此修掉）
- 裁决: 对齐
- 验收判据: 单测：两端都不操作时只生成一份 party 级结果、每列最多一个 owner；真机：两端列不同（run #42 clear #4 已验）
- 归属切片: S6

### PD-4005 NOTI 34/35 的载荷（含"最佳通关时间"附加字段）
- 官服行为: 结算页数据由 `CParty::makeBestClearTimePacket` 等构造（NOTI 34 PLAY_RESULT / NOTI 35 CLEAR_DUNGEON_REWARD）
- 官服证据: CParty::makeBestClearTimePacket@0x085be1f8(418)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::makeBestClearTimePacket | findstr /C:"0x085BE1F8"
  ```
- 客户端可达性: NOTI 35 → 0x420C23、NOTI 34 → 0x420B0F（均非 default）
- 现状: `GameProtocolEngine.cs:3742-3800` 一带自研布局（"Keep all four columns present even in solo play"）
- 裁决: 待真机
- 验收判据: S6 用 `packet_layout.py` 展开 `makeBestClearTimePacket` 与 NOTI 35 的构包函数，与我们的逐字段对照（NOTI 35 的 4 列语义是历史缺陷点，见 STATUS §2.7 第 18 条）
- 归属切片: S6

### PD-4006 篝火页 EPLP 与两个回城定时器（间隔参数位序仍未证实）
- 官服行为: EPLP = 客户端上行 CMD 75；服务端侧 `TimerEPLPReturnVillage`（key 20）与 `TimerEPLPCommandWait`（key 21）负责最终把玩家送回城；`Dispatcher_EPLPCommand` 是 CMD 75 的入口
- 官服证据: Dispatcher_EPLPCommand::dispatch_sig@0x081feedc(1128)、TimerEPLPReturnVillage::dispatch_sig@0x08634c06(100)、TimerEPLPCommandWait::dispatch_sig@0x08634c6a(147)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func TimerEPLPReturnVillage::dispatch_sig | findstr /C:"0x08634C06"
  ```
- 客户端可达性: 客户端会发 CMD 75（真机 24 次，见 STATUS §2.7）；篝火页由客户端 UI 驱动
- 现状: 120 秒兜底 + 最后 10 秒逐秒系统消息（用户真机口径）；`ReturnToVillage` 拉全队
- 裁决: 待真机
- 验收判据: S6 前置：用已知的 4 秒（key 16）/30 秒（key 19）交叉验证 `InsertTimer` 的秒参位序，再断言 key 20/21 的间隔；真机：点"返回城镇"全队 1 秒内回城、不点则 ~2 分钟兜底 + 逐秒提示
- 归属切片: S6

### PD-4007 NOTI 67 CARD_SELECT_COMPLETE_STATE 本客户端不处理
- 官服行为: 官服有 `SendCardSelectComplete` 广播（NOTI 67，空载荷）
- 官服证据: 无（未取到该构造函数的独立地址；`packet_senders.txt` 未列 class0/cmd67）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 67 | findstr /C:"default=yes"
  ```
- 客户端可达性: case 67 落 default（`0x42A069`）——客户端不处理
- 现状: 未实现（正确）
- 裁决: 无法实现
- 验收判据: 不实现；不要把它当作翻牌状态机的必需包（前身设计稿 §5.4 已给同样结论）
- 归属切片: S6

### PD-4008 `CParty::dungeon_clear` 逐人发放（奖励物品 + SetEPLPState + 60 秒定时器）
- 官服行为: 全员选完那一刻 `set_dungeon_clear_state(5)` + `CParty::dungeon_clear`：逐人发奖励物品、`SetEPLPState(2)`、起 60 秒 key 21 计时器
- 官服证据: CParty::dungeon_clear@0x085ab736(1354)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::dungeon_clear | findstr /C:"0x085AB736"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 各自的结算与奖励发放（`PartyCardSettlementFlow` + store 幂等提交）
- 裁决: 对齐
- 验收判据: 单测：每人最多一次奖励、落库幂等（已有 `--dungeon-reward-commit` 定向测试）；真机：两侧金币/物品各自到账（run #42 账本 11 条）
- 归属切片: S6

## ⑤ 跨端 UDP 中继（S2·S8）

详见 `relay-recon.md`（本目录）。一句话：**官服的 relay 是独立进程、会解析包并按 ACCID 配对转发**，不是纯字节管道。

### PD-5001 relay 是"解析 + 按 ACCID 配对转发"，不是纯字节管道
- 官服行为: relay 进程（独立于 game）用 epoll reactor 同时接受 TCP 与 UDP；`TCPUser::onPacketParse` 从环形缓冲取包、经 handler 表分派、用 `getACCID()` 索引用户；两张 `hash_map<uint32, TCPUser*>` / `hash_map<uint32, UDPUser*>` 是配对表；`UDPHandlerS2S::dispatch(char*, int, int)` 负责服务端到服务端的数据报转发
- 官服证据: RelayServiceApp::TCPUser::onPacketParse@0x0805d02e(1334)；RelayServiceApp::UDPHandlerS2S::dispatch@0x0805e406(144)；EpollReactor<RelayServiceApp::TCPUser,TCPSocket,TCPUser>::handleEvents@0x0805c158(1487)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py --elf official/template/neople/relay/df_relay_r func RelayServiceApp::UDPHandlerS2S::dispatch | findstr /C:"0x0805E406"
  ```
- 客户端可达性: 不适用（服务端进程）；客户端侧对应命令见 PD-5003/5004
- 现状: 我们没有 relay；`GameplayDatagramService` 只做 NAT 探测/keepalive 分类（UDP 审计 `mt=0`）
- 裁决: 待真机
- 验收判据: S2 产出 relay 包格式与配对键的结论文档；S8 实现后真机验"一端的数据报经中转后对端收到"
- 归属切片: S2·S8

### PD-5002 relay 的配置面（端口 / 监控认证 / 容量）
- 官服行为: relay 由 `relay_200.cfg` 配置：`this_tcp_port`/`this_udp_port`（部署时模板替换）、`max_client=1000`、`this_pvp_channel=1`；另有 `[aradauth]` 段（`udp_port_to_monitor`、`monitor_ip/port`、`relay_index=200`）⇒ 启动后向监控进程认证（`RelayService::setAuthenticated(uint)`）；启动方式 `./df_relay_r relay_200 run`
- 官服证据: RelayServiceApp::RelayService::setAuthenticated@0x08051b2c(74)、RelayServiceApp::PortInfo::setTCPPort@0x08049f5c(13)；配置 `official/template/neople/relay/cfg/relay_200.cfg` 与 `official/template/init/run/start_relay.sh`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py --elf official/template/neople/relay/df_relay_r func RelayServiceApp::RelayService::setAuthenticated | findstr /C:"0x08051B2C"
  ```
- 客户端可达性: 不适用
- 现状: 无对应组件
- 裁决: 待真机
- 验收判据: S2 文档给出"自建 relay 需要哪些端点与认证步骤"；S8 真机：客户端能连上我们的 relay 并完成配对
- 归属切片: S2·S8

### PD-5003 CMD 2 SET_UDP_IP_PORT：自报地址 + ACK + 转发给其他用户
- 官服行为: 客户端用 CMD 2 自报 UDP 地址（byte/int/int/short/int/int/str + sha256 令牌）；服务端校验后回 ACK（CMD 2/0x01）并把该声明**转发给其他用户**
- 官服证据: DisPatcher_SetIP::dispatch_sig@0x081f468c(1626)；转发点 class1/cmd2 @0x081f4c71（见 `STATIC-FINDINGS.md` §2.4）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_SetIP::dispatch_sig | findstr /C:"0x081F468C"
  ```
- 客户端可达性: 客户端确实会发（run #42 `seen_as_cmd=4`）
- 现状: 完全不处理该命令；NOTI 11 的对端表来自"服务端观察到的 UDP 源地址"（`GameplayDatagram.TryGetPeerInfo`）
- 裁决: 对齐
- 验收判据: 单测：收 CMD 2 → ACK + 落库 + 转发他人（单人时只 ACK，零回归）；真机：日志出现 CMD 2 的 ACK 与转发
- 归属切片: S2

### PD-5004 CMD 100/479/599 与 `DisPatcher_ResPeer`（P2P 结果与统计）
- 官服行为: P2P 连接结果与统计类命令进入 `DisPatcher_ResPeer::dispatch_sig`（9341 字节：读 short/byte/int、`GetReceivedRequestType`、按状态分支、必要时 `GameWorld::find_from_world` 后回包）
- 官服证据: DisPatcher_ResPeer::dispatch_sig@0x081f088e(9341)；调用序列见 `official/calls_p2p.txt`
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_ResPeer::dispatch_sig | findstr /C:"0x081F088E"
  ```
- 客户端可达性: run #42 里 CMD 100/479/599 各 **0 次**（`cmds --log`）
- 现状: 未实现
- 裁决: 待真机
- 验收判据: S2 先判"客户端在什么条件下才发这些命令"（当前 0 次 ⇒ 很可能要先有成功的 P2P）
- 归属切片: S2

### PD-5005 对端地址表与主机槽位的构造者（NOTI 11 / NOTI 26）
- 官服行为: 对端地址表由 `CParty::send_party_ipinfo`（NOTI 11）下发；主机槽位由 `CParty::send_host_info`（NOTI 26）下发；两者被 `DisPatcher_ResPeer` 与 `Dispatcher_Change_PartyMemberPosition` 复用
- 官服证据: CParty::send_party_ipinfo@0x0859cea2(683)、CParty::send_host_info@0x0859cdd2(207)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::send_party_ipinfo | findstr /C:"0x0859CEA2"
  ```
- 客户端可达性: NOTI 11 → 0x41C7E7（每成员 7 字段）、NOTI 26 → 0x41F144（1 字节槽位）——均已验
- 现状: 已实现（`CreateUdpPeerInfo` + host 包）
- 裁决: 对齐
- 验收判据: 见 PD-1001/PD-1002；S2 需把"地址表来源"从"观察 UDP 源"改成官服的 CMD 2 声明
- 归属切片: S2

### PD-5006 游戏数据报（`mt`）到底发给谁 —— 至今未结案
- 官服行为: 官服架构里 relay 承担客户端↔客户端的数据报中转；但**我们尚未证明**本客户端的游戏数据报走 relay
- 官服证据: relay 二进制与配置证明服务端侧存在该角色（PD-5001/5002）；客户端侧发包去向**无**证据
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py --elf official/template/neople/relay/df_relay_r func RelayServiceApp::TCPUser::onPacketParse | findstr /C:"0x0805D02E"
  ```
- 客户端可达性: 真机 UDP 审计至今显示成员只发 NAT 探测/keepalive，`mt=0`（`STATUS.md` §2.7 第 9 条）
- 现状: `GameplayDatagramService` 收包只做 NAT/keepalive 分类
- 裁决: 待真机
- 验收判据: S8 前置真机实验：副本里让一方持续移动/攻击，同时抓两侧 UDP 端点与内容（`DFLEGACY_UDP_AUDIT=1` + 本机抓包），判定 `mt` 数据报的目标（对端直连 / 服务端 / 根本没有）；结论决定 S8 形态（纯转发 vs 需解析）
- 归属切片: S8


## ⑥ 副本内其它（S7）

### PD-6001 副本内死亡走 `die_user`，不是退队
- 官服行为: `DisPatcher_DieCharacInDungeon::process` 调 `CParty::die_user`（不是 `leave_user`）⇒ 死亡本身不算退队
- 官服证据: CParty::die_user@0x085a7828(586)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::die_user | findstr /C:"0x085A7828"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 未单独处理（死亡倒计时被送回城时客户端若发 CMD 45 会走退队路径）
- 裁决: 待真机
- 验收判据: 真机：副本内死亡 → 倒计时结束被送回城时，观察客户端是否自行发 CMD 45；若发，走 PD-1004 的路径（自动退队），需显式记录为用户可见后果
- 归属切片: S7

### PD-6002 疲劳 `NOTI 36`（每房各自扣）
- 官服行为: 疲劳变化以 NOTI 36 下发；原版"每人各自扣"
- 官服证据: 无（未取到扣疲劳的官服函数级地址；`CDataManager::get_hellparty_script_values`@0x0836378c(13) 只是取脚本值的 thunk）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/client_surface.py noti 36 | findstr /C:"handler=0x0042119F"
  ```
- 客户端可达性: NOTI 36 → 0x42119F（非 default）
- 现状: 每个成员在自己入场/换房时各扣一次（`ConsumeDungeonRoomFatigueAsync` + `lastSettledRoomX/Y` 防重复）
- 裁决: 保留
- 验收判据: 单测：换房不重复扣、镜像刷新不重复扣；真机：两端疲劳各自下降
- 归属切片: S7

### PD-6003 地狱模式入场券扣谁的（官方侧未取证）
- 官服行为: 地狱模式入场券的扣除对象（只扣队长 / 全员扣 / 队伍共享一张）——官服侧未取到证据
- 官服证据: 无（`CDataManager::get_hellparty_script_values`@0x0836378c(13) 只说明地狱脚本值存在）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CDataManager::get_hellparty_script_values | findstr /C:"0x0836378C"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: 只在队长会话的入场路径扣（队员不碰背包）
- 裁决: 待真机
- 验收判据: S7 前置：在官服找地狱入场的扣券点（`game_start` 的 hell 分支）；在此之前保持现状并在清单标注"仅扣队长"
- 归属切片: S7

### PD-6004 位置同步：CMD 37 / NOTI 22 / NOTI 23 / NOTI 24 的字段与红线
- 官服行为: 客户端用 CMD 37 报位置；服务端用 NOTI 22 转发位置、NOTI 23/24 下发区域与区域内用户表（NOTI 24 会**建立本客户端的 town/area**）
- 官服证据: DisPatcher_SetPos::read@0x081c97a6(289)、DisPatcher_SetArea::read@0x081c91e6(514)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func DisPatcher_SetPos::read | findstr /C:"0x081C97A6"
  ```
- 客户端可达性: NOTI 22 → 0x41E878、NOTI 23 → 0x41E9C7、NOTI 24 → 0x41EC7C（均非 default；NOTI 24 的处理器会调 0x89FB20 切地图）
- 现状: 位置转发（`UpdatePositionAsync`）+ 外观/名册补包；**红线**：绝不用 NOTI 24 把客户端拉回城镇地图
- 裁决: 对齐
- 验收判据: 单测：副本内位置转发不带 NOTI 24；真机：P2（两人互相可见并移动）
- 归属切片: S7

### PD-6005 组队经验/物品分配系数
- 官服行为: 组队时经验与掉落按人数加成（结算页 `partyMemberCount` 参与计算）
- 官服证据: 无（系数在脚本/表侧；`CParty::clear_reward`@0x085ae0d4(12569) 是结算主函数，系数来源待 S6/S7 取证）
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::clear_reward | findstr /C:"0x085AE0D4"
  ```
- 客户端可达性: 不适用（服务端内部）
- 现状: `clear-card rewards prepared` 用 `partyMemberCount: instance.MemberCount` 计算加成
- 裁决: 保留
- 验收判据: 单测：单人为 1 倍、双人为官服系数；真机：run #42 结算金币量级合理（`party=50`）
- 归属切片: S7

### PD-6006 技能使用的广播（`CParty::use_skill` 不发包）
- 官服行为: `CMD 41` 的官服处理只做反外挂校验 + `CParty::use_skill` → `OnUsedSkill`（按槽位累计统计，**不发任何包**）⇒ 队友看到技能特效靠客户端↔客户端通道，不是服务端广播
- 官服证据: CParty::use_skill@0x085a77d8(40)、CParty::OnUsedSkill@0x085b5f8e(176)
- 可重跑命令:
  ```
  python ../../2026-10-02-party-dungeon-official-rewrite/work/official_surface.py func CParty::OnUsedSkill | findstr /C:"0x085B5F8E"
  ```
- 客户端可达性: CMD 41 是客户端上行（run #42 有 `seen_as_cmd`）；特效依赖 ⑤ 的 P2P/中继
- 现状: 我们只回一个成功字节（`CreateUseSkillReply`），不广播
- 裁决: 保留
- 验收判据: 与官服一致：**不**新增服务端广播；技能同步的解法在 ⑤（S8），不在 S7
- 归属切片: S7

