# 2026-11-22 本地测试客户端与未完成项交接

## 1. 当前结论

服务端两项功能已经完成并通过定向 smoke test：

- 城镇装扮栏丢弃/销毁（CMD18 `listType=1`）
- 限时装备/称号到期回收（含装备槽、主背包、仓库、邮件）

尚未完成真机视觉验收。阻塞点不在服务端功能，而在本地测试客户端：

- 正常 v1.0.17-r3 包的 Updater 校验失败，原生/外层启动器都提示“更新或自动修复未完成”。
- QA `client-a` 和 v1.0.18 包能启动登录器，但游戏宿主在 DirectInput 初始化处失败。
- Smart App Control 处于开启状态，资源管理器双击未签名启动器会被静默拦截。

启动提示窗另有一个独立 UI 修复未完成：当前 `startup_prompt.cpp` 的分层透明重写还在工作区，编译未通过；已提交的 `0e10a8b` 只是中间版本（矩形深色角），不满足“圆角透明 + 完整显示”的要求。

## 2. 已完成的服务端改动

### 2.1 城镇装扮栏销毁

- 工作区：`.tmp/worktrees/avatar-destroy-v1017`
- 分支：`fix/timed-title-expiry`（基于 `fix/avatar-destroy-v1017`）
- 提交：`23f87fb Fix town avatar destroy on the v1.0.17 release line`
- 主要改动：`src/DFLegacy.Server/EntranceService.cs`
  - CMD18 由只接受 `listType=0` 放宽为接受 `0/1`。
  - 装扮栏删除只持久化装扮栏，回 `listType=1` 并补发完整快照。
  - 主背包和金币销毁逻辑保持不变。
- 回归测试：`tests/DFLegacy.SmokeTests/AvatarDestroySmokeTests.cs`
- 结果：`--avatar-destroy` 0 failures；基线 `ff81308` 上为 RED（`no reply type=1 id=18`）。

### 2.2 限时装备/称号到期回收

- 分支：`fix/timed-title-expiry`
- 提交：`d1f14ba Expire limited equipment and titles on the v1.0.17 release line`
- 主要改动：
  - `src/DFLegacy.Server/ItemCatalog.cs`：解析 `[usable period]`，新增 `UsablePeriodDays`。
  - `src/DFLegacy.Server/AvatarLifetimePolicy.cs`：从“只处理装扮”扩展为“装扮 + 带期限装备”；首次归一化锚定 `now + 天数 * 86400`，到期移除并写墓碑。
  - `src/DFLegacy.Server/AvatarLifetimeSessionRefresh.cs`：合并主背包、仓库并刷新。
  - `src/DFLegacy.Server/EntranceService.cs`：登录/选择角色刷新；装备槽 10 到期触发现有外观/属性刷新。
  - `src/DFLegacy.Server/JsonGameStore.AvatarLifetime.cs`
- 测试：`tests/DFLegacy.SmokeTests/TimedTitleLifetimeSmokeTests.cs`、`AvatarLifetimeSmokeTests.cs`
- 结果：
  - `--timed-title`：0 failures
  - `--avatar-lifetime`：0 failures
  - `--avatar-destroy`：0 failures
  - `--chapter-two-random-box`：0 failures
  - `--town-gold-discard`：0 failures
  - `--active-drop`：0 failures
- 已知限制：
  - 升级后已有的限时称号会在第一次归一化时重新获得一个完整期限，无法补算已流逝时间。
  - 客户端没有做称号倒计时，`add_info` 仍是品质种子；本轮只保证服务端到期移除。
  - 全量 smoke tests 未通过：被无关的 `IdleCeraRefreshSmokeTests` 固定端口 `127.0.0.44:47001` 连接拒绝阻断，重复运行可复现。不要把它算作功能失败，也不要声称全量通过。

## 3. 本地测试环境

### 3.1 服务端

- 测试服务端：`.tmp/avatar-destroy-v1017/server-publish/DFLegacy.Server.exe`
- 配置：`.tmp/chapter-two-local-qa/server.json`
- 存档：`.tmp/chapter-two-local-qa/state.json`
- 日志：`.tmp/chapter-two-local-qa/timed-title-test-server.out.log` / `.err.log`
- 账号：`localqa1`
- 密码：`LocalQa20261116`
- 角色：`拼音验收甲`（60 级鬼剑士）
- 当前配置绑定：
  - Admin `0.0.0.0:48091`
  - Entrance `0.0.0.0:42312`，AdditionalPorts `48090`
  - CharacterDatagram `0.0.0.0:42312`
  - GameplayDatagram `0.0.0.0:47012/47013`，AdvertisedHost `192.168.203.101`
  - Channel `0.0.0.0:47001`
  - FallbackRelay `0.0.0.0:47005`，AdvertisedHost `192.168.203.101`
  - PvP disabled；GameProbe disabled；Gm 面板 `127.0.0.1:48192`
- 健康检查：`http://192.168.203.101:48091/health`
- 本地服务端已停止；交接后需要重新启动。
- `ScriptPvfPath` 指向 `G:/Project/DNF2/Server/resources/Script.pvf`，不要指错。

启动命令：

```powershell
& 'G:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish\DFLegacy.Server.exe' `
  --config 'G:\Game\DNF_ACT1\DFLegacy\.tmp\chapter-two-local-qa\server.json'
```

### 3.2 用户正常客户端（只读，不要修改）

- 路径：`G:\DFLegacy-v1.0.17-r3\DFLegacy-Chapter2-Full-v1.0.17-r3`
- 原始配置已恢复：`ChapterTwoServerHost=games.firekula.site`、GitCode 更新、Relay IPv6、`DisableGitHubUpdates=false`。
- 配置备份：`launcher.chapter2.json.before-local-test-20261122-020956`
- 我临时加的 `client\launcher.chapter2.json`、`launcher.json`、任务脚本和计划任务已删除。
- 该包的 `.dflegacy` 状态：
  - 有 `v4/installed.json`：tag `v1.0.17`，manifest `e4da14c27a0ad67049a3829fba841e64d38c0555f32da523b3b81a63fbcc8512`
  - 有 `bootstrap/recovery.ini`
  - 缺 `v4/validated.json` 和 `bootstrap/startup.dat`
- 现象：原生 `client\DFLegacy.Launcher.exe` 和根目录 `DFOLauncher.exe` 都会启动 Updater；Updater 退出 1，`client/.dflegacy/bootstrap/bootstrap.log` 只有 `Verified updater failed.`。
- 这是交接后的第一优先客户端问题。不要在用户正常包上继续改配置；用副本排查。

### 3.3 测试客户端与已确认差异

| 包 | 启动器 / 宿主 | 现象 |
| --- | --- | --- |
| `.tmp/chapter-two-local-qa/client-a` | 独立 `DFLegacy.Chapter2.Launcher.exe` + `s1c2_chapter2_host` | 登录器能开；游戏宿主 DirectInput 初始化失败；d3d11/d3d8 都失败 |
| `.tmp/full-client-1.0.17` | 原生 `DFLegacy.Launcher.exe` | 与正常包 Launcher/Updater/App 哈希完全一致（`8CFB…` / `E1CF…` / `59B7…`），但缺安装记录；提示“更新或自动修复未完成”；测试后已恢复原启动器 |
| `.tmp/v1018-release/DFLegacy-ChapterTwo-v1.0.18-win-x64` | v1.0.18 完整安装包 | 有 `installed.json`，登录窗口能开；游戏宿主 DirectInput 失败；App/Updater 哈希 `1B4C…` / `AA95…`，与 v1.0.17 不同；测试配置指向 LAN/测试账号 |
| 用户正常 r3 包 | v1.0.17 生产宿主 | 有 `installed.json` 和 `recovery.ini`，缺 `validated.json`/`startup.dat`；Updater 退出 1 |

已确认的宿主日志特征：

- `ORDINARY_DESKTOP_PROCESS appcontainer=0`
- `C:\Windows\SysWOW64\dinput8.dll` 已加载
- 没有 CodeIntegrity/Defender 拦截记录
- 游戏窗口标题 `地下城与勇士`，错误对话框 `公告`
- Smart App Control：`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState=1`

DirectInput 根因尚未证明。已排除/未排除项：

- 不是 AppContainer。
- 不是 dinput8.dll 缺失。
- 可能是游戏窗口在 DirectInput 初始化时不在前台、宿主/启动代理的输入桌面限制，或 Smart App Control 对输入相关代码的拦截。
- 下一步建议：优先修复正常包的安装校验，或使用一个干净安装、平时能正常玩的客户端；其次用最小 DirectInput 探针区分“环境不支持”和“游戏宿主问题”。

## 4. 限时称号测试数据

测试称号位置（`localqa1` / `拼音验收甲`）：

| 位置 | 物品 ID | 名称 | 用途 |
| --- | --- | --- | --- |
| 装备槽 10 | 26040 | 斗士 | 装备槽到期 |
| 背包 10 | 26015 | 幸福恋人 | 背包到期 |
| 背包 11 | 26029 | 彩虹色希望 | 背包到期 |
| 背包 12 | 26034 | 玛尔战争英雄 | 背包到期 |
| 仓库 0 | 26038 | 瞬发者 | 仓库到期 |
| 仓库 1 | 26039 | 测验达人 | 仓库到期 |
| 背包 13 | 26001 | 卡妮娜的希望☆ | 永久对照 |

当前 `state.json` 里这些物品还在，但到期时间已经全部是过去（交接时服务端已停止，未结算）。启动服务端前先重设到期时间，否则登录时会被立即移除。

重设脚本（必须先停止服务端）：

```powershell
$state = 'G:\Game\DNF_ACT1\DFLegacy\.tmp\chapter-two-local-qa\state.json'
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$j = Get-Content $state -Raw | ConvertFrom-Json
$c = ($j.Accounts | Where-Object { $_.UserName -eq 'localqa1' }).Characters[0]
$map = @{ 26040 = 600; 26015 = 660; 26029 = 720; 26034 = 780; 26038 = 840; 26039 = 900 }
foreach ($item in @($c.Inventory) + @($c.Equipment) + @($c.Warehouse)) {
    if ($map.ContainsKey([int]$item.ItemId)) {
        $item.AvatarExpiresAtUnixSeconds = $now + $map[[int]$item.ItemId]
    }
}
$j | ConvertTo-Json -Depth 30 | Set-Content $state -Encoding utf8
Remove-Item "$state.lock" -Force -ErrorAction SilentlyContinue
```

说明：

- 这批物品是直接赋 `AvatarExpiresAtUnixSeconds`，用于验证到期移除/刷新，不是 `[usable period]` 模板路径。
- 模板解析/首次归一化锚定由 `--timed-title` smoke test 覆盖。
- 装备槽到期后先从装备栏消失；背包/仓库到期后分别刷新；仓库需要打开仓库窗口查看。
- 永久称号 26001 用来确认没有误删。

## 5. 启动提示窗修复（未完成）

### 5.1 当前状态

- 主工作区分支：`fix/startup-prompt-transparency`
- 已提交：`0e10a8b Fix startup prompt transparency and clipping`
  - 这是中间版本：GDI+ 颜色键 + 去掉窗口区域，窗口变成矩形，四角深色不透明。
  - 用户已明确拒绝：要圆角透明，并且内容要完整、布局正确。
- 工作区还有未提交的**分层窗口重写**：`src/DFLegacy.Startup/startup_prompt.cpp`
  - 使用 `UpdateLayeredWindow` + 32bpp PARGB DIB，目标是真正的 per-pixel alpha。
  - 当前编译失败，需要先修。

### 5.2 当前编译错误与修法

1. `startup_prompt.cpp` 约 106 行：`Gdiplus::PixelFormat32bppPARGB`
   - `PixelFormat32bppPARGB` 是宏，不能加 `Gdiplus::` 前缀。
   - 改成不带命名空间的 `PixelFormat32bppPARGB`。
2. 约 116 行：`const auto* button = prompt.held ? prompt.pressed.image : prompt.confirm.image;`
   - 推导成 `const Gdiplus::Bitmap*`，`DrawSkin` 需要 `Gdiplus::Image*`。
   - 改成 `Gdiplus::Image* button = ...;`。
3. 约 140 行：`static_cast<REAL>(...)`
   - 当前作用域没有 `REAL`。
   - 改成 `static_cast<float>(...)`。
4. 约 232 行：`new (std::nothrow) Gdiplus::Font`
   - GDI+ 的 `GdiplusBase::operator new` 不接受 nothrow 重载。
   - 改成普通 `new Gdiplus::Font(...)`，用 `try { ... } catch (...) { prompt.font = nullptr; }` 包住；`ShowStartupPrompt` 是 `noexcept`。

### 5.3 目标规格

以 WPF `ClassicMessageWindow` 为准：

- 窗口 `286x170`，`WS_EX_LAYERED`，per-pixel alpha，不要 `SetWindowRgn`。
- 面板图 `classic-message-panel.png` 按原始 `286x170` 绘制在 `(0,0)`。
- 不要额外画深色消息面板；面板图自身带纹理，额外填充会破坏内容。
- 文本区域与 WPF 一致：`Margin="20,44,20,38"`，即 `RectF(20,44,246,88)`，水平/垂直居中。
- 文本颜色 `#F4DCAF`，1px 深色描边；字体宋体，GDI+ 用 14px 近似 WPF `FontSize=13`。
- 按钮 `80x29`，位置 `(98,137)`；使用 `classic-message-confirm0.png` / `confirm2.png`，不要先填深色矩形。
- 键盘 Enter/Escape 确认；鼠标点击按钮；按钮悬停手型光标。
- 不要任务栏图标（`WS_EX_TOOLWINDOW`）。

### 5.4 构建与验证

构建环境：

- CMake：`G:\Game\DNF_ACT1\DFLegacy\.tmp\cmake-venv\Scripts\cmake.exe`（pip 安装的 CMake 4.4.5）
- Visual Studio：`D:\Program Files\Microsoft Visual Studio\18\Community`
- MSBuild 需要可写 `%TEMP%`；沙箱下会访问 `C:\Users\Firekula\AppData\Local\Temp` 失败，必须用非沙箱并设：

```powershell
$env:CMAKE_EXE = 'G:\Game\DNF_ACT1\DFLegacy\.tmp\cmake-venv\Scripts\cmake.exe'
$env:TEMP = 'G:\Game\DNF_ACT1\DFLegacy\.tmp\build-temp'
$env:TMP = $env:TEMP
./src/DFLegacy.Startup/build-native.ps1 -Architecture x64 -RunTests `
  -BuildDirectory 'G:\Game\DNF_ACT1\DFLegacy\.tmp\startup-prompt-build\x64'
```

通过标准：

- 构建 0 错误。
- `bootstrap_policy_and_process` 测试通过。
- 在**测试副本**（不要用正常包）替换 `client\DFLegacy.Launcher.exe`，触发提示窗，截图确认：
  - 四角真正透明，不再有紫色。
  - 面板边框完整，右侧/底部不裁剪。
  - 文本在面板内容区居中，按钮完整，没有深色按钮底框。
- 中间版本截图 `.tmp/prompt-fixed.png` 是矩形深色角版本，不作为验收。

## 6. 未完成项

1. **客户端安装校验/DirectInput**（第一优先）
   - 正常 r3 包缺 `validated.json`/`startup.dat`，Updater 退出 1。
   - 可考虑：用干净安装包重新安装/修复；从可用的完整安装复制 `.dflegacy` 状态（注意路径/DPAPI 绑定）；或使用平时能正常玩的客户端。
   - DirectInput 仍可能在干净客户端上复现；需要最小探针或前台/输入桌面排查。
2. **启动提示窗分层透明**
   - 按第 5 节修编译错误、构建、视觉验收。
3. **设置跟随账号同步**
   - 尚未实现，只做了分析。
   - 需要设计：账号级设置存储、登录/登出同步、本地缓存合并、冲突策略、离线/缓存失效处理。
   - 相关用户反馈：游戏内设置和启动器游戏设置不跟随账号，本地缓存有时失效。
4. **装扮格子容量**
   - 生产仍是 105。
   - 不要直接改 9999；先在测试服做客户端容量探针：104/105/106/200/5000 槽放装扮，确认客户端显示和操作上限。
   - 确认客户端支持后再决定是否扩到 9999，避免“服务端有、客户端看不见”的隐形物品。
5. **限时称号客户端倒计时**
   - 服务端到期回收已完成。
   - 客户端 `add_info` 仍是品质种子；未做倒计时 UI。如需要，另行设计。
6. **真机验收**
   - 装扮栏销毁、限时称号到期都还没有真机视觉验收；客户端跑通后按第 4 节数据执行。
7. **启动提示窗内容布局**
   - 当前中间版本去掉了额外深色面板，但分层重写未完成；按第 5 节目标规格验收。

## 7. 复现命令

服务端构建/发布（在服务端工作区）：

```powershell
Set-Location 'G:\Game\DNF_ACT1\DFLegacy\.tmp\worktrees\avatar-destroy-v1017'
dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release `
  -o 'G:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish'
```

定向 smoke tests：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --timed-title
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --avatar-lifetime
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --avatar-destroy
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --chapter-two-random-box
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --town-gold-discard
dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --active-drop
```

本地服务端启动：

```powershell
& 'G:\Game\DNF_ACT1\DFLegacy\.tmp\avatar-destroy-v1017\server-publish\DFLegacy.Server.exe' `
  --config 'G:\Game\DNF_ACT1\DFLegacy\.tmp\chapter-two-local-qa\server.json'
```

客户端本地配置（用副本，不要改正常包）：

```json
{
  "ClientPath": "..\\DNF.exe",
  "StartServer": false,
  "ChapterTwoServerHost": "192.168.203.101",
  "Relay": { "Enabled": false, "Preference": "ipv4" }
}
```

## 8. 禁止事项与风险

- 不要修改 `G:\DFLegacy-v1.0.17-r3\DFLegacy-Chapter2-Full-v1.0.17-r3` 的游戏文件、`.dflegacy`、账号配置；需要测试就用副本。
- 不要覆盖用户存档、账号配置或正式服状态。
- 不要在正式服上验证称号/装扮改动。
- Smart App Control 开启时，资源管理器双击未签名启动器会被静默拦截；需要计划任务交互令牌或临时关闭/加排除（关闭不可逆，需用户明确同意）。
- 本地 QA 服务端绑定 `0.0.0.0` 并广播 `192.168.203.101`，只用于本机测试；注意端口冲突和防火墙。
- 全量 smoke tests 被 `IdleCeraRefreshSmokeTests` 固定端口 `127.0.0.44:47001` 阻断；不要声称全量通过。
- 终端沙箱会限制 `%TEMP%` 和 MSBuild；原生构建必须在非沙箱、可写 `%TEMP%` 下执行。

## 9. 关键路径索引

| 内容 | 路径 |
| --- | --- |
| 服务端工作区 | `.tmp/worktrees/avatar-destroy-v1017` |
| 服务端发布 | `.tmp/avatar-destroy-v1017/server-publish` |
| 本地 QA 配置/存档 | `.tmp/chapter-two-local-qa/server.json`、`state.json` |
| 本地 QA 日志 | `.tmp/chapter-two-local-qa/timed-title-test-server.out.log` |
| 正常 r3 包 | `G:\DFLegacy-v1.0.17-r3\DFLegacy-Chapter2-Full-v1.0.17-r3` |
| 正常包配置备份 | `launcher.chapter2.json.before-local-test-20261122-020956` |
| 1.0.17 测试副本 | `.tmp/full-client-1.0.17` |
| v1.0.18 测试包 | `.tmp/v1018-release/DFLegacy-ChapterTwo-v1.0.18-win-x64` |
| 原生启动源码 | `src/DFLegacy.Startup/startup_prompt.cpp` |
| 原生启动构建 | `.tmp/startup-prompt-build/x64` |
| 原生启动构建日志 | `.tmp/startup-prompt-build/x64/build.log` |
| CMake venv | `.tmp/cmake-venv` |
| 中间版本提示窗截图 | `.tmp/prompt-fixed.png`（矩形版本，不作验收） |
| 本交接文档 | `knowledge/decisions/2026-11-22-local-test-handoff.md` |

## 10. 建议交接顺序

1. 修正常 r3 包的安装校验（`validated.json`/`startup.dat`/Updater 退出 1），或换一个干净可用的客户端。
2. 用第 4 节重设称号到期时间，启动本地服，完成限时称号和装扮栏销毁的真机验收。
3. 修完 `startup_prompt.cpp` 的分层透明重写，构建并用测试副本截图验收。
4. 做设置跟随账号同步。
5. 做装扮容量探针，确认客户端上限后再决定 9999。
6. 视需要补称号客户端倒计时。
