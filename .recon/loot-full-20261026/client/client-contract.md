# ACT1 1.0.1.9 拾取接口重核（2026-10-26）

本报告只分析文件；未启动客户端、未注入、未构建、未修改产品代码。IDA 由父任务统一运行。本目录的 Python 仅进行 PE 读取、反汇编、历史日志解析和隔离 CPU 仿真。

证据标记：**静态**=原始 EXE 指令/表；**离线仿真**=真实保护代码在合成内存中执行，所有原生/API/网络调用被拦截；**历史**=已有归档日志/人工台账；**未知**=尚未闭合。

## 样本与复核入口

- `G:/Game/DNF_ACT1/DF/DNF.exe`，9,846,272 B，PE 版本 `1.0.1.9`。
- SHA256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`。
- `raw_probe.txt`：哈希、段、相关字符串、CMD46/NOTI39/40/86 和 network helper 原始指令。
- `rtti_probe.txt`：原生确认窗完整 RTTI/vtable 反查。
- 父任务 `../client_full.txt`、`../client_more.txt`：IDA 补导出；旧 `../../loot-routing-20261026/client_probe.txt` 末尾为 NOTI39 前半 ASM。
- `offline_window_emu.py`、`offline_window_click{0,1,2,3}.txt/.trace.txt`：确认窗保护分支的可重跑离线夹具。
- 复核命令：`python .recon/loot-full-20261026/client/contract_checks.py`。本轮结果 `contract_checks.txt` 为14夹具通过：8个按钮/随机值场景、6个真实descriptor→rarity分支场景。后6个进入前把local[-8]故意填CCCCCCCC，验证它确实被descriptor稀有度覆盖，避免预填local造成循环论证。
- `history_summary.txt`：全仓归档 `real-client-run-*-server.log` 的 CMD46，625 条、20 个日志/会话组；序号只表示各归档中的顺序，不能假定归档前没有请求。

## 需要正式作废的旧结论

1. **“ACT1 没有任何原生 routing 确认窗”作废。** EXE 存在 `CNRDItemRoutingConfirmWindow`，且两个回答分支已走到真实 CMD46 发送；它是本次拾取的二元许可，不等于新版全队 `NOTI281/282 + CMD301` 投票。
2. **“NOTI39 只有本地得主才删除地面物”作废。** 查到 actor 后先无条件 `v58(false)`；本地得主门只控制 descriptor 移交和入包。
3. **“CMD46 +4 恒0/+5只能0,1,2”只适用于早期有限样本，不能当协议约束。** run49 已有 `+4=1,+5=4`。旧台账也明确记录 run49 第一趟出现掷点弹窗。
4. `sub_417C40` 不是地图移除函数：它返回 actor+0x7A0 并将此 descriptor 置空，属于所有权移交。仅统计其两个直接调用点不能证明只有两个地面删除入口。

## CMD46 请求字段

以下偏移相对服务端现有日志的 `Body`（即十字节传输头之后）。network finalizer `8B24C0` 在帧 +10 写入 u16 sequence；这不是拾取业务字段。

| Body偏移 | 宽度 | 当前结论 | 证据/边界 |
|---|---:|---|---|
| +0 | u16 LE | transport sequence | `8B24C0` 写帧+10、随后自增；不能继续叫单字节 records |
| +2 | u16 LE | groundId | 历史成功/拒绝配对；确认窗从 this+0x48 取值写2字节 |
| +4 | u8 | 本次 routing 许可/选择 | 确认窗第1按钮写0，第3按钮写1；第2按钮不发请求 |
| +5 | u8 | RequestedRarity（客户端物品稀有度快照） | 普通路径写descriptor的rarity；确认路径固定4。服务端以模板稀有度为准 |
| +6 | u16 LE | 随机值混入标志位的 guard word | 确认窗见下；不是坐标、itemId、groundId或routing选项 |

确认窗发送的**业务载荷为6字节**：`u16 groundId, u8 choice, u8 4, u16 guard`；连同公共 sequence，日志 Body 为8字节。不可按官服较新九字段 reader 强行读16字节。

### 原生确认窗回答链（已闭合）

RTTI `.?AVCNRDItemRoutingConfirmWindow@@` 字符串 B53224，TypeDescriptor B5321C，COL AB644C，vtable A7F7F4；ctor `7C4FC0`。工厂 `7BDC60` 的 type98/0x62 创建此窗；ctor 内部 id113/0x71。`799EF0(type98, groundActor, ...)` 调虚表+0xC=`7EAA90`，保存 actor 的 groundId 至 this+0x48，复制 descriptor 供展示。显示虚函数为7EA740；proc虚函数7EA940跳转保护入口8C16F8。

三按钮对应 this+0x30/+0x34/+0x38；初始化分别放在 `(104,91)/(164,91)/(12,91)`。离线夹具以按钮的 native control id 驱动 `B55184`，未杜撰文字标签：

| 按钮 | 原生结果 |
|---|---|
| +0x30（第1） | 关窗，CMD46 choice=0 |
| +0x34（第2） | 只关窗，无Begin/Write/Send |
| +0x38（第3） | 关窗，CMD46 choice=1 |
| 没有按钮点击 | 无发送 |

两个发送回答都经：

```text
D4495F ecx=[BF0A9C]
D4EDF8 [esp+1C]=46 → D4EE00 esp+=1C
D4EE0A call D42EDE → D42EDE esp+=4 → D42EE2 BeginCommand(46)
D3C858 ax=[esi+48] → D53B91 local=-C=eax → D53BA2 WriteBuffer(local,2)
D5258C local=-4=bl → D4F0B2 WriteBuffer(local,1)  // button choice 0/1
D42514 local=-4=4 → D4251D WriteBuffer(local,1)
D42527 random@900C50
D45B58 SetBit@776D50(&guard,true,1)
D4043D SetBit@776D50(&guard,true,4)
D3E154 SetBit@776D50(&guard,false,5)
D5239A WriteBuffer(&guard,2)
D460E2 Send@8B24C0
```

`900C50` 读取/推进客户端随机状态后返回 `(state >> 16) & 0x7FFF`（见 `guard_candidates.txt`）。确认窗因此发送 `(random15 | 0x12) & ~0x20`。仿真将 random 返回值设为0997仅用来跟踪传递，未把0997当所有真实请求的值；输出 guard `9709` 逐字节到达最后的 WriteBuffer。真实随机状态推进子函数900B70/44CE10尚未完整业务命名。

### 稀有度字段与打开分支（数据流已闭合，上游动作门控仍未知）

装备loader `73D61A` 将 `[rarity]` 整数写 parsedEquipment+0x14；复制函数449860将其写descriptor+0x68。Stackable loader `73F107` 写 parsedStackable+0x24；458B40将其同样写descriptor+0x68。两类descriptor的虚表+0x18均为4619A0，只返回this+8，所以metadata+0x60确实是rarity。证据父任务 `../client_loaders.txt`、`../client_rarity_copy.txt`、`../client_stackable_copy.txt:163`。

本地拾取helper的下列实际栈链也已离线核对，未将线性邻近当可达证据：

```text
D40D01 call417C60(actor)  // descriptor=actor+7A0
保护链调descriptor虚+18 (=4619A0)
通过ret重写准确回到8879E5
8879E9 eax=[metadata+60] → 8879FA local[-8]=eax
887A04 cmp local[-8],4 → D46720 jne887899
相等：D50FC4 callD582CC → 实际重写流至D46141→887869→887888
      openUI(type98, groundActor,0,0)，此时无CMD46
不等：普通CMD46(choice0, requestedRarity=local[-8])
```

`contract_checks.txt` 对rarity0..5逐个使用合成descriptor：仅4打开type98，其它0/1/2/3/5均直接发CMD46，RequestedRarity逐字节保留。`offline_window_click0_groundPath_rarity*.txt/.trace.txt` 从417C60调用起给完整实际数据与栈路径；rarity4经74条真实指令到打开。`predicate_results.txt` 是更早从metadata读取点开始的单段交叉复核。

**不能扩大为整个客户端的全局规则：** 这个helper上游何时被角色动作调用、是否另有party人数/场景/当前actor门控，仍未闭合到4A0920或其它原始方法入口。这里只证明已经进入此helper的路径严格`rarity==4`开窗；不能写成`rarity>3`、全队投票或服务器通知启动。887780自身的directcall属于NOTI137 `REQ_HUMAN_CERTIFY`，保护代码跨原始地址混排，不能把887420..887B00整个地址区间视为拾取函数。

### 普通发送器与guard

普通Begin46在D4FC74；actor来自`[ebp+8]`，虚+0x38取groundId→D3C6F1 WriteShort；D4A2CE WriteByte固定0；D4A2D5取local[-8]低byte→D3F0BD WriteByte RequestedRarity；D4B561取900C50随机数。

guard依次由776D50设置bit1=`45D760(localActor,groundActor)`、bit4=true、bit5=false，然后887A19 WriteShort，7EA9D8 Send。即普通guard为 `(random15 & ~0x22) | 0x10 | (predicate ? 0x02 : 0)`；确认窗把predicate固定true。

45D760需要localActor的map可用，再调4C50F0。后者对ground位置的虚+0x84/+0x88及全局B6F654/B6F658范围，与localActor的边界虚方法比较，是空间范围谓词，**不是同map成员查找**。各边界函数精确业务名仍未命名；服务端可保持guard为raw字段，不应误读为路由选择。

### 取消与本地等待状态

第2按钮只调虚+0x28=7BDC50→7A1670，把窗口指针加入interface manager+0xEF4的关闭队列。此分支不发请求、不调用40E290，也不直接改actor+0x7A5。

本窗发送后的路径才在8C1729调用40EE40记录等待类型2，并在8C1771将actor+0x7A5设1；取消跳过这段，因此没有本窗刚创建的pending要等服务端回包清理。更上游进入helper前是否设置其它状态尚未知，不能从关闭队列逻辑断言所有上游状态均已覆盖。窗口onClose虚+0x10=7EA580只销毁预览actor并清this+0x44。

## CMD46 ok/error 接收

入口414090。通用分发先读 resultFlag，再进入该case。

| result/body | 原生行为 |
|---|---|
| `[1]` 或任意 resultFlag非0 | 414095直接跳default；不读更多字段、不移除/入包、不弹出pending |
| `[0,4]` | 显示 string id319 的消息，随后共同恢复 |
| `[0,22]`（0x16） | 与error4完全相同 |
| `[0,21]`（0x15） | 不增加本case的文本，直接共同恢复 |
| `[0,81]`（0x51） | string id1581 窗口后共同恢复 |
| `[0,99]`（0x63） | string id7535 提示后共同恢复 |
| 其它 `[0,error]` | 本case无专属文本，仍共同恢复 |

共同恢复 `414198..4141F9`：

1. `40E290([B5DCEC])`：其+0x20/+0x24向量非空时 end-=4，弹出最后一个pending记录。
2. 取当前场景map，遍历全部对象；凡虚表+0x2C判定kind0x121者，写 actor+0x7A5=0。
3. 返回通知循环。没有NOTI40重建，也没有自动重发请求；是解除本地请求中状态，使现存地面物可再次拾取。

因此 error4/22 在这个客户端具有完整释放行为；不能因为以前只用了error21就断言其它官服错误码会卡拾取。反过来，单独发ok不会释放pending，也不会确认入包，成功必须仍有NOTI39。

## NOTI39 原生成功结果

入口421EA7。先读 `u16 groundId, u16 pickerUid`。若 pickerUid==myUid，在421EF2调用40E290弹出pending；之后无条件查当前地图kind0x121/groundId、dynamic_cast为CNRDItemObject。查不到就跳过后续结果；查到则**421F58..421F68无条件v58(false)**，随后取descriptor。

### 普通物品分支：descriptor metadata+4 非0

固定12字节：

```text
u16 groundId, u16 pickerUid, u8 scores[4], u16 winnerUid, u16 destinationSlot
```

- 4222AB循环读四个score；4222CA/4222D9读winner/slot。
- 非零score且对应party slot有成员模型，拼接 ` %s[%d]`；完整四槽结果在42243D进入文本显示。并调用人物虚表+0x5A0，参数为该成员uid是否等于winnerUid。
- 42244F之后按winnerUid查人物并展示获得效果。
- 4224AC..4224BF只有 winnerUid==myUid 才进本地入包；417C40分离descriptor，再将其插入/合并至指定库存容器slot，最终处理物品类型特例。
- 无新版尾byte读取，不能把较新官服13字节形态直接混进该固定格式。

**全队同一份NOTI39会让各端的地面actor均停止激活，但只有winner入包。** 这同时解释为何广播可显示骰子和清理地面，且不能向不同队员伪造不同winner结果。

### 金币分支：descriptor metadata+4==0

前4字节相同，之后固定四槽，每槽 `u32 goldAmount, u8 shareCount, u32 shares[shareCount]`。无细分share时载荷24字节；一般长度为24+4*sum(shareCount)。这不是普通物品的score/winner布局。客户端只对本机对应成员真正修改金币并展示，但会消费其它槽位的完整变长字段。

## NOTI40 不是移除接口

入口422CC3，结束422E98；消费17字节：

| 偏移 | 类型 | 使用 |
|---|---|---|
| +0 | u16 | 本case读取后未使用；服务端称dropperUid需另据服务端命名 |
| +2/+4 | s16/s16 | 新actor位置x/y |
| +6 | u16 | 注册为kind0x121的地面网络id |
| +8 | u16 | 交给51AB60的物品模板索引 |
| +10 | u8 | 交给51AB60的附加属性（代码中可按既有服务端构包映射增强等级） |
| +11 | u32 | 交给51AB60的数量/属性 |
| +15 | u16 | 交给51AB60的附加short（既有构包映射耐久） |

它创建descriptor、new 0x838的CNRDItemObject、设置坐标，并经4C7AE0注册新对象。绝不能用NOTI40伪装拾取删除。

## NOTI86 原生与 IJL15 扩展

427104读subtype byte，只比较1/2，其他均跳42740F/default。**原生subtype5没有移除操作。** 新版官方7字节 `u8 5,u16 pickerUid,u32 itemIndex` 在ACT1属于原生无动作。

现工程 `ground_item_removal.cpp` 是独立兼容扩展：只认完整帧长15（六字节server头+九字节payload），在原解密reader BF29E0读取 `u8 5,u16 pickerUid,u32 itemId,u16 groundId`。按当前地图和kind0x121/groundId查actor，校验vtable/active/map后调用568540(false)。它不入包、不detach descriptor；itemId和pickerUid仅用于载荷/诊断，实际移除按groundId和map完成。七字节原生包、其它subtype都走原代码。

## 可兼容发送顺序与不能改的内容

1. 原生成功必须先有正确的地面对象（NOTI38/40），随后NOTI39完成反馈和winner入包；不存在对象时NOTI39会跳过结果。
2. 骰子：同一份12字节NOTI39先发所有需要看到结果的成员。原生已经停用地面actor；之后NOTI86九字节扩展为幂等清理，不能先发扩展让按帧清理抢先销毁actor/descriptor。
3. 直取若只给请求者NOTI39，其它客户端仍需九字节移除扩展。原生七字节NOTI86(5)不能替代。
4. 成功最后给请求者CMD46 `[1]`；不可用此ok取代NOTI39。失败可用真实`[0,error]`解除pending和+7A5状态，是否返回及服务端错误语义由官服process证据决定。
5. 不截断281/282/301到byte；不把本次CMD46选择改成全队确认协议；不把+4/+5合并u16后丢弃；不把随机guard当成固定常量或全包CRC。
6. 未验证UI文字和完整上游门控之前，不把按钮文字、整个客户端的rarity阈值或全队确认语义写成已闭合规则。RequestedRarity是客户端快照，不能替代服务端模板权威值。

## 剩余未知

- 角色动作入口到本次helper的全部上游门控，及金币/任务物/单人/组队等触发条件；actor→descriptor→rarity→打开/直接发送的数据链已经闭合。
- guard空间范围谓词中各边界方法的业务名、随机状态推进算法完整命名；随机源和bit1/4/5写链已经闭合。
- 标题string id1554和三按钮的实际中文文字；没有新真机画面验收。
- 625条历史样本是包体事实，不能仅用它证明当前安装版本和未记录的窗口按钮文字。
