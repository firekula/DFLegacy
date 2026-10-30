# ACT1 客户端地图右键跨区域寻路验收

已把跨区路线控制接入 debug.dll。按 **N** 打开城镇地图，**右键目标区域的空白位置**，客户端自动规划、移动、过门并继续到最终点；操作不需要另外运行 Python 路段控制器。普通方向键和新的有效目标可以接管。

本轮验收 DLL SHA256：`5E5FF3F218E738C9C4AAEFACBB496B800A44BD5D44BA88E45BB00AFFB401836B`。代码提交 `f6a9eb93cfff8678d3b99ad0fe7a5a92871b551f`，构建及源码见[冻结清单](assets/2026-10-29-native-town-route/frozen-v2-source.json)。服务端继续处理原有城镇过门，路径规划在客户端 DLL 内完成。

## 使用方式与画面

保留原配置 `ExportState=1`、`EnableTownNavigation=1`，加载新版 DLL 并重启客户端。N 地图可以直接选择其他区域；NPC、玩家、副本图标、下拉菜单和按钮保留原操作。地图像素会量化最终坐标。这次扩展的是 N 键城镇地图。

![N 键城镇地图右键选择目标](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-native-town-route/Town-map.jpg)

A 端去程终点：

![A端到达赫顿玛尔](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-native-town-route/A-hendon.jpg)

A 端回程终点：

![A端回到赛丽亚房间](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-native-town-route/A-room.jpg)

B 端去程及回程终点：

![B端到达赫顿玛尔](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-native-town-route/B-hendon.jpg)

![B端回到赛丽亚房间](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-native-town-route/B-room.jpg)

图片均为客户端原生 PrintScreen JPG 的逐字节副本。画面左侧的保存提示来自原生截图操作，未裁剪或重绘；[清单](assets/2026-10-29-native-town-route/manifest.json)记录来源及 SHA256。

## 双客户端完整路线

A 为 mageco／UID1，B 为召唤师／UID2。每个下列用例仅有一次原生 IJL 右键输入；DLL 在内部调用原版目的地函数完成14段移动。去程观察到 `1/1→1/0→2/1→2/0→2/2`，回程相反，均跨4个传送门。观察脚本只读取状态。[运行中身份记录](assets/2026-10-29-native-town-route/running-v2-deployment.json)、[实际模块哈希](assets/2026-10-29-native-town-route/loaded-v2-modules.json)绑定精确 PID、路径和进程创建时间。

| 用例 | 验收总耗时 | 原生目的地调用数 | 终点 |
|---|---:|---:|---|
| A 去程 | 30.672 秒 | 14 | `2/2 (1734,263)` |
| A 回程 | 30.625 秒 | 14 | `1/1 (575,297)` |
| B 去程 | 32.454 秒 | 14 | `2/2 (1734,263)` |
| B 回程 | 30.422 秒 | 14 | `1/1 (575,297)` |

耗时包含发起输入、观察和末端采样。四轮均有 `source=minimap/status=arrived`、完整区域观察、原始状态更新，以及严格晚于原生终态的新鲜 SDK 坐标和停止方向4/4；每轴误差不超过3。完整记录：[A去程](assets/2026-10-29-native-town-route/cases/A-full-forward.json)、[A回程](assets/2026-10-29-native-town-route/cases/A-full-reverse.json)、[B去程](assets/2026-10-29-native-town-route/cases/B-full-forward.json)、[B回程](assets/2026-10-29-native-town-route/cases/B-full-reverse.json)。两端同区域右键也分别以1次原生目的地调用到达：[A](assets/2026-10-29-native-town-route/cases/A-same-area.json)、[B](assets/2026-10-29-native-town-route/cases/B-same-area.json)。

## 接管与误触保护

A 端先向赛丽亚房间自动移动。右键 NPC 后，原路线 ID 和目标保持，角色继续前进；关闭 N 地图后，重新取得的原生状态仍为 moving。再按左方向键220ms，路线中断；其后2250ms坐标停稳、auto关闭、方向4/4，路线结果和步数不再变化。[输入和独立状态证明](assets/2026-10-29-native-town-route/cases/A-native-manual-interruption.json)。

![方向键接管后停止](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-native-town-route/A-manual-stop.jpg)

新的有效右键目标将运行中的目标7替换为目标8，到达后再观察2100ms，旧路线没有恢复。[目标替换](assets/2026-10-29-native-town-route/cases/A-valid-target-takeover.json)。

控件右键、投影区域外、两种已展开下拉菜单以及隐藏地图的缓存平面均没有创建新的地图路线；重新打开地图也没有重放旧目标。两个菜单的展开状态来自原生控件读取，原始记录和输入都在清单中。隐藏地图用例只验证地图路线没有误触，保留原版地面操作行为。

## 单目标 debug 接口

```powershell
python tools/debug/client_native_route.py --directory <SnapshotDirectory> --pid <PID> --uid <UID> --town 2 --area 2 --x 1200 --y 280
```

脚本写一次独立 `DLRT/v1` 最终目标请求，由 DLL 完成所有内部路段。B 端在本轮同一5E5 DLL上另完成实机跨区：26.843秒、14段，到达 `(1200.171,280.265)`，SDK方向4/4。[实测记录](assets/2026-10-29-native-town-route/cases/B-v2-native-api-cross.json)，附原始96字节请求及按ID终态。API说明和 Python 调用方式见[debug README](../../tools/debug/README.md)。

## 实现与测试

实际客户端 PVF提供区域、地面与入口；只读解析及文件IO在工作线程。游戏线程使用不可变目录、有界几何规划、新鲜角色状态和原版 point/set/activate 函数。身份绑定及 floor 核对保护原生调用；仅接受预期切区、递增代次和新地图。人工停止、目标改变、身份异常、持续缺数据或超时终止续行。离开出生门、避开其他入口、完整逐轴移动范围和停靠余量都有回归。

首轮实机发现79D242早于右键命中 ID 发布，保留了[原生事件时序 RED](assets/2026-10-29-native-town-route/diagnostics/first-right-edge-trace.json)。修正入口到8F6FAF：原生 hit 函数先执行，核验本次 release 已消费及真实地图 root，再读取目标，保留原返回值；[时序说明](assets/2026-10-29-native-town-route/review/right-hit-order-correction.md)和[独立审查](assets/2026-10-29-native-town-route/review/hit-review.md)记录原PE字节、ABI、回滚及卸载保护。

- Native 目录与 Python 实际PVF对照：3037检查通过；集成导航、控制器、原生调用适配和结果IO测试通过。
- 完整 Native DLL 测试通过。异步导出原先固定短等待的断言改为两秒内连续驱动帧、观察真实新结果；[原失败](assets/2026-10-29-native-town-route/diagnostics/native-short-wait-red.log)、[对照](assets/2026-10-29-native-town-route/diagnostics/native-baseline-control.log)、[最终日志](assets/2026-10-29-native-town-route/tests/async-export-full-native-green.log)保留。
- Python debug：**165项、173个子用例通过**。[日志](assets/2026-10-29-native-town-route/tests/python-debug-final.log)。迟发终态仍保留请求/身份/时钟绑定，到达须另取新鲜SDK证明；不重发已受理命令。[审查](assets/2026-10-29-native-town-route/review/consumer-review.md)。
- 全仓 Python：**463项、221个子用例通过，6项已知基线失败**。[日志](assets/2026-10-29-native-town-route/tests/full-repository-python.log)。仍为3项旧台账12行断言与3项固定2311端口的Windows10013错误；此前[基线复现记录](2026-10-29-debug-cross-area-verification.md)仍适用。

第二轮验收脚本最初过早检查登录后的首份采样、未确认地图已打开，以及一次结果读取被短暂占用，均保留诊断记录；最终完整四轮使用有界读取重试，独立观察终态，未重发移动请求。

独立归档审查完成2375项断言，0失败，未发现P1/P2；核验了原始右键输入、路线身份与时钟、六张原图、API请求及恢复哈希。[最终审查](assets/2026-10-29-native-town-route/review/acceptance-review.md)。

## 覆盖与恢复

实机覆盖两城镇五区域、两端同区和跨区往返、人工接管、目标替换及上述UI保护；目录解析覆盖当前PVF的29区域68入口。不外推到全部城镇、任务运输、动态障碍或副本右上角小地图。

本轮拥有的客户端及独立服务端已停止归档。两份正常配置、八份输入文件已恢复；15项受保护输入、正式运行目录50个文件、四份原生DLL和四份新版debug DLL均核对。旧W冻结的146份证据仍逐字节匹配。[恢复记录](assets/2026-10-29-native-town-route/closeout-restoration.json)。新版debug DLL已留在两端客户端及启动器旁，重启即可使用。

U06的客户端地图右键扩展在上述范围完成。U03四客户端掉落矩阵、U04主动丢弃实机验收、U05技能MP/冷却及通用UI扩展、U07位置同步继续保留在[后续列表](2026-10-28-restoration-backlog.md)。
