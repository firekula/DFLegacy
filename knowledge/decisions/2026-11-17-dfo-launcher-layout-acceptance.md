# DFOLauncher 单启动器布局 Phase A 验收

日期：2026-11-17 13:03–13:10（+08:00）
范围：v1.0.13-r2 客户端 → `DFOLauncher.exe` + `client\` 布局；不签名、不发行（Phase A）。
设计：[2026-11-17-dfo-launcher-layout-design.md](2026-11-17-dfo-launcher-layout-design.md)
证据：[assets/2026-11-17-dfo-launcher-layout/phase-a-evidence.json](assets/2026-11-17-dfo-launcher-layout/phase-a-evidence.json)

## 环境与产物

- 源安装：`G:\tmp\c2r2-normal`（v1.0.13-r2；入口 `CF16FEDC…`、App `0C111884…`、Updater `FCD8E895…`）。
- 验收副本：`G:\tmp\dfo-phase-a`。
- 启动器：`artifacts/native/launcher-stub/Release/DFOLauncher.exe`，440,832 字节，SHA-256 `be020178…`，FileVersion 1.0.13.0，ProductVersion 1.0.13+`e911974…`，CompanyName/LegalCopyright FireKula。
- 客户端收据：`v1.0.13-r2`，manifest SHA-256 `3ab28d719134046b1cce38f885571b9f0555bbf921cdd17cc83474aaa234f086`。

## 迁移

- 运行 stub → 经典确认框（[phase-a-confirm.png](assets/2026-11-17-dfo-launcher-layout/phase-a-confirm.png)）→ 确认后完成同盘改名迁移。
- 外层只剩 `DFOLauncher.exe` 和 `client\`；普通文件没有遗漏，迁移日志 `DFOLauncher.migrate` 已删除。
- 收据仍是 `v1.0.13-r2`/seq 21；`validated.json` 根重写为 `G:\tmp\dfo-phase-a\client`，即迁移后客户端完成了一次完整重校验。
- 设置原样保留（长度/SHA-256）：
  - `launcher.chapter2.json` 467 B `27009290A1AA6C1B…`
  - `Config.ini` 28 B `B6416E57D3E1FD8E…`
  - `.dflegacy\chapter-two\settings\DNF_CHINA.cfg` 44 B `76918F2961953722…`
- 登录页显示 `v1.0.13`，账号 `qaaaccount2`、服务器“第二章 · 本机测试”保留（[phase-a-login.png](assets/2026-11-17-dfo-launcher-layout/phase-a-login.png)）。
- 登录器日志：`2026-11-17T13:03:50.2324514+08:00 Bootstrap verified UI handoff; offline=False.`
- 更新检查 incident：`update-20261117-130352-707-73872-…`，`stage=v4-check result=verified`，“gitcode / V4：已确认当前版本 v1.0.13-r2；使用已完成校验的安装记录。”——迁移后更新链正常，客户端复用了已完成校验的安装记录。

## 新布局启动包

- `launcher_layout.py`：`verified=787`；zip 1,553,001,288 字节，SHA-256 `1fc03a0a…`；stub SHA-256 `be020178…`。
- 解压到 `G:\tmp\dfo-phase-a-package` → 运行 stub（无需确认，直接启动）→ 客户端 `validated.json` 根重写为 `G:\tmp\dfo-phase-a-package\client`，登录页 `v1.0.13`，更新检查 `v4-check verified`（`update-20261117-130810-274-47248-…`）。
- 该包是 Phase A dry run，包含测试缓存（约 357 MB）、账号设置和日志；正式 r3/v1.0.14 启动包必须使用干净客户端目录并排除 `cache`/`transactions`/`Logs`。

## 故障注入

- `stub_tests`：CTest `1/1 Passed`（2.53s）；打包测试 `5/5 passed`（0.96s）。
- 启动失败恢复：迁移后客户端 fixture 以 `--result 3` 退出 → 出现“新版客户端启动失败，是否恢复旧布局？”→ 确认 → 旧布局恢复（`DFLegacy.Launcher.exe` 回到外层、`client` 删除、迁移日志删除），stub 退出码 1；三次经典对话框（迁移确认、失败确认、恢复完成提示）全部可操作。
- 中断恢复（半迁移、完整迁移补删日志、损坏日志）由 `stub_tests` 覆盖，见 Task 7。

## 未完成与限制

- **进入游戏未在本次验收重跑**：登录页和客户端运行已验证；进入游戏依赖“本机测试”服务器，本次验收不包含该服务器。公司电脑实机可由用户运行 stub 后验证登录/游戏。
- **公司电脑实机迁移待用户执行**：把 `DFOLauncher.exe`（SHA-256 `be020178…`）放进 `D:\Game\DFLegacy-ChapterTwo-v1.0.5-win-x64`，关闭客户端后双击，确认迁移，等待客户端启动并登录；旧桌面/开始菜单快捷方式会被改指向 `DFOLauncher.exe`。若新版启动失败，按提示恢复旧布局。
- **签名、FireKula 作者字段、v1.0.14 重建、新清单/签名入口迁移对**属于 Phase B，等待受信任 OV/EV 代码签名证书；当前 stub 未签名，不能声称已解决杀软误报。
- 内部 2009 风格 EXE 改名仍作为 v1.0.14 的可选项，需固定命名方案后并入 Phase B。

## 结论

Phase A 本地验收通过：真实 v1.0.13-r2 安装可一次性迁移到 `DFOLauncher.exe` + `client\`，收据/设置/账号/登录页/更新检查均正常；新布局包可解压即用；启动失败可回滚。Phase A 剩余的公司电脑实机迁移由用户执行；Phase B 在证书到位后继续。
