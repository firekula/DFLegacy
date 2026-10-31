# 城镇队友位置差异：六输入窗口与原版停步容差

接续地图右键反馈验收，重新观察到同区角色停步后，本人与队友视角相差约25～41像素。四条有效移动末消息均完整送达对端；原版客户端明确存在逐轴小于50像素的停步容差，本轮原生目标变化与这条规则相符。因此保留原生行为，本轮没有为这些小差强制写坐标或修改阈值。

这是限定诊断结果，不是全量U07或怪物同步修复。旧247.596/202样本没有因此获得独立闭环；大差值、丢包和怪物接收门控仍待各自取证。

## 环境与观察方法

两端沿用地图验收022A构建：A UID1/PID8820，B UID2/PID8568，同Town2/0。通过已有debug组队请求和接受接口建立队伍，再分别执行原生地面左键、原生短方向键、单目标DLRT。未修改存档来摆位，未发外部业务包或写入角色状态。

六轮各约11秒，前2秒静止；原生输入或单目标接口执行后继续观察。每轮137次成对只读采样，每端3836次受限读取；绑定UID、user→actor、Town虚表、map及进程启动时间，坐标来自原生运动控制器，同时记录目的地和方向。每次读取起止QPC保留，属于顺序采样，不能称为原子双端快照。测量窗口内没有截图。[六轮原始记录](assets/2026-10-29-town-position-followup/live/A-direction.json)、[观察器源码](assets/2026-10-29-town-position-followup/observe-six-inputs.py)、[运行身份](assets/2026-10-29-town-position-followup/running-deployment.json)。

下图仅记录同区组队环境，不用于按像素测算坐标差，也不作为本轮地面点击已经走动的证据。

![同区双角色观察环境，A端原生截图](assets/2026-10-29-town-position-followup/A-standing-town.jpg)

## 对齐上报、广播与原生目标

过滤器内部按完整TCP流解码以维护客户端rolling cipher，只输出CMD37和NOTI22的9字节位置体及必要时间、身份和哈希。CMD前2字节是序列，NOTI前2字节是UID；转发比较其余7字节，不把两者整个body哈希强行匹配。停止后的原始副本哈希与[父清单](assets/2026-10-29-town-position-followup/closed-input-manifest.json)一致；两个方向流无帧/cipher/CRC错误、无尾残留。

| 输入 | 最后上报坐标 | 对端完整相同广播 | 窗口8秒后，本人−对端的持续差 |
|---|---|---|---|
| A地面左键 | 无新上报；实际没有位移 | 无新增位置通知 | X +41.03，Y 0 |
| A向左300ms | 973,372 | 唯一匹配，日志差4ms | X −25.42，Y 0 |
| A单目标寻路 | 1083,372 | 唯一匹配，日志差5ms | X +0.29，Y 0 |
| B地面左键 | 无新上报；实际没有位移 | 无新增位置通知 | X −0.40，Y −6.60 |
| B向右300ms | 1191,372 | 唯一匹配，日志差5ms | X −0.62，Y −6.60 |
| B单目标寻路 | 1301,372 | 唯一匹配，日志差3ms | X +0.72，Y −6.60 |

四条有效消息的方向均5、movement值均100。日志差是socket记录时间差，不能当作真实网络延迟或原生应用耗时。两个地面点击窗口未造成走动，不算地面寻路通过，也不归因于服务端丢包。[严格匹配结果](assets/2026-10-29-town-position-followup/offline/six-window-position-match-closed-final.json)、[接收前后原生样本](assets/2026-10-29-town-position-followup/offline/receipt-neighbor-samples.json)、[解析器及边界自测](assets/2026-10-29-town-position-followup/offline/position-contract-and-results.md)。

## 原版合同与本轮推断

原版EXE SHA256为 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5`。两次独立原字节推导一致：NOTI22处理入口 `41E878` 经user+10C、当前map成员校验后，调用Town虚表+170的 `478F00`。方向5解码为两轴4/4，再进入 `4798A0`。

`4799D6/4799DA` 在两轴均停止时，分别比较收到整数坐标与当前整数坐标。`4799F4/479A1A` 的阈值为严格小于50；小差轴用当前坐标替换新目标，达到或超过50的轴保留收到目标，最后经 `478F70` 写目的地。movement100设置速度系数约1，不代表强制对齐。

本轮A收到973时，对端目标保持998；收到1083时目标更新为1083。B横向大差目标1125→1191→1301被接受，纵向目标一直保留379，与收到372相差7。六轮12轴的目的地均符合上述条件模型。[独立合同与原始字节清单](assets/2026-10-29-town-position-followup/native-contract/contract.txt)、[模型对照](assets/2026-10-29-town-position-followup/native-contract/samples-and-semantic-model.json)、[另一份独立原PE导出](assets/2026-10-29-town-position-followup/offline/native-position-apply.asm)。

这些是原始合同、已收到的消息和邻近原生状态之间的匹配；没有执行事件hook证明每条消息实际经过哪一个分支，也未采样所有provider/碰撞状态。因此当前结论保持为“本轮小差符合原版停步容差”。模型产物中`own_peer_final_delta`字段实际使用“对端−本人”，与本报告和live原始表采用的“本人−对端”相反，见[追加字段说明](assets/2026-10-29-town-position-followup/semantic-model-field-clarification.json)；未修改原始封印字节。原生发送函数另有整数坐标缓存抑制，但本轮没有实际调用及缓存字段样本，不用它解释地面点击没有移动。

官方服务端ELF SHA256为 `8859C99D8290DC5FB58151444138CF2697B2BFFF12D47D6159596A03F20B5261`。完整 `DisPatcher_SetPos::process@081C98C8` 与 `GameWorld::move_position@086C5706` 表明：通过状态、ghost及区域可见性门控后，缓存并按UID、X、Y、方向、movement原值构造NOTI22；选定完整函数链未见10/s限流或修改坐标。[官方原始反汇编与哈希](assets/2026-10-29-town-position-followup/offline/official-position.json)。

此前本地默认10/s硬丢尾包的离线候选依然成立，但本六窗四个新位置消息都已送达，不能将该候选当成这里的原因。若后续实际捕获超过预算且末位置未纠正，再独立修复并回归；不为解决本轮小差直接删除限流或修改原版50阈值。

本组未修改业务源码；过滤工具六项分合包、截断拒绝、错误头不重同步、半帧/半行pending及非位置哨兵不泄露自测通过。这些是工具验证，不能称为产品同步修复测试。[独立证据复核](assets/2026-10-29-town-position-followup/review/u07-evidence-review.md)重新验证消息匹配、读数、原始字节及上述结论边界。测量阶段与地图验收共用的自有进程已全部停止，原始存档和配置恢复见[地图收尾](2026-10-29-map-right-feedback-verification.md)。
