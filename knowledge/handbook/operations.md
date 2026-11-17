# 部署、备份、回滚与诊断

这是操作说明，不是对任何真实服务执行部署的授权。本文没有连接线上、读真实凭据、发放奖励或修改存档。构建入口见 [构建与发行](build-test-release.md)，验证记录规则见 [测试与证据](testing-and-evidence.md)。示例使用占位目录/域名；部署前必须替换并核实。

## 1. 第一件事：识别章节、实例和数据所有者

先记录当前安装目录、实际执行文件、镜像 digest、配置来源、数据目录、PVF 身份、监听端口和哪个进程拥有存档。不能仅凭窗口标题、容器名或 `latest` 标签判定版本。依据最终 `release-manifest.json`、正在运行的文件哈希、容器 inspect 与日志核对。

| 项目 | ACT1 通用部署 | 第二章部署 |
| --- | --- | --- |
| 本机管理页 | `127.0.0.1:18080` | `127.0.0.2:48092` |
| 账号 API | 默认 `8081` | 默认 `48091` |
| 默认启动模板 | [根 compose.yaml](../../compose.yaml)、通用发行 Start 脚本 | [第二章 templates](../../deploy/chapter-two/templates/server.json)、[Start.ps1](../../deploy/chapter-two/Start.ps1) |
| 镜像构建 | [Dockerfile](../../Dockerfile) | [deploy/chapter-two/Dockerfile](../../deploy/chapter-two/Dockerfile) |
| 容器持久化根 | `/app/data` | `/app/data`，但必须是另一份卷/宿主目录 |
| PVF | Manager 上传至 `data/resources`，或显式只读挂载 | 匹配的服务端 PVF 挂载 `/app/resources/Script.pvf` |
| 客户端 | ACT1 对应原生补丁 | 第二章宿主/broker/原生客户端资源；不混入 ACT1 DLL |
| 远程模式 | 根 bridge 或单独 host/IPv6 模板 | [第二章 remote/compose.yml](../../deploy/chapter-two/remote/compose.yml)，原生 Linux host 网络 |

两章可以有相同的逻辑角色数据，但不能共享可写存档。需要迁移时复制**停服后的备份**到独立数据根，验证后再使用。第二章服务端和客户端 PVF 有不同格式；必须是相配的导出，不能直接交换文件。

## 2. 数据目录与权限

通用发行结构详见 [DEPLOYING](../../DEPLOYING.md#文件与可持久化目录)。运维要特别保护：

| 路径（相对数据根） | 内容 / 操作约束 |
| --- | --- |
| `config/server.json`、`config/manager.json` | 有效配置；环境变量可能覆盖它们，修改文件不保证立即生效 |
| `management/` | 管理账号哈希、会话密钥、一次性设置凭据；属于敏感备份内容 |
| `game/state.json` | 游戏账号和角色状态；只能由一个实际服务端写入 |
| `resources/` | 上传 PVF 的按内容摘要保存副本；旧配置/备份可能仍引用旧文件 |
| `logs/` | 有限保留的日志；事故时先复制关联时段，避免轮换丢失 |
| `backups/` | 配置备份；**不是整份数据灾备** |
| `exports/` | 玩家包导出；不是游戏存档备份 |
| `client-updates/` | 旧自托管更新索引/包，使用该功能的实例需要保留 |

`--data-dir <绝对路径>` / `DFLEGACY_MANAGER_DATA` 可把数据与程序分开。CLI 优先于同用途环境变量；服务配置中 `DFLegacy__...` 环境覆盖会在管理页标注。容器以非 root `app` 用户运行；宿主绑定目录必须允许该 UID 写入。先检查镜像中的实际 UID，不用 root 启动来掩盖权限问题。

不要绕过 Manager/Server 的 OS 文件锁；不要启动第二个实例“测试一下”同一数据目录。关闭浏览器不会停服；停止游戏不会关闭 Manager。

## 3. 首次启动与健康检查

### ACT1 发行包

将可信归档解压至新目录，核对摘要与 `release-manifest.json`。Windows 运行 `Start.cmd`，Linux 运行 `./start.sh`；不要从源码输出目录随意拼装一个生产安装。

首次 Manager 使用一次性 setup token，创建部署管理账号：账号名为 3–32 位字母、数字或 `_ . -`，密码为 12–128 个字符，规则见 [ManagerIdentity](../../src/DFLegacy.Manager/ManagerIdentity.cs)。token 位于数据根 `management/setup-token.txt`，只在受控本机终端/页面使用，不复制进聊天、日志、截图或交接文档。设置完成后失效删除。部署管理账号与游戏管理员/普通游戏账号是不同身份。

随后上传有权使用的兼容 PVF（上限 1 GiB），配置玩家可达的公布地址、API URL 和端口，需要时建立初始游戏管理员；首次模板关闭测试账号播种和自动启动。已有账号冲突时明确处理，禁止用重建存档解决。

### ACT1 Compose

在独立部署目录准备同版 `compose.yaml` 与 `.env`；`DFLEGACY_IMAGE` 指向确定版本或不可变 digest。先做：

```bash
docker compose config
docker compose ps
```

`config` 只解析配置，但可能展开环境值，输出含秘密时不要上传。得到部署授权且核对卷名/绑定目录后才 `docker compose up -d`。保留原 Compose 项目名和卷映射；随便改目录/项目名可能新建另一卷，看起来像“账号全没了”。

### 第二章

独立 Windows 包的 `Start.cmd` 调用 [Start.ps1](../../deploy/chapter-two/Start.ps1)，清理当前进程中可能污染测试的 DFLegacy/ASP.NET 环境，数据在包内 `data/`，默认本机 `127.0.0.2`。不要指望父终端中某个 DFLegacy 环境变量一定穿透此脚本。

远程 Linux 使用 [remote/compose.yml](../../deploy/chapter-two/remote/compose.yml)，至少核对 `C2_IMAGE`、`C2_SERVER_HOST`、`C2_DATA_DIR`、`C2_PVF_PATH`。`C2_DATA_DIR`/PVF 必须已存在，模板 `create_host_path:false`；不要让拼错路径变成空目录。管理端口仍绑定 `127.0.0.2`。host 网络直接占宿主端口，不需要再加 `ports`；不是 Docker Desktop 的可移植默认方案。

`C2_SERVER_HOST` 与客户端 `ChapterTwoServerHost` 使用同一 DDNS 域名，不带协议、端口、路径；移除旧静态 IPv4 字段，避免双重配置。`C2_BIND_HOST` 默认 `::`，仅 IPv4 主机可用 `0.0.0.0`。客户端每次进入游戏重新解析，连接中 IP 变化不保证无缝恢复；须重新登录并等待 DNS 缓存/DDNS 生效。

### 健康检查的正确解释

```powershell
# Windows，仅访问本机示例。第二章把地址换成 http://127.0.0.2:48092。
curl.exe -sS -i http://127.0.0.1:18080/health
curl.exe -sS -i http://127.0.0.1:18080/ready
```

Linux 使用 `curl`。`/health` 成功只证明 Manager 可响应；`/ready` 只有私有通道及所有必要监听就绪才成功，正常停服时应为 503。因此容器健康检查使用 `/health`，不能以停服 `/ready=503` 为由反复重启服务。

## 4. 端口、地址与远程暴露

以下是源码模板的默认值，不代替实际配置。监听 `0.0.0.0/::` 不是玩家可使用的公布地址。端口修改须同时调整服务配置、客户端 Relay、Compose/宿主映射和防火墙。

| ACT1 默认端口 | 用途 |
| --- | --- |
| TCP 18080 | 部署 Manager，根 Compose 默认只映射宿主回环 |
| TCP 8081 | 账号 API / 游戏内网页 |
| TCP 18081 / 18082 | GM 后台 / 玩家中心，根 Compose 默认对外绑定；公网使用前必须核对访问控制和 HTTPS |
| TCP 2311 / 7001 / 8080 | 登录、频道、附加入口 |
| TCP 7100–7105 | 可选地区频道；根 Compose 当前未列这些映射，启用时需另外配置 |
| TCP 7004 / 7110–7112 | 可选 PvP；映射存在不代表业务已启用 |
| TCP+UDP 7005 | ACT1 回退中继 |
| UDP 2311 / 7002 / 7003 | 角色 / 玩法数据报 |
| TCP 18083 | 旧自托管更新；默认回环，使用公共 V4 不等于需要打开该口 |

第二章远程模板使用 TCP `42312,47001,47005,48090,48091,7100–7105,7004,7110–7112`；UDP `42312,47005,47012,47013`。按实际启用项开放，不把 README 的全部候选端口都视为必须公网暴露。第二章本地基础模板禁用 PvP、GM/玩家独立面板和 GameProbe，但远端模式的 [ServerSupervisor](../../src/DFLegacy.Manager/ServerSupervisor.cs) 会为子进程启用 PvP。应核对 Manager 的 effective 配置和子进程环境，不能只读基础模板判断远端监听。

远程管理优先保持 loopback，用受控 SSH 隧道或 HTTPS 反代。反代需设置 `DFLEGACY_MANAGER_ALLOWED_HOSTS`，仅在 `DFLEGACY_MANAGER_TRUSTED_PROXIES` 填真实可信代理 IP，保留正确 Host/Origin。CSRF/cookie 不是传输加密。第二章管理器监听 `127.0.0.2`，隧道目标不能误写为 `127.0.0.1`。

## 5. 安全升级与备份

没有验证过的整份备份，不进行真实数据升级。每次先写下目标章节/实例、旧新版本和 digest、数据绝对路径/卷名、预期停机影响、回滚位置和验收项目；已获授权的范围内执行，未获授权的真实部署切换须先确认。

操作顺序：

1. 通知并确认维护窗口；先在 Manager 停游戏，确认状态 stopped、`/ready=503`、游戏子进程已退出。
2. 备份整份数据根；若配置/管理身份也需一致快照，随后停止 Manager/容器再复制。外部 PVF、`.env`、Compose 文件和镜像身份单独一起留存。
3. 将备份恢复到另一个隔离目录或卷，核对至少 `state.json`、配置、管理数据和 PVF 摘要；恢复演练不能连接真实玩家端口或与原服务并发写数据。
4. 新程序解压到新目录，或拉取明确版本镜像；保持数据映射。不要覆盖正在执行的 EXE/DLL。
5. 切换程序/镜像，检查 Manager health、日志、有效配置和资源；再启动游戏检查 ready、账号登录和关键业务。
6. 记录验证结果，保留原版本和备份到明确保留期，不能“升级看起来成功”就立即清理。

Windows 停服后的目录备份示例（只复制，不覆盖目的）：

```powershell
$dataRoot = (Resolve-Path -LiteralPath 'D:\replace-with-service-data').Path
$backupRoot = 'E:\replace-with-backup-root\maintenance-new'
if (Test-Path -LiteralPath $backupRoot) { throw 'Choose a new backup path' }
New-Item -ItemType Directory -Path $backupRoot | Out-Null
Copy-Item -LiteralPath $dataRoot -Destination (Join-Path $backupRoot 'data') -Recurse
Get-FileHash -LiteralPath (Join-Path $dataRoot 'game/state.json'),(Join-Path $backupRoot 'data/game/state.json') -Algorithm SHA256
```

前置条件：确认服务已停且目的不在源数据目录内部；磁盘空间足够；若目录含重解析点先识别并单独处理。备份含账号与密钥，需受控权限。哈希一致只证明该文件复制一致，不代表所有配置/外部资源均已备份。

Linux 宿主绑定目录可在停服后用受控备份软件或 `tar` 完整保存所有者/模式。命名卷不要猜宿主内部路径；先 `docker volume inspect <已核实卷名>`，再使用明确只读挂载源卷的备份流程。仓库没有可泛用于所有部署的一键灾备脚本；不能编造某个目录复制就涵盖全部数据。

升级已获授权后，Compose 的常见动作是 `docker compose pull <服务名>` 再 `docker compose up -d <服务名>`。ACT1 服务名是 `dflegacy`，第二章 remote 是 `chapter-two`。`pull` 只下载，`up` 才可能重建服务。**禁止 `down -v`、删卷、删整个 data 或复制测试档覆盖日常档。** Podman 用其 compose 实现，并保留相同文件/项目身份。

## 6. 回滚不是仅换旧 EXE

先停止新游戏服务并保存故障日志、有效配置、原始数据和失败版本身份，避免调查证据被旧版本改写。确认旧版本能否读取新数据格式：如果不确定，不让旧进程直接打开唯一的新存档。

安全选择是把升级前整份备份恢复到**新隔离数据目录/卷**，用原程序/digest 和配套 PVF 验证，再按授权切换。恢复备份会丢失备份后的游戏进度，必须明确时间范围，不能由 agent 默默决定。若要保留新进度，需要专门验证的数据迁移方案，而不是手改 JSON 抹去字段。

配置回滚要连同其引用的 PVF 保留。`data/backups` 只是配置备份；`resources` 的旧文件可能仍被旧配置使用，不能一律清理。

## 7. 客户端更新、修复与本地数据

V4 说明见 [CLIENT_UPDATES_V4](../../CLIENT_UPDATES_V4.md)。必须保留玩家账号/服务器/显示配置；更新通过受支持字段迁移，不以新模板全文件覆盖 `launcher.json`、`launcher.chapter2.json` 或 `Config.ini`。

固定入口、托管界面、更新器分别是 `DFLegacy.Launcher.exe`、`DFLegacy.Launcher.App.exe`、`DFLegacy.Updater.exe`。正常打开 GUI 可复用完成校验记录；实际进入游戏、显式检查、修复等路径仍有内容校验。不能仅凭登录窗打开就认定 PVF/NPK 全量健康。

| 客户端位置 | 作用 / 注意 |
| --- | --- |
| `.dflegacy/v4/installed.json` | 已提交安装版本/清单；用 `release.tag` 辅助判断实际资源版本 |
| `.dflegacy/v4/validated.json` | 完成校验记录；与来源、目录、架构、类型、清单及关键文件属性绑定 |
| `.dflegacy/bootstrap/` | 固定入口恢复副本、记录、锁、日志；不能删除来绕过失败 |
| 更新事务、缓存、备份 | 中断后先恢复，不能把未决事务当“垃圾”清理 |
| `.dflegacy/chapter-two/settings/DNF_CHINA.cfg` | 第二章游戏声音/画面设置；原 `.keyset` 保存按键，不应误删 |
| `Logs/DFLegacy/Sessions/` | 第二章本次会话临时文件和原生日志，退出后归档相关日志 |

离线时，已验证安装满足后置条件且无待恢复事务才可继续使用；下载目标 B 失败不等于已装 A 失效。反之，“网络坏了”也不能免除本地安装已损坏的拦截。

需要修复时先退出对应游戏/登录器。存在可信、匹配版本的独立 `DFLegacy.Repair.exe` 时，可按其 `--repair-client <绝对目录>` 入口处理 V4，但修复程序必须放在目标目录外；不要执行目标目录中已疑似损坏的登录器。旧 V3 未决事务遵循旧桥接恢复流程，不直接套 V4 删除状态文件。固定入口及所有恢复副本均损坏时使用匹配章节/架构的完整可信启动包。

更新事务备份保存原始字节，即使原文件本来已经损坏，所以“回滚完成”不代表“旧版健康”。文件占用时只处理已识别的相关进程，并遵循当前用户授权；不按进程名批量终止所有游戏/诊断助手。不要改 `DFLegacy.Bootstrap.ini` 的哈希绕过校验。

## 8. 诊断顺序与日志

先复核实际时间/时区、章节、构建/模块哈希、当前 PID+启动时间、配置来源和资源身份，再解释错误。保留第一次失败日志，避免反复启动覆盖现场。

| 症状 | 先看什么 | 不应立即做什么 |
| --- | --- | --- |
| 管理页正常但不能登录 | `/ready`、服务状态、PVF、私有管理通道和业务监听 | 以 `/health` 成功宣称游戏已就绪 |
| Windows 端口错误 10013 | `netsh interface ipv4 show excludedportrange protocol=tcp`、本机监听表 | 杀无关进程或删除系统保留规则 |
| 地址正确但远端连不上 | 宿主防火墙、云规则、公布地址、IPv4/IPv6、Relay 端口与 API URL | 把 `0.0.0.0`/`::` 填入客户端目标 |
| 更新失败或无限修复 | `update-*`、bootstrap 日志、未决事务、来源/架构、磁盘/文件锁 | 删除 `.dflegacy`、覆盖清单或变更旧标签资源 |
| 第二章启动失败/进图掉线 | 同次 `chapter-two-events-*`、`chapter-two-client-*`、服务端同时间日志 | 把任意 stderr 行当根因 |
| 卡顿/内存增长 | 第二章 perf CSV、对应渲染器/地图/输入场景、同轮对照 | 把截图造成的停顿算作业务问题，或只看平均 FPS |

容器只读排查示例：

```bash
docker compose ps
docker compose logs --tail 200 dflegacy
docker compose images
```

第二章将服务名替换为 `chapter-two`；完整 inspect/config 可能含环境秘密，输出需本地审查脱敏后才分享。

客户端 `Logs/DFLegacy/Incidents/` 的前缀：`error-` 为启动/配置/更新异常，`networkerror-` 为有上下文的连接故障，`crash-` 为原生未处理异常或明确异常退出，`update-` 为每次校验/下载/安装摘要。单独一个非零退出码不能证明具体崩溃原因；正常退出、正常 EOF/换频道、用户取消不能算 crash。

第二章：`chapter-two-events-*.log` 是 broker 事件，`chapter-two-client-*.log` 为原生上下文；从当前说明记录的 v1.0.13 起 stderr 警告用 `[stderr]` 前缀保留，启动判断依赖握手和退出状态。`libpng ... iCCP` 警告本身不证明启动失败。需要详细捕获时使用已确认版本支持的 `--detailed-crash-capture`；详细内存片段只留本地，反馈前审查。

第二章性能：`chapter-two-perf-*-interval.csv` 约每 2 秒一个区间，`*-total.csv` 为累计，`*-slowframes.csv` 为 ≥50 ms 前台帧；时间是 UTC，P95/P99 是桶上界，归因使用 `trace_complete=1`。Ctrl+F10 面板收起不停止日志。切出游戏暂停帧采样，不能拿前后台混合时段直接比较。

自动基本诊断与本地详细 Incidents 不同。当前用户偏好在 `%LOCALAPPDATA%/DFLegacy/Diagnostics/preference.json`；自动队列是有界结构化基本报告，本地原始转储不走该上传接口。不要把完整目录/转储直接上传；本地日志可能含账号相关上下文、路径或内存。符号定位必须匹配实际加载的 PE 与私有 PDB，见 [构建证据](build-test-release.md#9-签名与发布证据)。

## 9. 需要明确授权的操作

如本次会话尚未授权，应在完成只读调查和准备具体方案后确认：真实服务启停/部署切换；开放公网端口或改 DNS；发布/删除远端附件或镜像；数据恢复/导入/结构迁移；清理卷/资源/备份；终止非本次测试拥有的进程；GM 发奖、点券/黑钻、封禁、改密码、删角色。若已有同范围明确授权，继续完成，不重复询问。

测试充值开关默认关闭。开启后可能允许按角色名增加测试财产，不是正常支付集成，不能在真实实例随手开启。奖励接口超时/响应丢失时先查原操作 ID/批次结果；不要换新 ID 重试造成重复发放。浏览器 sessionStorage 中的恢复线索关闭标签页可能丢失，应先记录必要 ID（不记录凭据）。
