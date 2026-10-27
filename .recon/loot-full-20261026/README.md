# 拾取与分配取证索引

这里保留依据本地样本编写的分析报告和可重跑的客户端离线夹具源码：

- [官服普通分配](ordinary/ordinary-flow.md)：来源与选择门控、活人资格、库存预筛、计分和轮转。
- [官服特殊确认](special/special-flow.md)：后期全队选择状态机及当前样本入口边界。
- [ACT1 接口](client/client-contract.md)：原生确认窗、请求和通知布局、地面与等待状态恢复。

原始二进制、IDA 数据库、反编译输出、生成的汇编/trace 文本和本机日志保留在本地，不随源码发布。报告中的这些路径用于定位原始证据，不表示仓库包含样本。

## 客户端离线夹具

`client/contract_checks.py` 通过 `offline_window_emu.py` 和 `raw_probe.py` 读取用户自行提供的 ACT1 1.0.1.9 EXE。依赖 Python 的 `pefile`、`capstone`、`unicorn`；`raw_probe.py` 的 `EXE` 当前使用原调查机器路径，运行前按本机位置调整。匹配样本 SHA256 为 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`。

```powershell
python .recon/loot-full-20261026/client/contract_checks.py
```

运行真实保护分支于隔离的合成 CPU 上下文；外部调用全部拦截，不启动游戏、不联网、不调用真实游戏/API 函数。14 个夹具覆盖确认窗按钮、guard 随机边界和 descriptor 稀有度 0..5 的分支。生成的诊断文本留在本目录。

离线夹具不替代真机。最终实现与实机范围以[接口对齐结论](../../knowledge/decisions/2026-10-27-official-loot-contract.md)及[双客户端验收](../../knowledge/decisions/2026-10-27-loot-live-verification.md)为准。
