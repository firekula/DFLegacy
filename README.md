# DFLegacy

2026-10-30增量：[修复N地图副本门前区域无法右键寻路](knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)。控制器支持入口城镇区域作为最终目标，传送门及借道保护保持；单客户端新增场景与620项／569子测试通过。新DLL已加载至当前手测客户端，正常存档保持。

当前源码／部署／剩余项总入口：[2026-10-30交接](HANDOFF-2026-10-30.md)。各阶段报告保留当时构建归属；源码归档与日常部署分别记录。

2026-10-30本轮U05：[资源数据、CLI与交接](knowledge/decisions/2026-10-30-u05-resource-closeout.md)补齐有界使用次数及物品成本门，新增独立资源字段。最终2D构建已四端实载，13条实机CLI匹配、4条未知等待超时，保留4张原图。Python620项／569子测试及相关原生、overlay/ABI通过；14文件恢复、8保护项保持，日常包未覆盖。全局输入、密码PvP依赖和未证特殊分支仍明确保留，U07／GP42及U08未处理。

2026-10-30前阶段U05接续：[鼠标门与八类弹窗](knowledge/decisions/2026-10-30-u05-mouse-dialog-followup.md)补齐type1早期物理门、按钮状态暂缓和显式弹窗覆盖。四端新增07/09开关、自然门观测及24条实机CLI通过，另16条仅快照回放，保留5张原图。完整Python617项／553子测试、相关原生及overlay/ABI通过；最终B4构建已四端实载，14文件恢复、8保护项保持。全局输入阻断、56密码窗实机、卡顿及同步／GP42保留，日常包未覆盖。

2026-10-30前阶段U05：[队友／城镇MP、键盘门和弹窗层级](knowledge/decisions/2026-10-30-u05-mp-input-dialog-verification.md)已完成限定四端验收，保留5张关键原图。完整Python613通过，另补原跳过夹具1项通过；原生回归通过。14个临时文件恢复、8项保护哈希保持；卡顿及同步／GP42继续保留。最终收窄MP身份守卫经竞争回归，未重做实机矩阵，两个构建哈希见报告；日常发布包未覆盖。以下保留各阶段历史。

2026-10-30后续源码进展：[三个剩余阻断](knowledge/decisions/2026-10-30-remaining-blockers-followup.md)。第四人邀请按原生旧单人队流程闭环；测试瞬移完整发布、原生普通插值旧时限分支已修。U08卡顿和U07自然大差／GP42继续阻断，四端首领仍捕获421px级差。Python586通过／1条件跳过／445子断言，完整IJL及相关debug原生测试无失败。所有测试进程关闭、12个客户端文件恢复，正常存档保持；本轮改动未覆盖日常发布包。下方保留夜间交付及各阶段历史状态。

2026-10-30夜间收尾：[最终报告](knowledge/decisions/2026-10-30-overnight-restoration-report.md)。已验证模块已[更新到日常目录](knowledge/decisions/2026-10-30-normal-delivery-verification.md)，四端启动／跨区／组队通过，原角色存档保持；完整Python574通过、2跳过、413子断言、0失败。U07／U08及回城第四人再邀请仍阻断保留。

2026-10-30：[U11死亡队员地面清除](knowledge/decisions/2026-10-30-dead-ground-removal-verification.md)已修复并四端验证，同房白装／真实换房返回蓝装／金币通过，5张原图；U07／U08及再邀请的原生生命周期阻断仍明确保留。

2026-10-30：[技能栏同档持久化](knowledge/decisions/2026-10-30-skillbar-persistence-verification.md)两轮实际编辑／重登／服务端重启通过，补逐包保存和测试种子来源／延续。U11最后接续。

2026-10-30：[超重协议复核](knowledge/decisions/2026-10-30-overweight-verification.md)确认实际2.526kg，无需额外单位修复。

2026-10-30计时增量：新增默认关闭的debug回调计时与CLI，记录各子区间、前台覆盖和调用间隔。四端两阶段发现51／69ms未归因间隔，已计时工作峰值<0.3ms，不能宣称全游戏无卡顿。见[计时数据及原图](knowledge/decisions/2026-10-30-frame-profile-verification.md)。

2026-10-30死亡观测增量：仅测试地面／库存可在死者脱离地图后继续更新，按当前stage及观察者身份核对；修改仍要求存活。见[四端新鲜采样及两张原图](knowledge/decisions/2026-10-30-dead-observation-verification.md)。

2026-10-30界面增量：debug v2可只读消费选择副本scene2，投影实际副本ID／难度及原生过渡状态，并读取五类常用弹窗的原生根标志。保留未知与操作场景限制，见[四端加载、组队窗／消息窗及3张原图](knowledge/decisions/2026-10-30-ui-state-verification.md)。

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
| [CHANGELOG.md](CHANGELOG.md) | 里程碑、功能变更和验证范围。 |
| [副本调试面板](tools/debug/README.md) | 独立 debug.dll 的配置、构建、浮动面板、掉落测试开关与自动控制。 |
| [城镇地图与状态接口验收](knowledge/decisions/2026-10-29-debug-town-navigation-verification.md) | N 地图同区点击移动、debug 目的地、结构化状态与双端原图。 |
| [测试 CLI 与四人掉落验收](knowledge/decisions/2026-10-29-test-control-four-client-loot-verification.md) | 仅测试环境的定位、指定敌人死亡、掉落隔离与权威结果查询，四端矩阵及7张原图。 |
| [ADMIN_API.md](ADMIN_API.md) | 后台管理 HTTP 接口参考。 |
| [DEPLOYMENT_MANAGER.md](DEPLOYMENT_MANAGER.md) | 跨平台部署管理器、首次设置、客户端包导出及验证范围。 |

## 本次更新

- 新增七个 ACT1 地区频道。通关结算按所选频道读取 PVF 地区经验比例，启用方法见[地区经验频道](DEPLOYMENT_MANAGER.md#地区经验频道)。
- 恢复仓库升级按钮。实机点击已打开购买确认框，仓库扩容沿用商城购买流程。
- 服务端发布至 GHCR。管理后台可生成 x86 和 x64 玩家包。登录器按架构和普通／测试类型从 GitHub Releases 自动更新。见[部署指南](DEPLOYING.md#客户端自动更新)。

## 一键部署与自动发行

新增 `DFLegacy.Manager`，Windows、Linux、macOS 与 Docker 共用中文 Web 管理页面。本机启动管理器会打开浏览器；管理器与游戏服务分别运行，停止游戏服务后仍可修改配置、查看日志、初始化游戏管理员及导出客户端补丁/登录器。

- 发行包按 `win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64` 自包含发布。Windows 运行 `Start.cmd`，Linux 运行 `./start.sh`，macOS 运行 `Start.command`；游戏客户端仍为 Windows 程序。
- 首次设置凭据只使用一次，保存在数据目录 `management/setup-token.txt`；双击启动时由本机浏览器自动携带。管理账号与游戏账号分开，发行模板不生成 `test/test`。
- 服务端配置、存档、日志保存在独立 `data/` 目录。升级先停服、备份数据，将新版本解压到新目录，再复用数据目录；不要直接覆盖正在运行的安装。
- 各平台和容器都使用同版本预构建的 Windows 客户端资源导出 ZIP，包含空凭据登录器、IJL15、配置和摘要。游戏内充值/黑钻页面保留 460×315 布局，GM 后台和玩家中心继续使用普通浏览器。
- Gitea 在提交后运行源码测试。服务端 Docker 镜像与双架构客户端更新使用 GitHub 分发，见 [构建与发布](BUILDING.md#源码与产物发布)。

这些是新增部署能力；真实客户端跨机进图、macOS 原生运行和远端 CI 发布的验收状态，见 [部署管理说明](DEPLOYMENT_MANAGER.md)。

容器部署使用仓库根目录的 `Dockerfile`、`.dockerignore` 与 `compose.yaml`，说明见 [DEPLOYING.md](DEPLOYING.md#5-容器部署docker--podman)。

## 模块说明

| 模块 | 作用 |
| --- | --- |
| `DFLegacy.Server` | 主业务、PVF 数据读取、账号角色持久化、TCP/UDP 服务及 HTTP 管理接口。 |
| `DFLegacy.Protocol` | 登录与通信协议的编解码、请求解析、响应与通知包构建。 |
| `DFLegacy.Relay` / `DFLegacy.Relay.Core` | 本机 TCP/UDP 端口中继：让只支持 IPv4 的原版客户端经本机连接 IPv6 服务器。核心库由启动器在自身进程内托管，独立可执行文件供手动运行。见 [DEPLOYING.md](DEPLOYING.md#64-让-ipv4-客户端连接-ipv6-服务器dflegacyrelay)。 |
| `DFLegacy.Launcher` | Windows 启动器：WPF 交互式登录壳，含注册/登录/改密/找回/账号管理/配置；检查运行环境、验证独立客户端包并通过内嵌 x86 引导器启动客户端，可按需启动本地服务端。`--headless`/`--account` 仍支持双击即玩。 |
| `DFLegacy.AccountClient` | 启动器使用的账号 HTTP 客户端（注册、改密、找回密码、健康检查）。 |
| `DFLegacy.Client` | 独立 x86 客户端补丁 `DFLegacy.Client.dll` 与内嵌引导器；普通版和测试版共用完整游戏优化核心。 |
| `DFLegacy.Client` / `DFLegacy.Ijl15` | 独立 32 位 `DFLegacy.Client.dll` 提供进程内兼容补丁和窗口缩放；原版 IJL 保留，旧 `ijl15.dll` 构建用于兼容回归。 |
| `DFLegacy.Debug` | 可选的 32 位 `debug.dll`，由 IJL15 延迟加载；提供浮动面板、结构化状态、城镇寻路及组队操作接口。另有默认关闭、需新鲜服务端租约的隔离测试控制。 |
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
| `DFLegacy.Debug`（调试覆盖层） | 支持 | 不支持 | 不支持 | 测试版默认本地诊断；仅服务端覆盖数据需共享本机快照目录 |
| `DFLegacy.RandomNative`（64 位随机桥） | 支持 | 不支持 | 不支持 | 可选；缺失或不可用时自动回退到托管随机 |
| 游戏客户端 | 支持 | 不支持 | 不支持 | 原版 32 位 Windows 客户端 |

因此服务端可以部署在 Linux 或 macOS 上、也可以放进容器，由 Windows 客户端远程连接；构建方式见 [BUILDING.md](BUILDING.md)，部署与网络配置见 [DEPLOYING.md](DEPLOYING.md)。

## 当前实现

### 2026-10-29：测试接口、四人掉落与城镇操作

2026-10-30技能增量：debug v2现有字段支持`cooldown_ms`、`usable`，另有`cooldown_blocked`区分持续禁用；按原生时钟／上下文／资源预检读取，未知不冒充可用。见[跨两职业施放、跳跃与死亡原图](knowledge/decisions/2026-10-30-skill-state-verification.md)。

2026-10-30后续：新增测试专用 `player kill --uid`（2～4人组队副本中的本机角色），独立核对原生／服务端／各端死亡；新增副本本地MP及上限，正常技能消耗和恢复已核对。见[5张原图、测试及适用范围](knowledge/decisions/2026-10-30-player-kill-mp-verification.md)。U11已按最新要求移至队列最后。

2026-10-30增量：测试CLI增加原生背包 `inventory drop` 及独立库存观测；四端完成蓝装、封装紫装四向交接、材料部分丢弃、金币均分和换房返回后的唯一保存。见[5张原图、测试与精确边界](knowledge/decisions/2026-10-30-active-drop-four-verification.md)；只部署隔离测试目录，普通配置默认关闭。

- 新增仅测试环境 CLI：查看状态、当前副本角色定位、调用原生指定怪物死亡入口、隔离现有掉落、查询真实提交／拒绝。客户端与服务端均需显式开启，普通配置默认关闭；原生回执与服务端保存分别核对。见[接口说明](tools/debug/README.md)及[实机记录](knowledge/decisions/2026-10-29-test-control-four-client-loot-verification.md)。
- 四客户端独立存档完成白装、蓝色材料、紫装、粉装和史诗选择矩阵：25次含金币的拾取为24次持久化提交、1次正确超重拒绝；四人各操作史诗“否／是／掷点”，金币23按5／5／5／8入账。使用明确的服务端 MOB 必掉夹具，不代表原始自然掉率或蓝色装备已验；7张原图及精确槽位／实例见上述记录。
- N城镇地图已支持客户端内跨区规划，仅右键有效空白点设置目标，黄色扩散反馈连续两次；debug单目标接口无需分段控制。双端跨区、回城后的再次寻路及手动打断已验，见[右键反馈](knowledge/decisions/2026-10-29-map-right-feedback-verification.md)与[回城验证](knowledge/decisions/2026-10-29-town-return-perf-verification.md)。
- 副本介绍超时残留已[双端修复验收](knowledge/decisions/2026-10-29-dungeon-tooltip-verification.md)。主动丢弃的白装、金币、消耗品、材料已由用户确认队友可见／可拾取及金币平分，蓝装与扩展边界仍按[列表](knowledge/decisions/2026-10-28-restoration-backlog.md)接续。
- 普通自动操作默认不截图，关键验收显式请求，避免已确认的隐式截图短停顿；测试启动四窗按A/B、C/D排列。见[停顿对照](knowledge/decisions/2026-10-29-operation-stutter-verification.md)及[布局工具](tools/acceptance/README.md#四客户端窗口排列)。怪物位置同步、其余卡顿和技能栏持久化没有因此关闭。

### 2026-10-28：倒计时复活币与后续列表

- 修复ACT1隐藏单币购买成功后的“未知错误”展示及价格标签，保持官方购币发放与CMD44消费复活分开。最终19C双端完成队员已有币复活、队员购币复活、队长购币复活及普通10币礼包回归；正式目录与原始配置核验完成，见[9张原图与验收范围](knowledge/decisions/2026-10-28-countdown-coin-purchase-verification.md)。新增[后续列表](knowledge/decisions/2026-10-28-restoration-backlog.md)：副本介绍残留、四人掉落/史诗选择、主动丢弃可见性及debug.dll状态接口。heal1024为6级，符合官服17级及以下退出免虚弱规则，无需修改。

### 2026-10-28：洛兰 3×3 Boss 专用选图

- 补读每套迷宫的 `[boss map specification]`，所选Boss坐标优先采用有效专用候选；洛兰(2,2)现选择MAP6416与魔笛使者1077。保持起点、任务替换、缓存及无有效配置时的原有规则。完整冒烟、真实PVF回归和严格Release通过；双客户端实际进入、自然击杀、双方结算回城及正常目录交付完成，见[8张原图及验收范围](knowledge/decisions/2026-10-28-boss-map-specification-verification.md)。怪物位置同步仍在排查。

### 2026-10-28：组队 TCP 回退

- 补全 ACT1 登录回退入口与真实 peer 账号，按原生 12 字节中继协议转发；修复持续 TCP 流量暴露的原生发送队列与生命周期竞争。正常 UDP、受控 UDP 失败后的双向 TCP、自然通关回城及双端频道重入已验，见含 12 张原图的[验收记录](knowledge/decisions/2026-10-28-party-tcp-fallback-verification.md)。实测构建已交付，原配置和存档保持；怪物位置差异仍单独排查。

### 2026-10-28：频道重入与普通商城赠送

- 修复设置中返回频道后再次进入的约11秒假死：同一连接环境复用实际探测过的MTU，并刷新原生主机引用；首次或网络身份变化仍走原探测。双端最终构建的登录应答至继续请求间隔为0–1毫秒，组队进副本、战斗和换房回归通过，见含原图和计时依据的[频道验收记录](knowledge/decisions/2026-10-27-channel-return-verification.md)。该间隔不代表完整进城耗时。
- 按官服业务规则、ACT1协议恢复收礼人查询和普通商城实物赠送，点券扣费、收礼邮件及账号月次数原子保存，恢复原生“全部赠送”入口。双客户端完成在线/离线赠送、领取、限额与两次重启验证，见18张原图的[赠送验收记录](knowledge/decisions/2026-10-27-cera-gift-verification.md)。时装、宠物、礼包和直接效果商品不在本轮范围。
- 完整.NET冒烟、Win32兼容测试及独立Release构建/发布通过；同一实测版本已部署，原配置与原存档哈希不变。升级需更新服务端和两端及启动器旁 `ijl15.dll`。

### 2026-10-27：商城限时时装

- 恢复ACT1原生7天、30天、无限制选择和相应计价；新增服务端绝对截止，穿脱、系统邮件领取和重启不续期，到期原子回收并刷新本人装备及对端外观。合成保持官服期限类型、5秒门槛及剩余秒相加规则。
- 完整.NET冒烟、Win32原生兼容测试和Release发布通过。双客户端完成六档购买、期限提示、在线回收/外观刷新及离线重启清理，附18张真实截图的[验收记录](knowledge/decisions/2026-10-27-avatar-periods-verification.md)标明自动化与原生未验范围。同一实测构建已部署，5份原配置与原档路径恢复、原档哈希不变，两端正常停在频道选择；升级需同步服务端和两端及启动器旁的 `ijl15.dll`。

### 2026-10-27：普通摆摊

- 接通 ACT1 CMD85–90 与 NOTI78–85，恢复建摊、陈列、进摊、购买、退出、收摊及同区后来者重放。普通店按官服开放7格，手续费为毛额4%向下取整；商品和售款信用原子持久化，断线、换区和重启回收余货。
- 需要账号活跃服务8商人许可；城镇 W 入口通过 ACT1 mode0 许可标志开放。原生商城商品100022（物品16）为380点券、30天；管理员可通过[Premium 接口](ADMIN_API.md#63-post-apiclient-paypremium)授予任意正整数天数。
- 完整协议冒烟与 Release 发布在 `e494483` 通过。双客户端已验证材料10个分4+6购买、首轮撤摊返还6个，以及24金币装备的强化+3、品质种子37、耐久19转移，161项验收断言通过。ACT1售罄后自动发送CMD89；其旧文案14格/1%与本轮官服7格/4%规则存在版本差异，详见[验收说明](knowledge/decisions/2026-10-27-private-store-verification.md)。同一实测构建已部署，原配置和原存档路径已恢复，原档哈希不变。

### 2026-10-27：师徒基础关系

- 接通 ACT1 师徒申请、接受/拒绝、解除、名单与上下线通知，按官服样本实现等级、容量、黑名单和06:00日界限制，双方关系彻底为空时清除登记限制。
- 关系原子持久化，支持重登、离线解除和删角清理；投递按会话顺序串行，防止旧连接与延迟通知覆盖新关系。完整冒烟、Release 发布和双客户端验收通过，见[验收说明](knowledge/decisions/2026-10-27-mentor-verification.md)。仅需更新服务端。
- 关系经验增长、COF、经验与疲劳奖励尚未恢复；名单保留官服初始关系等级和经验阈值。

### 2026-10-27：队伍聊天与金币均分

- 恢复 CMD17/type2 队伍聊天，包含发送者，城镇与副本使用同一当前队伍名单；ACT1 NOTI12 不带后期服务端的 ServerGroup 字节。
- 普通金币按本房合格成员均分、余数给拾取者；同房死亡仍合格，死亡后换房失去资格，复活恢复。24 字节 NOTI39 使用固定四槽实际入账金额，先分配再移除地面。
- 金币份额与去重收据原子保存，再由各成员会话安装；覆盖并发拾取、余额上限、普通库存保存交错、交易交付、写入中断和重启恢复。完整冒烟与双客户端记录见[验收说明](knowledge/decisions/2026-10-27-party-chat-gold-verification.md)。仅需更新服务端，保留配置与存档。

### 2026-10-27：组队骰子、原生确认与调试面板

- 普通物品按官服样本的来源、稀有度及独立 `[epic routing]` 参数判断分配；仅存活队员进入候选，结合背包容量、轮转和武器职业加成投骰。重复拾取、得主离队及保存失败有对应的预留与恢复处理。
- 保持 ACT1 原生接口：粉装直接投骰；本轮史诗确认窗中的“掷点”进入随机分配，“是”直接拾取，“否”取消并保留地面物品。高品质确认不需要增加客户端协议。
- 双客户端洛兰实测已覆盖蓝色材料、白色武器、粉装、史诗、确认窗三个按钮，以及同房队员死亡后被排除；分数、获奖背包和双方地面移除均已核对。详情见[实机验收记录](knowledge/decisions/2026-10-27-loot-live-verification.md)及[官服规则与版本边界](knowledge/decisions/2026-10-27-official-loot-contract.md)。
- 浮动调试面板已能在游戏中显示、点击，并正确处理鼠标进入和离开；新增“掉落测试”开关和本地自动控制命令，收到服务端确认后才显示成功。仅 `test` 账号可操作，状态作用于本局，换房保留，新一局重置。配置见[调试说明](tools/debug/README.md)。

升级需配套更新服务端、`debug.dll` 与 `ijl15.dll`，同步启动器目录中的 DLL 副本并重启服务端和客户端；保留现有配置和存档。后期官服的全队逐人确认及幸运点生成仍未移植；普通金币均分见上文后续实现。

### 2026-10-24 后续修复：私人房间、双开流畅性与翻牌显示

- 五个城镇的赛丽亚房间固定按玩家隔离，只显示自己；额外私密区域仍可通过 `TownSync.PrivateAreas` 配置，普通街道继续共享。
- 取消客户端失去焦点后的主循环休眠与进程降优先级。双开时后台客户端继续正常更新；用户完整副本手测确认队友动作恢复流畅，同步频率和插值参数保持原样。
- 修正组队免费翻牌显示：按卡片归属展示对应成员的金币与装备，实际发奖逻辑保持原样；双客户端手测已确认不同成员的奖励显示正确。

升级时同时更新服务端与两端的 `ijl15.dll`，并更新各启动器目录中用于覆盖安装的 DLL 副本，然后重启服务端和客户端。保留现有 `server.json`、`Config.ini` 和存档。验证范围及待办见 [CHANGELOG.md](CHANGELOG.md) 和 [STATUS.md](STATUS.md)。

### 2026-10-24 里程碑：双客户端组队副本闭环

双客户端实测已确认：圣职者缓慢愈合对队友及自身生效，双方可见特效；BOSS 击败后在场小怪正常死亡、产生掉落，再开启通关结算；结算后打破藏怪箱子，新怪立即死亡且不重复结算；一端拾取后双方地面物品同步移除，奖励只入账一次。

此前已验收独立 `debug.dll` 文字覆盖层，可查看本轮 BOSS 房箱子或地堡隐藏怪、容器坐标、怪物数量及释放/死亡状态。**F9** 收放详情，**F10** 隐藏/显示。浮动面板的后续修复与验收见上方 2026-10-27 记录。普通发行配置默认关闭调试和自动输入。开启调试时，服务端提前生成并缓存 BOSS 房出怪表，实际进房复用同一份数据；这会提前消耗本轮随机数，概率规则不变。配置与构建见[调试说明](tools/debug/README.md)，变更与验证范围见 [CHANGELOG.md](CHANGELOG.md)。

### 功能概览

| 系统 | 已实现内容 |
| --- | --- |
| 账号与角色 | 注册、登录、改密与找回密码；角色创建、选择及状态保存；支持多账号；点券、契约及黑钻等服务按账号管理。 |
| 后台权限 | 8081 管理接口需 Bearer token 登录，账号分「玩家 / 管理员」两档并持久化角色：玩家只能查自己的角色，管理员可发邮件、广播、充值、改副本难度、提升/降级他人。登录失败按账号+来源 IP 限流，角色变更即时吊销旧 token。 |
| 角色成长 | 怪物经验、通关经验及结算字段；SP 随当前等级的经验进度渐进发放，PVF 缺少 `etc/sptable.etc` 时回退到与原版一致的内置 SP 表；经验书、升级券、属性类道具；技能学习、重置、转职与觉醒相关处理。 |
| 副本 | 进入条件、疲劳消耗与恢复、虚弱及定时恢复、难度解锁；房间切换与重访状态保存；精英怪、深渊入口与相关掉落处理。 |
| 组队副本 | 共享副本实例、成员进场与跟图、房间状态同步、队员详细状态、死亡与掉落、翻牌及返回城镇；双客户端已覆盖治疗、BOSS 清场、通关后箱怪、拾取同步、后台动作流畅性和免费翻牌显示。 |
| 掉落与奖励 | PVF 掉落表、怪物独立掉落、精英专属掉落及任务掉落；组队活人骰子分配与 ACT1 高品质原生确认；地面移除、单次入账及负重失败恢复；翻牌结算处理。 |
| 任务 | 接取、进度、完成与奖励；接取时给予任务物品；击杀、收集、普通及带条件通关等已适配条件；清除地图任务激活时，其救援图会替换当次副本的 boss 房并按 boss 房结算；隐藏副本与职业相关任务处理。 |
| 物品与装备 | 背包、快捷栏、仓库、丢弃、拾取、商店买卖；非堆叠装备 UUID、强化、品级、封装状态、再封装、分解与配方制作；商店每日统一品级种子。 |
| 普通摆摊 | 服务8商人许可、7格普通背包商品陈列、进摊和部分购买、4%手续费、余货收回与离场清理；持久托管及售款信用，双客户端材料与装备交易已验收。 |
| 商城与装扮 | 商城购买、账号点券扣除、契约及兑换券直接兑现；普通实物赠送、收礼人查询、账号月限额及邮件领取；礼包购买时打开、随机礼包抽选及手动开启；装扮发放、限时回收与合成。 |
| 宠物 | 宠物与宠物装备、经验升级、饱食度变化与自动喂食、进化任务相关处理、死亡与复活、脚本消息及喊话请求处理。 |
| 邮件与通知 | 邮件列表、附件领取及保管箱相关处理；普通物品、装备、装扮和宠物蛋附件；指定角色或在线广播的消息通知与自定义弹窗。 |
| 决斗数据 | 决斗经验表读取、决斗经验书、等级计算及记录同步。 |
| 城镇同屏与附近聊天 | 同一城镇同一区域内的在线角色互相可见（名字、装备、宠物、黑钻标记）并能看到走动；普通聊天与宠物喊话按区域互通。五城赛丽亚房间为各玩家的独立实例；其他共享区域仅在**同镇同区**互通。离开区域的移除信号与位置通道仍在真机验证中。 |
| 好友与黑名单 | 账号级好友名单（上限 32，加/删为单向、立即生效）；好友列表与在线状态、上下线提醒（离线即时灰显并保留"[X]已退出游戏"提示）；角色间私聊（悄悄话）与 1:1 聊天；账号级黑名单（上限 10，拉黑/解除/名单窗口，悄悄话与 1:1 双向拦截）；查看他人角色信息（好友行与城镇右键路人，含形象/装备/等级/经验）。离线好友申请、备注/分组、后台管理面未实现。 |

### 后台管理

默认入口：<http://127.0.0.1:8081/admin/index.html>（端口由 `server.json` 的 `Admin.Port` 决定）。页面**需要登录**，账号分「玩家 / 管理员」两档：玩家只能看自己的角色（另有独立玩家页 `/admin/player.html`），管理员才有下面这些 GM 功能。

- 给指定角色发送邮件，可附金币和一项物品附件，区分普通物品、装扮与宠物蛋。
- 给在线角色发送消息或弹窗，或向当前在线角色广播；消息支持类型选择。
- 设置角色可进入的副本最高难度。
- 查看角色在线状态，从已加载 PVF 中搜索物品和选择副本。
- 「用户角色」页：查看全部账号并提升/降级管理员（不能降级自己，也不能把最后一名管理员降级）。

**第一个管理员怎么来**（部署后必做一次）：管理员只能靠 `server.json` 的 `Admin.Users`（容器里是环境变量 `DFLegacy__Admin__Users__0`，多个依次 `__0`/`__1`）在**服务端启动时**被提升——注册不会自动获得管理员权限（否则谁抢注了名单里的名字谁就是管理员）。所以顺序是：

1. 先注册你要用的账号（启动器的「注册账号」，或公开接口 `POST /api/accounts`，账号名 4-32、密码 6-128、QQ 码至少 5 位）；
2. 把该账号名写进 `Admin.Users`；
3. 重启服务端使提升生效（容器请用 `docker compose up -d` 重建：`docker compose restart` 不会读取新的环境变量）；
4. 启动日志出现 `bootstrap admins: <你的账号>` 即成功；之后再加管理员直接在「用户角色」页操作，不需要重启。

⚠️ 名单里**不要放默认的 `test`**——它密码公开，进了名单等于给所有人管理员权限。后台端口若对外暴露，**必须限制来源并在前面挂 HTTPS**（token 走明文 HTTP 可被链路嗅探），详见 [ADMIN_API.md §1.2](ADMIN_API.md#12-鉴权与权限) 与 [DEPLOYING.md](DEPLOYING.md)。

## 已知缺口

- **多人验收范围**：当前里程碑以同机双客户端组队副本为依据；更多人数、跨机器和长时间运行仍需扩展验收，具体边界见 [STATUS.md](STATUS.md)。城镇同屏与附近聊天只在同镇同区生效。
- **组队待排查**：TCP 回退已完成[有界验收](knowledge/decisions/2026-10-28-party-tcp-fallback-verification.md)；新鲜双端只读观察仍有单只怪物持续位置偏差，继续核对 ACT1 对象选择及消息应用链，尚未宣称根因或位置修复。
- **调试浮窗验收边界**：窗口模式下的显示、鼠标进出、真实点击及双端开关状态已验收；拖动、缩放和圆点恢复已有原生测试覆盖，独占全屏等场景仍需实机扩展验收。
- **玩家交易** 已支持物品、金币及双方确认流程，范围与限制见 [STATUS §2.5](STATUS.md)。
- **私人商店扩展** 普通摆摊已实现；时装、宠物、仓库/穿戴来源、店铺人偶与拍卖代理不在本轮范围。
- **社交与公会** 好友、黑名单与"查看他人角色信息"已实现（见「当前实现」，经六轮双客户端实测）；好友申请/备注分组与完整的公会系统尚未实现；战场、公会战等玩法尚未实现和验证。
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
| `FallbackRelay` | ACT1 组队原生回退中继，默认 TCP/UDP 7005；可关闭，绑定及下发 IPv4 地址分别配置。与启动器端口代理独立。 |
| `GameplayDatagram.AdvertisedHost` | 下发给客户端的玩法地址，留空时回退为 `GameplayDatagram.Host`；容器与 NAT 部署必填，详见 [DEPLOYING.md](DEPLOYING.md#61-监听与下发地址)。 |
| `TownSync.PrivateAreas` | 额外私密区域列表，格式为 `城镇:区域`，默认 `[]`。五城赛丽亚房间 `1:1`、`2:5`、`3:2`、`4:1`、`5:1` 始终隔离，无需填写。 |
| `Drop.Enabled` | 服务端掉落总开关。 |
| `Drop.RatePercent` | 普通掉落倍率百分比，`100` 使用 PVF 原始概率。 |
| `Drop.EconomicRate` | 普通怪物/深渊相应判定的额外掉落倍率，默认 `1.0`；与概率倍率重叠时相乘，不表示金币数量倍率。 |
| `Drop.ForceDrops` | 强制掉落测试开关，正常体验应保持 `false`。 |
| `Experience.MonsterMultiplier` | 逐只怪物经验倍率，默认 `1.0`。 |
| `Experience.ClearMultiplier` | 通关基础经验及评分奖励倍率，默认 `1.0`。 |
| `EnablePacketTracing` | 协议包跟踪开关，用于排查客户端交互问题。 |

默认 TCP 端口为 `2311`、`7001`、`8080`、`7005`，管理 HTTP 为 `8081`；默认 UDP 端口为 `2311`、`7002`、`7003`、`7005`。更改网络配置时，应同步检查启动器与频道地址。监听地址与下发地址的区别、IP 地址族（IPv6）支持现状见 [DEPLOYING.md](DEPLOYING.md#6-网络配置)。

`FallbackRelay` 在游戏进程内托管 ACT1 原生 12 字节中继协议，仅向密码验证成功的频道会话下发已启动入口；用户名兼容登录不获得中继权限。转发保留原生 MT 数据，要求双方处于同队同副本实例，或同一角斗场房间及场次。`Host` 留空继承实际游戏 TCP 绑定地址，`AdvertisedHost` 留空沿用频道下发地址；局域网部署也可明确设置 `Host=0.0.0.0` 和客户端可达的 IPv4 地址。同一主机开多个游戏进程时，应为每个进程配置独立中继端口。已完成本地双客户端组队故障回退验收；PvP 故障回退、跨机器与互联网/NAT 覆盖仍未实测。

### 启动器

`launcher.json` 支持 `Accounts` 账号列表及 `AccountIndex`；也可以使用 `--account-index N` 或 `--account NAME` 临时选择已配置账号。默认测试凭据为 `test/test`，请勿将包含真实密码的配置公开分发。启动器无参运行时打开**按 DNF2008 原版登录器 1:1 还原的皮肤窗口**（左侧账号列表 + 账号/密码输入 + 服务端与账号服务状态 + 原版按钮），注册、登录、改密、找回与账号管理都在界面内完成。

| 设置 | 用途 |
| --- | --- |
| `AccountServicePort` | 账号服务（后台）端口，默认 `8081`。**域名与 `EntranceHost` 共用**：实际地址 = `http://{EntranceHost}:{AccountServicePort}`，发布前只改这一个端口，域名不用改两处。 |
| `AccountServiceUrl` | 可选覆盖：填了就完全使用该地址（账号服务在别的域名或走内网时）；留空即按上面的「域名:端口」自动拼。 |
| `Theme` | 启动器界面主题：`auto` / `light` / `dark`（只作用于注册/改密/找回/账号管理/设置这些窗口，主窗口是原版皮肤不受影响）。 |
| `Accounts[]` 子项 | 除 `Account` / `Password` 外还支持 `DisplayName`（显示名）、`CreateTime`（创建时间）、`OicqCode` 与 `RememberPassword`（记住密码，默认 `true`；为 `false` 时只保存空密码，启动前需在界面输入，无头启动需要存储密码）。 |

`Relay.Enabled` 打开后，启动器会把本地中继托管在自己进程内（启动客户端后驻留到客户端退出，不额外产生进程），用于客户端只能 IPv4、服务器只有 IPv6 的场景，见 [DEPLOYING.md](DEPLOYING.md#64-让-ipv4-客户端连接-ipv6-服务器dflegacyrelay)。

新版使用独立 `DFLegacy.Client.dll`，通过登录器内嵌的 x86 引导器在游戏入口前加载；保持原始 EXE、DLL、PVF、NPK 不变。仅支持已验证的同版本兼容展开客户端，不修复原始旧壳的现代 Windows 兼容性。旧版用户需手动迁移一次：退出游戏并备份客户端，使用新登录器验证原版恢复，不能根据 `ijl15.original.dll` 等备份文件名猜测原版。

部署管理器下载卡片默认导出普通版；勾选“测试版（包含 debug.dll，便于诊断）”加入同版本诊断模块，并默认在首个游戏内画面加载本地诊断，不改写旧 `EnableDebugOverlay` 配置。普通版同样包含完整游戏优化且不加载诊断。测试控制、自动输入和组队文件命令仍需显式开启；仅服务端覆盖数据需要共享本机目录，不自动同步远程数据。缺失或不匹配的测试资源会阻止测试包导出。补丁、引导器和登录器内嵌 Detours 完整许可与版本，运行 `DFLegacy.Launcher.exe --third-party-notices | more` 可查看。

玩家 ZIP 解压到 `DNF.exe` 所在目录，含 `client-package.json`、单文件登录器、独立补丁及安全生成的服务器连接配置；已有 `Config.ini` 继续保留。GitHub 的手动迁移工具包不含 `Config.ini`、`launcher.json`、游戏文件或账号。迁移后按已安装普通／测试类型自动使用对应 schema 2 更新包；不会新发 schema 1 桥接包。

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
