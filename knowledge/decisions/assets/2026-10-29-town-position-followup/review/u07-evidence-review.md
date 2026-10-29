# U07 限定证据审查

日期：2026-10-29。独立审查：map_feedback_review。

结论：**六窗证据支持“四条 direction/DLRT 末位置业务字段完整一致地被对端收到，当前持续小差值与原版停止方向的逐轴50px容差规则相符”的限定判断。没有形成位置同步修复通过或根因最终闭合。** 本轮不支持把10/s候选认作这四条消息的丢失根因；大差值、其他窗口与怪物位置同步仍开放。产品未因本组证据而改动。

本审查只读现有JSON、过滤程序、导出反汇编及原始字节文件，执行了不写文件的离线重算脚本。未操作游戏/GUI、查询进程、输入、部署、写产品、读取原始netdump或存档，也未重新运行原始抓包过滤器。唯一写入是本报告。未扩展调查范围。

## 1. 身份、采样与闭合数据边界

- N/live的A/B-ground、direction、destination共6窗，分别有137对own/peer原生样本，实际末点10.96323–10.96940s，约11秒。每窗均无记录错误，t单调；每对的QPC起止满足own开始≤own结束≤peer开始≤peer结束。
- 六窗before/after身份均为A PID8820/UID1/start134377441565015453、B PID8568/UID2/start134377441572443797，和记录的identity及原phase部署一致。两个进程各自的actor/map指针用于本进程身份绑定，不能跨进程直接比较指针数值。
- **SDK before/after snapshot在这里仅作身份/场景绑定，不作为SDK坐标与VM_READ坐标的一致性验证。** 表中的差值由每对原生浮点位置独立复算，全部满足dx=own.x−peer.x、dy=own.y−peer.y。每对是有界顺序读，不是同一时刻的原子快照，也不是接收分支执行事件。
- offline/A-closed-final.json、B-closed-final.json引用停止后的closed-right-pulse-v2副本，记录长度905425/911087字节。其input_captured_sha256分别与闭合manifest中的E6BA7711…、7CACC21A…完全一致。闭合deployment状态是stopped，原phase/build-manifest绑定仍为right-pulse-v2/00F9C92D…。
- 本审查比较了过滤输出与闭合manifest的哈希声明，未打开原始A/B-netdump内容、重解密原始流或再次独立计算原netdump哈希。不会把这一检查描述为第二次原始流采集/解码。

## 2. 过滤与匹配独立复算

阅读Program.cs确认只落客户端CMD37与服务端NOTI22的9字节位置体；同流所有客户端帧仍依序推进cipher，以免跳过其他消息使rolling state失配。按端点及TX/RX组流，支持分/合包，保留首末贡献记录时间；错误头/截断失败后不扫描重同步。sequence与UID不能混用：CMD37前2字节是sequence，NOTI22前2字节是UID，严格匹配的是后7字节x:i16/y:i16/direction:u8/movement:u16，并另用发送者UID筛选对端通知。

独立检查最终输出A26条/B31条均仅含这两个协议；9字节体自身SHA256及解析字段一致。两端errors、selected_decode_failures、other_decode_failures、末半行及所有7001流pending均为0。端点重连代次重置没有实现，不能推广到同四元组复用的其他录制；本组输出/说明按当前唯一端点范围使用。

读取filter-selftest-results与test_filter.py，六项合成结果全部passed，覆盖分/合包、贡献首末记录、截断、错误头拒绝且不重同步、帧pending和末半行pending；代码含非位置敏感哨兵不泄出断言。这些是过滤器自测，不是产品位置同步通过测试。本审查未重新运行生成器以免改写旧证据。

独立从最终过滤JSON按各live窗的utc_start与末sample.t重选消息，按+08:00解释socket记录时间；未使用原join脚本写回结果。每条只按同UID和完整7字节一致寻找候选，不按最近时间强配。与six-window-position-match-closed-final.json逐窗计数、trial SHA及唯一性一致：

| 窗口 | CMD37 / 对端NOTI22 | 新末消息(seq, x/y, direction, value) | 唯一匹配socket记录差 | 8秒至末点的own−peer差(dx,dy) |
|---|---:|---|---:|---|
| A-ground | 0/0 | 无 | 不适用 | +41.031860, 0 |
| A-direction | 1/1 | 41, 973/372, 5, 100 | +4ms | −25.422363, 0 |
| A-destination (DLRT) | 1/1 | 42, 1083/372, 5, 100 | +5ms | +0.291260, 0 |
| B-ground | 0/0 | 无 | 不适用 | −0.396484, −6.596405 |
| B-direction | 1/1 | 46, 1191/372, 5, 100 | +5ms | −0.619385, −6.596405 |
| B-destination (DLRT) | 1/1 | 47, 1301/372, 5, 100 | +3ms | +0.718628, −6.596405 |

四条均只有一个完整7字节一致的对端候选。3–5ms是两端socket日志末贡献时间差，不能写成物理网络时延、原生应用延迟或服务器处理耗时。它支持四条所测位置消息已到达，不能证明所有消息/所有时间均无丢失。

两个ground窗口自身首末位移都是(0,0)，而且无新CMD37。**这两轮没有形成有效移动试验，不能算地面点击寻路通过，也不能以无NOTI22推断服务器丢包。** action返回成功只说明输入计划完成；没有移动结果，不能把它替代业务验收。

符号口径：本表使用live.dx/dy及直接复算的own−peer。native-contract/samples-and-semantic-model.json中axes.own_peer_final_delta实际采用反向peer−own，不应和此表混用；绝对值/阈值判断不受该命名口径差异影响。

## 3. 原版接收规则：静态解释成立，实际分支未采

独立检查native-contract/manifest中21段raw range的长度与SHA256、全部48个产物哈希：均匹配。offline/native-position-apply.asm内5组原始指令字节按各记录地址边界重建后SHA全部匹配；sender完整函数rawhash匹配；官方5个完整导出函数也由asm行内原始字节重建并通过各自rawhash。读取town-vtable.bin，+84/+88/+170/+174/+178/+17C分别为407460/407470/478F00/4798A0/478F70/479A40，与JSON槽位一致。

原PE chain支持以下静态关系：NOTI22分支41E878读取UID及位置方向等字段，排除自身并核当前map成员后，41E9B0调用actor虚表+170=478F00；其direction−1按3解码，direction5得到x/y方向4/4，再经+174调用4798A0。478F00还可能被actor+3C8对象的+1A0标志拒绝；本轮没有采到实际入口/拒绝/通过分支。

4798A0在4799D6/4799DA同时检查两轴方向为4，才执行逐轴停止容差。4799F4/479A1A各CMP绝对整数差0x32，JGE跳过替换，故条件是**严格小于50**，达到50不属于保留范围。小差轴由479A03/479A29取得当前整数getter作为目标轴，再在479A31经+178=478F70写目的地。准确说是“以当前坐标轴替换收到的目标轴”，不是任意情况下保留旧destination内存值，也不是将收到位置无条件覆盖当前坐标。

本轮8秒至末点的逐轴绝对差均小于50；A方向的peer目标998与收到973差25、B纵轴peer目标379与收到372差7，和规则相符。邻近接收采样还显示A横向目标998→1083、B横向1125→1191→1301，而B纵向379保留，支持“较大横差更新、小纵差保留”的相关观察。

**最强措辞仍是：当前小差异符合原版逐轴50px死区的静态推断。** 未记录41E9B0→478F00→4798A0的实际调用参数、条件判断或目标写入事件；不能说实际分支已经证明，亦不能用窗口初始/结束坐标代替收包瞬间的原生getter值。其他运动预测/舍入行为也不能仅从末点小差值中排除。

## 4. 官服与发送端契约的限定含义

official-position.asm导出链显示read读取2/2/1/2业务字段，check_error要求state3，process调用GameWorld::move_position；后者保存位置，按UID/x/y/direction/movement构造NOTI22并经区域/可见性门控发送。本组已导出的处理链没有可见10/s limiter或尾包flush机制。该静态片段不是官服所有被调函数的全域证明，但与本轮四条完整到达相容。

原客户端sender479A40在构造CMD37前比较整数X/Y与+254/+258缓存，两者相同时直接返回，否则构造/发送并刷新缓存。这证明原版存在整数位置不变时不发新包的机制；本轮未读缓存、未采sender实际调用，因此不能将ground无包直接归因于该缓存。

当前四条发送/广播业务字段一致，故此前10/s候选不能作为本轮这四条“小差”根因。没有证据支持修改50阈值、强写每包坐标或改产品来消除此原版容差。此结论不关闭另有较大偏差、不同map/场景、未知接收分支或怪物位置同步问题。

## 5. 可审计输入哈希

以下为本次实际读取核心文件的SHA256。旧证据均保留；没有重写输入JSON/asm/manifest。

| 文件（相对N） | SHA256 |
|---|---|
| live/A-ground.json | 60ede7d7b01c5049459326a5948539c8b62ff03c5bd41f56b0a4f2cb146287f5 |
| live/A-direction.json | de2b7709621730e95b3035602f8a9803b553926b78b2d461997dd7a9a0ecf291 |
| live/A-destination.json | 054b7d20128b7378aa59b250202b2e96b92b2f997699a5e2bb3ae173650bd744 |
| live/B-ground.json | ab8e0c3954afd38b4dfcf5a539b337d65015cf22929cfb9cd8c32d595a451dc3 |
| live/B-direction.json | d3144801a5c0ecf4e34aaa59e95224b45e59755aac17092e834f55c35acce080 |
| live/B-destination.json | d592caa6b4e1bf7afab32aec251cedb3c637df6d9b367c375955d81d109f42f3 |
| offline/A-closed-final.json | 683f3dae764ff666a3b6665abb20feb5f1c2e16088329111054d16542b9ecb3f |
| offline/B-closed-final.json | b19e58211fc59a54f3941510c1d27b0b801381d200dc4d98c4578ed9a5cb740f |
| offline/six-window-position-match-closed-final.json | e08627494592b0b1602a1f142b2baeb172ca3cbae50c84d09f7a6cd960c2d90d |
| offline/filter-selftest-results.json | a947be9709b44a525da331e96bda6e7c3963afc5ec084b2cb534526b069c6f35 |
| offline/Program.cs | f3653cc7822e795513ff015da6b147f7986b72f1a06f5705e22d18a0bc845d6c |
| offline/test_filter.py | 0b5d203bf23529ad4f2dec05ac2bf04a6c1e1335f6738142b69bc0a8c6b26503 |
| offline/official-position.asm | 0d6ffd4b8b7212969b5c7234f187d37f482bddc49728738133b88384f6b7cf92 |
| offline/official-position.json | a010b6b2e5140caf8d4cb4faf37acdad572efc72e56b75d7a9f019947b714707 |
| offline/native-position-apply.asm | ea05e594e686473495247d476dce222fa2edc9c47bb9a378a8a50e84012c4016 |
| offline/native-position-apply.json | ea5fb97b759f4fe3fc59e3a6aae600e87bc71224a25ec9521de86d7b1e1752ba |
| offline/native-position-receiver.asm | d57ab99491a559a69362ba9714941bf3c78eef0d73549c49af76f2eac497525b |
| offline/native-position-vtable.json | b9b6793dedc7e66587934690c1dd71fc6ad18b9ca8d878ed091284d88b452bc9 |
| offline/native-position-sender.asm | 47e5b75bda7a47bedf8c9e41ab8a4169dbee0ac5e800cd5db7f03839d18822d1 |
| offline/native-position-sender.json | 2978d8dbd4a889970780b455ffc5aa5c53085aed3253f3ff4b74518c699727b3 |
| native-contract/manifest.json | 3bfa451cc3977bf8f708bab29538e434c4a7b8b0922a4e1231856803958d0d5d |
| native-contract/abi-and-constants.json | 7d7be4161e807d27663e7017849b72d37f8115203c0cfa8382770c9bbf019b05 |
| native-contract/samples-and-semantic-model.json | 55c8d8a4ea7b13bfb463cc6d77e5c67f1aaae6b7808c5785d549fe5fc8b21e79 |

闭合manifest SHA256：44ce14ee55aa8e5cead2637f3d780b9f7640486efcc0ba2827fe36f4c5235696。闭合deployment SHA256：8df92cdb4a14af50972b698a7250a0d7cdbe41e16b89b8abd22273dfbfd2a4ad。


追加说明：已读取semantic-model-field-clarification.json，其明确模型字段采用peer−own，与本报告的符号口径备注一致；该文件SHA256为af740646494096c3c7b242ea6c92ff3581762f4a14c1da0663111596690e4451。原模型不改写。地图P冻结后本审查未向P写入任何文件；本次输出仅在N/review。

