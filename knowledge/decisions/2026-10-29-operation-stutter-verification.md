# 操作时全画面停顿：隐式截屏复现与修复

用户补充：停顿在代理操作角色时反复触发，表现为整个画面一起停顿。本轮确认了一个具体来源：旧操作包装器默认附加原生 PrintScreen。对照实验中，6次原生截屏分别伴随约57～70ms的连续原生时钟停顿。现已改为普通操作默认不截图，关键验收显式加 `--screenshot`。

修复源码为 `e6dac3f`，见[操作控制器](../../tools/acceptance/native_control.py)。本轮修改代理的操作工具，debug.dll仍为上一轮的C3ED构建，原生输入仍为0F；未改原版EXE/PVF、DLL或原始存档。确认的截图停顿已处理，其他来源的偶发停顿没有据此全部关闭。

## 对照与结果

私有双客户端环境中，仅操作A端 mageco45，PID77704；B端作为后台客户端。截图对照及修改后测量均在西海岸2/0、同一前台进程进行。完整[身份记录](assets/2026-10-29-operation-stutter/running-deployment.json)和[工具来源](assets/2026-10-29-operation-stutter/controller-adoption.json)已保存。本轮性能范围是A端，未声称双端全场景性能验收。

观察器保留每一次只读查询的开始/结束时间、前台PID及原版两个时钟字段。只有相邻查询间隔≤10ms、单次查询≤5ms、两个字段连续保持相同时，才计入有证据支持的停顿下界；观察器自己未被调度的空档单独排除。这是原生时钟停顿观察，不是GPU Present耗时。

| 阶段 | 时长 | 最大合格连续停顿下界 | 超过50ms的合格段 |
|---|---:|---:|---:|
| 截图前等待 | 12s | 7.60ms | 0 |
| 相同连续按键计划，显式加入6次原生截屏 | 20s | 70.24ms | 6 |
| 截图后等待 | 12s | 7.16ms | 0 |
| 修复后：默认调用，32次按键的一条计划 | 20s | 7.42ms | 0 |
| 修复后：默认调用，16次单键短计划 | 20s | 7.56ms | 0 |

6次截图对应的停顿下界依次为59.30、56.69、57.11、64.80、66.30、70.24ms；6张原图的文件完成时间均落在对应停顿结束后约0.55～2.65ms。修改后40秒内48次按键均成功执行，未显式传入任何截图或“不截图”开关，截图目录1129项的大小和修改时间前后完全一致。另有一次62.22ms的观察器空档已剔除，未将它归因于游戏，也未对该空档里的游戏状态作保证。

原始数据与独立重算：[截屏对照](assets/2026-10-29-operation-stutter/perf/screenshot-polls-v4-C3ED.json)、[截屏分析](assets/2026-10-29-operation-stutter/perf/screenshot-polls-v4-C3ED-analysis.json)、[默认不截屏对照](assets/2026-10-29-operation-stutter/perf/default-no-shot-v5-C3ED.json)、[其分析](assets/2026-10-29-operation-stutter/perf/default-no-shot-v5-C3ED-analysis.json)。[分析脚本](assets/2026-10-29-operation-stutter/analyze-polls.py)可离线重算。

## 操作规则与关键图片

旧包装器的逻辑是“除非传入 `--no-shot`，否则截图”；已有部分数据驱动调用显式禁用了截图，但直接调用包装器仍会意外附加。本轮将其抽取为统一控制器，默认不追加PrintScreen，保留原有PID/路径/启动时间前后校验、唯一运行角色、运行记录不变校验及证据保护，并以独占标签预约拒绝重复操作。

后续本地验收使用 `tools/acceptance/native_control.py <运行目录> <角色> <标签> <步骤JSON>`。该运行目录须有本轮绑定的running部署记录和既有 `builtin_driver.py`。关键检查点才追加 `--screenshot`；旧 `--no-shot` 兼容，显式步骤中的VK44也仍属于调用方主动请求。历史冻结目录中的旧包装器保留原字节。

显式关键截图通过同一新控制器实测成功，原图与复制图逐字节相同，SHA256为 `2861FFB66BA14585DFE2C310EEB3B575E711442DD6BCE1EF39EB8C485F43DDBC`。下图是原版客户端自身生成的JPG，未编辑；它证明显式验收截图仍保留，不能凭单张图片判断停顿时长。

![显式请求的原生关键验收截图](assets/2026-10-29-operation-stutter/evidence-operation-baseline/explicit-key-acceptance-shot.jpg)

## 测试、限制与收尾

默认行为回归先复用旧语义得到3失败/5通过，改为显式截图后，相关Python测试为176通过、173子测试通过。全仓为471通过、221子测试通过，仍有6项已知基线失败：三项协议台账数量断言和三项固定端口被Windows10013拒绝的编排测试。见[RED](assets/2026-10-29-operation-stutter/verification/native-control-red.log)、[GREEN](assets/2026-10-29-operation-stutter/verification/native-control-green.log)与[全仓日志](assets/2026-10-29-operation-stutter/verification/python-full.log)。本轮没有C++改动，因此未重新构建Native。

采样迭代中的限制已明确[更正](assets/2026-10-29-operation-stutter/measurement-corrections.md)：最初采样达到既有只读调用上限中止；早期只统计计数递增1的区间会遗漏计数重置；v4原始scope文字沿用旧版而过时，其显式截图阶段和输入记录才是依据。Windows拒绝系统ETW性能采集，未改系统策略、未声称取得调用栈归因。原始记录均未为迎合结论而改写。

已停止本轮自有进程，恢复2份配置及8个输入接口文件，15项保护文件检查通过；正常运行目录50文件及debug/原生DLL哈希保持原版本，上一轮冻结目录539文件逐项复核通过。原始存档没有被私有测试存档覆盖。见[恢复记录](assets/2026-10-29-operation-stutter/closeout-restoration.json)、[最终检查](assets/2026-10-29-operation-stutter/final-verification.json)及[离线独立审查](assets/2026-10-29-operation-stutter/review/operation-capture-review.md)。全部发布字节见[清单](assets/2026-10-29-operation-stutter/manifest.json)。
