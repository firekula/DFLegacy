# v1.0.15 / v1.0.14-bridge 发布与验收（2026-11-18）

## 发布

| 版本 | 状态 | 序号 | 提交 | 清单 SHA-256 |
| --- | --- | --- | --- | --- |
| v1.0.15 | latest | 25 | `c570c68f93fc167597a11db6760625fe7f79682f` | `d50b1464aceac623c8542c950a565c0b3616b8424e3e26f21f18af950335dafd` |
| v1.0.14-bridge | pre | 24 | 同上 | `8a15f92b1c18fa518c4b9af80d24f829a36bf91d6a480c0f9ef0abde16fd1c58` |

- 两份清单均 787 文件、minimum 17；bridge 只替换 App/Updater/Bootstrap.ini/Diagnostics，入口保持 v1.0.14 的 `8391eacc…`；v1.0.15 再替换入口为 `94441d8d…`（435,048 B）。
- 附件：v1.0.15 为 8 个分卷 + 清单 + `DFOLauncher.exe`；bridge 只带清单。全部匿名回读校验通过。
- 发行号 1.0.15，全部 EXE 用 `CN=FireKula` 调试证书签名（不受信任）。

## 本地/线上验收

- 客户端探针（真实清单字节）：`--v1015-manifest-probe` 6/6。
- 套件：入口迁移 50/0、布局抬升 6/0、入口延迟 3/0、传输回退 2/0、V4 helper 38/0、登录 UI 38/0。
- 线上真实第二跳（bridge 夹具 → GitCode target）：收据 24→25，入口 `8391eacc…`→`94441d8d…`，布局抬升，登录页 v1.0.15。
- 线上真实完整两跳（干净官方 v1.0.14）：约 3 分 30 秒内 `v1.0.14`→`v1.0.14-bridge`→`v1.0.15`，无人工干预；最终收据 v1.0.15、入口 `94441d8d…`、游戏在根、登录页 v1.0.15。
- 交付目录：`G:\tmp\dfo-v1015-check`（真实迁移产物）、`G:\tmp\dfo-v1014-clean-check`（本次两跳起点，已升级）。

## 修复的阻断性 bug

第一次第二跳失败：父进程在助手 ready 握手处 `EndOfStreamException`（`update-install`，0x80070026）。根因是助手从 `%TEMP%` 副本运行，`ClientLayout` 指向副本目录；布局抬升后 `HasValidatedReceipt`/`RequireReady` 解析错根目录而判定关键文件缺失。修复：`ValidateV4Inputs` 开头按 `plan.Target` 配置布局；新增回归断言（helper 套件 37→38）。修复后 v1.0.15 以同 tag/seq 重发（当时无客户端装过它），再发布 bridge。

## 边界与已知限制

- 完整零参数测试套件与 `--update-v4-tests`/`--repair-tests` 在本机因 `RmStartSession` 返回 29 失败（最小 .NET 10 控制台可复现，PowerShell/.NET 11 返回 0），属既有环境限制；发行版升级流程中的占用检查本次正常。
- 已安装旧测试版同序号清单的机器会被“同序号内容被替换”拒绝更新，但可继续运行；不要用同 tag/seq 改 target 字节。
- 旧 tag（v1.0.15-bridge、v1.0.16~19）没有 release，不会被发现。

## 证据

- 图文报告：`G:\tmp\dfo-v1015-report\report.html`（含更新窗、登录页截图与两份时间线）。
- 构建与发布脚本：`.tmp/v1015-build/`、构建产物 `.tmp/v1015-release-final2/`。
