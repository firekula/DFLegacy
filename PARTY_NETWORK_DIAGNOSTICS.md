# ACT1 组队黑钻与联机诊断（2026-11-06）

## 本次修改

组队结算的 NOTI 35 原来只填写收件人自己的黑钻奖励栏，CMD 73 也把其他队员的位置标成空位。现在按副本结算代次生成并缓存各成员自己的奖励，给所有成员发送一致的黑钻栏和真实选卡资格。普通、已过期账号不会借用队友的黑钻权益；奖励仍由所属角色的原有结算路径入账。

本机 TCP 中继新增连接编号、方向、已完成发送字节数、持续时间及结束原因。正常 EOF 保留半关闭语义，让另一方向仍能收到回复；异常重置则取消另一方向的等待，退出中继时等待已接受的连接释放。

服务端玩法 UDP 的任一接收循环发生异常时，现在撤销就绪状态、关闭两个监听端口并向宿主报告原始异常。此前它会被仍在运行的另一循环拖住，导致端口已失效但后台仍显示就绪。正常的静默、无效数据和 UDP 丢包不触发这条致命故障处理。

## 开启服务端日志

需要使用包含本次改动的服务端；未发布的旧镜像不识别该新增选项。在 Compose 原有 `environment` 中增加：

```yaml
DFLegacy__EnableConnectionDiagnostics: "true"
```

无需打开 `EnablePacketTracing` 或全局 Debug 日志。新增诊断不记录密码、令牌或包正文。

复现期间保存完整日志，再按关键字筛选；将容器名替换为实际名称：

```sh
podman logs --timestamps --since 30m dflegacy_dflegacy_1 > party-server.log 2>&1
grep -E 'party-net|Native fallback|Gameplay UDP|Entrance session' party-server.log
```

记下故障的当地时间和时区、双方角色、队长是谁、地下城/房间、当时是战斗/换房/翻牌，以及客户端是卡住、弹窗退出还是直接消失。容器 UTC 时间与北京时间相差八小时，比较时优先使用 `--timestamps` 的完整时间。

| 日志 | 能说明什么 |
| --- | --- |
| `[party-net] relay stalled` | 发送或等待目标队列已超过 5 秒；同一连接最多每 30 秒报告一次。`queueTarget` 是等待的目标，`queueWaitMs`/`writeWaitMs` 区分排队和套接字写阻塞。 |
| `[party-net] relay drop` | 服务端实际拒绝或丢弃的原因与累计次数。按 1、2、4、8…次采样输出，避免无效包刷屏。不是互联网丢包率。 |
| `malformed-udp` | 回退中继 UDP 的外层头/长度无效。不能由此推断玩家间直连 UDP 损坏。 |
| `udp-target-queue-full` | UDP 到 TCP 回退的目标输出队列满。 |
| `route-unavailable` / `stale-route` | 目标未就绪、队伍/副本不匹配，或排队后会话已经更换。 |
| `[party-net] relay closed` | 回退中继连接与游戏会话的关联、结束原因、收发帧数和残留队列。 |
| `[party-net] channel closed` | 游戏频道会话结束时的角色标识、地下城/房间、结算代次、最后解码的命令、距该命令的时间、收发数及异常类别。 |
| `Gameplay UDP primary/secondary loop failed` | 具体 UDP 监听循环已异常，两个端口已退出；需要查看同一条原始异常及宿主后续状态。 |

诊断完成后把选项改回 `false` 并重建容器即可关闭新增服务端摘要。

## 双方客户端需要保留的文件

使用包含新中继实现的启动器后，本机中继事件写入游戏目录的 `Logs/DFLegacy/launcher.log`，包含 `event=connection-start`、`event=direction-end`、`event=connection-end`。`bytes` 只统计已完成的写入；失败写入可能已部分发送，因此不能把它当抓包精确字节数。

双方均保留整个 `Logs/DFLegacy/`，以及 `Logs/` 中实际生成的 `DNF.trc`、`.dmp`、`.trc`、`DNF_DUMP*` 等原生文件。测试版客户端的 `debug-overlay-<pid>.log` 可包含异常地址和寄存器；其中首次机会异常不等于最终崩溃。

普通玩家包限制原生诊断钩子，单独设置 `DumpNetwork=1` 不会使它输出网络转储。如需定位游戏内部崩溃，使用独立测试包采集被动诊断，不要为了采集日志开启自动操作游戏的 `EnableAutoInput`。本次没有变更玩家原生补丁，也没有在正式目录安装测试包。

## 本地模拟及边界

```powershell
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --party-card
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --party-network
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --gameplay-datagram-lifecycle
dotnet run --project tests/DFLegacy.Relay.Tests -c Release
dotnet run --project tests/DFLegacy.SmokeTests -c Release
```

测试使用临时存档和回环随机端口，涵盖混合黑钻资格、奖励单次生成、独立落库、UDP 可控丢失/延迟/重复/乱序、TCP 拆帧、慢接收者、会话替换、异常重置、半关闭和监听生命周期。

慢接收者测试还确认了现有 TCP 背压的边界：一个发送者在等待某队员的满队列时，它后续发给健康队员的 TCP 帧也会延后；被阻塞队员退出后，后续帧可继续。独立的 UDP 转发仍能服务健康队员。本次保留可靠 TCP 的原有排队策略，新增日志用来确认玩家故障时是否真的命中这一条件，不增加静默丢弃 TCP 帧或强制踢人的超时。

这些测试不能代替双客户端游戏实测，也没有复现并证明玩家报告的那次原生闪退。服务端只能观察经过自己的数据，无法据此计算玩家之间直连 UDP 的丢包率。

## 当前验证记录

2026-11-06，第一章隔离工作区：最终完整服务端冒烟通过（`All DFLegacy protocol smoke tests passed.`），独立中继测试 10/10 通过，启动器 Release 编译 0 警告、0 错误。代码审查发现的正常停服误分类、日志写入异常影响传输、黑钻资格异步读取期间停局及周末日期影响测试等问题均已处理。本次没有提交、发布或安装到玩家正式目录。

### 玩家补充：房间内战斗时触发

排查重点收窄到战斗期间的持续收发、技能/受击/召唤物及原生对象状态。`--party-network` 现在把会话放在同一个非空副本上下文中，额外双向连续发送各 512 个不同长度的数据帧，其中一端接收暂停 350ms，验证恢复后全部帧的顺序和内容。定向测试通过；这些是合成不透明数据，只验证传输，不冒充客户端实际接受了技能包。

同时重建并通过 `ijl15_compatibility` 原生兼容测试（含发送队列并发和对象初始化保护），以及下面 21 项原始运动指令回放/数据解析测试：

```powershell
python -m unittest tools.acceptance.tests.test_motion_horizon_native tools.acceptance.tests.test_motion_grid_native tests.test_act1_gameplay_evidence
```

本机现有 `Logs/DNF.trc` 最近一轮只包含打开/正常关闭，未提供故障指令；本机记录不能代替反馈玩家的崩溃证据。后续复现需要双方职业、触发前技能/召唤物情况以及同一时段双方日志，以区分传输停顿与客户端处理状态时崩溃。尚未针对未证实的技能/同步路径新增绕过逻辑。

进一步补充：队长为鬼剑士，队员为魔法师；出问题时魔法师正在移动，召唤物进图时就已经召唤。后续游戏复现按这个组合，优先观察已有召唤物的控制者/状态变更与角色移动、受击的时间关系；不把它记录成“刚释放召唤技能就崩溃”。

现有 `summon_owner_death` 补丁的地图移除入口要求副本场景、玩家对象且 HP 不大于零；`mage_skill_contact` 则保护原生技能 20 接触路径的缺失模板/等级。仅凭此次移动时间点，尚无证据指向这两处保护失效。需要故障双方同一时段的 `launcher.log`、`party-combat-detail-*.log`、`summon-owner-death-*.log`、`mage-skill-contact-*.log`（如存在）及原生崩溃文件；缺少某个按需产生的日志本身不代表异常。优先保留整个日志目录，避免丢失临近事件。
