# 源码、文档与证据归档核验

2026-10-30。按用户要求统一更新文档与Changelog，将当前 `codex/channel-return-next` 分支的阻断诊断及三轮U05增量一并归档。日常运行目录、正常存档和其他worktree不随Git提交改变。当前功能与剩余边界见[总交接](../../HANDOFF-2026-10-30.md)。

## 提交前自动化

- 完整Python：**620 passed、569 subtests、0 skipped、exit0**。已提供原ACT1、原生导出及相关夹具；Unicorn `mem_map` 内既有Windows faulthandler诊断仍原样留档，测试继续完成。
- Debug与IJL的Release／Win32 MSBuild均退出0。
- 完整IJL compatibility通过，包括运动修正、输入、地面接收等现有套件。
- Debug的client-state、navigation、party、真实PVF town-route、frame-profile、gameplay-trace、native-loop、test-control、map-feedback、gameplay ABI及完整overlay共11组均退出0。
- `dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --debug-overlay`为0失败，并生成本次新的跨语言fixture供完整overlay使用；未冒充重新运行托管全套。

本轮没有重开四端或重复已完成实机矩阵。当前debug为 `2D443011161A943B79BE4ED7C02EACAFE02F120DC5B0688C142A313D789BBF83`，IJL为 `0103C7DCE1FA8AEA5233D7A02C6E1115F83598A8E3A02EF85AC8834F24B2871A`，与最近实载产物一致。日志与结果见[本次核验附件](assets/2026-10-30-source-archive/manifest.json)。

## 冻结附件与一处历史报告哈希差异

五组旧证据共156个文件：151个清单条目逐项核对大小及SHA256一致，另有5份原始根清单一并保持字节。没有修改旧截图、日志、JSON或清单来消除差异；通过Git属性保留原字节，并精确加入被全局日志规则忽略的原件。

检查发现 `2026-10-30-remaining-blockers-followup` 旧根清单的 `report_sha256` 为 `A90F91701D73588123FFC73FDEEED1AD007E3D278E3BCA7A6E6707A6A6EF3326`，当前报告正文为 `F52FEF944E9B8C36AB036D848564B6426D6E71ECE8B5D01FE4A28DBB1DA0D2FD`。这是一处报告绑定不一致，不能称旧字段仍验证了当前正文。此次保留旧清单，并在[核验记录](assets/2026-10-30-source-archive/historical-manifest-audit.json)明确记录差异，在新总清单中绑定当前正文；不据此推断未取证的变更原因。该组29份原始附件全部与旧清单一致。

有界报告和冻结附件使用原始字节哈希；可编辑入口文档与源码同时记录LF规范化哈希，避免把正常Git换行转换误称代码变更。提交时还核对Git索引中的附件字节与磁盘原件一致。

## 交付与保护

本次更新包括README、STATUS、Changelog、当前及旧交接导航、构建／部署指南、验收README／RUNBOOK、debug说明、接续列表、覆盖索引和夜间报告入口。旧报告继续保留当时数据与构建身份，U07／GP42、U08及U05尚未证明的全局输入／密码PvP／特殊装备精度边界均未写成完成。

最新私有测试的14个临时替换文件及8项保护哈希复核一致；正常存档仍为 `E0E2FB4BB53B9775463E9F54905CE9CB3426D72A9EB0BDADF845642ABB98A283`。所属客户端／启动器／服务端保持关闭。Python缓存只加入忽略规则，没有删除；原始账号档、凭据、运行目录及未过滤网流不纳入提交。
