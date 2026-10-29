# 2026-10-30：死亡队员视角残留已拾取地面物

2026-10-30后续：[接收端根因、修复及四端实机已完成](2026-10-30-dead-ground-removal-verification.md)。下文为最初只读调查历史，不能继续当作当前待办状态；玩家击杀与死亡观测也已分别完成。

后续更新：用户指定U11移到整个队列最后；[玩家击杀接口已独立实现并通过队员／队长四端验收](2026-10-30-player-kill-mp-verification.md)。下文“接口尚未实现”和优先修复建议为最初只读调查时的状态，当前顺序以[最新列表](2026-10-28-restoration-backlog.md)为准；地面残留本身仍未修复。

用户补充第一轮四端换房失败后的实际画面：C/D死亡，A返回原房拾取蓝装，B端地面消失，C/D端仍存在。本项不属于前组“四活人换房返回通过”的范围，新增待办单独跟踪；当前没有完成修复或重新实机复现。

## 已核对的证据

原轮为 `drop4-v1`，角色进程A65460/B8068/C73420/D10192；原蓝装24002、ground23，实例 `01a18860-caab-7803-a562-f84ce92c8471`。服务端拾取event16为committed/persisted，得主A，主背包22。前组关闭后存档复核仍只有一份原装备，不存在已证实的重复入账。

四端原生 `ground-item-removal-<pid>.log` 都记录了该物品的移除通知：

| 客户端 | ground23原始记录 | 含义 |
|---|---|---|
| A | `absent-inactive-or-outside-map picker=1 item=24002 ground=23` | 拾取者可能已由NOTI39移除，不能仅凭这行判为异常 |
| B | `deactivated picker=1 item=24002 ground=23` | 扩展移除入口找到地面对象并停用 |
| C | `absent-inactive-or-outside-map picker=1 item=24002 ground=23` | 收到通知，但未成功停用对象 |
| D | `absent-inactive-or-outside-map picker=1 item=24002 ground=23` | 收到通知，但未成功停用对象 |

服务端 `BroadcastPickupRemovalAsync` 遍历实例全部成员，没有按死亡过滤；原生四端日志进一步证明本次移除扩展已经到达处理入口。因此不能解释成服务端没有向C/D发送。C/D的日志把地图校验失败、对象不存在、对象已非active等合并成一个原因，尚不足以确定是哪条分支。

原生 `ground_item_removal.cpp::CurrentMap` 需要dispatcher所指地图与本地actor地图一致；`ApplyRemoval`还检查对象地图、类型、ID和active。这些路径没有显式按死亡过滤，但死亡后actor／观战地图关系是否变化，需要增加分支诊断和受控复现验证，不能直接猜测放宽校验。

另外，`client_test_control_runtime.cpp::CaptureTestState`依赖存活`LiveContext`，角色死亡后地面／库存测试快照停止刷新。这是独立观测缺口，不能用旧快照证明画面残留，更不能用它否认用户实际看到的画面。本轮只读调查未启动客户端、未修改已封存私有证据。

## 现有测试接口与接续

当前 `test_control.py` 的 `player` 子命令只有 `teleport`，`monster kill`只支持活着且本机拥有控制权的怪物；DLTC operation仅1～5，没有玩家死亡命令。服务端现有对象HP发送底层方法也不等同于已验证、可用的玩家死亡测试接口，不能拿怪物ID接口代用。

下一步先补齐死亡角色的只读地面观测与移除拒绝原因，再在相同测试租约／身份约束下验证原生玩家死亡入口，使死亡、复活窗口和服务端状态走正常链路；随后重测两活两死状态的原房留地→换房→返回→拾取，同步核对四端清除、唯一保存以及复活后的残留。玩家死亡测试接口尚未实现，本记录不声称已经可用。
