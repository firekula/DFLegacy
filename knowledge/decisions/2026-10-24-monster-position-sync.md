# 双人怪物位置修正：现有同步与官服样本核查

2026-10-24。范围：检查神枪手队长与圣职者组队时的怪物位置修正；本轮不改变战斗同步、控制权或 UDP 转发。

## 已确认的路径

| 项目 | 当前实现 | 官服样本 / 原客户端证据 |
|---|---|---|
| 队伍主机 | `DungeonEntryPackets.PlanMemberEntry`、`EntranceService.SendDungeonSelectionStateAsync` 向全队发送相同的队长槽位；已不是每人各当主机的旧实现 | `CParty::set_host` 0x0859CDBC 取队长；`send_host_info` 0x0859CDD2 把同一槽位通过 NOTI 26 发全队。既有原始证据 `official/ida_targeted_out.txt`、`official/STATIC-FINDINGS.md` A.1 |
| 服务端职责 | 提供相同的 START_MAP 怪物表、种子、房间与加载屏障；服务端处理死亡、掉落、结算 | 官服 `CParty::game_start / move_map / finish_loading` 同类编排。不能据此推导具体每只怪物永久归队长 |
| 实时移动 | `GameplayDatagramService.RelayGameplayDatagramAsync` 按完整 IP:port 确认发送者，将原始数据转给同队且同一副本实例的其他端点，排除自己 | 官服 relay 样本 `UDPHandlerRelay::dispatch` 0x0805E4C0 → `RelayService::relayToTCP` 0x08051B76；按头部目的账号找 TCPUser 转交。该路径没有怪物坐标计算，传输封装与我们的 UDP 广播不同 |
| 移动包校验 | 服务端不裁决坐标、不选怪物控制者、不重新编码客户端包；解析失败仅记诊断日志，随后仍逐字节中继 | 原客户端 0x0078EE00 的 GP4 分支查对象、校验房间与包里声明的控制者，再调用 0x004C9270 应用移动 |
| 每只怪物控制权 | 留给原客户端 | GP6 `SET_MONSTER_MASTER` 查玩家与怪物，调用怪物虚表 +0x2D8。怪物实现 0x004AE790 更新独立控制者；0x00473620 判断对象控制者是否本机角色。队伍主机与对象控制者是两个概念 |
| 位置修正方式 | 原客户端执行 | 0x004C9270：标志 0x10 分支直接复制移动状态；普通分支使用目标位置、速度与时间插值，默认窗口值 500。不能把所有可见修正都归为服务端回拉 |

官服材料是仓库现有 `df_game_r.bin` 与 `template/neople/relay/df_relay_r`。未证明与本项目 2008 ACT1 客户端同版；上述主机语义有客户端交叉验证，中继结构仅作为样本事实，不能直接照搬其封装。

## 当前值得优先调查的线索

1. **对象控制权与移动包的交接**。旧日志中，同一怪物曾被两端分别声明由不同玩家控制。这能解释为何队长也可能接收修正，但还不能证明它就是本次异常。
2. **跨房间迟到包 / 可靠包重发**。当前中继只比较副本实例，不比较房间代次；客户端 GP4 自带房间检查，但 GP5/GP6 等其他状态路径也需要逐项核对，不能假定相同。
3. **状态与移动包的时序**。GP5 会改变受击/动作状态，GP4 会修正运动；还要分辨攻击、击退、浮空、抓取后的位置变化与实际控制权冲突。

20:51:27 的旧日志样本（均 CRC 校验通过，仅作线索，不作为本次神枪手异常的复现）：

| 文件 | 时间 / 方向 | GP4 对象 | 声明控制者 |
|---|---|---|---|
| `Client_test2/netdump-35560.log` | 20:51:27.676 TX，槽 1，序号 2 | kind 529，UID 0 | kind 273，UID 1 |
| `DF/netdump-8844.log` | 20:51:27.678 RX，相同数据 | kind 529，UID 0 | kind 273，UID 1 |
| `DF/netdump-8844.log` | 20:51:27.778 TX，槽 0，序号 4 | kind 529，UID 0 | kind 273，UID 2 |
| `Client_test2/netdump-35560.log` | 20:51:27.788 RX，相同数据 | kind 529，UID 0 | kind 273，UID 2 |

这证明双方发出过不同控制者的移动包，以及这四条样本的中继到达；**不证明接收端接受了包**，因为 GP4 还会检查本地控制者。完整异常前后的 GP6/本地对象变化尚未对齐。

另一个诊断限制：`GameDatagramProtocol` 固定使用 XOR 0x14 / ROL2 解密；旧日志中有通过 XOR 0x56 / ROL1 才能验证 CRC 的 GP4，也有原文与 GP42 包装。不能把当前日志里的 `failed CRC or cipher validation` 当作坏包或丢包证据。本轮没有修改该解析器或发送路径。离线摘要仅覆盖能验证 CRC 的外层包，不代表已解出全部嵌套 GP5/GP6。

## 下一步建议（待讨论）

先做只读诊断，复现一次神枪手队长 + 圣职者：逐帧记录房间代次、怪物 UID、本地控制者、GP4/5/6 的实际收发、修正前后坐标与时间；保留两端 netdump，记录屏幕跳变时刻。随后按证据区分控制权争用、迟到包、受击状态或插值问题。

暂不增加“只有队长能发怪物数据”的过滤。这会干预原客户端对象控制权交接，也可能破坏队员命中、击退与技能同步。

本轮工作文件：`.tmp/sync-inspect/client-probe.txt`（0x78EE00 完整分派）、`client-movement.txt`、`client-master-setter.asm`、`official-relay-asm.txt`、`netdump-summary.json`、`inspect-netdumps.py`。这些是本地调查产物，非产品协议变更。
