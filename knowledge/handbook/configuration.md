# 配置来源、优先级与修改方法

用途：确认“改哪个文件才会生效”，以及配置与章别/运行数据的关系。部署步骤见 [运维](operations.md)；完整声明定位见 [configuration-properties.tsv](reference/configuration-properties.tsv)。

## 配置分层

| 对象 | 文件/入口 | 代码依据 |
| --- | --- | --- |
| 游戏服务 | `server.json` 的 `DFLegacy` 节；可用 `--config` 指定绝对路径 | [HostingArguments](../../src/DFLegacy.Server/HostingArguments.cs)、[Program](../../src/DFLegacy.Server/Program.cs)、[ServerOptions](../../src/DFLegacy.Server/ServerOptions.cs) |
| 部署管理器进程 | `--root`、`--data-dir`、`--listen`、`--port`、`--no-browser`、`--allowed-hosts` | [ManagerHostOptions](../../src/DFLegacy.Manager/ManagerHostOptions.cs) |
| 管理器保存的部署设置 | 管理器数据目录下的配置；读取 saved/effective 差别 | [ConfigurationStore](../../src/DFLegacy.Manager/ConfigurationStore.cs)、[ManagerSettings](../../src/DFLegacy.Manager/ManagerSettings.cs) |
| 桌面登录器 | ACT1 为程序同目录 `launcher.json`；内嵌第二章为 `launcher.chapter2.json`，由 `Program.LauncherConfigPath` 选择；另有选中服务器/账号和启动参数 | [LauncherOptions](../../src/DFLegacy.Launcher/Models/LauncherOptions.cs)、[Program](../../src/DFLegacy.Launcher/Program.cs) |
| 游戏 INI/显示设置 | `Config.ini` 等客户端自身配置，按章别转换 | [IniConfiguration](../../src/DFLegacy.Launcher/IniConfiguration.cs)、[ChapterTwoGameSettings](../../src/DFLegacy.Launcher/ChapterTwoGameSettings.cs) |
| 诊断上报 | 登录器 `Diagnostics` 对象与当前用户 `%LOCALAPPDATA%/DFLegacy/Diagnostics/preference.json`；已保存用户偏好优先于复制来的 JSON 默认值 | [DiagnosticSettings](../../src/DFLegacy.Diagnostics.Core/DiagnosticSettings.cs)、[AutomaticDiagnostics](../../src/DFLegacy.Launcher/Diagnostics/AutomaticDiagnostics.cs) |
| 独立中继 | CLI 参数而非 `server.json` | [RelayOptions](../../src/DFLegacy.Relay/RelayOptions.cs) |
| 容器 | `.env` 变量经 Compose 映射成进程参数/环境变量 | [.env.example](../../.env.example)、[compose.yaml](../../compose.yaml) |

服务端在 JSON **之后**注册环境变量，因此 `DFLegacy__GameplayDatagram__AdvertisedHost` 等环境变量覆盖 JSON。即使 JSON 启用了 `reloadOnChange`，启动时取得的 `ServerOptions` 对象和已打开的监听器也不能据此当作完整热重载；改变服务配置按管理器保存/重启流程验证。

Manager 命令行值优先于对应 `DFLEGACY_MANAGER_*` 环境变量，最后使用代码默认值。保存值与环境覆盖后的 effective 值可能不同；网页显示没变时先查覆盖来源，避免反复改文件。

**第二章 Manager 有例外。** [ConfigurationStore](../../src/DFLegacy.Manager/ConfigurationStore.cs) 在第二章专用 profile 下清空通用部署环境覆盖；[ServerSupervisor](../../src/DFLegacy.Manager/ServerSupervisor.cs) 启动子进程时先移除继承的 `DFLegacy__*`、`DFL_S1C2_*`，再根据章别、数据目录和远端模式重建环境。域名模式只额外透传已验证的 `EnableTestCeraTopUp`、`Channel__RegionalChannelsEnabled`、`Fatigue__WeekendDoubleEnabled` 三个业务开关。不要把直接启动 Server 的覆盖规则照搬到第二章 Manager；核对 `DFLEGACY_CHAPTER_TWO_*` 专用设置与最终子进程配置。

## 服务端关键节

| 节/字段 | 含义与易错点 |
| --- | --- |
| `DataPath` | 游戏持久化快照路径；是文件路径。单个可写快照只能由一个游戏服务拥有 |
| `ScriptPvfPath` | 当前章别服务端 PVF；与客户端封装可能不同 |
| `SeedTestAccount` | 代码默认 `true`，发行模板明确 `false`；新部署不得只靠遗漏字段去掉测试账号 |
| `Admin` | 账号/API监听、启动引导管理员、登录限流和 token 时效；名称不表示所有接口仅 GM 可用 |
| `WebPanels.Gm` / `Player` | 分离 GM 与玩家页面监听；开启时需要与其他 TCP 端口不冲突 |
| `Entrance`、`CharacterDatagram` | 游戏 TCP 入口与角色 UDP；同数字的 TCP/UDP 是不同监听 |
| `GameplayDatagram` | 玩法 UDP 双端口；`Host` 是绑定地址，`AdvertisedHost` 是告诉客户端使用的地址 |
| `Channel` | 频道端口、名称、容量及地区频道开关；`Host` 只是拨号地址的末级回退，并非唯一对外地址来源 |
| `Processes` | 可选独立频道进程和主进程入口绑定控制，不等于 Manager 进程设置 |
| `FallbackRelay` | 游戏组队 TCP/UDP 回退中继，和客户端本地 IPv6 中继是不同层 |
| `Pvp` | 决斗入口和频道规则，不能随意占用进程间端口 |
| `Drop` / `Experience` / `Fatigue` | 掉落/经验/疲劳；周末疲劳刷新口径为北京时间 06:00 |
| `Debug` / `GameProbe` | 调试与测试能力；按代码明确启用，生产配置不要照抄测试夹具 |
| `EnablePacketTracing` / `EnableConnectionDiagnostics` | 逐包追踪与有界连接摘要，诊断范围不同 |
| `MaximumPacketLength` / `RejectBadCrc32` / `HexDumpLimit` | 帧限制和日志控制；不能靠取消长度/校验来掩盖协议错误 |
| `ChapterTwoRemote`、`ChapterTwoLocalTestPaymentsOnly` 等 | 第二章部署/测试策略；只在对应章别链路中解释 |

子字段及默认值在 [ServerOptions](../../src/DFLegacy.Server/ServerOptions.cs)、[ProcessOptions](../../src/DFLegacy.Server/ProcessOptions.cs)、[PvpOptions](../../src/DFLegacy.Server/PvpOptions.cs)、[FallbackRelayOptions](../../src/DFLegacy.Server/FallbackRelayOptions.cs)。参考表保留声明原文，但实例级初始化、转换和 Validate 方法仍以代码为准。

## 登录器关键字段

- `ClientPath`：要运行的客户端；记录最终解析路径，不只记录配置里的相对路径。
- `EntranceHost/EntrancePort`、`Servers/SelectedServerId`：服务器选择。`AccountServiceUrl` 非空时覆盖自动拼接的账号地址，否则由入口主机和 `AccountServicePort` 组成。
- `Accounts/AccountIndex` 和旧 `Account/Password`：用户私有数据，禁止复制进示例或日志；迁移时保留未知扩展字段。
- `StartServer`：是否本地启动服务；远端玩家配置不要沿用开发默认值。
- `Relay.Enabled/TcpPorts/UdpPorts/Preference`：本地网络中继；默认地址族偏好 `ipv6`。
- `UpdateProvider/UpdateRepository/UpdateChannel/UpdateFlavor/UpdateServiceUrl` 等：更新源与渠道身份，不能随意切源绕过清单安全检查。
- `ChapterTwoServerHost`：第二章域名；旧 `ChapterTwoServerIpv4` 与新主机配置按迁移流程处理，不同时塞互相矛盾的值。
- `ChapterTwoDisplay/Renderer/PostProcess/Dialogue`：第二章显示和文本；允许值及规范化在对应类中，字体尺寸当前支持 12/14/16/18，默认 16，行距默认 3。
- `ChapterTwoDebugEnabled/ChapterTwoTestMuteEnabled/Diagnostics`：调试、测试静音、诊断是不同控制项。

## 地址不要混淆

绑定 `0.0.0.0` 表示接收所有 IPv4 网卡，不能把它作为玩家实际连接地址。绑定 `::` 的双栈行为还取决于具体监听实现和系统。对外地址可能是公网 IP、域名，或在本地中继架构中刻意下发的 loopback；不能凭“127.0.0.1 看起来不对”统一替换。

频道下发地址的实际优先级为：可拨的 `GameplayDatagram.ResolveAdvertisedHost()` → 可拨的 `Entrance.Host` → `Channel.Host`，见 [EntranceDialHost](../../src/DFLegacy.Protocol/EntranceDialHost.cs) 和 [EntranceService.ResolveChannelDialHost](../../src/DFLegacy.Server/EntranceService.cs)。玩法监听绑定使用 `Entrance.Host`。因此仅修改 `Channel.Host` 未必改变玩家实际连接目标；最后回退也不是完整的地址有效性校验。

第一章端口示例读 [通用部署](../../DEPLOYING.md)，第二章读 [remote compose](../../deploy/chapter-two/remote/compose.yml) 和 [第二章说明](../../deploy/chapter-two/remote/README.md)。对修改后的配置做端口冲突检查，并分别验证账号 HTTP、选频道 TCP、玩法 UDP 与回退链路。

## 安全修改步骤

1. 确认正在运行的进程实际读取哪份配置、数据目录及环境覆盖。
2. 备份目标配置；脱敏记录变更前后值和章别。
3. 只改目标字段，保留账号、未知扩展数据和无关设置。
4. 按该配置的加载机制重启自己负责的实例，检查解析错误与监听日志。
5. 用真实访问路径验证，不以“JSON 可解析”代替运行正确。
6. 把新增字段同时补入代码默认值、适用模板、管理页模型/转换（如有）、测试和本页；无需向不适用章别硬加字段。
