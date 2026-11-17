# 桌面应用、更新与客户端辅助服务

> 核对日期：2026-11-17。本页面向维护代码的 agent；源码现状与线上发布状态分开判断。总体状态见 [current-state.md](current-state.md)，游戏内部/native 部分见 [client-and-native.md](client-and-native.md)，构建、发布和运行环境分别见 [build-test-release.md](build-test-release.md)、[operations.md](operations.md)。

## 1. 角色与入口：先找对进程

| 项目/发行角色 | 技术和入口 | 负责什么 |
| --- | --- | --- |
| `DFLegacy.Launcher` | .NET 10 Windows/WPF；[App.xaml.cs](../../src/DFLegacy.Launcher/App.xaml.cs) 的 `OnStartup` | 登录、账号窗口、服务选择、配置、启动游戏、更新 UI、生命周期协调 |
| `DFLegacy.Launcher.exe` 固定入口 | 原生 C++；[Startup/main.cpp](../../src/DFLegacy.Startup/main.cpp) | 校验或恢复启动组件，再打开托管登录器；发行包中不能与直接构建的同名托管 exe 混淆 |
| `DFLegacy.Launcher.App.exe` | 从 Launcher 项目发布的托管 UI 角色 | 由固定入口带验证参数启动，持有 App 锁并确认 ready |
| `DFLegacy.Updater.exe` | Launcher 项目的更新器角色，不是另一个独立 csproj | 修复/V4 安装/恢复入口；同一份启动分派代码按参数、进程角色走不同分支 |
| `DFLegacy.AccountClient` | .NET 库；[AccountServiceClient.cs](../../src/DFLegacy.AccountClient/AccountServiceClient.cs) | 账号 HTTP API 客户端和 DTO，不负责游戏 socket 登录 |
| `DFLegacy.UpdateCore` | .NET 库；[UpdateEngine.cs](../../src/DFLegacy.UpdateCore/UpdateEngine.cs) | 清单、资源下载/校验、事务、回执、配置迁移、跨章升级与回退 |
| `DFLegacy.Diagnostics.Core` | .NET 库；[DiagnosticSession.cs](../../src/DFLegacy.Diagnostics.Core/DiagnosticSession.cs) | 会话绑定、结构化诊断、用户偏好、队列与发送器 |
| `DFLegacy.Diagnostics` | 控制台；[Program.cs](../../src/DFLegacy.Diagnostics/Program.cs) | 带 session/capability 启动的独立诊断发送 helper |
| `DFLegacy.Relay.Core` | .NET 库；[RelayHost.cs](../../src/DFLegacy.Relay.Core/RelayHost.cs) | TCP/UDP 转发、域名会话、HTTP 连接策略；由 Launcher 内嵌调用 |
| `DFLegacy.Relay` | 控制台；[Program.cs](../../src/DFLegacy.Relay/Program.cs) | 可独立运行的中继外壳及退出跟随参数 |
| `DFLegacy.ClientPackager` | .NET 10 Windows/WinForms；[Program.cs](../../src/DFLegacy.ClientPackager/Program.cs) | 离线生成客户端包、调用更新发布工具，依赖 Manager 的导出能力 |

`DFLegacy.Manager` 提供管理器与打包共享实现，详细模块见 [web-and-admin.md](web-and-admin.md) 和 [Manager 源码](../../src/DFLegacy.Manager)。不要因 ClientPackager 引用 Manager 就把两者当成同一应用入口。

### 小上下文任务的阅读顺序

1. 只看本表确认进程/项目。
2. 看相关小节的入口文件和一个对应测试文件。
3. 追踪直接调用方/数据模型；不要先把整个 `Launcher/Updates` 装入上下文。
4. 修改前确认章节、普通/测试包、更新协议版本和真实安装布局；这些会改变执行分支。

## 2. Launcher 启动分派与游戏生命周期

[App.OnStartup](../../src/DFLegacy.Launcher/App.xaml.cs) 优先分派 bootstrap 更新、独立修复、`--dflegacy-apply-update` 等维护入口，随后才进行普通 UI/无界面启动。它处理 pending 事务、跨章恢复、固定入口验证、App 锁与 ready 事件。不要在它完成验证前从另一路直接显示可登录界面。

托管 App 角色需要 `--bootstrap-verified --` 契约；未经固定入口启动时会重定向到固定入口。已管理的安装还要验证当前程序集章节与安装章别一致。跨章 ready probe 在新 UI 完成自检、事务确认之前不显示登录窗口。

[Program.RunHeadlessAsync](../../src/DFLegacy.Launcher/Program.cs) 保留 `--headless`、账号选择、`--self-test`、`--check-d3d9`、`--third-party-notices` 等路径。`--self-test` 验证本地文件/组件，不证明真实游戏已进城。`--install-ijl15` 已明确拒绝覆盖安装旧 JPEG 代理。

[MainWindow.xaml.cs](../../src/DFLegacy.Launcher/MainWindow.xaml.cs) 管登录、账号表单、链接与窗口按钮；分部文件 [MainWindow.ServerSelection.cs](../../src/DFLegacy.Launcher/MainWindow.ServerSelection.cs)、[MainWindow.Updates.cs](../../src/DFLegacy.Launcher/MainWindow.Updates.cs)、[MainWindow.ChapterTransition.cs](../../src/DFLegacy.Launcher/MainWindow.ChapterTransition.cs) 分别管服务器、更新和跨章入口。

[Services/LaunchFlow.cs](../../src/DFLegacy.Launcher/Services/LaunchFlow.cs) 是实际游戏启动总入口：

- 公共前置：注册诊断敏感字段、校验配置/账号、解析客户端、取得游戏共享租约。
- ACT1：要求同目录、验证独立补丁包/原 IJL、检查 PVF 和 D3D9、准备支付站点/Relay，再通过 native bootstrap 创建游戏。
- 第二章：交给 [ChapterTwoClient.RunAsync](../../src/DFLegacy.Launcher/ChapterTwoClient.cs)，使用内嵌脚本/宿主/broker，不走 ACT1 DLL 注入链。
- 运行期：[ClientProcessLifetime](../../src/DFLegacy.Launcher/ClientProcessLifetime.cs) 绑定本次启动进程，[ClientStartupMonitor](../../src/DFLegacy.Launcher/ClientStartupMonitor.cs) 观察早期退出；结束时释放相关会话，不凭进程名清理所有游戏。

游戏原生启动约束和章节差异详见 [client-and-native.md](client-and-native.md)。

## 3. 配置、账号与章节隔离

主模型在 [Models/LauncherOptions.cs](../../src/DFLegacy.Launcher/Models/LauncherOptions.cs)。ACT1 使用同目录 `launcher.json`，第二章使用 `launcher.chapter2.json`，由 [Program.LauncherConfigPath](../../src/DFLegacy.Launcher/Program.cs) 决定。不要把两个配置合并或在第二章保存时覆盖 ACT1 配置；完整配置定位见 [configuration.md](configuration.md)。

| 配置领域 | 关键字段/实现 | 维护要点 |
| --- | --- | --- |
| 客户端定位 | `ClientPath`、`Program.ResolveClientPath` | 相对路径相对 App 基目录；历史默认路径只是 fallback，不代表当前机器安装位置 |
| 连接 | `EntranceHost/Port`、`AccountServicePort/Url`、`Servers/SelectedServerId` | 显式 AccountServiceUrl 优先，否则由入口 host 和账号端口计算；不要把账号 API 当游戏入口 |
| 账号 | `Account/Password`、`Accounts/AccountIndex`、`RememberPassword` | 模型/本地 JSON 可含敏感值，文档、测试输出和错误日志不得复制真实值 |
| 更新 | `UpdateProvider/Repository/Channel/Flavor`、`UpdateServiceUrl`、`DisableGitHubUpdates` | 名称保留历史；最终来源还受已安装回执、章别和协议优先级影响 |
| 本机 Relay | `Relay.Enabled/Preference/TcpPorts/UdpPorts` | ACT1 配置目标必须是中继真正拨号的服务器；开启 Relay 后不能把远端写成 `127.0.0.1` |
| 第二章 | `ChapterTwoServerHost/Ipv4`、`ChapterTwoDisplay/Renderer/PostProcess/Dialogue` | 新域名/旧 IPv4 互斥，显示选项由章节实现标准化 |
| 诊断 | `Diagnostics`、当前用户 `DiagnosticSettings` | 已保存的用户偏好优先于复制来的 JSON 默认值 |

[LauncherAccountStore](../../src/DFLegacy.Launcher/Services/LauncherAccountStore.cs) 会保留未知 JSON 字段，按读取快照合并本调用者改过的字段，并持有安装锁写临时文件再替换。不要简单 `Serialize(options)` 覆盖整份文件；那会丢失新版本字段或另一个窗口的修改。pending 安装/跨章恢复时不允许保存。

[ClientDisplayConfiguration](../../src/DFLegacy.Launcher/ClientDisplayConfiguration.cs) 管 ACT1 `Config.ini`，保留编码和原字节，用 expected-save 防止覆盖外部修改，保存前用账号配置 read lease 确认目标客户端没变。[IniConfiguration](../../src/DFLegacy.Launcher/IniConfiguration.cs) 是实际 INI 解析/写入层；不要用 JSON 配置路径推测 INI 路径。

[ChapterTwoGameSettings](../../src/DFLegacy.Launcher/ChapterTwoGameSettings.cs) 管第二章游戏设置准备；`ChapterTwoClient.ApplyLocalProfile` 标准化章节连接与显示配置。第二章账号新建/重置要求与旧账号登录密码长度有区别：当前新密码提示为 6–16 位字母数字，登录允许 1–16 位，账号均为 4–16 位字母数字，避免阻断迁移来的短密码。

### AccountClient 的边界

[AccountServiceClient](../../src/DFLegacy.AccountClient/AccountServiceClient.cs) 提供：

| 方法 | HTTP 接口 |
| --- | --- |
| `RegisterAsync` | `POST api/accounts` |
| `LoginAsync` / `LogoutAsync` | `POST api/auth/login` / `api/auth/logout` |
| `ChangePasswordAsync` | `PUT api/accounts/{name}/password` |
| `ResetPasswordAsync` | `POST api/accounts/{name}/password/reset` |
| `CheckHealthAsync` | `GET health` |

DTO/结果枚举见 [AccountDtos.cs](../../src/DFLegacy.AccountClient/AccountDtos.cs)。Bearer token 写在每个 `HttpRequestMessage`，不写共享 HttpClient 默认头，避免并行窗口串号。界面应区分成功、重复账号、错误凭据、限流和无效响应，但继续保留服务端“账号或密码错误”的统一提示。改密逻辑必须保留先登录取 token 的权限模型，不能因旧测试端点可用就恢复匿名修改。

## 4. 项目窗口与版本：必须遵守的规范

根 [AGENTS.md](../../AGENTS.md) 是权威要求。**项目自有的新窗口、对话框和启动提示采用对应章节原版 DNF 风格。** 复用现有边框、像素字体、金棕配色和按钮状态，不因已有旧式窗口存在就复制其通用 WinForms/WPF 外观。

| 元件 | 代码 |
| --- | --- |
| 通用样式 | [Views/ClassicStyles.xaml](../../src/DFLegacy.Launcher/Views/ClassicStyles.xaml) |
| 对话框边框 | [Controls/ClassicDialogFrame.xaml](../../src/DFLegacy.Launcher/Controls/ClassicDialogFrame.xaml)、[ClassicDialogFrame.cs](../../src/DFLegacy.Launcher/Controls/ClassicDialogFrame.cs) |
| 像素文字 | [PixelText.cs](../../src/DFLegacy.Launcher/Controls/PixelText.cs)、[PixelRasterizer.cs](../../src/DFLegacy.Launcher/Controls/PixelRasterizer.cs) |
| 按钮/图片/进度 | [SkinButton.cs](../../src/DFLegacy.Launcher/Controls/SkinButton.cs)、`SkinFrame`、`OriginalProgressStrip`、[SkinImage.cs](../../src/DFLegacy.Launcher/SkinImage.cs) |
| 普通提示和确认 | [ClassicMessage.cs](../../src/DFLegacy.Launcher/ClassicMessage.cs)、[ClassicMessageWindow.xaml](../../src/DFLegacy.Launcher/Views/ClassicMessageWindow.xaml) |
| 工作区与光标 | [WindowWorkArea.cs](../../src/DFLegacy.Launcher/WindowWorkArea.cs)、[OwnedPanelCursor.cs](../../src/DFLegacy.Launcher/OwnedPanelCursor.cs) |
| 原生启动提示 | [Startup/startup_prompt.cpp](../../src/DFLegacy.Startup/startup_prompt.cpp) |

功能窗口包括 `Register/Reset/Modify/AccountEdit/AccountFieldDialog/Config/Update/UpdateInstall/UpdateOperation/ChapterTransition`，都在 [Views](../../src/DFLegacy.Launcher/Views)。`ChapterTwoDebugWindow` 是第二章专用调试窗口；`ChapterTwoDebugApi/Resources/Icons` 管后端调用、私有调试 PVF 视图与图标，不能把调试资源操作混进普通安装文件改写。

交付前用**真实内容**检查标题、按钮、关闭操作、长文本、键盘焦点与常用系统显示缩放。可重复验收至少覆盖 100%、125%、150% 和实际目标屏幕工作区；这些比例是实用检查集，规范原文要求是“常用显示缩放”。确认模态窗口的 Enter/Esc/Tab、Owner 和返回焦点，不要只核对 XAML 能编译。

版本有四种概念，不能混用：

1. **登录页显示**：[MainWindow](../../src/DFLegacy.Launcher/MainWindow.xaml.cs) 读取正在运行的程序集 `AssemblyInformationalVersion`，去掉 `+` 后的提交元数据，只显示 `v<发行号>`。
2. **默认第二章发行号**：[LauncherVersion.txt](../../src/DFLegacy.Launcher/LauncherVersion.txt)，核对时为 `1.0.13`。这是源码默认值，不是线上版本证明。Launcher 在设置 `ChapterTwoPayloadDir` 时读取它；原生入口构建也读取它。
3. **更新身份**：V4 清单包含 Version、Commit、Channel、Architecture、Flavor、Sequence；字符串看似更大不代表协议允许升级。
4. **Windows 文件属性**：原生入口、托管 App、Updater 必须用同一发行号，但具有各自角色对应的文件说明、原始文件名、产品名、发布者、版权、文件版本和产品版本。资源模板见 [launcher.rc.in](../../src/DFLegacy.Startup/launcher.rc.in)，最终产物需检查属性并真实启动。

Git 提交号保存到构建元数据中供核对，不能塞进登录页当发行号。不要只改 `LauncherVersion.txt` 就声称三个 EXE 已同版。

## 5. 更新系统：UI 编排和事务引擎分开看

### 5.1 `Launcher/Updates` 的分工

| 文件组 | 职责 |
| --- | --- |
| [UpdateCoordinator](../../src/DFLegacy.Launcher/Updates/UpdateCoordinator.cs) | 窗口寿命、Dispatcher、普通自动更新、恢复确认、文件占用处理、切回固定入口 |
| [UpdateController](../../src/DFLegacy.Launcher/Updates/UpdateController.cs)、`UpdateModels` | 状态机、取消/确认/安装动作；不承担底层文件替换 |
| [V4UpdateSession](../../src/DFLegacy.Launcher/Updates/V4UpdateSession.cs)、`UpdateSession` | UI 会话适配：V4 与旧协议入口、来源解析、检查/下载/交接 |
| `GitHubUpdateClient`、`HostedUpdateClient`、`UpdatePackage/Artifacts/Download` | 历史 GitHub ZIP/Hosted 更新读取与包校验；不是所有文件都属于 V4 |
| [UpdateInstaller](../../src/DFLegacy.Launcher/Updates/UpdateInstaller.cs)、`UpdateHelper/V4UpdateHelper` | 脱离当前 App 的安装器交接、参数/身份、进度和恢复 |
| `UpdateTransaction`、`UpdateSafety` | 历史安装事务与安全校验；V4 核心事务在 UpdateCore |
| [BootstrapLayout](../../src/DFLegacy.Launcher/Updates/BootstrapLayout.cs)、`BootstrapUpdateClient`、`RepairClient` | 固定入口布局、描述文件、已完成回执、恢复与独立修复 |
| [StartupEntryMigration](../../src/DFLegacy.Launcher/Updates/StartupEntryMigration.cs) | 明确验证的固定入口代际迁移；不是普通更新任意替换入口的后门 |
| `ChapterUpdatePolicy`、`ChapterDisplayMigration`、`ChapterTransitionOffer` | 章别布局约束、显示设置迁移与跨章提示 |
| `FileLockOwners`、`UpdateProgressText`、`UpdateInstallProgress` | 文件锁占用、用户进度文本与进度数据 |

当前构造默认使用 V4 session，但源码明确保留 Hosted V3 优先级和旧协议回退，判断实际路径请看 `V4UpdateSession`，不要把所有旧更新类删成“死代码”。

### 5.2 V4 核心流程

入口 [UpdateEngine](../../src/DFLegacy.UpdateCore/UpdateEngine.cs)：`CheckAsync` → `DownloadAsync` → 形成 `StagedUpdate` → [InstallTransaction.Apply](../../src/DFLegacy.UpdateCore/InstallTransaction.cs) → 全量结果校验 → 写已安装与已校验回执。发现线上版本不能覆盖最后一次已提交安装的事实。

| UpdateCore 文件 | 要维护的不变量 |
| --- | --- |
| [Manifest.cs](../../src/DFLegacy.UpdateCore/Manifest.cs) | schemaVersion=4；严格大小/字段/重复键；每文件 SHA256/大小/分卷；来源代际 Sequence；退役 delete/unmanage；配置迁移 |
| [ReleaseSource.cs](../../src/DFLegacy.UpdateCore/ReleaseSource.cs) | Provider/Repository/Channel/Architecture/Flavor 组成身份；不是任意 URL 更新器，保留当前允许来源约束 |
| `ReleaseProvider`、`ChapterTwoReleaseProvider`、`LoopbackReleaseProvider` | 公共/镜像、第二章代际清单、本机测试来源；验证完整绑定 |
| `ResourceDownloader`、`ContentCache`、`FileVerifier` | 分卷下载、内容寻址缓存、完整文件校验；下载成功不能替代最终文件 hash |
| [InstallationStore.cs](../../src/DFLegacy.UpdateCore/InstallationStore.cs) | `.dflegacy/v4/installed.json` 是已安装事实；`validated.json` 是完成校验凭据；`pending.json` 是未完成事务 |
| [InstallTransaction.cs](../../src/DFLegacy.UpdateCore/InstallTransaction.cs) | 锁/目录 pin/空间检查、备份、替换、退役、提交和回滚；中断后 Recover，不删 journal 冒充完成 |
| `PathPolicy`、`DirectoryPin`、`InstallationLease`、`ClientGameLease` | 路径范围、链接拒绝、目录身份、安装互斥与游戏会话互斥 |
| `ConfigurationMigration` | 声明式、可验证的配置变换；保留非目标字段，绑定获准来源 |
| `ChapterTransition*` | 章别身份、跨章来源授权、配置隔离、checkpoint 与回退 |

`CheckAsync` 拒绝同序号不同清单、低于 MinimumSequence 的旧安装和遗漏旧受管理文件的新清单。线上序号落后于可信本地安装时验证并保留本地，不做降级。离线只表示可校验缓存安装，不表示“已确认线上最新”。

完成回执可用于启动快路径，依赖关键文件属性与配置条件，不是每次登录都重新 hash 全部资源。pending 状态优先于回执；显式修复和强制校验走完整校验。不能把回执存在当作任意磁盘更改都已检测。

### 5.3 固定入口与跨章不能走普通替换

布局由 [BootstrapLayout](../../src/DFLegacy.Launcher/Updates/BootstrapLayout.cs) 定义：`DFLegacy.Launcher.exe`、`DFLegacy.Launcher.App.exe`、`DFLegacy.Updater.exe`、`DFLegacy.Bootstrap.ini`。五行 ASCII 描述文件绑定架构和 App/Updater hash。

`RequireStableEntry` 检查目标固定入口 hash 与当前一致。入口字节变化必须发新完整启动包或走已验证的迁移流程；**不能删 stable-entry guard 或手工改回执来“放行”**。现有第二章代际迁移含 bridge/最终清单和最低序号约束，参见 [Startup/README.md](../../src/DFLegacy.Startup/README.md) 与 `StartupEntryMigration`；具体发布顺序归 [build-test-release.md](build-test-release.md)。

跨章从 [MainWindow.ChapterTransition](../../src/DFLegacy.Launcher/MainWindow.ChapterTransition.cs) 发起，经 `ChapterTransitionOffer`、`CheckChapterUpgradeAsync`、`PrepareUpgrade`、安装器 ready probe 进入第二章。要求显式选择、可信第一章桥接安装、目标来源授权和完整旧文件去向。回退使用 checkpoint/rollback journal；不要通过改 `launcher.json` 中的 chapter 字符串模拟。章别描述验证代码在 [ChapterTransitionIdentity.cs](../../src/DFLegacy.UpdateCore/ChapterTransitionIdentity.cs)。

更新前 [AutomaticDiagnostics.SuspendForUpdateAsync](../../src/DFLegacy.Launcher/Diagnostics/AutomaticDiagnostics.cs) 要让本次 helper 结束并释放文件；游戏仍活着会拒绝交接。已交给安装器后不能由窗口 finally 又重启诊断 helper，否则会重新锁住待替换文件。

## 6. 诊断：本地证据、结构化报告、上传器分开

### 6.1 Launcher 侧

[Diagnostics/IncidentReporter](../../src/DFLegacy.Launcher/Diagnostics/IncidentReporter.cs) 写本地上下文/事故文件并注册敏感字段；[ClientLogPaths](../../src/DFLegacy.Launcher/ClientLogPaths.cs) 管客户端 `Logs/DFLegacy` 和旧日志归档；[DetailedCrashCapture](../../src/DFLegacy.Launcher/Diagnostics/DetailedCrashCapture.cs) 管本地详细捕获；[ChapterTwoLaunchDiagnostics](../../src/DFLegacy.Launcher/Diagnostics/ChapterTwoLaunchDiagnostics.cs) 解析第二章启动和游戏退出证据。

[AutomaticDiagnostics](../../src/DFLegacy.Launcher/Diagnostics/AutomaticDiagnostics.cs) 协调会话、用户偏好、helper 和更新挂起。ACT1 可启动同目录已验证 `DFLegacy.Diagnostics.exe`，运行前仍校验 helper 身份/hash。第二章当前使用进程内 `DiagnosticHelper` task，并通过域名 HTTP connector 上传；不能假定每次第二章启动都有独立 Diagnostics.exe 子进程。

第二章无远端配置的普通本机包会关闭自动上报；local-test 构建有专属诊断根。失败时只保留本地记录/警告，不阻止游戏主流程。原始转储只留本地。

### 6.2 Core 与独立 helper

| 组件 | 职责 |
| --- | --- |
| [DiagnosticSettings](../../src/DFLegacy.Diagnostics.Core/DiagnosticSettings.cs) | 默认根 `%LOCALAPPDATA%/DFLegacy/Diagnostics`，当前用户 Enabled/Epoch/LocalSmallDumps；只读状态查询不发网络请求 |
| [DiagnosticSession](../../src/DFLegacy.Diagnostics.Core/DiagnosticSession.cs) | 注册 session/capability、安装路径/接收站点/偏好作用域、绑定拥有者和游戏创建身份、采集已确认事件 |
| [DiagnosticModels](../../src/DFLegacy.Diagnostics.Core/DiagnosticModels.cs) | 严格白名单报告投影；不接受任意异常文本/原日志/转储字段；硬件状态有界，未知值仍 unknown |
| [DiagnosticQueue](../../src/DFLegacy.Diagnostics.Core/DiagnosticQueue.cs) | 冻结请求正文、去重、限额/过期、站点作用域、撤销/重试/回执；发送 `/api/client-diagnostics/reports` |
| [DiagnosticHelper](../../src/DFLegacy.Diagnostics.Core/DiagnosticHelper.cs) | 有界循环收集和上传，观察会话拥有者结束后短时排空；不附加 debugger、不拥有游戏 Job、不终止游戏 |
| [DiagnosticHardware](../../src/DFLegacy.Diagnostics.Core/DiagnosticHardware.cs) | 有界系统/显卡等信息采集 |
| [Diagnostics/Program](../../src/DFLegacy.Diagnostics/Program.cs) | 只接受 `--session <目录> --capability <能力值>`，打开匹配会话后运行 helper |

队列当前最多 100 条、32 MiB，本地状态查看按 14 天窗口；HTTP 禁止自动 redirect/cookies/proxy，单站点连接受限，有超时和重试上限。用户关闭或更换接收站点/偏好 epoch 后，旧 scope 不应被重新解释为可上传。修改时保持这些行为，不只看 happy-path 上传成功。

`DiagnosticIncident` 的报告不能携带任意路径、日志文本、账号/密码/token。网络异常使用枚举、端口、socket code 等有限字段；崩溃因果要用 confirmed evidence 和关联 event ID，不能把用户取消、正常退出、网络 EOF 全算 crash。更多格式与本地捕获约束见 [docs/client-incidents.md](../../docs/client-incidents.md)。

## 7. Relay、支付和公会本机服务

[RelayHost](../../src/DFLegacy.Relay.Core/RelayHost.cs) 负责在一个本地地址监听指定 TCP/UDP 并转发远端；`Start` 先绑定全部端口，成功后才运行。ACT1 Launcher 按配置内嵌启动它，生命周期跟随游戏。端口冲突先检查旧实例，不要自动终止同名进程。

[RelayOptions](../../src/DFLegacy.Relay/RelayOptions.cs) 是独立命令行接口：`--server`、`--bind`、`--tcp/udp`、`--prefer`、`--exit-with-pid`、`--exit-with-image`。CLI 默认端口集合较小，不等同 Launcher 扩展后的 ACT1 PvP/第二章集合。用 CLI 替代内嵌 Relay 时必须核对实际端口。

[DomainRelaySession](../../src/DFLegacy.Relay.Core/DomainRelaySession.cs) 是第二章会话策略：解析候选交错尝试 IPv6/IPv4，有总超时和单地址超时；选中一个可达入口后固定远端，给每个实例分配独立回环地址和可选动态端口。退出/取消时 Stop 并释放。它不修改系统 DNS/hosts，不能保证远端变址时当前游戏连接不断开。

[DomainHttpConnection](../../src/DFLegacy.Relay.Core/DomainHttpConnection.cs) 只竞速/尝试建连；[ChapterTwoConnection.CreateAccountHttpClient](../../src/DFLegacy.Launcher/ChapterTwoConnection.cs) 设置有限连接复用寿命和关闭代理/重定向。不要通过“请求重试”重复扣款或充值 POST。[RelayTermination](../../src/DFLegacy.Relay.Core/RelayTermination.cs) 把正常 EOF/取消/预期交接与 socket/超时/绑定错误分开，诊断 sink 异常不能破坏传输。

| Launcher 内部服务 | 用途和约束 |
| --- | --- |
| [LocalPaymentSite](../../src/DFLegacy.Launcher/LocalPaymentSite.cs) | 只监听回环随机端口，使用随机路径前缀、Host 校验、有界请求，嵌入 `Server/wwwroot/client` 页面资源并转发受支持 API |
| [ChapterTwoPaymentRelay](../../src/DFLegacy.Launcher/ChapterTwoPaymentRelay.cs) | 为第二章原生 URLMon/内嵌浏览器桥接本次支付路径与回复目录，保留原游戏充值窗口 |
| [ChapterTwoGuildSite](../../src/DFLegacy.Launcher/ChapterTwoGuildSite.cs) | 只代理明确公会页面/API，当前监听 `127.0.0.2` 随机端口，按 Launcher 实例命名 cookie，再交系统浏览器打开 |
| [ChapterTwoTestAudio](../../src/DFLegacy.Launcher/ChapterTwoTestAudio.cs) | 第二章测试音频会话策略，不等于全系统静音 |

本机页面只是显示/桥接，实际充值能力仍受服务端配置和权限控制。对支付、公会和账号的修改需同时验重定向、Host、cookie/token 隔离、双开和取消/关闭后游戏键鼠状态。

## 8. ClientPackager 的当前定位和限制

[PackagerForm](../../src/DFLegacy.ClientPackager/PackagerForm.cs) 读取发行资源目录/目标连接配置，支持配置方案、生成 ZIP 和 V4 资源窗口。它当前是较早的通用 WinForms 界面，这是**现状**，不能当成违反根 AGENTS 新窗口规范的理由。新窗口或新启动提示依然要用章节 DNF 外观。

[ClientBuild.RunAsync](../../src/DFLegacy.ClientPackager/ClientBuild.cs) 要求发行目录已有 `client-assets/manifest.json` 和 x86 Launcher 资源，读取其中 version/commit，调用 Manager 的 `ClientPackageExporter.ExportPairAsync` 生成双架构包，先写 `.tmp` 再移动到输出 ZIP。缓存位于当前用户 LocalAppData 的 DFLegacy/ClientPackager。

它保留旧“生成并发布到 GitHub”路径：`github_publish.py --preflight`、`release.py build-client-packages --updates-only`、四个 V2 ZIP 上传。另有 `RunV4Async` 调用 `update_v4.py preview/build/verify/publish`。不能认为旧按钮已自动涵盖第二章 V4 最新发布流程。

Python 入口当前固定为 `py -3.12`，工具脚本位于应用旁 `tools/release`，缺 Python/脚本会失败；这部分不等于完全不依赖工具链的单 EXE。具体应发布哪些资源、何时可 publish 和实际验证归 [build-test-release.md](build-test-release.md)。仅修改文档或研究代码不授权点击发布。

## 9. 常见任务的修改位置和验收

| 任务/故障 | 最短定位路径 | 至少验收 |
| --- | --- | --- |
| 登录页版本不对 | LauncherVersion.txt → csproj 发布属性 → MainWindow 运行程序集版本 | 三 EXE 文件属性、真实 UI、commit 仍在元数据 |
| 登录按钮后没游戏 | MainWindow → LaunchFlow → 章节分支 → native/脚本日志 | 自检 + 对应真实客户端；失败不留下孤儿进程 |
| 配置保存丢字段/账号串窗口 | LauncherAccountStore 快照合并、AccountClient 请求头 | 未知字段保留、并发保存、取消/重开、两账号 token 隔离 |
| 更新后仍提示旧版/循环修复 | 来源身份、Sequence、installed/validated/pending、Bootstrap.ini | 缓存与线上分别判断、离线、损坏文件、恢复、角色一致 |
| 固定入口无法更新 | stable-entry hash、bridge/代际迁移 | 普通拒绝保持；获准迁移必须做中断恢复与最终启动 |
| 更新文件占用 | 游戏共享租约、App 锁、诊断 helper、FileLockOwners | 只处理绑定身份；交接后不重新创建 helper |
| 第二章资源错误 | ValidateSource、PVF 格式/白名单、ImagePacks2 | 不改原资源绕校验；核对配套资源和镜像 |
| 域名可解析但进不去 | DomainRelaySession 的入口可达探测、实际 TCP/UDP 集合 | 双地址族候选、超时/取消、双开隔离；HTTP 请求不重复提交 |
| 自动诊断重复/误上报 | Session evidence/owner、Queue scope/body、用户 Epoch | 关闭立即停止、换站点隔离、重复/冲突回执、正常退出不计 crash |
| 弹窗文字截断/焦点错 | Classic 样式、窗口尺寸/工作区、Owner、PixelText | 真实长文、缩放、Enter/Esc/Tab、关闭后焦点回原窗口 |

### 对应测试项目

这些是 `OutputType=Exe` 的自运行检查程序，不应仅跑 `dotnet test` 没有发现测试就声称通过。通常用 `dotnet run --project <项目> -c Release -- <支持的选择器>`，完整命令和前置依赖见 [build-test-release.md](build-test-release.md)。

- [Launcher/tests](../../src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj)：UI/账号/启动、bootstrap、V2/V3/V4、锁与恢复、第二章设置/诊断/公会/跨章等。选择器在 [Program.cs](../../src/DFLegacy.Launcher/tests/Program.cs)，不要编造通用测试过滤参数。WPF 与 native 资源依赖须满足。
- [UpdateCore.Tests](../../tests/DFLegacy.UpdateCore.Tests/DFLegacy.UpdateCore.Tests.csproj)：V4 清单/下载/事务、路径与跨章；已有 `--chapter-transition` 入口。
- [Diagnostics.Tests](../../tests/DFLegacy.Diagnostics.Tests/DFLegacy.Diagnostics.Tests.csproj)：Core 和服务端接收契约，包含本地队列、偏好与上传状态。
- [Relay.Tests](../../tests/DFLegacy.Relay.Tests/DFLegacy.Relay.Tests.csproj)：TCP/UDP、域名和生命周期行为；第二章协议还看 [ChapterTwo.Network.Tests](../../tests/DFLegacy.ChapterTwo.Network.Tests)。
- 第二章启动脚本另有 [ChapterTwoStartup.Tests.ps1](../../src/DFLegacy.Launcher/tests/ChapterTwoStartup.Tests.ps1)、[ChapterTwoNativeLogArchive.Tests.ps1](../../src/DFLegacy.Launcher/tests/ChapterTwoNativeLogArchive.Tests.ps1)，必须用兼容交付的 PowerShell 环境验证。

界面测试通过不替代最终 EXE 启动和缩放；更新单元检查通过不替代真实隔离安装的中断恢复；诊断成功不代表游戏故障根因已定位。记录具体运行命令、统计/退出码、产物 hash、现场覆盖和未完成项。本次编写交接文档只核对源码/既有记录，没有发布、部署或执行真实游戏验收。
