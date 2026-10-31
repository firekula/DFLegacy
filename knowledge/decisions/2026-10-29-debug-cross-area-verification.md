# Debug 跨区域移动验收

2026-10-29：A、B 两端各完成去程与回程，四轮均由一条最终目的地指令完成。接口自动计算转弯、出生门退出及跨区入口；每轮内部调用13次原生目的地操作，没有方向键或手工拆分路段。方向键接管和真实过期拒绝后的恢复也已验证。

## 实机结果

去程为赛丽亚房间`1/1 → 1/0 → 2/1 → 2/0 → 2/2`，回程相反；覆盖艾尔文防线与赫顿玛尔的五个区域。A为mageco（UID1、45级），B为召唤师（UID2、52级）。使用现有角色与独立验收存档，没有写入坐标、等级、装备或金币夹具。

| 客户端 | 路线 | 对外调用／过门次数 | 时间 | 最终坐标 | 原始数据 |
|---|---|---|---|---|---|
| A | 去程至`2/2 (575,247)` | 1／4 | 38.187秒 | `(575,246.972)` | [A去程](assets/2026-10-29-debug-cross-area/A-v5-forward.json) |
| A | 回程至`1/1 (474,234)` | 1／4 | 35.422秒 | `(474,233.915)` | [A回程](assets/2026-10-29-debug-cross-area/A-v5-reverse.json) |
| B | 去程至`2/2 (575,247)` | 1／4 | 36.328秒 | `(575,246.972)` | [B去程](assets/2026-10-29-debug-cross-area/B-v5-forward.json) |
| B | 回程至`1/1 (474,234)` | 1／4 | 39.047秒 | `(474,234.064)` | [B回程](assets/2026-10-29-debug-cross-area/B-v5-reverse.json) |

四轮均观察到最终区域、目标坐标、停止方向4/4，并逐门校验新地图与递增代次；PID、创建时间、UID及角色绑定保持一致。最终四轮没有触发请求更新。完整数据见[摘要](assets/2026-10-29-debug-cross-area/summary.json)及[原字节SHA256清单](assets/2026-10-29-debug-cross-area/manifest.json)。

以下为客户端原生PrintScreen原图，未裁剪、拼接或绘制标注。

A端回到赛丽亚房间：

![A端回程终点](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-cross-area/A-room.jpg)

A端赫顿玛尔目标区：

![A端去程终点](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-cross-area/A-hendon.jpg)

B端回到赛丽亚房间：

![B端回程终点](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-cross-area/B-room.jpg)

B端赫顿玛尔目标区：

![B端去程终点](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-cross-area/B-hendon.jpg)

## 中断及过期恢复

A端在跨区路线的赫顿玛尔长街段，用原生IJL输入接口按一次左方向键180ms。消费者返回`native_auto_move_stopped`；按键完成后的新采样确认自动移动关闭、方向停止。再观察超过两秒，命令ID未变，没有后续导航请求。[输入与状态证据](assets/2026-10-29-debug-cross-area/A-native-manual-interruption-v2.json)。

![方向键接管后停止](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-cross-area/A-interrupted.jpg)

另外单独做了消费者命令文件延迟测试，未伪造时间戳、注入网络包或直接改写游戏状态。首条正确生成的请求在写入前等待约1.1秒，实测时钟间隔1094ms；客户端真实返回`rejected/expired_request`。接口重新读取未变的角色、位置和目标，使用新ID恢复，最终到达赛丽亚房间，用时23.484秒。保留了[完整过程](assets/2026-10-29-debug-cross-area/diagnostics/A-delayed-mailbox-expiry-recovery.json)、原始96字节请求和原生回执。这一轮明确标为受控延迟测试，没有混入上面的四轮自然移动。

验收脚本的首次中断检查未限定按键后的采样；首次延迟检查要求实测间隔≥1100ms，因1094ms而返回`rc=1`，原失败记录保留。最终分别以严格晚于按键完成的采样、真实原生过期回执和原始数据哈希独立核验，没有将原脚本退出码改成成功，也没有修改产品状态以满足断言。

## 实现与复核

代码提交`8ba4b88032e9769505abb38f1a55c51c151961a8`；运行消费者SHA256为`CFE6B98B5BAA347E77DD62B929E2B51F40A0EBC3E2D0A48DDD40B1BDE347AF21`。本轮扩展Python操作接口，沿用已加载的debug.dll B31E及ijl15.dll 0F2C，没有新增服务端协议或监听。

调用者只给最终目的地：

```powershell
python tools/debug/client_route.py --directory <SnapshotDirectory> --pid <PID> --uid <UID> --town 2 --area 2 --x 575 --y 247
```

目录和身份替换成本轮真实值；完整接口说明见[debug README](../../tools/debug/README.md)。实际客户端PVF提供地面与入口。内部路径考虑原生逐轴移动、坐标取整、窄接缝及未选入口；出生门只允许能证明单向离开的单段退出。每段从新鲜实际位置重算，并共用既有PID导航锁。

发送前等待不超过200ms的采样，为原生750ms校验留下预算。数据暂缺时只等待，不发送下一段；连续缺数据五秒失败。仅原生明确过期拒绝、从未受理且角色原地停止、身份／目标／自动移动状态均未变化，才允许每个内部到达阶段最多三次新ID更新。已观察到的人工接管、回执逆转、身份或上下文变化会中断。[最终源级审查](assets/2026-10-29-debug-cross-area/review/client-route-expiry-recovery-final-review.md)无剩余P1/P2。

## 测试与范围

debug测试为**152项通过、134个子用例通过**。[日志](assets/2026-10-29-debug-cross-area/tests/route-aged-cancellation-green.log)包含真实PVF、窄连接浮点停步、整段避门、出生门反向重入、身份变化、人工中断、锁、等待与请求更新回归。

全仓Python测试为450项通过、182个子用例通过、6项失败，未报告全仓GREEN。这6项在未包含本次消费者的基线工作树也分别复现：[全仓日志](assets/2026-10-29-debug-cross-area/tests/root-full-repository-after-closeout.log)、[旧台账基线](assets/2026-10-29-debug-cross-area/tests/baseline-ledger-existing-failures.log)、[编排基线](assets/2026-10-29-debug-cross-area/tests/baseline-orchestrate-existing-failures.log)。

- `protocol/tests/test_ledger.py::test_scenario_rows_cover_all_four_scenarios_and_match_s0_expects`
- `protocol/tests/test_ledger.py::test_cmd_ledger_end_to_end_writes_three_ledgers`
- `protocol/tests/test_ledger.py::test_cmd_ledger_merges_runs_and_does_not_clear_other_scenarios`
- `tools/acceptance/tests/test_cli_orchestrate.py::test_orchestrate_wires_server_actors_steps_and_collect`
- `tools/acceptance/tests/test_cli_orchestrate.py::test_orchestrate_waits_for_the_server_to_listen_before_starting_actors`
- `tools/acceptance/tests/test_cli_orchestrate.py::test_orchestrate_reports_ambiguous_pid_as_clear_failure_not_traceback`

前三项仍断言旧12行，当前场景实际23行；后三项的固定2311端口位于Windows排除范围2280–2379，绑定返回10013。停止验收进程后仍复现，临时端口可绑定；没有修改系统端口设置或跳过测试。

本轮实机范围为两城镇五区域，没有宣称全部城镇、运输方式、动态障碍、未被采样观察到的人工输入或副本内导航。U04背包丢弃、四客户端稀有度／史诗矩阵、技能MP／冷却及怪物位置同步仍按[待办](2026-10-28-restoration-backlog.md)继续，未因跨区通过而关闭。

验收使用的V私有运行阶段已按精确PID／路径／创建时间停止并归档；恢复了两份正常配置、八份输入文件。15项受保护输入、正式入口50个文件（包含已批准Native更新）、四份Native DLL、四份debug DLL和六份消费者文件均已核对。[恢复记录](assets/2026-10-29-debug-cross-area/closeout-restoration.json)保留校验结果；旧S/T/U冻结验收目录未改写。
