# ACT1 城镇目的地与城镇地图移动

用户已授权扩充 debug.dll 状态与操作接口，并持续完成实现、测试和带原图的双客户端验收。沿用现有隔离工作树；原客户端 EXE、PVF、原始存档不改。

## 交付行为

城镇地图（当前客户端快捷键 N）当前区域空白处点击可设置原生城镇目的地；NPC、标记、关闭、筛选、其他面板维持原流程。debug 提供同区 `set_destination`，和地图点击调用同一目的地处理器。成功受理仅表示原生目标已设置，必须继续观察坐标到达或中断。跨区、未知布局、阻挡点、运输模式或身份变化明确拒绝。

原客户端 `TownActor.v+178=478F70` 是普通目的地 setter，已证明写目标与方向；尚无 A*、跨区路线保证。复用该入口，不能把 Donkey/CMD58 当作普通导航。缺失 map/area 数据时原生点检查会返回 true，桥接必须先验证完整上下文。

## 结构

- `client_state` 升为 `dfl-client-state/v2`，保留既有 Dungeon 字段语义；Town 的 me/players 使用 user+10C 绑定，生命与死亡未知值保持 null。新增 town 当前区、坐标、原生目标及 UI 状态。UI 包含原生倒计时状态、选图、玩家菜单和 城镇地图 可见状态；未知不猜测。
- 状态采样完全只读，固定容量、游戏线程有界读取，后台 JSON IO。旧 v1 Python 输入继续接受。
- 独立 `client_navigation` 模块处理请求、M hook 与原生调用；不把写操作混入状态 sampler。请求绑定 PID、精确进程开始时间、UID、actor/map、Town/Area、捕获 generation 和新鲜时间；拒绝重复、过期、跨身份请求。
- worker 从每进程命令文件读入有界请求，游戏线程重新验证后调用 native；worker 写回结果。不得在游戏线程等待磁盘或锁。模块设置默认关闭。
- 城镇地图 仅替换唯一 CALL 79D242，先运行原 880E20；要求 release edge、实际根控件 capture ID、可见启用、当前区域唯一投影矩形、排除全部已显示标记。动态读取原生 hash 缓存中的 real/project Rect，不固定 PVF 表。新逆投影定义整数舍入并验证边界。

## 依据与验收

原 PE SHA256 6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5；只读提取见私有 recon/u06-ground-path、u06-town-map-v2、u06-current-rect-lookup。原始冻结 SDK b7773d7e/B53341 保留。

先做读取/身份/投影/回放/阻挡/重叠控件 RED→GREEN 测试，再构建 /W4 /WX 与既有完整测试。实际两端验证同区目的地受理与到达、M 空白点击、NPC/关闭/筛选不误移动、跨区拒绝、切区后旧请求失效。验收保留少量原生截图与结构化时间线；未完成的范围如实记录。
