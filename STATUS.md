# 项目状态与接续说明

本文面向"换一个对话/换一个人继续开发"的场景，记录**当前实现状态、已验证与未验证的部分、可复用的验证方法、以及待办**。
模块职责见 [README.md](README.md)，构建见 [BUILDING.md](BUILDING.md)，部署见 [DEPLOYING.md](DEPLOYING.md)，接口见 [ADMIN_API.md](ADMIN_API.md)。本文不重复这些内容。

## 1. 近期改动的功能

| 功能 | 关键位置 |
| --- | --- |
| SP 按经验进度渐进发放；PVF 缺少 `etc/sptable.etc` 时用内置回退表（取值与原版一致） | `CharacterSpCatalog`，8 处给经验调用点 |
| 清除地图任务：救援图替换当次副本的 boss 房并按 boss 房结算 | `DungeonCatalog.TryApplyQuestMap` / `IsQuestRescueRoom` |
| 客户端 IPv6 中继：核心库 + 独立 exe + 由启动器进程内托管 | `DFLegacy.Relay.Core`、`DFLegacy.Launcher` |
| 玩法 UDP 会话接纳真 IPv6 对端（不再被 `MapToIPv4()` 压扁） | `GameplayDatagramService.NormalizeAddress` |
| 绑 `::` 时 UDP 也是真双栈（此前只服务 IPv6） | `DualStackUdp.Bind` |
| 服务端/中继可用环境变量覆盖配置（`DFLegacy__X__Y`） | `Program.cs`（在 server.json 之后注册） |
| 容器镜像与编排（host 网络、双栈、PVF/存档挂载） | `Dockerfile`、`compose.yaml` |

## 2. 验证状态

**已实测（可复现）**

- 服务端在本机与 Linux 容器中都能启动，并加载真实 `Script.pvf`：48326 个脚本文件、10942 个物品定义、869 个任务、43 个副本、1025 个地图。
- 真实存档可正常加载（含既有角色），启动时只发生预期的每日疲劳重置。
- 原生随机桥在 Windows 上生效（`provider=RDSEED, nativeBridge=True`）；容器内按预期回退为操作系统 CSPRNG。
- 中继：TCP（HTTP 健康检查与数据接口）与 UDP（NAT 探测往返）经 IPv4→IPv6 转发成功；独立 exe 与"启动器进程内托管"两条路径均验证。
- 启动器：`--self-test`、`--check-d3d9`、`--install-ijl15` 通过；完整启动路径（托管中继 → 把客户端入场地址改写为 `127.0.0.1` → 客户端退出后中继停止并退出）用一个替身客户端实测通过。
- 容器：镜像构建、`healthy`、挂载 PVF、在 IPv4 与 IPv6 绑定下的 TCP/UDP/后台均可用。

**未验证（需要真实环境，接手时优先做）**

- 用真实 `DNF.exe` 的完整游戏内流程（登录、进图、任务、SP 增长、召唤师转职）。
- 公网 IPv6 的真实可达性：本机没有公网 IPv6，容器/服务端的 IPv6 行为只在回环与容器网络内验证过。
- Podman：本机 podman 机器损坏（WSL 发行版缺失，`WSL_E_DISTRO_NOT_FOUND`），只做了 Docker 验证；Dockerfile 只用经典指令，理论上 Buildah 可用。
- 冒烟测试中有 8 项**既有的**与 PVF 版本相关的失败（本仓库 PVF 缺 `etc/sptable.etc`、文件数/商城定价/宠物进化数据与测试基准不同），与上述改动无关；改动前后失败项一致。

## 3. 可复用的验证方法

### 3.1 中继 / IPv6 端到端

1. 另起一个只监听 `::1` 的服务端（**换端口**，避免与本机服务端冲突），`DataPath` 指向存档的**副本**。
2. 启动中继：`DFLegacy.Relay.exe --server ::1 --tcp <后台端口> --udp <玩法端口>`。
3. 验证：`curl http://127.0.0.1:<后台端口>/health` 与 `/api/admin/characters`；UDP 用单字节 `0x01` 发到玩法端口，应收到 7 字节回包（首字节 `2`）。

### 3.2 容器的双栈行为

- 绑 `::` 时**分别用 IPv4 与 IPv6 客户端**测 TCP、UDP、后台。
- 容器内 `/proc/net/tcp6`、`/proc/net/udp6` 可确认监听地址；容器内 `curl` 可单独验证 Kestrel。
- **排查提示**：若经 Docker 端口映射的 UDP 收不到回包，先确认没有另一个容器占用同一宿主端口——端口冲突会让新容器停在 `Created` 状态而看起来像"UDP 不通"（这个坑实际踩过一次）。

### 3.3 启动器完整路径（不需要真实客户端）

在沙盒目录里造一个会存活若干秒的替身 exe 作为 `ClientPath`、放一个桩 `Script.pvf`，写一份 `Relay.Enabled=true` 的 `launcher.json`，运行 `DFLegacy.Launcher.exe --no-dialog`，然后看日志是否依次出现：`relay ready` → `Started DFLegacy client process …; entrance=127.0.0.1:…`；客户端退出后应出现 `relay stopped` 与 `launcher stopping`。

## 4. 待办 / 已知事项

- 用真实客户端做一次完整试玩（这是目前唯一缺失的端到端环节）。
- **本地部署目录（`DF/dist`）仍是双栈修复之前的构建**：当时服务端正在运行、文件被占用，未替换。该修复对 `127.0.0.1` 部署是 no-op，等停服后替换即可。
- 容器编排默认 host 网络；文件末尾的"桥接 + 端口映射"写法未在真实 IPv6 桥接环境验证过。
- 客户端目录的 `ijl15.dll` 已被启动器替换为 DFLegacy 补丁（原版备份为 `ijl15.original.dll`）。
- 若追求"零额外进程"而把中继进一步做进 ijl15：已评估过，**不建议**——ijl15 目前没有任何线程、入口在 `DllMain` 的加载器锁内，且崩溃会带崩游戏进程。

## 5. 环境与构建注意事项

- Windows 上必须用 **Visual Studio 自带的 MSBuild** 才能构建整个解决方案（含两个 `.vcxproj`）：

  ```powershell
  MSBuild.exe .\DFLegacy.Emulator.slnx -m -restore -p:Configuration=Release
  ```

  `dotnet build` 处理 `.slnx` 会因 `.vcxproj` 报 `MSB4278`（见 BUILDING §8）。发布启动器用 MSBuild 的 Publish 目标：`-t:Publish -p:PublishDir=<绝对路径>\`，它会一并输出启动器、`DFLegacy.Relay.Core.dll` 与 `ijl15.dll`。
- **Git Bash 下 MSBuild 开关要用 `-` 前缀**（`-m`、`-restore`、`-p:Configuration=Release`）。写 `/m` `/restore` 会被 MSYS 当成路径转换成 `M:/`、`D:/Program Files/Git/restore`，报 `MSB1008: 只能指定一个项目`。
- 本机**没有安装 cmake**，因此 `build-native.ps1` 不可用；原生模块改由 `.vcxproj` 构建，产物落在 `artifacts/native/{ijl15,random}/<Configuration>/`，服务端发布时会自动带上随机桥 DLL。
- 服务端的 `server.json`、启动器的 `launcher.json` 都以 `PreserveNewest` 复制到发布目录：重复发布前先备份，或发布后改回本机值（见 DEPLOYING §10）。
