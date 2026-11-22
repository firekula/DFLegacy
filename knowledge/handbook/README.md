# DFLegacy 维护手册

这套手册解释现有工程，供没有历史上下文的维护者接手。初次进入从 [START-HERE](../../START-HERE.md) 开始。基线日期为 2026-11-17；后续代码变更需要同步对应章节。

## 阅读目录

| 页面 | 内容 |
| --- | --- |
| [当前基线](current-state.md) | 章别、源码版本、历史结论和遗留问题 |
| [agent 工作步骤](agent-workflow.md) | 小上下文任务拆解、检索、修改、验证、恢复和交接模板 |
| [术语表](glossary.md) | 章节、进程、协议、资源、发布和测试常用词 |
| [目录与模块地图](repository-map.md) | 全部 `src` 模块、顶层目录、第三方边界 |
| [服务端架构](server-architecture.md) | 启动、会话、网络、玩法服务和修改入口 |
| [协议与数据](protocol-and-data.md) | 封包、章别、PVF读取、持久化、事务与协议证据 |
| [客户端与原生层](client-and-native.md) | ACT1、第二章、注入、补丁、渲染、调试接口 |
| [章别适配](chapter-adaptation.md) | 第一章/第二章在协议、资源、原生、构建、发布上的边界与跨章移植检查 |
| [区域频道与假人](social-world-and-bots.md) | 已实现的地区频道/经验/城镇社交，假人研究的停点与继续验证 |
| [桌面应用](desktop-apps.md) | 登录器、更新器、启动入口、诊断、账号、中继和打包 |
| [配置说明](configuration.md) | 文件来源、优先级、端口、章别与参数定位 |
| [Web 与管理 API](web-and-admin.md) | 管理器、GM、玩家中心、游戏内页面和 HTTP 权限 |
| [资源与 PVF](resources-and-pvf.md) | 游戏资源、语义迁移、图片、导出工具和校验 |
| [资源子项目协作](resource-subprojects.md) | PVFStudio、NPKStudio 的独立手册、跨仓库接口及解析/修改链路 |
| [pvf-dump](pvf-dump.md) | 本机地图勘察工具的实际参数、硬编码输入和换机限制 |
| [工具索引](tools-reference.md) | 发布、调试、验收、资源及运行脚本的用途和副作用 |
| [构建测试发布](build-test-release.md) | 依赖、命令、发行物和版本策略 |
| [测试与证据](testing-and-evidence.md) | 自动化、真机矩阵、测试控制与证据边界 |
| [第二章本地真机测试](chapter-two-local-qa.md) | 测试环境、地图通行前置、角色基线和玩法用例 |
| [运维手册](operations.md) | 安装、部署、日志、备份、迁移、升级和恢复 |

## 可检索参考表

- [files.tsv](reference/files.tsv)：受 Git 管理的源码、项目、配置、测试、脚本及资源路径，按目录归属分类；用于查漏和定位，不是功能完成度统计。
- [documents.tsv](reference/documents.tsv)：本轮开始时受 Git 管理的既有 Markdown 文档及首标题；用于找到历史原因。新手册导航在上表。
- [configuration-properties.tsv](reference/configuration-properties.tsv)：配置相关 C# 文件的自动属性声明和代码行号；用于继续阅读实际类型，不是可直接提交的 JSON schema。
- [http-routes.tsv](reference/http-routes.tsv)：静态字符串形式的 HTTP 路由注册位置；动态拼装路径、端口和权限仍须看实现。
- [参考表生成方法](reference/README.md)：范围、限制与重建步骤。

## 文档可信度

手册中的源码链接用于定位事实；历史报告中的“通过”只适用于它写明的构建、环境和场景。本文档工作没有重新证明所有玩法或线上部署状态。遇到冲突时先核对当前实现、当前配置、最终产物和任务要求，再更新文档，不通过删掉旧失败记录制造一致。

新长期文档放在 `knowledge/handbook/`；具体事件和决策放在 `knowledge/decisions/`。`docs/` 在 `.gitignore` 中，虽已有部分文件被跟踪，新文件不会自然进入 Git；新增后必须检查 `git status`。
