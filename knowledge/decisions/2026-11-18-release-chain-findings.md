# 发布前发现的链路事实（2026-11-18 凌晨）

## 线上现状（匿名 API 实测）

- `v1.0.15`：`latest`，manifest `version=v1.0.15`、`sequence=25`、入口 `f9a40d1e…`（413,032 B）。
- `v1.0.15-bridge`：`pre`，`sequence=24`、入口 `8391eacc…`（与 v1.0.14 入口相同）。
- 两者清单附件同名：`DFLegacy-client-update-v4-chapter-two-startup-v2-normal-win-x64.json`。
- 两者都由 `7e5eb30` 构建，**早于** A/C1/B/S1（`ab3a42f` 之后），即线上 bridge/target 都不含本轮修复。

## 关键发现：预检看不到“要换入口”的目标

- 启动预检走 `RepairClient.RunAsync`，它创建 provider 时**没有**传 `allowStartupMigration`
  （[RepairClient.cs:125-126](.tmp/upgrade-wt/src/DFLegacy.Launcher/Updates/RepairClient.cs)），
  于是 `ChapterTwoReleaseProvider.includeManifest` 只保留“入口哈希与本地相同”的清单。
- 登录器内更新走 `V4UpdateSession`，它传了 `allowStartupMigration: StartupEntryMigration.IsBridge(installed)`
  （[V4UpdateSession.cs:36-37](.tmp/upgrade-wt/src/DFLegacy.Launcher/Updates/V4UpdateSession.cs)），因此能看到 seq 25 目标。
- 后果：对官方第二章来源的安装，**boot 预检不会发现 v1.0.15 目标**，入口迁移是登录器内更新器的职责；
  B（预检里的“入口变更 → 延迟更新”）只在预检能看到目标时才触发（例如来源非官方、或过滤器被绕过）。
  这也解释了我们伪造 bridge 收据时的现象：预检只看到线上 bridge（seq 24），与本地伪造的同序号清单冲突，
  报“同序号内容被替换”，而不是进入延迟更新。

## 由此决定的发布范围（待授权）

要让测试机跑上本轮修复，必须重新发布 **bridge**（tag `v1.0.15-bridge`、seq 24，入口不变）：
- 把 app/updater 换成本工作区构建并签名，重算清单与分卷；
- 线上 target `v1.0.15`（seq 25）也需同样重建，否则迁移后运行的仍是旧 app。
- 代价：bridge 同序号换清单，**现有 bridge 安装会触发“同序号内容被替换”硬失败**，必须用新的启动包重装。

## 阻塞

- 本会话没有发布凭据：`DFLEGACY_GITCODE_RELEASE_TOKEN` 未设置（`Publish-V1015.py:46` 要求）。
- 没有 dry-run 模式；脚本会删除并重传线上附件（`Publish-V1015.py:64-76`）。
- 签名证书可用：`CN=FireKula`（2029-11-17 到期）。

## 待用户决定

1. 提供 `DFLEGACY_GITCODE_RELEASE_TOKEN`（或自行执行发布脚本）；
2. 确认接受“bridge 同序号换清单 → 测试机需重装”的代价；
3. 确认是否顺带重建 `v1.0.15` 目标（否则迁移后仍是旧 app）。
