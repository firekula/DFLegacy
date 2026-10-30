# 跨平台部署与发行：本地交付记录

日期：2026-10-30。功能代码提交：`c5b098859c541a979ef50dcf3d030442c064d827`。后续文档提交不改变以下构建身份。

发行版本：`dev-20261030T090644Z-c5b098859c54`，`SOURCE_DATE_EPOCH=1793351204`。

## 已实现

- 三平台共用中文 Web 部署管理器：首次独立管理账号、配置和备份、游戏管理员初始化、服务启停/重启、有效配置、日志、补丁及 Windows 自包含登录器 ZIP 导出。
- Windows `Start.cmd`、Linux `start.sh`、macOS `Start.command`；安装目录与数据目录可分开。新发行默认关闭测试账号播种，导出包账号/密码为空。
- 保留原有 GM、玩家角色与 460×315 游戏内网页。补丁的游戏内 HTTP(S) 基址可配置；登录器安装时保留玩家其他 INI 设置。
- 五 RID 自包含包、同提交 Windows 客户端资源、SHA-256 清单；Docker 双架构构建、非 root 运行、只读 PVF、持久数据卷和 IPv6 host-network 示例。
- Gitea 测试/构建/发行与开发预发布清理工作流。发布先保留包含附件及镜像摘要的身份，草稿仅补缺、拒绝内容替换；测试失败不能发布。main 为开发预发布，`vX.Y.Z` 为正式版，人工触发默认只构建。

## 实际验证

| 检查 | 结果与范围 |
| --- | --- |
| 现有默认服务端冒烟 | Windows x64、Linux x64 官方 SDK 容器均通过；另有真实服务端初始化、私有就绪探测与优雅停止检查 |
| Manager 控制台行为检查 | Windows 与 Linux 各 73 项，0 失败；包含文件、真实子进程、HTTP 身份/CSRF/来源边界、配置、ZIP 与存档保护 |
| 现有 Python 工具行为测试 | 锁定依赖的独立 venv：183 项通过 |
| 发行/CI 工具行为测试 | 40 项通过；包含本地模拟 Gitea 的草稿并发、摘要冲突、续传、公开版本只读重试，以及真实 .NET 环境变量回归 |
| 原生代码 | IJL15/Debug CTest 3/3，RandomNative 1/1；登录器行为测试及自包含发布通过 |
| 原生重复构建 | 同源、同工具链与配置下，CMake/vcxproj 两条构建路径的三 DLL 共六组 SHA-256 均一致；不代表跨工具链或所有依赖可复现 |
| Windows 运行时 | 解压发行包健康/readiness 冒烟通过；登录器在不存在的 DOTNET_ROOT 下启动检查通过，host trace 加载自带 coreclr。未使用独立干净 Windows VM |
| 五平台最终归档 | 全部构建、解压并通过清单/摘要、对应 RID 运行时依赖、Unix 执行位及凭据检查；Windows 包另实际执行健康/readiness 冒烟 |
| 最终 Docker 镜像 | 同一 OCI 索引包含 amd64/arm64，两平台版本、提交标签与非 root 配置校验通过；加载该索引后，amd64 的 12 项实际容器生命周期、导出和持久化检查全部通过 |
| 网页 | 桌面浏览器实际查看首次设置、登录和配置；JavaScript 语法检查通过。未完成所有移动尺寸的人工视觉检查 |
| 代码审查 | 两轮独立实现/集成审查的 Critical/Important 项已修复并验证；有效入口端口显示的 Minor 项也已修复 |

最终 OCI 索引摘要为 `sha256:cdf2c59bfb857016d5f99c2a0ffbe117838f694981b614ac9ae919c4375ae202`。本机 `artifacts/final/<版本>/validation.json`、`image-metadata.json` 和 `SHA256SUMS` 记录具体产物及文件摘要；没有推送到 Registry。

## 产物

本机工作树的 `artifacts/final/dev-20261030T090644Z-c5b098859c54/` 保存五个平台归档、双架构 OCI tar、Compose/.env 示例、`SHA256SUMS`、资源清单和 `validation.json`。临时测试数据、构建产物和日志均被 Git 忽略，未放入源代码提交。发行包不含客户端、私有 PVF、现有存档或管理凭据。

启动和数据迁移见 [DEPLOYMENT_MANAGER.md](../../../DEPLOYMENT_MANAGER.md)，Runner 标签、工具版本、变量/secret 与首次接入检查见 [.gitea/README.md](../../../.gitea/README.md)。

## 尚需外部环境的验收

- 尚未注册 Windows/Linux Runner、启用远端工作流或向 Gitea/Registry 上传发行物。需由实际机器与仓库权限完成 Runner、artifact 往返、Registry 权限/多架构 digest 和远端失败重试验收。
- macOS x64/ARM64 与 Linux ARM64 包的交叉构建、摘要和执行位检查不等于目标硬件原生运行通过。macOS 首版未签名/公证。
- 真实 Windows 游戏客户端连接 Windows/Linux/macOS/Docker 服务端的注册、登录、进图与长期运行未执行。测试使用合成最小 PVF，没有读取用户的私有游戏资源和已有玩家存档。
- 本次没有自动升级现有服务实例，也没有推送分支、合并或发布远端版本。原工作区的研究文件和未提交改动保持原样。
