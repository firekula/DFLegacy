# 副本调试覆盖层

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

路径只遍历数据，可使用 `room.map_token`、`me.hp`、`players.0.dead` 等，不执行表达式。未知null永不匹配，连 `=null` 也不会完成；布尔false不等于数字0。完整性不足、未证MP/职业/冷却/可用性、未绑定实体/地图的位置以及独立 `server_context` 均不能当作已知动作条件。技能等级不等于技能已可用，authority token不推断为owner。v1 的有效Town保留 `me`、`room` 为null；v2 读取 Town 原生角色绑定，HP、MP和死亡状态仍保持null，不伪造HP0或死亡。

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

每段从新鲜实际位置重新规划，绑定 PID/创建时间/UID/user/actor/context，只接纳预期区域、递增代次及新地图。发送前等待年龄不超过200ms的采样，为原生750ms校验留下排队预算；短暂无效或过期采样最多连续等待五秒，等待期间不下发移动。只有原生明确 `rejected/expired_request`、该请求从未受理、角色仍停在原处且身份/目的地/自动移动状态未变，才会重新取样、规划并以新ID更新请求，每个入口或最终目标最多三次，旧ID记录在 `expired_request_renewals`。原生时限保持不变；受理过的请求、仅超时而没有明确拒绝、其他错误及人工接管都不自动重发。

观察到人工方向输入、目的地覆盖、其他控制器接管、身份/地图异常和超时会中断。中断或超时不会强制取消已受理的原生移动；本轮支持范围为当前 ACT1 普通城镇地面，动态障碍仍可能导致有界失败。

请求wire为96字节、小端，Python `struct` 格式 `<4sHHIIQQQQQQIIIIiiii`：`DLNV`、u16版本1、u16长度96、u32 PID/UID、u64进程FILETIME/requestId/issuedTick/snapshotTick/generation/sequence、u32 actor/map/user/context token、i32 town/area/x/y。请求时效1000ms，引用采样时效750ms，未来时间与重放被拒绝。结果schema为 `dfl-client-navigation/v1`，`source` 区分 debug/minimap，`status` 为 requested/arrived/rejected/interrupted；结果携带身份、目标、位置、时钟与原因，不把写文件成功当作受理。

消费端标准库测试：

```powershell
python -m unittest discover -s tools/debug -p 'test*.py' -v
```

## 构建和验证

使用 VS MSBuild，`Configuration=Release`、`Platform=Win32`：

- `src/DFLegacy.Debug/DFLegacy.Debug.vcxproj` → `artifacts/native/debug/Release/debug.dll`
- `src/DFLegacy.Ijl15/DFLegacy.Ijl15.vcxproj` → `artifacts/native/ijl15/Release/ijl15.dll`
- `src/DFLegacy.Debug/tests/DFLegacy.Debug.Tests.vcxproj` → 独立模块测试程序。

将两个 DLL 一同复制至客户端目录，也更新启动器旁的副本；已有 `Config.ini` 应只修改对应开关。更新 DLL 后重启客户端，服务端新增功能也需要重启。启动器可以保留既有流程，调试 DLL 可选，关闭开关时不会加载。

`dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --debug-overlay` 验证预生成不改变房间访问状态、进房精确复用缓存、结束后不泄漏旧表。设置环境变量 `DFLEGACY_DEBUG_TEST_FIXTURE` 为输出文件绝对路径可导出真实 C# 编码样本；将该文件作为 `debug_tests.exe` 的唯一参数，验证 C++ 解码、错误/过期数据隔离、真实 DLL 中文绘制回调以及安全停线程再卸载。

自动化验证包含中文内容、窗口归属、不抢焦点样式、标题/缩放命中区、圆点收起与恢复、尺寸保持、隐藏、卸载销毁。自动化不能代替游戏内视觉验收；进图后实际显示由玩家确认。测试传入第三个参数 `--preview` 可打开五分钟的模拟数据窗口。

测试传入 `--interactive-cursor` 会等待测试预览窗口获得前台，再验证“游戏隐藏系统指针 → 指针位于面板时可见 → 隐藏面板后精确恢复显示计数”，并覆盖绘制消息积压时退出。该项需要可交互桌面；普通无前台运行会明确跳过真实指针检查，继续其余窗口/协议检查。卸载失败时保留 DLL 执行代码并输出线程栈/等待链，不在工作线程仍存活时强制卸载。

异常诊断独立于自动输入：客户端目录 `debug-overlay-<pid>.log` 记录加载基址、进图绘制与异常位置。发生渲染异常会停用该次覆盖层；场景和UID只读校验保护整数，不调用客户端断言修复。文字缓冲区在整个原生绘制批次内保持有效。
