# 2026-10-30：N地图副本门前区域右键终点修复

用户反馈大地图右键寻路已符合预期，仅副本门前区域无法点击。根因是客户端控制器的终点白名单：原版 PVF 将格兰之森入口城镇区域 `1/2` 标记为 `dungeon_gate`，目录路由已允许其作为起终点，但控制器仍拒绝终点。原 DLL `2D4430…` 在 PID8256 上对 `1/2 (320,247)` 返回 `unsupported_area`，见[修复前原始数据](assets/2026-10-30-dungeon-gate-map-target/live-red.json)。

## ACT1依据与修复范围

原始 PVF SHA256 `288D58AACE8ED4724A0A94AAA4CD0957B1D52C633771132EDE2F96363C1B506F`。区域对应 `map/elvengard/d_elvengard.map`，可行走矩形 `(7,192,655,128)`；返回城镇门与 `(-1,-1)` 副本选择门均保留原始数据，详见[提取记录](assets/2026-10-30-dungeon-gate-map-target/original-pvf-gate.json)。

控制器初始化、当前区域校验及重规划现一致允许**精确最终区域**为 `dungeon_gate`。保持初始入口离开时的地图／代际绑定，目录继续禁止借入口穿越。最终点仍须避开所有传送门及其4px扩张区；未改变原版地图命中、图标保护、黄色双次扩散反馈、身份／场景／新鲜度／手动中断规则、ABI和服务器逻辑。

本次终点支持取代[前一日回城记录](2026-10-29-town-return-perf-verification.md)中“入口仍不能作为一般寻路目标”的当时限制。该历史报告与其原件保留，不将本次结果移记到旧构建。

## 回归与实机证据

- [RED](assets/2026-10-30-dungeon-gate-map-target/red-tests.log)：旧策略下新增跨区／同区／真实PVF及门保护断言产生9项失败。修复后[GREEN](assets/2026-10-30-dungeon-gate-map-target/green-tests.log)：目录72检查、控制器、适配器、IO均0失败。覆盖入口进入后的出生门退出、同区到达、不可借道、危险门边缘、选择／副本场景中断和真实PVF完整到达。
- [完整Python](assets/2026-10-30-dungeon-gate-map-target/python-regression.log)：620项、569子测试，退出0；保留既有Unicorn faulthandler诊断。9组其他debug／ABI（无文本输出的ABI另附[命令与退出码](assets/2026-10-30-dungeon-gate-map-target/gameplay-abi-result.json)）及[overlay运行测试](assets/2026-10-30-dungeon-gate-map-target/overlay-runtime.log)退出0。IJL未改，沿用[基线完整IJL验收](2026-10-30-source-archive-verification.md)，不重开已闭合的四端手工矩阵。
- PID48476／UID1实载新DLL。原生文件输入右键屏幕 `(458,279)`，逆投影目标 `1/2 (323,248)`；从赛丽亚房间经 `1/0` 到 `1/2`，6条原生目的地、约9.3秒，最终SDK物理坐标及停止方向匹配，见[跨区原件](assets/2026-10-30-dungeon-gate-map-target/cross-entry.json)。
- 同入口右键 `(449,280)` 到 `(120,253)`，1条目的地、约2.2秒，[同区原件](assets/2026-10-30-dungeon-gate-map-target/same-entry.json)与[黄色反馈原图](assets/2026-10-30-dungeon-gate-map-target/same-entry-feedback.jpg)对应。右键返回 `1/0 (1088,264)`，3条目的地、约3.8秒，见[返回城镇](assets/2026-10-30-dungeon-gate-map-target/return-town.json)。
- CLI 请求副本选择门 `(652,256)` 返回 `goal_inside_portal`，新鲜SDK位置保持，见[拒绝证据](assets/2026-10-30-dungeon-gate-map-target/selector-rejected.json)。随后同修复前请求 `1/2 (320,247)` 成功，见[CLI与SDK到达](assets/2026-10-30-dungeon-gate-map-target/cli-arrival.json)；N地图关闭后的[门前原图](assets/2026-10-30-dungeon-gate-map-target/physical-arrival.jpg)保留原JPEG字节。

用户的[标注截图](assets/2026-10-30-dungeon-gate-map-target/user-reported-region.png)仅标识反馈区域，明确不计作验收截图。此次实机仅一个客户端的新增场景，不外推其他入口或全部城镇；U05既有四端结论仍归原构建，U07／GP42、U08保留。

## 构建、运行与存档

基于提交 `37f86cb8403b7b408231395dd377a218351c64ee`。新debug SHA256 `0082559CB80050226500290C6BAB2CD482BD09B571682D0EAB614F3F5099CFCB`；未改IJL SHA256 `0103C7DCE1FA8AEA5233D7A02C6E1115F83598A8E3A02EF85AC8834F24B2871A`。源码、EXE、PVF及28份附件字节绑定见[清单](assets/2026-10-30-dungeon-gate-map-target/manifest.json)，清单SHA256 `836544AD981E0B845F0DAFA855E94EA43C8631EC774A86E4132F2D97F5065D84`。独立只读审查未发现Critical／Important问题，并独立重跑真实PVF路由测试通过。

只重启用户正在测试的 `Loot4_A` 客户端，重新选择原slot2角色；保留同一私有服务PID51664及 `manual-one-20261030/state-v1.json`，没有重新种子复制或回写正常存档。原PID8256与新PID48476证据分开。加载模块路径／哈希、8项保护输入及无活动输入计划见[身份和保护记录](assets/2026-10-30-dungeon-gate-map-target/live-identity-and-protection.json)。正常存档SHA256仍为 `E0E2FB4BB53B9775463E9F54905CE9CB3426D72A9EB0BDADF845642ABB98A283`。

手测客户端保留在门前，启动器配置和启动器旁临时IJL已恢复。当前客户端DLL与测试配置暂保留供用户继续测试；原始5份替换备份不改写，清理期望哈希已更新为本次产物。私有运行根为 `G:\Game\DNF_ACT1\DFLegacy\.tmp\manual-one-20261030`，停止与恢复仍用其中 `Stop-ManualTest.ps1`，勿删除该目录或复制其私有存档覆盖正常档。其他日常客户端目录未部署新DLL；原始账号配置、私有存档和journal未公开。
