# Gitea 源码与测试

Gitea 工作流仅运行只读测试，使用 `dflegacy-linux-x64` 自托管 Runner。Runner 需要 Git、Node 20、.NET SDK 10.0.402、Python 3.12+ 和 venv/pip。分支推送、PR 和手工触发均使用相同测试流程，检出不保留凭据。

Docker 镜像与两种客户端架构的更新发布见 [构建指南](../BUILDING.md#源码与产物发布)。Runner 的历史安装记录见 [Runner 运维](../deploy/gitea-runners/README.md)。
