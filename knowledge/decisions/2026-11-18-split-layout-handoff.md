# Handoff：拆分布局修复（2026-11-18 夜间）

## 基线

- 分支 `codex/chapter-two-full-starter-upgrade`，HEAD `5463dee`（A/C1/B/S1 + 两份证据文档）。
- 工作区仍有**用户本轮的未提交布局改造**（`ClientLayout.cs`、`PathPolicy.cs`、stub/startup、4 个皮肤 PNG 等），
  本轮所有构建都包含它们，但未提交；不要覆盖。
- 构建入口（一次性，均在 `.tmp/` 下，已被 gitignore）：
  - `.tmp/v1014-clean/build-roles.ps1`：按 v1.0.14 + 第二章 payload 构建 app/updater。
  - `.tmp/v1014-clean/rewrite-receipt/`：把安装收据重写成与本地二进制一致（支持 tag/seq/仓库覆盖）。
  - `.tmp/v1014-clean/run-fixture.ps1`：无人值守跑 stub，自动确认迁移框。
  - `.tmp/v1014-clean/capture-window.ps1`：PrintWindow 截图（cua 会话不可用时的替代）。

## 本轮结论（含证据）

| 项 | 结论 | 证据 |
| --- | --- | --- |
| 迁移 | flat → `DFOLauncher.exe` + `client\` 成功 | 目录结构 + 迁移日志 |
| 本地校验 + 登录 | 787 文件完整校验后交接，登录窗 `地下城与勇士 · 第二章`，版本 `v1.0.14` | `launcher.log` `offline=False`；`2026-11-18-split-layout-gui-verify.md` |
| C1 换根复用 | 改名整个安装目录后 **12.3 s** 进登录窗，`offline=True` 快路径 | 同上 |
| B 延迟更新 | 自动化集成用例绿；**GUI 路径被发布状态挡住** | `线上清单的同序号内容被替换`（线上 bridge 同为 seq 24） |
| A 源不可达 | 单测 + 集成用例覆盖；GUI 走了 fast path 的 `offline=True` | `--engine`、`--bootstrap-transport-tests` |

## 下一波（按优先级）

1. **发布决策（阻塞 GUI 验 B）**：线上目前只有 `v1.0.15-bridge`（seq 24），没有 `v1.0.15`（seq 25）目标；
   要真机验证“延迟更新 → 登录页”，需要发布 seq 25 目标，或重新生成与本地字节一致的 bridge。
   发布后用 `G:\tmp\dfo-v1014-clean-fixed`（seq 23、修复版）跑第一次，再跑第二次即可看到 defer。
2. **S1 弹窗真机验证**：需要让 RM 在该进程失败；测试宿主限制（`RmStartSession` 29）仍存在，
   可在真机上用“占用文件但阻止 RM”的场景验证。
3. 若用户要“冻结版”夹具（更新源不可达，固定住修复版）：`rewrite-receipt` 已支持第 5 个参数覆盖 repository。

## 现场清单

- 保留：`G:\tmp\dfo-v1014-clean-fixed`（干净 flat 交付夹具）、`G:\tmp\dfo-v1014-gui-fixed-renamed`（含本次验证日志）。
- 已删除：`dfo-v1014-bridge-fixed`、`dfo-v1014-verify`。
- 无残留 `DF*` 进程。

## 下一会话只读

- 本文 + `2026-11-17-split-layout-transport-fallback-fix.md`、`-receipt-reanchor.md`、`-entry-deferral.md`、`-occupancy-message.md`、`2026-11-18-split-layout-gui-verify.md`。
- 代码：`UpdateEngine.cs`、`InstallationStore.cs`、`BootstrapLayout.cs`、`BootstrapUpdateClient.cs`、`RepairClient.cs`、`FileLockOwners.cs`。

## 更新（2026-11-18 08:00）：v1.0.16 已发布并验收

- 发布：GitCode `v1.0.16`，seq 26，提交 `a9bc51d`，入口不变（`8391eacc…`）；附件 6 个、匿名校验通过。
- 干净 v1.0.14 夹具实测：迁移 → 自动更新到 v1.0.16 → 登录窗显示 v1.0.16。
- 换根复核：改名安装目录后 13.6 s 进登录窗，`offline=True`。
- 现场：`G:\tmp\dfo-v1016-renamed`（v1.0.16 已安装版，可继续测试）。
- 仍待：启动包（新 stub）未重建；S1 弹窗真机验证；RM 测试宿主限制。