# DFLegacy 部署指南

本文档说明 DFLegacy 的部署形态、目录结构、配置要点、数据备份与升级流程。
从源码构建见 [BUILDING.md](BUILDING.md)，后台接口细节见 [ADMIN_API.md](ADMIN_API.md)；
容器部署使用仓库根目录的 `Dockerfile` 与 `compose.yaml`，见 §5。

## 1. 部署形态与平台能力

三种常见形态：

- **A. Windows 单机**：服务端与原版 32 位客户端同机运行。功能完整，可用启动器免手工配置。
- **B. Linux / macOS 无头服务端**：服务端跑在类 Unix 主机上，Windows 客户端通过局域网或公网连接。不能使用启动器与客户端补丁。
- **C. 容器（Docker / Podman）**：只把服务端放进容器，见 §5。宿主可以是 Linux、Windows（Docker Desktop）或 macOS。

各组件的能力边界：

| 组件 | Windows | Linux | macOS | 说明 |
| --- | --- | --- | --- | --- |
| `DFLegacy.Server` | 支持 | 支持 | 支持 | 纯托管代码 + Kestrel，无强制原生依赖 |
| `DFLegacy.Protocol`、冒烟测试 | 支持 | 支持 | 支持 | 纯托管代码 |
| 服务端容器镜像 | 支持 | 支持 | 支持 | 见 §5，宿主只需能运行 Docker 或 Podman |
| `DFLegacy.Launcher` | 支持 | 不支持 | 不支持 | WinForms 程序，且需要检测 D3D9 设备 |
| `ijl15.dll` 客户端补丁 | 支持 | 不支持 | 不支持 | 32 位 Windows DLL，替换客户端模块 |
| `DFLegacy.RandomNative.dll` | 支持（可选） | 不支持 | 不支持 | 仅提供 RDSEED 硬件随机，缺失时自动回退 |
| 游戏客户端本身 | 支持 | 不支持 | 不支持 | 原版 32 位 Windows 客户端 |

服务端在类 Unix 平台上的差异：

- **依赖**：Linux 的最小化系统需要自行安装 ICU 与 tzdata（见 §4）；macOS 系统自带，无需额外安装。
- **路径大小写**：Linux 文件系统区分大小写，若用解包目录代替 `Script.pvf`，目录名需与 PVF 内的虚拟路径一致（全小写）。macOS 默认大小写不敏感，无此问题。
- **随机源**：两者都不加载 `DFLegacy.RandomNative.dll`，自动回退到操作系统 CSPRNG。


## 2. 目录结构

服务端目录（`<server>`，下列两者之一）：

```text
<server>/
  DFLegacy.Server.exe              # Windows 启动入口
  DFLegacy.Server.dll              # Linux 下用 dotnet DFLegacy.Server.dll 启动
  DFLegacy.Server.deps.json
  DFLegacy.Server.runtimeconfig.json
  DFLegacy.Protocol.dll
  server.json                      # 必需配置，见 §7.1
  wwwroot/                         # 后台管理页与客户端页面
  data/state.json                  # 首次启动后生成，见 §9
  DFLegacy.RandomNative.dll        # 可选，仅 Windows 64 位
```

同机运行客户端时，服务端目录还会包含启动器相关内容：

```text
  DFLegacy.Launcher.exe
  launcher.json                    # 见 §7.2
  ijl15.dll                        # 由启动器安装到客户端目录
  Config.ini                       # 客户端配置模板，客户端缺失时复制过去
```

## 3. Windows 单机部署

1. 按 [BUILDING.md](BUILDING.md#4-windows只构建服务端) 与 [BUILDING.md](BUILDING.md#61-启动器仅-windows) 把服务端和启动器发布到同一目录，例如 `dist\DFLegacy.Server`。
2. 把 `Script.pvf` 放到客户端目录（或任意位置）。
3. 编辑 `server.json`：把 `ScriptPvfPath` 指向实际的 `Script.pvf`。相对路径以**服务端程序目录**为基准。
4. 编辑 `launcher.json`：设置 `ClientPath` 为客户端 `DNF.exe` 的完整路径，以及登录账号。保持 `StartServer: true` 时，启动器会按需拉起本地服务端。
5. 运行 `DFLegacy.Launcher.exe`。它会：
   - 安装 `ijl15.dll` 到客户端目录（同名模块已存在时先备份为 `ijl15.original.dll`；补丁只在进程内存中生效，不改写客户端 PE 文件）；
   - 客户端缺少 `Config.ini` 时复制默认配置（已存在的用户配置优先，不会被覆盖）；
   - 检查 D3D9 HAL 设备可用性；
   - 启动客户端，并在需要时启动服务端。
6. 只想跑服务端时，直接在服务端目录执行 `DFLegacy.Server.exe`。

启动器支持以下命令行开关，便于脚本化与排障（`--no-dialog` 抑制交互弹窗）：

```powershell
DFLegacy.Launcher.exe --check-d3d9                  # 只检查 D3D9 可用性
DFLegacy.Launcher.exe --install-ijl15               # 只安装 ijl15 补丁
DFLegacy.Launcher.exe --self-test                   # 自检并输出启动器日志
DFLegacy.Launcher.exe --account-index 1             # 临时选用 launcher.json 中第 2 个账号
DFLegacy.Launcher.exe --account another-account     # 按账号名临时选用
```

## 4. Linux 与 macOS 无头服务端部署

1. 按 [BUILDING.md](BUILDING.md#5-linux-与-macos构建并发布服务端) 发布服务端到 `<server>`（macOS 见该节的 RID 说明）。
2. 放入 `Script.pvf`，并把 `server.json` 的 `ScriptPvfPath` 改成该平台上的路径（例如 `/srv/dflegacy/Script.pvf`）。**Windows 风格的 `D:\...` 路径在类 Unix 系统上无法解析**：服务端仍会启动，但脚本库为空并只打一条警告，表现为物品、任务、技能等全部缺失。
3. 按 §6 调整监听与下发地址，按 §6.2 放通防火墙。
4. 启动：

   ```bash
   cd <server>
   dotnet DFLegacy.Server.dll          # 任何发布方式都可用
   # 或
   ./DFLegacy.Server                   # 发布产物自带应用宿主时
   ```

5. 最小化容器/系统还需要：
   - **ICU**：`runtimeconfig.json` 里 `System.Globalization.Invariant` 为 `false`，缺少 ICU 会启动失败。安装 `libicu`（Debian/Ubuntu：`icu` 相关包），或设置 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`（会退化为不变区域，内部仍用托管代码页处理 CP936/CP949）。
   - **tzdata**：疲劳每日重置使用本机时区（`TimeZoneInfo.Local`），缺少时区数据时会退化为 UTC。

### 4.1 systemd 单元示例

```ini
[Unit]
Description=DFLegacy emulator server
After=network-online.target

[Service]
Type=simple
User=dflegacy
WorkingDirectory=/srv/dflegacy/server
ExecStart=/usr/bin/dotnet /srv/dflegacy/server/DFLegacy.Server.dll
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

服务端是普通控制台程序（Generic Host），能正确处理 `SIGTERM`/`SIGINT`，无需 Windows 服务封装。

## 5. 容器部署（Docker / Podman）

仓库根目录提供 `Dockerfile`、`.dockerignore` 与 `compose.yaml`，**只构建服务端**。容器内的服务端与 §4 的 Linux 无头部署等价，供 Windows 客户端远程连接；启动器与两个原生模块不入镜像（游戏客户端是 32 位 Windows 程序）。

### 5.1 构建与运行

```bash
docker build -t dflegacy-server:local .
# 或
podman build -t dflegacy-server:local .
```

运行（挂载 PVF，并设置客户端可达地址）：

```bash
docker run -d --name dflegacy-server \
  -e DFLegacy__GameplayDatagram__AdvertisedHost=192.168.1.10 \
  -v /srv/pvf/Script.pvf:/pvf/Script.pvf:ro \
  -v dflegacy-data:/app/data \
  -p 2311:2311/tcp -p 7001:7001/tcp -p 8080:8080/tcp \
  -p 2311:2311/udp -p 7002:7002/udp -p 7003:7003/udp \
  -p 127.0.0.1:8081:8081/tcp \
  dflegacy-server:local
```

编排方式见 `compose.yaml`：Docker 用 `docker compose up -d --build`，Podman 用 `podman compose up -d --build` 或 `podman-compose up -d --build`。

### 5.2 镜像约定

| 项 | 值 |
| --- | --- |
| 基础镜像 | 构建阶段 `sdk:10.0`，运行阶段 `aspnet:10.0`（自带 ICU 与 tzdata） |
| 运行用户 | 非 root 的 `app`（UID 1654） |
| PVF | 镜像**不含**，固定从 `/pvf/Script.pvf` 读取，需只读挂载 |
| 存档 | `/app/data/state.json`，建议挂载具名卷或已授权的绑定目录 |
| 默认监听 | 登录、角色、玩法均为 `0.0.0.0`；后台管理保持 `127.0.0.1` |
| 健康检查 | 容器内 `curl http://127.0.0.1:8081/health` |

镜像内已清空基础镜像的 `ASPNETCORE_HTTP_PORTS`/`ASPNETCORE_URLS`，因为服务端始终按自身配置绑定监听地址。

### 5.3 用环境变量配置

服务端的环境变量**优先于** `server.json`，键名用双下划线表示层级，因此无需为改路径或端口去挂载配置文件：

| 环境变量 | 等价配置项 |
| --- | --- |
| `DFLegacy__ScriptPvfPath` | `DFLegacy.ScriptPvfPath` |
| `DFLegacy__DataPath` | `DFLegacy.DataPath` |
| `DFLegacy__Entrance__Host` | `DFLegacy.Entrance.Host` |
| `DFLegacy__GameplayDatagram__AdvertisedHost` | `DFLegacy.GameplayDatagram.AdvertisedHost` |
| `DFLegacy__Admin__Host` | `DFLegacy.Admin.Host` |
| `DFLegacy__Experience__MonsterMultiplier` | `DFLegacy.Experience.MonsterMultiplier` |

### 5.4 必填项与常见坑

- **必须设置 `DFLegacy__GameplayDatagram__AdvertisedHost`**：它是下发给客户端用于连接玩法 UDP 端口的地址，容器无法自行得知宿主机的对外地址。缺失时启动日志会打印明确警告（因为此时会把 `0.0.0.0` 下发给客户端）。
- 后台管理接口没有鉴权（见 [ADMIN_API.md §1.2](ADMIN_API.md#12-安全边界)）。镜像默认只在容器内监听 `127.0.0.1`；要从宿主机访问需设置 `DFLegacy__Admin__Host=0.0.0.0`，并**只把 8081 映射到宿主机回环地址**（`compose.yaml` 已如此配置）。
- 用**绑定挂载**保存档时，宿主机目录必须让 UID 1654 可写，否则写盘失败：

  ```bash
  mkdir -p data && sudo chown 1654:1654 data
  ```

  用**具名卷**则开箱即用（首次会继承镜像内 `/app/data` 的属主）。
- Podman + SELinux 环境需给挂载项追加 `:Z`（或 `:z`），`compose.yaml` 中有示例。
- 容器内没有 `DFLegacy.RandomNative.dll`，随机源自动回退为操作系统 CSPRNG，日志会注明，属预期行为。
- 需要区分「监听地址」与「通告地址」时（容器、NAT、多网卡），用 `GameplayDatagram.Host` 绑定、用 `GameplayDatagram.AdvertisedHost` 通告；Linux 上也可以改用宿主网络（`--network host`），此时端口映射失效、服务端直接占用宿主机端口，但通告地址仍需按客户端实际使用的地址设置。

## 6. 网络配置

### 6.1 监听与下发地址

`server.json` 里各项 `Host` 的职责不同，**远程部署时最容易踩的坑是只改了监听地址、没改下发给客户端的地址**：

| 配置项 | 作用 | 是否下发给客户端 |
| --- | --- | --- |
| `Entrance.Host` / `Port` / `AdditionalPorts` | 登录入口 TCP 监听地址与端口 | 否 |
| `CharacterDatagram.Host` / `Port` | 角色数据 UDP 监听地址与端口 | 否 |
| `GameplayDatagram.Host` / `PrimaryPort` / `SecondaryPort` | 玩法 UDP 监听地址与端口 | 两个端口**是**；地址取决于下一行 |
| `GameplayDatagram.AdvertisedHost` | 下发给客户端的玩法地址；留空则回退为 `Host` | **是** |
| `Admin.Host` / `Port` | 后台管理 HTTP 监听地址与端口 | 否 |
| `GameProbe.Host` / `Port` | 可选协议探针，默认关闭 | 否 |
| `Channel.Host` | **未参与监听或下发** | 否 |

因此：

- 默认全部绑定 `127.0.0.1`，**只服务本机**。
- 只为局域网/公网客户端提供服务时，把 `Entrance.Host`、`CharacterDatagram.Host`、`GameplayDatagram.Host` 改为 `0.0.0.0`（或指定网卡地址）。
- **玩法地址必须让客户端能访问到**：`GameplayDatagram.Host` 同时承担"监听"和"下发"，只有在服务器确实能绑定对外地址时才可直接填局域网/公网 IP。容器、NAT 或多网卡场景下无法两者兼顾，此时把 `GameplayDatagram.Host` 设为 `0.0.0.0` 监听、把 `GameplayDatagram.AdvertisedHost` 设为客户端实际使用的 IP 或域名。只绑定了通配地址却没设置 `AdvertisedHost` 时，启动日志会打印明确警告（因为会把 `0.0.0.0` 下发给客户端）。
- `Channel.Port` 应与 `Entrance.AdditionalPorts` 中的频道端口（默认 `7001`）保持一致；服务端用它判断连接是否落在频道端口上。
- `GameProbe.Port` 默认 `7001`，与 `Entrance.AdditionalPorts` 中的 `7001` **相同**。探针默认关闭所以不冲突；若要启用，必须先把二者错开。
- 后台管理接口没有登录与鉴权（见 [ADMIN_API.md §1.2](ADMIN_API.md#12-安全边界)），默认只监听 `127.0.0.1`。**不要把 `Admin.Host` 直接改成 `0.0.0.0` 暴露到不可信网络**；确需远程管理时，在反向代理或防火墙层做认证与来源限制。

### 6.2 端口清单

| 端口 | 协议 | 用途 | 配置项 |
| --- | --- | --- | --- |
| `2311` | TCP | 登录入口 | `Entrance.Port` |
| `7001` | TCP | 频道入口 | `Entrance.AdditionalPorts`（与 `Channel.Port` 对应） |
| `8080` | TCP | 登录入口附加端口 | `Entrance.AdditionalPorts` |
| `8081` | TCP | 后台管理 HTTP | `Admin.Port` |
| `2311` | UDP | 角色数据报 | `CharacterDatagram.Port` |
| `7002` | UDP | 玩法主端口 | `GameplayDatagram.PrimaryPort` |
| `7003` | UDP | 玩法次端口 | `GameplayDatagram.SecondaryPort` |

对外开放时至少需要放通上述 TCP 与 UDP 端口。`Entrance` 与 `CharacterDatagram` 默认复用 `2311`，注意 TCP/UDP 是两套规则。

## 7. 配置文件

### 7.1 `server.json`

节名为 `DFLegacy`。完整字段说明见 [README 的常用配置](README.md#服务端)，部署时优先关注：

- `ScriptPvfPath`：PVF 路径。相对路径以服务端程序目录为基准；更换 PVF 后需重启服务端重新加载。
- `DataPath`：存档路径，默认 `data/state.json`，相对路径同样以程序目录为基准。
- `Admin.Host` / `Admin.Port`：后台管理监听地址。
- `GameplayDatagram.AdvertisedHost`：下发给客户端的玩法地址，容器与 NAT 部署必填；留空则回退为 `GameplayDatagram.Host`。见 §6.1。
- 其余各项 `Host`：见 §6.1。
- `Experience.MonsterMultiplier` / `Experience.ClearMultiplier`：经验倍率，默认 `1.0`。
- `Drop.*`：掉落开关与倍率。
- `EnablePacketTracing`：协议跟踪开关，排障时临时打开。

服务端在启动时会读取程序目录下的 `server.json`（必需文件），并开启文件变更重载。

### 7.2 `launcher.json`（仅 Windows 启动器）

- `ClientPath`：客户端 `DNF.exe` 的完整路径（必填）。
- `EntranceHost` / `EntrancePort`：客户端要连接的登录入口，需与 `Entrance` 配置一致。
- `Account` / `Password`，或 `Accounts` + `AccountIndex`：登录凭据。默认示例是 `test/test`，**不要随发布分发含真实密码的配置**。
- `StartServer`：是否由启动器按需启动本地服务端。
- `D3D9Adapter` / `D3D9TimeoutSeconds` / `D3D9RetryIntervalSeconds`：D3D9 设备检测参数。

### 7.3 客户端 `Config.ini`（IJL15）

由启动器在客户端缺少该文件时复制默认配置，之后以客户端内的文件为准。客户端渲染始终为 `640×480`，`Width`/`Height` 只拉伸外层窗口，建议保持 4:3。完整字段见 [README](README.md#启动器)。

## 8. 可选客户端兼容文件

仓库内 `third_party/d3d8to9/v1.15.1/d3d8.dll` 是第三方的 D3D8→D3D9 转换模块（附带其 `LICENSE.md`）。它**不会被任何构建或发布流程自动部署**，仅在需要改善渲染兼容性时手动放到客户端目录（与该目录已有的同名模块二选一），并自行评估其许可与兼容性。

## 9. 数据、备份与多实例

- 所有服务端状态保存在 `DataPath` 指向的单个 JSON 文件（默认 `data/state.json`），首次启动时会自动创建。
- 首次创建时会写入默认数据：账号 `test`（密码 `test`）与一个预设角色。**对外提供服务前应先改密或删除该账号**，不要保留默认凭据。
- 写入方式为"同目录临时文件 + 原子替换"，因此**不要同时启动多个服务端实例写同一份状态文件**，否则会互相覆盖。
- 升级、迁移或修改配置前先备份该文件与 `server.json`：

  ```powershell
  Copy-Item .\data\state.json .\backups\state-$(Get-Date -Format yyyyMMdd-HHmmss).json
  ```

  ```bash
  cp data/state.json "backups/state-$(date +%Y%m%d-%H%M%S).json"
  ```

- 当前为 JSON 文件存储，未接入数据库，也不支持多实例共享写入。

## 10. 升级流程

1. 停止服务端（务必先停，避免写入中断）。
2. 备份 `data/state.json` 与 `server.json`。
3. 用新构建替换程序文件。**保留** `server.json`、`data/`、`wwwroot/` 中的本机内容。
4. 重新启动并查看日志确认数据加载正常。

`server.json` 与 `launcher.json` 在发布时按"较新者优先"复制，直接 `dotnet publish` 到现有目录会用仓库里的示例配置覆盖你的本机配置（示例里的 `ScriptPvfPath` 是 `D:\DFLegacy\...`）。因此：

- 发布到现有目录前，先备份这两个文件，发布后改回本机值；或
- 先把新版本发布到空目录，再手工同步程序文件。

## 11. 运维命令

服务端支持两个"执行完即退出"的命令行开关，用于批量维护。在服务端目录执行：

```powershell
# 给所有角色增加点券（必须为正整数）
DFLegacy.Server.exe --add-cash-all 1000

# 用满级转职角色整体替换 test 账号的角色列表
DFLegacy.Server.exe --reset-maximum-transfer-roster
```

```bash
# Linux（框架依赖发布）
dotnet DFLegacy.Server.dll --add-cash-all 1000
```

- 两者都会直接写入 `DataPath` 指向的状态文件，执行前请先停止服务端并备份状态文件。
- `--reset-maximum-transfer-roster` 会**整体替换** `test` 账号的角色列表（原有角色不再保留），属破坏性操作。
- 参数不合法或执行失败时退出码为 `2`，成功时为 `0`，可用于脚本判断。

## 12. 验证与排障

启动后按顺序检查：

1. **进程存活**：Windows 看控制台窗口；Linux 用 `systemctl status` / `journalctl -u dflegacy -f`。
2. **HTTP 健康检查**：

   ```bash
   curl http://127.0.0.1:8081/health
   # {"status":"ok","service":"dflegacy-emulator"}
   ```

3. **后台管理页**：浏览器打开 <http://127.0.0.1:8081/admin/index.html>（访问 `/admin` 会重定向到该页）。页面能列出副本即说明 PVF 已加载。
4. **端口监听**：确认 §6.2 的 TCP/UDP 端口都已监听。

常见问题：

| 现象 | 可能原因 | 处理 |
| --- | --- | --- |
| 启动日志出现脚本库告警，游戏内物品/任务/技能全空 | `ScriptPvfPath` 指向的文件不存在或路径风格不对（例如在类 Unix 系统上沿用了 `D:\...`） | 改为目标平台上的真实路径后重启 |
| 本机能进游戏，其他机器连不上 | 各项 `Host` 仍是 `127.0.0.1` | 按 §6.1 改为 `0.0.0.0` / 对外地址，并放通防火墙 |
| 能登录但进频道/进图卡住或掉线 | 玩法地址是客户端不可达的值（`0.0.0.0`、内网地址，或容器场景下没设 `AdvertisedHost`） | 设置 `GameplayDatagram.AdvertisedHost`（或改 `Host`）为客户端可达的 IP/域名后重启 |
| 启动日志警告 clients cannot dial | 绑定了通配地址但未设置 `GameplayDatagram.AdvertisedHost` | 见 §5.4 与 §6.1 |
| Linux 上启动即失败并提示 ICU 相关错误 | 缺少 ICU | 安装 `libicu`，或设置 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` |
| 疲劳重置时间不是本地零点 | 系统缺少时区数据 | 安装 `tzdata`（macOS 自带） |
| 启动器报告缺少 32 位 `ijl15.dll` | 发布时未构建原生补丁 | 按 [BUILDING.md §6.1](BUILDING.md#61-启动器仅-windows) 用带 VS C++ 的环境发布 |
| 客户端启动后无窗口/渲染异常 | D3D9 不可用或驱动问题 | 用 `DFLegacy.Launcher.exe --check-d3d9` 检查；必要时手动放置 `third_party/d3d8to9` 的 `d3d8.dll`（见 §8） |
