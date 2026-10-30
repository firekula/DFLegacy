# DFLegacy 部署管理器

管理器提供配置、游戏服务启停、滚动日志和客户端 ZIP 导出。默认入口为 `http://127.0.0.1:18080`；游戏账号 API 与已有 GM/玩家页面继续使用游戏服务的 Admin 端口，默认 `8081`。

## 首次运行

1. 将对应平台发行包解压到一个新目录。准备自行取得、与客户端匹配的 `Script.pvf`，放入 `resources/Script.pvf` 或在管理页指定路径。
2. Windows 双击 `Start.cmd`；Linux 执行 `./start.sh`；macOS 使用 `Start.command`。Unix 发行包请用保留可执行位的方式解压。macOS 首版未签名，系统首次运行限制按系统提示处理。
3. 本机浏览器自动打开首次设置页面并携带一次性凭据。无头服务器或 Docker 在数据目录 `management/setup-token.txt` 中读取凭据后打开页面。凭据不会写到启动日志，设置完成即失效并删除。
4. 创建部署管理账号，密码长度 12–128 字符。该账号仅管理进程、配置与发行资源。
5. 在「服务配置」填 PVF、玩家连接地址、账号 API 和端口；选择本机、IPv4 或 IPv6 中继模式。保存配置不会立即重启运行中的游戏服务。
6. 首次新建存档时，可在同页创建游戏管理员，输入游戏账号、密码与找回码。已有管理员时直接使用；同名账号会报冲突，不会替换密码或重置存档。
7. 启动服务。只有私有管理通道就绪后才显示「运行中」。停止游戏服务后部署管理器仍保持在线；关闭浏览器也不会结束服务。

默认管理器托管单体拓扑。已有 Channel 拆分部署请继续使用高级手动启动方式；管理器会明确拒绝只启动一部分进程。

## 配置与数据

```text
DFLegacy/
  DFLegacy.Manager[.exe]
  runtime/server/        游戏服务及原有 wwwroot
  runtime/channel/       高级手动部署工具
  client-assets/         配套 Windows 客户端资源和清单
  templates/             首次运行模板
  release-manifest.json  发行身份和文件摘要
  data/config/           用户配置
  data/management/       管理账号哈希、会话密钥、首次凭据
  data/game/state.json   游戏账号与角色存档
  data/logs/             滚动日志
  data/backups/          最近配置备份
  data/exports/          临时生成的 ZIP
```

可以通过 `--data-dir <绝对路径>` 或 `DFLEGACY_MANAGER_DATA` 把数据放在安装目录外。管理器和服务端均不依赖启动时的当前工作目录；同一数据目录/存档文件只能有一个写入进程，OS 文件锁随进程结束释放。

已有配置中页面未展示的字段和额外入口端口会保留。环境变量优先，页面会显示有效值及文件值，受控输入被锁定；与中继地址要求冲突时拒绝保存并说明对应变量。修改 PVF、监听或倍率后需重启游戏服务。

存档导入应在停服时把旧 `state.json` 复制到新数据目录的 `game/state.json`，并先备份原文件。不要同时启动旧、新两个服务端写同一存档。新模板关闭测试账号播种；旧存档中的账号不会因这一开关而被删除。

## 网络与已有页面

| 场景 | 监听 | 玩家连接地址 | 游戏协议地址 |
| --- | --- | --- | --- |
| 本机 | 127.0.0.1 | 127.0.0.1 | 127.0.0.1 |
| 局域网/公网 IPv4 | 0.0.0.0 | 玩家可达 IPv4 或有 A 记录的域名 | 同玩家连接地址 |
| IPv6 + 玩家本机 Relay | :: | 真实服务器 IPv6/域名，登录器启用 Relay | 127.0.0.1，由玩家本机中继转发 |

管理器和游戏 API 的监听地址分别配置。客户端原版协议有 IPv4 限制，IPv6 模式沿用已有本机 Relay；账号注册及游戏内网页使用单独的 HTTP(S) API 基址。

- `/admin/index.html`：原有 GM 邮件、通知、广播、副本权限和管理员角色页面。
- `/admin/player.html`：玩家自己的角色列表。
- `/client/*.htm`：游戏内 460×315 充值/黑钻页面，保留旧浏览器兼容性；这些操作仍需游戏管理员权限。

远程管理建议通过 SSH 隧道或 HTTPS 反向代理，Docker 默认只在宿主回环发布管理端口。使用域名时通过 `--allowed-hosts example.com` 或 `DFLEGACY_MANAGER_ALLOWED_HOSTS` 明确允许 Host；反代应保留 Host。默认只信任本机代理的转发协议头，其他代理需通过 `DFLEGACY_MANAGER_TRUSTED_PROXIES` 配置确切 IP，不要信任任意来源。

管理会话使用 HttpOnly、SameSite=Strict cookie；写操作需 CSRF 凭据及同源校验。游戏账号 token 不能控制部署管理器。服务端内部启停通道只接受本机与每次启动生成的私有凭据。

## 客户端包

在「客户端分发」选择补丁或完整登录器。管理器校验发行身份、资源清单及每个文件摘要；不同提交、被修改的 DLL、符号链接和越界路径会拒绝导出。

- 完整包中的 `DFLegacyLauncher` 放到 `DNF.exe` 同级；`ClientPath=../DNF.exe`，账号和密码为空，`StartServer=false`。资源自带 Windows 运行时，玩家自行注册/登录。
- 补丁 ZIP 将 `ijl15.dll` 与 `Config.ini` 模板放在根目录。先退出游戏并备份旧文件，合并配置以保留玩家自己的分辨率和其他设置。
- `[Network] ClientServiceUrl=` 指定游戏内网页 HTTP(S) 基址，登录器安装时会同步此项，并保留其他 INI 内容。旧客户端按格式串处理 URL，基址限定为最多 1000 ASCII 字节，不能含百分号、凭据、查询或片段。
- ZIP 含安装说明和 `SHA256SUMS`。不包含客户端、PVF、游戏存档、管理账号或已有玩家凭据。
- 调试浮窗默认关闭，现有调试传输需要与服务端共享本机目录，远程 Linux/macOS/Docker 不自动提供远程浮窗能力。

## 构建、测试与发布

具体命令及 Runner 安装见 [BUILDING.md](BUILDING.md)、[DEPLOYING.md](DEPLOYING.md)。管理器测试是控制台执行器：

```powershell
dotnet run --project tests/DFLegacy.Manager.Tests -c Release -- --http --server-dir <绝对Server发布目录>
```

测试创建独立临时数据和公开合成的最小 PVF；不会读取已有玩家存档。覆盖配置保留/覆盖、管理身份、HTTP 鉴权、客户端 ZIP、真实子进程就绪/停止/异常退出。现有默认服务端冒烟、原生测试与 Python 工具测试仍需执行；`dotnet test` 不会替代控制台测试的实际运行。

Gitea 流水线产物包括五平台自包含归档、校验清单、独立 Compose/.env 示例及多架构镜像。PR 与普通分支不使用发布凭据；main 创建开发预发布，正式版本标签创建正式 Release，正式发布成功后才更新镜像 latest。手工触发默认只构建，显式发布也不能跳过门禁。开发预发布清理默认 dry-run，只清理由本流程创建的开发版本。

发行不自动升级正在运行的实例。远端接入需启用仓库 Actions、注册 Windows/Linux Runner、设置 Registry 变量和专用 secret；不要将 token 写入仓库或客户端包。

## 验证边界

各发行包的 `release-manifest.json` 记录构建身份和验证范围。交叉构建/摘要通过不代表在目标硬件上运行通过。macOS、Linux arm64 未有原生 Runner；真实 Windows 客户端到远程服务端的登录、进图与长期运行仍需游戏实机验收。现有代码和工具检查通过也不代表已在 Gitea 启用 Runner 或成功发布到远端 Registry。
