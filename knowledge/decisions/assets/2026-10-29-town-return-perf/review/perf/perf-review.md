# Debug 5E5：只读开销审查与对照方案

2026-10-29，active_drop_rules。**不能据目前证据确认“debug数据接口导致卡顿”。** 当前能确认的是执行路径和重复操作；缺少与帧时间同钟关联的耗时证据。本次没有改产品源码、构建/部署、控制进程或执行输入。UI/SourceGate由根及另一审查者负责。

审查 root `fa1bd68eb441979d218b5526510f862fe62b13ca`；所讨论 Native Debug DLL 为已验收5E5：
`5E5FF3F218E738C9C4AAEFACBB496B800A44BD5D44BA88E45BB00AFFB401836B`。这是已有冻结来源绑定，不是本次重新检查运行中模块。

## 可测候选

| 路径、线程 | 已核源码事实 | 应测量什么/解释边界 |
| --- | --- | --- |
| SDK CaptureFrame，游戏/渲染回调 | `client_state.cpp:580`，ExportState启用时200ms节流；固定Snapshot，主捕获含Town或Dungeon分支。Dungeon最多4玩家/256怪/216技能，安全字段可多次读。发布TrySRW失败则跳样 | 完整CaptureFrame入/出耗时及该帧间隔，按scene/对象数分组。现有capture_duration_us只覆盖Capture内部一部分 |
| 活动route Fresh，游戏线程 | `client_town_route_runtime.cpp:234–274`，**先处理inbox，再在!Active/NotBefore处早return**，之后才Fresh/Controller.Tick；idle无请求不会每帧Fresh。active时Fresh调用完整Capture，没有200ms节流；发起UI/新命令另可Fresh。route使用独立History | 分别统计idle、active、接受新goal、过图及每腿重规划。这里的capture没有发布到SDK，所以现有SDK耗时样本不覆盖它。不能把idle路径写成持续全量重复采样 |
| SDK JSON导出，同一worker | `client_state.cpp:595`，新sequence才写；每次ToJson、Create/Write/FlushFileBuffers/Close、MoveFileEx REPLACE+WRITE_THROUGH，成功才更新LastExported。发布/复制锁在I/O前释放 | 序列化、write、flush、rename各耗时及失败重试、export延迟。正常最多5次/s/client；worker忙时可能少于5 |
| Party发布，游戏线程 | `client_party_runtime.cpp:207–244`，启用时每render CopyLatest，Town有效时还查签名/上下文/邀请。**非Town也继续++PublishedSerial和Flush发布**，不要求内容发生语义变化 | OnFrame耗时/调用数/发布数；Town与Dungeon都量。发布是Try锁，不能把它直接描述成磁盘阻塞 |
| Party导出，同一worker | `:246` 50ms轮询门，新serial就Export；`:160–190` state总是重写，Game.haveResult一旦存在且未初始化清除，既有result也跟着每轮重写。每个文件都FlushFileBuffers+WRITE_THROUGH rename | state/result各文件次数、重复内容比例及flush/rename尾延迟。理论≤20轮/s/client，33ms普通worker轮转常会约66ms一轮；不能把理论上限当实测频率 |
| Legacy navigation/route结果，同一worker | legacy导航仅新结果serial才导出，但也Flush+WRITE_THROUGH。`client_town_route_runtime.cpp:167` 新route结果Save只有Write/Close/REPLACE，**没有FlushFileBuffers/WRITE_THROUGH**；active进度约200ms发布，动作/终态可额外；debug API每结果另写per-ID与latest两份 | 分开计数，不把所有route结果都归为fsync。UI目标通常一份latest；API可两份 |
| Worker轮转/overlay共享锁 | `debug_overlay.cpp:46–93` worker按state→nav/route→party→窗口/原有server debug读取顺序。正常MsgWait33ms；消息到达可提前醒。游戏端Content发布用blocking SRW，worker只在复制Content短暂持锁，I/O不在这把锁内；Snapshot发布为Try锁 | 测worker loop与各阶段，主线程Content锁等待另计。慢flush可推迟下一轮SDK/命令消费，但尚不能由此推出帧卡顿 |
| 两个minimap hook，游戏调用线程 | `client_navigation_runtime.cpp:196–228` 两者始终各执行原调用一次。旧左poll原生后检查Town/map/release，失败早退。右hook读eventBefore，原生8F6C30后**只有token==0x10才AfterRightHit**；通常非release不会扫描投影。有效点击才扫描最多256区域/256marker并开始规划 | 原函数耗时与新增前/后处理分开计；普通帧/地图开闭/左释放/右释放分组。不能把原生hit遍历耗时整体算作新增hook成本；没有每帧磁盘写盘在这两个hook内 |
| IJL DumpNetwork，原socket调用线程 | `net_dump.cpp:356–445` 原send/recv返回后，同步getsockname/getpeername、时间/文本/hex编码、LogLock串行WriteFile。thread_local缓冲复用但新高水位会分配。文件普通FILE_ATTRIBUTE_NORMAL，**没有FlushFileBuffers/WRITE_THROUGH** | 按TID测format/hex、等锁、WriteFile、bytes/calls/s；先证明该TID是否渲染线程。普通WriteFile仍可能阻塞，但不能假定每包都强制刷盘 |
| 网络转储到达cap | `net_dump.cpp:205–310` size-cap检查在hex编码之后；达到总量上限会停写而不是停所有格式化成本。PayloadBytes=0含完整payload | 即使log已不增长，仍量CPU；不可把“文件不长了”等价于开销为零 |

若两个客户端都持续导出，SDK+party无result的理论文件更新上限约50/s；两边都有保留result时约90/s。每次更新都有flush及WRITE_THROUGH rename调用。此为源码调用上限估算，**不是实测磁盘IOPS、硬件flush次数或耗时**；worker变慢、帧率/调度/失败会改变频率。

原图SHA检查只在ExportLatest首次gate执行，不是每200ms重算；PVF约65.95MB的LoadCatalog也是worker首次尝试一次。应把这些冷启动I/O与已暖机的偶发卡顿分开，不能写成持续扫描原档。

## 已有SDK耗时记录的范围

只读8个明确现有JSON（六个X UI验收正例、Y same-room-entry与u07观察），按PID/start/sequence/captured_tick去重。来源哈希见`capture-summary.json`；没有扫描大trace或查询live。

| 现有记录 | n | median us | p95 us | max us |
| --- | ---: | ---: | ---: | ---: |
| Town(scene1) | 31 | 4 | 14 | 15 |
| Dungeon(scene3) | 4 | 33.5 | 37 | 37 |
| 合计 | 35 | 5 | 36 | 37 |

这些记录valid，skipped_publications最大0。但这是少量、非均匀、经export/poll选中的样本，不能当连续性能基准或排除稀有长尾。

计时在Capture中清零Snapshot、获取filetime/tick后才开始，在finish更新History前停止；还漏掉调用者Snapshot初始化、发布/复制、route Fresh、party、hook、几何规划、worker JSON/I/O、netdump。尤其**小capture_duration_us不能证明整个debug模块每帧只有几微秒**。captured_filetime→exported_filetime只到序列化前取的导出时间，不能充当Flush/rename完成时延；要取文件提交完成的QPC或外部I/O事件。

## 最小对照顺序（供root执行，本审查未执行）

先保留同5E5/IJL、相同分辨率/前后台/帧率限制、相同双端人数/区域/观察器频率；每格记录PID/start和设置哈希。暖机至少完成原图gate/PVF首次加载，再做同场景60s×3，ABBA顺序；冷启动/过图另留独立区段。双端同时运行与单端也要分开，别让另一客户端抢前台/输入成为混杂因素。

1. 当前配置，N关闭、无active route，原地idle；再N打开但不点击。原生帧节奏、SDK和I/O开销先同钟记录。
2. 仅DumpNetwork=0对照，其他不变；开关通常在初始化读取，由root决定可重启的独立run。比较帧长尾与socket dump CPU/锁等待；网络包量也要匹配。
3. 仅EnablePartyCommands=0对照，保留ExportState/nav。观察party文件写入及卡顿是否一起变化；这会同时移除party游戏线程读取与worker I/O，所以仍不能单独定因flush。
4. 仅EnableTownNavigation=0做idle/N打开对照，估计nav每帧/两个hook组合成本。它不适合拿“route根本不运行”与“route自动行走”直接比较；active Fresh/规划需额外细分计时。
5. ExportState=0只作整个SDK/nav/party组合基线，**不是单独关JSON导出**：client_state.Initialize会在设置Started和布局gate前return，后续nav/party reader.started=0也不启用。若此格变快，只能说明组合差异相关，不能归因数据接口写盘。

若上述关联有信号，再由root授权最小诊断构建/外部追踪分解；本次不改开关语义、不取消安全/身份freshness、不过早降低route采样频率以掩盖手动中断证据。

## 低侵入计时建议

优先用现有外部CPU采样/File I/O/磁盘延迟追踪，按PID/TID/文件后缀做时间关联；若无可信的原生Present帧时间来源，再加短寿命内存计时。无工具/数据时不要从SDK tick间隔推算FPS：SDK本来200ms采样，worker也会延迟。

最小新增仪表建议：QPC同钟、固定容量内存ring/直方图，无逐帧文件、printf或Flush。记录：

- host帧/Present的进入间隔与CPU耗时；DebugRender自身进入/退出另记。若只钩DebugRender，名称应是callback cadence，不冒充实际屏幕present时间。
- CaptureFrame实际执行 vs节流返回；route Fresh/Controller.Tick与规划、party OnFrame；两hook新增部分与原native调用各自分段。
- worker的serialize/write/flush/rename、文件种类、bytes、成功/失败、队列深度；锁等候独立计。netdump记录格式化/hex/等待/WriteFile与调用TID。
- 每轮结束一次性写Z，固定ring满时计drop，不阻塞游戏；两组都使用同一仪表，先校验仪表自身开销。

以实测常态周期T为基准报告p50/p95/p99/max、>2T、>50ms、>100ms帧数及连续长帧段；仅均值/FPS不足。至少要求“卡顿时间与候选阶段重叠”和“单变量对照可重复改善”才能提高因果置信度。单次关闭某项后主观变顺不能闭合。

当前最值得优先隔离的是party重复持久化、SDK持久化及同步netdump；active专属Fresh/规划是另一条独立候选。没有新的数据前，不改成‘已确认debug接口导致’。

