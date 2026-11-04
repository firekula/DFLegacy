> 以下为历史 Runner 运维记录。当前 Gitea 工作流仅使用 Linux Runner 执行源码测试。

# Windows + Docker Linux Gitea Runner

本目录是 Linux Runner 的可复用部署配置。实际凭据、`.runner` 和运行日志保存在仓库外。本次安装位置为 `D:\DFLegacyCI`，安装结果及完整流水线链接见[接入验收记录](../../docs/superpowers/reports/2026-10-30-gitea-runner-deployment-validation.md)。

## Windows

本次使用 Gitea Runner 5.2.4，仓库级名称 `DFLegacy-Windows-Firekula`、标签 `dflegacy-windows-x64:host`。独立 Node 20、PowerShell 7、CMake 保存在 `D:\DFLegacyCI\tools`；VS C++、Python 3.12、Git 和 .NET SDK 使用本机安装。启动脚本会设置确定的工具路径。

`DFLegacy-Gitea-Runner-Windows` 计划任务使用当前用户、普通权限，登录时启动；当前用户必须保持登录。计算机休眠、关机或用户注销时不能执行任务。停止前先确认仓库 Actions 中没有本机正在执行的 job。

```powershell
Get-ScheduledTask -TaskName DFLegacy-Gitea-Runner-Windows
Start-ScheduledTask -TaskName DFLegacy-Gitea-Runner-Windows
# 暂停自动重启后，等待当前 job 完成，再停止计划任务。
New-Item -ItemType File D:\DFLegacyCI\windows\disabled -Force
Stop-ScheduledTask -TaskName DFLegacy-Gitea-Runner-Windows
# 恢复：删除标记，再启动计划任务。
Remove-Item -LiteralPath D:\DFLegacyCI\windows\disabled
Start-ScheduledTask -TaskName DFLegacy-Gitea-Runner-Windows
```

状态、配置和日志位于 `D:\DFLegacyCI\windows`，该目录仅当前用户、SYSTEM 和本机管理员可访问。备份/迁移须同样保护 `.runner`；不要将其上传到 Git 或 artifact。停用还可禁用计划任务；彻底撤销需在 Gitea 仓库的 Settings → Actions → Runners 删除相应注册。

## Docker 中的 Linux

将本目录的配置复制到仓库外的持久目录，例如 `D:\DFLegacyCI\linux`。复制 `.env.example` 为 `.env`，将两个 Docker 官方镜像固定到已核验的 RepoDigest。本次 image tag 与 digest 记入验收记录。构建时校验 Gitea Runner、Node 的官方 SHA-256；SDK 固定版本和镜像 digest。

在该目录创建受保护的 `state/config.yaml`：

```yaml
log:
  level: info
  job:
    dir: /runner/logs/jobs
    retention: 168h
    max_size: 128MB
runner:
  file: /runner/.runner
  capacity: 1
  timeout: 3h
  shutdown_timeout: 30s
  labels:
    - dflegacy-linux-x64:host
cache:
  enabled: false
host:
  workdir_parent: /work/jobs
metrics:
  enabled: true
  addr: 127.0.0.1:19101
```

```powershell
Set-Location D:\DFLegacyCI\linux
docker compose build
# 先通过受保护输入设置 GITEA_RUNNER_REGISTRATION_TOKEN，值来自仓库级注册页。
docker compose run --rm --no-deps -e GITEA_RUNNER_REGISTRATION_TOKEN runner --config /runner/config.yaml register --no-interactive --instance http://firekula.site:3000 --name DFLegacy-Linux-Docker --labels dflegacy-linux-x64:host
Remove-Item Env:GITEA_RUNNER_REGISTRATION_TOKEN
docker compose up -d docker
docker compose run --rm --no-deps --entrypoint docker runner buildx create --name dflegacy-ci --driver docker-container --use
docker compose run --rm --no-deps --entrypoint docker runner buildx inspect dflegacy-ci --bootstrap
docker compose up -d runner
docker compose ps
```

确认 builder 支持 `linux/amd64` 和 `linux/arm64`。Docker Desktop 提供的模拟能力须实际出现在 builder 列表中；移到独立 Linux 后如缺少模拟能力，先按 Docker 官方文档配置 binfmt/QEMU，再开放该标签。

Runner 以容器内的 host 模式执行 job。专用 DinD 守护进程只共享 Unix socket，不对宿主发布 Docker TCP 端口，也不使用 Docker Desktop 的宿主 socket。DinD 为特权容器；仅给受信任私有仓库使用，不要向其开放不可信 PR 执行。Runner 和 DinD 同时挂载 Linux 原生 `work:/work`，且 `TMPDIR=/work/tmp`，因此测试创建的 PVF 路径可由 sibling 容器原样只读挂载。禁止只挂宿主 socket 而丢失这些路径。

Runner 使用 `network_mode: service:docker` 共享 DinD 的网络命名空间，使现有测试可以从 Runner 访问发布在 DinD `127.0.0.1` 上的临时端口；这些端口不发布到 Windows 宿主。容器烟测仅只读挂载可读的合成 `Script.pvf` 文件，保持宿主临时目录的私有权限，兼容应用的普通用户 UID。

Docker Desktop 必须运行；Compose 使用 `restart: unless-stopped`。本次安装另有当前用户计划任务 `DFLegacy-Gitea-Runner-Linux`，登录 20 秒后执行 `D:\DFLegacyCI\linux\start-runner.ps1`，启动 Docker Desktop 并执行 Compose `up -d`。脚本检测 `D:\DFLegacyCI\linux\disabled` 标记后会跳过启动；长期停用也可禁用该计划任务。

`docker compose stop runner` 停止拉取任务；空闲时 `docker compose restart runner` 可验证恢复。`docker compose down` 保留数据卷；不要使用 `down -v`，否则会删除构建缓存和工作卷。更新时先等待任务结束，备份受保护的 state、记录旧 image digest，再重建并启动。

## 发布配置

当前工作流的接入说明见 [Gitea 源码与测试](../../.gitea/README.md)。
