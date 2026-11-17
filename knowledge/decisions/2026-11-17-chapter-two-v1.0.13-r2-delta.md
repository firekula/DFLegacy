# 第二章 v1.0.13-r2 小额过渡与桥接撤回

2026-11-17。把 v1.0.12 → v1.0.13 的过渡从 1.08 GiB 完整包改为约 170 MiB 的入口迁移：先用正常 V4 更新装好 v1.0.13 的托管登录器、更新器、启动描述符和许可说明，再由已验证的固定入口迁移只替换 411,648 字节的入口。随后发布 `v1.0.13-r2` / `v1.0.13-bridge-r2`，撤回两个旧桥接发行版。**没有部署线上服务、没有切换玩家更新源。**

## 发行身份

| 项目 | 值 |
| --- | --- |
| 源码提交 | `18bfc9d3f798e4978243d168402d0f65fcd7a038`（两镜像 `main` 随后为文档提交 `2882ab8`） |
| 过渡发行 | `v1.0.13-bridge-r2`，sequence 20，manifest `6984583f6892decd680a9359b67755eb51e242d85ecc87be2d0b151eb465cf12` |
| 目标发行 | `v1.0.13-r2`，sequence 21，manifest `3ab28d719134046b1cce38f885571b9f0555bbf921cdd17cc83474aaa234f086` |
| 过渡登录器 | `DFLegacy.Launcher.App.exe` = `0c111884d0b07fed5bb6c098f59f2842c05265654d273b9fc8fef24a7939a1e9`，89,100,868 字节，文件版本 `1.0.13` |
| 启动描述符 | `7222ae68d1a6c5759173311599b7cf9625f32fbf27b83891a8b7974a53cc02bc`，204 字节 |
| 保留入口 | v1.0.12 原版 `ebe2fbc158df3a70db7ffdc67de49f204fadfcfa6a1100b9d19f14ee149d7804` |
| 目标入口 | v1.0.13 原版 `cf16fedc2b310214398caad1be8a5d60f3dce2a7be5f749b70a06e177bc345dc` |
| 更新器 / 许可 | v1.0.13 原版 `fcd8e895…`（89,091,366）/ `2590477c…`（645） |

两份清单都是 787 个文件；过渡包相对 v1.0.12 只改 App、Updater、描述符和许可，目标相对 v1.0.13 只改 App 和描述符，过渡包与目标之间**仅固定入口不同**（迁移授权的前提）。

## 下载量

| 阶段 | 下载内容 | 字节 | 约 |
| --- | --- | ---: | ---: |
| 过渡安装 | App + Updater + 描述符 + 许可 | 178,193,083 | 169.9 MiB |
| 入口迁移 | 仅 v1.0.13 固定入口 | 411,648 | 0.39 MiB |
| 合计 | | 178,604,731 | 170.3 MiB |

v1.0.13 完整包是 1,135,865,180 字节；Script.pvf、ImagePacks 等 782 个未变文件不再下载。

## 机制与代码

- 过渡清单保持 v1.0.12 入口，走普通 V4 更新装好 v1.0.13 的托管文件；旧入口校验新描述符后启动新登录器。
- 新登录器发现 `v1.0.13-r2`（`ChapterTwoReleaseProvider.IsStartupMigrationTarget` 只接受两种固定形状），`V4UpdateSession` 命中 `StartupEntryMigration.IsAuthorized` 后走 detached 入口迁移：只下载入口，校验除入口外所有文件与过渡清单逐字节一致，由更新器在 pending 日志保护下替换入口并写入目标收据；中断时 `RecoverStartupEntryMigration` 恢复整个过渡收据和原入口。
- 代码改动：`src/DFLegacy.UpdateCore/ChapterTwoReleaseProvider.cs` 增加第二组目标；`src/DFLegacy.Launcher/Updates/StartupEntryMigration.cs` 改为成对白名单；`StartupEntryMigrationTests.cs` 覆盖第二组、跨组拒绝、Updater 变化拒绝、detached 计划和原子提交。

## 验收

- 自动测试：`--startup-entry-migration-tests` 40 条断言 0 失败；`--r2-manifest-probe` 用仓库内真实清单核对身份、787 文件、迁移授权和“仅入口不同”共 6 项通过。
- 真实双镜像端到端（`G:/tmp/c2r2-github`，原版 v1.0.12 的 787 个文件，源为 GitHub）：
  - 10:58:29 启动 → 10:59:44 `independent-repair/repaired-and-verified`（应用过渡 App/Updater/描述符/许可）→ 10:59:49 过渡登录器启动。
  - 11:00:00 `v4-check/repair-required`（入口不符）→ 11:00:11 `v4-download/download-verified`（目标 `v1.0.13-r2`）→ 11:00:38 `v4-install/installed-verified` → 11:00:42 登录器启动。
  - 最终收据 `v1.0.13-r2`/`3ab28d71…`，入口 `cf16fedc…`，App `0c111884…`，Updater `fcd8e895…`，描述符 `7222ae68…`；无 pending；账号、显示设置、`Config.ini`、`DNF_CHINA.cfg` 保持不变；登录页显示 `v1.0.13`。
  - 该准备目录没有验证收据，所以第一步由独立修复路径应用过渡包；入口迁移本身走的是普通 V4 流程。正常玩家目录已有的验证收据会让过渡包也走普通 V4 下载/安装，这部分由单元测试和上一轮回环验收覆盖。
- 线上发现探针（生产 `ChapterTwoReleaseProvider`）：v1.0.12 客户端发现过渡包（20/17，仍含旧入口）、过渡客户端发现 `v1.0.13-r2`（21，含新入口并判定为迁移目标）、v1.0.13 客户端把 r2 视为普通在线更新；GitCode 真实下载了 204 字节描述符和 411,648 字节入口。GitHub 镜像：发布时匿名回读了全部附件和发行元数据；匿名发现探针在配额恢复后又遇到 GitHub 连接中断（TLS EOF），本轮没有拿到最终结果，详见 `live-discovery.log`；同一生产发现代码的 GitHub 列表/过滤路径在上一轮过渡包验收中已实际通过。
- 两镜像发布 `v1.0.13-r2`（7 个分卷 + 目标清单）和 `v1.0.13-bridge-r2`（过渡清单），所有附件通过匿名长度/SHA-256 回读。

## 旧桥接撤回

- GitHub：`v1.0.13-bridge`、`v1.0.12-bridge` 已删除（含 tag）。
- GitCode：`DELETE /releases/{tag}` 返回 405、`PATCH` 返回 400，且列表不回传 release id，公开 API 无法删除或改写；两个旧桥接发行版仍在 GitCode，但发现逻辑按 sequence 取最新，`v1.0.13-bridge`（19）已被 `v1.0.13-bridge-r2`（20）取代，对 v1.0.12 客户端不再生效。需要手工在 `https://gitcode.com/firekula/DFLegacy/releases` 删除。
- GitCode 保留的 `v1.0.12-bridge` 仍可服务 v1.0.11 老客户端；删除它会让 v1.0.11 无法先升到 v1.0.12。

## 边界与未验证项

- GitHub 侧的过渡安装/下载传输没有做真实端到端；发布时全部附件匿名回读通过，匿名发现探针本轮因 GitHub 连接中断未完成，GitCode 侧完成了真实发现、下载、安装和迁移。
- 已装 v1.0.13 的玩家若继续更新，会收到一次约 85 MiB 的 App 刷新；没有游戏资源下载。
- 仍在旧 `v1.0.13-bridge` 上的客户端会先执行它内置的完整包流程（1.08 GiB），失败才回落到新过渡包；撤回旧 release 不会改变这一已安装行为。
- 没有在真实宽带/代理/限速/断网条件下重复 170 MiB 下载；中断恢复由单元测试的中断点和回滚用例覆盖。

## 证据

`knowledge/decisions/assets/2026-11-17-chapter-two-v1.0.13-r2/`：`r2-acceptance.json`（收据、文件哈希、设置、阶段、发行元数据）、`build-report.json`、`app-evidence.json`、`r2-login.png`、`test-migration-r4.log`、`r2-manifest-probe.log`、`live-discovery.log`、真实客户端的 5 份 update 事故日志和 `launcher.log`。
