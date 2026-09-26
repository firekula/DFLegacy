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
| 启动器账号 UI：WPF + HandyControl 交互式登录壳（注册/登录/改密/找回/账号管理/设置），headless 路径保留 | `DFLegacy.Launcher`（`MainWindow`、`Views/*`、`App.xaml.cs`）、`DFLegacy.AccountClient` |

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

### 2.1 启动器账号 UI

启动器已从无头程序改为 WPF 交互式登录壳（HandyControl 3.5.1）：无参运行打开主窗口（账号下拉 + 密码 + 服务端状态灯），注册、登录、改密、找回、账号管理与设置（主题）都在界面内完成；带 `--headless`/`--account`/`--account-index` 等开关时仍走无头路径（不打开界面，退出码保留）。

**已实测（可复现，2026-09-26）**

- `dotnet build src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -p:DesignTimeBuild=true`：成功，0 警告 0 错误（本机没有 CMake，`DesignTimeBuild=true` 跳过原生 ijl15 目标，见 §5）。
- `dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release`：退出 0、全绿，含 `AccountClientSmokeTests` 契约测试（桩 `HttpMessageHandler`：注册 201/409/400、改密 200/401/400、找回 200/401/400、健康检查 200 与不可达）。
- `dotnet run --project src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -p:DesignTimeBuild=true -- --check-d3d9`：退出 0（headless 回归，无 UI 依赖）。
- `dotnet publish src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -p:DesignTimeBuild=true -o ./dist/launcher-test`：产物含 `DFLegacy.Launcher.exe`、`DFLegacy.AccountClient.dll`、`HandyControl.dll`、`launcher.json`；此方式不含 `ijl15.dll`（需含 ijl15 的分发产物用带 C++ 工作负载的 VS msbuild 另行发布，见 BUILDING）。

注：此前记录的 8 项与 PVF 版本相关的**既有**冒烟失败（PVF 缺 `etc/sptable.etc`、文件数/商城定价/宠物进化数据与测试基准不同）在本次运行中未复现；换用不同版本的 PVF 时，数据相关测试仍可能再次与基准不一致。

**手动验证清单（需要桌面与真实客户端，接手时优先做）**

1. 启动器无参运行 → 出现主窗口。
2. 点注册 → 注册成功 → 账号自动加入下拉列表 → 启动游戏 → 游戏内登录成功。
3. 改密、找回、账号管理、设置（主题切换）各走一遍。
4. 远程 IPv6 场景：账号 API（8081）默认无法触达，属预期（中继不转发 8081，见 DEPLOYING §6.4）；如需开启按该节操作。
5. 确认注册的账号已写入 `launcher.json`。
6. 客户端退出后启动器随之退出（中继驻留路径）。
7. headless（`--headless`/`--account X`）仍能端到端启动游戏。

**已知限制**

- 账号的 `RememberPassword=false` 时配置只保存空密码：UI 流程不受影响（启动前在界面输入密码即可）；但无头启动（`--headless`/`--account X`）会因「Accounts[i].Password 不能为空，也不能包含空白、问号或双引号。」校验失败——无头模式需要存储密码。
- 找回密码成功后，本地记住的密码不会自动更新：下次启动若勾选记住密码，需在 UI 重新输入一次（游戏内登录用的才是新密码）。
- `launcher.json` 经 UI 保存后会重写整个文件，中文注释会丢失（System.Text.Json 不支持注释往返，已确认接受；字段说明以 README 为准）。

### 2.2 8081 鉴权与角色（本次新增）

已实测（用本仓库构建出的服务端，临时 `DataPath`、非默认端口）：

- **权限矩阵 28/28 通过**：公开接口（`/health`、`/`、`/admin/*`、注册、找回、登录）匿名可用；`/api/**` 无 token 一律 `401`；玩家查自己角色 `200`、查他人 `403`、管理接口 `403`；管理员全通；未登记路径 fail-closed（`401`）；登录接口末尾斜杠与不带一致。
- **角色即时生效**：提升/降级后旧 token 立刻 `401`；重新登录后按新角色放行；「不能降级自己」「至少保留一名管理员」两条约束返回 `400`。
- **限流（安全复审后重设计，两级预算）**：单机连续 8 次错误密码后仍 `401`，**正确密码照常登录**（消除了"匿名者可把管理员锁在门外"的拒绝服务）；独立实例上把来源额度调成 3，第 4 次起**正确密码也 429**（来源级硬挡，证明单机爆破被卡死）；找回密码共用同一预算。
- **改密/找回吊销旧 token**：改密成功后原 token 立即 `401`。
- **双栈绑定**：`Admin.Host=::` 时 netstat 同时出现 `0.0.0.0` 与 `[::]`，`127.0.0.1` 与 `[::1]` 都能访问。
- **smoke 覆盖**：`AdminAuthSmokeTests` 断言 40 条路由权限表 + token 生命周期（滑动续期/绝对上限/登出/按用户吊销）+ 两级限流语义（多来源才锁账号）+ 有界内存（2.5 万条刷量后仍在上限内）+ 角色模型与引导名单 + 旧存档兼容 + QQ 码长度。
- 启动器侧：改密流程改为「先 `POST /api/auth/login` 取 token，再带 token 改密，成功后登出」，客户端契约测试断言了 `Authorization: Bearer` 头确实发出。
- **启动器客户端 × 真实服务端端到端 14/14**（真实 `AccountServiceClient` 打真实进程：注册 → QQ 码过短被拒 → 登录 → 旧密码错误 → 带 token 改密 → 改密后旧 token 失效 → 新密码登录 → 引导管理员登录 role=1 → 玩家 token 改他人密码被拒 → 找回密码 → 登出）。
- **独立安全复审**：对 8 个提交的整份 diff 做了对抗性复审（含自建最小复现工程逐条探测路径归一化/编码绕过），逐条核对 36 条路由的权限档位，未发现权限表绕过、越权或 token 泄漏到日志；复审提出的两个必须修复项（F1 找回密码无限流 → 无认证账号接管；F2 锁定型拒绝服务）与三项建议（F3 token 吊销与绝对上限、F8 末尾斜杠/fail-closed、F9 no-store 与暴露告警）均已修复并复测。

待人工（需要浏览器/桌面）：

- 打开 `http://<地址>:8081/admin/`，用**管理员**账号登录：看到 邮件/单人通知/广播/副本权限 + 用户角色，能提升/降级；退出登录后回到登录框。
- 用**普通玩家**账号登录：只看到「我的角色」，没有管理功能；访问 `/admin/player.html` 同样只看自己的角色。
- 充值页（`/client/client_pay.htm`、`/client_diamond.htm`）现在需要管理员身份：页面会弹登录框，登录后自动重试。**这一段只做了语法与接口层验证，未做浏览器点击验证**，请点一次确认。
- 错误路径：密码错误提示、来源限流触发后的「尝试过于频繁」提示。

已知限制：

- 老部署升级后，**原来的匿名 curl/脚本会全部 `401`**，需要先登录取 token（破坏性变更，见 ADMIN_API.md §8）。
- token 存在服务端内存：服务端重启后需重新登录（换取的是"角色变更/改密即时吊销"的能力）。
- 8081 默认对外（`::`）。**远程部署必须限制来源并在前置代理终止 HTTPS**，否则 token 明文可被嗅探。
- 密码哈希仍是无盐 SHA-256 + 固定前缀，QQ 码明文存在 `state.json`（既有实现，未在本次改动范围；建议后续换 PBKDF2/Argon2 + 加盐）。
- 反向代理后所有请求共用代理 IP，来源级限流会一起计数（见 ADMIN_API.md §1.2 的处理建议）。

## 3. 可复用的验证方法

### 3.1 中继 / IPv6 端到端

1. 另起一个只监听 `::1` 的服务端（**换端口**，避免与本机服务端冲突），`DataPath` 指向存档的**副本**。
2. 启动中继：`DFLegacy.Relay.exe --server ::1 --tcp <后台端口> --udp <玩法端口>`。
3. 验证：`curl http://127.0.0.1:<后台端口>/health`（公开）与带 token 的 `/api/admin/characters`——先 `POST /api/auth/login`（`Admin.Users` 里的账号，或临时把该实例的 `Admin.Users` 配上）拿 token，否则会 `401`；UDP 用单字节 `0x01` 发到玩法端口，应收到 7 字节回包（首字节 `2`）。

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
