# 第二章客户端原生 PVF 修复与 v1.0.3 在线更新

用户整合包中的 `Script.pvf` 是服务端封装。此前仅放行文件摘要，未完成游戏读取验收，导致登录后出现 `std::bad_alloc`，退出码 `e06d7363`。本轮将同内容的原生客户端封装通过在线更新交付，不重新分发完整整合包，不覆盖用户的服务端 PVF。

两种封装的 69,399 条资源记录逐条核对 ID、路径、长度、偏移、密钥和 CRC；资源主体逐字节一致。详见 `G:/codex-archives/c2-integrated-startup-fix-20261114/pvf-content-equality.json`。客户端摘要为 `d0b4ac799896398bfd97a5cfcf12e8be0a603a44a965fd5478ab8c43a99b8c9b`。

启动器和打包工具增加服务端封装拒绝检查。本地已安装版本先于在线发布时，按本地完整安装凭据核验，不把较旧清单当作降级目标；同序号清单被替换仍拒绝。正式配置保持调试关闭、GitHub 更新开启；更新保留已有账号和玩家设置。

## 已完成验收

归档目录：`G:/codex-archives/c2-integrated-startup-fix-20261114`。

- `package-red.log` / `package-green.log`：先复现、后修复；11 项打包测试通过。
- `core-tests.log`：更新核心 801 条断言通过，跨章测试通过。
- `acceptance-x64-v102.log`、`acceptance-x64-v101.log`、`acceptance-x86-v101.log`：真实安装事务、全部受管理文件、设置字节保留、正确 PVF 和事务结束状态通过。
- `game-acceptance.json`、`screenshots/01-channel.png`、`02-character.png`、`03-town.png`：修复后 x64 客户端实测登录、选频道、原有角色加载、进城和物品栏；游戏音频会话已核对为零音量。x86 未另做真机登录。
- `publish-verify-final.log`：92 项新增附件经过完整下载及哈希核验。701 项复用资源来自同一份 v1.0.1 发布，其完整匿名下载证据为 `G:/codex-archives/c2-public-release-20261114/c2-anonymous.log`；本轮复核对应发布身份、公开状态、附件大小和摘要。

额外全量历史发布工具测试有 164 项通过，1 项旧 PackageProbe 工程缺少编译引用，记录于 `release-tests.log`，没有据此宣称全量历史测试全部通过。本次正式包构建和上述真实更新验收另行通过。首次真机启动经历已有地址预留重试，该原生机制未在本轮修改。

## 发布

- 已公开客户端 `v1.0.3`，更新序号 7，发布 ID `445170462`，固定公共源代码快照，未向公开仓库推送私有源码。
- 地址：<https://github.com/firekula/DFLegacy/releases/tag/v1.0.3>。
- 退出游戏并重新打开原登录器即可在线更新，无需重新下载完整整合包。
- 最终匿名下载复核状态以归档中的 `c2-publication.json` 和 `anonymous-*.log` 为准。
- 最终结果：`anonymous-retry.log` 中 92 项新增附件完整匿名下载及哈希校验通过；701 项历史资源的发布身份、公开状态和摘要复核通过，沿用上述既有完整匿名下载证据。`c2-publication.json` 已记录 `published=true`、`anonymousVerified=true`。
- 从独立的匿名访问路径确认公开发布页可访问，并显示为最新正式版。
- 服务端旧任务占位修复由独立镜像 `v1.0.2` 提供，见同目录 `2026-11-14-c2-retired-quest-slots.md`。

原始用户 ZIP、F 盘用户客户端、主分支和线上服务未改动。私有验收目录中的本机登录资料不属于发布产物。
