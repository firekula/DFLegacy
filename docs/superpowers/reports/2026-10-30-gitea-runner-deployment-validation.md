# Gitea Runner 接入与真实发布验收

日期：2026-10-30。

状态：部署完成；main 自动触发的流水线 #6 四个 job 全部成功，已完成远端 Release 附件与私有双架构镜像的独立回读校验。

验收源码：`83f040dec5c57f419e53f69ec544842b09bc2427`。由 main 推送自动触发的[流水线 #6](http://firekula.site:3000/firekula/DFLegacy/actions/runs/6)。旧运行 #3、#4 已作为被替代的接入运行取消，未发布其版本。运行 #5 的测试、五平台打包和容器 12 项检查全部通过，发布阶段因 Skopeo 不接受空认证 JSON 文件而中断；草稿 3699 保持未公开、零附件。初始化有效的 `auths` JSON 后，真实 Skopeo 登录验证通过，修复已提交为 `83f040dec5c57f419e53f69ec544842b09bc2427` 并自动触发运行 #6。旧草稿和其绑定身份保留，不替换已有产物以绕过检查。

## 实际安装

| 项目 | Windows | Linux |
| --- | --- | --- |
| Gitea 仓库级 Runner | ID 1，`DFLegacy-Windows-Firekula` | ID 2，`DFLegacy-Linux-Docker` |
| 标签 | `dflegacy-windows-x64:host` | `dflegacy-linux-x64:host` |
| 执行环境 | Windows 11 x64，当前用户普通权限 | Ubuntu 24.04 x64 Runner 容器，专用 DinD |
| SDK / Node / Python | .NET 10.0.402 / 20.20.2 / 3.12 | .NET 10.0.402 / 20.20.2 / 3.12.3 |
| 其他工具 | VS C++ 18、CMake 4.4.4、PowerShell 7.6.5 | Docker 29.9.2、buildx 0.38.1、Skopeo 1.13.3 |
| 持久目录 | `D:\DFLegacyCI\windows` | `D:\DFLegacyCI\linux` 与 `dflegacy-ci_*` Docker 卷 |
| 登录启动任务 | `DFLegacy-Gitea-Runner-Windows` | `DFLegacy-Gitea-Runner-Linux`，延迟 20 秒启动 Docker Desktop/Compose |

两者都使用 Gitea Runner 5.2.4。Windows/Linux 官方二进制 SHA-256 分别为 `ec4df88c593289e24e7f93dea244f5867ebe9fabe8e542edc8b5e78c7d0b4166`、`674145e627e5c879d72c293a08991458cf73c81124e54afdd40cf6995098a01b`，下载后已校验。Node 也按官方 checksum 校验，Linux Dockerfile 固定下载摘要。

Linux SDK 镜像固定为 `mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2`；Docker CLI/DinD 的固定 digest 见[配置样例](../../../deploy/gitea-runners/.env.example)。没有对其他 Docker 容器、已有游戏服务、Docker Desktop 全局 Registry 设置进行变更。

## 预检与独立复核

- 两类 Runner 在 Gitea 显示在线并实际领取了相应标签的 job。
- Linux 容器内工具版本均已执行检查；持久 builder 的平台列表包含 `linux/amd64`、`linux/arm64`。
- 实际 sibling 容器读取到同一 `/work/tmp` 路径上的测试文件。Runner 和 DinD 只共享专用 Unix socket、构建卷及网络命名空间，没有发布 Docker TCP 端口到 Windows。
- Windows Runner 在无活动 job 时重启后保留 ID 1；Linux Runner 重建后保留 ID 2、builder 选择及构建卷，健康检查通过。
- 发布工具 40 项测试通过；工作流 YAML、9 个 Bash 脚本块语法通过。对 Registry 两个发布脚本分别执行空值、false、true 的 HTTP Registry 选项，共捕获校验 15 次 Skopeo 调用；API 的 HTTP 开关不改变 Registry 默认 TLS 验证。
- 独立复核发现并修复两项重要问题：DinD 回环端口未在 Runner 网络中可达、root 创建的私有临时目录对应用 UID 不可遍历。修复前真实探针分别复现连接拒绝、UID 1654 权限拒绝；修复后 HTTP 探针和从实际烟测源码提取挂载参数的 UID 探针均通过。PVF 探针使用 umask 077，临时父目录仍为 0700，仅只读挂载可读的合成文件。
- 没有 Critical 或待处理 Minor 发现；修复后的完整流水线作为集成验收，不以探针代替产品测试。

## 凭据与传输

注册状态与工具安装保存在仓库外；`D:\DFLegacyCI` ACL 仅允许当前用户、SYSTEM 和本机管理员。注册 token、PAT、Cookie 和完整游戏资源没有写入 Git 或 CI artifact。

Registry secret `DFLEGACY_REGISTRY_TOKEN` 使用独立的 `write:package` token；Release 使用 job 内建 token。Gitea 包权限跟随所有者而不继承仓库权限，因此已创建私有发布组织 `dflegacy-ci`，成员仅 `firekula`，镜像使用该私有命名空间；个人账号的公开可见性不变。参见[官方包权限说明](https://docs.gitea.com/1.26/usage/packages/overview/#access-restrictions)。当前实例为 HTTP，仓库分别配置 `DFLEGACY_ALLOW_HTTP_API=true`、`DFLEGACY_ALLOW_HTTP_REGISTRY=true`，Registry 为 `firekula.site:3000/dflegacy-ci/dflegacy`。Skopeo 的 HTTP 选项不会使其他 Docker daemon 自动接受 HTTP Registry；迁移 HTTPS 或在实际拉取主机明确配置该 Registry 后再直接使用 Docker 拉取。

## 真实流水线与发布产物

实际 [Release：dev-20261030T135024Z-83f040dec5c5](http://firekula.site:3000/firekula/DFLegacy/releases/tag/dev-20261030T135024Z-83f040dec5c5)，ID `3700`，`draft=false`、`prerelease=true`，完整包含 9 个附件。

| job | ID | Runner | 结论 |
| --- | --- | --- | --- |
| `test-linux` | 15 | 2 | success |
| `windows` | 16 | 1 | success |
| `linux-packages-and-image` | 17 | 2 | success |
| `publish` | 18 | 2 | success |

实际测试结果：Windows/Linux 默认服务端冒烟均通过；Manager 各 85/85、发布工具各 40/40；Windows 原生 CTest 13/13、随机数原生测试 1/1、登录器配置测试通过；Windows 完整 Python 697 项测试加 592 项子测试，JUnit 1289/1289、零失败/错误/跳过；实际 Docker 生命周期 12/12。

Windows 与 Linux artifact 已完成同 SHA 上传、下载往返。所有归档使用同一版本与完整提交 SHA；发布后的 9 个附件均重新从 Gitea 下载并计算 SHA-256，五个包逐一重新执行发行校验，四个 Unix 包额外核对启动脚本及三个应用的可执行位。

| 已发布附件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `.env.example` | 539 | `81de01b0e7f4df9fdd3918b0252ad3c29e3d99c7d2939a128c13212a51cd74fd` |
| `DFLegacy-dev-20261030T135024Z-83f040dec5c5-linux-arm64.tar.gz` | 200885941 | `f2ce5499e2baf483220ab8b45c0bbfd9844c1054497c2c62ff02450d4cd02a30` |
| `DFLegacy-dev-20261030T135024Z-83f040dec5c5-linux-x64.tar.gz` | 208596897 | `5b2d570342d02bd2d8fcdcd594c45e04026b1432d00e565a0409e6c8500a6a56` |
| `DFLegacy-dev-20261030T135024Z-83f040dec5c5-osx-arm64.tar.gz` | 197794136 | `c0859308f40b99563ed7626edcf0e58cbb37a33210267e1cf97408ec74671860` |
| `DFLegacy-dev-20261030T135024Z-83f040dec5c5-osx-x64.tar.gz` | 205737040 | `6a38551b79d364c32b4196a324aad52b8cb89b8418ba3bee908797a119eff9b9` |
| `DFLegacy-dev-20261030T135024Z-83f040dec5c5-win-x64.zip` | 212656782 | `6eb6511635519550749b1e230ca43d49ab8aab493ba32ab14f443496aef307c6` |
| `compose.host-ipv6.yaml` | 1146 | `d52cad77ebc6c331212ff0d720cf6a17b811db586b478d1874a487f34d8af2e1` |
| `compose.yaml` | 1398 | `ac3c7d3f61fc96653dbd1de39eab3ad3ea0f81e4e84c0895940fc2b368e36015` |
| `release-inventory.json` | 1500 | `e827aae24dc340ea70f1ea8a75fdca1777d05f2398f3160774c95d08a2e775dd` |

私有镜像的不可变引用：

`firekula.site:3000/dflegacy-ci/dflegacy@sha256:b49403f4c8f810d48102786785cb9f0f4581646a39cf5b1be431cc82359029d5`

从实际 Registry 回读索引原始字节的 SHA-256，与 CI 导出前保留的 OCI metadata、Release inventory 完全一致。索引仅包含 `linux/amd64` 和 `linux/arm64`；两个子 manifest 与 config blob 均逐一回读核对摘要，并确认 version、revision 标签和 `USER app`。匿名客户端请求已存在的镜像被拒绝。

本次实际跑通的是 main 开发预发布。`vX.Y.Z` 正式发布及其 `latest` 更新入口已配置，本次没有创建正式版本标签；macOS/ARM 只交叉构建与校验，不声明其原生应用实跑成功。真实客户端联机未包含在本次 CI 环境验收中。

网络记录：本机访问部分 Ubuntu/NuGet 源出现超时，包管理器自动重试后完成；基础镜像、NuGet 包和 buildx 状态保存在专用缓存。没有通过跳过测试、替换产物或修改不可变版本身份完成发布。


## 使用边界与维护

主机需开机、当前用户保持登录，Linux Runner 还需 Docker Desktop 运行。已部署的是本机 CI 环境；关机、注销、休眠期间不会执行构建。启动/暂停、私有状态备份、更新和撤销注册见[维护说明](../../../deploy/gitea-runners/README.md)。

复核范围的三项裁定：macOS/ARM 与真实客户端联机验收仍需对应机器/人工验证；持久 host Runner 只用于受信私有仓库，处理不可信 PR 须另设隔离执行环境；现有游戏功能继续由既有 CI 测试覆盖，本次不扩大产品修改范围。相应代价是这些范围之外的行为不能作为本次接入已经验证的能力。

本机 main 原有 3 个修改文件逐一 SHA-256 校验一致，全部 245 条工作区状态记录与接入前一致；未改动、提交这些研究文件及未跟踪内容。
