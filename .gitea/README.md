# Gitea 1.26 Runner 与发行接入

工作流是可审查配置；没有注册 Runner 或 Registry 凭据时不会自动具备这些能力。不要把 Runner 注册 token、PAT、Docker 凭据或私有 PVF 写入 Git。

| Runner 注册标签 | 执行环境 | 预装工具 |
| --- | --- | --- |
| `dflegacy-windows-x64:host` | 专用或每次重建的 Windows x64 主机 | Git、Node 20、.NET SDK 10.0.402、Python 3.12（`py -3.12`）、PowerShell 7、VS C++ 与 CMake 3.24+ |
| `dflegacy-linux-x64:host` | 专用或每次重建的 Linux x64 主机 | Git、Node 20、同版 .NET SDK、Python 3.12+（`python3`，含 venv/pip）、curl、Docker/buildx、预配置多架构 builder/QEMU、Skopeo |

`runs-on` 使用标签冒号前的名称。注册方式见 [Gitea 1.26 act runner](https://docs.gitea.com/1.26/usage/actions/act-runner/)。宿主模式执行仓库代码，须使用受限专用主机，fork PR 需平台审批和隔离环境，不应将不可信 PR 交给有持久凭据的发布机器。工作流不提供 `pull_request_target` 或给 PR 传 Registry secrets；Git 检出禁用持久凭据。

仓库启用 Actions 后配置变量：

- `DFLEGACY_REGISTRY_HOST`：Registry 主机（含必要端口），不含 scheme。
- `DFLEGACY_REGISTRY_IMAGE`：`registry/owner/dflegacy`，不带标签，owner 使用小写。
- `DFLEGACY_REGISTRY_USER`：Registry 发布用户。
- `DFLEGACY_ALLOW_HTTP_API`：默认空；仅受信 HTTP Gitea 实例设为 `true`。该值不改变 Registry TLS 校验。
- `DFLEGACY_ALLOW_HTTP_REGISTRY`：默认空；仅对明确使用 HTTP 的 Registry 设为 `true`。该值只给本次 Skopeo 登录、检查和复制添加对应 TLS 选项，不修改 Docker 全局设置。

本机 Windows + Docker Linux Runner 的部署、启动/停止与持久卷约定见[部署说明](../deploy/gitea-runners/README.md)。

包读取权限跟随所有者的可见性，不继承所关联仓库的私有权限；公开个人账号下的镜像包不能因此视为私有。私有发行可使用专用私有组织。本次实例使用仅含 `firekula` 成员的私有组织 `dflegacy-ci`，镜像地址为 `firekula.site:3000/dflegacy-ci/dflegacy`。见 [Gitea 1.26 包访问限制](https://docs.gitea.com/1.26/usage/packages/overview/#access-restrictions)。

仅发布任务使用 secret `DFLEGACY_REGISTRY_TOKEN`，限制为必要包写权限。Release 使用内建 `GITEA_TOKEN`；Gitea 1.26 job 指定 `code: read`、`releases: write` 与 `packages: write`，且仍受仓库/组织最大权限限制。测试任务只读。权限与 fork 限制见 [官方 job token 文档](https://docs.gitea.com/1.26/usage/actions/token-permissions/)。Registry 是否接受该实例的 token、HTTPS 与 push 需接入时实际验证；工作流采用独立最小权限 Registry token，不复用个人全权限凭据。

`release.yaml` 先在 Windows/Linux 实际执行默认 `dotnet run` 冒烟、Manager HTTP/子进程测试和 Python 行为测试。Windows 再构建/测试原生项目、登录器和 win-x64 包；同 SHA 的不可变 Windows 资源交给 Linux 交叉生成其余四 RID。Linux x64 解压包与容器必须实际通过健康/停服 readiness、non-root 与命名卷持久化检查。只有全部完成，发布任务才上传 OCI 镜像，并将五个包全部上传到 Release 草稿后公开。任何 test/build 失败均使发布任务不能运行。

外部 actions 固定完整 SHA：

| Action | 官方版本 | 固定提交 |
| --- | --- | --- |
| checkout | [v4.2.2](https://github.com/actions/checkout/releases/tag/v4.2.2) | `11bd71901bbe5b1630ceea73d27597364c9af683` |
| upload-artifact | [v3.2.2-node20](https://github.com/actions/upload-artifact/releases/tag/v3.2.2-node20) | `c6a3b2bd78b3985e4b2f15397fec357f0fd808de` |
| download-artifact | [v3.1.0-node20](https://github.com/actions/download-artifact/releases/tag/v3.1.0-node20) | `ad191675b41f6a5b46da9a048cb6893812da158b` |

选择 artifact v3 的旧 artifact API，而非 GitHub v4+ 服务；Node 20 backport 避免 Node 16 运行时。官方 Gitea 的 [artifact v3 行为记录](https://github.com/go-gitea/gitea/issues/27314) 与 [GitHub artifact 兼容说明](https://github.com/actions/upload-artifact#v4---whats-new) 可供核对。Git refs 与官方 action.yml 已在实施时读取校验；实际 act_runner 版本、Windows artifact 上传/下载往返仍需注册后做一次同 SHA 测试，不把文档兼容性判断称作真实 Runner 验收。跨 job 传输归档而非散文件，避免 artifact 服务丢失执行位。

OCI 镜像先构建为 artifact，发布时 Skopeo `--all --preserve-digests` 上传已经测试的内容，不重新构建。Runner 的 builder、Registry HTTP/TLS 配置由部署者明确管理；工作流不会更改 Docker 全局设置或自动注册 Runner。默认管理页与游戏 PVF 实机设置仍由部署者完成。

Registry 写入前，`release --reserve` 先通过 Gitea 的 Release 标签唯一约束原子保留完整 inventory（包括镜像 digest）身份。并发 run 只有相同提交、附件摘要和 image digest 才能复用同一草稿；不同身份的竞争者在 Registry 步骤前失败。版本镜像标签已存在时必须具有相同 digest，不能覆盖其他内容；正式版 `latest` 从本次不可变 digest 复制。

草稿续传只下载校验已有附件并补缺，不删除、不替换附件。Gitea 1.26 不提供附件 SHA 字段，工具从同源无重定向 URL 流式计算摘要；异摘要或未知附件直接失败。相同身份且附件完整的公开 Release 返回 `already-published`，不修改附件或发布状态，因此 `latest` 瞬时失败后可以重跑发布步骤补齐。失败恢复优先复用同一次已通过门禁的 artifact；也可下载这些原件在本地调用 CLI。不要用不同内容替换草稿以绕过摘要检查。

Windows/Linux 共用 Git 提交时间 `SOURCE_DATE_EPOCH`，发行 stage 规范化 ASP.NET 生成的静态资源 Last-Modified，归档时间固定；原生构建启用确定性选项。Buildx 关闭 provenance/SBOM 时间附件并启用 `rewrite-timestamp=true`，发布的 OCI metadata 仅保留 digest、源版本/提交/时间与平台，去掉每次构建的随机 ref。基础镜像或 apt/NuGet 远端依赖发生内容变化仍可能改变镜像，不能把这类变化宣称为相同构建输入；完整 inventory 预留会拒绝它覆盖已有版本，应复用原 artifact 或明确发布新版本。

`main` 版本是 `dev-<提交UTC时间>-<12位SHA>`，相同提交得到相同身份；正式标签仅接受 `vX.Y.Z`。跨平台包必须具有相同版本、完整提交 SHA 与摘要。已有公开 Release 拒绝重传，草稿只允许同提交/同附件摘要重试。Registry 与 Release 无分布式事务；测试或构建失败不会开始发布，网络发布失败保留未公开草稿供检查，不能将中断运行当作完整发布。

`prune.yaml` 为独立人工触发维护任务，默认 dry-run。先审查输出，再将 `apply` 精确设为 `true` 才删除由本流程标记且符合固定开发标签前缀的预发布和对应标签，保留最新 20 个。正式标签、人建 Release 与未完成草稿不在清理范围。也可本地执行 `release.py prune`；`--apply` 才会删除。

首次接入需验证：两类 Runner 标签可调度、同 SHA artifact 往返、原生与 .NET 测试实际退出码、五个最终归档校验、x64 容器生命周期和卷持久化、Registry 多架构 digest、草稿失败重试，以及正式发布前真实客户端连接。缺少环境的项目必须列出“未执行”。

测试 job 在独立 venv 通过 `tools/release/requirements-ci.txt` 安装锁定版本和 SHA-256 的 pytest、PyYAML、Pillow 及全部传递依赖，实际运行 `tools/acceptance/tests` 和 `tools/debug/test_drop_control.py`（`PYTHONPATH=tools`）。这些测试使用临时文件/进程/socket，无需客户端；不得为通过 CI 将其无理由跳过。

离线原生字节回放所需的 pefile、Capstone、Unicorn 也使用固定版本和 wheel 摘要安装。Windows 原生 CTest 同时运行调试模型和 ABI 检查；城镇控制器使用有界源脚本子集执行生产解析与导航检查。

后续专用夹具补齐后，原先 11 项的默认输入由仓库中的真实原生导出与有界原始指令提供；相邻探针及城镇回放也有可校验的有界源数据。Windows 的完整门禁在构建后重新生成当前原生导出，设置本次 DLL 路径，执行全部 Python 测试并要求 JUnit 零跳过。原生城镇控制器默认 CI 输入为脚本子集 PVF；完整原始 EXE/PVF 仅用于明确选择的对照或再生，不上传到仓库、artifact 或镜像。

metadata 只导出 `DFLEGACY_RELEASE_VERSION`、`DFLEGACY_RELEASE_COMMIT` 与 `SOURCE_DATE_EPOCH`，Docker build args 也采用前两个限定名称；job outputs 仍为 `version`/`commit`。通用 `VERSION=v...` 会被 MSBuild 当成非法 NuGet 版本，因此工作流与构建子进程会清理遗留变量，不能改回旧名称。

手工 `workflow_dispatch` 默认只运行完整测试/构建并保留 artifact。仅在 main 或正式标签上把 `publish` 精确设为 `true`，才在全部门禁通过后进入相同发布 job；不提供跳过测试的手动发布路径。完整 Release 除五个归档和 inventory 外，单独附 bridge Compose、host IPv6 Compose 和 `.env.example` 并记录摘要。镜像 `latest` 仅在正式标签完整发布成功后更新，开发版始终使用自己的版本标签。

prune 任务需要额外 `code: write` 删除其选中的开发 Git 标签；此权限仅用于 main 上人工触发的清理任务。普通测试不具有写权限。
