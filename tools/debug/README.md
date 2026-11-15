# 副本调试覆盖层

2026-10-30[门前区域终点修复](../../knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)：N地图右键与 `client_native_route` 共用控制器，现允许原PVF `dungeon_gate` 作为精确最终城镇区域，支持跨区进入和区内坐标。门及其4px扩张区仍返回 `goal_inside_portal`，入口不能作为中途节点，选择／副本场景仍中断；ABI、96字节DLRT、图标保护和黄色双次反馈不变。实机覆盖 `1/2`，不外推全部入口。

当前功能、四端构建与未闭合边界见[2026-10-30交接](../../HANDOFF-2026-10-30.md)。资源／键鼠／八类窗均为有界局部观测；原生循环和运动／provider诊断沿用各自默认关闭开关，不将本次源码归档当作日常部署或U07／U08闭环。

`ijl15.dll` 在首个渲染帧加载客户端目录的独立 `debug.dll`（ABI v2）。面板使用游戏窗口所属的浮动窗口，在聊天文字之上绘制独立背景，不再进入原生文字队列；不激活面板、不接管键盘焦点。DLL 加载与工作线程初始化均在 `DllMain` 之外。

进入副本约半秒内显示本次 BOSS 房的实际箱怪表：有/无、数量、容器脚本索引与地图坐标、怪物编号/等级、待进房/未释放/已释放/已死亡、通关状态。包含箱子和地堡使用的 `IsBoxMonster` 条目。

- 拖动标题栏移动；拖动边框/角落改变大小，滚轮查看超出窗口的记录。
- 点击右上 `−` 或 F9 收起成可拖动的圆点，点击圆点恢复。
- 点击 `×` 或 F10 隐藏，F10 再次显示；下一次进入副本自动显示，城镇隐藏。
- 切出或最小化游戏时隐藏，恢复游戏时跟随显示；面板限制在游戏客户区内，随游戏窗口移动。
- 位置及展开尺寸保存在客户端 `Config.ini` 的 `[DebugOverlay]`：`X/Y/Width/Height`。
- 鼠标进入面板时临时显示 Windows 指针，离开、隐藏或销毁后配平恢复游戏的指针显示状态；边框继续显示对应的缩放指针。窗口仅在显示/位置变化时更新，不在每帧重新调整所属游戏窗口的层级。

两个 DLL 必须一起更新（ABI v1/v2 不兼容）。窗口模式的显示、鼠标进出和开关真实点击已完成双客户端实测，见[验收记录](../../knowledge/decisions/2026-10-27-loot-live-verification.md)；独占全屏等场景仍需扩展实机确认。

## 掉落测试开关

面板标题下方增加“掉落测试”复选框，显示当前副本实例的真实开关状态。仅 `test` 账号可点击；队友可看到相同实例状态。提交时显示“等待确认”，收到服务端确认后更新；超过 5 秒仍未确认显示失败，可重试。没有新鲜快照、旧版服务端、非副本场景或无权限时不可操作。

开关复用现有聊天命令 `掉落测试` 的满掉落生成逻辑：每类物品只取当前等级候选池中最稀有的一件，并受现有总数上限约束。设置只影响当前副本实例，换房保留，结束或开启新一局时关闭。

自动测试可使用与面板相同的本地控制接口，不依赖中文输入：

```powershell
python tools/debug/drop_control.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --user-id 2 --on
python tools/debug/drop_control.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --user-id 2 --off
```

`--user-id` 是本次会话的区域 UID，取自 `dfdebug-<uid>.bin`，不是账号号或角色持久化 GUID。目录须与本轮服务端及客户端的 `SnapshotDirectory` 相同。退出码 0 和输出 `success:true` 表示收到对应请求的服务端确认；写文件成功不算通过。脚本拒绝过期快照、无权限、换局和被其他控制器覆盖的请求。

本地文件协议（仅启用 `Debug.ClientOverlay` 时处理，不增加游戏网络协议）：

- 请求原子替换为 `dfdebug-drop-<uid>.bin`，32 字节，小端：`u32 magic=0x31504444, u32 version=1, i64 requestId, i64 runId, u16 userId, u32 reserved=0, u8 enabled(0/1), u8 reserved=0`。`requestId` 为当前 UTC FILETIME，同时用于时效与重复请求判断。
- 服务端验证活跃实例、成员、`test` 账号、run ID 和 5 秒请求时效，按明确的开/关值幂等设置；旧请求不能覆盖新请求。
- 快照文件 v2 保持 40 字节头和 12 字节怪物记录不变，末尾追加 12 字节：`u8 enabled, u8 allowed, u8 result, u8 reserved, i64 requestId`。`result`：0 无请求、1 已应用、2 无权限、3 已过期或不同副本。原生模块和监听脚本仍能读取 v1 快照，此时禁用开关。
- 副本退出后快照删除，残留旧请求不会作用于下一局。该接口是本机测试工具，目录应沿用现有本地调试目录的访问权限。

## 配置

客户端 `Config.ini`：

```ini
[Features]
EnableDebugOverlay=1
EnableAutoInput=0

[DebugOverlay]
SnapshotDirectory=G:\Game\DNF_ACT1\DF\town-test\debug-overlay
```

服务端 `server.json` 的 `DFLegacy.Debug`：

```json
"ClientOverlay": {
  "Enabled": true,
  "SnapshotDirectory": "debug-overlay"
}
```

服务端相对路径基于服务端程序目录。两客户端应指向同一个目录。覆盖层只读对应本地区用户 ID 的快照，过期超过 1.5 秒、上一轮副本或别人的数据不显示；服务端未启用时显示“等待服务端调试数据”，不会误报无箱怪。默认发行配置关闭此功能。

BOSS 房原先在进入该房间时才随机生成。启用服务端调试后会提前生成并缓存该房间，真实进房复用相同怪物表和种子；不增加访问次数、不自动过门或击杀。生成时机提前会改变本轮随机数的消耗顺序，概率规则不变；关闭服务端开关后恢复原来的生成时机。

## 只读监听

```powershell
python tools/debug/watch_dungeon.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --out .tmp/debug-session/events.jsonl
```

脚本不操纵客户端，不写存档或内存，只将房间、箱怪状态与结算状态的变化写入 JSONL，玩家负责操作。Ctrl+C 停止。

## 原生客户端状态

默认关闭的回调计时可独立开启（修改INI后重启客户端）：

```ini
[DebugOverlay]
ExportFrameProfile=1
```

在相同`SnapshotDirectory`导出`dfdebug-frame-profile-PID.json`。查询：`python -m tools.debug.frame_profile --directory <目录> --pid <PID>`；Python可用`load_profile`读取完整8桶／32事件。CLI汇总是本进程启用后的累计值，分阶段比较使用`completed_windows`及明确时间范围。只测`DebugRender`原有工作至内容发布及相邻调用间隔，排除计时器自身汇总／导出；不是GPU呈现计时或连续前台证明。未知保持null，时钟错误、覆盖缺口明确报告，不能以低SDK读取耗时推断无整帧停顿。见[实机计时、两次未归因间隔及原图](../../knowledge/decisions/2026-10-30-frame-profile-verification.md)。

同一开关另导出`dfdebug-frame-pipeline-PID.json`，覆盖外层性能绘制调用，分为`input_and_original_overlay`、`text_setup`、`debug_module`、`fps_and_memory_format`、`text_draw`五段。查询时追加`--pipeline`，或使用`load_profile(..., pipeline=True)`。Host／Frame ABI v2及原回调文件格式不变；旧debug DLL没有可选导出时不记录外层数据。首个加载debug模块的帧不纳入外层计时。计时仍不覆盖调用之外的原生更新、GPU Present、记录器自身和连续前台状态。

### 原生玩法接收和运动入口

默认关闭；需在启动前同时启用`Features.EnableDebugOverlay=1`和`DebugOverlay.ExportGameplayTrace=1`。原生接收观察写入64条固定环，每条最多保留2048字节；游戏线程不写盘、不分配动态内存，沿用工作线程每200ms尝试导出。读取：

```powershell
python -m tools.debug.gameplay_trace --directory <SnapshotDirectory> --pid <PID>
```

文件`dfdebug-gameplay-trace-PID.json`使用`dfl-native-gameplay-trace/v2`，消费者也支持v1。`accepted`位于ACT1的`78EF2B`，表示已过原生CRC门、尚未opcode分派；保留原始内部应用消息，独立严格解码器可继续校验CRC。`gp4_movement_entered`位于`78F22C`，表示对象、房间和关联角色检查通过，将调用原`4C9270`，不表示函数已经完成或坐标已经收敛。两种事件分别计数。

`frame_token`是可复用的栈地址。关联事件应同时核对序号相邻、thread、context、frame、对象／角色／房间及flags；有序号缺口、`lock_drops`或`invalid_records`时不作缺失事件或唯一配对推断。`capacity_overwrites`是环的正常覆盖计数，实际采集是否丢失须比较连续读取的serial范围。静默且无新事件时文件可过期；过期不等于没有消息。`provider_buffer_match`仅表示指针等于原生provider结果缓冲区，不能单独证明GP42分支执行或与某个外层包一一对应；不扫描不透明字节寻找内部头。

观察通过原始调用继续执行，没有替换TerSafe provider或改动其返回值。旧四参数`DebugObserveGameplay`导出保留，新入口独立命名`DebugObserveGameplayV2`；新版IJL缺少所需导出或安装不完整时同时禁用两个观察入口。相关回归、四端数据及当前仍未关闭的大差见[剩余阻断接续](../../knowledge/decisions/2026-10-30-remaining-blockers-followup.md)。

仅测试地面／库存观测现可在角色死亡后继续更新，独立于修改校验；新增`observer`绑定当前本机角色、stage、死亡状态及原生actor-map，后者脱离时为null。地面采用当前stage地图，消费者拒绝旧的未绑定死亡数据；修改接口仍要求存活。见[四端死亡观测、库存核对和原图](../../knowledge/decisions/2026-10-30-dead-observation-verification.md)。

v2支持只读scene2：选择界面没有town／room／me／players／enemies／skills，不授权移动和战斗操作。`ui.dungeon_selector.selected`是索引，新增`selected_dungeon_id`／`difficulty_raw`为经重复校验的选中行投影；`transition_pending`／`phase_raw`／`stage_token`保留原生加载状态。`active_raw`是初始化标志，visible仍null。

`ui.dialogs`仅列出已验证的八类常用原生窗，十六进制07/09/23/25/30/56/59/6A，JSON中的`class_id`为十进制7/9/35/37/48/86/89/106。`covered_class_ids`显式声明局部覆盖；含`valid`／`truncated`／`items`，条目有实例／根token及`shown_raw`／`input_enabled_raw`。活动池与待关闭队列分开，列表不完整时行查询拒绝；空列表不表示其他UI关闭。旧v2没有覆盖字段时只接受原五类窗。

条目的`placement`记录原生层token、附着、层内顺序及层shown/enabled原值。07/09属于低层，其余当前类属于高层；不同层可都有order0。同层共用有界前后快照，单层损坏只使对应placement未知。根或层标志不等同像素可见和全局输入阻断。新增07/09四端打开／取消及56仅静态覆盖的边界见[接续验收与五张原图](../../knowledge/decisions/2026-10-30-u05-mouse-dialog-followup.md)。

`ui.command_input`的父scope仍为`native_command_checker_type0`，绑定本机UID／actor／checker，提供`keyboard_lock_level/keyboard_suppressed`。可选`mouse`对象使用独立scope `native_command_checker_type1`，含`lock_level`、`lock_suppressed`、`checker_flag90_raw`和`physical_excluded`：锁非零或flag90置位只表示早期物理检查排除，上层仍可能合成命令，城镇地面点击另有路径。鼠标字段无效不抹去独立有效的键盘状态。

独立`ui.mouse_button_state`使用scope `engine_mouse_button_state`和`deferred`，表示原生CFD208使按钮状态更新暂缓。原有held状态仍保留、坐标／回调继续；只有WM分支排队，DirectInput分支不承诺重放。不可将这些字段合并为全局操作许可；`ui.input_blocked`保持null，短脉冲可能被目标200ms的SDK导出漏采。

新增标量沿用既有CLI，无需新增动作接口，例如：

```powershell
python tools/debug/client_state.py --directory <SnapshotDirectory> --pid <PID> --wait ui.command_input.mouse.physical_excluded=false --timeout-seconds 5
python tools/debug/client_state.py --directory <SnapshotDirectory> --pid <PID> --wait ui.mouse_button_state.deferred=false --timeout-seconds 5
```

先从完整快照按class_id定位当前条目索引，再查询`ui.dialogs.items.0.class_id`／`placement.attached`等；数组顺序不等于窗类号。旧v2缺字段保持未知，null不匹配任何条件。

新增v2动态技能：`skills[i].cooldown_ms`、`cooldown_blocked`和`usable`，由能力标志分别声明支持。有限冷却过期为0；原生持续禁用为毫秒null、blocked=true；其他未知为null。`usable`为正常手动施放的原生预检候选，不是按键／目标／命中回执。原生上下文、死亡、CD、MP成本和已证明资源条件都通过才为true；超界或未证的物品／计数布局、非空装备修正和精度边界为unknown。支持范围及5张原图见[验收](../../knowledge/decisions/2026-10-30-skill-state-verification.md)。

读取完整快照后按`id`找到本次技能数组索引，再使用既有查询／等待，例如`--wait skills.0.usable=true`。数组位置不是技能ID，不能把裸Z一律当id5；原PVF中鬼剑士裸Z上挑为id46。消费者区分已知0／false与null，旧v1和未绑定本地角色不开放动态技能字段。更新DLL时同步更新消费脚本；没有调用分配／消耗型原生方法进行观察。

可选`skills[i].resources`使用scope `native_manual_precast_resources`，独立提供`mp_cost`、`use_limit`、`use_count`及`items_sufficient`。非零上限按ACT1原表及signed计数比较；上限0时count为null，不能把不适用写成已用0。物品需求采用原生向零截断插值，查询第一个匹配库存槽，不合并同ID堆叠。64节点计数表／32物品行／512库存槽均有界且校验变化；库存前后快照由本次技能采集共享。资源字段可在动作／死亡／冷却已阻止时独立有效，但true仍须全部守卫通过，不等于实际施放成功。

先按技能ID找到索引，再例如`--wait skills.0.resources.use_count=0`或`--wait skills.0.resources.items_sufficient=false`；只能等待已知值。旧v2缺对象保持未知，坏scope／来源／资源矛盾会拒绝。新2D DLL及消费脚本的[13条实机CLI、原始依据和交接边界](../../knowledge/decisions/2026-10-30-u05-resource-closeout.md)已归档；全局输入、密码PvP、非空装备修正和未证精度仍保留，不以局部资源充分证明全局许可。

新导出使用独立的 `dfl-client-state/v2` JSON，消费脚本同时兼容已有 v1；不改变 DLL ABI v2 或服务端箱怪协议。客户端先以 `EnableDebugOverlay=1` 加载 DLL，再显式开启默认关闭的导出：

```ini
[DebugOverlay]
ExportState=1
```

文件位于现有 `SnapshotDirectory`，每个进程独立为 `dfdebug-client-<pid>.json`。原生采集目标间隔200ms；收起或隐藏面板不关闭导出。错误客户端布局、未采集、过期或截断数据不能用于操作条件。新接口的实际刷新、开销和双端表现仍需对应实机验收。

从仓库根目录读取一次紧凑摘要，或有界等待一个条件：

```powershell
python tools/debug/client_state.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --pid 12345
python tools/debug/client_state.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --pid 12345 --uid 1 --wait scene=3 --timeout-seconds 10
python tools/debug/client_state.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --pid 12345 --wait me.dead=true --timeout-seconds 5
```

替换为本轮真实PID及可选区域UID。`scene`保留原生数字，1为Town、3为Dungeon；其他值未知。CLI不读游戏内存、不发输入；省略 `--process-started-filetime` 时仅以 `QUERY_LIMITED_INFORMATION` 查询一次进程创建时间。也可显式传入十进制整数FILETIME，避免重复查询。PID、创建时间、序号和时钟均保留整数精度。

等待只打印最终匹配或timeout，不逐帧输出。退出码0表示一次有效读取或已知条件匹配，1表示超时，2表示无效数据/身份变化；timeout范围0到300秒。默认最大采样年龄750ms，使用同机Win32 `GetTickCount64`，不使用UTC差值判断新鲜度。重写旧JSON或UTC跳变不会续期；同序号内容变化、序号/代次倒退也不能完成等待。

路径只遍历数据，可使用 `room.map_token`、`me.hp`、`players.0.dead` 等，不执行表达式。未知null永不匹配，连 `=null` 也不会完成；布尔false不等于数字0。完整性不足、未证MP/职业/冷却/可用性、未绑定实体/地图的位置以及独立 `server_context` 均不能当作已知动作条件。技能等级不等于技能已可用，authority token不推断为owner。v1 的有效Town保留 `me`、`room` 为null；v2读取Town原生角色绑定，HP和死亡仍未知，MP可在统计对象／secure值／UID绑定均通过时提供，附`mp_source`，不伪造HP0或死亡。队友MP是接收端原生观察值，详见[MP验收](../../knowledge/decisions/2026-10-30-u05-mp-input-dialog-verification.md)。

v2 新增 `town` 与 `ui`：Town包含当前 `town_id/area_id`、位置、原生 `destination.x/y` 和 `direction_raw.x/y`；`auto_move_active` 是原生连续移动开关的只读bool，缺失或未证原值时未知。Dungeon的 `town` 必须为null。Town位置须与本地角色及地图绑定一致。UI包含玩家菜单、M地图、选图及复活倒计时的只读状态。`ui.dungeon_selector.active_raw` 是原始字节，不代表可见性；该选择器的 `visible` 仍为null。`ui.countdown.state_raw` 保留原生数值，未知不变成0。隐藏玩家菜单的目标UID和动作列表不能完成操作条件。

摘要的 `doors.enabled_directions` 只表示原生mask中存在的拓扑方向，不等于门已开放或可通过。`room.map_clear_state` 保留原生保护整数的signed raw值（或null）；不把0/1改名为clear布尔，也不据此单独推断过门许可。

连续的本地控制循环应导入同一模块，不反复启动CLI或PowerShell：

```python
from tools.debug.client_state import (
    SnapshotError, UnknownFieldError, get_known_field,
    get_process_started_filetime, load_snapshot, now_tick_ms,
)

pid = 12345
started = get_process_started_filetime(pid)  # 一次元数据查询，无VM_READ
snapshot = load_snapshot(
    directory / f'dfdebug-client-{pid}.json',
    pid=pid, process_started_filetime=started, now_tick_ms=now_tick_ms(),
)
try:
    dead = get_known_field(snapshot, 'me.dead')
except UnknownFieldError:
    dead = None
```

`load_snapshot` 返回结构，坏JSON、错误身份、unsupported、未来/过期采样或截断抛 `SnapshotError`（`code`为有界原因）；有效结构中的未知/不支持字段由 `get_known_field` 抛 `UnknownFieldError`。必须先成功加载，再分别验证实际动作所需字段；仅 `scene=3` 不能证明门、位置或技能可用。长循环需维持UID/序号/代次约束，并在真正发出既有原生输入前重新核对进程创建身份和新鲜数据。本模块自身不发送任何输入。

## 城镇目的地接口

`client_navigation.py` 使用 debug.dll 独立的本地命令接口，调用客户端普通城镇目的地 setter。它只处理当前区域，不保证绕障或跨区路线；未知地图、运输模式、阻挡点及切区后的旧身份会被拒绝。采样器保持只读。城镇地图（本客户端按 N 打开）空白处点击与此接口共用原生目的地处理器，NPC、其他标记及面板按钮保留原操作。

组队 UI 也可以使用数据接口。`client_party.py` 提供 `state`、`request`、`accept`、`reject`；命令在游戏主线程调用原版 ACT1 逻辑，文件读写在 debug 工作线程进行。开启两项设置后重启客户端：

```ini
[DebugOverlay]
ExportState=1
EnablePartyCommands=1
```

```powershell
python tools/debug/client_party.py state --directory <SnapshotDirectory> --pid <进程号> --uid <本角色UID>
python tools/debug/client_party.py request --directory <SnapshotDirectory> --pid <进程号> --uid <本角色UID> --target-uid <对方UID>
python tools/debug/client_party.py accept --directory <SnapshotDirectory> --pid <进程号> --uid <本角色UID>
python tools/debug/client_party.py reject --directory <SnapshotDirectory> --pid <进程号> --uid <本角色UID>
```

`state` 导出邀请来源 UID、剩余毫秒和邀请序号。`accept` / `reject` 默认针对当前已读取的邀请，也可传 `--target-uid` 限定来源；没有邀请时不会盲目回复。自动化建议同时传 `--process-started-filetime` 固定进程创建时间。命令绑定 PID、创建时间、UID、场景代次、角色/地图/区域及邀请指纹，750 毫秒以上的旧状态、旧角色、重复请求和过期邀请会拒绝。`dispatched` 只表示客户端已调用原生处理，实际组队成功必须再检查双方 `client_state` 队伍名单。当前只支持同一城镇区域、一个待处理的组队邀请；多个同时出现的同类邀请或交易等其他邀请不会代答。发送缓冲区忙时返回不可用，调用方应重新读取状态后再决定是否重试。

状态文件为 `dfdebug-party-<pid>.json`，命令为固定 108 字节的 `dfdebug-party-<pid>.bin`，结果为 `.result.json`。接口默认关闭；不修改只读状态采样器，不发送外部网络包，不伪造 UI 按钮捕获状态。

副本内依次中途退出时，最后一人会保留原来的单人队，符合官服规则。此时三人新队邀请他，双方均已有队伍，原版 ACT1 会在发送前拒绝；新版回执以 `both_already_in_parties` 说明这个已验证原因。先由该角色在原生“队伍信息”中显式解散单人队，再邀请／接受即可，无需重启。CLI 不会自动退队；`players` 只有本人也不能据此判为未组队。未知原生拒绝仍返回 `native_action_failed`。

先按上文加载debug.dll，再开启默认关闭的导航开关并重启客户端：

```ini
[DebugOverlay]
ExportState=1
EnableTownNavigation=1
```

目录须和本轮 `SnapshotDirectory` 一致。将以下PID、UID、目标坐标替换为本次 v2 状态中的真实值：

```powershell
python tools/debug/client_navigation.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --pid 12345 --uid 1 --x 500 --y 240
python tools/debug/client_navigation.py --directory G:/Game/DNF_ACT1/DF/town-test/debug-overlay --pid 12345 --uid 1 --x 500 --y 240 --wait accepted --timeout-seconds 5
```

默认等待 `arrived`（最长35秒）：必须收到原生到达结果，并独立观察到新鲜、同身份状态中目标一致、XY各轴相差不超过3且原生方向停止值为4/4。`--wait accepted` 只等待 `requested`，表示原生 setter 已受理并核对目标；输出 `accepted:true,arrived:false` 不表示角色已到达。等待只打印一条最终JSON；退出码0表示所选条件达到，1为等待超时，2为拒绝、中断或无效身份/数据。CLI超时不会取消游戏内已受理的移动。

请求写入 `dfdebug-navigation-<pid>.bin`，DLL结果写入同名前缀 `.json`。脚本校验进程精确创建时间、fresh v2 Town采样和UID，并绑定 actor/user/map/context、Town/Area、generation、sequence及采样时钟；等待期间继续检查身份与采样。单进程 `.lock` 避免两个脚本并发覆盖，原子替换保证DLL只看到完整请求。脚本不会清理其他控制器的锁；异常退出残留锁应在确认该控制器已经退出后移走。默认 request ID使用正的单调递增整数；`--request-id` 可用于重放拒绝测试，但不能覆盖更大的已有ID。脚本不发送OS输入、不读写游戏内存。

Windows原子替换期间的瞬时快照/结果读取失败最多重试6次、总预算250ms，每次重新校验进程身份；过期、无效或身份变化不重试。受理后，只有捕获时钟严格晚于原生受理时钟的样本才用于判断目标是否改变；新目标会返回 `interrupted/destination_changed`，不会发送取消命令，也不会用受理前的200ms旧样本误判。

跨区域移动使用 `client_route.py`，一次只给最终城镇、区域和坐标。接口读取该 PID 实际客户端旁的 `Script.pvf`，自动规划区域入口及内部转弯，沿途仍调用同一个原生目的地 setter：

```powershell
python tools/debug/client_route.py --directory <SnapshotDirectory> --pid <进程号> --uid <本角色UID> --town 2 --area 2 --x 575 --y 247
```

Python 中可调用 `client_route.execute_route(directory, pid=pid, uid=uid, started=started, town_id=2, area_id=2, x=575, y=247)`。`started` 是本轮进程的 FILETIME。默认120秒，`--timeout-seconds` 最长300秒；`--pvf` 可指定与实际客户端 PVF 字节完全一致的副本，不能替换为不同地图。操作在整条路线持有原有 PID 导航锁，返回每段 request ID、观察到的区域/代次及最终位置。`ok:true/status:arrived` 要求原生接受并观察到最终坐标和停止方向，单次过门不代表完成。

路径依据原生地面矩形规划，每段完整包围盒须可通行，并绕开未选中的入口；内部转弯点留出原生坐标取整的停靠余量。出生在返回门内会自动离开再续行。最终目的地不能位于入口或其四像素保守范围。普通城镇路线不以副本入口作为中转，也不绕过任务、等级或其他运输限制。

每段从新鲜实际位置重新规划，绑定 PID/创建时间/UID/user/actor/context，只接纳预期区域、递增代次及新地图。发送前等待年龄不超过200ms的采样，为原生750ms校验留下排队预算；短暂无效或过期采样最多连续等待五秒，等待期间不下发移动。只有原生明确 `rejected/expired_request`、该请求从未受理、角色仍停在原处且身份/目的地/自动移动状态未变，才会重新取样、规划并以新ID更新请求，每个内部到达阶段最多三次，旧ID记录在 `expired_request_renewals`。原生时限保持不变；受理过的请求、仅超时而没有明确拒绝、其他错误及人工接管都不自动重发。

观察到人工方向输入、目的地覆盖、其他控制器接管、身份/地图异常和超时会中断。中断或超时不会强制取消已受理的原生移动；本轮支持范围为当前 ACT1 普通城镇地面，动态障碍仍可能导致有界失败。

请求wire为96字节、小端，Python `struct` 格式 `<4sHHIIQQQQQQIIIIiiii`：`DLNV`、u16版本1、u16长度96、u32 PID/UID、u64进程FILETIME/requestId/issuedTick/snapshotTick/generation/sequence、u32 actor/map/user/context token、i32 town/area/x/y。请求时效1000ms，引用采样时效750ms，未来时间与重放被拒绝。结果schema为 `dfl-client-navigation/v1`，`source` 区分 debug/minimap，`status` 为 requested/arrived/rejected/interrupted；结果携带身份、目标、位置、时钟与原因，不把写文件成功当作受理。

新增客户端内跨区模式：开启同一个 `EnableTownNavigation=1` 开关并加载新版 debug.dll 后，按 **N** 打开城镇地图，**右键目标区域的空白处**设置最终目的地。DLL 自行读取本客户端 PVF、规划转弯并在过门后续行，无需运行 Python 路段控制器。NPC／玩家／副本标记、下拉菜单和其他控件不作为目的地；地图投影的像素会量化最终坐标。这里扩展的是 N 键城镇地图，副本右上角小地图保持原行为。

数据操作优先使用客户端内控制器的单目标接口：

```powershell
python tools/debug/client_native_route.py --directory <SnapshotDirectory> --pid <进程号> --uid <本角色UID> --town 2 --area 2 --x 1200 --y 280
```

Python 可调用 `client_native_route.execute(directory, pid=pid, uid=uid, started=started, town_id=2, area_id=2, x=1200, y=280)`。脚本仅写一次最终目标请求并观察结果；受理后不逐段下发或重发。独立请求 `dfdebug-route-<pid>.bin` 使用 `DLRT/v1`，布局同上96字节，身份字段绑定起点而 town/area/x/y 为终点。状态为 `dfl-client-route/v1`，最新状态在 `dfdebug-route-<pid>.json`，debug请求另保留按 request ID 的结果文件，含 source、goal、origin、steps、PVF哈希及实时观察。

控制器在普通城镇、赛丽亚房间和酒馆的已解析地面/门之间规划，拒绝副本/未支持运输中转及门边危险终点。方向键、原生目标改变、新有效目标和已确认停止会接管路线；无效替换请求不打断旧有效路线。途中短暂缺数据只等待，连续五秒或整条120秒等预算到期终止续行。`arrived` 必须观察最终区域和坐标，Python另取新鲜SDK停稳证明；仅收到 `requested` 不代表到达。CLI超时和数据中断不会强制取消已经交给原版客户端的当前移动段。

消费端标准库测试：

```powershell
python -m unittest discover -s tools/debug -p 'test*.py' -v
```

## 构建和验证

### 仅测试环境的 CLI 控制

客户端v2支持本机／队友及城镇／副本`mp/max_mp`，附`mp_source={actor_token,local}`。城镇MP来自经绑定的`user+110`统计对象，展示actor仍用于身份／位置；Town HP／死亡保持未知。队友值是接收端原生观测；旧v2没有source时仅兼容原本地副本MP，v1仍未知。坏镜像、负数、非有限、溢出或身份变化为null；当前MP不压到上限。最大值按原getter先float32基础值、再乘截断，全部x87边界仍未证明。CLI摘要含队友MP与来源，字段查询／等待沿用现有接口。见[四端数据和范围](../../knowledge/decisions/2026-10-30-u05-mp-input-dialog-verification.md)。

新增`ui.command_input`固定scope为`native_command_checker_type0`，携带本机actor／UID／checker及`keyboard_lock_level/keyboard_suppressed`。锁级非零是原键盘命令早期阻断；零不承诺角色一定能移动，`ui.input_blocked`仍null。五类`ui.dialogs.items[i].placement`提供层附着、正向order及层原始标志；坏链／超限／变化时placement为null，detached时order为null。它们不表示像素可见或全局输入允许。

```powershell
python -m tools.debug.client_state --directory <SnapshotDirectory> --pid <PID> --process-started-filetime <FILETIME> --uid <UID> --wait 'ui.command_input.keyboard_suppressed=false' --timeout-seconds 5
```

原IJL文本输入JSON现支持`\r`、`\n`转义，送到既有Return/WM_CHAR路径。单次聊天激活用`{"t":0,"op":"text","chars":"\r"}`；`\r\n`是两个Return槽。其他未支持转义、原始控制字节及超限仍拒绝。

默认关闭。隔离服务端同时开启 `DFLegacy.Debug.ClientOverlay.Enabled=true` 和
`DFLegacy.Debug.TestControl.Enabled=true`，客户端对应的 `Config.ini` 开启：

```ini
[DebugOverlay]
ExportState=1
EnableTestControl=1
SnapshotDirectory=G:\path\to\isolated-test\snapshots
```

服务端的 `ClientOverlay.SnapshotDirectory` 必须指向同一目录。CLI不会替用户打开开关，
也不需要截图、操作系统输入或任意地址读写。租约由开启测试控制的服务端产生，绑定本次进程、
run和UID；旧进程、旧房间、旧演员或过期请求会拒绝。

在仓库根目录运行（`--uid` 是当前会话的区用户ID，不是账号序号）：

```powershell
python -m tools.debug.test_control --directory G:\test\snapshots state --uid 1
python -m tools.debug.test_control --directory G:\test\snapshots player teleport --uid 1 --x 300 --y 240
python -m tools.debug.test_control --directory G:\test\snapshots player kill --uid 2
python -m tools.debug.test_control --directory G:\test\snapshots monster kill --uid 1 --monster 0
python -m tools.debug.test_control --directory G:\test\snapshots drop isolate --uid 1 --ground 5 --park-x 800 --park-y 240
python -m tools.debug.test_control --directory G:\test\snapshots inventory drop --uid 1 --slot 22 --item 24002 --count 1
python -m tools.debug.test_control --directory G:\test\snapshots inventory drop --uid 1 --slot 0 --item 0 --count 23
python -m tools.debug.test_control --directory G:\test\snapshots loot result --uid 1 --ground 5 --run 123 --after-event 10
```

- `state` 分别返回服务端已提交背包/金币/负重、实例/掉落与原生位置/HP/敌人和地面状态。
- `player teleport` 只在当前副本房间定位本机角色，并核对原有同房队员看到的位置；不换地图或跨区。
  定位后调用经签名和本机绑定验证的原版`4C8FE0(controller,true,1)`，请求完整`0x10`发布并更新原生压缩基线。`native_position_publication_requested`只表示调用已执行；CLI仍须观察所有同房客户端才返回`synchronized`。发布前校验失败不修改位置，部分写入或发布失败保留部分结果；地面物定位不使用此角色发布路径。
- `player kill` 初版用于2～4人组队副本中的本机角色，所有当前队员客户端须提供新鲜状态；单人、目标已死或缺少服务端成员生死数据会在发送前拒绝。调用原玩家HP死亡入口，操作不自动复活。发布前核对完整成员身份、目标存活和各端上下文；只有服务端alive=false、自身和各端新采样看到同一角色HP0/dead且SDK代次未变才返回`synchronized`。`partial`只继续只读确认，不重发。它与`monster kill`分开，不能用怪物ID去杀玩家。
- `monster kill` 使用原生HP死亡入口，保留原生权限、复活等分支，随后观察服务端死亡与各端状态。
  UID从0开始；需要指定拥有怪物原生控制权的客户端。收到死亡请求不代表死亡已完成。
- `drop isolate` 把现存ground放到指定角色脚下，其他同房掉落放到停车点（至少相距100像素）；
  保留物品和掉落身份。服务端布局先原子验证，再协调所有客户端；部分执行必须返回`partial_sync`。
  客户端独立地面快照核对后才返回`synchronized`，不能只依靠setter回执。
- `loot result` 查询真实业务路径的最终`committed/rejected`，而非“骰子选中了谁”的中间日志。
  `committed`必须具有`persisted=true`；装备同时给出得主、目标槽位与实例ID。金币的ledger提交
  没有装备槽位。`--after-event`排除同一掉落早先的拒绝；明确`--run`可在实例销毁后查询本进程
  最近512条结果，重启进程后不能把旧结果冒充本次提交。

退出码：0=观察/同步成功或已提交；2=拒绝、部分同步；1=输入、身份、读取或超时错误。

`inventory drop` 调用原版ACT1 CMD50背包丢弃入口。仅主背包9～511槽，金币为0槽、item=0；数量必须为正，测试CLI金币单次上限1000。执行前核对同一物品、数量、对象和房间，拒绝待处理库存、交易占用或陈旧绑定。`synchronized`要求源库存减少、新地面和所有当前同房客户端独立出现，装备另核对实例ID、状态、耐久、封印、品质；拾取仍需另查`loot result`。不直接改存档或生成物品。

测试快照新增独立`inventory`的valid、reason、容器／数组绑定、容量、pending及占用槽位列表；行含slot、item_token、item_id、count。它与`ground.valid`分别校验，不能相互替代。当前只支持存活副本角色，死亡测试地面／库存观测仍待补。服务端库存与地面增加state、durability、seal_state、equipment_quality_seed；地面另含owner_uid、routing_source，缺失元数据以null表示。

原生`partial`保留回执并继续只读核对，不误称安全拒绝或重发；丢弃动画坐标与身份确认分开报告。见[四端主动丢弃及5张原图](../../knowledge/decisions/2026-10-30-active-drop-four-verification.md)。

已执行后的错误保留已知回执及部分同步信息。原生结果明确`server_committed=false`、
`persistence_verified=false`，不能代替服务端结果。测试控制准备场景不能冒充正常移动或战斗验收。
Windows短暂文件访问拒绝和快照替换缺口采用有限重试；不会重新生成修改请求或拼接不同轮次的队员集合。
实机边界、四端掉落矩阵及原图见[验收记录](../../knowledge/decisions/2026-10-29-test-control-four-client-loot-verification.md)。

聚焦验证：`python -m pytest tests/test_debug_test_control.py -q`、
`dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --test-control`、
`debug_tests.exe --client-test-control`。完整Python套件另需按验收工具约定将`tools`加入`PYTHONPATH`。

使用 VS MSBuild，`Configuration=Release`、`Platform=Win32`：

- `src/DFLegacy.Debug/DFLegacy.Debug.vcxproj` → `artifacts/native/debug/Release/debug.dll`
- `src/DFLegacy.Ijl15/DFLegacy.Ijl15.vcxproj` → `artifacts/native/ijl15/Release/ijl15.dll`
- `src/DFLegacy.Debug/tests/DFLegacy.Debug.Tests.vcxproj` → 独立模块测试程序。

将两个 DLL 一同复制至客户端目录，也更新启动器旁的副本；已有 `Config.ini` 应只修改对应开关。更新 DLL 后重启客户端，服务端新增功能也需要重启。启动器可以保留既有流程，调试 DLL 可选，关闭开关时不会加载。

`dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --debug-overlay` 验证预生成不改变房间访问状态、进房精确复用缓存、结束后不泄漏旧表。设置环境变量 `DFLEGACY_DEBUG_TEST_FIXTURE` 为输出文件绝对路径可导出真实 C# 编码样本；将该文件作为 `debug_tests.exe` 的唯一参数，验证 C++ 解码、错误/过期数据隔离、真实 DLL 中文绘制回调以及安全停线程再卸载。

自动化验证包含中文内容、窗口归属、不抢焦点样式、标题/缩放命中区、圆点收起与恢复、尺寸保持、隐藏、卸载销毁。自动化不能代替游戏内视觉验收；进图后实际显示由玩家确认。测试传入第三个参数 `--preview` 可打开五分钟的模拟数据窗口。

测试传入 `--interactive-cursor` 会等待测试预览窗口获得前台，再验证“游戏隐藏系统指针 → 指针位于面板时可见 → 隐藏面板后精确恢复显示计数”，并覆盖绘制消息积压时退出。该项需要可交互桌面；普通无前台运行会明确跳过真实指针检查，继续其余窗口/协议检查。卸载失败时保留 DLL 执行代码并输出线程栈/等待链，不在工作线程仍存活时强制卸载。

异常诊断独立于自动输入：客户端目录 `debug-overlay-<pid>.log` 记录加载基址、进图绘制与异常位置。发生渲染异常会停用该次覆盖层；场景和UID只读校验保护整数，不调用客户端断言修复。文字缓冲区在整个原生绘制批次内保持有效。

原生循环分段与运动拒绝观察默认关闭：

- `[DebugOverlay] ExportNativeLoopProfile=1`启用原ACT1 `433B00`至tail返回的七段计时，以及相邻调用之间的耗时。`dfdebug-native-loop-<pid>.json`由worker约200ms导出；主线程旧采样与新worker心跳分开，未完成、嵌套、漏序号和缺CPU数据保留为未知。CPU是量化的线程user+kernel时间，不能当GPU计时。scene是最近一次debug绘制观察值，前台只检查端点。
- `EnableDebugOverlay=1`及`ExportGameplayTrace=1`同时启用时，IJL固定内存环保留原`4C8EBC`的实际X/Y逐步候选、原v94拒绝返回及state／bounds／两次grid出口。只收集双绑定、已入图的怪物`529`和原函数`4B3C30`；Z的v90不在此流。漏marker是`unknown_exit`，不从缺失记录推断bounds。版本2不改变Host／Frame ABI v2。
- 同一开关还观察实际GP42 provider调用在`78EE76`的结果及返回时source/output快照。source尚不能当作调用前未修改的输入；output位于后续copy、二次变换和原生CRC／dispatch之前。成功与CRC有效都不能单独证明运动完成。每侧最多2048字节，截断和未知分别标记。

两个内存环使用已加载PE的DATA导出，CLI只申请查询／读取权限，绑定PID、启动时间和EXE路径；不调用远程函数或写进程内存。事件revision双读保护一致性；丢锁、overflow／invalid计数独立更新，不代表与事件同一瞬间。`--after-serial`只筛选新事件，旧事件年龄不作为当前状态证明。

```powershell
python -m tools.debug.native_loop_profile --directory 'G:\task\snapshots' --pid 1234
python -m tools.debug.motion_test_trace --pid 1234 --exe 'G:\task\DNF.exe' --after-serial 100
python -m tools.debug.provider_trace --pid 1234 --exe 'G:\task\DNF.exe' --after-serial 100
```

离线重算必须使用实际候选：`grid_cells(x,y)`按`trunc0(x/16)`、`trunc0(y/12)`与`trunc0(wrap_i32(y+1)/12)`转换。原EXE两段机器码覆盖负数、换行和溢出回绕；当前／目标向量代替实际候选时只能作为假设。安装表由`DflMotionProbeSites`实际导出供控制流审计；测试执行原函数的同一行和跨行路径，不能只验证每个独立trampoline。
