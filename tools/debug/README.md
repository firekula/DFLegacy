# 副本调试覆盖层

`ijl15.dll` 在首个渲染帧加载客户端目录的独立 `debug.dll`，通过已有游戏原生文字绘制接口叠加面板。DLL 加载与工作线程初始化均在 `DllMain` 之外。窗口、游戏分辨率和玩法输入不受面板接管。

进入副本约半秒内显示本次 BOSS 房的实际箱怪表：有/无、数量、容器脚本索引与地图坐标、怪物编号/等级、待进房/未释放/已释放/已死亡、通关状态。包含箱子和地堡使用的 `IsBoxMonster` 条目。F9 收放详情，F10 隐藏/显示；下一次进入副本自动显示，城镇不画面板。

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

自动化回调验证不能代替游戏内视觉验收；进图后实际显示由玩家确认。

异常诊断独立于自动输入：客户端目录 `debug-overlay-<pid>.log` 记录加载基址、进图绘制与异常位置。发生渲染异常会停用该次覆盖层；场景和UID只读校验保护整数，不调用客户端断言修复。文字缓冲区在整个原生绘制批次内保持有效。
