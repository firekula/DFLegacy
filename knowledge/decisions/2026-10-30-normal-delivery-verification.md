# 日常目录交付与四端启动检查

2026-10-30 04:34（北京时间）交付检查结束。已将本夜实际验收的服务端 AA11B5DB、IJL 8B060DBB 和 debug 1AFDF77A 更新到日常启动目录及两个启动器的补丁包；六个客户端目录为 DF、Client_test2、Loot4_A/B/C/D。33个文件逐项核对，其中26个改变，原件均备份；完整旧／新哈希见[交付清单](assets/2026-10-30-normal-delivery/acceptance.json)。产品源码仍在当前 D: 工作树，未合并或重置 G: 主检出。

六份 INI 仅更改 SnapshotDirectory，统一指向 `G:\Game\DNF_ACT1\DF\town-test\debug-overlay`；其余字节保持。测试修改和回调计时默认关闭，原有只读 ExportState 保留。正常角色存档、服务端业务配置、两个启动器配置、两份 DNF.exe 和 Script.pvf 共8个保护项哈希均保持；角色存档仍为 E0E2FB4BB53B9775463E9F54905CE9CB3426D72A9EB0BDADF845642ABB98A283，没有用测试档覆盖。

正常服务端入口为 `G:\Game\DNF_ACT1\DF\town-test\Start-NormalServer.cmd`，随后使用既有启动器。现有脚本按启动器配置使用入口27001、频道7001；本轮以相同脚本、正常目录的新程序和显式私有 DataPath 检查启动，不改业务配置。四客户端实际登录、各自原生单目标跨区域寻路、三次邀请／接受后，四端均收敛为四人队伍。HTTP管理入口返回200；这不代表完整管理界面验收。检查后四个客户端和服务端全部关闭，停止时活动会话为0，已归档本轮输入计划。

![日常目录新模块四人组队界面](assets/2026-10-30-normal-delivery/01-normal-four-party.jpg)

服务端首次直接运行 EXE 的探针使用默认2311，遇到 Windows 保留端口拒绝；随后采用项目既有正常启动脚本成功。此探针未启动客户端，失败日志单独保留，没有修改系统端口或安全设置。U11前组私有复制启动器未返回的探针亦保留，实际验收改用已知正常启动器，见[U11记录](2026-10-30-dead-ground-removal-verification.md)。

原件备份及回滚脚本位于 `G:\Game\DNF_ACT1\DFLegacy\.tmp\overnight-delivery-20261030`。`Rollback.ps1` 默认仅检查，带 `-Apply` 才恢复26个改动文件；已完成默认检查，没有执行回滚。脚本要求客户端／服务端／启动器关闭，核对当前新文件与备份哈希，遇到后续编辑会拒绝覆盖，不回退角色数据。完整私有日志、进程身份与关闭记录留在该目录；公开附件仅包含过滤清单及原始关键图片，其[字节清单](assets/2026-10-30-normal-delivery/manifest.json)可复核。
