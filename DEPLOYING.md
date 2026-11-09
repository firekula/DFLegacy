# DFLegacy 部署指南

## 已有日常运行目录交接

合并前 [2026-10-30 手测记录](knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)中的 debug `0082559C…` 仅在 `Loot4_A` 手测客户端加载，其他日常目录未升级。原私有服务、测试存档及恢复脚本归属见 [当前交接](HANDOFF-2026-10-30.md)。手测结束应使用原私有目录的 `Stop-ManualTest.ps1`，不可在客户端仍加载 DLL 时覆盖，也不可用测试档回写正常存档。

该分支已有原生诊断与 U05 数据／CLI 的隔离验收不等同新构建的日常部署。源码合并不自动替换 DLL、启动器旁副本或正常存档；升级仍需备份、匹配 ABI／消费脚本、重启并验收，旧构建矩阵保留原身份。

下载与你系统对应的发行包，校验可信来源与 SHA-256，解压到一个新目录。管理器和 Server 为自包含 .NET 程序；不需要安装 SDK 或运行时，但系统仍需满足 [.NET 系统依赖](https://learn.microsoft.com/dotnet/core/install/)。游戏客户端与 `Script.pvf` 由使用者自行提供，发行包不包括这些资源。

## 本机启动与首次设置

Windows 双击 `Start.cmd`；Linux 执行 `./start.sh`；macOS 双击 `Start.command` 或从终端运行它。启动脚本以自己的位置定位安装根，可从其他工作目录调用，也支持含空格路径。Linux/macOS 使用 tar 归档保留执行位；若解压工具丢失模式，可在确认下载可信后对三个程序入口和启动脚本 `chmod +x`。

macOS 首版未签名、未公证，也未宣称已完成原生机器验收。系统可能阻止首次运行；核对来源后按 macOS“隐私与安全性”提示允许此应用。不要全局关闭系统安全机制。

Manager 默认监听 `127.0.0.1:18080`，本机模式会尝试打开浏览器。无桌面时传 `--no-browser`，访问控制台显示的地址。关闭浏览器不会停服。部署管理身份独立于游戏账号/GM，游戏服务停止后仍可登录 Manager。

1. 本机首次页面使用一次性设置凭据。无头模式从 `data/management/setup-token.txt` 读取它；该文件应仅运行账号可读，成功设置后失效并删除。
2. 创建部署管理账号，在「服务配置」上传你持有的 PVF 文件，再填写连接设置；无需输入服务器路径。上传上限 1 GiB，失败保留原资源和配置，运行中需先停服。
3. 使用向导创建初始游戏管理员；已有存档可跳过创建，不会重置账号。重名冲突要明确处理后重试。
4. 检查 PVF 可读、端口和公布地址，保存后点击启动游戏服务。首次配置与后续配置不会自动覆盖现有存档。

发行模板禁用测试账号播种，首次配置默认不自动启动。Manager 首版管理单体拓扑；拆分 Channel 仅供已有高级部署手动使用，启用拆分配置时 Manager 会明确拒绝托管，防止只启动一半。

## 文件与可持久化目录

```text
DFLegacy/
  DFLegacy.Manager[.exe] + wwwroot/ 自包含单文件管理器及网页
  runtime/server/                 自包含单文件Server与网页
  runtime/channel/                高级手动部署的单文件Channel
  client-assets/                  同版Windows登录器、补丁和manifest
  templates/                      安全默认配置
  release-manifest.json           版本、提交、摘要、验证范围
  resources/                      资源说明；已有外部PVF配置仍兼容
  data/resources/                 网页上传的PVF（按内容摘要保存）
  data/config/                    server.json、manager.json
  data/management/                管理账号哈希与一次性设置凭据
  data/game/                      游戏存档
  data/logs/                      有上限的运行日志
  data/exports/                   已导出的客户端包
  data/backups/                   配置备份
```

默认以安装根定位数据，使用 `--data-dir <绝对目录>` 可将它移到安装目录外；升级继续指定同一持久化目录。程序只初始化缺失文件，不覆盖既有设置。Manager 与 Server 使用操作系统文件锁防止同一数据被两个实例同时管理或写入，不应绕过锁并发操作存档。

CLI：`--root <目录> --data-dir <目录> --listen <IP> --port <端口> --no-browser --allowed-hosts <逗号分隔域名>`。对应环境变量：`DFLEGACY_MANAGER_ROOT`、`DFLEGACY_MANAGER_DATA`、`DFLEGACY_MANAGER_LISTEN`、`DFLEGACY_MANAGER_PORT`、`DFLEGACY_MANAGER_NO_BROWSER`、`DFLEGACY_MANAGER_ALLOWED_HOSTS`。命令行优先；文件中被 `DFLegacy__...` 环境变量覆盖的服务设置会在页面中标注，需修改部署环境后重启生效。不要将密码放进命令行参数。

## 地址、端口与远程管理

| 默认端口 | 协议 | 用途 |
| --- | --- | --- |
| 18080 | TCP HTTP | 部署Manager；默认宿主loopback |
| 8081 | TCP HTTP | 玩家账号API与既有GM/玩家网页 |
| 2311、7001、8080 | TCP | 登录、频道及附加入口 |
| 7100–7105 | TCP | 可选地区频道 |
| 7004、7110–7112 | TCP | 可选分级和自由决斗频道 |
| 2311 | UDP | 角色数据报 |
| 7002、7003 | UDP | 玩法数据报 |

监听地址决定本机接受连接的位置；公布地址与账号 API URL 必须让玩家可以连接。`0.0.0.0`、`::` 可作为监听地址，不能作为公布地址。容器/NAT 下在 Manager 设置客户端可达的地址和 API URL，然后重启游戏服务。端口改动时同步修改防火墙、Compose 映射与客户端设置。

远程管理使用 SSH 隧道或 HTTPS 反向代理，保持默认 loopback 映射。例如 `ssh -L 18080:127.0.0.1:18080 user@server` 后访问本机 `http://127.0.0.1:18080`。显式公开管理口时同时配置 HTTPS 与受信任的访问 Host。反代域名添加到 `--allowed-hosts`/`DFLEGACY_MANAGER_ALLOWED_HOSTS`，仅可信代理 IP 添加到 `DFLEGACY_MANAGER_TRUSTED_PROXIES`（逗号分隔），代理传递正确 Host/Origin 与 Forwarded Headers。Compose 对应 `.env` 的 `MANAGER_ALLOWED_HOSTS`、`MANAGER_TRUSTED_PROXIES`。cookie/CSRF 不能替代传输加密。游戏 API 的 GM/玩家身份与部署管理账号分别登录。

Manager `/health` 只表示管理器可响应；`/ready` 表示私有通道与全部必要监听已就绪，停服时返回 503。正常停服不应触发容器反复重启。部分启动失败会回收本次进程与已绑定端口，管理页继续可用，修正配置后可直接重试。日志会区分端口占用与 Windows 系统保留端口导致的拒绝访问；不会终止外部占用进程。

掉落概率以倍数显示：1 倍对应 `Drop.RatePercent=100`，沿用 PVF 基础概率；2 倍表示基础概率乘 2，最高仍为 100%，不表示所有物品必掉。普通怪物/深渊额外倍率与相关概率判定重叠时相乘，不代表金币数量倍率。详细操作见 [部署管理器说明](DEPLOYMENT_MANAGER.md)。

## Docker / Compose

### 四类决斗频道与 GM 奖励（v0.1.7）

启用 `DFLegacy__Pvp__Enabled: "true"`（已有网页中启用 PvP 的部署保持即可）。在现有 Compose 的 `ports` 增加 `"7110-7112:7110-7112/tcp"`，保留原 `7004:7004/tcp`，并同时在云安全组、宿主防火墙放行 7110–7112/TCP。这些是独立 TCP 入口，无需增加 UDP 端口。地区频道 7100–7105 保留。

上传并选择本轮配套 PVF，服务端和所有客户端使用同一份。停服上传后重启服务，客户端随更新包一起更新 PVF。新 PVF SHA256 为 `c2321f05953ae4484dc6d0b10db50f83c3251c4967cdd0859376d4f8acdf27f4`；原已发布 PVF 的 `09e5eefd…` 仍原样保留。只更新镜像或只开端口，无法改变旧 PVF 的段位限制。

初级索引 0–9，中级 10–19，高级 20–当前 PVF 最大段位（本轮为 30），自由 0–30。原版界面沿用自身档位标签；频道标题分别标识初级、中级、高级、自由。每个频道独立房间。旧 launcher.json 内标准 PvP TCP 7004 列表在运行时补齐新端口，不写回服务器、账号或自定义配置；不启用本机中继的客户端直接连接公布地址。

房间支持2～8人个人战，以及每队1～4人、双方人数相同的组队战和擂台赛。房主在房间下方切换模式，队员通过座位颜色调整红蓝队，其他队员准备后由房主开始；切换模式或队伍会清除准备状态。擂台赛可申请出场，胜者继续作战，败方换下一位队员，整队结束后统一结算。观战席及死亡竞赛暂未实现。

组队战和擂台赛只需更新服务端，沿用上述决斗TCP端口及已有玩法UDP，无须额外端口、配置开关或客户端补丁。四类段位频道仍需前述配套PVF和端口；这两部分升级要求不同。多人地图按实际人数校验，6～8人专用地图只在对应人数的房间可选。v0.1.7发布服务端镜像，本次不发布客户端自动更新附件。已安装玩家仍使用v0.1.6更新源；启用四类频道时仍须手动同步上述PVF及客户端中继端口，或等待后续配套客户端更新。

此配置支持 ACT1 `Channel.ServerNumber=1` 的单进程部署；其他服务器组或拆分 Channel 拓扑启用 PvP 时会明确拒绝启动。

18081 GM 后台新增「胜点发放」，按角色增加可消费胜点；新增全服邮件范围，先预览、再确认。接收范围冻结于预览，包含所有现有离线角色；之后新建角色不补发。失败结果可仅补发失败角色。以上测试不向线上发奖励，本轮尚未发布镜像或更新附件。

GHCR 镜像地址为 `ghcr.io/firekula/dflegacy:<版本号>`，开发预发布使用明确版本标签，不能依赖 `latest`。公开包可匿名拉取，私有包先登录 GHCR。

升级前软停止游戏并备份整个持久化数据卷，然后修改 `.env` 的 `DFLEGACY_IMAGE`，执行 `docker compose pull` 和 `docker compose up -d`。保持原 Compose 项目名和卷映射，不执行 `down -v`。Podman 部署把命令替换为 `podman compose`，在配置文件所在目录执行，或用 `-f` 指定路径。

客户端打包使用后台任务并显示阶段进度，完成后通过独立下载请求交给浏览器；具体进度、重试及日志行为见 [部署管理器说明](DEPLOYMENT_MANAGER.md#客户端生成进度与下载)。

镜像内 Manager 根为 `/app`，持久化数据为 `/app/data`；默认从管理页上传 PVF，保存到 `/app/data/resources/`。镜像使用非 root `app` 用户，命名卷首次自动建立为可写目录；不需要挂载 Docker socket。单文件程序首次运行可将原生依赖解压到数据卷的 `.net` 缓存目录。

1. 复制 `.env.example` 为 `.env`。
2. 将 `DFLEGACY_IMAGE` 设为你构建/发布的版本镜像或可信的 `registry/owner/image@sha256:...`。默认模板不要求填写 `PVF_PATH`。
3. 执行 `docker compose config` 审查映射，然后 `docker compose up -d`。
4. 执行 `docker compose exec dflegacy cat /app/data/management/setup-token.txt` 取得首次设置凭据，通过本机管理页或隧道上传 PVF、完成配置，确认公布地址/API URL 后启动游戏。

需要保留只读宿主 PVF 挂载时，下载同版 `compose.pvf.yaml`，将 `.env` 的 `PVF_PATH` 设为已有文件的绝对路径，再执行 `docker compose -f compose.yaml -f compose.pvf.yaml up -d`；源码仓库中第二个文件位于 `deploy/compose.pvf.yaml`。显式环境变量控制的 PVF 不能在网页替换。下方 host/IPv6 示例仍要求填写 `PVF_PATH`。

默认 bridge 模板将管理口仅映射宿主 `127.0.0.1:18080`，业务 API 8081 以及游戏 TCP/UDP 端口单独映射。可通过 `.env` 调整宿主绑定；游戏 API 在不受信网络需置于 HTTPS 代理后。查看日志用 `docker compose logs --tail 100 -f`，停止用 `docker compose stop`。`stop_grace_period` 给 Manager 时间软停止自己的游戏子进程。

Linux 需要 host 网络和 IPv6 时使用独立示例：

```bash
docker compose --env-file .env -f deploy/compose.host-ipv6.yaml config
docker compose --env-file .env -f deploy/compose.host-ipv6.yaml up -d
```

host 模式直接占用宿主业务端口，Manager 仍监听 `127.0.0.1`。IPv4-only 游戏客户端连接 IPv6 服务器需开启登录器内 Relay；其账号 API 直接使用服务器可达 URL。原协议中的固定 IPv4 字段不代表原客户端已支持 IPv6。

如将数据改成宿主绑定目录，先让镜像 `app` UID 有读写权限（官方 .NET 镜像通常 UID 1654，可运行 `docker run --rm --entrypoint id <镜像> app` 核对），再替换卷映射。SELinux 主机按当地策略标记目录。不要通过以 root 运行服务来跳过权限问题。

Docker 镜像和客户端的发布流程见 [BUILDING.md](BUILDING.md)。

### 启用地区频道

在现有 Compose 服务的 `environment` 增加 `DFLegacy__Channel__RegionalChannelsEnabled: "true"`，在 `ports` 增加 `"7100-7105:7100-7105/tcp"`，并放行对应 TCP 端口。host 网络部署直接放行宿主端口。本机中继玩家需在 `Relay.TcpPorts` 保留已有值并加入这六个端口，或使用管理器重新导出的配置。地区及经验比例见[管理器指南](DEPLOYMENT_MANAGER.md#地区经验频道)。

### 升级到 v0.1.6

在部署管理页停止游戏并备份原数据卷。将 Compose 同目录的 `DFLEGACY_IMAGE` 改为 `ghcr.io/firekula/dflegacy:v0.1.6`，再执行 `podman compose pull dflegacy` 和 `podman compose up -d dflegacy`。继续使用原数据卷。

在部署管理页上传本次的 `Script.pvf`，确认已选用后启动游戏。使用只读宿主 PVF 挂载的部署更新挂载文件后重建容器。可以从任一 v0.1.6 V3 客户端更新 ZIP 提取同一份 PVF。该文件的 SHA-256 为 `09e5eefdee0777378a967704eb14c175383eef89ca0f4e27e00dd3409af728cb`。

服务端在启动时按北京时间 06:00 检查疲劳日期，补刷尚未刷新的日期。周末活动在周六 06:00 开始，周一 06:00 结束。

## 客户端自动更新

以下 V2／V3 步骤描述已发布客户端。V4 尚处于隔离测试阶段，不能据此认为正式仓库已经有 V4 附件。新协议的来源设置、离线状态和修复边界见 [V4 更新说明](CLIENT_UPDATES_V4.md)。

GitHub Releases 分发 Windows x86 和 x64 两条更新线，各有普通版和测试版。管理后台默认生成指向 GitHub 的安装包，游戏连接地址仍由后台的部署配置决定。服务端镜像从 GHCR 拉取。

v0.1.4 登录器先从 GitHub 安装 v0.1.5 的 V2 兼容更新。重新启动后，新登录器会获取 v0.1.6 的 V3 更新包，其中包含 `Script.pvf`。安装前校验 ZIP 和文件摘要，旧 PVF 随原程序文件备份至 `.dflegacy-backups`。x86 和 x64 都沿用各自的普通版或测试版，现有玩家通过登录器更新即可。

从 18083 迁移时，从 Compose 移除 `DFLEGACY_UPDATE_PUBLIC_URL`（标准模板清空 `UPDATE_PUBLIC_URL`），并移除自定义的 18083 端口映射。更新镜像后，玩家覆盖一次后台导出的同架构、同类型内层 ZIP。保留账号和现有连接设置时，在原 `launcher.json` 中把 `UpdateServiceUrl` 设为空字符串、`DisableGitHubUpdates` 设为 `false`。客户端需要访问 GitHub 的 HTTPS API 和下载地址。

如果曾通过 18083 成功安装过自动更新，先完成任何待恢复的更新，然后退出游戏和登录器，将客户端目录中的 `.dflegacy/state.json` 改名为 `state.hosted-backup.json`。这个文件记录的是旧更新源的版本 ID。完成一次覆盖安装后重新启动，登录器会记录 GitHub 的版本 ID。

上述改名步骤仅针对旧 V2／V3 来源迁移，不适用于 V4 的安装记录或待恢复事务；不要删除 V4 缓存、事务、备份来绕过校验。V4 的已安装状态与待下载目标分开保存，联网失败时仍须全量验证已安装版本并确认没有待恢复事务。

### V4 测试来源与保留配置

仅在配套测试登录器中，把 `launcher.json` 的 `UpdateProvider` 设为 `gitea`、`UpdateChannel` 设为 `gitea-test`、`UpdateRepository` 设为测试仓库；保留现有账号、服务器与 `Relay`。显式 Gitea 来源固定使用 `http://firekula.site:3000`，优先于遗留 `UpdateServiceUrl`，且不会静默回退 GitHub。正式来源配置为 `github`／`stable`；该模式下非空 `UpdateServiceUrl` 仍沿用现有自托管 V3 路径。完整来源决策表见 [来源与离线状态](CLIENT_UPDATES_V4.md#来源与离线状态)。

V4 不整文件覆盖 `launcher.json` 或 `Config.ini`。`Relay.TcpPorts`／`UdpPorts` 在玩家配置中仍是逗号分隔字符串；迁移值使用端口数组，合并后保留自定义端口并写回字符串。格式损坏时保留原配置并提示错误，不以空模板覆盖账号。登录器自身无法运行时需要单独的可信修复程序；独立 `--repair-client` 入口及验收范围见 [V4 更新说明](CLIENT_UPDATES_V4.md#安装恢复与诊断)，修复程序必须放在被修复目录之外。

### 可选的自托管客户端更新

使用独立 HTTP 端口直连时，设置 `DFLEGACY_UPDATE_PUBLIC_URL=http://games.example.com:18083`，并将容器端口发布为 `"18083:18083/tcp"`。标准 Compose 可在 `.env` 设置 `UPDATE_PUBLIC_URL=http://games.example.com:18083` 和 `UPDATE_BIND=0.0.0.0`。放行 TCP 18083 后，客户端可直接访问。HTTP 的版本清单和 ZIP 都可能被中途替换，公开部署可使用以下 HTTPS 配置校验服务器身份。

同一域名可以使用独立 HTTPS 端口，例如 `https://games.example.com:18443`。
在标准 Compose 的 `.env` 设置 `UPDATE_PUBLIC_URL=https://games.example.com:18443`。
容器更新监听使用 TCP 18083，默认只映射到宿主机回环地址。让宿主机的 HTTPS 反向代理监听 TCP 18443，并将整个端口转发到 `http://127.0.0.1:18083`。
使用 host 网络时，更新监听同样随 Manager 绑定在宿主机回环。只需对外放行代理的 HTTPS 端口。

已有自定义 Compose 时，把 `DFLEGACY_UPDATE_PUBLIC_URL` 加入服务的 `environment`，并在 `ports` 中添加 `"127.0.0.1:18083:18083/tcp"`。
更新端口只响应版本查询和 ZIP 下载。管理后台继续使用 18080。更新包与发布索引保存在 `/app/data/client-updates`。

后台一次生成所选版本的 x86 和 x64 安装包，并在两套更新包写入完成后发布。
普通版和测试版独立发布。已有 GitHub 客户端需手动覆盖一次新导出的同架构、同类型安装包，以取得新更新地址。
首次迁移时，如需保留已保存的账号，先备份原 `launcher.json`，将新包里的 `UpdateServiceUrl` 和 `DisableGitHubUpdates` 合并到原配置。此处自托管路径继续使用原有 ZIP 更新合同，不因配置中出现 V4 字段就自动扩展成任意资源更新。

## 导出玩家包

在 Manager 中确认公布地址、账号 API URL、端口和 Relay 偏好后，点击“生成并下载玩家包 ZIP”。所有系统使用随版 Windows 资源离线组包，不需要在服务器上安装 VS 或 WPF。

外层 ZIP 包含 x86 和 x64 两个内层 ZIP。按 Windows 系统位数选择其一。内层 ZIP 包含单文件 WPF 登录器、独立的 `DFLegacy.Client.dll` 和 `client-package.json`、空凭据连接配置、条款、说明和摘要，直接解压到 `DNF.exe` 所在目录。游戏退出后先备份再替换；ZIP 不直接包含 `Config.ini`，登录器仅在缺少配置时从内嵌模板生成，保留已有设置。玩家包不包含管理凭据、游戏存档、PVF 或预置游戏账号，玩家自行登录/注册。

独立普通版不加载 `debug.dll` 诊断界面；测试版默认在首个游戏内画面加载该模块，不改写旧 `EnableDebugOverlay` 配置。Unreleased 的按事件保存事故报告不依赖开启测试界面，详见 [诊断边界](CLIENT_UPDATES_V4.md#安装恢复与诊断)。仅服务端覆盖数据依赖本机共享快照目录，不支持远程 Linux/macOS/Docker 调试数据同步。自动输入等主动控制仍需显式开启。第三方可选 `d3d8to9` 不会自动分发，使用时自行核对仓库附带许可与兼容性。

## 导入、备份、升级与恢复

1. 在旧 Manager 软停止游戏，再停止 Manager/容器，确认无进程仍写存档。
2. 备份完整持久化数据目录及你自己的 PVF/配置；备份包含管理身份与游戏账号，限制读取权限。容器命名卷用 `docker volume inspect` 确认实际卷名，先停止服务再导出卷，不要执行 `down -v` 删除数据。
3. 旧版 `data/state.json` 通过明确手工导入迁移：在新服务尚未启动、目标 `data/game/state.json` 不存在时复制旧文件到该路径。目标已有存档则先备份并明确选择，不能自动合并或覆盖。
4. 配置 `DataPath`/PVF 路径并检查旧未知字段仍被保留；已有游戏管理员选择跳过向导创建，不重置账号。不要同时运行新旧实例。
5. 升级将新版本解压到新程序目录，校验清单，继续使用原 `--data-dir`；容器只替换镜像标签/摘要，保留同一命名卷。备份后再启动新版本并检查账号/角色和日志。
6. 恢复时停服并使用相配的程序与备份数据恢复，先在副本验证。不要在有新写入时直接回滚旧存档。

源码直接运行保留旧配置入口，详见 [BUILDING.md](BUILDING.md)。新增 Server `--config <绝对文件>` 可显式选配置；离线 `--initialize-admin` 从 stdin 接受 JSON `{userName,password,oicqCode}`，仅在停服且能取得存档锁时使用。不要把维护命令当作在线存档编辑器。

## 验收范围

自动测试覆盖公开 fixture、身份/HTTP 边界、子进程生命周期、真实 ZIP/TAR、摘要和假 Gitea HTTP 流程。源码构建成功不等于客户端进图成功；发布检查分别记录两条客户端更新线和 Docker 镜像的验证范围。


### 独立客户端包与 CI 夹具

管理器下载默认普通版；测试版加入同版本 `debug.dll` 并默认加载本地诊断，不自动启用输入、组队文件命令等测试控制。普通版包含完整游戏优化。新版补丁为独立 `DFLegacy.Client.dll`。旧用户先退出游戏、备份客户端，并确认一次可见且经哈希验证的原版 IJL 恢复，不能靠备份文件名推断原版。只支持已验证的兼容展开客户端，不修复原始旧壳；原始 EXE/DLL/PVF/NPK 保持不变。仅服务端覆盖数据依赖本机共享目录，不提供远程目录同步。补丁、引导器和登录器内嵌 Detours 完整许可与版本，运行 `DFLegacy.Launcher.exe --third-party-notices | more` 可直接查看。

旧版发行使用 V2 更新 ZIP 和用于迁移的 tools 包。V2 更新包包含项目程序和 `update-manifest.json`。v0.1.6 的 GitHub V3 更新包含 x64 普通／测试及 x86 普通／测试共四个 ZIP，每个 ZIP 都包含同一份 `Script.pvf`。发布器核对各附件、共同文件摘要及公开下载内容。公开包记录构建提交，源码历史保存在 Gitea。

Windows CI 从本次源码构建 `artifacts/native/ijl15`，向 Launcher 测试及发布传入 `ClientPatchPrebuiltPath`、`ClientBootstrapPrebuiltPath`，`Ijl15PrebuiltPath` 仅用于旧补丁回归。测试显式设置 `DFL_NATIVE_FIXTURES` 为匹配的 CMake 构建目录（不是 Release 子目录），`DFL_LEGACY_PATCH_ARCHIVE` 为验证过的历史项目补丁 ZIP。`ci.py windows` 只从固定公开地址下载版本 `dev-20261031T114502Z-4a0e12555c17` 的 `DFLegacy-client-update-win-x64.zip` 并缓存到 `artifacts/test-fixture-cache`；严格校验大小 63332728 和 SHA256 `4851bfc41b1c39799ba7ed735954c1d6fd1cbc91fba0d8b2a201eb2c7977e0f5`。这是历史项目自有测试夹具，不是原版 IJL 或新 schema 1 发行附件；测试本身不联网，不依赖真实游戏盘。

本地 Launcher 全套测试也需要上述两个显式环境变量及同版本原生预构建路径。只验证发布包解析可运行 `python -m unittest discover -s tools/release/tests -p test_github_publish.py -k real_client_parser -v`，它调用真实 Launcher 的安装清单和更新解析器检查四种合成包，不启动游戏或操作界面。完整发布工具测试为 `python -m unittest discover -s tools/release/tests -v`。

## 客户端基本故障报告（Unreleased）

无需新端口。`POST /api/client-diagnostics/reports` 只在账号 API `Admin.Port`（本地默认 8081）接收，GM 管理仅在独立 GM 口（本地默认 18081）且需 Admin 身份；玩家 HTTP 口不提供接收／查看。GM 后台新增故障报告页，支持类型、版本、状态、系统、GPU、时间筛选、分页、状态维护、基本 JSON 下载及删除。交互式页面操作本轮尚未完成，自动 HTTP 权限／筛选／下载／删除回归见[验收报告](docs/acceptance/2026-11-09-client-diagnostic-reporting.md)。

在 `server.json` 的 `ClientDiagnostics` 中可设置 `Enabled`（默认 true）、`StoragePath`（空则存档同级 diagnostics 独立目录）、`RetentionDays`（最多 14）、`MaxStorageBytes`（最多 268435456）、`MaxReportCount`（最多 2000）、`MaxDeduplicationEntries`（最多 50000）、`SourcePerMinute`（5）、`SourcePerHour`（30）、`GlobalPerMinute`（10）、`GlobalPerHour`（120）。两个并发接收，上行压缩 256 KiB／展开 1 MiB、15 秒请求界限；容量不足返回 503，不通过提前删除幂等摘要腾空间。删除正文后保留 30 天去重摘要，重试返回 410，防止报告复活。诊断存储不持有游戏存档锁。

HTTP 基本 JSON 兼容现有公网入口，设置明确显示未加密；建议部署 HTTPS。HTTPS 不允许降级或忽略证书，不允许跨源重定向。自动队列按当前用户保存，100 份／32 MiB／14 天、单并发约 64 KiB/s，每次登录器运行每事件最多三次尝试；丢失响应／重启重复发送保持相同正文和事件 ID。该服务没有原始转储上传接口。

## v0.1.10 冰心少年与邮件更新

本版只需把 .env 的 DFLEGACY_IMAGE 改为 ghcr.io/firekula/dflegacy:v0.1.10，再执行 podman compose pull 和 podman compose up -d。保留数据卷、原环境变量和端口；PVF 与 v0.1.8 相同，无需重复上传。客户端从 GitHub 对应架构自动更新。详见 [v0.1.10 升级说明](docs/releases/v0.1.10.md)。
