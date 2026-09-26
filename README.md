# DFLegacy

<a href="#运行环境"><img src="https://img.shields.io/badge/Windows-0078D4" alt="Windows" /></a>
<a href="#构建与启动"><img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" /></a>
<a href="#模块说明"><img src="https://img.shields.io/badge/UI-Web-7A3E9D" alt="Web UI" /></a>
<br />
<a href="#运行环境"><img src="https://img.shields.io/badge/architecture-x86%20%2F%20x64-455A64" alt="x86 / x64" /></a>
<a href="#许可证"><img src="https://img.shields.io/badge/license-0BSD-2E7D32" alt="0BSD License" /></a>

面向赛里斯服公测 ACT1 客户端的本地模拟服务端、启动器与客户端补丁。

项目从 `Script.pvf` 读取客户端配置数据，并将用户数据在本地持久化。

项目不包含客户端、官方服务端文件或 PVF，相关资源的权利归原权利人所有，请自行确保使用资源的合法性。

本项目用于个人学习、研究及技术交流使用，任何项目副本均与本项目作者无关。

本项目所有资源均来源于互联网公开信息，仅用于学习测试网络连通性与技术实现方式。

本项目不对资源的合法性、准确性、完整性作任何保证，也不承担任何直接或间接责任。

如本项目内容侵犯了您的合法权益，请及时联系删除，作者将在第一时间进行处理。

使用本项目即代表您已阅读并同意以上声明内容。

## 文档

| 文档 | 内容 |
| --- | --- |
| 本文件 | 项目概况、模块职责、已实现内容、平台支持与配置速查。 |
| [BUILDING.md](BUILDING.md) | 各平台从源码构建可执行文件与原生模块、发布产物、构建排障。 |
| [DEPLOYING.md](DEPLOYING.md) | 部署形态、目录结构、网络与端口、容器部署、数据备份、升级流程、部署排障。 |
| [STATUS.md](STATUS.md) | 项目状态与接续说明：已验证/未验证内容、可复用的验证方法、待办与环境注意事项。 |
| [ADMIN_API.md](ADMIN_API.md) | 后台管理 HTTP 接口参考。 |

容器部署使用仓库根目录的 `Dockerfile`、`.dockerignore` 与 `compose.yaml`，说明见 [DEPLOYING.md](DEPLOYING.md#5-容器部署docker--podman)。

## 模块说明

| 模块 | 作用 |
| --- | --- |
| `DFLegacy.Server` | 主业务、PVF 数据读取、账号角色持久化、TCP/UDP 服务及 HTTP 管理接口。 |
| `DFLegacy.Protocol` | 登录与通信协议的编解码、请求解析、响应与通知包构建。 |
| `DFLegacy.Relay` / `DFLegacy.Relay.Core` | 本机 TCP/UDP 端口中继：让只支持 IPv4 的原版客户端经本机连接 IPv6 服务器。核心库由启动器在自身进程内托管，独立可执行文件供手动运行。见 [DEPLOYING.md](DEPLOYING.md#64-让-ipv4-客户端连接-ipv6-服务器dflegacyrelay)。 |
| `DFLegacy.Launcher` | Windows 启动器：WPF 交互式登录壳，含注册/登录/改密/找回/账号管理/配置；检查运行环境、安装 IJL15 模块并启动客户端，可按需启动本地服务端。`--headless`/`--account` 仍支持双击即玩。 |
| `DFLegacy.AccountClient` | 启动器使用的账号 HTTP 客户端（注册、改密、找回密码、健康检查）。 |
| `DFLegacy.Ijl15` | 32 位 C++ `ijl15.dll` 兼容模块，提供客户端所需的 JPEG 功能及进程内兼容补丁、窗口缩放等功能。 |
| `DFLegacy.RandomNative` | 64 位 C++ 硬件随机种子桥接，为玩法随机提供 RDSEED 支持；不支持该指令时自动回退，不是运行必需项。 |
| `wwwroot/admin` | 独立的中文后台管理页面，使用现代浏览器访问，不依赖前端构建工具。需登录：管理员看到全部 GM 功能与「用户角色」，普通玩家只看到自己的角色（另有独立的玩家页 `player.html`）。 |
| `wwwroot/client` | 充值及会员服务页面，与后台管理页面分离。 |
| `tests/DFLegacy.SmokeTests` | 协议、数据解析和主要业务规则的冒烟测试。 |

## 平台支持

| 组件 | Windows | Linux | macOS | 说明 |
| --- | --- | --- | --- | --- |
| `DFLegacy.Server` | 支持 | 支持 | 支持 | 纯托管代码，只依赖 .NET 10 与 Kestrel，无强制原生依赖 |
| `DFLegacy.Protocol`、冒烟测试 | 支持 | 支持 | 支持 | 纯托管代码 |
| `DFLegacy.Relay` | 支持 | 支持 | 支持 | 纯托管端口中继，无原生依赖 |
| 服务端容器镜像 | 支持 | 支持 | 支持 | 见 [DEPLOYING.md](DEPLOYING.md#5-容器部署docker--podman)；宿主只需能运行 Docker 或 Podman |
| `DFLegacy.Launcher` | 支持 | 不支持 | 不支持 | WPF 程序，且需要检测 D3D9 设备 |
| `DFLegacy.Ijl15`（32 位客户端补丁） | 支持 | 不支持 | 不支持 | 原生 C++，用于替换客户端模块 |
| `DFLegacy.RandomNative`（64 位随机桥） | 支持 | 不支持 | 不支持 | 可选；缺失或不可用时自动回退到托管随机 |
| 游戏客户端 | 支持 | 不支持 | 不支持 | 原版 32 位 Windows 客户端 |

因此服务端可以部署在 Linux 或 macOS 上、也可以放进容器，由 Windows 客户端远程连接；构建方式见 [BUILDING.md](BUILDING.md)，部署与网络配置见 [DEPLOYING.md](DEPLOYING.md)。

## 当前实现

| 系统 | 已实现内容 |
| --- | --- |
| 账号与角色 | 注册、登录、改密与找回密码；角色创建、选择及状态保存；支持多账号；点券、契约及黑钻等服务按账号管理。 |
| 后台权限 | 8081 管理接口需 Bearer token 登录，账号分「玩家 / 管理员」两档并持久化角色：玩家只能查自己的角色，管理员可发邮件、广播、充值、改副本难度、提升/降级他人。登录失败按账号+来源 IP 限流，角色变更即时吊销旧 token。 |
| 角色成长 | 怪物经验、通关经验及结算字段；SP 随当前等级的经验进度渐进发放，PVF 缺少 `etc/sptable.etc` 时回退到与原版一致的内置 SP 表；经验书、升级券、属性类道具；技能学习、重置、转职与觉醒相关处理。 |
| 副本 | 进入条件、疲劳消耗与恢复、虚弱及定时恢复、难度解锁；房间切换与重访状态保存；精英怪、深渊入口与相关掉落处理。 |
| 掉落与奖励 | PVF 掉落表、怪物独立掉落、精英专属掉落及任务掉落；地面物品、拾取、负重检查；翻牌结算处理。 |
| 任务 | 接取、进度、完成与奖励；接取时给予任务物品；击杀、收集、普通及带条件通关等已适配条件；清除地图任务激活时，其救援图会替换当次副本的 boss 房并按 boss 房结算；隐藏副本与职业相关任务处理。 |
| 物品与装备 | 背包、快捷栏、仓库、丢弃、拾取、商店买卖；非堆叠装备 UUID、强化、品级、封装状态、再封装、分解与配方制作；商店每日统一品级种子。 |
| 商城与装扮 | 商城购买、账号点券扣除、契约及兑换券直接兑现；礼包购买时打开、随机礼包抽选及手动开启；装扮发放与合成。 |
| 宠物 | 宠物与宠物装备、经验升级、饱食度变化与自动喂食、进化任务相关处理、死亡与复活、脚本消息及喊话请求处理。 |
| 邮件与通知 | 邮件列表、附件领取及保管箱相关处理；普通物品、装备、装扮和宠物蛋附件；指定角色或在线广播的消息通知与自定义弹窗。 |
| 决斗数据 | 决斗经验表读取、决斗经验书、等级计算及记录同步。 |

### 后台管理

默认入口：<http://127.0.0.1:8081/admin/index.html>。

- 给指定角色发送邮件，可附金币和一项物品附件，区分普通物品、装扮与宠物蛋。
- 给在线角色发送消息或弹窗，或向当前在线角色广播；消息支持类型选择。
- 设置角色可进入的副本最高难度。
- 查看角色在线状态，从已加载 PVF 中搜索物品和选择副本。

## 已知缺口

- **多人组队** 尚未完成组队、离队、成员同步及多人副本流程。
- **玩家交易** 尚未实现玩家之间的物品、金币交易及双方确认流程。
- **私人商店** 尚未实现摆摊、商品展示及玩家购买流程。
- **社交与公会** 尚未实现好友、黑名单及完整的公会系统；战场、公会战等玩法尚未实现和验证。
- **决斗场** 已有决斗经验、等级及记录同步，尚未实现房间、准备、对战及结算流程。
- **数据持久化** 当前采用 JSON 文件，尚未接入数据库，不支持多实例共享写入。

## 运行环境

- **服务端**：需要 .NET 10 运行时（含 ASP.NET Core），Windows、Linux、macOS 均可；Linux 不需要 C++ 工具链，但最小化系统还需 ICU 与时区数据，见 [DEPLOYING.md](DEPLOYING.md#4-linux-与-macos-无头服务端部署)。也可以直接用容器（Docker / Podman）运行，见 [DEPLOYING.md](DEPLOYING.md#5-容器部署docker--podman)。
- **客户端与启动器**：仅 Windows。客户端及 IJL15 为 32 位，硬件随机桥接为 64 位；启动器需要可用的图形桌面，会检查 Direct3D 9 HAL 设备，远程或无显示环境可能无法通过检查。
- **从源码构建**：需要 .NET 10 SDK；构建原生模块与启动器还需要带桌面 C++ 开发组件的 Visual Studio 和 CMake ≥ 3.24，见 [BUILDING.md](BUILDING.md)。

## 构建与启动

各平台的构建差异、原生模块与发布产物见 [BUILDING.md](BUILDING.md)，部署、网络配置与升级流程见 [DEPLOYING.md](DEPLOYING.md)。下面是 Windows 单机环境的快速开始。

在仓库根目录打开 Visual Studio 开发者 PowerShell，先构建混合解决方案：

```powershell
msbuild .\DFLegacy.Emulator.slnx /m /restore /p:Configuration=Release
```

发布服务端与启动器到同一目录：

```powershell
dotnet publish .\src\DFLegacy.Server\DFLegacy.Server.csproj -c Release -o .\dist\DFLegacy.Server
dotnet publish .\src\DFLegacy.Launcher\DFLegacy.Launcher.csproj -c Release -o .\dist\DFLegacy.Server
```

启动器发布会构建并复制 IJL15。若只构建服务端而没有 `DFLegacy.RandomNative.dll` 原生随机模块，则仍使用操作系统伪随机。

1. 编辑发布目录中的 `server.json`，将 `DFLegacy.ScriptPvfPath` 改为实际 PVF 路径。
2. 编辑同目录的 `launcher.json`，设置 `ClientPath` 和登录账号；客户端目录也应包含匹配的 `Script.pvf`。
3. 运行 `DFLegacy.Launcher.exe`。默认 `StartServer: true`，启动器会按需启动本地服务端。

仓库示例配置含测试环境绝对路径，需要自行调整，并不会自动适配目录。`server.json` 按较新者优先复制到发布目录，重复发布前应先备份，或发布后改回本机路径。

仅运行服务端时，可在发布目录执行：

```powershell
.\DFLegacy.Server.exe
```

更新前备份数据，避免覆盖现有存档。不要同时启动多个实例写入同一份状态文件。

## 常用配置

### 服务端

`server.json` 的设置位于 `DFLegacy` 节：

| 设置 | 用途 |
| --- | --- |
| `ScriptPvfPath` | PVF 路径；相对路径以服务端程序目录为基准。更新 PVF 后重启服务端重新加载。 |
| `DataPath` | 存档路径，默认 `data/state.json`，相对路径以服务端程序目录为基准。 |
| `Admin` | 后台监听地址与端口，默认 `::`（双栈）+ `8081`。**全部管理接口需要登录**（Bearer token），并分「玩家 / 管理员」两档：玩家只能看自己的角色，管理员可发邮件、广播、充值、改角色档位。`Admin.Users` 为引导管理员名单（**只在启动时提升**，防抢注提权）；`TokenHours` / `MaxTokenLifetimeHours` 控制 token 滑动有效期与绝对上限；`LoginSourceMaxAttempts` / `LoginAccountLockSources` / `LoginLockoutMinutes` 控制两级登录限流（单来源尝试额度 + 多来源才锁账号）。详见 [ADMIN_API.md §1.2](ADMIN_API.md#12-鉴权与权限) 与 [DEPLOYING.md](DEPLOYING.md) 的安全边界。 |
| `Entrance` / `Channel` | 登录入口和频道连接配置。 |
| `CharacterDatagram` / `GameplayDatagram` | 客户端 UDP 监听配置。 |
| `GameplayDatagram.AdvertisedHost` | 下发给客户端的玩法地址，留空时回退为 `GameplayDatagram.Host`；容器与 NAT 部署必填，详见 [DEPLOYING.md](DEPLOYING.md#61-监听与下发地址)。 |
| `Drop.Enabled` | 服务端掉落总开关。 |
| `Drop.RatePercent` | 普通掉落倍率百分比，`100` 使用 PVF 原始概率。 |
| `Drop.EconomicRate` | 金币倍率，默认 `1.0`。 |
| `Drop.ForceDrops` | 强制掉落测试开关，正常体验应保持 `false`。 |
| `Experience.MonsterMultiplier` | 逐只怪物经验倍率，默认 `1.0`。 |
| `Experience.ClearMultiplier` | 通关基础经验及评分奖励倍率，默认 `1.0`。 |
| `EnablePacketTracing` | 协议包跟踪开关，用于排查客户端交互问题。 |

默认 TCP 端口为 `2311`、`7001`、`8080`，管理 HTTP 为 `8081`；默认 UDP 端口为 `2311`、`7002`、`7003`。更改网络配置时，应同步检查启动器与频道地址。监听地址与下发地址的区别、IP 地址族（IPv6）支持现状见 [DEPLOYING.md](DEPLOYING.md#6-网络配置)。

### 启动器

`launcher.json` 支持 `Accounts` 账号列表及 `AccountIndex`；也可以使用 `--account-index N` 或 `--account NAME` 临时选择已配置账号。默认测试凭据为 `test/test`，请勿将包含真实密码的配置公开分发。启动器无参运行时打开交互界面（账号下拉 + 密码 + 服务端状态灯），注册、登录、改密、找回与账号管理都在界面内完成。

| 设置 | 用途 |
| --- | --- |
| `AccountServiceUrl` | 账号 HTTP API 基址，默认 `http://127.0.0.1:8081`（服务端 Admin 端口）；注册/改密/找回经此访问。 |
| `Theme` | 启动器界面主题：`auto` / `light` / `dark`。 |
| `Accounts[]` 子项 | 除 `Account` / `Password` 外还支持 `DisplayName`（显示名）、`CreateTime`（创建时间）、`OicqCode` 与 `RememberPassword`（记住密码，默认 `true`；为 `false` 时只保存空密码，启动前需在界面输入，无头启动需要存储密码）。 |

`Relay.Enabled` 打开后，启动器会把本地中继托管在自己进程内（启动客户端后驻留到客户端退出，不额外产生进程），用于客户端只能 IPv4、服务器只有 IPv6 的场景，见 [DEPLOYING.md](DEPLOYING.md#64-让-ipv4-客户端连接-ipv6-服务器dflegacyrelay)。

启动器会安装发布目录中的 `ijl15.dll`，首次替换已有模块时保留 `ijl15.original.dll`，缺少客户端 `Config.ini` 时复制默认配置。IJL15 补丁只修改当前进程内存，不改写磁盘上的 PE；Launcher 启动器本身不读写客户端进程内存。

客户端目录中的 `Config.ini` 提供以下常用设置：

```ini
[Resolution]
Width=800
Height=600

[Rendering]
ScaleFilter=native

[Features]
EnablePerformanceOverlay=1
EnableMouseWheel=1
```

窗口大小只改变外层缩放，内部渲染保持 `640×480`。`ScaleFilter` 支持 `native`、`point`/`nearest`、`linear` 和 `anisotropic`。两个功能开关分别控制性能信息覆盖层与鼠标滚轮支持，设为 `0` 关闭。修改后重启客户端生效。

## 测试

运行主要业务冒烟测试：

```powershell
dotnet run --project .\tests\DFLegacy.SmokeTests\DFLegacy.SmokeTests.csproj -c Release
```

## 许可证

源码使用 **BSD Zero Clause（0BSD）许可证**，详见仓库根目录的 `LICENSE`。该许可证不授予客户端或其他第三方资源的使用与分发权利。
