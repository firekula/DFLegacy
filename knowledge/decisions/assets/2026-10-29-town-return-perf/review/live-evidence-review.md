# 回城与性能记录离线一致性审核

结论：所列实机记录、运行身份、构建及收尾清单关联一致，未发现未闭合的证据冲突。此审核仅检查已有离线 JSON、指定构建产物哈希和公开报告，不是独立实机认证；未启动或查询游戏进程、输入、部署、修改产品，也未读取原始 netdump、私有存档或配置正文。

构建清单记载源码 `5f101c9e54314cb5643c7afee2a09455380fa18f`；实际 `build-return-green/debug.dll` SHA256 为 `C3ED35E1008D74536D96307A9478C636AAD092186DEF3C9EE99CF8F3652D14AD`，与清单一致。指定场景记录均引用同一构建清单 SHA `76F9EF9A…F626FAA` 和运行记录 SHA `E9243126…C4D145`，两者已按文件字节核对。

运行记录 A=64280、B=60624，路径分别为两端 DNF.exe；进程起始 FILETIME 分别为134377284957567115、134377284966393813，与各前后 SDK、路线终态和运行记录 UTC 时间换算一致。

| 记录 | 离线核对结果 |
|---|---|
| `return-town-select.json`、`B-return-town.json` | 同 PID/start 下 scene3→1，SDK代次6→7，返回入口1/2；after采样分别比输入完成tick晚63/62ms。回城结论来自 SDK 转换，不来自输入 ACK。 |
| A 回城后左/右键 | 路线 request1/2 均 `arrived`；分别到1/0(1135,294)、1/0(900,294)，耗时6141/2750ms。 |
| B 回城后右/左键 | 路线 request1/2 均 `arrived`；分别从1/2跨区到2/0(1431,299)、同区到(1550,299)，耗时28094/2063ms。 |
| 四项到达的共同验证 | terminal与history最后状态一致；origin actor/user/map/context对应操作前 SDK；最终区域一致、各轴误差≤0.488、方向4/4；随后 SDK 比 terminal晚235/250/328/282ms，且始终是回城前后的同一进程身份。原生路线与SDK使用各自代次，不错误要求两套代次数字相等。 |
| `A-manual-interrupt.json` | request3为 `interrupted/native_auto_move_stopped`，原生观察auto=false；随后仍停在1/0，未到跨区目标。 |
| `A-hidden-no-route.json`、`B-NPC-no-route.json` | 两项记录 `route_unchanged=true`；对应输入记录有完成回执及更晚 SDK，A图已隐藏、B图仍显示。A隐藏点击后原生地面移动确实改变位置/目的地；这里只支持“未产生新的地图寻路结果”，不支持“没有移动”。NPC命中及画面效果不属于本次独立视觉核验。 |

回城及保护记录附带的旧 `dfl-client-navigation/v1` 回执，tick早于本次操作，不用于证明当前操作成功。四项到达采用各自的新 `dfl-client-route/v1` 终态与后续 SDK。

性能只采用 `baseline-valid-v2-5E5.json` 和 `optimized-valid-v2-C3ED.json` 两个约20秒样本。重新从 `file_changes`、SDK年龄及进程计数计算，结果支持：

| 指标 | 基线 A/B | 修改后 A/B |
|---|---:|---:|
| party-result替换次数 |81/82|0/0|
| SDK数据年龄中位数(ms) |179.5/172|31/31.5|
| 进程write_ops增量 |263/245|583/571|
| 暖机3秒后、前台单次更新且有区间界限的记录 |2744/0|2606/0|
| 上述记录中已证明下界>50ms |0/0|0/0|

重复结果“内容未变”对应两个观测文件记录的 `result_bytes_unchanged=true`；本次没有读取结果文件正文重新比较。两端合计write_ops为508→1154，不能笼统称总IO减少。SDK年龄更小也不证明整个插件耗时减少或偶发卡顿已解决。

`B5DD34` 是原生更新计数的外部轮询区间，不是 GPU Present；摘要筛选还要求t≥3秒暖机条件。B在此筛选下无前台区间，不能外推其前台帧性能。修改后样本存在上界约50.249ms、下界不足50ms的不确定区间；“没有已证明>50ms的区间”不等于“所有更新都<50ms”。未使用任何早先B5DD20误采记录作帧耗时证据。

停止与恢复链：`deployment.json`为stopped，原PID/path/start与运行记录一致，StoppedAtUtc=2026-10-29T06:27:15.8424584Z，停止前活动频道session记为0。`final-verification.json`记录NoNativeProcesses=true及15保护项；其ClosedManifestSha256对应 `closed-return-green/manifest.json`（7E94838F…CA6D41），RestorationSha256对应 `closeout-restoration.json`（84B9EBD6…FD8780），均已核对。恢复清单记录2配置、8接口；正常目录50项中42项保持、8项托管更新均匹配构建清单，不能描述为50项全部不变。原存档仅核对清单记录，未读取存档内容或独立复查当前机器状态。

只读检查公开 `2026-10-29-town-return-perf-verification.md`，其保留原生地面行为、总写操作增加、两个短样本及卡顿未完全归因的限定与证据相符，未见把ACK当到达、把原生更新当GPU帧时间或宣称全部卡顿已修复的过度结论。未外推到全部城镇、运输、动态障碍或副本小地图。
