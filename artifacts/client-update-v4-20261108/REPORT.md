# V4 更新与事故报告验收草稿

日期：2026-11-08（Asia/Shanghai）。状态：**Unreleased / 测试中 / 最终整合验收未完成**。

本稿记录 `codex/client-update-v4-diagnostics` 隔离工作区已有证据和剩余门槛，不是正式 v0.1.8 发布说明。未发布正式 GitHub Release、未推送私有 Git 历史、未更新线上服务器。计划测试版本为 `v0.1.8-v4test.1`／`v0.1.8-v4test.2`，均应使用 Gitee `firekula/DFLegacy` 的 `gitee-test` 渠道。

## 可追溯实现

| 子系统 | 已有提交／状态 |
| --- | --- |
| V4 清单、安全路径和全文件 SHA256 | `c9cdd99` |
| 匿名来源、分卷下载和内容缓存 | `0eb9809` |
| 配置迁移与可恢复事务 | `9649c25` |
| CLI、离线 GUI 和单变体发布 | `e9fa9f4` |
| 四变体分组发布 | `be9eaa4`；全部资源先于全部清单，组内附件去重，并补齐实际旧客户端解析 probe 的 UpdateCore 项目依赖。 |
| 登录器助手、独立修复、进程占用交接 | 整合中，不据核心库专项通过推断已交付。 |
| 事件报告、原生最小现场和中继分类 | 已有专项证据，最终登录器与原生整合构建仍需统一确认。 |

主要使用说明：[CLIENT_UPDATES_V4.md](../../CLIENT_UPDATES_V4.md)。原始日志位于工作区 `.tmp/client-update-v4-20261108/`；下表指向这些本地证据文件，不把未保存的控制台印象当成通过依据。

## 已执行的局部验证

| 检查 | 观察结果 | 证据与边界 |
| --- | --- | --- |
| 发布器原完整回归 | 114 passed，29 subtests passed | `publisher-all-release-python-final.log`，53.30 秒；包含原 V2／V3 发布测试。 |
| V4 发布器原专项 | 24 passed，无跳过 | `publisher-v4-final.log`；临时真实文件、49 MiB 分卷、零字节、Windows junction、配置／退役、内容复用、匿名本地 HTTP。 |
| 分组后的发布完整回归 | **120 passed，29 subtests passed**，无跳过 | `publisher-grouped-all-release-final.log`，69.60 秒；包含当前 30 项 V4 用例及旧版归档解析。 |
| 文档中的四变体构建示例 | 四份请求的 preview／build／verify 全部通过；7 份 Markdown 相对文件链接检查通过 | `publisher-doc-validation.log`；使用明确标记的临时小文件，不访问远端。 |
| 离线打包器 | Release 构建 0 警告、0 错误 | `publisher-packager-build.log`；证明编译与工具复制，不宣称已完成所有 DPI／人工 GUI 操作。 |
| Python → C# 清单 | x86／x64 × normal／test 共 4 个通过 | `publisher-cross-language.log`；临时控制台直接链接实际 `Manifest.cs`／`PathPolicy.cs`。这些小文件是 schema fixture，**不是可运行客户端测试包**。 |
| UpdateCore 完整专项 | 206 assertions passed | `transaction-suite-green.log`；包括真实子进程中断、配置和事务恢复。来源与下载 fixture 使用本地 HTTP。 |
| 登录器 V4 适配初轮 | 8 assertions，0 failures | `launcher-v4-green.log`；后续助手／锁进程／修复改动后必须重新执行，不能当最终登录器总验收。 |
| 托管事件报告 | 21 assertions，0 failures | `incident-managed-final.log`；脱敏、上下文、独立事件、保留和异常分类。 |
| Relay | 10/10 passed | `incident-relay-final.log`；正常结束、拒绝连接、重置、关联 ID 与字节计数。 |
| 原生 CMake 套件 | 47/47 passed | `incident-native-final.log`；原生 fixture 与签名／调用约定验证，不扩大为原始游戏每种崩溃都已实测。 |
| 显式详细崩溃抓取 | handled、unhandled、detach、snapshot、rotate 五例均 passed | `incident-capture-tests.log`；只对自行启动的测试子进程操作；循环保留观察到 16 份转储。 |

所有发布器 RED→GREEN 证据保留为 `publisher-red-*`／`publisher-green-*`、`publisher-grouped-red.log` 与 `publisher-grouped-conflict-red.log`。分组发布检查了四变体共用 tag、共享资源只上传一次、全部资源先于清单、GitHub 草稿最终可见、合并后的 1000 附件边界、错误身份在远端变更前拒绝，以及失败后不自动删除或覆盖 Release。

## 真实 Gitee 只读下载证据

这是第三方公开附件的匿名来源／传输验证，**不是本项目测试版本已发布**。

- 仓库 `lifedever/health-tick-release`，版本 `v1.3.13`，Release ID `586860`，附件 ID `2611855`。
- 从仓库专属 `attach_files` 列表取得 ID，再从规范 API 下载入口跳转到 `foruda.gitee.com/attach_file/...`；无 Authorization、无 Cookie。
- 完整读取 1,981,247 字节，SHA256 `3b9129678e9bd6316aeca48b8b74b93b023689ba7302b7d64fc7434099d0ad34`，发布器复读校验通过。
- CDN 的临时签名查询键为 `attname`、`token`、`ts`；证据不保存其值。删除查询再直连会得到 401，说明需要每次从固定附件身份刷新地址。
- Range 请求返回 200 全量内容，完整哈希仍通过。这只证明忽略 Range 的回退路径，不声称该 Gitee 节点支持 206 续传。

原始摘要：`publisher-gitee-live-verification.json`；重定向链摘要：`publisher-gitee-download-probe.json`。提供方 API 字段以实时公开 `https://gitee.com/api/v5/swagger_doc`（5.4.93）核对，下载列表使用 `attach_files`，不从发行说明中的任意链接选取文件。

## 执行中的失败与未完成检查

1. 仓库根目录直接执行 `py -3.12 -m pytest -q` 在收集阶段出现 **15 个错误**，见 `publisher-all-python.log`，不能汇报整库 Python 全绿。`src/DFLegacy.Ijl15/tests/test_incidents.py` 需要原生可执行文件参数；`tools/debug/crash_capture/test_capture.py` 需要抓取程序、测试程序与输出目录参数。另 13 个 `tools/acceptance/tests` 文件缺少正确的 `PYTHONPATH=tools` 导入环境：`test_actor_pid.py`、`test_auto_input.py`、`test_cli_dry_run.py`、`test_cli_orchestrate.py`、`test_collector.py`、`test_combat_gate.py`、`test_model.py`、`test_multi_server.py`、`test_orchestrate.py`、`test_party_dungeon.py`、`test_runner.py`、`test_state_seed.py`、`test_verdict.py`。后续按 [BUILDING.md](../../BUILDING.md#本地测试) 的专用入口与夹具运行。
2. 一次完整登录器测试因未设置 `DFL_LEGACY_PATCH_ARCHIVE` 而停止，见 `incident-launcher-full.log`。V4／incident 专项通过不抹去这个未完成项；最终整合需要提供已校验的旧版本归档并执行完整套件。
3. `core-integrated.log` 曾在强杀子进程后立即恢复时报告 `.dflegacy/update.lock` 共享冲突；后续未修改锁策略的完整套件多次达到 206 assertions。`transaction-notes.txt` 记录了这一观察及另一个已修正的测试夹具选择问题。最终整合仍须核对该时序边界，不能把一次重跑通过当成已根因修复。
4. 共享源码整合后，旧归档解析测试 `test_github_publish.py::UpdatePackageTests::test_real_client_parsers_accept_all_produced_archives` 曾因 `PackageProbe.csproj` 缺少新 `UpdateCore` 引用编译失败。已添加实际项目引用，专项复跑通过（`publisher-probe-green.log`），随后发布完整套件达到 120 passed。旧失败记录保存在 `publisher-grouped-all-release.log`。

## 端到端验收矩阵

| 场景 | 当前可证明 | 最终门槛 |
| --- | --- | --- |
| 四变体正式自包含程序与清单 | 四份小型 schema fixture 通过；本地生成工具与分组发布 fixture 通过 | 从同一提交构建实际 x86／x64 normal／test `.1`／`.2` 产物，记录字节与哈希。 |
| 本项目 Gitee `.1` 发布 | 尚未进行；缺少可用发布授权，已请求 `DFLEGACY_GITEE_RELEASE_TOKEN` 进入当前发布进程 | 最小公开说明提交、附件上传、匿名清单／分卷／文件复核与提供方总量预检。不得推送私有开发历史。 |
| `.1` → `.2` 内容复用 | 本地 HTTP 和实际内容哈希证明不重复上传共享资源 | 本项目真实仓库记录每个引用 tag、附件 ID、下载字节和未变化资源零重复上传证据。 |
| V3 → 桥接 → V4 | 旧 V3 工具保留，相关旧发布测试通过 | 用旧客户端副本完成真实桥接，核对 x86／x64 和 normal／test 不串线。 |
| 同版本损坏、缺失、同长错误 | 核心 SHA256 与事务 fixture 覆盖 | 实际客户端损坏一个受管理文件后，只修复必要资源并复核全部管理集。 |
| A 完整、B 下载取消／离线 | 核心引擎 fixture 覆盖状态分离与 A 校验 | 真实登录器取消、重启、断网，提示缓存版本而非“线上最新”，最终安装 A 保持完整。 |
| B 部分提交／损坏备份 | 真实子进程检查点与拒绝混合状态 fixture | 实际助手交接中断后恢复或明确失败，未决状态不能启动游戏。 |
| A 跳 C | 累计退役、迁移与最低基线 fixture | 本项目三清单／跨版本实机验收；只删除历史受管理文件，未知玩家文件保留。 |
| 配置迁移 | 端口字符串／数组语义、缺省展开、凭据与未知字段保留 fixture | 真实账号／设置并发编辑与安装回滚核对；配置坏文件保留。 |
| 下载恢复 | 本地 200／206／416、截断／错误哈希及完成缓存 fixture；真实 Gitee 200 回退 | 本项目真实大登录器 48 MiB 分卷，在断网与重开后核对实际字节与缓存复用。 |
| 独立修复与占用提示 | 实现整合中 | 损坏安装目录登录器后，从独立自包含入口启动修复；真实占用进程可辨认，未经同意不终止。 |
| 原生崩溃与断线 | 原生／Relay／详细抓取 fixture 通过 | 原始游戏受支持签名的实机证据；CrashSender 存活弹窗入口未验证时继续标记需详细抓取。 |

Gitee 的 48 MiB 分卷不能绕过仓库累计 1,000,000,000 字节限制；实际两轮测试产物预检超限时应停下报告，不自动漏选资源、删除旧发布或切换存储。任何不完整发布保留现场；没有同 tag 自动覆盖／恢复上传流程，审查后改用新的测试 tag。

最终负责人应在集成完成后补充：准确提交、实际四变体清单哈希、测试 Release URL／ID、各阶段测试命令与退出码、真实请求字节、未完成项和是否取得发布授权。本稿不替代这一步。
