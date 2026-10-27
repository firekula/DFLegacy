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
