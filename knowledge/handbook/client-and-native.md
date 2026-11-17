# 客户端与原生层交接

> 核对日期：2026-11-17。本文解释仓库内代码和已保存的证据，不证明当前线上产物或任一正在运行的客户端来自这些代码。总览见 [current-state.md](current-state.md)，桌面与更新见 [desktop-apps.md](desktop-apps.md)，构建发布见 [build-test-release.md](build-test-release.md)。

## 1. 先判断正在处理哪个客户端

**ACT1 与第二章共享部分算法，但不共享游戏地址、结构布局或启动协议。** 不要把 ACT1 的固定地址补丁复制到第二章，也不要用新 DLL 覆盖原版 `ijl15.dll` 来试错。跨章移植的系统检查见 [章别适配](chapter-adaptation.md)。

| 范围 | 当前代码路径 | 要保留的边界 |
| --- | --- | --- |
| ACT1 独立补丁 | `DFLegacy.Bootstrap32.exe` 创建游戏，加载 `DFLegacy.Client.dll` | 游戏为 x86；保留原版 JPEG 库；通过私有握手确定普通/测试模式 |
| ACT1 旧 JPEG 兼容入口 | `DFLegacy.Ijl15` 生成 `ijl15.dll` | 保留兼容和测试用途；不是当前独立补丁的默认安装方式 |
| 第二章 | 托管启动器 → 内嵌 PowerShell → x64 broker → x86 兼容宿主 → 已验收重建镜像 | 当前要求普通桌面进程组件；原游戏镜像、资源与宿主都有独立校验 |
| 公共调试代码 | `DFLegacy.Debug` | 目录名不等于运行所有权：导航和状态所有者已经编入核心补丁；普通模式不加载 `debug.dll` |
| 原生启动入口 | `DFLegacy.Startup` 生成 `DFLegacy.Launcher.exe` | 它启动/修复登录器，不是游戏注入器 `DFLegacy.Bootstrap32.exe` |
| 随机数桥 | `DFLegacy.RandomNative` | 给服务端提供 Windows x64 RDSEED；不是客户端游戏补丁 |

第二章是否内嵌由 [ChapterTwoClient.IsBundled](../../src/DFLegacy.Launcher/ChapterTwoClient.cs) 检查程序集资源决定；构建分支是 [Launcher.csproj](../../src/DFLegacy.Launcher/DFLegacy.Launcher.csproj) 的 `ChapterTwoPayloadDir`。不能仅凭可执行文件名或版本前缀判断章节。

### 最小阅读包

| 要修改什么 | 先读这些，确认后再扩展 |
| --- | --- |
| ACT1 启动/注入失败 | [LaunchFlow.cs](../../src/DFLegacy.Launcher/Services/LaunchFlow.cs)、[ClientBootstrapLauncher.cs](../../src/DFLegacy.Launcher/ClientBootstrapLauncher.cs)、[bootstrap-protocol.md](../../src/DFLegacy.Client/bootstrap-protocol.md) |
| 第二章启动/资源拒绝 | [ChapterTwoClient.cs](../../src/DFLegacy.Launcher/ChapterTwoClient.cs)、[Start-Client.ps1](../../src/DFLegacy.Launcher/ChapterTwo/Start-Client.ps1)、[Build-StartupBroker.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-StartupBroker.ps1) |
| ACT1 游戏行为 | [dnf_patches.cpp](../../src/DFLegacy.Ijl15/src/dnf_patches.cpp)、对应功能 `.h/.cpp` 与同名测试 |
| 第二章游戏行为 | [ChapterTwoPresentation.cpp](../../src/DFLegacy.ChapterTwo.Native/ChapterTwoPresentation.cpp)、对应 `ChapterTwo*.h/.cpp`、[CMakeLists.txt](../../src/DFLegacy.ChapterTwo.Native/CMakeLists.txt) |
| 城镇导航/状态采样 | [player-runtime.md](../../src/DFLegacy.Client/player-runtime.md)、[player_runtime.cpp](../../src/DFLegacy.Client/player_runtime.cpp)、`DFLegacy.Debug/src/client_*` 和 `town_route_*` |
| 显示/鼠标坐标 | 下文第 4、5 节；设置入口在 [ClientDisplayConfiguration.cs](../../src/DFLegacy.Launcher/ClientDisplayConfiguration.cs) |
| 崩溃、异常退出 | 下文第 7 节及 [desktop-apps.md](desktop-apps.md) 诊断部分 |

## 2. ACT1 独立补丁的完整启动链

入口：[LaunchFlow.RunAsync](../../src/DFLegacy.Launcher/Services/LaunchFlow.cs)。它校验账号/配置和客户端路径，取得游戏共享租约，再选择 ACT1 或第二章分支。ACT1 分支要求启动器与客户端同目录，取得安装租约，校验包清单、镜像布局、`Script.pvf` 和 D3D9 设备。

1. [ClientPackageManifest](../../src/DFLegacy.Launcher/ClientPackageManifest.cs) 校验当前包；[ClientBootstrapInspector](../../src/DFLegacy.Launcher/ClientBootstrapInspector.cs) 检查可支持镜像。
2. [ClientIjlMigration](../../src/DFLegacy.Launcher/ClientIjlMigration.cs) 判断 JPEG 库是否原版。只有找到可信备份且用户确认时才恢复；未知文件不会直接覆盖。旧 `--install-ijl15` 入口已明确拒绝覆盖安装。
3. [StandaloneClientInstaller](../../src/DFLegacy.Launcher/StandaloneClientInstaller.cs) 准备内嵌 helper 与配置。支付本机站点、可选 Relay 启动后构造 `99?host?port?...` 游戏参数。参数含账号密码，不能写入日志或交接文本。
4. [ClientProcessLauncher](../../src/DFLegacy.Launcher/ClientProcessLauncher.cs) / [ClientBootstrapLauncher](../../src/DFLegacy.Launcher/ClientBootstrapLauncher.cs) 调用 x86 `DFLegacy.Bootstrap32.exe`。
5. [bootstrap_main.cpp](../../src/DFLegacy.Client/bootstrap_main.cpp) 校验文件哈希/PE/身份，创建挂起的**新子进程**，用 Detours 在其内存导入表前加入固定名称 `DFLegacy.Client.dll`。不修改游戏 EXE 磁盘内容，不提供任意 PID 附加入口。
6. [client_entry.cpp](../../src/DFLegacy.Client/client_entry.cpp) / [bootstrap_runtime.cpp](../../src/DFLegacy.Client/bootstrap_runtime.cpp) 处理私有 payload，安装原始入口门闩。真正游戏补丁在 loader lock 之外、游戏 CRT 之前执行；失败不进入游戏。
7. helper 输出一行 `ready` JSON，托管端核验 nonce、PID、创建时间、模块路径和文件身份，再写精确的 `commit <nonce>\n`。**收到 ready 不代表成功，必须观察 commit 后 helper 退出码 0。** 此后才解除 kill-on-close 责任。
8. [ClientProcessLifetime](../../src/DFLegacy.Launcher/ClientProcessLifetime.cs) 持有本次创建的进程；游戏退出时回收 Relay/支付等资源。安装租约在 bootstrap 成功后释放，游戏共享租约覆盖整个游戏会话。

私有协议细节必须以 [bootstrap-protocol.md](../../src/DFLegacy.Client/bootstrap-protocol.md) 和 [bootstrap_protocol.h](../../src/DFLegacy.Client/bootstrap_protocol.h) 为准：Windows 10+ 的原子进程 Job 归属、有限继承句柄、同目录 DLL、10 秒初始化、15 秒 commit 等都是协议的一部分。不要用创建后再附加 Job、宽松接受多行 stdout 或直接 `LoadLibrary` 替代它。

### 普通模式与测试模式

独立补丁模式只来自握手里的 `Mode { Normal, Test }`，不是环境变量或文件名。普通模式拒绝加载 `debug.dll`，忽略旧自动输入/网络探针诊断设置；测试模式仍需各功能自己的显式开关与请求校验，不自动允许所有控制操作。

[player_runtime::OnFrame](../../src/DFLegacy.Client/player_runtime.cpp) 在 FPS/调试 UI/自动输入关闭时仍采样并推进导航，不能把它放回“只有 overlay 可见才运行”的分支。独立普通模式城镇导航默认开启，`[Features] EnableTownNavigation=0` 才关闭；旧 IJL 路径保留 `[DebugOverlay] EnableTownNavigation` 的 opt-in 语义。

## 3. `DFLegacy.Ijl15` 与 `DFLegacy.Debug` 模块索引

[Ijl15/CMakeLists.txt](../../src/DFLegacy.Ijl15/CMakeLists.txt) 同时组装 `ijl15.dll`、`DFLegacy.Client.dll`、`DFLegacy.Bootstrap32.exe`、`debug.dll`。`client_patch` 复用 IJL 的游戏功能源码，剔除 JPEG 实现和旧入口，再加入独立入口。目录分开不表示应该分开维护两套 ACT1 游戏补丁。

| 代码组 | 职责/修改入口 |
| --- | --- |
| [ijl15.cpp](../../src/DFLegacy.Ijl15/src/ijl15.cpp)、[ijl15.h](../../src/DFLegacy.Ijl15/include/ijl15.h)、[ijl15.def](../../src/DFLegacy.Ijl15/src/ijl15.def) | 使用 Windows WIC 实现 IJL 兼容接口，保留导出 ABI、像素布局与边界检查；独立补丁不导出 JPEG codec 接口 |
| [ijl15_entry.cpp](../../src/DFLegacy.Ijl15/src/ijl15_entry.cpp)、[dnf_patches.h](../../src/DFLegacy.Ijl15/src/dnf_patches.h) / `.cpp` | 旧 DllMain 目标识别与补丁汇总；开关、受支持指令和玩法入口集中在这里 |
| [memory_patch.cpp](../../src/DFLegacy.Ijl15/src/memory_patch.cpp)、[pe_import.h](../../src/DFLegacy.Ijl15/src/pe_import.h) | 内存写入、跳转和导入表工具；更改时检查页保护、原字节与回滚，不能降低镜像/签名拒绝规则 |
| `inventory_tab`、`warehouse_upgrade_button`、`creature_gift_row`、`countdown_coin`、`dungeon_tooltip` | 背包页保持、仓库升级、宠物礼物行、倒计时道具、地下城提示 |
| `channel_switch`、`channel_reentry`、`relay_send_queue` | 换频道、重入与组队发送队列；同时看服务端协议和对应测试，不能只改客户端时间常量 |
| `party_card_display`、`party_combat_detail`、`boss_room_death`、`ground_item_removal`、`loot_gather*` | 组队翻牌/战斗细节、Boss 房死亡、地面物品和归拢；画面表现与服务端持久化需分别验收 |
| `mage_skill_contact`、`priest_heal_target`、`stage_follow`、`summon_owner_death`、`motion_horizon` | 职业接触/治疗目标、房间跟随、召唤主人死亡和运动窗口 |
| `external_string_table`、`message_audio`、`mouse_wheel`、`output_paths` | 文字、消息声音、滚轮与原游戏输出路径 |
| `auto_input`、`net_dump`、`provider_probe`、`motion_test_probe`、`native_loop_probe` | 自动输入、抓包/路由/运动/原生循环诊断；先读 [AUTO_INPUT.md](../../src/DFLegacy.Ijl15/AUTO_INPUT.md)，不向日常客户端写测试控制文件 |
| `native_incidents`、`native_capture`、`diagnostic_delivery` | 异常证据、进程外现场捕获与诊断投递；不把普通退出或已处理异常直接等同崩溃 |

上述短名均位于 [Ijl15/src](../../src/DFLegacy.Ijl15/src)，大部分有 [Ijl15/tests](../../src/DFLegacy.Ijl15/tests) 同名测试。功能是否真的进入产物，应再核对 CMake 的 target 源文件列表和调用方。

`DFLegacy.Debug` 的三个层次：

- **核心所有者源码**：`client_state*`、`client_navigation*`、`town_route_catalog/controller`、`client_town_route_*`、`map_click_feedback`、`client_map_feedback_runtime`。源码住在 Debug 目录，但由 `player_runtime` 调度并编入 IJL/独立补丁；卸载调试 DLL 不能卸载这些运行时。
- **可选 DLL/UI**：[debug_overlay.cpp](../../src/DFLegacy.Debug/src/debug_overlay.cpp)、`debug_window`、`shared_state_bridge`。它消费核心快照。ABI 位于 [debug_api.h](../../src/DFLegacy.Debug/include/debug_api.h)，当前 `Version=3`，Host、SharedState、Snapshot/Reader 必须严格匹配大小/版本。旧 v2 DLL 不兼容。
- **诊断与有界控制**：`client_party*`、`client_test_control*`、`gameplay_trace`、`frame_profile`、`native_loop_profile`；纯解码与运行时文件分开。核心 Reader 的寿命必须长于 DLL，不能保留指向已卸载模块的回调。

## 4. ACT1 显示、坐标与输入

[presentation_scaling](../../src/DFLegacy.Ijl15/src/presentation_scaling.cpp) 决定显示策略；`d3d8_hooks` / `d3d8_presenter` / `d3d8_scaler` 负责 D3D8 接入与呈现；`window_scaling` / `window_input_scaling` 保证游戏内部坐标和外部窗口坐标配对。`performance_overlay` 同时是重要帧回调调用方，不能因为关掉 FPS 就绕过核心采样。

托管设置接受 `legacy`、`sharp`、`point`、`linear`。增强模式使用六个 4:3 预设：640×480、800×600、960×720、1024×768、1280×960、1600×1200，来自 [ClientDisplayConfiguration.Presets](../../src/DFLegacy.Launcher/ClientDisplayConfiguration.cs)。旧模式允许的尺寸范围与增强模式不同。新增尺寸时要一起检查窗口客户区、鼠标命中、内嵌支付窗口、设备重置和配置兼容。

不要把第一章与第二章的 post-process 列表混在一起；第二章才有其独立的 NTSC/CRT/xBRZ 参数接入。`xbrz_frame` 源码可复用不代表旧 ACT1 UI 已启用所有后处理选项。

## 5. 第二章：当前普通桌面进程链

[ChapterTwoClient.RunAsync](../../src/DFLegacy.Launcher/ChapterTwoClient.cs) 先验证原 `DNF.exe`、白名单客户端 PVF 与 `ImagePacks2`。发现 PVF 以 `DNF_SCRIPT_PACK` 开头会明确拒绝：这是服务端格式，不能交给第二章游戏读取。

接着它计算内嵌宿主、broker、脚本的组合哈希，将 runtime 放在客户端 `.dflegacy/chapter-two/<hash前缀>`，按哈希释放重建镜像/组件。可选 D3D11 bridge 也使用自己的哈希目录。`VerifyEmbeddedPayload` 检查镜像和架构：呈现宿主为 x86，QuietStartupBroker 为 x64。因此第二章启动不能仅按托管程序位数推断系统最低要求。

当前必须同时存在 `ChapterTwoPresentation.exe` 和 `QuietStartupBroker.exe` 内嵌资源；缺少时拒绝启动。名字中的 `Preview`/旧宿主资源是历史命名，**当前代码没有缺组件就自动恢复 AppContainer 旧链路的后备行为**。

[Start-Client.ps1](../../src/DFLegacy.Launcher/ChapterTwo/Start-Client.ps1) 接收 stdin JSON 中的凭据；它保留 Windows PowerShell 5.1 兼容性，通过 A/B 命名互斥量限制本机两个槽位，为每次尝试创建 `Logs/DFLegacy/Sessions/<A或B>-<时间>-<尝试号>`。`loginInfo.inf` 不存在时只 CreateNew 空文件，不能截断原文件或写入账号密码。

脚本清理继承的 `DFL_S1C2_*` 后设置本次参数，broker 输出普通进程标志和受控子进程事件。`DFL_S1C2_SESSION_DATA` 是当前会话目录接口；不要恢复 `DFL_S1C2_CONTAINER_DATA`。按 [普通进程决策记录](../decisions/2026-11-12-chapter-two-ordinary-process.md)，当前宿主保留镜像兼容加载和本机网络转接，移除 AppContainer SID/ACL/授权流程。直接运行原版 `DNF.exe` 不能代替兼容宿主。

### 第二章代码功能地图

下面名称位于 [ChapterTwo.Native](../../src/DFLegacy.ChapterTwo.Native)，以 [CMake 的 `chapter_two_presentation` 源文件列表](../../src/DFLegacy.ChapterTwo.Native/CMakeLists.txt) 和 [Build-PresentationHost.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-PresentationHost.ps1) 的接入位置为准。

| 代码组 | 职责 |
| --- | --- |
| `ChapterTwoPresentation`、`ChapterTwoNtscRenderer/Shaders`、`CompileChapterTwoNtscShaders` | 呈现接入、D3D8 包装、NTSC/CRT 后处理；xBRZ 复用已引入的第三方实现 |
| `ChapterTwoPerformance*`、`ChapterTwoFrameStages`、`ChapterTwoSlowFrameTiming` | 帧分段、采样历史、日志、性能面板及显示文本寿命 |
| `ChapterTwoPaymentProtocol/Navigation/Viewport`、`ChapterTwoGuildNavigation`、`ChapterTwoPanelCursor` | 原游戏浏览器中的支付/公会页面导航、大小和光标；托管端本机站点见桌面文档 |
| `ChapterTwoNpcDialogue`、`ChapterTwoDungeonTooltip`、`ChapterTwoExternalStringTable`、`ChapterTwoPopupAnnouncement` | NPC 字体/行距、地下城说明、外部字符串与公告 |
| `ChapterTwoInventoryTab`、`ChapterTwoCreatureTabs`、`ChapterTwoWarehouseUpgrade`、`ChapterTwoQuickStore` | 背包/宠物页、仓库按钮和快捷存取 |
| `ChapterTwoJarCapacity/Feedback`、`ChapterTwoCashItemUse`、`ChapterTwoMallCreature`、`ChapterTwoShopLayout`、`ChapterTwoStoreTitle`、`ChapterTwoCountdownCoin` | 罐子、现金道具、商城宠物/布局、个人商店标题、倒计时购买 |
| `ChapterTwoHudPortraitCache`、`ChapterTwoFreeCardDisplay`、`ChapterTwoParty`、`ChapterTwoLootGather*`、`ChapterTwoCemeteryGuard`、`ChapterTwoDungeonRating` | 头像资源寿命、免费牌、组队、地面物品归拢、墓地守卫与评分 |
| `ChapterTwoTownMap`、`ChapterTwoCursorAuto`、`ChapterTwoMouseWheel`、`ChapterTwoKeyboardTiming`、`ChapterTwoImeComposition` | 城镇地图/路线、系统光标自动定位、滚轮、键盘时序与中文输入 |
| `ChapterTwoBackgroundPolicy`、`ChapterTwoAdultBlood`、`ChapterTwoType16Audio` | 前后台节流策略、成人血液效果、16 类消息声音 |
| `ChapterTwoRelaySockets`、`ChapterTwoRelayPortMap`、`ChapterTwoRemoteEndpoint` | 只对已知游戏目标做 socket/端口映射，支持每实例 Relay |
| `ChapterTwoLocalPaths`、`ChapterTwoNativeLog`、`ChapterTwoDiagnosticEvents`、`ChapterTwoStartupMutex` | 原游戏本地路径、原生日志、broker 事件与启动互斥 |

模块存在不等于每种真实玩法已验收。需按问题查阅 [knowledge/decisions](../decisions) 中对应日期记录，分别记录“编译/原镜像夹具/真人或 UI 实测/持久化”。旧 [ChapterTwo.Native/README.md](../../src/DFLegacy.ChapterTwo.Native/README.md) 有早期“待迁移/实验宿主/AppContainer”描述，不能覆盖当前调用链。

### 第二章网络和渲染参数

[ChapterTwoConnection](../../src/DFLegacy.Launcher/ChapterTwoConnection.cs) 区分本机 `127.0.0.2`、旧固定 IPv4、新 `ChapterTwoServerHost`。后两项不能同时填。新域名模式由 [DomainRelaySession](../../src/DFLegacy.Relay.Core/DomainRelaySession.cs) 每次登录解析/探测并分配 `127.73.*.*` 独立回环和动态端口映射；一次会话固定远端，换公网地址后需重新登录，不能宣传无缝迁移。

启动器通过 `DFLEGACY_CH2_RELAY_TCP/UDP` 传递映射，经 broker 与 `ChapterTwoRelaySockets` 对接。不要手写映射猜端口；权威集合在 `ChapterTwoConnection.TcpPorts/UdpPorts`。账号服务用 48091，游戏入口内部兼容参数仍出现 42311，broker 映射到服务入口 42312，这些不是可随意统一的重复配置。

显示过滤与 post-process 是不同设置。脚本过滤列表为 `legacy/sharp/point/linear`，后处理为 `none/ntsc-svideo/ntsc-composite/ntsc-rf/crt-rgb/xbrz/crt`。`ChapterTwoRenderer` 区分 D3D8 与 D3D11 bridge；仅 D3D8 分支做现有 D3D9 设备预检。桥接库受固定哈希校验，不要下载任意同名 DLL 替换。

### 第二章私有测试控制绝不能随普通包交付

[test_control/README.md](../../src/DFLegacy.ChapterTwo.Native/test_control/README.md) 与 `Policy/Runtime/CombatOverride/CardControl` 是独立的 DO-NOT-SHIP 宿主扩展，不在普通 CMake link 中。它要求专用进程名、nonce、输出根、创建时间、新鲜快照、当前地图和本机角色归属。动作在原游戏线程调用原函数，确认只是“原函数接受”，不是掉落/翻牌/持久化成功。

其中明确有测试传送、杀怪、选牌和塔内无敌；这些不能接进普通启动器、自动开放给普通包，或当作正常玩法修复。跨进程同步、死亡、掉落、奖励必须保留独立服务端/存档证据。

## 6. 原生构建方式与缺失输入

完整工具链与发布命令统一见 [build-test-release.md](build-test-release.md)。以下用于判断“为什么只有源码还构建不了”。

| 目标 | 支持的入口与限制 |
| --- | --- |
| ACT1 IJL/独立 DLL/bootstrap/debug | [Ijl15/build-native.ps1](../../src/DFLegacy.Ijl15/build-native.ps1)：Windows、VS C++、CMake ≥3.24、`-A Win32`、C++17、静态 CRT。默认输出 `artifacts/native/ijl15/<配置>`；不是 `dotnet build` 的纯托管产物 |
| ACT1 测试 | 同脚本 `-RunTests`；`-RunGpuTests` 还要求 `-RunTests` 和可交互图形桌面。CMake 有原生、Python、.NET 夹具；旧 `debug_overlay` 完整 GUI 测试也需桌面，不能把所有默认测试当成完全无 UI |
| 第二章呈现库/夹具 | [ChapterTwo.Native/CMakeLists.txt](../../src/DFLegacy.ChapterTwo.Native/CMakeLists.txt)：x86 MSVC。大部分 C++17，所引入 xBRZ vendor target 使用 C++23。生成 shader bytecode 并链接 Windows 图形库 |
| 第二章完整宿主 | [Build-PresentationHost.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-PresentationHost.ps1) 要求 `AcceptedHostSource`、`RebuiltImage`、新的 `OutputDirectory`；对外部验收源码与镜像固定 SHA256，按唯一源码锚点变换。商城资源夹具需真实 `sprite_interface_cashshop.NPK`；可给 `TownMapTestPvf`。仅构建当前仓库静态库不会凭空生成缺失的验收宿主源码 |
| 第二章 broker | [Build-StartupBroker.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-StartupBroker.ps1) 要求外部 `AcceptedBrokerSource` 和全新输出目录；同样验证源码哈希，生成普通桌面 x64 broker。使用 [Test-StartupBrokerOrdinary.ps1](../../src/DFLegacy.ChapterTwo.Native/Test-StartupBrokerOrdinary.ps1) 验证普通进程契约 |
| 固定登录入口 | [Startup/build-native.ps1](../../src/DFLegacy.Startup/build-native.ps1)：x86/x64 两种，见下一节 |
| RDSEED | [RandomNative/build-native.ps1](../../src/DFLegacy.RandomNative/build-native.ps1)：Windows x64，CMake ≥3.24、C++17、静态 CRT；不支持 x86 target |

外部验收宿主、原版游戏和资源不等于仓库 tracked 源码。遇到缺文件/哈希拒绝时，先向任务上下文或维护者查验收输入位置；不要删校验、拿陌生二进制凑路径或把本机 `dist` 当作公共依赖。构建脚本要求新隔离输出目录时就换新目录，不删除不明旧产物。第三方来源见 [Detours provenance](../../third_party/detours/PROVENANCE.md)、[NTSC provenance](../../src/DFLegacy.ChapterTwo.Native/ntsc-upstream/PROVENANCE.md) 与 `third_party/xbrz`，发布时保留对应许可。

## 7. 日志、原始证据与常见故障

[client_paths::BuildLogPath](../../src/DFLegacy.Client/client_paths.cpp) 只接收文件叶名称，写客户端 `Logs/DFLegacy`，拒绝 traversal/ADS/设备名，不静默回退到根目录。`Config.ini` 与控制文件不属于日志，不能为了“归档”随意搬走。

ACT1 [early_log_redirect](../../src/DFLegacy.Client/early_log_redirect.cpp) 在已映射 `RS.dll` 的具名 `KERNEL32!CreateFileA` IAT 上只拦截精确 `rs_log.txt`，通过宽字符路径写统一目录；这里不能启动工作线程、加载模块或等待游戏初始化。其余日志重定位与游戏自身 `Logs/ScreenShot` 补丁是另一条路径。

第二章使用每次尝试独立 Session；原始 `DNF.trc` / `NeopleEngine.LOG` 在 `Temp/NativeLogs/<PID>`。脚本只从 broker 自有事件文件得到唯一 PID，校验文件时限、大小、普通文件身份再归档；stdout 中的任意路径不能变成复制输入。

| 现象 | 优先检查 | 不应采取的捷径 |
| --- | --- | --- |
| ACT1 helper ready 后仍失败 | commit 字节、helper 退出码、nonce/创建时间、同目录 DLL 文件身份、超时 | 把 ready 当成功、按 PID 杀可能复用的其他进程 |
| ACT1 未知/已补丁镜像被拒绝 | `ClientBootstrapInspector` 与 native 签名、包 DLL 哈希 | 放宽签名后盲目写地址 |
| 第二章自检拒绝 PVF | 是否误用服务端格式，当前客户端白名单是否包含该已验收版本 | 将服务端 PVF 改名后强行启动 |
| 第二章启动多次重试 | 每次 attempt 的普通进程标志、地址兼容尝试、最后游戏退出事件 | 把全部前置失败计为进城后崩溃 |
| 中文输入、鼠标偏移 | IME/键盘时序、客户区坐标、系统与游戏光标、当前过滤/缩放 | 用模拟点击绕过真正坐标修复 |
| 两客户端串线/入口超时 | 各自回环/动态端口、远端探测、Native Relay 映射 | 共用常驻中继或改系统 hosts 代替实例隔离 |
| 调试 DLL 卸载后导航失效 | 核心 Reader/运行时所有权是否被移动、OnFrame 是否仍执行 | 强制普通模式加载 debug.dll |
| 效果已经修复但游戏仍旧 | 产物哈希、内嵌资源哈希、启动进程路径/创建时间 | 认为重新编译就更新了仍在运行的进程 |

更多诊断格式见 [docs/client-incidents.md](../../docs/client-incidents.md)；托管发送队列、隐私边界和 helper 寿命见 [desktop-apps.md](desktop-apps.md)。

## 8. `DFLegacy.Startup` 与 `DFLegacy.RandomNative`

固定入口从 [Startup/main.cpp](../../src/DFLegacy.Startup/main.cpp) 的 `wWinMain` 调用 [bootstrap.cpp](../../src/DFLegacy.Startup/bootstrap.cpp) 的 `Run`，读取 `DFLegacy.Bootstrap.ini`，检查架构/hash/安装状态，启动托管 App 或恢复 worker。`startup.dat` 是当前用户 DPAPI 保护的快速启动凭据，不是可手工复制的版本清单。`.dflegacy/bootstrap/entry.lock` 与 App 锁、就绪事件约束并发和所有权。详见 [Startup/README.md](../../src/DFLegacy.Startup/README.md)，注意其中历史版本发布说明不能代替检查最终 EXE。

原生失败窗口参考 [startup_prompt.cpp](../../src/DFLegacy.Startup/startup_prompt.cpp)。新增项目窗口必须遵守根 [AGENTS.md](../../AGENTS.md) 的原版 DNF 皮肤、像素文字、金棕按钮与焦点/缩放验收。版本默认读 [LauncherVersion.txt](../../src/DFLegacy.Launcher/LauncherVersion.txt)，可显式传 `-Version/-Revision`。`-BuildManagedResources` 给 App/Updater/Standalone 各自产生角色资源；三种发行 EXE 必须同发行号但不同正确角色元数据。

[RandomNative/dflegacy_random.cpp](../../src/DFLegacy.RandomNative/src/dflegacy_random.cpp) 导出 `dflegacy_rdseed_supported` 和 `dflegacy_rdseed64`，先 CPUID 再有限次 RDSEED。调用方 [GameRandomSource.cs](../../src/DFLegacy.Server/GameRandomSource.cs) 负责可用性和操作系统 CSPRNG 后备；没有本桥不应使 Linux 服务端完全无法随机。修改桥时运行 native 测试及服务端随机数测试，验证不支持 CPU、DLL 缺失和重试耗尽分支。

## 9. 修改后的最低验收记录

1. 明确章节、模式、镜像/PVF/宿主/补丁 hash、当前分支和目标进程；只记录必要身份，不记录凭据。
2. 给改动选择已有同名夹具：ACT1 [Client/tests](../../src/DFLegacy.Client/tests)、[Ijl15/tests](../../src/DFLegacy.Ijl15/tests)、[Debug/tests](../../src/DFLegacy.Debug/tests)；第二章各 `*_tests.cpp` 和脚本测试。夹具结果必须区分合成内存与真实原镜像执行。
3. 对固定地址改动验证原字节匹配、禁用/不匹配零写入、完整回滚、寄存器/栈/调用约定与原有分支。第二章外部宿主变换还要验证唯一源码锚点和最终内嵌资源。
4. 显示/输入改动在真实客户端检查至少原尺寸与常用放大、窗口前后台、鼠标命中、中文输入、设备恢复。项目自有窗口另做标题、关闭、长文本、键盘焦点与显示缩放检查。
5. 玩法改动检查普通模式和受控测试模式。组队场景检查双端/多端观察及服务端状态，不能用一次本地动作返回值宣称“同步成功”。
6. 保存准确执行命令、退出码、测试统计、进程/产物身份、截图/日志路径和未覆盖项。文档更新不等于重新跑完 native/游戏验收；本次交接编写没有部署或启动真实游戏。
