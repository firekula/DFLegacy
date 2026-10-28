# 2026-10-28：ACT1 有界恢复覆盖与未验范围

本轮六组的源码、自动化、独立审查及批准范围内的原生验收均已完成，结论为 **PASS_AS_SCOPED**。最终30AD真实PvP击杀、频道往返、双端重入响应及第四份收据完整重启均闭合；正常运行目录已发布，Windows PowerShell5.1双端口启动通过。原存档、5项配置及83份备份哈希保持，测试进程全部退出。逐项交付证明见[最终索引](2026-10-28-overnight-delivery.md)。覆盖以实际有界证据计，不把未知命令数换算成实现率。

托管源码基线为 `5078da27a4807695f002ceea61333c43e1932ce4`，最终原生为ab1/30AD；C79实际RED另行保留。下表将历史原生、最终原生、源码批准分别绑定自己的构建身份，具体原图、包、状态和独立检查见组件记录与[本轮交付索引](2026-10-28-overnight-delivery.md)。

## 已实现范围与真实验收进度

| 有界组 | 已实现业务 | 真实原生证据与本快照边界 |
|---|---|---|
| 既有队伍聊天/金币/原生骰子、普通摆摊、基础师徒、时装期限、频道重入、实物赠送 | 各自既有完整基础流程 | 沿既有已验记录；最终统一构建的必要重叠回归另行绑定，不因旧审计仍写missing而重复实施 |
| ACT1诊断 `294fdf3` | CMD125有界记录、不回包/改业务；保留独立118历史报告 | 双原生正常频道/选角/进城回归通过；**没有自然触发125**，其解析由确定性测试覆盖，不能称实际错误上报已触发 |
| 师徒成长与徒弟EXP `695232c` | 每活动角色600秒在线边成长、名单进度、怪物/通关真实EXP分项 | 历史695构建7/7类行为及后续cacab重启PASS；最终5078源码变化审查、后续成长/名单保持及30AD重登交集已绑定。历史EXP案例保留原构建身份 |
| 个人双人PvP `b0ea0f0` + native `3a35188` | 专用决斗频道，两席mode0、建/入房、准备/地图/peer、死亡/超时、排名/原子奖励、再战/离房 | **组件PASS_AS_SCOPED**：历史cacab/743三局，加最终5078/30AD一局真实击杀、第四份双人原子收据、同进程普通/决斗往返；独立普通频道双端全窗口468次响应正常，第四收据完整服务端重启保持。仅两席mode0/地图3，默认关闭；8人/team/观战/公网等仍未验 |
| 时装赠送/本人属性修正 `ccad0cc` | CMD146→67 gift1→mail→avatar bag；7天/30天/永久、真实能力索引、九部位含skin；gift0共用属性验证 | **历史5078 managed +743 native上的Task4已闭合**：7天/30天/永久赠送与B领取、7天未领取重启、gift0能力4及月额度提示通过；30AD既有商城入口另做交集回归。30天未重启；120秒派生deadline已验，不计自然7/30天到期。最终交付/原环境核验见索引 |
| 永久封印蛋赠送 `feade88` / `cacab2e` | 当前15款fresh subtype1封印蛋、FF/term0、category3、提交UUID→领取UID→既有孵化 | **组件PASS_AS_SCOPED**：743/C79实际入口RED保留；ab1/30AD真实6600赠送、未领完整重启、B领取/孵化、再完整重启保持通过，六stage checker57/57、103TX CRC有效。固定15款中的105009原生闭环，非15款逐只实机；最终交付见索引 |
| 师父普通PvE金币 `5078da2` | 在线自然generated pickup/free card税基、独立随机批次、native cap裁剪、已授予信用、具名NOTI76及顺序修复 | **组件PASS_AS_SCOPED**：普通PvE来源、正常选角flush、自然金币timer、师父离线正额反例、两次完整重启余额/receipt保持与无补发已闭合；743/C79/30AD按各阶段原构建归属记录。最终交付见索引 |

上述既有完成组详见[队伍聊天与金币](2026-10-27-party-chat-gold-verification.md)、[原生骰子](2026-10-26-native-loot-dice.md)、[普通摆摊](2026-10-27-private-store-verification.md)、[基础师徒](2026-10-27-mentor-verification.md)、[时装期限](2026-10-27-avatar-periods-verification.md)、[频道重入](2026-10-27-channel-return-verification.md)、[实物赠送](2026-10-27-cera-gift-verification.md)。本轮证据见[诊断](2026-10-28-client-diagnostics-verification.md)、[师徒成长/经验](2026-10-28-mentor-growth-verification.md)、[双人PvP](2026-10-28-pvp-individual-verification.md)、[时装赠送](2026-10-28-avatar-gift-verification.md)、[封印蛋赠送](2026-10-28-creature-gift-verification.md)、[师父金币](2026-10-28-mentor-master-gold-verification.md)。组件页和已冻结附件中的PENDING/NOT_READY表示各自取证时点，由最终索引追加闭合；未验的具体场景仍保留未验。

已交付的5078 managed Server DLL为 `A0EE99650A49011B9527E06BB8E1C19E6A7E5D284CC51D8F9865597C9E133134`，Protocol DLL为 `5A55089D384EFA9E4ACEC5A211EE5C551668CDDB5C93ED46A698D50250E04A4E`；历史时装原生成功与宠物入口RED使用的native为 `7431347A19A5D8B0B963B474B9739288BA8755895B260DF04A51D80BFF65B3DC`。C79 `C79E139201E55DAE86AD6FD6ABE501A98759371269F937046E0C392B1830C912`实际行Gift仍RED；ab1价格字段修正后的最终native `30AD9A4A3CD565DE845F74AB3D6C6D60EED1F3429EC7D53929D912F158246584`完成宠物、PvP及频道交集验收。历史695/cacab/743/C79归属不改写；正常runtime与4处native部署、原配置/原档保持已单独核验。

公开正文以逻辑证据ID和相对附件链接定位资料。已封存proof、测试/构建日志及其清单保留原始字节，少量本机绝对路径仅作为历史来源定位，不是公开下载链接，也不包含认证值或完整存档；本次整理不改写这些已作哈希承诺的资产。公开声明的规则和实际字段由各组件页及有界附件说明，原始网流、完整存档和私有分析资料不随路径公开。

## 哪些原服业务可以参考，哪些必须适配

“可参考完整业务”只指该有界流程有实际官方函数体、状态变化和ACT1收发对应，**不等于整份后期协议可原样发ACT1**。

| 业务依据已闭合的窄范围 | ACT1/本地适配与明确限制 |
|---|---|
| 队伍聊天成员路由、普通金币商/余数、师徒基础关系/在线成长与EXP | ACT1通知字段独立核对；JSON原子快照、幂等receipt、会话incarnation和信用交付；保留float32小额截断与样本在线缓存传播，不填固定bonus |
| 普通摆摊陈列/购买/手续费/许可与地理检查 | ACT1缺后期人偶尾字段；原生商人入口byte、库存escrow和串行交付属于本地实现；91失败收摊恢复不包含在正常85–90已验范围 |
| 普通时装目录期限/分步价格、属性选择、gift发信起租/领取保持 | 首byte为ability，u16为期限；服务端绝对deadline/UUID/墓碑，不重新起租；上海完整年月和跨角色原子cash/mail/count为本地可靠性适配，不能冒称官服已有同一单事务 |
| 官方Creature type5独立实例先创建、邮件领取沿原身份 | 只开放fresh永久封印蛋；本地UUID在赠送提交时创建，wire UID在领取时避碰分配。catalog蛋名/level1/stomach100是ACT1状态对象适配，官方新蛋DB名为空；不代表任意成长/改名/限时宠物邮件完整 |
| 普通individual决斗状态机、死亡/超时/排名与数学 | 后期individual1映射ACT1 mode0，仅两席；本地watchdog、双边原子receipt/Unknown隔离、独立UDP context与原生重连兼容补丁；不伪造peer-ready，不承诺未实现备用relay |
| 师父金币真实PvE来源、online L/N/C、两组件float32保存、600+rand批次及钱包cap | 未flush池只在运行时，绑定双方attachment/关系边并丢旧池；只持久化已授予的钱/receipt。通知用实际入账delta，先NOTI76加客户端钱、再规范绝对余额；真实交易取消/摆摊销售绝对金币包由owner队列统一采样排序，不额外扣徒弟、不补离线税 |

师父金币的最后一项以5078批准修复为准：单纯锁住mentor发送仍不能阻止别的服务晚送旧绝对余额，真实交易取消和私人商店销售发送者的交错已由专项RED/GREEN与独立复审闭合。原生提示、实际余额及两次完整重启保持由组件页闭合；最终正常runtime交付和原档保持由最终索引单独绑定。

## 证据阻塞的功能

这些缺的是匹配版本/业务链，而非等待常规确认；本轮没有返回虚假的成功包来填覆盖率。

| 项目 | 已知依据 | 仍然阻塞的具体条件 |
|---|---|---|
| 师父疲劳收益 | 后期来源、结算和角色/账号quota代码存在；ACT1也有显示接口 | 当前ACT1 PVF没有`[member bonus fatigue limit]`，已证parser默认两限额0，正收益裁0；缺ACT1正额度及适用日界/计数契约，不能猜20/30/156或把0收益称恢复 |
| COF | ACT1文字与旧数据库字段存在 | 当前样本活动检测恒0、费率恒1，计算函数为空/无状态更新；文字不能补具体增减量、持久化、舍入和真实生效方程 |
| CMD147聊天举报 | 原生选中聊天→context action22→UI105确认→两个CP936字符串闭合；后期有真实Monitor与DB链 | ACT1 implicit type/schema、适用quota/reset/duplicate、bounds/失败显示及“一个月检验后处分”的consumer未闭合，不能任猜type0或只存JSON便显示已处理 |
| CMD145受限旧坏名修复 | 原生旧名禁用检查→UI100/101；slot+old/new字符串；后期restricted rename有DB业务 | 当前ACT1禁止名数据、collision/view255生命周期及引用更新规则未闭合；这不是普通改名卡/任意合法角色改名，146已是独立gift查询 |
| CMD91失败收摊回城 | 89 error22→UI72→空91与后期MoveToGate函数已知 | 函数不回收库存/clear/改store state，调用的move_area仍因Busy1/2拒绝；成功移动及最终escrow状态未闭合，不能用force-close发邮件替代 |
| 公会原生创建/加入/退出 | UI60/61 factory/RTTI/回调绑定已闭合；后期公会服务确有业务 | 创建资格71成功只关闭窗口，未提供名址提交；加入78确认转旧网页，缺auth/申请/审批回调；退出/踢人/解散ACT1入口/wire未闭合。factory绑定完成不等于公会创建完成，不能种两成员冒充原生闭环 |

举报DB地址需明确勘误：活动链是 `Inter_ReportMannerless@084D24A2` 投signal202，经signal+1调到 **`DB_ReportMannerlessUser_Upgrade@084276B0` 的INSERT**；旧 `0842796A` 是legacy三字符串/cache路径，本样本不执行该INSERT。后期目标表确认为 `charac_accusation`，保存type、reporter账号/名、双方server group、target名、时间、chat。后期schema被识别，并没有解决ACT1两字符串如何映射type/schema：后期0/1/2都带chat，constructor4为无效sentinel。后期name键普通2/特殊10额度、target/type全局去重、local06:00清内存和ACK先于DB确认也不能直接宣称ACT1原版规则；月度审查/处置链仍未证明。

## 旧审计更新与未来背景

旧审计的“师徒只有空82”“普通决斗无53–62”“时装67永不过期”“146未实现”均不能沿用到5078源码。当前79–82走真实师徒服务，125与118已拆开，146为礼物收礼角色查询，普通决斗有独立房间/对局服务。CMD101虽有常量且被PvP入口分类接收，当前没有QuickJoin成功业务分支；CMD122挑战、8人/team/观战等不因此恢复。忙态白名单允许141也不等于权威校时业务已经闭合。

更广PvP/原生备用relay、公会全产品、WarRoom/assault、其他商城礼物类（饲料/改名卡/服务/包/货币/限时宠物）、22/23租期扩展、TIME141与辅助安全命令仍是后续背景。当前没有对其中一个新有界候选同时闭合“官方活动业务、ACT1实际入口与wire、完整校验/持久化、可执行测试及原生路线”，故本次**没有新增IMPLEMENTATION_READY组**；它们不是当前原生与交付收尾的活动阻塞项。只有名字/保留receiver/共用caller不能升级为Ready，已知native空NOTI52和故意单向160–162也不作为漏功能填充。

上述六组可实施范围、证据冻结和运行交付已经完成。后续系统待相应证据契约闭合后再实施；未闭合系统继续明确保留边界。
