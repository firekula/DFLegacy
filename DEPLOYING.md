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
| 2311 | UDP | 角色数据报 |
| 7002、7003 | UDP | 玩法数据报 |

监听地址决定本机接受连接的位置；公布地址与账号 API URL 必须让玩家可以连接。`0.0.0.0`、`::` 可作为监听地址，不能作为公布地址。容器/NAT 下在 Manager 设置客户端可达的地址和 API URL，然后重启游戏服务。端口改动时同步修改防火墙、Compose 映射与客户端设置。

远程管理使用 SSH 隧道或 HTTPS 反向代理，保持默认 loopback 映射。例如 `ssh -L 18080:127.0.0.1:18080 user@server` 后访问本机 `http://127.0.0.1:18080`。显式公开管理口时同时配置 HTTPS 与受信任的访问 Host。反代域名添加到 `--allowed-hosts`/`DFLEGACY_MANAGER_ALLOWED_HOSTS`，仅可信代理 IP 添加到 `DFLEGACY_MANAGER_TRUSTED_PROXIES`（逗号分隔），代理传递正确 Host/Origin 与 Forwarded Headers。Compose 对应 `.env` 的 `MANAGER_ALLOWED_HOSTS`、`MANAGER_TRUSTED_PROXIES`。cookie/CSRF 不能替代传输加密。游戏 API 的 GM/玩家身份与部署管理账号分别登录。

Manager `/health` 只表示管理器可响应；`/ready` 表示私有通道与全部必要监听已就绪，停服时返回 503。正常停服不应触发容器反复重启。部分启动失败会回收本次进程与已绑定端口，管理页继续可用，修正配置后可直接重试。日志会区分端口占用与 Windows 系统保留端口导致的拒绝访问；不会终止外部占用进程。

掉落概率以倍数显示：1 倍对应 `Drop.RatePercent=100`，沿用 PVF 基础概率；2 倍表示基础概率乘 2，最高仍为 100%，不表示所有物品必掉。普通怪物/深渊额外倍率与相关概率判定重叠时相乘，不代表金币数量倍率。详细操作见 [部署管理器说明](DEPLOYMENT_MANAGER.md)。

## Docker / Compose

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

Docker build/多架构发行见 [BUILDING.md](BUILDING.md)。Gitea Registry 使用 `registry/owner/image` 命名；若实例仅提供 HTTP，部署者需明确设置 Docker/Skopeo 对该 Registry 的信任，脚本不会修改 Docker 全局配置。`DFLEGACY_ALLOW_HTTP_API` 仅控制 Release HTTP API，不能替代 Registry TLS 配置。

## 导出玩家包

在 Manager 中确认公布地址、账号 API URL、端口和 Relay 偏好后，点击“生成并下载玩家包 ZIP”。所有系统使用随版 Windows 资源离线组包，不需要在服务器上安装 VS 或 WPF。

ZIP 包含单文件 WPF 登录器、独立的 `DFLegacy.Client.dll` 和 `client-package.json`、空凭据连接配置、条款、说明和摘要，直接解压到 `DNF.exe` 所在目录。游戏退出后先备份再替换；ZIP 不直接包含 `Config.ini`，登录器仅在缺少配置时从内嵌模板生成，保留已有设置。玩家包不包含管理凭据、游戏存档、PVF 或预置游戏账号，玩家自行登录/注册。

独立普通版不加载诊断；测试版默认在首个游戏内画面加载 `debug.dll` 本地诊断，不改写旧 `EnableDebugOverlay` 配置。仅服务端覆盖数据依赖本机共享快照目录，不支持远程 Linux/macOS/Docker 调试数据同步。自动输入等主动控制仍需显式开启。第三方可选 `d3d8to9` 不会自动分发，使用时自行核对仓库附带许可与兼容性。

## 导入、备份、升级与恢复

1. 在旧 Manager 软停止游戏，再停止 Manager/容器，确认无进程仍写存档。
2. 备份完整持久化数据目录及你自己的 PVF/配置；备份包含管理身份与游戏账号，限制读取权限。容器命名卷用 `docker volume inspect` 确认实际卷名，先停止服务再导出卷，不要执行 `down -v` 删除数据。
3. 旧版 `data/state.json` 通过明确手工导入迁移：在新服务尚未启动、目标 `data/game/state.json` 不存在时复制旧文件到该路径。目标已有存档则先备份并明确选择，不能自动合并或覆盖。
4. 配置 `DataPath`/PVF 路径并检查旧未知字段仍被保留；已有游戏管理员选择跳过向导创建，不重置账号。不要同时运行新旧实例。
5. 升级将新版本解压到新程序目录，校验清单，继续使用原 `--data-dir`；容器只替换镜像标签/摘要，保留同一命名卷。备份后再启动新版本并检查账号/角色和日志。
6. 恢复时停服并使用相配的程序与备份数据恢复，先在副本验证。不要在有新写入时直接回滚旧存档。

源码直接运行保留旧配置入口，详见 [BUILDING.md](BUILDING.md)。新增 Server `--config <绝对文件>` 可显式选配置；离线 `--initialize-admin` 从 stdin 接受 JSON `{userName,password,oicqCode}`，仅在停服且能取得存档锁时使用。不要把维护命令当作在线存档编辑器。

## 验收范围

自动测试覆盖公开 fixture、身份/HTTP 边界、子进程生命周期、真实 ZIP/TAR、摘要和假 Gitea HTTP 流程。源码构建成功不等于客户端进图成功；首次正式发布前须记录 Windows 客户端分别连接 Windows、Linux、Docker 的人工验收。当前未接入 Runner/Registry、Docker daemon 未启动或没有 macOS/ARM 原生机器时，相应检查必须列为未执行，不能用交叉构建替代。


### 独立客户端包与 CI 夹具

管理器下载默认普通版；测试版加入同版本 `debug.dll` 并默认加载本地诊断，不自动启用输入、组队文件命令等测试控制。普通版包含完整游戏优化。新版补丁为独立 `DFLegacy.Client.dll`。旧用户先退出游戏、备份客户端，并确认一次可见且经哈希验证的原版 IJL 恢复，不能靠备份文件名推断原版。只支持已验证的兼容展开客户端，不修复原始旧壳；原始 EXE/DLL/PVF/NPK 保持不变。仅服务端覆盖数据依赖本机共享目录，不提供远程目录同步。补丁、引导器和登录器内嵌 Detours 完整许可与版本，运行 `DFLegacy.Launcher.exe --third-party-notices | more` 可直接查看。

公开发布固定包含四个附件：`DFLegacy-client-update-v2-win-x64.zip`、`DFLegacy-client-update-v2-test-win-x64.zip`、`DFLegacy-client-tools-win-x64.zip`、`DFLegacy-client-tools-test-win-x64.zip`。V2 更新包只有该类型的项目程序和 `update-manifest.json`；tools 包另含安装清单、摘要和迁移说明，不含玩家配置或游戏资源。发布器校验完整四附件集合及共同程序的哈希，再发布草稿并匿名回读；部分发布不得作为成功重试。旧已发布版本保持不变。公开标签指向预先导出的脱敏公开源码快照；私有构建提交仍作为包身份，不推送私有历史。

Windows CI 从本次源码构建 `artifacts/native/ijl15`，向 Launcher 测试及发布传入 `ClientPatchPrebuiltPath`、`ClientBootstrapPrebuiltPath`，`Ijl15PrebuiltPath` 仅用于旧补丁回归。测试显式设置 `DFL_NATIVE_FIXTURES` 为匹配的 CMake 构建目录（不是 Release 子目录），`DFL_LEGACY_PATCH_ARCHIVE` 为验证过的历史项目补丁 ZIP。`ci.py windows` 只从固定公开地址下载版本 `dev-20261031T114502Z-4a0e12555c17` 的 `DFLegacy-client-update-win-x64.zip` 并缓存到 `artifacts/test-fixture-cache`；严格校验大小 63332728 和 SHA256 `4851bfc41b1c39799ba7ed735954c1d6fd1cbc91fba0d8b2a201eb2c7977e0f5`。这是历史项目自有测试夹具，不是原版 IJL 或新 schema 1 发行附件；测试本身不联网，不依赖真实游戏盘。

本地 Launcher 全套测试也需要上述两个显式环境变量及同版本原生预构建路径。只验证发布包解析可运行 `python -m unittest discover -s tools/release/tests -p test_github_publish.py -k real_client_parser -v`，它调用真实 Launcher 的安装清单和更新解析器检查四种合成包，不启动游戏或操作界面。完整发布工具测试为 `python -m unittest discover -s tools/release/tests -v`。
