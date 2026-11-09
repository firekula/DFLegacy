# 项目状态与接续说明

2026-11-07第一章更新：四类独立决斗频道、GM胜点发放及向所有现有角色发送邮件已经实现；房间进一步支持八座位个人战、组队战和擂台赛，包含申请出场、换人、整队结算及持久化去重。协议/段位、原双人个人战、2v2/4v4及完整服务端smoke均通过。双客户端实机完成模式切换、红蓝分队、擂台开战与结算；组队战实机开战、四客户端换人和跨机器网络仍未复测，详见[频道与GM验收](docs/acceptance/2026-11-07-pvp-gm-rewards.md)及[组队战与擂台赛验收](docs/acceptance/2026-11-07-pvp-team-relay.md)。

用户已要求同步本轮源码和文档，并停止本地测试服务器；127.0.0.4测试监听已全部释放。最终构建保留在独立测试目录，不自动启动。后续发布范围为v0.1.7 GHCR服务端镜像和GitHub脱敏源码快照，客户端更新附件继续保留v0.1.6；线上运行实例和第二章工作区不在本次部署范围。下方较早记录保留其当时的构建身份及验收边界。

2026-10-30新增修复：[N地图副本门前终点](knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)已完成。debug `0082559C…` 支持精确 `dungeon_gate` 终点和区内移动，保留门、图标及禁止借道规则；单客户端新增跨区／同区／返回／拒绝场景通过，未重复四端矩阵。当前手测客户端使用新DLL，其他日常目录未覆盖。

最新总入口：[2026-10-30源码与交接](HANDOFF-2026-10-30.md)。U05已支持能力和未证边界分开记录；U07／GP42、U08仍未闭合，日常目录未随本次源码归档更新。

2026-10-30本轮U05：[技能资源与接续交接](knowledge/decisions/2026-10-30-u05-resource-closeout.md)新增MP成本、非零使用上限／计数及原生首槽物品门，保持未知守卫。最终2D已四端实载；13条实机匹配、4条未知超时、240条SDK投影，初始四端存活与后续B/C/D自然死亡分开记录。Python620项／569子测试、相关原生及overlay/ABI通过；14文件恢复／8保护项不变。全局输入、密码PvP及特殊未证分支保留；U07／GP42、U08保持，未部署日常包。

2026-10-30前阶段U05接续：[鼠标门与八类弹窗](knowledge/decisions/2026-10-30-u05-mouse-dialog-followup.md)已补type1早期门、CFD208按钮更新暂缓和分层覆盖。最终B4四端实载，07/09开关、自然原生门及24条实机CLI通过；16条另记历史回放。完整Python617项／553子测试、相关原生及overlay/ABI通过，5张原图；14文件恢复、8保护项保持。全局输入阻断、56实机和其他未证UI保留；U07／GP42、U08继续暂停，日常包未覆盖。

2026-10-30前阶段U05：[队友／城镇MP、键盘门和弹窗层级](knowledge/decisions/2026-10-30-u05-mp-input-dialog-verification.md)已完成限定四端验收，含MP来源、聊天锁级、四端0x25弹窗附着／层序和CLI等待。613项全量＋1项夹具补验通过，原生回归通过；14文件恢复、8保护项不变。全局输入阻断仍未知，U07／U08／GP42保留；最终收窄守卫仅补竞争回归，实机和最终产物哈希分别记录，未部署日常包。下文为历史阶段。

2026-10-30后续源码进展：[三个剩余阻断](knowledge/decisions/2026-10-30-remaining-blockers-followup.md)。第四人邀请按原生旧单人队流程闭环；测试瞬移完整发布、原生普通插值旧时限分支已修。U08卡顿和U07自然大差／GP42继续阻断，四端首领仍捕获421px级差。Python586通过／1条件跳过／445子断言，完整IJL及相关debug原生测试无失败。所有测试进程关闭、12个客户端文件恢复，正常存档保持；本轮改动未覆盖日常发布包。下方保留夜间交付及各阶段历史状态。

2026-10-30最终状态：[夜间报告](knowledge/decisions/2026-10-30-overnight-restoration-report.md)和[日常交付验收](knowledge/decisions/2026-10-30-normal-delivery-verification.md)。服务端AA11／IJL8B06／debug1AFD现已更新到日常目录，33文件核对、8保护项保持、四端启动与跨区组队通过，测试已全部关闭。最新完整Python574通过、2跳过、413子断言、0失败，之前6项测试耦合已收尾；完整托管及IJL通过。U11已作为最后业务项完成；U07历史同步、U08剩余卡顿及U05回城第四人再邀请保留阻断。以下是各阶段历史，未部署／6项失败并非当前状态。

2026-10-30 队列最后U11：[死亡队员接收端修复](knowledge/decisions/2026-10-30-dead-ground-removal-verification.md)完成旧版复现和新8B06模块四端同房／真门往返／金币验收；目标actor最终不存在，关闭后装备唯一。32项及完整IJL通过，debug修改仍要求存活。U07历史同步、U08剩余卡顿和回城第四人邀请仍阻断，不写成全游戏完成。

2026-10-30 U09：[技能栏持久化](knowledge/decisions/2026-10-30-skillbar-persistence-verification.md)完成同档基线及修复版两轮实机，逐包处理／null及替换失败回滚、CLI明确种子来源已补。完整托管通过，Python567／413subtests、2跳过及6既有失败。正常存档保持，接U11最后。

2026-10-30 U10：[超重原始协议复核](knowledge/decisions/2026-10-30-overweight-verification.md)确认实际发送2.526kg，克→公斤换算已正确；纠正旧图文字解释，无业务修改。继续U09，U11最后。

2026-10-30卡顿诊断：[默认关闭的回调计时／CLI](knowledge/decisions/2026-10-30-frame-profile-verification.md)及四端测试完成，新增各步骤与覆盖数据。两次51／69ms调用间隔仍未归因，不写成U08全修复；回城重组第四人原生拒绝亦保留阻断。后续U10／U09，U11最后。

2026-10-30位置复测：[U07城镇／怪物新窗口](knowledge/decisions/2026-10-30-sync-followup.md)末次上报送达、城镇终差<1px，普通房间592行最大30.27px；未复现历史大差，未改同步业务。原生应用事件／GP42未知保留阻断，继续后项。

2026-10-30死亡观测增量：[U05测试地面／库存](knowledge/decisions/2026-10-30-dead-observation-verification.md)已分离观测与修改，C／D死亡脱离后35／34个新序列及库存核对通过，死者修改零发布。完整Python547／392subtests、6项既有失败；继续U07／U08／U10／U09，U11最后。

2026-10-30界面增量：[U05选择副本／常用弹窗](knowledge/decisions/2026-10-30-ui-state-verification.md)已补v2 scene2消费、稳定索引投影和五类活动窗原生标志。四端加载、实际class37／35开关、返回按钮通过；完整Python545／376subtests及6项既有失败。死亡测试地面观测继续，U11仍最后。

2026-10-30最新：[U05技能冷却与可用性](knowledge/decisions/2026-10-30-skill-state-verification.md)限定完成。native6431518F仅隔离部署，四端验证C上挑／D枪手CD循环、跳跃上下文和死亡阻止；复杂道具／装备／次数／精度分支保持未知，不等同实际命中。原生相关和Python235/315通过，完整541/363仍6既有失败；托管E058未改未重跑。进程已关闭、四份INI恢复、正常存档E0E2FB4B…保持。接U05 UI／scene2等剩余，用户已授权整夜完成列表，阻断记录跳过，U11最后。

2026-10-30最新：新增[玩家击杀CLI与本地MP观测](knowledge/decisions/2026-10-30-player-kill-mp-verification.md)。2～4人组队副本本机角色，队员／队长原生死亡、服务端及四端确认、已死重复拒绝通过；本地MP技能消耗／恢复与HUD核对通过，5张原图。native E1CB3D51／托管E0581828仅隔离部署，已关闭并恢复配置，正常存档E0E2FB4B…保持。完整托管冒烟、相关原生和Python229 tests／300 subtests通过，完整Python仍6项既有失败。后续接U05技能冷却／可用性与UI等剩余项；U11按最新指示移到队列最后，排在U09技能栏之后，覆盖下方历史“技能栏最后”的顺序。

2026-10-30接续：已完成[四端主动丢弃扩展](knowledge/decisions/2026-10-30-active-drop-four-verification.md)，新增原生CMD50测试CLI及独立库存采样；蓝装、封装紫装四向交接，材料部分丢弃、金币守恒及换房返回共12次装备提交通过，附5张原图。托管91项新断言、完整Release冒烟、原生相关套件及Python相关221 tests／280 subtests通过；完整Python仍6个既有失败。仅隔离目录部署native12D58F6A／托管1ECD5759，进程已关闭、INI恢复、正常存档E0E2FB4B…保持。后续继续U05剩余观测、U07／怪物同步、U08其他卡顿与U10单位；U09技能栏最后处理。下方2026-10-29“下一阶段补U04”是前组历史范围，本组已按上述限定补齐。

本文面向"换一个对话/换一个人继续开发"的场景，记录**当前实现状态、已验证与未验证的部分、可复用的验证方法、以及待办**。
模块职责见 [README.md](README.md)，构建见 [BUILDING.md](BUILDING.md)，部署见 [DEPLOYING.md](DEPLOYING.md)，接口见 [ADMIN_API.md](ADMIN_API.md)。本文不重复这些内容。

## 1. 近期改动的功能

2026-10-29最新接续（源码 `7f8ddf5`）：默认关闭的测试CLI/debug已完成，支持状态、当前副本定位、指定怪物原生死亡、现有掉落隔离及服务端最终结果查询。隔离四端25次拾取为24次持久化提交、1次超重拒绝，四次史诗“否”保留地面，四人“是／掷点”均有实际结果；金币23分为5／5／5／8。源码、测试、7张原图及边界见[本组记录](knowledge/decisions/2026-10-29-test-control-four-client-loot-verification.md)。仅部署到隔离测试客户端／服务端，不写成正常运行目录已升级；测试进程已关闭、四份INI恢复、正常存档E0E2FB4B…保持。下一阶段补U04蓝色装备、封印等元数据及反向／重复／换房四人验收；技能栏持久化仍放最后。

2026-10-29城镇操作的后续范围已推进：[客户端内跨区规划](knowledge/decisions/2026-10-29-native-town-route-verification.md)、[仅右键及黄色双次反馈](knowledge/decisions/2026-10-29-map-right-feedback-verification.md)、[回城再寻路](knowledge/decisions/2026-10-29-town-return-perf-verification.md)和[组队接口及同组副本闭环](knowledge/decisions/2026-10-29-debug-party-commands-verification.md)分别有原图记录。普通操作默认不截图，四窗启动固定排成田字形，不能把早期同区验证范围当作当前跨区能力的上限。MP／技能冷却、通用模态UI、选图scene2消费、怪物位置同步及其他卡顿仍按[待处理列表](knowledge/decisions/2026-10-28-restoration-backlog.md)接续。新增相关检查通过，完整Python的6个既有失败及原生UI时序失败见本组记录。

2026-10-29早期 debug 状态与城镇移动（历史范围）：新增 client-state/v2 的 Town/UI、房间／敌人／玩家／已学技能只读数据；同区 debug 目的地与 N 城镇地图点击共用原生 setter 和一次持续移动激活，保留方向键停止。源码02de7053/debug5449，双端实际到达、控件保护和四类拒绝通过，71项Python及原生状态／导航／运行状态机／完整overlay测试通过。当时最终构建单人副本完成原生字段对照与无截图的数据驱动移动、攻击；不把该历史阶段算作同组完整流程、MP/冷却、跨区规划或怪物同步已修复。详见[原验收](knowledge/decisions/2026-10-29-debug-town-navigation-verification.md)，后续范围见上方记录。

2026-10-28 倒计时复活币：native09429cc/19C修复隐藏100004单币购买结果误入普通商城的未知错误，价格显示100且标签一次，官服发币与ACT1 CMD44复活分开。双客户端完成队员已有币复活、队员购买后复活、转队长购买后复活，普通10币/880礼包保持原结果窗口。三轮消费与四笔购买钱包守恒；最终445文件证据、严格UDP、422次响应采样、四处native及49运行文件交付/原配置和原档核验完成，见[9张原图及精确边界](knowledge/decisions/2026-10-28-countdown-coin-purchase-verification.md)。managed仍保留59666d9。新增[用户待办](knowledge/decisions/2026-10-28-restoration-backlog.md)，优先debug.dll结构化状态以减少截图；6级heal1024符合官服≤17级退出豁免，无需修复虚弱。怪物位置同步继续排查。

2026-10-28 洛兰3×3：补读每套maze的Boss专用候选，选中Boss角色坐标时优先应用；修正(2,2)回退洛兰MAP13的问题，实际选择MAP6416与Boss1077。真实PVF、旧布局/无效候选/任务替换/缓存及起点重合回归、完整冒烟、严格Release通过。59666d9托管/native8AB双客户端走完实际3×3路线，两端加载同一Boss房；B死亡、A自然清场，双方Continue及队长带队回城完成。正常目录49文件交付与PS5.1克隆档启动已验，原配置/存档与历史证据保持。见[8张原始截图及精确范围](knowledge/decisions/2026-10-28-boss-map-specification-verification.md)。接续怪物位置同步S02；本次不计为位置同步或复活币UI修复。

2026-10-28 组队 TCP 回退：ACT1 完整登录尾、真实 peer 账号及原生 12 字节中继已恢复；原生发送队列竞争另经九处受保护入口修复。完整 .NET 冒烟、Release、Win32 兼容及独立严格抓包检查通过。同一 20d/8AB 构建完成正常 UDP、两轮受控 UDP 故障的自然战斗/通关/回城，以及双端频道重入；含 12 张原图的[验收记录](knowledge/decisions/2026-10-28-party-tcp-fallback-verification.md)区分失败 v2、v3 截图缺项与 v4 补证。正常目录 49 文件交付及 Windows PowerShell5.1 克隆存档启动通过；五份原配置、两原档、EXE/PVF、历史包及备份复算一致，测试进程退出。怪物位置同步尚未修复：新快速观察有一只普通怪持续约653–736平面差异，继续对照原始接收链；不强制怪物归队长、不写坐标。

2026-10-28 频道重入：用户补图的“未响应后能进入”由原生同步MTU探测造成，基线三次阻塞10.814–10.974秒。同一manager/主机/本地IP/端口复用真实MTU并刷新主机引用；最终合并构建双端登录应答至继续请求为0/1毫秒，首次和身份变化仍走原探测。双端组队、副本战斗和换房17项回归通过；UDP应用审计仍有既有CRC/cipher警告，未宣称全部解码通过。图文、计时口径与边界见[频道验收](knowledge/decisions/2026-10-27-channel-return-verification.md)。

2026-10-28 普通商城赠送：恢复ACT1 CMD146查询及CMD67 gift1，官服普通实物、黑名单和账号月次数规则，默认额度1；扣点券、邮件、计数同快照原子提交，账号在线会话重读规范余额。原生全部赠送门控恢复，空篮和禁赠商品仍禁用。在线及离线原生赠送、收信领取、额度、重登/重启守恒102项检查通过，附18张原图和官服/ACT1差异的[验收记录](knowledge/decisions/2026-10-27-cera-gift-verification.md)。完整.NET冒烟、Win32兼容、各自Release构建和服务端发布通过；整份slnx的dotnet构建仍受C++ VCTargetsPath工具链限制，未计为成功。同一实测构建已部署，5份原配置与原存档哈希不变，正常环境26项核对通过，两端停在频道选择。时装/宠物/礼包/直接效果赠送和物理删角的官服24错误码尚不对等，详见记录。

2026-10-27 商城限时时装：ACT1原生7天/30天/无限制菜单和计价已恢复，服务器绝对截止持久化，穿脱/领取/重启不续期，到期原子清理并刷新本人装备和对端外观。邮件、合成5秒门槛、陈旧快照与写入故障由自动化覆盖；完整.NET冒烟、Win32原生兼容及Release发布通过。真实双客户端完成六档购买（26600→19250点券）、三种期限提示、在线穿戴/包内回收、对端外观及离线重启清理，见含18张原图的[验收记录](knowledge/decisions/2026-10-27-avatar-periods-verification.md)。同一实测构建已部署，5份原配置与原档路径恢复、原档哈希不变，正常环境26项核对通过，两端停在频道选择。升级需同步服务端和两端及启动器旁IJL15；原生邮件/合成/副本到期画面未逐项验收，未开放时装直接交易/摆摊或CMD22/23延期。

2026-10-27 普通摆摊：ACT1 CMD85–90 与 NOTI78–85 已接通，普通店按官服开放7格、手续费为毛额4%向下取整，商品持久托管、售款信用及直接交易共享商业占位已实现。服务8为商人许可，ACT1城镇W入口依赖mode0许可标志；原生商城100022/物品16为380点券、30天，管理API可授予任意正整数天数。完整协议冒烟与Release发布在 `e494483` 通过。真实双客户端已完成材料10个分4+6购买、第一轮撤摊返还6个，以及24金币装备（强化+3/品质种子37/耐久19）转移，161项验收断言通过。服务端售罄保留营业，ACT1原生客户端会自动发送CMD89收摊；旧文案14格/1%与本轮7格/4%也存在版本差异，详见[验收说明](knowledge/decisions/2026-10-27-private-store-verification.md)。同一实测构建已部署，原配置和原存档路径已恢复，原档哈希不变，部署后25项核对通过。

2026-10-27 师徒基础关系：CMD79/80/81/82 与 NOTI72/73/74/75/77 已按 ACT1 接通，官服等级、容量、黑名单、06:00登记/解除日界和空关系重置已实现。关系原子保存，删角清理，按会话有序投递。完整冒烟和 Release 发布通过，真实双客户端完成拒绝、接受、列表、上下线、重登、在线/离线解除及再次结成，37项证据断言通过，原配置和原存档已恢复并部署。关系经验、COF和经验/疲劳奖励仍未恢复；详见[验收说明](knowledge/decisions/2026-10-27-mentor-verification.md)。

2026-10-27 后续：恢复队伍聊天与普通金币均分。使用 ACT1 NOTI12 与四槽 24 字节 NOTI39；金币资格独立于活人物品投骰，支持同房尸体、死亡换房排除与复活恢复。持久信用与永久收据防止重复拾取及会话陈旧余额覆盖，交易交付和翻牌回调已处理。完整冒烟与 Release 发布通过，双客户端证据见[本次验收](knowledge/decisions/2026-10-27-party-chat-gold-verification.md)。后期公会税、幸运点、1199 与装备/宠物加成仍未移植。

2026-10-27：完整取得官服拾取/普通骰子/特殊确认流程，并对齐 ACT1 的 8 字节 CMD46 与 12 字节 NOTI39。普通物品仅活人参与，来源/确认选择覆盖、武器职业加分、独立容量预筛、总人数轮转、无候选 22、预留 token 和失败取消回包已落地。洛兰双客户端已验证蓝色材料、白色武器、粉装、史诗三个原生确认按钮及同房死亡成员排除，双方地面与单次入账一致。独立浮动调试面板的显示、鼠标进出、开关真实点击与两端状态已验收，支持带服务端回执的本地自动控制。规则边界见[官服接口对齐](knowledge/decisions/2026-10-27-official-loot-contract.md)，结果见[实机记录](knowledge/decisions/2026-10-27-loot-live-verification.md)。后期全队投票与幸运点生成仍不属于已移植内容；独占全屏等面板场景仍待扩展实测。上一轮真实 PVF 的 8 项数据期望失败保留在历史记录，未冒称已修复。

| 功能 | 关键位置 |
| --- | --- |
| ACT1 组队 TCP 回退：完整登录/peer账号、规范会话路由、原生发送串行化及严格证据解码 | `FallbackRelayService`、`GameplayDatagramService.FallbackRelay`、`NativeFallbackRelayProtocol`、`relay_send_queue`、`party_fallback_evidence.py` |
| 普通商城赠送：ACT1查询/结算、原子扣款/邮件/账号月次数、会话现金失效；在线/离线双端验收 | `GameProtocolEngine.CeraGift`、`JsonGameStore.CeraGift`、`CeraGiftSessionEffects`、`CeraShopCatalog` |
| 返回频道重入：刷新owned host引用、按连接身份复用原生实际MTU，保留首次/变化探测 | `DFLegacy.Ijl15/src/channel_reentry.cpp`、`dnf_patches.cpp` |
| SP 按经验进度渐进发放；PVF 缺少 `etc/sptable.etc` 时用内置回退表（取值与原版一致） | `CharacterSpCatalog`，8 处给经验调用点 |
| 清除地图任务：救援图替换当次副本的 boss 房并按 boss 房结算 | `DungeonCatalog.TryApplyQuestMap` / `IsQuestRescueRoom` |
| 客户端 IPv6 中继：核心库 + 独立 exe + 由启动器进程内托管 | `DFLegacy.Relay.Core`、`DFLegacy.Launcher` |
| 玩法 UDP 会话接纳真 IPv6 对端（不再被 `MapToIPv4()` 压扁） | `GameplayDatagramService.NormalizeAddress` |
| 绑 `::` 时 UDP 也是真双栈（此前只服务 IPv6） | `DualStackUdp.Bind` |
| 服务端/中继可用环境变量覆盖配置（`DFLegacy__X__Y`） | `Program.cs`（在 server.json 之后注册） |
| 容器镜像与编排（host 网络、双栈、PVF/存档挂载） | `Dockerfile`、`compose.yaml` |
| 启动器账号 UI：WPF + HandyControl 交互式登录壳（注册/登录/改密/找回/账号管理/设置），headless 路径保留 | `DFLegacy.Launcher`（`MainWindow`、`Views/*`、`App.xaml.cs`）、`DFLegacy.AccountClient` |
| 城镇同屏与附近聊天：同镇同区的成员目录、外观/位置转发与区域聊天接线完成（真机可见性待 Task 11）；位置走 TCP（默认限速 10 条/秒）；调试幽灵支持单人复现 | `TownSessionDirectory`、`AreaUserIdPool`、`TownGhostService`、`EntranceService` |
| 交易系统：城镇内双向交易（24 物品格 + 金币、锁定/最终确认/原子结算、取消/超时/掉线/换区整笔回退）；协议要点与真机结论见 §2.5 | `ItemTradeService`、`ItemTradePlanner`、`JsonGameStore.CommitItemTradeAsync`、`EntranceService`（CMD 10/11/19/26） |
| 普通摆摊：7格商品、4%手续费、持久托管/售款信用、会话代际与离场回收；双客户端材料和装备交易已验收，见[验收说明](knowledge/decisions/2026-10-27-private-store-verification.md) | `PrivateStoreService`、`CommerceActivityRegistry`、`JsonGameStore.PrivateStore`、`GameProtocolEngine.PrivateStore`（CMD85–90/NOTI78–85） |
| 组队系统：城镇内组队（邀请/申请加入、成员名单与队长、退出/踢人/掉线、队伍信息列表与推荐伙伴的数据前提）；协议要点与真机验收清单见 §2.6 | `PartyService`、`PartyRoster`、`EntranceService`（CMD 10/11/12/13/14） |
| 同队进副本：每队共享副本实例（队员由自己会话入场、门口集结制换房、共享广播/掉落先到先得/各自结算）；实现摘要与真机验收清单见 §2.7 | `DungeonInstance`、`DungeonInstanceRegistry`、`DungeonPartyService`、`DungeonDoorAssembly`、`EntranceService` |
| 客户端 TCP/UDP 全量打印（`Config.ini` 的 `DumpNetwork`）：钩客户端自己的 ws2_32，绕开"抓包看不到 loopback"；用法与真机判定标准见 §3.4 | `DFLegacy.Ijl15/src/net_dump.cpp`、`DFLegacy.Ijl15/tests/ijl15_tests.cpp` |
| 客户端合成输入（B′）：ijl15 内 IAT 钩 `GetAsyncKeyState` + 直调 `NativeMouseInput`，`auto-input.json` 时间线驱动真机客户端完成 选频道/选角/进城，**全程无人工 UI**（真机验收见 §12，操作手册 `docs/superpowers/plans/2026-10-22-b3-real-machine-runbook.md`） | `DFLegacy.Ijl15/src/auto_input.cpp`、`tools/acceptance/auto_input.py`、`tools/acceptance/tests/test_auto_input.py` |

### 1.1 官服「登录 → 选服 → 进频道」链路分析（2026-10-05，**纯分析，未改任何代码**）

任务书 `docs/superpowers/plans/2026-10-05-official-login-channel-analysis.md`，交付物在
`docs/superpowers/sdd/2026-10-05-official-login-channel/`（6 份：`topology.md` / `STATIC-FINDINGS.md` /
`packet-timeline.md` / `dual-evidence.md` / `SPEC-login-to-channel.md` / `open-questions.md`）。

**结论摘要（每条都有证据，细节看交付物）**：

- **计划书里"`coserver`/`manager`/`channel` 被 strip"这条前提是错的** `[作废]`：
  三者都有 `.symtab`（3,450 / 6,963 / 1,731 个 FUNC），`df_channel_r` 与 `df_bridge_r` 还有完整 DWARF。
  误判根源是**容器内没有 `readelf`/`nm`/`objdump`/`strings`**（`command not found` 被读成 `FUNC=0`）。
- **客户端第一站是 TCP 7001 = `df_channel_r`**，实测序列 `CONNECT(11) → CHECK_SCRIPT_VERSION(5) → GET_SCRIPT(9) → ASK_CHANNEL_INFO(1)`，
  应答 `12 / 6 / 10 / 3`；7/8 与官服注册表逐条吻合。
- **频道列表由频道服下发**（应答 3，载荷 = `zlib(80 字节密文)`），而它的地址表**是 `df_bridge_r` 每 3 秒推给它的**（抓到了原始帧）。
- **进游戏是"新开一条 TCP"**（7001 会话关闭 → 4 秒后连游戏服端口），**不是复用**。
- **第二站是 `df_game_r`，`CMD 1 = LOGIN`**（`DisPatcher_Login`）。账号口令由网关校验，
  游戏服**验签**：网关持 `/data/privatekey.pem`、游戏服持 `publickey.pem`，
  **两侧 RSA 模数实测相同**（容器与参考包两个独立部署都是）。
- **`df_manager_r` / `df_coserver_r` 完全不参与玩家登录**：四个进程对客户端命令词表 **0 命中**；
  coserver 的 UDP 30703 在游戏服 cfg 里的名字就是 `udp_ip_of_doublecheck`（重复登录检查）。
- **打断计划书 §5"不要扩大范围"的地方只有一处**：为了拿到"客户端视角的逐包"，用了项目既有的
  `ref-proxy` 抓包（`.superpowers/sdd/2026-09-29-dungeon-party/work/captured-run1/`），没有新开范围。

**动手前必须先做三个实验**（否则会返工）：`Q-CLIENT-7001`（2008 客户端在 7001 上用哪几个 opcode，未测）、
`Q-HDR`（帧头 10 vs 14 字节）、`Q-TOKEN32`（`CONNECT` 的 32 字节凭据怎么算）。见 `open-questions.md`。

### 1.2 官服登录链的**端到端实测**（2026-10-05 下午，真客户端 × 真官服）

**世代落差先说清楚**（项目所有者指出，已在产物上核实）：官服容器是**第四季外传·缔造者**
（区域表含 `[timedoor]` 时空之门；容器与参考包的 `server_str.xml` **md5 逐字节相同**），
而我们的客户端是第一季第一章 ⇒ **2008 客户端测不了这个后端**。
能测的是参考包里那个 **41.9 MB、2012-09-04 台湾分支**的客户端
（它自己的调试串写着 `E:\DNF\Src\Taiwan\120904_sub\…`）。

**怎么接起来的**（关键三点，缺一不可）：
1. 停 `DFLegacy.Server`（它占宿主 `TCP 7001` + `TCP/UDP 2311`）→ `docker restart dnf`
   ⇒ 容器的 `7001/tcp`、`2311/udp`、`7300/tcp` **这才绑上**（**证明**此前为空纯粹是被本项目占用，不是设计）；
2. 用**容器自己的**网关（宿主 `127.0.0.1:5505`），**不要**用参考包里的网关 ——
   两套 RSA 私钥模数不同，包里的令牌会被容器的 `df_game_r` 拒（`SPEC` §2.3）；
3. 启动器账号来自 `%APPDATA%\llnut-launcher\credentials.dat`（DPAPI 加密），
   `testuser4` 正好在容器 `d_taiwan.accounts` 里。

**结果（`[抓包]`+`[真机]`，产物 `docs/superpowers/sdd/2026-10-05-official-login-channel/work/captures/`）**：

- 客户端三条连接，每条**恰好 92 字节**，序列恒为
  `11 CONNECT(32B 凭据) → 5 CHECK_SCRIPT_VERSION(16B) → 9 GET_SCRIPT → 1 ASK_CHANNEL_INFO`，
  应答 `12（时间戳）/ 6（32B 摘要）/ 10（zlib 7239B）/ 3（zlib 139B）`，服务端流 7468B 精确链到 EOF；
- 容器 `channel/log/Info_channel.log` 同一秒打出 `In onCS_CONNECT → onCS_CHECK_SCRIPT_VERSION → onCS_GET_SCRIPT → onCS_ASK_CHANNEL_INFO → onClose`，**三仪器同秒对齐**；
- **与"参考客户端 × 参考实现"逐条同构**，只有随内容变的项变（脚本 7236→7239、频道表 102→139、脚本摘要 16B），
  协议常量不变（opcode、10 字节头、凭据前 16 字节）——框架对、内容别对，自洽性很强；
- **新结论①**：`CONNECT` 的 32 字节 = **16 字节跨世代常量**（8 条连接 / 2 个部署恒定）+ 16 字节每会话量
  ⇒ 已排除"网关签发的 token"与"账号相关"；
- **新结论②**：`GET_SCRIPT` 明文 **13312 B**，而两份 `channel_info.etc` 是 13311 / 13306，**都补到 13312**
  ⇒ 是 **16 字节分组的 `channel_info.etc`**（`df_channel_r` 源码模块里正好有 `Rijndael.cpp`）；
- **自我更正**：`id=12` 那条"官服注册表里没有"**不是矛盾** —— `registerProtocols()` 是**入站**表，
  发出去的报文本来不用登记；另外 `ASK_CHANNEL_INFO`(1) 的应答是 **3** 不是 2，我原先的"应答=N+1"推广有误（已改）。
- **没走完的部分**：客户端在 7001 链之后**没有**连游戏服（实测 30011/30021 连接数 = 0），
  原因是容器把 `30011` 发布成宿主 `30021`、而客户端被告诉的是 `30011` ⇒ 要进游戏需先补宿主侧转发。

### 1.3 【关键结论】2008 客户端的 7001 号表 **＝ 官服那一代**（2026-10-05 定案）

**问题**：本项目客户端停在第一季第一章，官服容器已到第四季外传 —— 两代的 opcode 是否相同？

**答案：相同。** 四方一致，其中**最强的一条是 2008 客户端自己的帧**（就在本项目自己的服务端日志里）：

```
DF\town-test\server-party44.log（EnablePacketTracing 打开）
10:10:50 RX type=0 protocol=11 length=43 records=1 crc=00000000 body=01 F2F7F142F7DEA0499F6BF6129FE6184F …
10:10:50 RX type=0 protocol=5  length=27 records=1 crc=00000000 body=01 1271F1F9D2D4D9A22F41B73FC78C34BF
10:10:50 RX type=0 protocol=9  length=11 records=1 crc=00000000 body=01
10:10:50 RX type=0 protocol=1  length=11 records=1 crc=00000000 body=01
```

与官服那次抓包（`11/5/9/1`、长度 `43/27/11/11`、`type=0`、`crc=0`、`records=1`）
**逐项相同**，连 11 号帧的前 16 字节 `F2F7F142F7DEA0499F6BF6129FE6184F` 都一致
（2008 客户端 / 2012 客户端 / 参考客户端**三方都相同** ⇒ 它是真正的协议常量）。

**另外三方**：`src/DFLegacy.Protocol/EntranceProtocolEngine.cs:31` 自述
`11/12 (key), 5/6 (cache decision), 9/10 (ChannelScript.pvf), 1/3 (entrance metadata)`
且注明 **recovered from DNF.exe 1.0.1.9**；官服 `df_channel_r` 的 `registerProtocols()`；
2012 客户端 × 真官服的抓包。

**三条随之落地的语义（原先全错或未知）**：

| 对 | 真实语义 | 出处 |
|---|---|---|
| 11/12 | **密钥交换**（不是"准入凭据"）：服务端在 12 里下发 32 B `KeyMaterial`，密钥 = 其前 16 B；**客户端发的 32 B 可以完全不看** | `EntranceProtocolEngine.cs:43,51-57` |
| 5/6 | **缓存判定**：6 的明文 = `int32 flag` + 16 B `SessionToken`；`flag=0` ⇒ 用协议 9 下载脚本，`flag=1` ⇒ 命中缓存 | 同上 `:44,60-67` |
| 9/10、1/3 | 脚本（`channel_info.etc`）与**频道表**（652 B，9 组固定布局） | `:45-46,70-74`、`EntranceMetadata.cs` |

**密码学（同样从 1.0.1.9 反推，已实现可用）**：**AES-128 / ECB / `PaddingMode.None`**，
密钥 = `KeyMaterial` 前 16 B，**补零到 16 的倍数**（不是 PKCS#7、也不是定长缓冲），
顺序 **先加密后 zlib**（`EntranceCrypto.cs`）。这一条同时解释了我观测到的
`13311→13312` 与 `13306→13312`。

**登录**：`CMD 1`，正文 = 计数 + `len32`+账号名 + `len32`+口令 + `len32`+8 B，
**账号口令是明文**（日志里直接可读 ASCII `test`/`test`；`EntranceService.cs:4652`）。
这与 2006 日服那代（下表）一脉相承，与第四季那代"网关签 RSA 令牌"**不是同一套**。

**第三个独立确认（项目所有者指引的日服 40 级那份）**：`G:\Game\Arad40-v1.31\Arad40\data\launcher.log`
是一次完整解析过的 2006 年真实会话（服务端自己打出 opcode 名与字段）：
`opcode=1 login{login,password}`、`opcode=2 character_list`、`opcode=4 enter_game`、
`opcode=5 character_create`、`opcode=8 character_list_request`、`13/19/21` 通知 ——
**与 2008/官服的号表逐条吻合** ⇒ **命令号在整个血脉里高度稳定**。
⚠️ 但它是**单连接单体架构**（全走 `127.0.0.1:31100`），进程划分**不要照搬**。

### 1.4 【帧头定案】11 字节，且 `header_*` 是死配置（2026-10-05）

**帧头是 11 字节**（`STATIC-FINDINGS.md` §4.4），字段：
`[0] category` / `[1] packet id` / `[2..5] size(LE，含头)` / `[6..9] 校验` / `[10] ackOk`，
**载荷 = `header+11`、长度 = `size-11`**。证据是三处独立常量 + 四个访问器：

- `tagPacketHeader::setCategory`(0x08051676) = `mov BYTE PTR [eax],dl`；
  `setPacketID`(0x0805164c) = `mov BYTE PTR [eax+1],dl`；
  `getSize`/`setSize`(0x0805165c/0x08051668) 读写 `[eax+2]`；
  **`setAckOk`(0x08051640) = `mov BYTE PTR [eax+0xa],1`** ⇒ 偏移 10 是**头字段**，不是"记录数"。
- `onCS_CONNECT` 里 `call getSize; sub eax,0xb` + `add edx,0xb`；
  `onSC_GET_GC_INFO` 里 `mov eax,[eax+0xb]` —— 两处都指向 **11**。

⚠️ **同构陷阱（别去"修"）**：本项目 `PacketFrame` 是"10 字节头 + 载荷首字节=记录数(1)"，
与官方模型**线上字节完全相同**（`43 = 10+1+32 = 11+32`，`47 = 10+1+4+32 = 11+36`），互通无碍。

**`header_*` 那组键（`header_msg_no = 5 1` 等）是死配置**：逐键在容器所有可执行文件里搜过，
**一个都不存在**（对照组 `scriptpacks` 命中 `df_game_r`）。⇒ 原先"官方用配置控制头布局"的说法作废，
布局是**硬编码**的。这也解释了为什么 Windows 参考包里那组键与容器**逐字相同**——没人读，就没人改。

### 1.5 【处理器函数体已读】拒绝路径与本地文件依赖（2026-10-05）

`work/handler-report.py`（call 目标 + 精确归因的字符串引用）：

| 处理器 | 拿到的东西 |
|---|---|
| `onCS_UPDATE_CHANNEL_INFO` | 两条拒绝路径：`"Incomming invalid gc_no="`、`"Invalid ServerGroupIndex"` |
| `onSC_CHECK_SCRIPT_VERSION` | 读本地 **`channel_info/version`**；错误串 `"[ERROR] : cannt open channel_script_version file"` |
| `onSC_GET_SCRIPT` | 读本地 **`channel_info/channel_info.etc`** ⇒ **实证 `id=10` 的明文就是它** |
| `onSC_GET_GC_INFO` | 读 `header+0xb` 的 4 字节，打 `Server Group Count = '%d'` |
| `onSC_ASK_CHANNEL_INFO` | **空桩**（10 字节 `mov eax,1; ret`） |
| `onCS_CONNECT` | **会用会话密钥解密客户端那 32 字节并用**（`"dnf_game"` / `"Key "`）⇒ 官方不是"忽略"它；本项目不看它也真机可用 |

**`/data/frida.js` 的真相（推翻我先前的猜测）**：它是 **2433 行的玩法修改脚本**
（绝望之塔金币、门票修复、镶嵌、怪物攻城…），用硬编码地址直接调 `df_game_r` 内部函数，**不是调试打点**。
且 **frida 只注入 `df_game_r`**（`start_game.sh` 有、`start_channel.sh` 没有）⇒
想给频道服动态打点**没有现成设施**。可用的一点：`frida.config` 是 `on_change: reload`，
往 `/data/frida.js` 追加 hook 会热重载进游戏服 —— 但**那是生产路径上的文件**，属探针，用完要退役。

### 1.6 【账号与计费定案】不存在"两套账号的映射"（2026-10-05）

**全栈只有一个账号 id —— `d_taiwan.accounts.UID`（int）**，没有任何映射表：

| 环节 | 键 | 实测 |
|---|---|---|
| 账号本表 | `accounts.UID int(11) PRI auto_increment` | 3 行：`1=testuser3, 2=testuser4, 3=test` |
| **计费** | `taiwan_billing.cash_cera.account varchar(30) PRI` = **UID 的十进制字符串** | 行值 `'1'/'2'/'3'`；UID 3 的账号行与计费行**同一秒**创建、`cera=1000` = 网关 `INITIAL_CERA` |
| 登录态 | `taiwan_login.member_login.m_id`（+ 分片的 `login_account_1..3`） | `m_id = 1,2,3` 与 UID 对齐 |
| `accounts.billing`（int，全 0） | — | **计费表不用它** ⇒ 独立槽位，实现计费不需要 |
| 上游身份（现代官服） | `m_id` | `WongWork::CSimpleSSO`：`_checkValiedPassword` 走 `CRijndael` + `DNFFLib::Binary2Hex` 比十六进制串；`checkValidPassword_Jpn` **只对 GM 强制校验口令**（`CGM_Manager::IsGm`） |

**对 2008 目标无需实现任何"映射"**：登录帧里没有计费/平台字段。
顺带钉住：那 8 字节尾随数据是**每机器常量**——`server-party*.log` 里 **42 条登录帧（9/28 → 10/3，多次会话）整个 body 逐字节相同**。

### 1.7 【UDP/NAT 定案】官服 `df_stun_r` 与本项目的 NAT 探针是**同一个协议**（2026-10-05）

`df_stun_r`（64 位、未 strip、~11 KB）三个线程 = 三种角色：
**2311 `RealIP` / 2312 `NAT` / 2313 `MTU_CHECKER`**（线程名就在它的 `.rodata` 里）。
协议本体只有 240 字节（`msgProcess` @ 0x401584）：

```
01 → 7 字节 02 | addr(u32 小端) | port(u16 小端)，从**同一**端口回
05 → 同样 7 字节，但从**另一个端口**回（PortRestrict socket）
00 开头 10 字节 → 不回（保活）；其它 → 不回（**不是 RFC 5389**）
MTU_CHECKER 端口收到任意报文 → 回 2 字节 = u16 小端(收到的长度)
```

**两侧实测一致**：
- 实探官服：`01` → `02 01 00 00 7f f4 8e`（`0x7f000001` = 127.0.0.1、`0x8ef4` = 源端口）；`05` → **从 2313 回**；RFC-5389 请求无回包；2313 → `0100`/`1400` = 长度回打。
- 2008 客户端自己的 socket 打印（`DF\netdump-53916.log`）：`TX 01 → RX 02 B103A8C0 C713`（`0xC0A803B1` = 192.168.3.177、`0x13C7` = 5063 = 客户端源端口）。

⇒ **本项目"反转地址字节 + 小端端口"的写法是对的**（我上一轮怀疑写反，被实探否掉）。
⇒ **端口号是部署配置**：官方 `2311-2313`，那次 2008 运行里客户端探的是 `7002/7003`。

**一处要补的保真缺口**：本项目把三种角色压在一个 socket 上，
**`05` 的应答也从同一端口回**（官方是从另一个端口回）⇒ 客户端据此判 NAT 类型会与官方不同；
MTU 探查角色（长度回打）也未实现。详见 `SPEC` §5.4 与 `open-questions.md` Q-STUN2311。

**新开的一个小问题**：~~2008 客户端的"游戏阶段"走哪条连接~~ —— **已定案（纯静态）**：

**2008 客户端开两条连接，且与官服那一代整体错开一格端口**：

| 角色 | 2008 客户端（本项目） | 官服第四季 / 2012 客户端 |
|---|---|---|
| 入口握手 `11/5/9/1 → 12/6/10/3` | **TCP 2311** | TCP **7001** |
| 登录 `CMD 1`（账号口令）+ 全部玩法 | **TCP 7001** | TCP **30011** |

**证据**（不重跑客户端，纯离线 join）：日志的会话建立行 `Entrance client <id> connected … on local port <N>` 带端口，
帧行 `RX <id> … protocol=<p>` 带会话 id —— 用
`.superpowers/sdd/2026-10-05-official-login-channel/work/join-session-port.py` join 起来，
**`server-party*.log` 30+ 份（9/28 → 10/3，单人与多人）里 2311 上恒定只有 `[1,3,5,6,9,10,11,12]` 这 8 个协议，7001 上有 34–63 个。**
⇒ **"先握手、再新开一条做游戏"两代相同，只有端口号错开一格。别把官服 7001 抄成我们的 7001。**

## 2. 验证状态

**已实测（可复现）**

- 服务端在本机与 Linux 容器中都能启动，并加载真实 `Script.pvf`：48326 个脚本文件、10942 个物品定义、869 个任务、43 个副本、1025 个地图。
- 真实存档可正常加载（含既有角色），启动时只发生预期的每日疲劳重置。
- 原生随机桥在 Windows 上生效（`provider=RDSEED, nativeBridge=True`）；容器内按预期回退为操作系统 CSPRNG。
- 中继：TCP（HTTP 健康检查与数据接口）与 UDP（NAT 探测往返）经 IPv4→IPv6 转发成功；独立 exe 与"启动器进程内托管"两条路径均验证。
- 启动器：`--self-test`、`--check-d3d9`、`--install-ijl15` 通过；完整启动路径（托管中继 → 把客户端入场地址改写为 `127.0.0.1` → 客户端退出后中继停止并退出）用一个替身客户端实测通过。
- 容器：镜像构建、`healthy`、挂载 PVF、在 IPv4 与 IPv6 绑定下的 TCP/UDP/后台均可用。

**未验证（需要真实环境，接手时优先做）**

- 用真实 `DNF.exe` 的完整游戏内流程（登录、进图、任务、SP 增长、召唤师转职）。
- 公网 IPv6 的真实可达性：本机没有公网 IPv6，容器/服务端的 IPv6 行为只在回环与容器网络内验证过。
- Podman：本机 podman 机器损坏（WSL 发行版缺失，`WSL_E_DISTRO_NOT_FOUND`），只做了 Docker 验证；Dockerfile 只用经典指令，理论上 Buildah 可用。

### 2.1 启动器账号 UI

启动器已从无头程序改为 WPF 交互式登录壳（HandyControl 3.5.1）：无参运行打开主窗口（账号下拉 + 密码 + 服务端状态灯），注册、登录、改密、找回、账号管理与设置（主题）都在界面内完成；带 `--headless`/`--account`/`--account-index` 等开关时仍走无头路径（不打开界面，退出码保留）。

**已实测（可复现，2026-09-26）**

- `dotnet build src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -p:DesignTimeBuild=true`：成功，0 警告 0 错误（本机没有 CMake，`DesignTimeBuild=true` 跳过原生 ijl15 目标，见 §5）。
- `dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release`：退出 0、全绿，含 `AccountClientSmokeTests` 契约测试（桩 `HttpMessageHandler`：注册 201/409/400、改密 200/401/400、找回 200/401/400、健康检查 200 与不可达）。
- `dotnet run --project src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -p:DesignTimeBuild=true -- --check-d3d9`：退出 0（headless 回归，无 UI 依赖）。
- `dotnet publish src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -p:DesignTimeBuild=true -o ./dist/launcher-test`：产物含 `DFLegacy.Launcher.exe`、`DFLegacy.AccountClient.dll`、`HandyControl.dll`、`launcher.json`；此方式不含 `ijl15.dll`（需含 ijl15 的分发产物用带 C++ 工作负载的 VS msbuild 另行发布，见 BUILDING）。

注：此前记录的 8 项与 PVF 版本相关的**既有**冒烟失败（PVF 缺 `etc/sptable.etc`、文件数/商城定价/宠物进化数据与测试基准不同）在本次运行中未复现；换用不同版本的 PVF 时，数据相关测试仍可能再次与基准不一致。

**手动验证清单（需要桌面与真实客户端，接手时优先做）**

1. 启动器无参运行 → 出现主窗口。
2. 点注册 → 注册成功 → 账号自动加入下拉列表 → 启动游戏 → 游戏内登录成功。
3. 改密、找回、账号管理、设置（主题切换）各走一遍。
4. 远程 IPv6 场景：账号 API（8081）默认无法触达，属预期（中继不转发 8081，见 DEPLOYING §6.4）；如需开启按该节操作。
5. 确认注册的账号已写入 `launcher.json`。
6. 客户端退出后启动器随之退出（中继驻留路径）。
7. headless（`--headless`/`--account X`）仍能端到端启动游戏。

**已知限制**

- 账号的 `RememberPassword=false` 时配置只保存空密码：UI 流程不受影响（启动前在界面输入密码即可）；但无头启动（`--headless`/`--account X`）会因「Accounts[i].Password 不能为空，也不能包含空白、问号或双引号。」校验失败——无头模式需要存储密码。
- 找回密码成功后，本地记住的密码不会自动更新：下次启动若勾选记住密码，需在 UI 重新输入一次（游戏内登录用的才是新密码）。
- `launcher.json` 经 UI 保存后会重写整个文件，中文注释会丢失（System.Text.Json 不支持注释往返，已确认接受；字段说明以 README 为准）。

### 2.2 8081 鉴权与角色（本次新增）

已实测（用本仓库构建出的服务端，临时 `DataPath`、非默认端口）：

- **权限矩阵 28/28 通过**：公开接口（`/health`、`/`、`/admin/*`、注册、找回、登录）匿名可用；`/api/**` 无 token 一律 `401`；玩家查自己角色 `200`、查他人 `403`、管理接口 `403`；管理员全通；未登记路径 fail-closed（`401`）；登录接口末尾斜杠与不带一致。
- **角色即时生效**：提升/降级后旧 token 立刻 `401`；重新登录后按新角色放行；「不能降级自己」「至少保留一名管理员」两条约束返回 `400`。
- **限流（安全复审后重设计，两级预算）**：单机连续 8 次错误密码后仍 `401`，**正确密码照常登录**（消除了"匿名者可把管理员锁在门外"的拒绝服务）；独立实例上把来源额度调成 3，第 4 次起**正确密码也 429**（来源级硬挡，证明单机爆破被卡死）；找回密码共用同一预算。
- **改密/找回吊销旧 token**：改密成功后原 token 立即 `401`。
- **双栈绑定**：`Admin.Host=::` 时 netstat 同时出现 `0.0.0.0` 与 `[::]`，`127.0.0.1` 与 `[::1]` 都能访问。
- **smoke 覆盖**：`AdminAuthSmokeTests` 断言 40 条路由权限表 + token 生命周期（滑动续期/绝对上限/登出/按用户吊销）+ 两级限流语义（多来源才锁账号）+ 有界内存（2.5 万条刷量后仍在上限内）+ 角色模型与引导名单 + 旧存档兼容 + QQ 码长度。
- 启动器侧：改密流程改为「先 `POST /api/auth/login` 取 token，再带 token 改密，成功后登出」，客户端契约测试断言了 `Authorization: Bearer` 头确实发出。
- **启动器客户端 × 真实服务端端到端 14/14**（真实 `AccountServiceClient` 打真实进程：注册 → QQ 码过短被拒 → 登录 → 旧密码错误 → 带 token 改密 → 改密后旧 token 失效 → 新密码登录 → 引导管理员登录 role=1 → 玩家 token 改他人密码被拒 → 找回密码 → 登出）。
- **独立安全复审**：对 8 个提交的整份 diff 做了对抗性复审（含自建最小复现工程逐条探测路径归一化/编码绕过），逐条核对 36 条路由的权限档位，未发现权限表绕过、越权或 token 泄漏到日志；复审提出的两个必须修复项（F1 找回密码无限流 → 无认证账号接管；F2 锁定型拒绝服务）与三项建议（F3 token 吊销与绝对上限、F8 末尾斜杠/fail-closed、F9 no-store 与暴露告警）均已修复并复测。

待人工（需要浏览器/桌面）：

- 打开 `http://<地址>:8081/admin/`，用**管理员**账号登录：看到 邮件/单人通知/广播/副本权限 + 用户角色，能提升/降级；退出登录后回到登录框。
- 用**普通玩家**账号登录：只看到「我的角色」，没有管理功能；访问 `/admin/player.html` 同样只看自己的角色。
- 充值页（`/client/client_pay.htm`、`/client_diamond.htm`）现在需要管理员身份：页面会弹登录框，登录后自动重试。**这一段只做了语法与接口层验证，未做浏览器点击验证**，请点一次确认。
- 错误路径：密码错误提示、来源限流触发后的「尝试过于频繁」提示。

已知限制：

- 老部署升级后，**原来的匿名 curl/脚本会全部 `401`**，需要先登录取 token（破坏性变更，见 ADMIN_API.md §8）。
- token 存在服务端内存：服务端重启后需重新登录（换取的是"角色变更/改密即时吊销"的能力）。
- 8081 默认对外（`::`）。**远程部署必须限制来源并在前置代理终止 HTTPS**，否则 token 明文可被嗅探。
- 密码哈希仍是无盐 SHA-256 + 固定前缀，QQ 码明文存在 `state.json`（既有实现，未在本次改动范围；建议后续换 PBKDF2/Argon2 + 加盐）。
- 反向代理后所有请求共用代理 IP，来源级限流会一起计数（见 ADMIN_API.md §1.2 的处理建议）。

### 2.3 城镇同屏与附近聊天（本次新增）

服务端按同一城镇同一区域维护在线成员，并向同区投递名单、外观、位置与区域聊天；进入区域（登录、回城、换区）时注册，装备变化广播外观，进入副本、返回角色选择或断线时离开区域；`SEND_MESSAGE`（类型 3）与两条宠物喊话命令改为按区域广播，副本内保留只回显自己的兜底。**同区互相可见与区域聊天已由两个真实客户端实测确认**（见下文「真机验收」）；`/api/admin/characters` 已返回 `townId`/`areaId`/`areaUserId`（不在城镇区域时均为 null），后台角色下拉显示 `城镇N/区域M`，便于核对两人是否同区。

**逆向确认的客户端顺序要求（「两人同区互不可见」的根因）**：客户端只从 `USER_AREA`（通知 23）/`AREA_USERS`（通知 24）建立外来角色，且两条通知都会先按 uid 查 `USERINFO`（通知 2，mode 0）记录、查不到就静默跳过该条目；而创建记录的路径只有 `USERINFO mode 0`。所以顺序必须是**先 `USERINFO`、后该 uid 的区域条目**。此前两个方向都发反了：新人先收名单（名单里老人的 uid 还没有记录），老人收到的加入增量又早于新人的外观广播——两边都被客户端默默丢掉且不报错，这就是同区双方都看不到对方的原因。修复：默认改为外观先于名单（`ForeignAppearanceBeforeRoster=true`），加入增量不再在进入时发，改为在该成员的外观广播之后补发一次（目录用 `TownMember.JoinAnnounced` 保证每人只发一次，迁移后的新成员对象自动归零）。

**自动化（全绿，随每个实现任务运行）**

`dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` 中的 `TownSyncSmokeTests` 覆盖：

- 区用户 id 池：容量跟随 `Channel.MaximumUsers`；首个 id 仍为 1（单人游玩保持字节级不变）；耗尽后拒绝继续分配；回收后可复用；越界释放被拒；0 容量被拒；调试幽灵从池尾反向分配。
- 配置默认值：`TownSync.Enabled=true`、外观先于名单（`ForeignAppearanceBeforeRoster=true`；`SendUserAreaForExistingMembers`/`RefreshRosterOnMembershipChange` 两个对称性开关默认仍为关闭）、`DepartureSignal=UserAreaInactive`、`MaxPositionForwardsPerSecond=10`、幽灵 `Enabled=false`。
- `USER_AREA` 的 active 参数：进区 active=1、离区 active=0。
- 目录：第一个成员收到的名单只含自己且没有外来 `USER_AREA`（与改造前一致）；新人默认先收老人外观再收名单；老人收到的加入 `USER_AREA(active=1)` 排在 `USER_AREA` 所属成员的外观广播之后且只发一次（换装重播外观不重复）；从不广播外观的成员不会产生加入增量；新人名单自己在前、老人在后；外观只发给别人不回发本人；位置按 uid 与方向转发且不回发本人；聊天按区域广播（跨区收不到）；同一角色换区为迁移（旧区收到 `USER_AREA(active=0)`，迁移后新区仍会补一次「外观 → 加入」，旧 ticket 不能再发包或改状态，空区释放后可重新进入）；投递失败的坏通道成员被移除、健康成员保留；`RosterResend` 信号发出的名单不含离开者；`TownSync.Enabled=false` 时目录完全不动作。
- 并发：同一角色在两个区域间反复并发进入，任何时刻只登记在一个区域且一定可达。
- 幽灵：名字编码长度边界；外观为 mode-zero `USERINFO` 且携带幽灵 uid 与配置名字；服务级观察者测试（真实后台服务 + 假通道）断言区域里的观察者能收到幽灵广播的外观，停止后释放 id。

**无头冒烟（幽灵开启，未接客户端）**

服务端发布件以幽灵（1/1、300ms、出现 3 秒/消失 2 秒）运行约 16 秒：日志按周期出现 `Town ghost is ENABLED`、`Town ghost TEST (uid 100) published its appearance.`、`Town member TEST (uid 100) entered 1/1`、`Town area 1/1 is now empty and was released.`，进程无异常退出。这只证明服务端循环、外观构建与目录进出正常，**不证明客户端能看到幽灵**。

**真机验收（Task 11，本机双客户端实测）**

测试环境：本机 `G:/Game/DNF_ACT1/DF/town-test`（分支代码发布，存档为 `dist` 存档的副本）；两个真实客户端同机运行——`test`/`圣骑士` 与 `shizuku`/`mageco`，两人走到同一城镇同一区域（城镇 1 区域 0，林纳斯一带）。**服务端侧证据**：日志逐包确认向两个会话投递了同区 2 人名单、对方 `USERINFO` mode-0 外观记录、对方 `USER_POSITION` 与区域对端表；**客户端侧证据**：两边画面同时出现对方角色并显示名字，聊天双向显示正确说话人。

| 项 | 内容 | 结论 |
| --- | --- | --- |
| A1 | 单人零回归（关幽灵：登录 → 进镇 → 走动 → 换区 → 下副本 → 打完一局 → 回城 → 换装 → 聊天） | 待验证 |
| A2 | 两人同镇同区互相可见（角色、名字、装备、宠物） | ✅ 已验证（修正「外观必须先于区域条目」后，两个客户端互相可见且名字正确） |
| A3 | 同区普通聊天与宠物喊话互通且显示发送者名字 | ✅ 已验证（双向互通，显示 `mageco:` / `圣骑士:` 正确说话人） |
| A4 | 一人走动、另一人看到位置跟随（1 Hz 允许一跳一跳） | 待验证 |
| A5 | 进副本 / 换区 / 返回角色选择 / 直接关客户端后，另一方数秒内不再看到该角色 | 部分通过（**下线场景已实测**：对方角色立即消失、无残留特效；进副本/换区/返回选人待补测） |
| A6 | 不同区域（同镇）与不同城镇互不可见、聊天不互通 | 部分观察（一人在赛利亚房间=区域 1、另一人在城镇广场=区域 0 时互不可见，符合预期） |
| A7 | 结论与证据落盘 | 部分完成（本表随验收推进更新） |

**待定项（Task 11 判定与兜底）**

- **S1 客户端是否渲染同区他人的角色**：✅ 已验证成立。根因是顺序（`USERINFO` 记录必须先于该 uid 的 `AREA_USERS`/`USER_AREA`，见上文「逆向确认的客户端顺序要求」）；两个真实客户端同区实测互相可见。排查过程中排除的因素：区用户 id 大小（uid 1 与 2 都受支持）、UDP 对端表（下发与否都不影响建角色，客户端只用它填端点）。
- **S2 客户端是否接受 TCP 转发的 `USER_POSITION`**（能看到别人走动）：待验证。若完全不动，判定为客户端不认 TCP 位置包，位置同步转下一轮的 UDP 中枢。
- **S3 离开信号**：✅ 已定论（逆向 + 真机）——移除信号是 **`USER_AREA` 的 `(town, area)` 填 `0xFF/0xFF` 哨兵**：客户端只有在该值与它当前区域**不一致**时才递归销毁角色及其显示子对象（`mapMgr->RemoveCreature`）；`active=0` **只是隐藏**（特效残留、重登后仍显示旧位置），**重发名单永远不能移除**角色。原先"两种信号二选一"的兜底阶梯已被推翻，实现与文档均已按新结论更新。

**已知限制**

- 只在**同一城镇且同一区域**内互相可见；跨区域或跨城镇不可见属预期（原版客户端按区域渲染，跨区塞人坐标会落在当前地图之外）。
- 位置同步目前只走 TCP（`SET_USER_POSITION` 转发，每个成员每秒默认上限 10 条）；是否需要 UDP 中枢待真机按 S2 判定。
- 广播中途通道坏掉的成员会被静默移除，且不会向同区补发离开信号：同区其他人可能保留其静止形象，直到自己换区才消失。
- 私密（单人实例）区域可配置，默认 `["1:1"]` 即赛利亚房间：同一区域里的玩家也互不可见、聊天不互通，与原作一致；未配置的区域行为不变。
- 区域目录与 id 池都在内存中，不支持多实例（与现有 JSON 存档一致）。

### 2.4 好友系统（本次新增）

账号级好友名单（上限 **32**，即客户端好友窗口容量）：加/删好友为**单向、立即生效**（只改自己账号的名单，不通知对方；协议里没有申请/同意这一层）。选中角色后**先给在线好友各推一条 `USERINFO` 记录、再推一条 `NOTI 56` 列表快照**（顺序不可颠倒：客户端在追加条目时按名字解析"用户对象"，记录晚到则该行没有用户对象，"查看信息"就点不动）；快照每条 42 字节，其中 **`0x21` u16 = 等级、`0x23` u8 = 职业（客户端有 `<5` 校验）、`0x24` = 打包成长/觉醒（低半字节 GrowType、高半字节 AwakeningType）、`0x25`/`0x29` 保持 0**——离线好友没有本地记录，列表就靠这几个字段显示等级/职业。加/删好友走 **`type=1` 命令通道**（`id=63` 追加条目、`id=64` 擦除）：加入包 = `[非0][u32 名字长度+名字][A=等级][B=职业][C=打包成长][u32 D=在线 uid 或 0][E=0][F=0]`（14+名字长度；**少发字节会让客户端用 `0x30` 填充并因"槽位<5"拒绝追加**），删除包同形。**名单快照每个会话只能发一次**：客户端的插入函数不匹配、无条件追加，重复发送会在列表里堆出同名重复行（这是"加了好友要重登才出现"与"重复好友行"两个问题的根因）。好友上下线用 `NOTI 54/55`：**下线发两条 `BUDDY_OUT`（先 `flag=0` 触发"[X]已退出游戏"聊天提示，再 `flag=1` 把该行置为灰色"未连接"）**。角色间私聊（悄悄话）走 `CMD 17 SEND_MESSAGE` 类型 1/7（正文后追加长度前缀的目标角色名）：接收方只收 `NOTI 12`（主聊天窗一行）+ 发送者的 `USERINFO` 前置（接收端按 uid 在本地用户表反查说话人）；**发送方收不到任何包**（客户端本地组装后在"自 uid"分支丢弃，属客户端行为）；`NOTI 65`（type 0x12、flag 0）**只用于 1:1 窗口流量**（`CMD 17` 类型 18）——此前用它给悄悄话兜底，会让悄悄话内容混进 1:1 窗口。**"查看信息"**（好友行菜单与城镇右键路人共用）由客户端发 `CMD 8 (uid, mode=3)`，服务端回 **`NOTI 2 mode=3`**：只有 mode-3 的 28 字节尾会打开资料卡窗口（回 mode 1 只更新模型、不弹窗）；卡片渲染目标的**装备**（未列出的槽会被删掉），**名字取自客户端本地用户表**（记录本身不带名字），**经验在 `u16 uid` 之后的 u32**。**黑名单**（账号级、上限 **10**、名字比较**大小写敏感**，与好友名单相反）走 `type=1` 命令 `128/129/130`，服务端在悄悄话与 1:1 上做**双向拦截**（发起者被对方拉黑时**完全静默**，只有自己名单命中才提示发起者）。名单存 `AccountRecord.Friends` / `AccountRecord.Blacklist`（老存档缺字段按空读取）；在线状态以 `FriendService` 的内存会话表（角色名 → 会话，`OrdinalIgnoreCase`）为准：选角成功时挂载，返回选人、切换角色、背包修复失败、断线四条路径摘除。未实现：离线好友申请、备注/分组、后台管理面。

**协议依据（静态分析 + 六轮真机迭代；报告见 `.superpowers/sdd/`）**

`.superpowers/sdd/friends-protocol-recon.md`（对 `DF/DNF.exe` 1.0.1.9 的静态分析，含证据地址）。用到的客户端 id：`CMD 63 ADD_FRIEND` / `CMD 64 REMOVE_FRIEND`（`u32 名字长度 + CP936 名字`）；`NOTI 56 BUDDY_CONN_LIST`（列表快照，**唯一能新建条目**的通知）、`NOTI 54 BUDDY_IN` / `NOTI 55 BUDDY_OUT`（只能更新已有条目）；`NOTI 52 FRIEND_INFO` 在本版本客户端是空实现，**未使用**；悄悄话是 `CMD 17` 类型 1/7 加尾部目标名，投递用 `NOTI 12`。id 与客户端限制（上限 32、名字 ≤30 字节、8 行/页、客户端**没有**"请求列表"命令）来自客户端的通知名表 `0xB54170` / 命令名表 `0xB54430` 及对应处理函数。

**自动化（全绿，随每个实现任务运行）**

`dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` 中的 `FriendSmokeTests` 覆盖：

- 协议字节级：`NOTI 56` = 1 字节数量 + 每条 42 字节（状态/标志/保留、30 字节 NUL 填充名字、五个未知字段全 0）；`NOTI 54/55` 布局；`CMD 63/64` 解析边界（长度 0、>30、内嵌 NUL、截断全部拒绝）；悄悄话尾缀解析（长度前缀目标名，截断拒绝）。
- 账号级存储：新账号空名单；加/删往返与**落盘后重载**；上限边界（前 32 个接受、第 33 个 `FriendListFull`）；名字 trim、保留调用方大小写、匹配不区分大小写；加自己/重复/角色不存在/账号不存在/删非好友各自返回对应失败；加好友只改调用方账号（**单向**）。
- `FriendService`（假通道 + 真实 store）：登录先给在线好友各推一条 `USERINFO` 记录、再推一条快照并对离线好友补 `BUDDY_OUT`（空名单也推空快照，用于清掉同一客户端进程里上一次登录的残留条目）；`BUDDY_IN`/`BUDDY_OUT` 只发给"名单里含该名字"的账号（无关账号静默）；会话 id 不匹配的摘除完全不动作（陈旧清理不会误删新会话）；加好友发 type-1 id63（补全 14+名字长度字节，在线好友带 uid）+ 按在线与否的状态更新 + 在线时该好友的 `USERINFO`，删好友发 type-1 id64，加删均**不推快照**（`NOTI 56` 只增不减）；失败按 `找不到该角色 / 不能加自己 / 已经是好友 / 好友已满 / 不是好友 / 操作失败` 发白色 `NOTI 12` 且不改状态；悄悄话路由（在线只投递给目标：发送者外观包先于 `NOTI 12` 正文；发送方无任何回显——本地"给[X]"行被客户端自 uid 分支丢弃，属客户端行为；离线或目标会话已摘除→`对方不在线`、自己→`不能给自己发悄悄话`）；`NOTI 65` 只用于 `CMD 17` 类型 18 的真 1:1 消息。
- 会话接线（CMD 63/64 分支、悄悄话分流、选角挂载、返回选人/换角色/背包修复失败/断线四条路径摘除）没有独立断言，由全量冒烟回归覆盖既有行为；其端到端验证在 Task 6。

**无头冒烟（发布件启动，未接客户端）**

服务端发布件以非默认端口启动，`Entrance listener ready` 出现、日志无异常，证明 `FriendService` 的 DI 装配（构造依赖全部可解析）。这只覆盖服务端启动，不涉及客户端。

**真机验收（Task 6，本机双客户端实测，共四轮）**

测试环境沿用城镇同屏：本机 `G:/Game/DNF_ACT1/DF/town-test`，两个真实客户端 `test`/圣骑士 与 `shizuku`/mageco（好友功能不要求同镇同区）。

| 项 | 内容 | 结论 |
| --- | --- | --- |
| B1 | 加好友 → A 的列表出现 B 且在线状态正确 | ✅ 通过（第四轮修复后**当场进列表**并显示"[名字]已被添加到好友名单"；在线好友的新行显示在线、离线显示离线） |
| B2 | 单向性：B 侧名单无变化、也收不到通知 | ✅ 通过 |
| B3 | 上下线提醒：B 下线再上线 → A 看到状态变化；A 重新登录后快照状态仍正确 | ✅ 通过 |
| B4 | 双向私聊：文字与说话人（名字）都正确 | ✅ 通过（接收方在主聊天窗看到一行且说话人正确；**发送方自己那行客户端不显示**——本地组装后在自发分支被丢弃，属客户端行为） |
| B5 | 删除好友 → 列表条目消失 | ✅ 通过（第四轮修复后条目**真正消失**、可重新添加） |
| B6 | 失败提示：加不存在的名字 / 加自己 / 重复加 / 删非好友 → 白色聊天栏提示 | ✅ 通过 |
| B7 | A 返回角色选择（或切换角色）后，B 侧应看到 A 变为离线 | ✅ 通过 |
| B8 | 按现象填 F1–F5 的最终取值（见下表） | ✅ 完成（见下表） |
| B9 | **查看信息**（好友行菜单与城镇右键路人两种入口）弹出资料卡，含形象/装备/等级/经验 | ✅ 通过（第五轮实现；其中经验值在第六轮修正为"uid 之后的 u32 写目标存档经验"） |
| B10 | **黑名单**：拉黑 / 解除 / 打开名单窗口看到条目；悄悄话与 1:1 被拦截（被拉黑方完全静默） | ✅ 通过（第五轮；第六轮把"被拉黑方静默"落实为原版行为） |
| B11 | **离线好友的等级与职业正确**；好友下线后该行**即时变灰（未连接）**且保留"[X]已退出游戏"聊天提示 | ✅ 通过（第六轮：等级/职业取自 `R+0x21/0x23/0x24`；下线发两条 `BUDDY_OUT`；**提示与灰显两者同时出现已由真机确认**） |

**四轮验收中发现并修复的根因**（每条都有客户端二进制证据，报告在 `.superpowers/sdd/friends-*-recon.md`）：

1. **顺序**：新进区域的角色必须**先收在线好友的 `USERINFO` 记录、再收 `NOTI 56` 名单快照**——客户端在追加条目时按名字解析"用户对象"，记录晚到则该行没有用户对象（"查看信息"的门因此过不去）。
2. **`type=1/id=63` 载荷不能截断**：加好友结果包 = `[非0][u32 名字长度+名字][u8 A][u8 B][u8 C][u32 D][u8 E][u8 F]`（14+名字长度）；少发字节时客户端用 `0x30` 填充，触发它的"槽位 <5"校验而**拒绝追加条目**（表现为"加了好友要重登才出现"）。
3. **离开信号**：`USER_AREA` 的 `active=0` **只是隐藏**（特效残留、角色仍在）；只有包里的 `(town, area)` 与接收者当前区域**不匹配**（`0xFF/0xFF` 哨兵）才会递归销毁角色及其显示子对象。重发名单**永远不能移除**角色。
4. **好友名单只增不减**：客户端的插入函数**不匹配、无条件追加**——因此 `NOTI 56` **每个会话只能发一次**；加/删好友走命令通道（`type=1/id=63`、`id=64`），上下线只用 `NOTI 54/55`。此前每次加/删/上下线都重推快照，表现为列表里不断出现重复行。
5. **悄悄话不要发 1:1 通知**：接收方只需 `NOTI 12`（主聊天窗一行）+ 发送者的 `USERINFO` 前置；`NOTI 65`（type 0x12、flag 0）只用于真正的 1:1 窗口流量——此前用它给悄悄话兜底，导致悄悄话内容混进 1:1 窗口。

**待定项 F1–F5（Task 6 判定）**

| 编号 | 待确认 | 结论/当前取值 |
| --- | --- | --- |
| F1 | `NOTI 56` 记录里 `u16 / u8 槽位 / u8 打包 / u32 / u8` 五个字段的语义（猜测：等级/职业/位置等） | ✅ **已定论**：`0x21` u16 = **等级**、`0x23` u8 = **职业**（<5 校验）、`0x24` = **打包成长**（低 GrowType / 高 AwakeningType）、`0x25`/`0x29` = 0（资料卡另有字段）。此前"`0x21` 必须为 0"的结论是把 SecureInt 解码误读为字符串取值，**已作废** |
| F2 | 在线状态取值 | ✅ **0 = 在线**已实测确认（在线显示在线，`BUDDY_OUT` 后显示离线） |
| F3 | 私聊类型是 1 还是 7 | ✅ 双向往通（服务端按收到的类型原样投递） |
| F4 | 发送者 `USERINFO` 是否真的必需 | ✅ **必需**（接收方按 uid 反查名字） |
| F5 | 快照推送时机 | ✅ 选角成功后推一次即足够（客户端不在进世界时清空管理器）；**不可重发**（见根因 4） |

**尚未实现**

- **交易**：协议已勘察（`.superpowers/sdd/trade-protocol-recon.md`），服务端未实现。
- **组队、公会**：未开始（好友行的"邀请组队/交易"菜单项目前点了没反应，属预期）。

**已知限制**

- 同一账号只有**当前选中的角色**在线：好友的在线状态以在线会话表为准，账号未上线的角色一律按离线显示。
- 返回角色选择界面、切换角色、背包修复失败或断线时都会摘除会话并通知好友离线；这些路径在真实客户端里的可见行为仍待 B7 确认。
- 好友名单是**账号级**的：同一账号换角色登录看到同一份名单（显示名字仍是添加时保存的角色名）。
- `NOTI 56` 记录里五个语义未知的字段目前全部填 0：好友条目可见，但等级/职业等信息是否显示待 F1 确认。
- 好友名字最长 30 字节；服务端匹配不区分大小写，显示用添加时保存的名字。名单上限 32 由服务端独立校验，不依赖客户端拦截。
- 存档里记录的好友角色即使已被删除也会留在名单中并永久显示离线（本轮不做自动清理）。

### 2.5 交易系统（本次新增）

城镇内玩家对玩家交易，服务端全量实现：请求/应答开窗、**24 物品格双向 + 金币**、逐侧锁定、双方最终确认、原子结算，以及取消/超时/掉线/换区/进副本的**整笔回退**（托管期间服务端从未把物品搬离原背包，所以"回滚"只是释放内存标记，掉线/崩溃零丢失）。协议与实现依据：`.superpowers/sdd/trade-protocol-recon.md`（初版静态勘察）、`trade-protocol-recon-2.md`（真机暴露问题后的定向勘察）、`trade-weight-check-recon.md`（负重自检字段）、设计稿 `docs/superpowers/specs/2026-09-28-trade-system-design.md`、计划 `docs/superpowers/plans/2026-09-28-trade-system.md`。

**协议要点（含真机纠正后的最终结论）**

- 流程：城镇右键 → `CMD 10 REQUEST_PEER (u16 uid, u8 type=1, u32 weightDelta)` → 对方 `NOTI 7 (u16 requesterUid, u8 1, u32 weightDelta)` 弹应答窗 → 对方接受发 `CMD 11 (u16 requesterUid, u8 1, u32 weightDelta)` → **请求方收 `NOTI 8`**、**接受方收 `CMD 11` 成功包 `[1, uid, 1, mode]`**，两端开窗。小窗**【否】会发包**：`CMD 11` 同前缀 + 尾部 `u16 0x0064`（body 含 2B 序列号共 **11 字节**；**【是】是 9 字节**），服务端据此判拒绝（G 卷 `.superpowers/sdd/party-protocol-recon-g-response-buttons.md`）；交易拒绝走既有取消流程（请求方收 `CMD 11 [0,0x64,1]`、接受方零包、会话当场摘除），超时只兜底静默情形。
- **`CMD 10/11` 尾部的 u32 = 客户端自己的「当前负重 − 负重上限」（二补码）**，服务端**必须原样回传**到 `NOTI 7`（请求方的值）与 `NOTI 8`（接受方的值）：客户端把它写进对方 user 的 `CNUser+0xF4`，锁定前那条"交易对象的负重上限不足，还缺少 X kg"检查（`SetReady` 0x4623A0）就是 `[对方+0xF4] + 我收到重量 − 我给出重量 < 0`。旧结论"这是客户端时间差/防抖字段"与"NOTI 7 的 X 写进 `user+0xC`"**均已作废**（反汇编证据见 `trade-weight-check-recon.md`）。
- 放入物品：`CMD 19 MOVE_ITEMSPACE`（与背包移动**同一布局**；`Body[5..6]` 是客户端自报的 itemId，仅用于日志）。**客户端的 `destTradeSlot` 恒为 0**（槽位分配权在服务端，`0x4627A3: xor esi,esi`）→ 服务端分配 `3..26` 的最小空槽（客户端给的槽合法且空闲时采纳），该槽号统一用于 CMD 19 成功回包、对端 `NOTI 15`、托管表，并自然派生 `NOTI 16/18`。
- **CMD 19 成功回包 = `[1, u8 srcType, u16 srcSlot, u32 count, u8 destType, u16 destSlot]`（11 字节）**，操作者面板只由它驱动（客户端不本地插入）；**失败回包 = `[0, u8 errorCode, u8 srcType, u8 destType]`（4 字节，不回显槽/数量）**，错误码走客户端 switch（`0x11` 物品不存在/数量不足、`0x12/0x17` 栏中有物、`0x13` 无权限、`0x5E` 超出对方金币上限）——发错形状（例如回显式 11 字节）会让客户端读成 `errorCode=0` 而**完全静默**。
- 金币：点面板金币行或拖金币图标都走 `CMD 19`（`srcType=0, srcSlot=0, itemId=0, count=金额, destType=4, destSlot=0`），面板槽 0；金额必须 ≤ 服务端记录的金币，重复放入按"覆盖"。
- `NOTI 15 (u16 slot, u16 itemId, u32 count, u8 state, u16 durability；itemId=0xFFFF 清格)` 的槽域 = **面板内部下标**（0=金币、1/2 保留、3..26 物品）；`NOTI 16`（`u16 count` + N×`u16 面板槽, u8 原栏, u16 原格`）与 `NOTI 18`（N×`u16 对方面板槽, u16 我的背包格`）同域。
- `CMD 26`：0 解锁 / 1 锁定 / 2 取消 / 3 最终确认（需**双方都已锁定**，否则回 `[0,1]`）/ 4 含义未定（忽略并记日志）；**双方都确认**后由后确认方结算。`NOTI 17 (u16 uid, u8 lock)` 的 uid = 变更方（客户端按 uid 决定写 myLock 还是 otherLock）。
- `protocol 161 = ENUM_CMDPACKET_ANTIBOT`：TerSafe 反外挂插件的 **60 秒心跳**（每客户端一条，与金币无关），服务端静默并把日志降到 Debug；160..162 同处理。

**复制防线（本系统的第一原则，代码里逐条可查）**

1. **只信服务端自己的 `(srcType, srcSlot)` 记录**：CMD 19 里的 itemId/count/金币只做校验与钳制（`ItemTradePlanner.TryPlaceEscrow`），托管快照存服务端记录（含 `InstanceId`）。
2. **结算原子**：`JsonGameStore.CommitItemTradeAsync` 在同一把 `_gate` 内"读完双方 → 计划（负重/空位/金币充足性/托管复核）→ 应用 → 一次 `SaveLockedAsync`"；任一步失败**不写盘**；保存抛异常时双方内存记录回滚并重新抛出（落盘是 `state.json.tmp` + `File.Move` 原子替换）。
3. **托管期冻结**：放入面板的物品仍留在原主背包，但 9 条写入路径（CMD 19 主路径、反向编码拖拽、丢弃、出售、删除、邮寄、合成、分解、整理）命中即按该路径既有失败回包拒绝；结算时再按 `InstanceId + 源格 + 数量 + State/Durability` 复核，不符即整笔中止。
4. **跨会话一致性**：结算后给被动方登记"待安装"，由它在会话循环顶部安装并推权威背包+金币；在确认安装之前，`JsonGameStore` 的 **15 处盲写型 API** 拒绝该角色的写入（`_pendingTradeDeliveries` 守门），杜绝"被动方用陈旧内存覆盖交易结果"。结算成功时双方各自拿到 `NOTI 18` + 金币 `NOTI 14` + 权威全量 `NOTI 13`。

**自动化（全绿，随每个实现任务运行）**

`dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` 中的 `TradeSmokeTests` 覆盖：协议字节级（NOTI 7/8 的 uid+type+负重差、NOTI 15/16/17/18 布局、CMD 11 成功/失败、CMD 19 成功 11 字节与失败 4 字节、CMD 26 结果、三个 `TryRead*` 的截断与负数、`TryReadInventoryMove` 的 itemId 字段）；`ItemTradePlanner` 纯逻辑（托管校验/取回/金币覆盖/结算计划、InstanceId+State/Durability 复核、负重/空位/金币充足性、面板槽分配、错误码映射）；`JsonGameStore`（双方一次落库的原子性、失败不写盘、保存抛异常回滚、15 处盲写守门与确认后可写）；`ItemTradeService`（请求→NOTI 7→接受→NOTI 8/CMD 11、自请求/未知 uid/离线/不同镇/已在交易中/被拉黑静默 30 秒同形失败/限流、放入回显+对端 NOTI 15+锁定清零、金币、取回、面板槽分配与采纳、锁定广播、单方确认不结算、双方确认结算并落库、失败码 `[0,4]`/`[0,1]`、并发确认只结算一次、取消/空闲超时/掉线清标记与待安装自愈、掉线窗口哨兵）。

**真机验收（本机双客户端；环境 `G:/Game/DNF_ACT1/DF/town-test`，日志 `server-trade.log`，`EnablePacketTracing=true`）**

| 项 | 内容 | 结论 |
| --- | --- | --- |
| T1 | 请求 → 对方应答窗 → 接受 → 双方开窗 | ✅ 日志 `Trade … requested …` / `opened …` |
| T2 | 双向放物（服务端分配面板槽） | ✅ `escrowed item 1004 x3 from slot 43 into panel slot 3`、`3037 x10 from slot 73 into panel slot 3`；面板实时可见 |
| T3 | 取回 | ✅ CMD 19 `srcType=4` 回包与对端 `NOTI 15 itemId=0xFFFF` 成对 |
| T4 | 金币放入/修改 | ✅ 回包 `[1,0,0,10000,4,0]` + 对端 `NOTI 15 slot=0,itemId=0,count=10000` |
| T5 | 锁定（含客户端"对方负重上限"自检） | ✅ 负重差原样回传后锁定包到达、双方 `NOTI 17` 同步 |
| T6 | 双方最终确认 → 结算交付 | ✅ `Committed item trade … 1/1 item(s)` + 双方 `NOTI 18` + `NOTI 14`/`NOTI 13` 权威刷新 |
| T7 | 面板为空时确认 | ✅ 正确结算为 0 件（早期一轮日志） |
| T8 | 小窗【否】→ 不成立交易（11 字节带 `64 00`；静默情形仍靠 30 秒超时失败提示） | 未逐条真机复验（拒绝与超时逻辑已由自动化覆盖） |
| T9 | 取消 / 掉线 / 进副本 / 换区 / 一人一笔 / 冻结抽查（V8–V12） | 未逐条复验；取消与掉线-结算窗口有自动化哨兵，冻结路径由自动化 + 结算复核双保险 |

**六轮验收中发现并修复的根因**（每条都有反汇编/日志证据）：

1. **放入的目标槽由服务端分配**：客户端 `destSlot` 恒 0，此前按 3..26 校验导致每次放入都被拒（`PanelSlotOutOfRange`），面板毫无变化。
2. **CMD 19 失败回包形状**：客户端只读 `[errorCode, srcType, destType]` 4 字节；回显式 11 字节让它读成 `errorCode=0` → 一字不弹（"静默失败"）。
3. **CMD 10/11 的负重差必须回传**：NOTI 7 填 uid、NOTI 8 填 0 会让客户端的"对方负重上限"检查恒失败（"还缺少 0.001kg"），且**锁定包根本不发**。
4. 顺带确认 `protocol 161` 是反外挂心跳（不是金币命令），金币走的一直是 CMD 19。

**已知限制**

- 放入**只接受主背包**（`srcType==0`）：仓库/时装/宠物/装备栏拖入一律拒绝；取回与取消统一退回主背包（托管记录已存源栏位，将来放开只需放宽校验）。
- 结算交付**只落空格、不叠堆**（避免猜客户端"同类物品"的合并判据），收货方类别段满了就整笔失败并提示"交易对象的物品栏空间不足"。
- **不做等级金币上限**（只防 `int` 溢出）；`NOTI 51` 与 `CMD 26` 的 0x46/0x47 未使用。
- `CMD 26` 的 state 4 忽略（语义未定）；取回不校验目的栏位（服务端不搬物，回显统一主背包）。
- 锁定广播每次每侧发 1 条 `NOTI 17`（设计稿读作 2 条，功能等价：客户端只需知道"该方已锁定"）。
- `SetUserAreaCommand`（38）到达即取消交易、不比较新旧区域：同区重载若也发此包会误取消（真机未观察到；出现再加差异判断）。
- `EntranceService` 侧的通道挂载/摘除、离区取消、待安装安装、冻结判定没有会话级自动化 harness（只有服务层与规划器的单测），这几条只能靠真机复验。
- 设计稿 §2.4 的"非私密区域"子句用现有 `TownSessionDirectory` API 无法实现（快照不含 Instance）；可达性低（私密区是单人实例）。

### 2.6 组队系统（本次新增）

城镇内组队，服务端全量实现：城镇右键"邀请组队"与列表"申请加入"（**同一个包** `CMD 10 peerType=0`）→ 对方应答窗 → 接受 → 建队/并入 → 4 人名册（成员槽、队长、预置队名）；退出/踢人/掉线/切换角色/切换频道的移除与**队长自动继任**；进副本、回城、换城镇、城镇内换区都**不退队**；「队伍信息」列表与「推荐伙伴」所依赖的数据前提（名册按区域广播 + 进区补发）一并落地。协议与实现依据：`.superpowers/sdd/party-protocol-recon.md`（合并卷，含对 A 卷的两条勘误）与四份分卷 `party-protocol-recon-a-core.md` / `-b-membership.md` / `-c-partylist.md` / `-d-recommend.md`、设计稿 `docs/superpowers/specs/2026-09-28-party-system-design.md`、计划 `docs/superpowers/plans/2026-09-28-party-system.md`。

**当前状态（诚实标注）**：代码在分支 `feat/party`。提交锚点（**按提交号记，不记总数**——总数会随每次文档提交变化）：代码提交 8 个 `c76c142^..a79c548`（`A..B` 记法不含 A，字面只有 7 个）（**实测：`git log --oneline main..feat/party | wc -l` = 9、`git rev-parse --short HEAD` = `c859137`**），本次修正提交（curl 调用形状 / 分支状态 / 顺序前提）又在其上，**终局全分支评审修复提交 `7803c62`**（进区名册时序 / 死代码清理 / 请求竞态与覆盖）再在其上（`git rev-parse --short HEAD` 以实际为准）。代码通过逐任务评审，`PartySmokeTests` 全绿；**真机双客户端验收进行中**（2026-09-29，已确认项见下表）。验收环境由发布件现启：`dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -o /g/Game/DNF_ACT1/DF/town-test` → 还原 `server.json` → 在 `DF/town-test` 启动（完整命令与核对项见本节"无头冒烟"与 `docs/superpowers/handoff-party.md`），PID 以实际为准。

**协议要点（含本轮勘察勘误与硬约束，真机最容易踩的就是这几条）**

- 流程：`CMD 10 (u16 目标 uid, u8 peerType=0, u32 复活币)` → 目标收 `NOTI 7 (u16 请求者 uid, u8 0, u32 复活币, u16 疲劳, u16 虚弱%)` 自动弹应答窗 → 对方接受发 `CMD 11 (u16 请求者 uid, u8 0, u32 …)` → **双方各收一条 `NOTI 9 kind=0`**，请求方另收 `NOTI 8 (u16 请求者 uid, u8 0, u32 0)`（7 字节，只为在聊天栏打一行"已接受组队请求"）。邀请与"申请加入"发的是同一个包（后者由客户端把目标填成**队长 uid**），服务端不区分两者，只按"目标此刻的队伍状态"决定语义（双方都有队 → 静默；目标有队且是队长 → 请求者并入；请求者有队、目标无队 → 目标并入；都无队 → 新建队，请求者为队长；目标在队里但不是队长 → 静默）。小窗**【否】会发包**：`CMD 11` 同前缀 + 尾部 `u16 0x0064`（body 含 2B 序列号共 **11 字节**；**【是】是 9 字节**），服务端据此判拒绝（G 卷 `.superpowers/sdd/party-protocol-recon-g-response-buttons.md`）→ 组队拒绝**立刻**给请求方 `CMD 11 [0,100,0]`（不建队、零广播）；超时只兜底静默情形（决斗 2/3 点【否】不发包、请求方在窗口内离线/守卫失败、窗口计时器到点静默关窗）。
- **`NOTI 9` 记录里的 `uid` = 队伍 id（partyId），不是"收包者自己的 uid"**（对 A 卷的勘误，由 C 卷证据推翻）：客户端「队伍信息」列表的行数据与去重键就是它 —— 同一支队伍必须对所有收包者用**同一个 u16**，否则旁观者侧的多支队伍会被客户端合并成一支、列表直接错乱。取值 = 建队时队长的角色 uid，队伍存续期间**不随换队长改变**（被占用时退化为当前队长 uid，再冲突则扫描一个未占用的 u16）。
- **4 个槽位是 `{u16 slotUid; u8 memberByte}` 交错**（`slotUid[0], memberByte[0], slotUid[1], …`），**不是两个独立数组**——这是对 A 卷的第二条勘误。`kind=0` 记录因此长 18 字节、整条 body 20 字节（`recordCount=1`）；`kind=3`（解散/退出）只有 `u16 count + u16 uid + u8 kind` 共 5 字节、不带名册。`memberByte` 语义未知（二进制里无读点）→ 恒 0。
- **空槽填 `0xFFFF`；非空槽永远填成员的真实 uid——"收包者在本地解析不到的成员（异区域队友）"填该成员自己的 uid，绝不填 `0`**（2026-09-28 真机 bug 修复后的规则）：客户端拿槽位 uid 去**本区域**用户表里找成员对象，找得到才建立"该成员属于这支队伍"的链接与显示。真机证明 **`0` 会被客户端当成"这个槽没有人 / 该成员已离开"**：旧实现给异区域成员填 0，两人同城镇组队后其中一人换区 → **两端同时**弹"队伍中只剩下[X]。要继续保留队伍吗？"并各自摘掉对方，队伍退化成两个单人队。所以解析不到时改填**该成员自己的 uid**（那一行只是显示空白，成员仍在队伍里；跨区域 uid 可能撞号、理论上显示成同区另一位玩家的名字，见"已知限制"）。同一份名册按**收包者所在区域**生成不同版本（只有 uid 值被替换，**槽位顺序在所有版本里保持一致**——`CMD 14` 踢人用槽位下标，队长客户端的槽位必须与服务端权威名册对齐）。两个哨兵必须区分：空槽只能是 `0xFFFF`，`0` 不再是合法槽位值。
- **`NOTI 10 [槽位, kind]`（0=被踢 / 1=掉线）必须早于清掉该槽的 `NOTI 9`**：它的 kind 0/1 分支用**槽位下标**去查成员对象取名字，而 `NOTI 9` 会把该槽写成 `0xFFFF` 并清掉成员的队伍指针；顺序反了会查不到成员（无提示或名字为空）。
- **不要发 `CMD 11` 成功包**：客户端组队分支要求 `peerType==1`（交易）才开窗，`peerType=0` 的成功包被直接丢弃——建队与开窗**只能靠 `NOTI 9`**。
- **失败一律 `CMD 11 [0,100,0]`**（请求方与接受方都是）：客户端 `CMD 11` 分支按 `body[1]` 当 peerType 分流，填别的 errorCode（如 19）是未知 peerType → **发错形状客户端静默**。只有 `CMD 13/14` 的失败才是 `[0, code]`（退出 18/19、踢人 4/19/8；**19 本轮发不出，只发 18/4/8**——"取到名册后又消失"的并发窗口在锁内一次判完，静默收场）。
- **`NOTI 9 kind=0` 必须广播给同区域所有客户端（含不在这支队伍里的旁观者），客户端进区还要补发**：旁观者 user 记录的"此人有队伍"链接（`user+0xFC`）就是靠它建立的——这是「队伍信息」列表能列出别人的队伍、以及「推荐伙伴」能把已有队伍的人过滤掉的前提；不广播则列表永远空、推荐伙伴会把有队伍的人推出来。解散时对**所有通知过的区域**发 `kind=3` 清场。
- **槽位下标稳定、不搬动**；队长用 `leaderSlot` 表达（可 0..3）：队长离开/掉线/被摘除时自动继任 = **最先入队的剩余成员**（槽下标最小者）**就地**接任（只改 `leaderSlot`），`partyId` 不变。（搬动成员会让客户端逐槽 clear-then-set 时把被搬者误判成"已离队"——幽灵队伍根因。）
- `NOTI 7` 的三个显示值：复活币取客户端 `CMD 10` 的自报值（原样透传）、疲劳取**剩余疲劳**（上限 − 已耗，含黑钻加成）、虚弱% = `100 − 当前耐力值`（由服务端耐力状态换算；未虚弱/黑钻 = 0）。旧实现填的是已耗疲劳与固定 0，真机数值对不上已修正（自动化钉住，真机项 P12）。
- `CMD 12 SET_PARTY_INFO (u8 sel, [sel==0 时 u32 长度 + 名字], u8 上限)`：**没有队伍 → 以发起者为队长、`partyId` = 其 uid 新建 1 人队伍**；已有队伍且发起者是队长 → 改预置名/上限；非队长忽略。`sel` 是 1-based 的**6 项预置队名**下标，`0` = 自定义名；**出站边界把 `sel==0` 规范化为预置名第 1 项**（名册存储保留原值）——`nameSel=0` 的名册在真机上完全无效，代价是自定义队名对所有客户端（含创建者）都显示为第 1 项预置名。上限钳到 `[当前人数, 4]`。

**自动化（全绿，随每个实现任务运行）**

`dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` 中的 `PartySmokeTests` 覆盖（测试台 = 真 `JsonGameStore` + 真 `TownSessionDirectory` + 真 `PartyService`，只把发包换成写进 List）：

- 协议字节级：`NOTI 9` 的 20 字节 body（`uid`=partyId、`slotUid/memberByte` **交错**、空槽 `0xFFFF`（构建器对非空槽的值不做解释；服务端任何路径都不再产生 `0`）、`leaderSlot` 收尾）、解散 `kind=3` 的 5 字节、`NOTI 10` 的 `[槽位,0]`/`[槽位,1]`、`NOTI 7` 11 字节（uid/peerType=0/复活币/疲劳/虚弱%）、`NOTI 8` 7 字节、`CMD 13/14` 失败回执 `[0, code]`；`CMD 10` 解析**按 peerType 分流**（回归：`peerType==1` 的交易包不被组队分支吞掉）与截断拒绝、`CMD 11` 组队分支、`CMD 12` 只在 `sel==0` 读名字串/名字长度越界拒绝/`sel!=0` 无尾串。
- `PartyRoster` 纯逻辑：新队只有队长在槽 0；上限 4（第 4 人进队、第 5 人被拒）；**槽位下标稳定、不搬动**（新成员填最小空闲槽，移除只清他自己那格）且 `SlotUids` 按收包者区域解析（同区域填真实 uid、**解析不到填该成员自己的 uid——回归断言：3 人队 `SlotUids(_ => null)` 必须是 `[11,12,13,0xFFFF]`、非空槽绝不出现 0**、空槽 `0xFFFF`，**所有区域版本槽下标一致**）；移除队长后**就地继任**（`leaderSlot` = 槽下标最小的剩余成员，名册不重排）；转交队长（`TryPromoteLeader`）只改 `leaderSlot`、不搬动；`SetInfo` 改预置名且**上限不降到人数以下**；`UpdateLocation` 后只有新区域能解析到他；`partyId` 分配跳过已占用值与 preferred 不可用时的扫描。
- `PartyService`：邀请闭环（`NOTI 7` → 接受 → 双方 `NOTI 9` + 请求方 `NOTI 8`，**接受方没有 `NOTI 8`**）；"申请加入"（目标为队长 → 请求者并入、`partyId` 与队长不变、追加在加入顺序之后）；拒绝路径（自请求、3 秒限流、目标已有待决请求时后来者**立刻**收失败且目标只留一个窗）；**接受方失败也是 `CMD 11 [0,100,0]`**；重复（并发）接受幂等（只产出一套名册 + 一条 `NOTI 8`，绝不冒失败回执）；30 秒超时与黑名单（目标零感知、发起方同形失败）；踢人（`NOTI 10` 早于 `NOTI 9`、被踢槽变 `0xFFFF`）、最后一人退队 `kind=3`、断线（其余成员收 `NOTI 10 [槽位,1]`、掉线会话收不到任何包）；`CMD 12` 新建 1 人队/改上限/非队长忽略；**区域广播覆盖旁观者**（同区旁观者收到与成员同 `partyId` 的名册、同镇另一区域收不到）、换区（旧区域版本把离场成员填**他自己的 uid**、新区域版本填其真实 uid；**跨区域回归 `CheckCrossAreaRosterKeepsBothMembersAsync`：发给两端的名册里对方那一格都不是 0、非空槽（人数）仍是 2**）、**进区补发**（队伍先建好、旁观者后进区仍看得见）、离区（进副本）**不退队**且各区域版本填成员自己的 uid（绝不填 0）、解散对旁观者发 `kind=3`。

**无头冒烟（发布件启动，未接客户端，2026-09-28）**

`dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -o /g/Game/DNF_ACT1/DF/town-test` → 还原 `server.json`（发布件会用仓库样例覆盖）→ 在 `DF/town-test` 启动 `./DFLegacy.Server.exe > server-party.log 2>&1`。实见：`Indexed 48343 script files`、`Entrance listener ready at 127.0.0.1:2311 / 7001 / 8080`（另有 `CharacterDatagramService` 2311、`GameplayDatagramService` 7002/7003）、后台 `http://127.0.0.1:8081` 就绪；整份启动日志**无 `Exception`/`error:`**（既无未处理异常也无启动期告警）。登录接口用 `curl -X POST http://127.0.0.1:8081/api/auth/login -d '{"userName":"test","password":"test"}'` 返回 **415**——curl 的 `-d` 默认发 `application/x-www-form-urlencoded`，补 `-H "Content-Type: application/json"` 后返回 **200** 并下发了 token（登录路径本身正常，是取 HTTP 码时容易踩的坑）。该次冒烟后服务端保持运行供双客户端验收（**PID 以实际为准**，易过期；验收时按上面的命令现启即可）；本次冒烟只证明服务端装配与启动，不涉及客户端。

**真机验收（本机双客户端，2026-09-29 进行中；环境 `G:/Game/DNF_ACT1/DF/town-test`，日志 `server-party.log`，`EnablePacketTracing=true`）**

账号 `test`/`test`（圣骑士）与 `shizuku`/`shizuku`（mageco），两人都在城镇 1 区域 0。读日志用 `grep -a`（日志含 NUL 字节会被判为二进制）：`grep -a "protocol=9 \|protocol=10 \|protocol=7 \|protocol=8 \|Party " server-party.log | tail -40`。

| 项 | 内容 | 结论 |
| --- | --- | --- |
| P1 | A 右键 B → 邀请组队 → B 应答窗（含复活币/疲劳/虚弱%）→ 确定 | ✅ 已验证（2026-09-29）：接受即出现队伍（应答窗三个显示值见 P12） |
| P2 | 找到组队窗本身的打开方式（试热键、右键自身菜单、聊天栏可点行） | 待验证（属**探索项**：客户端二进制没钉死开窗触发者；已知子窗入口是组队窗上的按钮——推荐伙伴 `<[>`、队伍信息 `<]>`） |
| P3 | 队长在队伍窗点某成员 → 强行踢除 | ✅ 已验证（2026-09-29） |
| P4 | B 直接关客户端 | 待验证（期望：A 侧"网络连接不稳定…"提示且名单刷新） |
| P5 | 队长点"退出队伍" | ✅ 已验证（2026-09-29）：剩余队员变队长，并出现"是否保留队伍"提示（`leaderSlot` 就地转移、`partyId` 不变） |
| P6 | 2 人队里 1 人退出 / 最后 1 人退出 | 待验证（期望："已退出队伍" / "队伍已解散"） |
| P7 | 组队窗的"新建队伍"入口（若找得到）选预置队名 | 待验证（期望：队名在双方窗口/列表里一致；**自定义名出站降级为预置名第 1 项**，见"已知限制"） |
| P8 | 与交易并发、单人零回归 | 待验证（期望：交易照常；无队伍时无任何新包） |
| P9 | B 进副本 → 回城 | 待验证（期望：**队伍仍在**（用户决策）；回到同区域后队友行恢复显示名字等级） |
| P10 | 应答窗点【否】（小窗是/否区分；交易侧同理） | ✅ 已验证（2026-09-29）：点【否】**不**建队/不成交，请求方收到"对方不在线，或拒绝了您的…"提示；点【是】照常 |
| P11 | 队伍窗把某成员设为队长（委任队长 → `CMD 131`） | ✅ 已验证（2026-09-29）：队长标记转到对方、双方界面同步；槽位不重排 |
| P12 | 应答窗的疲劳与虚弱% 显示值 | ✅ 已验证（2026-09-29）：数值与客户端界面一致（剩余疲劳 / 虚弱%） |
| P13 | **跨区域队伍不散**：两人在同一城镇区域组队后，其中一人走到同城镇的另一个区域 | ✅ 已验证（2026-09-29）：两端都不再弹"队伍中只剩下[X]"、队伍保留；异区域那一行空白（修复前此处必然散队——旧实现给异区域成员填 0，客户端把 0 当成"该成员已离开"） |
| P14 | 队长进副本（队员留在城镇） | ⚠️ 部分观察（2026-09-29）：队员**不会**被带进副本（本轮设计：副本是单人实例，同队进副本属第二轮）；**队伍状态保持**（队员在城镇仍看到 2 人、队长侧名册里两个槽都是真实 uid、无"已离队"提示）；**副本内队长侧只画得出自己那一行**——因为该客户端的本地用户表在副本实例里没有队友的记录，行画不出来（属同一条限制，第二轮共享副本实例后自然消失）。出副本回城后那一行**已复验恢复**（2026-09-29） |
| L1 | B 打开「队伍信息」（热键 `]`） | ✅ 已验证（2026-09-29）：窗口可打开（列表内容另见下方"列表为空先查发包时序"——名册必须晚于对方的 `USERINFO`/区域条目，见"已知限制"与设计稿 §6.5） |
| L2 | B 在列表里选 A 的队伍 → 申请加入 → A 收应答窗 → 接受 | 待验证（期望：B 入队、A 仍是队长） |
| L3 | B 打开「推荐伙伴」（热键 `[`） | ✅ 已验证（2026-09-29）：窗口可打开（推荐列表内容依赖队伍链接，**推荐出有队伍的人先查发包时序**，见"已知限制"§6.5） |
| L4 | 预置队名（`sel` 1..6） | 待验证（期望：别人的窗口/列表显示同一队名） |
| L6 | 一人换区后看对方那一行 | 待验证（**允许空白，绝不能显示别人的名字**；出现错名立即记录并回报） |
| L7 | 两人分开后又回到同区域 | 待验证（期望：队友行恢复显示名字等级） |

**实现与评审期发现并修正的根因（每条都有自动化断言；真机复验随上表进行中）**

1. **接受方的失败包必须与请求方同形**：客户端 `CMD 11` 分支按 `body[1]` 当 peerType 分流，接受失败若回 `[0,19]` 会落到未知 peerType → 接受方什么都看不到；统一改回 `CMD 11 [0,100,0]`（以设计稿 §2.1 为准，原计划文本已改写）。
2. **目标已有待决请求时改为"先到者优先"**：原设计是"抢占 + 给旧请求者回失败"，会让目标正在看的应答窗被换掉；改为后来者**立刻**收同形失败（马上有反馈，而不是干等 30 秒）。
3. **解散广播要覆盖"曾通知过的区域"**：若按"解散瞬间名册里成员所在区域"发 `kind=3`，最后一名成员退队时名册已空 → 旁观者收不到清场包、列表里留一支永远点不开的队伍；改为每队累积"通知过的区域"集合，解散时对该集合 ∪ 当前在场区域广播（代价：队伍已撤出的区域会继续收到该队更新直到解散，本地解析不到成员、不进列表）。
4. **人数上限不得降到当前人数以下**：`CMD 12` 的 `maxMembers` 钳到 `[当前人数, 4]`，否则队伍处在"3/2"这种自相矛盾状态（客户端如何渲染未知，真机 P7 顺带看）。
5. **名册必须出锁前快照**：`PartyRoster` 早期直接暴露内部可变 `List` 且 `UpdateLocation` 原地改写，与"锁内快照、出锁发包"的惯用法冲突（并发下会发出半更新名册/枚举异常）；改为成员不可变 + `Members` 返回新数组。
6. **重复接受幂等**：同一份待决请求被并发接受两次时只产出一次结果（靠 `_pending.TryRemove` 的返回值守卫），不再冒出失败回执或第二套名册。

**已知限制**

- **跨区域那一行可能空白**：`NOTI 9` 不携带名字/等级，客户端按**本区域**用户表解析槽位 uid；解析不到的成员填**他自己的 uid**（**绝不填 0**——0 会被客户端当成"该成员已离开"，跨区域散队，2026-09-28 已修）→ 该行空白，成员仍在队伍里。**极端情况下（uid 按区域分配、跨区域撞号）那一行可能显示成同区域另一位玩家的名字**——本轮不改 uid 池，真机若真出现错名再单独立项做"频道内全局唯一 uid"（真机 L6/P13）。这是原版协议的固有限制，不是服务端 bug。
- **自定义队名出站时降级为预置名第 1 项**：`NOTI 9` 只有预置名下标编号这一个字段，而 `nameSel=0` 的名册在真机上完全无效，所以出站边界把 `sel==0` 规范化为第 1 项预置名——**所有客户端（含创建者自己）**都会看到该预置名；名册存储仍保留客户端给的原值（自定义名不传播只是出站策略，将来要改不用动名册）。
- 「队伍信息」列表与「推荐伙伴」只能看到**本区域**有成员的队伍/玩家（列表是客户端遍历本区域用户表本地推导的，服务端**不存在**列表请求/响应包）；服务端只负责广播与补发。
- **名册必须晚于该成员在本地的 `USERINFO` 与区域条目**（设计稿 §6.5）：`NOTI 9` 的槽位写入是按 uid 查客户端**本地 user 表**，查不到就只存槽位、**不建立"某玩家属于这支队"的链接**——旁观者列表能列出这支队伍、推荐伙伴能把有队伍的人过滤掉，靠的都是这条链接。城镇同屏的进入序列本就是"外观 → 名单"，队伍广播必须排在其后（与 §2.3 的"外观必须先于名单"是同一类坑）。**真机若出现"列表里看不到对方的队伍"或推荐伙伴推荐了有队伍的人，第一件事查发包时序**（队伍记录 vs 该成员的 `USERINFO`/`USER_AREA`）。
- **区域广播的包量随区域人数/队伍数线性增长**（每次名册变化 + 每次进区补发各发一轮）；队伍 ≤4 人、朋友间小规模同玩下用户已确认可接受（设计稿 §0/§6.2 的代价）。
- **已支持委任队长**：客户端 `CMD 131`（u8 槽位）→ 服务端**就地**提升 `LeaderSlot` → `NOTI 9` 广播（真实槽位下标、名册不重排）；下行不可达（客户端把 131 映射到空分支）故**无失败回包**，非法请求只记日志。
- 不做等级差/COF 限制、不做组队经验与掉落分配（`ClearRewardCatalog.PartyMultipliers` 保持不动）；「拒绝组队」是**客户端本地选项、服务端不可见** → 无法据此过滤（对方开启后邀请仍会弹窗）。
- `memberByte` 恒 0（语义未知）；队伍是**内存态、不落盘**，服务端重启即全部解散（与城镇目录一致）。
- 同区成员会收到**两份逐字节相同的名册**（定向一份 + 区域广播一份）——无害冗余，真机 L1 顺带观察客户端是否幂等。
- **副本内组队**（同队进图、共享副本实例、副本内队友位置同步）留待第二轮；本轮"进副本不退队"只是队伍记录跨区域持续（第二轮已完成，见 §2.7）。

### 2.7 同队进副本（组队第二轮）

同队进副本已实现（设计稿 `docs/superpowers/specs/2026-09-29-dungeon-party-design.md`，计划 `docs/superpowers/plans/2026-09-29-dungeon-party.md`）：**每队共享一份副本实例**（布局/怪物/掉落/房间开放/结算全员共享）；**单人 = 以角色 id 为键的独立实例**，报文与改造前一致（自动化只有结构性断言，逐字节证据见 P8）；**队员由自己的会话入场**（队长只投"待入队请求"，队员取走后自己执行，同区域是硬前提）；**门口集结制换房间**；**掉落全队共享、先到先得**；**结算各自出结果屏**、奖励按人数加成、卡片槽位取自己所处实例名册。实现提交 10 个（`1a0371d..39e7b0a`，其上另有 9 个探针/勘察提交 `8d5fdac..d7052b6`）；本轮 **Task 7 把临时探针整体退役**、全量验证并备好真机环境。**真机双客户端验收待用户按下方清单逐项填写结论**（2026-09-29 环境已备好，见下"无头冒烟"）。

**实现要点（关键位置）**

- **每队一个实例**：`DungeonInstanceRegistry`（`_byParty` 按 partyId、`_soloByCharacter` 按角色 id 两张键表，互不串用）、`DungeonInstance`（`IsParty => PartyId != 0`）；队伍实例在**最后一个成员离开**时就地销毁（`UnbindCharacter` → `Remove` → `StopRun`），其余成员的房间/掉落/run 原样保留。
- **队员入场**：队长进副本成功后由 `DungeonPartyService.PostEntryRequests(...)` 在锁外投递（全员必须在队长进图前的同一 `(town, area)`，任一人不在 → **整队中止**、只记 `party entry aborted` 日志）；队员的包由**他自己的会话**发：取走请求 → 同区域/实例存活/仍有槽位三重校验 → 离城 → 绑实例 → **外观 → 名册 → `NOTI 26`（自己的槽位）→ `NOTI 11` → `NOTI 28` → `NOTI 29`**（`DungeonEntryPackets.PlanMemberEntry`；顺序沿用探针真机结论 `3d5bb45`——外观/名册必须先于副本报文）。
- **门口集结制换房间**：`DungeonDoorAssembly`——第一个 `CMD 48` 只登记目标房间与到门口的人；**全员到齐立即推进**；**过半到门口开 10 秒倒计时**，到点强制推进（`TryAdvanceOnCountdown`；人数缩水会带着新人数重新评估，不能当成推进）。推进时给已到门口者补发 `CMD 48` 成功回包（客户端还在等）。
- **共享广播**：击杀/房间开放/掉落向**同实例全员**各发一份（包由成员自己的发送句柄出，`NOTI 40` 见"已知限制"）；房间开放状态（`RoomStateFlag`）随实例，不随某个客户端。
- **拾取**：`DungeonInstance` 锁内"取快照 + 摘除"一次完成——**全队共享、先到先得**；落库失败放回原地，别人仍可捡。
- **结算**：各自结算、各自结果屏；`clear-card rewards prepared` 以 `partyMemberCount: instance.MemberCount` 计算人数加成；卡片槽位取实例名册里自己的那一格。

**探针退役（本次）**

- 删除 `EnterDungeonPeerProbeAsync` 与调用点、`DungeonProbeSpawnFor`、`SET_USER_POSITION` 的探针日志块（同段 `UpdatePositionAsync` 坐标转发保留）、`ServerOptions.DungeonPeerProbeOptions` 与 `DebugOptions.DungeonPeerProbe`、`GameplayDatagramService` 的 UDP 探针日志块。
- 部署目录里仍带 `Debug.DungeonPeerProbe`（Enabled=true、AreaRecycle=true）的旧 `server.json` 已用干净备份还原。
- 验证：`grep -rn "DungeonPeerProbe\|AreaRecycle\|DungeonPartyProbeState\|TryReadArea" src/ tests/ server.json` **无输出**（exit 1）。探针最后一次迭代保留在历史里备查（`d7052b6` 等 9 个提交）。

**自动化（全绿，2026-09-29 本次运行）**

- `dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release` → 末行 `All DFLegacy protocol smoke tests passed.`
- `dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-incremental` → **0 警告 0 错误**。
- `DungeonPartySmokeTests`（1552 行）覆盖：实例/注册表并发语义（单人键与队伍键互不串用、最后一人离场销毁、断线不崩；**并发开局只认一个赢家**、空 boss 房/地狱房 detach 等新用例）、单人零回归（**结构性断言**：单人实例键 = 自己角色 id、单成员、`IsParty == false` → `CMD 48` 走立即推进、单人实例不参与扫描/集结）、入场报文序列与顺序（外观 → 名册 → `NOTI 26/11/28/29`、`NOTI 26` 槽位=自己、无名册时退化）、同区域校验（异区整队不投递）、共享广播（击杀/掉落对全员各一次、捡拾者不收回自己的掉落广播、**被踢成员的 OCE 不打断健康成员的投递**）、拾取先到先得（并发争抢只出一个赢家、落库失败放回可再捡）、门口集结（全员即推 / 倒计时扫全队 / 坏成员不卡死 / 单人实例不参与）。
- **口径（2026-09-29 终审更正）**：仓库里**没有**驱动 `EntranceService` 报文流的测试（会话是 16k 行的单个方法，无接缝），所以"单人报文与改造前逐字节一致"**不是**自动化结论——自动化只提供**结构性断言**（实例键/成员数/`IsParty` 决定的分支）与报文规划器（`DungeonEntryPackets`）的顺序断言；**唯一的逐字节证据是真机 P8**（改造前后日志比对，见下表）。组队侧的会话接线（入场/广播/推进/邮箱取件）同样只有真机 + 模型层/结构性证据。

**无头冒烟（发布件启动，未接客户端，2026-09-29 本次运行）**

`taskkill` 停掉旧服 → `dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -o /g/Game/DNF_ACT1/DF/town-test` → 还原 `server.json`（`.superpowers/sdd/2026-09-28-trade-system/town-test-server.json.bak`；旧部署配置里的 `Debug.DungeonPeerProbe` 块借此清除）→ 在 `DF/town-test` 起服（`./DFLegacy.Server.exe > server-dungeon-party.log 2>&1 &`）。实见：`Indexed 48343 script files`、`Entrance listener ready at 127.0.0.1:2311 / 7001 / 8080`（另有 `CharacterDatagramService` 2311、`GameplayDatagramService` 7002/7003、后台 `http://127.0.0.1:8081`）、整份启动日志**无 `Exception`/`error:`/告警**。核对完**该服务端已停掉**（不占端口，验收时按上面的命令现启即可；日志文件保留，重启用 `>>` 追加或先备份）。

**真机验收清单（双客户端，2026-09-29；环境 `G:/Game/DNF_ACT1/DF/town-test`，日志 `server-dungeon-party.log`；账号见 §2.6，`test`/`test` 与 `shizuku`/`shizuku`）**

清单照设计稿 §6，逐项**留空待用户填写**（结论列）；日志关键字供排查用。

| # | 检查项 | 日志关键字 | 结论 |
| --- | --- | --- | --- |
| P1 | 同区域组队 → 队长选图 → 两人进入、同一房间看到同一批怪 | `Party member … entered dungeon`、`party instance` | ✅ 已验证（2026-10-02，run #42 四局：`Party member test (uid 1, slot 1) entered dungeon 1 instance 2 room (0,0)`，两端同实例） |
| P2 | **两人能看到对方角色并看到移动**（A2 假设核心验证点） | 客户端画面 | 待填写（本轮未逐项检查） |
| P3 | 一方杀怪 → 另一方**怪物消失/房间开放**同步 | `broadcast`/`MonsterDie` 相关 | ✅ 部分验证（2026-10-02：击杀 `NOTI 38` 与经验 `NOTI 37` 对两端成对投递；`NOTI 31` 房间开放两端同刻收到） |
| P4 | 掉落共享（一方捡走，另一方看到消失；顺带看非物主是否被客户端置灰） | — | ❌ **未通过，且已定性为「客户端版本限制」**（2026-10-02 静态证据）：官服的移除广播是 `NOTI 86 SERVER_BROADCAST` 子类型 5，而本客户端的 86 号 case 只实现子类型 1/2；客户端删地面物只有两处调用点、都以"载荷 uid == 我的 uid"为门 ⇒ **服务端不存在任何包能让非拾取者删掉别人的地面物**。详见下方第 19 条的"2026-10-02 更正" |
| P5 | 通关结算：双方各自结果屏、奖励按人数加成 | `clear-card rewards prepared` | ✅ 已验证（2026-10-02，run #42 四局：`settlement sent for user 1/2`、`clear-card rewards prepared for user 1/2` 各有，`partyMemberCount=2`） |
| P6 | 队员自己能回城；队长退出后队员仍在副本且实例不崩；其余成员能看到离开者角色消失 | `left dungeon instance` | 待填写（`#40` 见到队员掉线后队长端收到离场信号 `NOTI 23`+名册刷新，属部分观察） |
| P7 | 队员不在同一区域 → 整队进不去（服务端日志有中止原因） | `party entry aborted` | 待填写 |
| P8 | **单人进副本零回归**（**唯一的逐字节证据**：报文序列与改造前日志逐条比对 + 实玩一次） | 与改造前日志比对 | 待填写（本轮未跑单人；`src/DFLegacy.Protocol/` 在本轮改动区间零改动，单人 CMD 74 两个重载有逐字节断言） |

**组队翻牌验收（2026-10-02，run #42，`server-party42.log`）——原缺陷「两端都认领第 0 列」未复现**

背景缺陷：自动选牌原为**会话级**实现（每会话都选第 0 列），而客户端卡位 owner **首写不覆盖**（`DNF.exe` `0x8765B0`/`0x876650`），两端各写一次即永久错位。修复：归属/内容/发布门收到 `DungeonInstance` 一份表（分支 `feat/party-loot-card-alignment`，合并点 `a2c999a`；报告 `.superpowers/sdd/2026-10-01-party-loot-card-alignment/task-2-report.md` §10）。

`[真机]` 四局副本，两端都到达翻牌页并各自结算（环境：发布件为本分支构建并已核对二进制标记；两份 300 秒线程栈采样常驻，本轮无崩溃）：

| 局 | 玩法 | 两端翻开的列（服务端整表 CMD 74 对两端逐字节相同） |
| --- | --- | --- |
| #1 | 两端手动 | 第 1 列（队员，`+10` 金＝自己掷的 freeGold）/ 第 3 列（队长，`+10`）；第二版把第 1 列**一字节未动**、补上第 3 列 |
| #2 | 队员手动 + 队长自动(4s) | 第 1 列（`+14`，物品进**邮件**）/ 第 0 列（`+11`） |
| #3 | 免费 + **付费卡**双侧 | 队长免费第 3 列（`+14`）；**队员付费第 0 列 −100**、**队长付费第 2 列 −100**（各扣各的、各拿各的物品） |
| #4 | **两端都不操作、都走 4s 自动（原缺陷标准形态）** | 第 0 列（队长，`+8`）/ 第 1 列（队员，`+8`）；末版表 `01 00 FF 00 01 FF 00 FF FF 00 FF FF 00` |

- 每局两端的 `NOTI 35` 掷取本就不同（freeItem 13200/15200、12400/19200；freeGold 10/10、14/11、14/12、8/8）⇒ 每列内容来自**该列归属成员自己那份掷取**。
- 存档 `data/state.json` 的 `dungeon-card:` 账本 11 条全 `Committed`，每条带**自己的角色/槽位/列/金币增量**（免费 +10/+10、+14/+11、+14/+12、+8/+8；付费 −100/−100）。
- 客户端屏幕侧由用户确认：两端看到的是**不同的列**。
- 已知残留（本轮**未**处理，均不属翻牌改动）：① 拾取只发本人（`4d7731a`）导致 P4 未通过——队友端物品不消失、能交互但捡不起来，修法见 `GameProtocolEngine.cs:3682-3689` 注释（照官服另发 7 字节移除包）；② 队员端客户端崩溃（`#40` boss 死后、`#41` 第二张图；`ACCESS_VIOLATION` 读 `0x223B0000`、`Eip=DNF.exe+0x5019C1`，句柄表遍历里索引字段被浮点数据污染）——`#42` 同样出现强制推进与"捡不起来"却未崩，两个嫌疑都不充分，仍未定性。


**真机重点检查点（待验证，先于清单细项看这几条）**

1. **延迟的 `CMD 48` 回包客户端能否接受**：两人都走到门口时，全员到齐立即推进不受影响；只有"过半 → 10 秒倒计时"时先到的人要等回包。若客户端卡死/超时：把 `DungeonDoorAssembly` 的"过半开倒计时"改成"**立即推进 + 已有集合里的人照常，其余人被强制**"（`StartedCountdown` 分支也走 `Advance`，一步改动），并把真机结论记回本节。
2. **队员客户端停在选图界面时完全不发包**：主循环的读等待本是"等包或超时"，待入队/待选图请求只在**有包**的路径被取走，队员静默时可能一直躺着。**本轮已修（部分）**：`DungeonInstanceRegistry.HasPendingMail` + 主循环的 250ms 短 tick（**只在确实有货时**缩短等待；单人恒无货 → 等待行为一行不变），短 tick 到点后只取信箱、不解码。**残留窗口（务必按此观察）**：短 tick 只在"进入等待的那一刻信箱已有货"时生效——若队员会话在队长投递请求**之前**就已经阻塞在无限读等待里（队员客户端在选图界面一个包都不发），没有任何东西能唤醒它，请求仍要等客户端下一次出站包。观察点：队员停手时若迟迟不进图，先看他有没有任何出站包（`RX` 日志）；彻底解法是给会话加"有队伍/在选图界面时的短 tick 或唤醒通道"，不在本轮清单范围内。
3. **P2 失败（两人互相不可见）的兜底**：静态证据指向客户端没有为队友建档/没登记进显示链表（勘察 §5.2 `creature[+0xE4]` 与 `sub_4C7890` 的登记门控）。兜底是按探针最后一次迭代（`d7052b6`）重新实现到生产路径：补发 `USER_AREA`（`NOTI 23`），用副本地图的 `[dungeon start area]` 出生点坐标、以"进副本前的 (town, area)"为区域、每轮 **外观 → `USER_AREA`（双向）→ 名册 → `NOTI 28`**、2.5s/6s/10s 三轮。**绝不用 `AREA_USERS`（`NOTI 24`——会把客户端拉回城镇地图）**。
4. **NOTI 26 现在发"主机（队长）槽位"**（提交 `99d55e1`，真机未验）：官服 `CParty::send_host_info` 反汇编证明
   载荷 = `party->host` 所在**槽位号**、全队同一个值；我们客户端把它当 0..7 的槽位索引用（`DNF.exe` 0x78D2B1 段）。
   此前队员主入场路径发的是**收件人自己的槽位**（队员会自认主机）。观察点：双人进副本后队员端"UDP 主机"应指向队长。
5. **结算期"EPLP 超时"**（提交 `d6d6d3e`，真机未验）：队员在结算页卡死、一个包都不发时，
   结算包发出后 20 秒由服务端把他送回城（官服同款；参考实现日志 `town return: eplp timer 20`）。
   观察点：卡死后约 20 秒应自动回城，日志 `settlement EPLP timeout: forced town return queued` 与
   `forced town return for <名字> (settlement-eplp-timeout)`。
6. **全员加载门槛（`NOTI 30` 的时机）**（提交 `99d55e1`，真机未验）：官方 `CParty::finish_loading` +
   `check_allmember_loading` 语义 = 每个成员上报 `CMD 40` 只置自己那位，**全员齐了**才向全队广播空载荷 `NOTI 30`；
   换房时全员标志清零（官方 `resetFinishLoadingVar`）。单人/非队伍实例仍"立即发"，报文逐字节不变。
   观察点：队员过图时 `NOTI 30` 应出现在**两人都上报 `CMD 40` 之后**；若有人卡在加载中，日志会打
   `waiting for the remaining member(s)`，全员齐时打 `all N member(s) finished loading`。
7. **结算期队员客户端崩溃（2026-09-30 真机；✅ 已修复并真机验收，过程与全部证据保留在下方）**：**五局**（party8/10/11/13/15）复现同一规律——**队员**客户端在收到**自己那份 `CMD 73`** 后
   **恰好 15 秒**连接被 RST、进程退出；队长全程正常；单人结算正常。party15 的两局补充判定：①**不是 UI 挂起**——主线程一直在正常消息泵里，
   最后自己走进 `BugTrap`（崩溃处理器）⇒ 一次**主线程异常**；异常约在 `CMD 73 + 9~10 秒`抛出，BugTrap 用 **wininet 上传报告、不写本地 dump**
   （客户端目录/临时目录都没有 `crashdump.dmp`），约 5 秒后进程退出 ⇒ 那 15 秒是"异常→退出"的总时延；②两局都**双方手动选牌、两份 `CMD 74` 都广播**
   且**服务端从未触发自动选牌**，队员仍死 ⇒ 广播对称化（第 8 条）与本崩溃无关（该修复仍是官服语义，保留）；③崩溃现场（看门狗 1.2 秒采样）：
   主线程 `eip` 为 **`d3d8.dll+0x99B4A`**（`movss xmm1, [ecx+4]`——读一段浮点向量时指针无效），DNF 侧调用链经过精灵绘制助手 `sub_82E3A0`，
   其邻域引用字符串 **`equipment/chara`、`title/animation`、`interface/newre…`** ⇒ **结算/篝火场景里"画角色（装备/称号立绘）"时崩在 D3D8 绘制调用内部**，
   不是死锁、也不是缺服务端包（崩溃前服务端一个包都没发）。已否证：槽位字段（`DFLEGACY_FORCE_SLOT_ZERO` 下 party13 仍死）、队友选牌广播、自动选牌路径。
   下一步仪器（已就位）：`work/wow64_stack.py`（`Wow64GetThreadContext`；WOW64_CONTEXT 偏移 `Eip=184/Esp=196/Ebp=180`；线程枚举走 PowerShell——
   沙箱过滤 Toolhelp 线程表；**eip 不在系统 DLL 里的线程**与**链上出现 `BugTrap` 的线程**都会附带 `rawstack@esp` 十六进制转储，后者窗口 4 KB）
   + `work/watch_clients.py`（0.5 秒/次，落 `G:/tmp/stacks/stacks_<pid>.txt`）+ `work/scan_exception.py`（扫 `EXCEPTION_RECORD`）+ `work/analyze_hang.py`。
   **注意地址口径**：采样器打印的 `模块+0xNNN` 是"VA − 模块基址"，IDA 里要加回基址（DNF.exe 基址 `0x400000`——早先按偏移反编译得到的是另一个函数）。
   **【2026-09-30 更正：崩溃点不是 `d3d8`，是 `DNF.exe` 里的空指针读】**
   从 party15 第二局**队员端已有的看门狗采样**里把异常记录本身恢复了出来（新脚本 `work/recover_exception.py`；
   判据是 `EXCEPTION_RECORD` 与 WOW64 `CONTEXT` **成对出现且 `ExceptionAddress == Context.Eip`**，因此不是巧合）：
   `0xC0000005`、`ExceptionAddress = 0x0087A46E`（= **`DNF.exe+0x47A46E`**）、参数 = (读, `0x00000000`) ⇒ **空指针读**；
   同一现场两次采样给出同一组寄存器：`Eip=0x0087A46E Esp=0x001AFD7C Ebp=0x001AFD94 Eax=Ebx=0x17C95400 Ecx=0 Edx=3 Esi=0x10 Edi=0x1788DBB0`。
   **上面"崩溃现场在 D3D8 绘制调用内（`d3d8.dll+0x99B4A`）"作废**：那个地址是**渲染热循环**（客户端健康时 11:18:47 也采样到它），
   不是崩溃点；`d3d8.dll` 只在最后两秒的链上，栈顶异常在 `DNF.exe` 内。[作废] 依据即本条。
   机制（**三来源交叉校验**：capstone 反汇编、GNU objdump、IDA Hex-Rays C 伪码，再加运行时寄存器；产物 `.recon/out/pseudo.txt`）：
   崩溃在函数 **`sub_87A340`**（IDA 名；VA `0x87A340..0x87A7AD` = `DNF.exe+0x47A340..+0x47A7AD`）内，按 `this->[+4]` 走
   **10 路 switch**（IDA 自己标注 `jmp jpt_87A384[eax*4]`，且 `0x87A422` = `jumptable 0087A384 case 8`）。case 8 的伪码就是崩溃表达式：
   `if (v6 != v7 || !(*(int (__thiscall **)(_DWORD))(**(_DWORD **)(*(_DWORD *)(v5*4 + dword_B5DCC8) + 272) + 84))(*(_DWORD *)(*(_DWORD *)(v5*4 + dword_B5DCC8) + 272)))`
   —— 遍历 `G+0x10..+0x1C`（`v5=4..7`，`v5*4` 即字节偏移；`G = dword_B5DCC8`）四个槽，与 `G[G[8]]` 比较（`sub_4FE830` 在
   UI 状态 ∈ {4,6,7,9} 时返回 -1 ⇒ 退回 `G[0]`），命中后取 `iface = [slot+0x110]`（伪码里的 `+272`）并
   `call [iface->vtbl+0x54]`（伪码里的 `+84`）。现场 `Esi=0x10` ⇒ 崩在**第 0 个槽**；`Ecx=0` ⇒ `[slot+0x110]` 为 NULL；
   异常记录 `Info=(读, 0x00000000)` 与 `0x87A46E` 的字节 `8B 11`（`mov edx,[ecx]`）逐条对上，紧跟的 `FF 52 54` 即该虚调用。
   **口径坑（本次实测踩到，记下来）**：把采样器打印的 `模块+0xNNN`（**偏移**）直接交给 IDA，会问到镜像里另一个地方、并返回
   "看起来合理"的结果（我据此一度得出"IDA 在这区域 code=False、分析坏了"的错误结论）；**VA = 偏移 + `0x400000`**。
   **运维事实**：IDA 实际打开的库是 `G:\Game\DNF_ACT1\DF\DNF.exe.idb`（**不是** `.recon/onboarding.md` 写的 `.i64`——后者的 mtime
   停在 9/29，是旧的打包副本；单独拷贝 `.i64` 会被 IDA 拒收 `You can't disassemble file with such an extension`）。
   本次 IDA 退出时重写了 `.idb`（hash 变了，**语义未改**：探针里的 `add_func` 分支从未执行；改动前备份在 `G:\tmp\DNF.exe.*.bak`）。
   **静态补充（VA 口径修正后才取到的证据）**：`sub_404320(G, i)` 就是"取第 i 个槽"——伪码 `if (a2 >= 4) return 0; else return this[a2 + 4];`，
   而它的调用者里有 **0x78Dxxx 段的组队槽位代码**（`sub_78D3A0`/`sub_78DBD0`/`sub_78EE00`；第 4 条那个"客户端把 `NOTI 26` 当槽位索引"
   的判读点就在 `DNF.exe` 0x78D2B1 附近）⇒ `G+0x10..+0x1C` 这四个槽**很可能就是队伍槽位** [推断]，`G+0x20`（即 `sub_4FE830` 的返回值，
   UI 状态 ∈ {4,6,7,9} 时为 -1）是"自己那一格"的索引。`sub_87A340` 本身是**状态设置器**：调用者清一色
   `mov eax,[esi+4]; inc eax; push eax; mov ecx,esi; call sub_87A340`，而它的 `this` = `*(dword_B5DCE8 + 0xF1C)`、
   调用者之一（`0x87B0C9`）紧邻字符串 **`REWARD_DIVIDE`**（结算奖励界面）——这与"崩溃只发生在结算期"吻合。
   因此新探针 `work/probe_party_table.py` **一次读全**：`G`、`G+0x20`、四个槽、每槽 `[+0x110]`/vtable/`[vtable+0x54]`，
   外加那个状态对象的 `[+4]`（= 崩溃那一支的 case 号；跑到 8 就是崩溃前夜）。
   **【party16 真机（2026-09-30 16:07–16:10）：机制已完整定位，且能用探针客观复现】**
   这一轮**没改服务端行为**（外观开关 OFF），三个只读仪器在跑（协议 `work/RUN-party16-protocol.md`；产物 `G:\tmp\party16*`）。
   结果：**P2（新采样独立复现同一条异常记录）✔**——`recover_exception.py` 在队员端 7 次采样里都恢复出同一条
   `0xC0000005 / Eip=0x0087A46E / badaddr=0 / Ecx=0 / Esi=0x10 / Esp=0x001AFD7C / Ebp=0x001AFD94`，与 party15 **逐字段相同**。
   **P1（队员端被选槽的接口为 NULL）✔**，而且**四个操作数在活内存里一一对上**：
   | 现场（运行时） | 活内存（探针同时读到） |
   | --- | --- |
   | `Edi = 0x17981790` = `*(0xB5DCC8)` | 探针 `G = 0x17981790` |
   | `Eax = Ebx = 0x06E73D80` = 被解引用的槽对象 | 探针 `slot0 = 0x06E73D80` |
   | `Ecx = 0` = `[slot+0x110]` | 探针 `slot0 [+0x110] = NULL` |
   | `Esi = 0x10` ⇒ 循环第 0 格、`G+0x20 = 0` ⇒ 选中的是槽 0 | 探针 `own/slot index (G+20) = 0` |
   **类别已定名（客户端自带 MSVC RTTI，不是我猜的）**：`dword_B5DCC8` 指向 **`CNRDPartyManager`**（`??_R4CNRDPartyManager@@6B@`）；
   它的四个槽（`+0x10..+0x1C`）装的是 **`CNUser`**（`??_7CNUser@@6B@`，由 `sub_490D00` 构造）＝**队伍成员**；
   `CNUser+0x110` 指向**按职业实例化的角色对象**：队长 `test` = **`CNGunner`**（vtbl 0xA0451C / locator `??_R4CNGunner@@6B@`），
   队员 `ʥ��ʿ` = **`CNPriest`**（vtbl 0xA0515C / locator `??_R4CNPriest@@6B@`）。这些对象由工厂 `sub_654E50(job)` 建：
   `case 0..4` 各 new 一个职业对象，**`default: return 0`** —— 职业号不认识就返回 **NULL**，正是我们看到的那个空指针。
   **非对称性由此完全解释**（两台客户端的实测槽表）：
   | 客户端 | `G+0x20` | slot0（`G+0x10`） | slot1（`G+0x14`） |
   | --- | --- | --- | --- |
   | **队员**（崩溃的那个） | 0 | `CNUser`＝**队长**，`[+0x110] = NULL` ← 崩在这里 | `CNUser`＝自己，`[+0x110]`＝`CNPriest`（有效） |
   | **队长**（对照组，存活） | 0 | `CNUser`＝**自己**，`[+0x110]`＝`CNGunner`（有效） | `CNUser`＝**队员**，`[+0x110] = NULL`（结算期实测 16:09:40–16:09:50） |
   **两台客户端的缺陷一模一样：外来成员的 `CNUser` 都没有职业对象**（各自只把自己那一格建全了）。差别只在"槽 `G[G+0x20]`"指向谁：
   两边的 `G+0x20` 都是 0，队长端的槽 0 是**自己**（对象完整 ⇒ 什么都不发生），队员端的槽 0 是**队长**（对象残缺 ⇒ 空指针）。
   写入/清空路径也已定名：`sub_4FD880(user, classObj)` 就是往 `CNUser+0x110` 存职业对象的那一句（伪码首行 `*(_DWORD *)(this + 272) = a2;`，
   且 `a2 == 0` 是被允许的状态——所以"没有职业对象"不会当场崩，只会在结算界面被解引用时崩）；`sub_4FD2D0` 是把它清零的复位函数；
   成套动作在用户构建器 `sub_4086F0` 里：`sub_4FDF60`（取或建 `CNUser`）→ `sub_654E50(job)`（建职业对象）→ `sub_4FD880`（存进去）。
   **下一步（修）**：让"外来成员"走到并走完这一成套动作——即把 `sub_654E50` 拿到的职业号喂对（工厂 `case 0..4`，越界即 `default: 0`），
   或补上客户端缺的那条构建路径。这条与 P2 是同一件事，**而且现在可以客观验证**（见下）。
   即：**奖励/结算界面（状态机跑到 state 8，探针实测 `0→2→4→5→6→7→8`）会去解引用"槽 `G[G+0x20]`"的 `[+0x110]`**；
   两台客户端的 `G+0x20` 都是 **0**，但**槽 0 的含义不同**——队长端的槽 0 是"自己"（对象完整，无事），
   队员端的槽 0 是**队长**，而队员端**从未把队长的职业对象建出来**（`[+0x110] = NULL`）⇒ 空指针读、BugTrap、RST。
   **这与 P2（队员看不到队长/外来角色没被完整建出来）是同一个根因**：`sub_4086F0` 里"取或建 `CNUser`（`sub_4FDF60`）→
   建职业对象（`sub_654E50`）"的成套动作，在队员端只完成了前半截。
   **更正一条旧结论**：正文说的"恰好 15 秒"不是常数——本轮异常在 `CMD73 + 11s`（16:09:53→16:10:04）、RST 在 `+32s`（16:10:25），
   5 秒与 21 秒的差别是 BugTrap 处理（含那次注定失败的 wininet 上传）耗时，**只有"异常≈+10s"是稳定量**。
   **新能力（这条比崩溃本身更值钱）**：`work/probe_party_table.py` 可以在**任意时刻**读出某个客户端的队伍槽表与
   `CNUser+0x110` ⇒ **"外来角色有没有被完整建出来"从此是可测量信号**，P2 的修复不必再打到结算才知道成不成——
   组队进图后看队员端 `slot0 [+0x110]` 是不是非 0 即可。
   **【根因与修的方向已定：`NOTI 2`(USERINFO) 的 mode 用错了】**
   客户端 `NOTI 2` 处理器（`0x419A57`）**按 mode 分派到两个不同解析器**（本项目自己的勘察 `.superpowers/sdd/viewinfo-blacklist-recon.md` Q1 已把这条写成硬证据）：
   - `mode 0` → `0x419BEF`（"区域用户"解析器）——**只登记区域里的 actor，不建 `CNUser+0x110` 的模型对象**；
   - `mode 1/3` → `0x41A764`（"详情"解析器）——正是**建模型对象**的那一支：
     `0x417AB0(record)` 取 `+0x110`，为 0 就 `0x654E50(*(record+0x14))`（职业工厂）新建，再 `0x4FD880(record, actor)` 挂上去；
     两者共用解析器，**只有 mode 3 会多解析 28 字节尾并弹资料卡**（mode 1 只补模型、不弹窗）。
   而我们目前**给其他玩家发的外观包走的是 `CreateCurrentCharacterInfo` = mode 0**（`EntranceService.CreateTownAppearancePacket`，第 587/938/4604 三处调用点），
   于是：外来成员的 `CNUser` 有了（`0x4FDF60` FindOrCreate），**模型对象永远没建** ⇒ `+0x110 = NULL` ⇒
   结算界面 state 8 解引用它 ⇒ 崩；**同时这也解释了 P2（副本里看不到队友）**——同一条"模型没建出来"。
   注意这与 `DFLEGACY_APPEARANCE_MINIMAL` 那个诊断开关无关（那条注释依据的 `d3d8` 立绘结论已作废）。
   **修（待实施）**：给**其他玩家**在既有 mode-0 包之后**追加一条 mode-1 的详情记录**（复用 `GameProtocolEngine.CreateRequestedUserInfoDetail`，
   把 mode 参数化：`mode==3` 才写 28 字节尾；mode-1 记录**不含名字**，名字由先行的 mode-0 记录进本地注册表，故必须"先 mode 0 再 mode 1"、且**只对他人**发、自己保持 mode 0 不变）。
   **验证很快**：不用打到结算——组队进图后用 `work/probe_party_table.py` 看**外来成员那一格的 `[+0x110]` 是否非 0** 即可判成没成。
   **【party17 真机（2026-09-30 16:53，这一轮带 mode-1 补包）：机制修好了，但冒出一个新症状】**
   队员 `圣骑士`(Priest) / 队长 `test`(Gunner)；日志 `server-party17.log`。
   **修成立的部分（两条独立证据）**：①**活内存**——两台客户端的每个已填槽位 `[+0x110]` 都非 0（party16 时外来成员那格是 `NULL`）：
   队员端 slot0(=队长) `0x18343C60` vtbl `0xA0451C`=CNGunner、slot1(=自己) `0x17F48980` vtbl `0xA0515C`=CNPriest；
   队长端 slot0(=自己) `0x17D088C8`、slot1(=队员) `0x181313A8`；②**线上**——`TX … protocol=2` 里 mode 0（带名字，能看到 ASCII `test`）之后紧跟同 uid 的 mode 1
   （`01 0100 0200 2F2C0000 51000000…` = mode1/count1/uid2/exp11311/blobLen81）✓。
   顺带证实：**登录时每个客户端本来就收自己那条 mode-0 + mode-1**（既有 `CreateCurrentCharacterDetails`，不是本次改动）——所以"给自己发 mode-1"本身不是新变量。
   **新症状（阻塞结算验证）**：进图后**能看到队员了**（P2 有改善）**但位置不对**；队员**移动之后**其客户端弹出
   **`error : 3525:100`**，随后 16:54:00 会话**优雅关闭**（`disconnected.`，进程仍存活且 Responding）。之后队长端看不到队员、队员打不死怪（等同掉线）。
   静态定位到该弹窗：客户端硬编码字符串 `"error : 3525"`（VA `0xA89E4C`），由 `sub_8BEAD0` 用 `"%s:%d"` 格式化后经
   `sub_799EF0(CNGameCore, 0x23, …)` 弹窗；触发条件是 `sub_402030(this+0x10) == 1`（即"被校验对象的类号 == 1"），
   `sub_8BECD0` 是它的回调、由 `sub_8BECF0` 经 `sub_901430` 注册（`0x9014xx` = 该校验/报错模块）。
   根因未定：可能是 mode-1 包内容，也可能是"模型被建出来之后才走到的代码路径"（此前外来成员根本没有模型，那些路径从未被触发）。
   **已加临时 A/B 开关（判定完即删）**：现场存在 `no-userinfo-detail.flag` 时**不发** mode-1 补包——用文件而非环境变量，可不重启服务端切换。
   判定顺序：先关掉补包跑一遍（若 3525 消失 ⇒ 与补包相关；不消失 ⇒ 是模型存在后的其它路径），再逐字段二分（经验值/装备表/战斗数值块）。
   **【party19 真机（2026-09-30 17:18–17:23，`exp0` 变体）：原始崩溃未复现，队员端结算走通】**
   （变体 u32 写 0 生效后）这一轮：`17:21:40` 结算包发双方 → `17:21:54` 双方 ack 记分卷 → `17:21:58` 双方收到 `CMD 73` →
   **`17:22:01` 队员（user 1）`card type 0, index 0 processed … accepted=True, reward=27300`**、**`17:22:02` 再选第二张（reward=13200）**
   → `17:22:10` 队长走出副本（`settlement-after-card` 正常回城）→ `17:22:22` 队员由 **EPLP 20 秒计时器正常送回城**
   （`forced town return … settlement-eplp-timeout`）——**这条 EPLP 是结算流程的正常收尾**（官服同款 `town return: eplp timer 20`），不是症状。
   **客户端侧独立证据**：队员端（PID 56360，会话 4fd84c0a）**33MB 看门狗采样里 `EXCEPTION_RECORD` 条数 = 0**
   （`recover_exception.py` 报 `records seen: 0`）；期间各线程都在 ntdll 正常等待或 AMDXN32.DLL（显卡驱动）渲染里，**没有 BugTrap**。
   ⇒ **`CNUser+0x110` 为空导致的结算期空指针崩溃，在"别人发 mode-1（且 u32 写 0）"之后不再出现。**
   **仍未解决（与本崩溃分开记）**：
   ① **偶发崩溃**：同一天 17:11 那一轮（同样 `exp0`）队员端在小怪死亡后 ~37 s **RST 退出**（`ended with an error`）——两次里中一次，
      需要崩溃窗口内有采样才能定位（看门狗必须在场；后台常驻采样器会卡在内核等待，得用前台/短窗跑）。
   ② **位置瑕疵**：进图初期位置正确、双向同步正常，但"刷新一次"（房间重载）之后短暂错位，再移动一次才回到正确位置。
   ③ `exp0` 这个结论还只是在**临时变体开关**里（`userinfo-detail.txt`，判定完即删）：要把"mode-1 记录的 u32 恒为 0"写进代码并退役开关，
      同时保留 mode-3（资料卡）那条路径继续传真实经验（勘察说该字段语义未定，两处按各自用途分开）。
   **角色归因（可复现，`work/analyze_settlement_crash.py`）**：party8/10/11/13/15 五份日志的 6 次事件，**死的全是队员会话**
   （`CMD73+15s` 被 RST；11:28:06 主线程已在 `BugTrap` 处理函数内 = `CMD73+~10s` 抛异常），**队长全部存活**；队员在
   party8/10 是 `test`、在 party11/13/15 是 `ʥ��ʿ` ⇒ **与角色无关、与分工有关**。队员在自己那份 `CMD 73` 后 ~0.5 s 仍活着
   （发出 `CMD 74`），此后服务端到它崩溃**一个包都没发**；`work/compare_sessions.py` 另确认结算阶段双方收到的报文**种类一致**
   （不存在"队员少收一个包"）。
   已否证：①选中"空奖励卡"（party8/13/15-2 队员选中的卡都有奖励，照样死）；②槽位字段（先前 `DFLEGACY_FORCE_SLOT_ZERO`）。
   **待判（下一轮真机，协议 `work/RUN-party16-protocol.md`）**：`[slot+0x110]` 为 NULL 是否**队员端独有**、是哪个槽 ——
   即"外来角色的对象/接口没被完整建出来"（与 P2 同源）还是另一回事。
   采样器两处缺陷本次一并修掉（旧采样因此拿不到调用链）：`watch_clients.py` 不认 `.recon/collect-dnf-stacks.ps1` 传的
   `--pid`（客户端真在跑时必定 `ValueError`）；`wow64_stack.py` 转储窗口固定 0x600，而异常帧在 `handler-ESP+0x2F8`(记录)/`+0x348`(上下文)、
   **出错帧本身在 `+0x7B0`（窗口外）** ⇒ 现在可用 `DFLEGACY_STACK_DUMP_BYTES_ON_CRASH` 单独放宽崩溃线程的窗口。
   对照（负例）：队长那份 40MB 采样里 `EXCEPTION_RECORD` 条数为 **0**，即上述判据不会在健康客户端上误报。
   **✅ 验收结论（2026-09-30 17:18–17:23 party19 真机，修复已锁定并部署）**：
   根因是"**给其他玩家只发 `NOTI 2` mode 0**"——客户端 mode-0 解析器只登记区域 actor，**不建 `CNUser+0x110` 模型对象**；
   只有 mode 1/3 的详情解析器会建。修法：在每个"他人外观"推送点，于 mode-0 之后**紧跟一条同 uid 的 mode 1**
   （记录里**经验字段写 0**——真机 A/B 实测：写真实经验会让队员端弹 `error : 3525:100` 并掉线；`mode 3` 资料卡那条路径仍传真实经验）。
   验收证据：队员端**完整走通组队结算**（`17:22:01`/`17:22:02` 两次选牌 `accepted=True`、reward `27300`/`13200`；
   队长 `settlement-after-card` 正常回城；队员由 **EPLP 20 秒计时器正常送回城**——这条 EPLP 是结算流程的正常收尾，不是症状），
   且队员端 **33MB 采样里 `EXCEPTION_RECORD` = 0、`BugTrap` 从未进入任何调用链**（`chain-mentions=0`）。
   改动：`GameProtocolEngine.CreateRequestedUserInfoDetail` 增 `mode` 参数（默认为 3，行为不变）+ `EntranceService` 三个他人推送点；
   临时诊断开关（`userinfo-detail.txt` / `no-userinfo-detail.flag`）**已退役**（源码无残留）。
8. **自动选牌也要广播 `CMD 74`**（本次提交，真机未验）：官服 `CParty::SendEachCardInfo` 构造后走 `send_to_party`；
   我们此前**只有手动选牌**那份广播（party10 10:05:09 队员会话确实收到了队长那一列），**自动选牌（4 秒计时器）那份只回发包者**
   → 另一名成员的结算页永远少一列。现在两条路径共用 `BroadcastCardSelectionSnapshotAsync`（`IsParty` 为真才发，单人一个包都不多发）。
9. **游戏 UDP 去向审计**（临时开关 `DFLEGACY_UDP_AUDIT=1`，只加日志、不发包、不改协议）：客户端把自己的 UDP 端口同时声明在
   `CMD 2`（`nat_type/inner_ip/nat_ip/nat_port/mtu`，我们已 ACK 并落库）与 `NOTI 11` 对端表里（两处一致，127.0.0.1 可直达），
   但副本内始终看不到对端角色/怪物同步。要判的是**客户端把游戏 UDP 发给对端还是发给服务端**（官方架构里 relay `8101-8300`
   的角色由谁承担）：审计每 10 秒按远端打一行（`nat-probe`/`nat-keepalive`/`mt` 计数 + 协议号直方图 + 首包 hex）。
   若显示"`mt` 数据报发给服务端" → 缺的是频道服的 UDP 中继转发（问题 1/3/4 可能同源）。顺带结论：`CMD 100`（PEER_CONNECT_RESULT）
   在官服 `Dispatcher_PeerConnectResult::dispatch_sig` 里**只做 NAT 统计**（`IncreaseNatType`），不回复、不推进状态机——我们忽略它是对的。
10. **官服结算状态机（静态实证，2026-09-30 第三轮，`df_game_r`）**：`dungeon_clear_state` 走 2→3→4→5，全部由服务端定时器推进——
   `TimerClearDungeonReward`（key 17）在 state 2 向全队广播 **CMD 72** → state 3；`TimerClearDungeonScoreScoll`（key 18）在 state 3 向全队广播
   **CMD 73** → state 4，并插入 `card_select_right`(30s, key 19) 与 `card_select`(4s, key 16)；`TimerCardSelect`（key 16，4 秒）在 state 4 里
   `CompleteCardSelect` + **`SendEachCardInfo`（CMD 74 全队广播）** → state 5 + `dungeon_clear`；`TimerClearDungeonCardSelectRight`（key 19，30 秒）兜底 state 4→5。
   客户端上行 **CMD 73**（`Dispatcher_Card_Select_Right_State`）要求 `CheckRecvFromAllMember` 为真，回包走 `send_to_party`，**每槽位 ushort = 该成员的疲劳状态**
   （`0`=疲劳已满 / `1`=还有疲劳 / `0xFFFF`=**空槽位**）——我们目前把"非自己的槽位"一律写 `0xFFFF`（等于宣称队友是空位），与本条语义不符，待判。
   客户端上行 **CMD 74**（`Dispatcher_Select_Card`）每次选牌都 `SetCardNumber` + `SendEachCardInfo`（= 我们已有的逐次广播 ✓，本次修复把自动选牌那份也补上 ✓）；
   全员选完那一刻才 `set_dungeon_clear_state(5)` + `CParty::dungeon_clear`（逐人发奖励物品、`SetEPLPState(2)`、起 60 秒 key 21 计时器）。

**已知限制**

- **地狱模式的队员入场券不消耗**（只扣队长）：入场券在队长会话的入场路径扣（`Rejected hell entry…` 也在队长侧），队员会话只走待入队请求，不碰背包。将来要么改为全员扣、要么改成队伍共享一张（用户未拍板）。
- **玩家丢弃（`NOTI 40`）不广播**：本轮只广播击杀/房间开放/掉落，主动丢弃的地面物品其他成员看不到。
- **`CMD 4` 同会话换角色的副本收尾（本轮已修，真机未验）**：换角色现在照 `CMD 7` 同序解绑旧角色（`TeardownSoloDungeonInstance` + `LeaveDungeonInstanceAsync` + 置空实例），不再把旧角色永久留在成员表/注册表里（幽灵成员、实例永不销毁、新角色会收到旧队伍的副本包）。**真机未验**：2008 客户端能否在副本内/回城后发 `CMD 4` 尚未证实；验收时观察旧角色头上是否还挂着那支队伍、以及新角色是否收到副本包（日志 `Party member … left dungeon instance` 应带角色名）。
- **组队推进的空 boss 房结算（本轮已修，真机未验）**：单人 `CMD 48`/`CMD 16` 一直有"空 boss 房可结算"（`.map` 没有静态怪的行为房/战争房），两条**组队**推进路径此前都没有 → 房间打不完；现在集中在"组队成员的主循环镜像块"（`EnableEmptyBossRoomSettlementAsync`，`TryEnableClear` 从关到开只一次，不会重复广播 `NOTI 31`）。**真机未验**：需要在空 boss 房（如战争房）里确认客户端能开门结算。
- **跨会话投递的 OCE 窗口（本轮已修，真机未验）**：每个成员句柄写的是**他自己会话**的流，成员被踢/掉线会抛他自己 token 的 `OperationCanceledException`；此前四处投递护栏只捕 `IOException/ObjectDisposed/Socket` → 击杀/开放/拾取/NOTI 29 的广播会顺着**发送者**的会话冒泡（打断健康玩家），扫描里会让"房间已换、后面几个成员的 `NOTI 29` 没发出去"。现在四处共用 `CrossSessionDelivery` 判定（模型层有 OCE 用例钉住）。**真机未验**：验收时可顺带观察"踢掉一个队员后其余人是否仍收到广播/换房包"。
- **跨区域组队进图不做**：任一队员不在队长进图时的同一 `(town, area)` → 整队中止（队长按单人惯例回城，只记 `party entry aborted` 日志），这是设计稿 §3.2 的硬约束，不是 bug。
- **疲劳按每房各自扣**：每个成员的会话在自己入场/换房时各扣一次（`ConsumeDungeonRoomFatigueAsync`，已有 `lastSettledRoomX/Y` 防镜像刷新重复扣），与原版"每人各自扣"一致。

**party19 之后的待办（2026-09-30 用户真机反馈 + 本轮遗留）**

11. **偶发崩溃（未解）**：同一份 build（`mode 1` + u32=0）下，**17:11 那一轮队员端在小怪死亡后约 37 秒 RST 退出**
    （日志 `ended with an error`；两次尝试里中一次），而 17:18 那一轮零异常走通结算 ⇒ **不是原崩溃，是另一处偶发问题**。
    定位前提：**看门狗要在崩溃窗口内在场**——后台常驻采样进程会卡在内核等待（CPU 不增长、无输出），
    必须用**前台/短窗口**跑（`work/watch_clients.py <分钟> <目录> 0.5`）；抓到后 `work/recover_exception.py`
    取 `EXCEPTION_RECORD` + `CONTEXT`，再回 IDA。
12. **结算后队员不跟队长一起回城** —— **结案：不是缺陷，是官服语义**。
    `EPLP` 就是客户端上行 **`CMD 75`**（`GameProtocolEngine.EplpCommand`；`EntranceService.cs:14407` 收、`CreateEplpReply` 回），
    而它的**触发点是"篝火页"上玩家的选择**（本项目 `2026-09-29-dungeon-party/progress.md:192-193` 记着用户澄清的流程：
    **结算 → 翻牌 → 篝火页（再次挑战 / 选择其他地下城 / 返回城镇）→ EPLP**；因此当时特意把超时登记点从"结算包发出"改到"翻牌完成"，
    免得 20 秒在翻牌中途到点把玩家从选牌界面拽走）。
    ⇒ 队长那侧点了"返回城镇"（party19 `17:22:09/10` 两次 `CMD 75`）→ `settlement-after-card` 正常回城；
    队员那侧没点 → 被 `SettlementReturnTimeout` 兜底送回（**当时是 20 秒**，与参考实现 `town return: eplp timer 20` 一致）。
    全量日志规律（"自己回城"的永远是队长一侧）由此解释，无需改动。
    **【2026-09-30 真机更正（用户）：20 秒是错的】** 原版（官服）的自动返回是**两分钟**，而且**最后 10 秒会在聊天框
    逐秒发系统消息倒计时**——20 秒会把还停在篝火页（"再次挑战 / 选择其他地下城"都还没选）的队员硬拽回城。
    已改：`DungeonPartyService.SettlementReturnTimeout = 120s` + `SettlementCountdownSeconds = 10`
    （`DungeonInstance.TakeSettlementCountdowns` 逐秒取"还剩几秒"，10..1 各播一次；扫描用成员自己的发送句柄发
    **NOTI 12 + `Type16Brown` + 目标 uid 0** 的系统消息 "`N 秒后自动返回城镇。`"，与"负重超限"提示同一口径）。
    官服侧旁证 `[静态]`：确实有**两个**回城定时器——`TimerEPLPReturnVillage::dispatch_sig` @0x08634c06
    （`check_timer_key(party, **20**, key)` → `CParty::ReturnToVillage`）与 `TimerEPLPCommandWait::dispatch_sig`
    @0x08634c6a（key **21**；`GetSelectedEPLPCmd()==3` 时不回城，否则 `GetEPLPState()==2` → 回城），
    两者都由 `TimerDispatcher` 构造（0x08631501 / 0x08631523）。
    **`[未验证]`**：这两个定时器的**装配间隔**没定死——`TimerQueue::InsertTimer` 把第 4 个形参交给
    `SECOND_TO_MILISECOND`（⇒ 那一位是"秒"），而 `CParty::check_allmember_loading` 里 key 21 那一支传的是
    `0x258 = 600`；要断言"600 秒 / 还是别的单位"得先用已知的 4 秒、30 秒（key 16/19）交叉验证参数位序，
    这轮没做完。**用户报的两分钟按 `[真机]` 采信并实现**。
    **下次真机的观察点（改成可判定的）**：队员端结算后应出现**篝火页**并有"**返回城镇**"选项；
    **点它应立即回城**（日志 `settlement-after-card`）；**不点则约两分钟后**被兜底送回
    （日志 `settlement EPLP timeout: forced town return queued`），且**最后 10 秒聊天框每秒一行**
    "`N 秒后自动返回城镇。`"（文案是一行常量，不对就说一声）。
    **【2026-09-30 真机补充（用户）：回城是"全队"一起】** 队长在篝火页点"返回城镇"后，队员**应当跟着一起回**
    （用户原版观察），而此前只有点的人回、其余人各自等超时。
    官服 `[静态]`：`CParty::ReturnToVillage` @0x085aca60 的组队分支**按槽位循环**——
    `for i in 0..3: CParty::_checkValidUser(i) → CUser::make_basic_info(member[i]) →
    GameWorld::out_from_dungeon(member[i])`（成员指针 `party+0x78+i*0x18`，循环增量在
    0x085acf1d-0x085acf2a）；而 `Dispatcher_EPLPCommand::isReTurnToVillage` @0x0825f7f4 只判
    `CUser::getMoveSpace() == 1`（在副本里），**不判是不是队长** ⇒ 任何人在篝火页选"返回城镇"都把全队送回城。
    已改：`DungeonPartyService.PostSettlementReturnsForOthers` 给同实例其余成员各投一次"强制回城"信箱
    （各自的会话取走后走同一条 `ReturnToTownFromDungeonAsync`）；单人/不在实例里不投。
    `[真机]` party22 实测信箱从投递到被会话取走约 **1 秒**（21:56:06 投、21:56:07 取）⇒ "跟着回"是秒级生效的。
    **下次真机观察点**：队长点「返回城镇」后，队员端应在**约 1 秒内**也被送回城
    （日志 `Settlement town return by … pulls N remaining party member(s) back to town.`），
    **不再需要等两分钟**；两分钟那条只用于"谁都没点"的情形。
    **【2026-09-30 最终决定（用户）：不做那条显式判定，保持简单形式】** 曾按"只有通关后结算页的返回才拉全队、
    副本内 ESC 的返回只自己退队"加过一道 `dungeonCleared`（`ClearReward != null`）的门，随后**按用户要求回退**
    （提交 `e60a325` Revert `ed312c4`）：客户端版本不再更换，而本机客户端 **ESC 的返回走 `CMD 45`**（退队语义、
    根本不经过 EPLP 分支），EPLP 只在结算页出现 ⇒ 那道判定没有任何实际作用，只会把 EPLP 分支复杂化。
    现状（代码形态与 `8599052` 逐字节相同，只多了解释性注释）：
    - 篝火页（结算页）点「返回城镇」→ **全队回城**，队伍保留；
    - 副本内 ESC 点「返回城镇」→ 走 `CMD 45`：**自己退队回城，不拉队友**（这是 `7db9866` 的第 13 条行为）。
    `[真机]` 支撑这条分工的观测：全量日志 **24 次 `CMD 75` 全部**发生在 `boss map cleared` / 结算包之后，
    副本内 ESC 的返回 **5 次全部是 `CMD 45`**。
13. **副本内擅自退出 = 退队（✅ 已按官服语义实现，2026-09-30；真机待验）**：客户端上行 **CMD 45 GIVEUP_GAME**
    （副本内"返回城镇"按钮；真机日志 `RX … protocol=45`，party18/party19 各一次）在官服 `CParty::giveup_game`
    @0x085b2baa（`opcode_cmd.txt` 第 45 项 = GIVEUP_GAME）里就是 **`CParty::leave_user(party, user, 2)`**
    （0x085b2f2c / 0x085b33c1，实参是**字面量 2**），而 CMD 13「退出队伍」（`DisPatcher_LeaveParty::process`
    @0x081c4256）也是**同一个函数、同一个实参**（`leave_user(party, user, 2)` @0x081c42f3）
    ⇒ "擅自退出"的退队形态**就是既有 CMD 13 的形态**，不需要新协议。
    `CParty::leave_user` @0x0859c114（1822B）只做 `change_manager`（队长继任/换主机）+
    `GameWorld::send_party_info_to_all`（NOTI 9）+ `send_party_realtime_info`（NOTI 153），**不发 NOTI 10**
    ——NOTI 10 在另一个函数 `CParty::walkout_member` @0x0859ca4a → `CParty::SendWalkout` @0x0859cb00 里
    ⇒ 我们走 `PartyLeaveNotice.Silent`。
    **只对"擅自退出"生效**（同一份实证排除另外两条回城路径）：结算回城 `CParty::ReturnToVillage` @0x085aca60
    的 58 个内部调用里**没有** `leave_user`（所以结算后队伍保留、"再次挑战"不受影响）；副本内死亡
    `DisPatcher_DieCharacInDungeon::process` @0x081c2a12 调的是 `CParty::die_user` 而**不是** `leave_user`
    ⇒ 死亡本身不算退队（**待真机确认**：死亡倒计时被送回城时客户端会不会自己发 CMD 45；若发，就自动走本路径）。
    **实现**：`DungeonPartyService.AbandonPartyForDungeonGiveUpAsync`（只在"确实绑定在**队伍**副本实例里"时动手；
    单人实例/选图界面/城镇里收到 CMD 45 一律返回 false 且**一个包都不发** = 单人零回归）+
    `PartyService.TryLeavePartySilentlyAsync`（把 CMD 13 的成功分支抽成"无回执"入口，不在队里不发失败码）；
    接线点 `EntranceService.ReturnToTownFromDungeonAsync(..., abandonsParty: true)`，位置**在实例解绑之前**
    （与官服同序：`leave_user` 早于 `GameWorld::out_from_dungeon`）。
    **自动化**：`DungeonPartySmokeTests.CheckGiveUpLeavesPartyAsync`（9 条断言：队员退队/队长继任不搬槽位/
    最后一解散 kind=3/没有人收到 NOTI 10/实例不被这次退队动到/单人实例零包/不在实例里零包）；
    两处**变异实测变红后已回滚**（① 通知换成 `Kicked` → 两条"没有 NOTI 10"断言红；
    ② 去掉 `IsParty` 前置判断 → 单人零回归断言红）。
    **证据工具**（可复核）：`work/calls_in.py <addr>+<size>`（官服函数调用序列，capstone + 符号表）+
    新增 `work/dis_range.py <func>+<size> --show lo,hi`（**从函数起点对齐解码**再打印窗口——不对齐会从半个指令
    解出垃圾，第一版就是这么读错第二处 `leave_user` 现场的）。
    **证据与类别**（六类标记见 `.recon/onboarding.md` §4；命令都可重跑）：
    - `[静态]` **CMD 45 → 退队**：`work/xrefs_elf.py 0x085b2baa` 给出 `CParty::giveup_game` 的 6 个调用点，
      其中 **`DisPatcher_GiveUpGame::process+297`**（0x081c41dd）就是 CMD 45 的派发器（该派发器 @0x081c40b4
      先 `check_error`，再按 `CUser::get_state()` 分支：10 → 死亡之塔、12 → Boss 塔、其余 → 普通副本
      `CParty::giveup_game`）；两处 `leave_user` 的**实参 2** 由
      `work/dis_range.py 0x085b2baa+0x976 --show 0x085b2ef0,0x085b2f40` 读出（0x085b2f2c：`[esp+8]=2`）。
    - `[静态]` **CMD 13 是同一个调用**：`DisPatcher_LeaveParty::process` @0x081c4256 → 0x081c42f3
      `leave_user(party, user, 2)`（同一函数、同一实参 ⇒ 报文形态必然相同）。
    - `[静态]` **缺席证据**（`work/xrefs_elf.py 0x0859c114` 的 **13 个调用点完整清单**里没有结算/死亡族）：
      `Dispatcher_EPLPCommand::dispatch_sig` @0x081feedc、`TimerEPLPReturnVillage::dispatch_sig`、
      `TimerEPLPCommandWait::dispatch_sig`、`DisPatcher_DieCharacInDungeon::process` @0x081c2a12
      **都不在**调用 `leave_user` 的清单里 ⇒ 结算回城（含 20 秒兜底）与副本内死亡都不会退队。
      （`ReturnToVillage` 自身不调用只是第一层；列全调用点才算数。）
    - `[真机]` 客户端确实发 CMD 45：`server-party18.log:468`（`protocol=45 … body=2000`）、
      `server-party19.log:432`（`body=2100`）；全量日志计数 `protocol=45` 5 次、选择界面的返回 `protocol=142`
      7 次、**`protocol=135` 0 次**（135 才是官方表里的 BACK_2_VILLAGE）。
    - `[静态]+[未验证]` **邻接发现（本轮刻意未改，待拍板）**：官方
      `Dispatcher_Back2Village::dispatch_sig` @0x0820559a 在 `CParty::getManager(party) != user`
      （**非队长**）时 `leave_user(party, user, 2)`（0x08205655；队长走另一支 → `ReturnToVillage`）。
      但它是**官方 135** 的派发器，而我们的客户端在选择界面发的是 **142**（官服表里 142 =
      GUILD_MEMER_LIST_NEXT，不是同一条命令）⇒ **不能据此改我们的 142 行为**，要单独判定
      （真机观察点：队员在**选图界面**点"返回城镇"后，城镇里队伍还在不在——现在的实现是**保留**）。
    - `[静态]+[未验证]` `DisPatcher_SelectDungeon::process` @0x081c8102 另有
      `leave_user(party, user, **3**)`（0x081c828d；type=3 与 CMD 13/giveup 的 2 **不同**）——
      "选图阶段被移出队伍"的另一条语义，我们没实现（我们走 `party-selection-cancelled`，保留队伍）。
    **真机观察点**：见 `work/RUN-verify-A.md`（队员副本内点"返回城镇"→ 城镇里**不应**再显示组队、
    队长端副本内队伍只剩自己；日志 `gave up inside party dungeon … and left the party`）。
    **【2026-09-30 首次真机（party22）：队员侧 ✅ 生效；队长侧"看起来被解散"需要下一轮判定】**
    `[真机]` 21:53:23 队员（圣骑士/uid 1）发 `CMD 45 body=1300` → 服务端 `Party roster 2 published:
    nameSel 0->1, **1 member(s)**` + 给**两个会话**各发一份 NOTI 9（`payload=01000200000104020000FFFF00FFFF00FFFF0000`
    = partyId 2、只有槽 0 的 uid 2 = 队长、leaderSlot 0）+ 2 秒后补播一次；**没有任何 kind=3（解散）**。
    21:53:27 队长（test/uid 2）自己发 `CMD 45 body=1900` → 这时才 `Party 2 dissolved`（kind=3）。
    ⇒ **服务端在队员离开后确实让队长留在"单人队伍"里**，解散来自队长自己按的返回城镇（4 秒后）。
    用户看到的是**队长端界面上队伍消失**：这与客户端对"成员数 < 2 的名册跳过 HUD 组激活"
    （party-protocol-recon 记的 `DNF.exe 0x41BEA5` 那道门）一致，属客户端显示行为，不是服务端把队伍拆了。
    **下一轮判定（可判定化）**：队员在副本点返回城镇后，**队长端**分别看两处——
    ① 队伍 HUD（预期：没有别人时就是空的，与官服同源）；② **队伍信息窗口 / 推荐伙伴列表**
    （预期：仍显示自己这支 1 人队）。若 ② 也空了，那才是缺陷，届时用 `work/probe_party_table.py`
    读队长端活内存的 `CNRDPartyManager`（四槽 + `G+0x20`）来区分"服务端没保住"还是"客户端没渲染"。
14. **每次进副本都会再刷新一次房间**（早期为"让地图加载器重建外来角色"加的补丁，`13f3f2a`/`017425c`）：
    **用户已确认现在可以去掉**——`mode 1` 补包已承担"建外来角色模型"的职责；去掉后应同时消除
    "刷新后位置短暂错位、需要再移动一次才恢复"的现象（用户 2026-09-30 观察）。
15. **圣职者（Priest）buff 技能跨端不同步** —— **已重新定性（2026-09-30 官服二进制实证）：不是"我们漏发一条包"，而是它本来就不走服务端。**
    官服 `DisPatcher_UseSkill::read/process`（`0x081CAC5E`/`0x081CADAA`）读四个字段
    （`+13` 必须为 1、`+14`=技能号、`+15`=**目标 u16**、`+17`=另一个 u16），然后只做两件事：
    反外挂校验（`CHackLog_InvalidUsingSkill`）与 **`CParty::use_skill`（`0x085A77D8`）→ `CParty::OnUsedSkill`（`0x085B5F8E`）
    ＝按队伍槽位累加使用次数/统计，`OnUsedSkill` 里**没有任何发包**。**服务器不会把"某成员用了技能"广播给队友。**
    ⇒ 目标端能看到特效/回血，靠的是**客户端↔客户端通道**（官服架构里的 relay/P2P），也就是 §2.7 第 9 条那个仍未结案的
    "游戏 UDP 去向审计"（我们的 UDP 审计至今显示成员只发 NAT 探测/keepalive，`mt=0`）。
    **两条路可选（待用户拍板）**：① 按官服架构补 P2P/中继（正路，工作量大，且第 9 条先要判定客户端把游戏数据发给了谁）；
    ② 服务端**模拟**：收到 `CMD 41` 后主动向同实例其他成员广播一条等价效果包（偏离官服协议，但便宜、可先验证"效果能不能显示/回血"）。
    **注意**：我们现在对 `CMD 41` 只回一个成功字节（`CreateUseSkillReply`），连"用技能"这件事都没告诉队友——这与①/②的取舍无关，
    是**确定的缺失**（只是这条缺失在官服里本来也不由服务端承担）。
16. **2026-09-30 真机 party26（`server-party26.log`，用户九条反馈）逐条判定**
    （证据都在那份日志里，行内给了时间戳，可 grep 复核）：
    - **① 队长中途返回 → 只他自己退队，队友变 1 人队 ✅**：`23:27:00` `gave up inside party dungeon 1` +
      `left party 1; 1 member(s) remain` + `returned user 1 through giveup-game`。
      **【同日更正（用户）：我先前把"队友随后自己返回 → 无队伍"判成"✅ 也是对的"是错的】**
      官方语义是 **最后一人擅自退出时队伍保留**——他仍是这支 1 人队的队长
      （`CParty::leave_user` 只有 `IsSinglePlay` 那支才 `CParty::destroy`；玩家自建的 1 人队不走那条）。
      **已修**：`PartyService.TryLeavePartySilentlyIfOthersRemainAsync`（**只有队里还有别人**才真把他移出队伍；
      不在队里/只有自己 → false 且一个包都不发）+ `DungeonPartyService.AbandonPartyForDungeonGiveUpAsync`
      改用它，并记 `… as the only member; party kept (he stays its leader).`。
      真机 party26 `23:27:13` 那条 `Party 1 dissolved` 是**修之前的旧行为**（当时的 build），修后应变成"保留"。
      **显式 `CMD 13` 退队不变**（`23:33:47` 那条 `protocol=13 → Party 1 dissolved` 仍然是对的：
      玩家自己点了退队，最后一人退队就解散）。
    - **② 战斗途中弹 `error : 3525:100` 后闪退（队长端）⚠️ 仍是未解那一条**：`23:29:16` 队长 `CMD 48`
      推房间（`arrived=1/2`）→ `23:29:17` 双方 `CMD 40` → 全员加载 → 之后队长只收到正常的
      36(疲劳)/37(经验)/38(怪物死)/39(拾取)，**崩溃窗口内一条 `protocol=2`（外观/详情）都没有**
      ⇒ **不是新发的 mode-1 记录触发的**（party17 那条 `3525:100` 是"mode-1 里写真实经验"的 A/B 结论）；
      队长端在死前发了 `protocol=160`(SDC 伤害校验)×2 + `42`(怪物死) + `118`(未知)，然后
      `23:29:33` 离开副本/退队/断线。与 party17 静态定位一致：该弹窗来自客户端自己的校验模块
      （`sub_8BECD0` 回调、`sub_402030(this+0x10)==1` 条件、硬编码串 `"error : 3525"` @0xA89E4C）
      ⇒ **按"偶发崩溃"统一处理，需要崩溃窗口内有看门狗采样**（用户已计划单独测）。
      **位置不同步**（队员视角两人都在门口并进了下一房、队长视角不同）＝ §2.7 已知的过图/位置瑕疵，未解。
    - **③ 通关后队长用 ESC 退出 → 队友 1 人队；队友再点结算页返回 → 仍是 1 人队 ✅**：
      `23:33:42` 队长 `CMD 45`（退队+回城）、`23:33:44` 队友 `settlement-after-card`（结算回城不退队）；
      `23:33:47` 队友自己发 `CMD 13`（`protocol=13`）才解散 ⇒ 收尾也是用户自己的操作。
    - **④ 通关后队长点菜单返回 → 两人一起回 ✅**：`23:36:35`
      `Settlement town return by test pulls 1 remaining party member(s) back to town.` +
      `forced town return for 圣骑士 (settlement-party-return)`。
    - **⑤ 通关后双方都不点 → 倒计时结束一起回 ✅**：`23:41:23~23:41:32` 双方各收 10 条
      `protocol=12 type=16 targetUid=0` 系统消息（正文 `N 秒后自动返回城镇。`，已按 936 解码核对），
      `23:41:33` 两条 `settlement EPLP timeout: forced town return queued` + 两个人各一条
      `forced town return (settlement-eplp-timeout)`。
    - **⑥ 篝火页每次通关翻牌后都出现 ✅**（与 §4 第 12 条一致）。
    - **⑦ 队员在选图界面点不了「返回城镇」⚠️ 开放**：本轮日志里 `protocol=142` 只出现 1 次
      （`23:26:38`，那是**组队之前**队员单独在选图界面时发的，服务端照常送回城），
      组队后的选图期没有第二次 ⇒ 无法区分"客户端把按钮置灰"还是"没点"，需专门的对照测试
      （单人 vs 组队选图各点一次，看是否有 `RX … protocol=142`）。
    - **⑧ 圣职者 buff 无特效/无回血 ❌ 未解**＝第 15 条那条（客户端↔客户端通道，未实现 P2P/中继）。
    - **⑨ 队员端队伍 HUD 里队长血量显示半血（实际满血）⚠️ 找到了机制**：官服 `CParty::leave_user`
      每次都会调 `CParty::send_party_realtime_info` @0x0859cd24（发 **NOTI 153**），
      载荷构造器 `CParty::get_party_realtime_info` @0x0859cbac 的写包序列已解出：
      `put_byte(计数)` 后**按槽位循环**：`put_short(uid)` + `put_byte(CUserCharacR[+0x75])`（**这一个字节
      就是 HUD 上那格的状态/血量百分比**）+ `put_byte(0)` + `put_byte(isHelpAbuseParty(member))` + 2 个 byte。
      **我们从未实现 NOTI 153**（`grep 153 src/` 只命中无关数字）⇒ 客户端那格一直是进图时的旧值
      （用户看到的"半血"）。修它需要两半：服务端**收**客户端上报的自身实时信息（官服对应 **CMD 169**；
      我们的客户端本轮日志里 `protocol=169` 出现 **0 次**，所以还要先判定这个 2008 客户端用什么上报）
      + 服务端**广播** NOTI 153。**下一步的候选任务**（`[静态]` 已有一半证据，还需要客户端侧取证）。
17. **2026-10-01 00:13 崩溃捕获（party27：`[真机]` 现场 + 看门狗）**：这次崩的是**队员端**
    （PID 54136；`server-party27.log` `00:13:11` 会话 `b64ac305`（圣骑士/uid 1）`ended with an error`
    → `left dungeon instance 2` → `left party 2; 1 member(s) remain`）。
    看门狗（前台 5 分钟窗口：`work/watch_clients.py 5 G:\tmp\party27 0.5`）抓到 **2 组成对的
    EXCEPTION_RECORD + WOW64 CONTEXT**（`work/recover_exception.py`），`00:13:07` 与 `00:13:09` 两次
    **逐字段相同**：`ACCESS_VIOLATION`（读，`badaddr=0x223B0000`）、`fault Eip=0x009019C1`
    （= `DNF.exe+0x5019C1`）、`Esp=0x001AFDC0 Ebp=0x001AFDF0 Eax=0x04DB0640`（= 全局 `[0xcfd458]` 的对象）、
    `Ecx=0x3F800000`（上一轮读到的 1.0f）、`Edx=0x223B0000`、`Esi=0x06F80D44`、`Edi=0x04DB0640`、`Ebx=0x00050663`。
    崩溃指令（`friends-protocol-recon/work/insns.txt` 全量反汇编表）：`009019C1 mov ecx, dword ptr [edx]`，
    而 `edx = [eax+0x44] + ([esi+4])*4`（`009019B1..009019BE`）⇒ **索引越界**（`[esi+4]` 那一位坏了）。
    所在函数入口 **`0x901900`**（prologue `push 0x9902e2` = SEH 作用域；调用点只有 `0x8C7FEE` 与 `0x901ECD`）。
    **时间线**：崩溃前 5 秒（`00:13:06`）队员端**只收到一个包**——`TX protocol=29 length=63`
    （**NOTI 29 START_MAP**，即过图到下一房间）⇒ 与用户观察"队员视角两人都在门口、进了下一房之后出事"一致。
    **模块归属**：第 16 条记的 `error : 3525` 来自 `0x9014xx` 那套校验/报错模块（`sub_901430` 注册回调），
    本次崩溃在 **`0x9019xx`**——**同一个模块** ⇒ 支持用户"这是客户端自己的保护措施"的判断
    （崩在客户端校验路径上，不是"某个服务端包解析不了"那么简单）。
    **同一轮的好消息**：`00:14:26` 出现
    `Party member a27fdf04… gave up inside party dungeon 2 **as the only member; party kept (he stays its leader)**`
    ⇒ 第 16 条 ① 的修复（最后一人保留 1 人队）**真机生效** ✅。
    第二轮看门狗（`00:18:03–00:21:04`，`G:\tmp\party27b`）**没有异常记录** ⇒ 偶发。
    **下一步**：① 再崩就再抓（每份现场都能收窄 `[esi+4]` 的来源）；② 只用**只读**手段做静态
    （`ida-dec`/`ida-script` 是 mutate 类工具、本会话审批被拒 ⇒ 用 `insns.txt` 全量反汇编 +
    自写 capstone 扫 `DNF.exe` 的字符串引用）；③ 与金标准抓包（`ref-proxy`）**逐字段比对我们的
    NOTI 29 载荷**（`protocol=29 length=63` 那条）——崩溃紧随过图，最值得比的就是它。
    **【2026-10-01 追加：找到了客户端自己的"错误上报"——`CMD 118 TRACE_ERROR`】**
    官方命令表逐字：`118 TRACE_ERROR`（`Dispatcher_TraceError::dispatch_sig` @0x08204b0c **只做
    `PacketBuf::get_byte` ×2 + `cMyTrace`**，不回包、不改任何状态）。`[真机]` 两轮、两个不同的端，
    客户端在**弹 `error : 3525:100` 或崩溃之前的最后一个上行包**都是它，body 逐字段相同：
    - party26 `23:29:33` **队长端**（会话 6886c6cd）`body=3B000801000000`，随后**干净**断线（无 WARN）；
    - party27 `00:09:14` **队员端**（会话 1b533fe0，**组队成功后 8 秒、还在城镇里**）
      `body=1F000801000000`，随后也是**干净**断线（无 WARN）。
    ⇒ ① `error : 3525:100` 会走"客户端自己关会话（回选人）"这条路（进程还活着），与 party17 那次
    "优雅关闭"同一个模式；② 它**不依赖副本/过图**（第二例在城镇里、刚组完队），所以不是 NOTI 29 专属。
    **已实现**（提交 `诊断：识别并记录 CMD 118 TRACE_ERROR`）：`GameProtocolEngine.TraceErrorCommand = 118`、
    `EntranceService.TryReadTraceError`（u16 序列后的两字节 = code/flag；两例都是 `code=8, flag=1`）、
    会话收到就记 Warning（code/flag/整包 hex），**不改任何状态**（照官服）。下一份现场会自带这个码。
    **与 line 626 那次硬崩的区别**：party27 `00:13:11` 队员端是**硬崩**（`ended with an error` = RST +
    看门狗抓到 `ACCESS_VIOLATION @Eip=0x9019C1`），而 party26/party27-00:09 两次是**客户端的优雅关闭** ✅
    ⇒ 两条不同的失败路径要分开查（前者是客户端校验模块里的越界，后者是客户端主动退出）。
    **【2026-10-01 00:31–00:38 party28：一轮"没有崩"的对照（`[真机]`）】** 看门狗前台 7 分钟
    （`work/watch_clients.py 7 G:\tmp\party28 0.5`）覆盖两端各 **2582 / 2366** 次采样，
    `recover_exception.py` 两次都报 **`records seen: 0`**；服务端 `server-party28.log` 里
    **没有任何 `TRACE_ERROR`**、也没有 `ended with an error` ⇒ 这一轮客户端一次都没报错。
    同一轮里确认到位的行为：`00:31:18` 与 `00:34:54` **两次**"最后一人擅自退出 → `party kept
    (he stays its leader)`"；`00:33:53` 结算页返回把队友一起带回（`returned user 1 through
    settlement-party-return`）；`00:38:42/45` 两人各一条 `settlement-eplp-timeout`（两分钟兜底）。
    **金标准比对的障碍（记一笔，别重复踩）**：`captured-run1/captured-run2` 里 game 通道（port 30011）的
    **载荷是加密的**（高熵密文；帧头可见 `00 01 00 <len u16> …`），要拿参考实现的 `NOTI 29` 逐字段比对，
    必须先用客户端载荷密码（本项目 `GameClientPayloadCipher`）解出明文——这是一件独立的事；
    参考实现的**可读来源**是它自己的 `_logs/game.log`（行为/顺序），不是抓包载荷。
18. **翻牌/奖励面板：两边看到同一个人翻的内容（2026-10-01 用户真机；已定位到代码，待修）**
    **现象**（用户截图）：奖励面板每行是"成员名 + 卡 + 金币"，两个客户端都把自己那一份**填进了两行**
    （A 端两行都是"破损的武士刀 +12 金币"、B 端两行都是"+11 金币"）；而**付费/高级那两行两边一致且正确**
    （test 行都是"破损的青铜肩甲"、圣骑士行都是"破旧的手套"）。
    **定位**：面板的"免费卡"那组来自 **NOTI 35（`CLEAR_REWARD`）的第 1 组 4 列**，我们
    `GameProtocolEngine.CreateClearDungeonReward`（`src/DFLegacy.Protocol/GameProtocolEngine.cs:3778-3787`）
    **把 4 列全部写成收件人自己的 `freeItemId/freeGoldAmount`**（注释原文："Keep all four columns present
    even in solo play"），而调用点 `EntranceService.cs:14062` 每个会话只为自己生成、只发给自己
    ⇒ 客户端按列渲染各成员行时，就成了"每个人看到的那一行都是自己"。
    **付费那组为什么是对的**：它只写 `partyIndex == localPartyIndex` 那一列，而**各成员的选择由
    CMD 74（`SendEachCardInfo`）广播给全队**（`BroadcastCardSelectionSnapshotAsync`，真机 party10 已验证过
    广播），所以两边都能看到对方的真实付费卡 ⇒ 两组行为差正好把根因钉在"NOTI 35 的 4 列"上。
    **修法（两步）**：① **先确认列↔成员的映射**——把第 1 组的 4 列按
    "本地列 = 自己的奖励、其余列 = 一个可辨认的哨兵（如 0 金币）"发一次，真机看队友那一行是"变空/显示哨兵/
    还是照旧"，一次就能判定客户端是"按列取成员"还是"只用本地列"；② 若确认按列：给
    `DungeonInstance` 加"每成员已生成奖励"的共享登记（各会话生成后发布），`CreateClearDungeonReward`
    改成**按列**写（列 i ← 槽位 i 成员的 `FreeItemId/FreeGoldAmount`），并处理"队友还没发布就被先发"的时序
    （与既有 `NOTI 30` 全员加载门槛同一套路子：齐了再发，或各自发布后统一由一次广播收口）。
19. **拾取/金币两边都刷屏（2026-10-01 用户真机；已定位，待修）**：用户现象——**每端都打印"获得…"**：
    自己那份无前缀（`获得[残破的萌芽短靴]1个`、`获得20金币`），对方那份带名字前缀
    （`[test]获得[残破的萌芽短靴]1个`），**且对方还会打出"获得0金币"**。用户要求（原版语义）：
    **只有拾取者本人该看到这些行**。
    **定位**：`EntranceService.BroadcastPickupToInstanceAsync`（定义在 `EntranceService.cs:2226`）把**同一份**
    拾取包广播给全队，**5 个拾取点全都调它**（金币 13554、强化 13613、宠物 13754、时装 13843、道具 13906）；
    拾取者本人那份是先单独发给自己的（如 13903）⇒ 别人收到广播那份就打印了带名字的行；
    金币那条别人打出 0 = 客户端按"自己这边加了多少"渲染同一份广播包。
    `[静态]` 官服对照：`DisPatcher_GetItem::process` @0x081c35c0（CMD **46** = GET_ITEM，命令表逐字）里
    拾取包由 `put_header + put_byte + put_short(CUser::get_unique_id) + put_int` 构造 = **7 字节**，
    然后 `GameWorld::send_all`（**确实是广播**）——但它的载荷**只有 uid + 一个 u32**，
    **不含我们那 12 字节里的"物品入包/落地"信息**（我们的 `CreateInventoryItemPickup` 写
    `groundId + pickerUid + 4×0 + pickerUid + destinationSlot`）。同一函数里先 `CParty::IsRoutingState`
    （**掷骰/路由状态**）再 `CParty::get_item` ⇒ 官服的"广播"更像"告诉全场这件地面物没了"，而"谁得到什么"
    由各自客户端按自己的入包信息显示。
    **修法**：把拾取拆成两份——**拾取者本人**收现有那份（打印行 + 入包），**其余成员**只收一条
    **不含物品细节的"地面物移除"**（照官服那 7 字节口径），从而"别人看不到你的获得内容"。
    **待真机确认的一点**：那条短包能不能让队友客户端**把地面上的东西删掉**（若不能，会留幽灵物件——
    那就得另找一个只删不刷屏的形态）。`[未验证]`
    **【2026-10-02 更正：这条"修法"在本客户端是空操作，旧结论作废】**
    `[静态]` 三处新证据（都可重跑，命令见 `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/progress.md`）：
    ① 官服那条 7 字节广播**不是 NOTI 39 的变体，而是 `NOTI 86 SERVER_BROADCAST`**（class 0 / cmd `0x56`），
       载荷 = `u8 子类型 5` + `u16 拾取者 uid` + `u32 索引`，由 `DisPatcher_GetItem::process` 的
       `0x081c3a42`–`0x081c3ac1` 段构造并 `GameWorld::send_all`；
    ② 本客户端 86 号处理器 `0x427104` **只比较子类型 1 与 2**（文本广播），其余一律
       `jmp 0x42740F` → `jmp 0x42A069`（公共收尾 = 直接返回）⇒ 官服发的 **5 什么都不做**；
    ③ 客户端里删地面 actor 的函数 `0x417c40` **只有两个调用点**（`0x4224CB` 在 NOTI 39 内、
       `0x4229BD` 在 NOTI 93 内），两处之前都有 `cmp <载荷 uid>, myUid; jne <尾部>`
       （myUid = 全局 `0xb75a40`，经 `0x4052d0` → `registry->Find(uid)` 证实是 uid）；
       且 NOTI 39 那条在删除后**立刻把物品插入本地背包**。
    ⇒ **"另发一条移除包"在本客户端无法实现移除**（发了也看不到任何变化）；`[作废]` 指向上面的"修法"与
    `GameProtocolEngine.cs:3682-3690` 的注释（该注释**待 S5 改写**——Phase 0 明确不改生产代码）。这与本文件既有的
    `EntranceService.cs:14006-14010` 注释（"客户端已乐观隐藏，CMD 46 error 0x15 负责回滚"）是同一条客户端机制的两面。
    **P4 的最终口径**：服务端按官服语义对齐（S5 会补发 `NOTI 86(5)`，广播给全场），
    **队友端"看到消失"不在本客户端能力范围内**；队友点幽灵物件被服务端正确拒绝（`Ignored invalid or duplicate pickup`）。
    替代方案只有"整间房重建"，代价是会把队友的位置/镜头重置（见第 14 条对房间刷新的记录），不采用。
    相关清单条目：`PD-3001`/`PD-3002`/`PD-3003`（`alignment-inventory.md`）。
20. **原版拾取分配规则（用户 2026-10-01 给出，待实现）**：**蓝色及以上装备**在拾取时要**掷骰**，
    随机分给一名队友；**白色装备、消耗品、材料**则是**谁捡到归谁**。
    我们现状是"**全队共享、先到先得**"（第一轮用户拍板的 §7 决策 5），与这条原版规则不同 ⇒ 需要改归属模型。
    `[静态]` 官服侧已经有对应子系统可查：`CParty::IsRoutingState` / `CParty::get_item` /
    **`CSpecialItemRoutingManager`（`ProcessFailEtc`、`ProcessGiveUp`——后者正是 `CParty::giveup_game` 的调用点之一）**
    ⇒ "掷骰路由"在官服是一套独立管理器，值得照它实现，而不是我们在会话里随手随机。
21. **2026-10-01 两处修复 + 一条很可能解释硬崩的线索**
    - **拾取刷屏已修**：删掉 5 处 `BroadcastPickupToInstanceAsync`（金币/强化/宠物/时装/道具），
      拾取包**只发给拾取者本人**；`CreateInventoryItemPickup` 注释里记了官服那 7 字节广播形态
      （`put_byte + put_short(uid) + put_int`）与"将来若要保留地面消失语义该怎么补一条移除包"。
      **待真机确认**：队友端"获得…"不再出现；**并且**地上那件东西在队友屏幕上是否仍会消失
      （若不消失 = 需要补一条不含入包信息的移除包）。
    - **门口集结诊断已加**（**不改变行为**）：会话每收到一次 CMD 48 就记
      `Door assembly diagnostics: <名字> (uid …, slot …) reported MOVE_MAP (tx,ty) from room (fx,fy) at (x,y);
      decision=…, arrivedCount=…, partySize=…`，与既有的
      `party room advanced … (arrived=N/M)` 对齐，就能直接看出"服务端先推进、另一客户端还没到"。
    - **🔑 关键线索（很可能就是硬崩诱因）**：CMD 48 的**组队分支故意用 `memberCount: 1` 登记**
      （`EntranceService.cs` 门口集结段；注释写明理由：真机 2026-09-29 证明 **2008 客户端不会每个队员都上报
      CMD 48**，"全员到齐"永不成立 ⇒ 用"**立即推进 + 其余人被强制**"兜底）
      ⇒ **只要一个人报到，服务端就把房间推进**，另一个人可能还在后一房走动 ⇒ 两边数据不同步，
      正对上用户 2026-10-01 的推断（"服务端觉得两人都到了、某个客户端还没到"）。
      **下一步候选修法**：给第一个报到者开一个**短宽限窗口**（复用既有倒计时机制，例如 1.5–2 秒内等其他人；
      到点仍按现在的强制推进兜底）——既不破坏"现在能开门"的兜底，又能消掉绝大多数不同步窗口。
22. **2026-10-01 门口宽限窗口已实现（第 21 条那条线索的修法）**
    - **实现**：CMD 48 的组队分支不再写死 `memberCount: 1`，改为传**真实人数**
      （`EntranceService.cs` 门口集结段）→ `DungeonDoorAssembly` 在"有人在门口、又**没过半**"时开一个
      **宽限窗口**（`ServerOptions.Dungeon.DoorGraceSeconds`，默认 **2 秒**，0 = 关闭并退回旧行为）：
      窗口内全员到齐 → 立即推进；**窗口到点仍有人没报 → 照旧强制推进**（门绝不会永远打不开）。
      过半（严格 &gt; 人数/2）时**不开**窗口，让既有的 10 秒倒计时独占节奏（既有集结行为逐字不变）。
      两条兜底路径都带 `Forced=true`，靠新增的 `DungeonDoorOutcome.GraceExpired` 在日志里区分：
      宽限窗口到点记 `Door assembly grace window (Ns) expired … (arrived=A/T)`。
    - **诊断行已扩展**：`Door assembly diagnostics: …` 追加了 `doorGraceSeconds=` 与 `graceDeadline=`，
      真机复验时把"谁几点报的门""窗口几点到"和 `party room advanced …` 对齐即可判定窗口够不够长。
    - **自动化**（`DungeonPartySmokeTests`，全绿；**变异验证**：把 `DoorGraceSeconds` 默认值改成 0 →
      5 条断言变红，说明"配置默认值 → 实例 → 状态机"这条接线真的被测到了）：
      ①单人上报不再立刻推进、窗口为 `now + 配置值`；②窗口未到点不推进；③窗口到点强制推进
      （`GraceExpired=true`、清空集结）；④窗口内第二人到齐 → 立即推进且不遗留窗口；
      ⑤**没到过半才开窗口**、过半时窗口恒为 null；⑥扫描闭环：两人队只有一人上报 → 窗口内房间不动、
      到点两人一起换房（上报者先拿 CMD 48 回包再收 NOTI 29，未上报者只收 1 条 NOTI 29）。
    - **⚠️ 必须真机复验的疑点（本次证据反而指向"2 秒可能不够"）**：`server-party28.log` 里
      fb805b2a 会话 7 次换房全部上报、88037268 会话**一次都没上报**（`RX … protocol=48` 计数 7 : 0）。
      也就是说这一次 run 里另一方**完全没有**在窗口内上报过；若"被拽进新房的客户端要 7 秒左右才会
      在下一次进房时报 CMD 48"（party28 `00:30:37` 服务端发进房包 → `00:30:44` 该会话才上报），
      2 秒窗口仍会到点强制推进。**所以真机复验要看两件事**：队友端是否出现
      `Door assembly diagnostics` 里第二个人在窗口内的上报；以及窗口是否需要调大（改 `server.json`
      即可，不必重新发版）。窗口调大只增加等待、不改变兜底，仍不会重演"门永远打不开"。
      **不要**直接把 `memberCount: 1` 改回 `MemberCount`（那是本次修法之前的退化写法，会重演"门永远打不开"）；
      正确写法是传**真实人数**并让宽限窗口兜底。
23. **2026-10-01 08:20–08:25 真机一轮（`server-party30.log`，本机双客户端）："队员端弹 `error : 3525:100` 后自行关会话"复现，且这次**不是**门口不同步**
    - **现场**（用户截图 + 日志）：两人同镇同区 → 组队 → 进副本"洛兰 冒险级"，**两人一直在同一实例同一房间**（截图里双方同屏、位置几乎重合）⇒ 与"服务端先推进、某人还在后一房"的假设**不符**。
    - **链条**：`08:23:56` 队员端（圣骑士，会话 `8b7fbe22`，角色 `f3e8b44c…`）自己发
      `CMD 118 TRACE_ERROR (code=8, flag=1, body=16000801000000)` → **客户端自行关会话**（`disconnected.`）→
      服务端随后正确地把"该成员离开副本实例 / 离队"处理掉（`left dungeon instance 2`、`left party 2`），
      剩下的人以**单人**身份继续过图并正常结算。**同形第 3 次**（party26 `23:29:33`、party27 `00:09:14` 都在城镇/副本里、都是 code=8,flag=1）。
      ⚠️ 另有一处与 party26 一致的现象：**该客户端此前也在 `08:21:36` 弹过一次同样的东西**（party26 的队长端同样弹了两次），
      即"同一会话在一局里会连弹"。
    - **不是硬崩**：看门狗对两个客户端共 1556 份栈采样（`G:\tmp\party30\stacks_48340.txt` / `stacks_58680.txt`），
      `recover_exception.py` 报 `records seen: 0` ⇒ 没有 `ACCESS_VIOLATION`，是"弹窗后正常退出"，
      与 §4 第 16/17 条记的 `Eip=0x009019C1` 硬崩是**两条不同的失败路径**。
    - **不是本次宽限窗口改动引起**：弹窗发生在"房间清空后约 6 秒"，而这 6 秒里**服务端一条包都没发给队员端**
      （该会话最后一次收包是 `08:23:50` 的杀怪 `NOTI 38` + `NOTI 37`）；同一轮里单人过图（`partySize=1`、`graceDeadline=none`）完全正常。
      ⇒ 宽限窗口在这轮里**没有被触发过**（队伍实际只有 1 人时按设计立即推进），它的真机复验仍待做。
    - **静态定位（`insns.txt`，DNF.exe）**：弹窗出口 = `sub_8BECD0` → `sub_8BEAD0`；
      `sub_8BEAD0` 只在 `sub_402030(this+0x10)==1` 时把传入的 code 用 `0xA89E4C` 的格式串打出来（三次都是 code=**100**），
      然后 `sub_8B2810` + `sub_799EF0(…,0x23,…)`（0x23 = 35，疑为对话框/消息类型）。
      `sub_8BECD0` 由 `sub_8BEE08`（`008BEE03 push 0x8becd0`）**注册为回调**，走间接调用，
      所以"找 `call 0x8becd0`"是 0 个——排查时别在直接调用上浪费时间（已踩）。
      「谁在什么条件下调这个回调、code=100 是什么校验」**仍未解**，这是下一步的目标。
    - **下一步实验设计（用来分离触发点）**：①房间清空后两人**都站着不动**，看弹窗是否仍在 ~5–6 秒出现
      （分离"清房"与"过图"）；②弹窗出现时**先别点确认**，立刻抓该客户端的包/栈；
      ③换边当队长（看这个 3525 跟"队员身份"还是跟"那台客户端"走）；④单人同角色刷同一副本（对照组，08:21 那次单人已正常）。
    - **✅ 2026-10-01 15:26–15:29 真机一轮组队通关（`server-party30.log`，两个独立客户端目录 `DF` / `Client_test2`）**：
      - **通关全程 0 次弹窗 / 0 次崩溃**（本文件的 `TRACE_ERROR` 仍只有 08:21:36 与 08:23:56 那两条，
        **都在窗口生效之前的 run 1**）；结算正常（双方 `settlement sent`，`party=50` 组队系数生效）。
      - **宽限窗口按设计触发 4/4**：`decision=Waiting, arrivedCount=1, partySize=2` → 2 秒后
        `grace window (2s) expired … forced the party into room (x,y)`。报告方这次是**队员 `ʥ��ʿ`**（uid 1, slot 0），
        与 08:43–08:54 那批（`test` 报）相反 ⇒ 再次印证"谁先到门谁报"。
      - **🔑 决定性发现：只有一方客户端会往服务端报门，另一方一个字节都不发。** 用户观察是"**双方都触发了门**，
        再等 2 秒才进去"，但同一时段两个会话的上行包统计是：
        `ff476772`（圣骑士，报门方）`protocol=48 ×4`、`42 ×21`、`40 ×6`…；
        `d35aedf7`（队长 test）**`protocol=48 ×0`**，只有 `40 ×6`（加载完成）、`42 ×6`、`37`、`160 ×9` 等。
        ⇒ 第二个人的"触发门"**根本没有上行到服务端**（推测走了客户端↔客户端的 P2P/中继通道，见 §2.7 第 9 条那条未结案项）。
        **结论：`DoorGraceSeconds` 再大也没用——服务端等不到那个包**；它只会把"第一个报门者"按 2 秒上限拖一下。
        要真正"等双方都到门"，必须有别的判据（例如用该玩家的 `protocol=42`/位置包判断它接近出口，或实现 P2P→服务端的转发），
        而不是继续调这个窗口值。
      - **待判**：这 2 秒等待是否为"改动引入的净成本"。窗口生效后的 run 2–5 与本次共 **5 轮 0 弹窗**，
        但 run 1（无窗口、立即推进）**有**弹窗 ⇒ "窗口防住 3525"仍只是相关性、非因果。
      - **⚠️ 对照实验踩到的坑（已修，别重复）**：把 `DoorGraceSeconds` 改成 `0` 后真机**直接过不去门**
        （用户原话）。根因是本次改动把登记人数从写死的 `1` 换成了**真实人数**：窗口为 0 时状态机
        **既不开宽限窗口、也永远不满足"全员到齐/过半"** ⇒ `TryAdvanceOnCountdown` 恒返回 null ⇒ 房间永不推进。
        修法：`DungeonOptions.ResolveDoorMemberCount`——窗口 &gt; 0 传真实人数（开窗口等其他人），
        **&lt;= 0 退化传 1**（复用"门口只有我一个人 → 立即推进"），保住"门一定能打开"这条底线；
        测试锁死该不变量（变异验证：改成恒传真实人数 → 断言变红）。提交 `8c14c83`。
        **结论：`DoorGraceSeconds` 只能"调时长"，不能用来"关功能"；要退回立即推进请设 0 并依赖上述退化路径。**
        **`[作废]`（2026-10-03，S4）**：本节整条（宽限窗口 / `ResolveDoorMemberCount` / 倒计时 / 强制推进）
        所描述的机制**已作为自研产物整体删除** —— 官服 `CParty::move_map@0x085a8f4e(987)` 里
        **没有"全员到门口"这种条件**（一次合法请求即推进整队）。现在是 `DungeonInstance.TryAdvanceRoom`，
        不再有窗口/倒计时/退化登记，也没有"调时长"这个旋钮了。详见下面
        "**组队副本官服对齐重写（2026-10-03：S1/S3/S4 完成）**"一节。保留本段仅为追溯当时踩的坑。
    - **✅ 真机复验：宽限窗口已生效，且"等不到第二个人上报"的担心被实验推翻**（2026-10-01 08:42–08:58，同一份
      `server-party30.log`，共 5 轮组队、**15 次**门口集结）：
      - **窗口按设计触发 15/15**：每次都是 `decision=Waiting, arrivedCount=1, partySize=2,
        doorGraceSeconds=2, graceDeadline=…`，2 秒后
        `Door assembly grace window (2s) expired … with 1/2 at the door; forced the party into room (x,y)`
        ——**兜底推进照常发生，门没有打不开**（这正是"不要改回 MemberCount"要守的那条线）。
      - **两个客户端都报过门**（关键）：08:43–08:54 是 `test`（uid 2，slot 0）报，
        **08:56–08:57 换成 `ʥ��ʿ`（uid 1，slot 1）连报 3 次**（用户按"换边当队长"做的实验）。
        ⇒ 之前"某一方客户端根本不发 CMD 48"的推断**作废**：**两边都能发**，只是**谁先到门口谁报**，
        另一方总是慢一拍（每次推进后，落后那方要 20–30 秒后才有自己的下一次上报）。
      - **但 2 秒窗口对这个节奏明显太短**：15 次集结里**没有一次**第二个人赶在 `graceDeadline` 之前上报
        （窗口从未出现 `arrived=2/2`），所以每次都是强制推进。真机需要的是"落后者走到门口"的时间，
        量级看是**秒级偏大**（落后者在自己的下一次上报前有 20–30 秒，其中"走到门"占多少未测）。
        **建议先按 8 秒试**（改 `server.json` 的 `DFLegacy.Dungeon.DoorGraceSeconds` → 重启，不必重新发版），
        看 `arrived=2/2`/`decision=Advance` 是否开始出现、以及 3525 弹窗是否仍不出现。
      - **弹窗（3525）在窗口生效后的 5 轮里 0 次**（08:42 之后 `TRACE_ERROR` 计数 = 0，此前 run 1 有 2 次）。
        这与"立刻推进把握不住节奏的一方才是触发条件"这一解释一致，但**样本仍少，不能当结论**。

**组队副本官服对齐重写（2026-10-03：S1/S3/S4 完成）**

用户 2026-10-02 裁决："整个多人副本按官服逆向逻辑证据**重写**"（六块全含、子系统级、旧实现作废）。执行方式为
**清单驱动 + 分片替换**：设计稿 `docs/superpowers/specs/2026-10-02-party-dungeon-official-rewrite-design.md`、
Phase 0 计划 `docs/superpowers/plans/2026-10-02-party-official-rewrite-phase0.md`、
**对齐清单（54 条，8 字段/条）** `docs/superpowers/plans/2026-10-02-party-official-rewrite-phase0/alignment-inventory.md`
（同目录另有 `client-capability.md` / `slices.md` / `relay-recon.md`；台账与工具在主检出
`.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/`，git-ignored）。

**两层证据纪律（本轮的做事方式）**：语义层 = 官服 `df_game_r`（"应该做什么"）；可达层 = 本客户端 1.0.1.9
（"做了有没有用"）。冲突必须显式裁决（`对齐`/`保留`/`无法实现`/`待真机`），**不能照抄官服**——
本轮已两次证明"官服是更晚的构建、它的包布局本客户端读不了"（`NOTI 27`、`NOTI 29`，见下）。

- **S1 组队本体 ✅ 真机已验证（run #43）**：`NOTI 26` 的载荷在**两个会话里是同一个字节** = 队长（主机）槽位
  （`01:17:13` 队长 slot 0 与队员 **slot 1** 各收 `payload=00`；若发"自己的槽位"队员那份会是 `01`）——
  即提交 `99d55e1` 的改动**真机确认生效**（此前只标"真机未验"）；单人对照一轮仍为 `00`。
  新增判别性用例 `PartySmokeTests.CheckHostSlotIsSharedByEveryMemberAsync`
  （**变异验证**：把 `TryGetLeaderSlot` 改成"返回自己的槽位" → 全量冒烟**只有该用例两条**断言变红；
  既有的 `DungeonPartySmokeTests:1025/1044` 因问的是队长而**存活**）。
  `NOTI 153`/`CMD 169`（队伍实时信息）经查**超出客户端跳转表（> 0x97）**⇒ 裁决"无法实现"，
  用户报过的"队长血量显示半血"由此定性为**客户端能力限制**，不是我们的缺陷。
  证据：`real-client-run-43-server.log`。
- **S3 进副本 ✅ 取证 + 护栏**：`CParty::game_start@0x0859d718(2489)` 建 **3 个包**
  （`NOTI 2` mode1 @`0x0859d75e` → `NOTI 3` @`0x0859d7ec` → `NOTI 27` @`0x0859dd4f`），**之后**才
  `set_host` → `send_host_info`(NOTI 26)；同区域门禁**指令级**：`0x0859d8b9` 取队长 vill、`0x0859d8bd` 比成员
  vill、`0x0859d8c2/c5` 比 area，不等才看 `is_quick_party`。**新裁决（PD-2011）**：官服 `game_start` 里那份
  `NOTI 27` body 是 `int` 开头的**更晚客户端布局**，本客户端处理器 `0x41F16C` 读的是 `byte,byte,short`
  ⇒ **不能照抄官服**（我们 `CreateEnterSelectDungeon` 是对的）。队员入场逐包顺序由 **run #43 真机核对**：
  `23 → 11 → 9×2 → 2(87B) → 2(203B,mode1) → 9×2 → 26 → 11 → 28 → 29 → 36`，与
  `DungeonPartyService.PlanMemberEntry` 的硬顺序（外观 → 名册 → 26 → 11 → 28 → 29）逐条吻合。
- **S4 换房 ✅ 门口真机验收通过（run #44）——自研"门口集结"整体删除（+75 / −923）**：
  `CParty::move_map@0x085a8f4e(987)`（伪码 `official/ida_targeted_out.txt:362-454`）证明官服
  **没有任何"全员到门口"的条件**：判定只看 `CBattle_Field::MoveMap(field, msg[13], msg[14], &out)` 对
  **请求者自己**目标的判定；成功即广播 `NOTI 29` 给全队并清零全员 loaded（`resetFinishLoadingVar`）。
  ⇒ 删除 `DungeonDoorAssembly.cs`（整文件：到达登记 / 2 秒宽限窗口 / 10 秒倒计时 / 强制推进 / `PendingTarget`）、
  `ServerOptions.Dungeon` 的 `DoorGraceSeconds`/`DoorGrace`/`ResolveDoorMemberCount`、`DungeonInstance.Doors`（4 处
  `Reset()`）与 **5 个集结用例**；换房唯一入口改为 `DungeonInstance.TryAdvanceRoom`（**第一个合法请求即推进整队**）；
  组队 `CMD 48` 失败补发错误包（此前**一个都不发**）；扫描改名 `SweepInstanceTimersAsync`
  （**只保留结算倒计时 + EPLP 超时**，门逻辑那半删除）。
  `CBattle_Field::MakePacket` 的 `NOTI 211` 分支**客户端不可达**（> 0x97）⇒ 永不发。
  **run #44 验收**：两次换房各由"先到门的队长"一人上报 → `RX protocol=48` → 给请求者 `TX 48` 回包 →
  **两个会话各一条 `NOTI 29`**；`Door assembly`/`arrived=`/`grace` 日志**各 0 行**；无 `Rejected party MOVE_MAP`。
  顺带验 **PD-2006**：`10:12:12` 两人各发 `CMD 40`（各收回包）**之后**才各收 `NOTI 30` ✓。
  证据：`real-client-run-44-server.log`。
- **PD-2007（静态，Hex-Rays）**：官服 `case 29` 前 6 字段 = `b(curX), b(curY), b(bool@mapInfo+232),
  i32(mapInfo+72), b, b`，而**本客户端按 `b, b, i32, b, u16, b` 读** ⇒ 与 `NOTI 27` 同型：
  **官服字段序不能照抄，我们现有的 `CreateStartMap` 才是本客户端可解析的那一份**
  （run #42/#43/#44 换房全部正常）；官服的"缓存地图"分支同时印证了 `CreateCachedStartMap` 的形态。
  复现：`idat.exe -A -c -S"work/ida_makepacket29.py <out>" official/df_game_r.bin`。
- **仍未归因**：run #44 **队长端**客户端硬崩（`10:12:30` 收 `NOTI 31` 后 6 秒无包、`10:12:36` `IOException`）；
  `protocol=118 TRACE_ERROR` 全轮 0 次（与 `error : 3525` 家族不是同一条路径）；**无栈现场**（该轮未挂看门狗）。
  **必须更正一条旧观察**：此前 party8/10/11/13/15 记的是"死的全是队员会话、与分工有关"——**本轮崩的是队长**
  ⇒ 那条规律不成立。下次复现**必须挂 `client-stacks`**（协议见
  `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/progress.md`）。
- **S5 掉落与拾取（2026-10-03）：静态取证完成，3 条清单裁决改写；代码落地一半**（分支
  `feat/s5-loot-routing`，2 个提交；`.worktrees/s5-loot-routing`）。取证方式：两轮 Hex-Rays
  单函数反编译（`work/ida_s5_loot.py` → 7 个官服函数；`work/ida_s5_routing.py` → `CItem` 判定谓词）
  + 客户端 class=1 命令接收面（`client_surface.py` **新增 `cmd <id>` 子命令**）
  + 5 份归档真机日志 **42 条** `CMD 46` 逐条配对（`work/s5_cmd46_bodies.py`，两个工具 `--selftest` 均 0 failures）。
  - **改写 3 条**：`PD-3006` 待真机→**保留**（本客户端 `CMD 46` **恒 8 字节**、42/42；
    `+0` = 帧的 records 计数、`+1` = 0、**`+2` = groundId（35/35 与结果行声明的 groundId 相等）**、
    `+4/+6` 未验证；官服 `read` 读 9 字段 16 字节 = 更晚构建的布局，我们 `Body.AsSpan(2,2)` 的读法被交叉证实）；
    `PD-3004` 保留→**对齐**（官服 `get_item` 入口 `memset(v136,0,16)`，该数组只在 routing 块内被写
    ⇒ **直取路径官服也是全 0**）；`PD-3010` 待真机→**对齐**
    （门控 = `CItem::check_item_routing` = `isEpicRoutingItem || get_rarity() > 0`；
    **"蓝装以上/阈值 > 3"的口径作废**——`get_rarity > 3` 只是把"是否允许路由"从默认值换成请求方许可）。
  - **`CMD 46` 是三态**：结果 0 → `SendCmdOkPacket(46)`（body `[1]`）；非 0 → `SendCmdErrorPacket(46,code)`
    （body `[0,code,paytype]`）；**`0x7FFFFFFF` = 什么都不发**，而 `get_item` 返回 false 恰好走它
    ⇒ 我们"重复/失效拾取不回包"**与官服一致**（不是缺口）。客户端接收处理器本次首次定位：
    分派器 `0x40F290`（remap `0x417638` + 表 `0x417480`）⇒ `0x414090`，首条 `test al,al; jne 0x417467`
    ⇒ **官方成功包在本客户端是空操作**（照发并标注）。
  - **`NOTI 86(5)` 的 u32 = 物品 id**（不是"索引"）：`CParty::get_item` 的出参 `a6`；官服广播
    还带 3 个本客户端包体里读不出的条件（用户裁决：成功直取即广播，差异写进注释）。
  - **已落地**：`CreatePickupRemovedBroadcast` / `CreateGetItemAcceptance` /
    `CreateInventoryItemPickup(routingScores)` + **5 个拾取成功点全部接上 `NOTI 86(5)` 全场广播
    （不 omit 拾取者，= 官服 `GameWorld::send_all`）+ `CMD 46` 成功回包**；新增 `LootRouting`
    （官服掷骰规则：基础 1 / 轮转 +40 / 职业 +30 / `rand(100-v)`、并列取先到、`0xD8` 拒绝；9 组单测）；
    **删除两条错误注释**（"照官服 7 字节另发一条移除包"、"同实例其他人也要让该掉落实体消失"——后者是幽灵注释）。
    4 条变异验证实测变红后回滚。
  - **T3b 已落地（2026-10-03，分支 `feat/s5-loot-t3b`，2 个提交）：掷骰归属真正生效**。
    ① `3523eb4`（**纯重构、行为不变**）把 5 个拾取点的落库/发包收敛成
    `EntranceService.TryGrantGroundItemAsync`——因为 routing 判给**别人**时物品必须进**得主的背包**，
    而得主的背包是他**自己会话的内存**，别的会话代改会让得主下次落库用陈旧快照覆盖 ⇒ **静默丢件**；
    该提交的正确性用**归一化逐行 diff** 证明（386 行 vs 386 行、只剩 53 行 diff、5 个 hunk 全是有意改动）。
    ② `fa9ab6d` 把 `LootRouting.Decide` 接进 `CMD 46`：新增 `DungeonInstance.DecidePickupRouting`
    （判定与官服 `party+324` 轮转下标**同一把锁**）+ 注册表**拾取信箱**（队列、并入 `HasPendingMail`）
    + `CMD 46` 四态（`RoutingActive`→`0xD8` 生产不可达／`Routed` 且得主不是我→只投信箱、
    本会话不落库不发 `NOTI 39`／其余→直取／`NotAvailable`→静默）；主循环在**处理下一条请求之前**
    排空信箱并由得主会话自己收，再补 `NOTI 86(5)` + `CMD 46` ok（给**请求者**）。
    `--loot-routing` 扩到 **11 组**单测；**变异验证 3/3 实测变红后回滚**（`routingActive` 恒真／
    信箱改单槽覆盖／`HasPendingMail` 丢掉拾取信箱）。
  - **这一步的用户可见变化（部署前必读）**：① 队伍里的可路由物品不再"谁先点谁拿"而是掷骰，
    结果**可能归别人**且拾取者**看不到任何提示**（客户端无骰子 UI）；② `NOTI 39` 的 4 个槽位字节
    现在写**真实骰子分** ⇒ 得主客户端会打印 `[槽位成员名][数字]` 行（用户 2026-10-03 裁决，
    取代 2026-10-01 的"只有拾取者本人该看到获得行"口径）；③ **单人零影响**（候选只有自己 ⇒
    不走骰子、字节全 0，报文与改造前逐字节相同）。
  - **掷骰门控已收窄（2026-10-03 晚，提交 `961cc27`）**：run #46 真机证明"按官服 `rarity > 0` 全开"
    的代价是**玩家侧"点了毫无反应、东西还在地上"**（神枪手唯一一次有效拾取就被判给队友），
    用户裁决**收窄到 `rarity > 3`**（回到 2026-10-01 的"蓝装以上"口径）。
    实现：`LootRouting.RoutableRarityFloor = 3` + `IsRoutableForThisClient(...)`；
    官服语义的 `IsRoutable`（`> 0`）**原样保留**并注明"只是语义层证据，不要当生效门控"。
    **放开条件与做法写在常量注释里**：有客户端反馈手段后把 3 改成 0（这是一处**有意偏离**，
    台账按"部分对齐"记）。变异验证：把常量改回 0 ⇒ 两条断言变红（已回滚）。
  - **run #46 "神枪手捡不起东西"的服务端侧结论：不是 S5/T3b 的缺陷**（日志 `real-client-run-46-server.log`）：
    神枪手整局只发出 **1 个** `CMD 46`（被**正确拒绝**——那件在 13 秒前已被队友拿走，官服
    `0x7FFFFFFF` 语义就是**什么都不回**，客户端"点了先乐观隐藏、没回包就不回滚"⇒ 表现正是
    "点了毫无反应、东西还在"）；他杀 3 只、队友杀 10 只，队友 **12 次**拾取几乎把掉落扫光；
    **且不存在"归属者门"**：`17:00:21` 的掉落 `GroundId=2 OwnerUserId=2`（神枪手的）在 `17:00:34`
    **被队友捡走** ⇒ 客户端允许捡别人的掉落（与官服"非 routing 归拾取者"一致）。
    全场只有 1 次路由，**现在也已被收窄掉**。
  - **仍未做**：① 收窄后的真机复验（还没跑）；② 两处 `[未验证]` 缺口照旧：职业 +30 的
    `check_job_type` / `item kind == 10` 语义（当前恒 false）、
    `isEpicRoutingItem`（`CItem+121`）在物品表里没有对应字段（判据退化为 `rarity > 0`）。
  - **真机 run #45（2026-10-03 14:36–14:59）= 第一次实测就撞上已知家族，S5 被排除**：
    14:42:24 队员端（圣骑士）弹 `error : 3525:100` → 发 `CMD 118 code=8 flag=1` → 会话**干净关闭** →
    用户报告窗口随后消失（没点确认 / 卡住后强杀 ⇒ **无 WER、无 dump、无栈**）。**S5 与本次故障无关**：
    全轮 `protocol=86` **0 次**、`protocol=46` **0 次**、`protocol=39` **0 次** ⇒ **一次拾取都没发生**，
    S5 改到的代码（拾取收尾 + 两个新构包）**一次都没执行**；队员端报错前只收到普通死亡包
    （NOTI 38 ×4 + NOTI 37 ×2），**从未收到 `NOTI 29`**（换房包只给了队长、且在它报错之后），
    也从未发过 `CMD 48`。现场与 **2026-10-01 §9**（`server-party35.log`，同角色、同"走到门口"场景）逐条相同，
    那里已判"3525 是客户端本地对象状态不对，不是服务端包"。**顺带：队长端本轮没崩**——
    `e8e0f280` 从 14:43:32 一直发包到 **14:58:32**（`CMD 161` 每 60 秒一条），14:54:50 自己死亡 → 14:55:00 回城
    ⇒ **run #44 的"队长端硬崩"本轮未复现**（任务 A 的负结果）。归档：`real-client-run-45-server.log`
    （111,163 B，**末尾 25 个假会话是我为顶出 4 KB 日志缓冲而打的 TCP 探测，别当真客户端**）、`.err.log` 0 B。
    台账 `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/progress.md` 有完整时间线与
    "两项方法论坑"（两个客户端跑同一个 `DF\DNF.exe` ⇒ 客户端日志互相覆盖；看门狗必须挂在**队员端** pid 上）。
  - **用户 2026-10-03 三条裁决**：`NOTI 86` 成功即广播 / **B1 忠实实现** `isEpicRoutingItem || rarity > 0`
    的加权掷骰（≈**品级 > 0 的任何物品**，知情后选择）/ `NOTI 39` 槽位字节**发真实骰子分**
    ⇒ **2026-10-01 的"只有拾取者本人该看到获得行"口径被取代**，队友端会出现 `[名字][数字]` 行。
  - 台账：`.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/progress.md`
    （"S5 掉落与拾取：静态取证完成…"一节，含未做项与两处 `[未验证]` 缺口）。

## 3. 可复用的验证方法

### 3.1 中继 / IPv6 端到端

1. 另起一个只监听 `::1` 的服务端（**换端口**，避免与本机服务端冲突），`DataPath` 指向存档的**副本**。
2. 启动中继：`DFLegacy.Relay.exe --server ::1 --tcp <后台端口> --udp <玩法端口>`。
3. 验证：`curl http://127.0.0.1:<后台端口>/health`（公开）与带 token 的 `/api/admin/characters`——先 `POST /api/auth/login`（`Admin.Users` 里的账号，或临时把该实例的 `Admin.Users` 配上）拿 token，否则会 `401`；UDP 用单字节 `0x01` 发到玩法端口，应收到 7 字节回包（首字节 `2`）。

### 3.2 容器的双栈行为

- 绑 `::` 时**分别用 IPv4 与 IPv6 客户端**测 TCP、UDP、后台。
- 容器内 `/proc/net/tcp6`、`/proc/net/udp6` 可确认监听地址；容器内 `curl` 可单独验证 Kestrel。
- **排查提示**：若经 Docker 端口映射的 UDP 收不到回包，先确认没有另一个容器占用同一宿主端口——端口冲突会让新容器停在 `Created` 状态而看起来像"UDP 不通"（这个坑实际踩过一次）。

### 3.3 启动器完整路径（不需要真实客户端）

在沙盒目录里造一个会存活若干秒的替身 exe 作为 `ClientPath`、放一个桩 `Script.pvf`，写一份 `Relay.Enabled=true` 的 `launcher.json`，运行 `DFLegacy.Launcher.exe --no-dialog`，然后看日志是否依次出现：`relay ready` → `Started DFLegacy client process …; entrance=127.0.0.1:…`；客户端退出后应出现 `relay stopped` 与 `launcher stopping`。

### 3.4 客户端自身的 TCP/UDP 全量打印（`net_dump`，2026-10-04）

**为什么需要它**：同机双客户端的组队/副本流量走的是 **Windows loopback**，而抓包看不到 loopback ——
2026-10-04 实测 pktmon 抓了 184 MB、里面**零个 loopback UDP 包**，而两个客户端之间的玩法数据正好就是 loopback UDP。
所以改成**钩客户端自己的 ws2_32 调用**：没有这个盲区、不需要管理员权限、拿到的是任何抓包都要再还原的**原始载荷**。

**怎么开**：`ijl15.dll` 旁边（客户端目录）的 `Config.ini`：

```ini
[Features]
DumpNetwork=1
DumpNetworkPayloadBytes=0   ; 0 = 整包；>0 只打印前 N 字节（用于压缩大包）
DumpNetworkMaxMegabytes=256 ; 打满就停并在日志里写一行提示
```

**产物**：`DNF.exe` 同目录的 `netdump-<pid>.log`，**每包一行、立即落盘**（正在查的那个客户端会崩，
缓冲日志会刚好丢掉最关键的那几包）。下面是自测跑出来的两行原文（`DumpNetworkPayloadBytes=6`，
所以 7 字节的载荷被截断并标了 `(truncated)`；默认 `0` 时是整包、不带这个标记）：

```
2026-10-04 12:14:33.002 T=67076 TX UDP 127.0.0.1:53558 -> 127.0.0.1:53557 len=7 (truncated) hex=11220033FE7A
2026-10-04 12:14:33.002 T=67076 RX UDP 127.0.0.1:53557 -> 127.0.0.1:53558 len=7 (truncated) hex=11220033FE7A
# 时间戳 线程 方向 协议 本地 -> 对端 len 十六进制载荷
```

注意 `len` 是**收到的真实长度**（7），而 `hex` 只是被限制打印的前 6 字节——两者不一致就是"被截断"的信号。

- 方向：`TX` = 客户端发出，`RX` = 客户端收到。
- TCP 的对端来自 `getpeername`（`accept` 返回的新 socket 仍然走 IAT，所以拿得到），UDP 的对端来自 `sendto`/`recvfrom` 自己的参数。
- `hex` 一律是载荷的十六进制，**不做任何重组**：收到什么就是什么。

**钩了哪六个**：`send`/`sendto`/`recv`/`recvfrom`/`connect`/`WSARecvFrom`。
`send`/`sendto`/`recv`/`recvfrom`/`WSARecvFrom` **正是** `DNF.exe` 静态导入的收发包函数
（2026-10-04 用 pefile 核对过完整导入表：另有 `WSAAsyncSelect`、`WSAEventSelect`、`accept`、`select` 等，
但**没有** `WSASend`/`WSARecv`/`mswsock`），所以这个客户端上没有漏口。
**`WSARecvFrom` 只在"已经完成"时记录**：投递到重叠 I/O 的接收会在别的线程完成，不假装已经收到。

**`connect` 是 2026-10-04 定位"选了频道点开始就闪退"时补上的**：这是唯一能回答"客户端到底拨了哪个地址"的调用。
`send`/`recv` 只能看到**已经建立**的连接，而"拨错地址"这种故障**一个字节都不会发**，
所以在补 `connect` 之前，日志里"没有任何别的 socket"既可能是没连成、也可能是根本没连，**无法区分**。
它记录在调用**之前**，因此**失败的拨号也会落盘**，行里带上结果：

```
2026-10-04 14:04:46.492 T=58576 TX TCP-CONNECT - -> 127.0.0.1:7114 len=22 (truncated) hex=636F6E6E6563
# 协议栏固定为 TCP-CONNECT；远端=试图拨的地址；载荷是 "connect=ok" 或 "connect=fail wsa=<错误码>"
```

读法：**会话建立后如果没有 `connect=` 行，就说明客户端没再拨任何地方**（而不是"拨了但没通"）。
自测里有一条反向验证过的用例：往一个刚被关闭的端口拨号，必须出现 `connect=fail wsa=10061` 那一行。

**自动化实证**（不跑游戏也能证明钩子与日志格式正确）：`src/DFLegacy.Ijl15/tests/` 里的原生自测会用**真实 loopback socket**
走一遍钩子，断言发出/收到的两行内容、长度与十六进制，并断言**超过 `DumpNetworkPayloadBytes` 的字节不出现在日志里**：

```powershell
MSBuild.exe .\src\DFLegacy.Ijl15\tests\DFLegacy.Ijl15.Tests.vcxproj -p:Configuration=Release -p:Platform=Win32
# 然后在 artifacts 的 ijl15-tests/<Configuration>/ 下运行 ijl15_tests.exe（它需要旁边的 ijl15.dll 与 Config.ini）
```

**它证明不了什么**：自测证明的是"钩子生效且日志格式正确"，**不代表真实客户端已经把包打出来**——
那必须真机跑一次（下面是判定标准）。

**真机验收步骤**：

1. 确认 `DF/town-test/Config.ini`（或客户端目录）里 `DumpNetwork=1`；`ijl15.dll` 要带 netdump（见下"部署"）。
2. 启动服务端 + 两个客户端，**让两个角色在同一个城镇同屏、并进同一个副本**，做一次当前在查的操作。
3. 看每个客户端目录是否出现 `netdump-<pid>.log`（**每个客户端一个文件，pid 在文件名里**）。
4. 判定标准：**同一次操作里，一个客户端 `TX UDP` 的那几行，应该在对端客户端的日志里以 `RX UDP` 出现、且 `hex` 一致**
   （端口按 NAT/顺序可能对调，`len` 与 `hex` 必须相同）。对得上 ⇒ 这份数据就是"服务端中转/客户端直连"的原始往来。
5. **对不上就是发现**：说明这段数据没走客户端自己的 socket（例如经中继/服务端转发），这正是要区分的结论。

**部署（容易漏的一步）**：启动器只把 `ijl15.dll` 拷进客户端目录，而且**`Config.ini` 已存在就不覆盖**（用户手改的优先）。
所以改了 `ijl15.dll` 的代码后要**同时**更新客户端目录的 dll；改了 `DumpNetwork` 要**手改客户端目录的 `Config.ini`**，
改仓库里的那份不会自动生效。启动器目录里也放一份新 dll 以免下次启动时被回退：

```powershell
Copy-Item .\artifacts\native\ijl15\Release\ijl15.dll .\DF\town-test\ -Force   # 冒烟/验收目录
Copy-Item .\artifacts\native\ijl15\Release\ijl15.dll .\DF\ -Force            # 客户端目录
```

**排查**：日志文件建出来了但一行都没有 ⇒ 钩子没挂上（`Config.ini` 的 `DumpNetwork` 没读到，
或 dll 是旧的）；`OutputDebugString` 里会打印 `net dump installed (send=… sendto=… recv=… recvfrom=… connect=… wsarecvfrom=…)`，
用 DebugView 之类看。**装了 `DumpNetwork=1` 记得查完关掉**：一个会话会打出很多行。

### 3.5 频道元数据把绑定地址当成拨号地址（2026-10-04 已修，附完整取证链）

**症状**：客户端能登录、能进频道选择，**点「开始」弹「连接失败」然后闪退**。本机与局域网都一样。

**根因**：`EntranceMetadata.BuildSingleLocalChannel` 写进元数据的 `Host` 字段是**客户端要去拨的地址**，
而 `EntranceService` 喂给它的是 `options.Channel.Host` —— **一个绑定地址**。shipped 配置里 `Channel.Host="0.0.0.0"`，
于是客户端被明确告知去连 `0.0.0.0:7001`：`0.0.0.0` 是"未指定地址"，Windows 上必失败
（`connect` 返回 `WSAEWOULDBLOCK`，随后超时/失败）。

**证据（netdump 的 `connect` 行，一次定位）**：
```
19:37:45 TX TCP-CONNECT - -> 127.0.0.1:2311  len=10 hex=636F6E6E6563743D6F6B      connect=ok
19:37:56 TX TCP-CONNECT - -> 0.0.0.0:7001    len=22 hex=... connect=fail wsa=10035
```
`Channel.Host` + `Channel.Port` = `0.0.0.0` + `7001` —— 与客户端实际拨的地址**逐字节吻合**，
且 `protocol=10` 那 14180 字节载荷正是 `BuildSingleLocalChannel` 输出经 zlib+`GamePayloadCipher` 的结果。

**为什么长期没被发现（两个盲区）**：
1. **测试只测函数、不测接线**：冒烟测试直接调用 `BuildSingleLocalChannel(..., "127.0.0.1", 7001)`
   （`Program.cs:598`），验证了"喂对值能出对结果"，**但从没验证服务端实际喂的是什么**。
2. **有告警，但只覆盖了一半**：`Program.cs` 早就有 `GameplayDatagram.Host` 的"绑通配地址却告诉客户端拨它"告警，
   **`Channel.Host` 没有同样的检查**。而 `RelayOptions.cs:44` 的注释其实早就写明
   "服务端需要把 `Channel.Host` 与 `GameplayDatagram.AdvertisedHost` 设为 `127.0.0.1`" —— 说明设计上知道要设，
   但没人**强制**它，也没人**告警**它。

**修法**（`EntranceService.ResolveChannelDialHost`）：拨号地址按优先级解析——
`GameplayDatagram.ResolveAdvertisedHost()` → `Entrance.Host` → `Channel.Host`，
并跳过 `0.0.0.0`/`::`/空值；`Channel.Host` 保持它文档里的语义（绑定地址）。
同时新增 `WarnAboutChannelDialHost` 启动检查，把"客户端将被要求拨的地址"显式打出来：
```
Channel.Host=0.0.0.0 is a bind address, so the channel metadata tells clients to dial '192.168.3.177' instead.
```
若解析结果只在本机可达（loopback）还会额外告警。

**验证方法（可复现）**：让客户端走一次到「点开始」，然后看 netdump：
```powershell
Get-ChildItem <DNF.exe目录>\netdump-*.log | Sort-Object LastWriteTime -Descending |
  Select-Object -First 1 | ForEach-Object { Select-String -Path $_.FullName -Pattern 'TCP-CONNECT' }
```
修复后第二条应为 `-> 192.168.3.177:7001  connect=ok`（而不是 `0.0.0.0:7001 fail`）。

**方法论教训**：这一条是靠**给 netdump 补上 `connect` 钩子**才定位的。
`send`/`recv` 只能看见**已建立**的连接，而"拨错地址"**一个字节都不会发**——
在此之前所有基于 `send/recv` 的推断（包括"客户端从没连过 7001"）都是**不可判定**的，不是"没连"，是"看不见"。

### 3.6 组队卡「连接中」＝客户端↔客户端通道没通，用 `DFLEGACY_UDP_RELAY=1` 补上（2026-10-04）

**症状**：两台客户端都能登录、能进游戏、**组队请求与接受都成功**（服务端日志 `Party request …` /
`accepted … party 1 now has 2 member(s) (created)`），但客户端界面一直显示**「连接中」**。

**根因**：组队后的成员互动走的是**客户端↔客户端的 P2P/中继通道**（官服架构里的 relay/P2P），
不经过我们的服务端。我们这套原本没实现它 ⇒ 客户端拿不到可用的对端端点，退化成占位地址反复重试。
这就是 §2.7 第 9 条那个未结案项（`:737` 列为"待用户拍板"的两条路）。

**证据（netdump 的 `connect` 行，同一个探针第二次立功）**：
```
19:53:45 TX TCP-CONNECT - -> 192.168.3.177:7001      connect=ok          ← 入口/频道正常
19:54:15 TX TCP-CONNECT - -> 255.255.255.255:12336   connect=fail wsa=10035   ← 之后每 5 秒重试
19:54:20/25/30/35/40 … 同上
```
`255.255.255.255` **全仓 grep 无命中**（我们代码里不存在这个字面量），
`12336 = 0x3030` 就是 ASCII 的 `"00"` —— 客户端拿不到有效对端时自己构造的占位地址。
所以这是"**没拿到端点**"，不是"端点写错了"。

**处置**：开启已有的诊断开关重启服务端（**环境变量**）：
```powershell
$env:DFLEGACY_UDP_RELAY = '1'   # 启动 DFLegacy.Server.exe 的进程必须带上
```
生效时启动日志会打印：
```
Gameplay UDP relay enabled (DFLEGACY_UDP_RELAY=1): advertising 192.168.3.177:7002
  as every peer endpoint and forwarding gameplay datagrams between sessions.
```
作用（`GameplayDatagramService.cs:25-31`）：把 `NOTI 11` 里"对端的端点"**改写成服务端自己的端点**
（`AdvertisedHost:PrimaryPort`），并在会话之间转发玩法数据报 —— 由服务端扮演官服的 relay 角色。
配套的 `DFLEGACY_UDP_PEER_SINK` 是"黑洞"对照模式（把对端端点改写成不可达地址，用来确认某段流量确实走 P2P）。

⚠️ **它是环境变量，不是 `server.json` 的字段**，默认关闭。所以"配置看着没问题却卡连接中"是预期现象——
别去 `server.json` 里找。要长期生效必须写进启动脚本/服务定义。提交 `bb3016f` 把它定性为**诊断开关**，
验收时要按"诊断"对待，不能当成已完成的正式功能。

**验证**：开中继后重新组队，看服务端日志有没有转发记录、客户端是否还卡「连接中」；
配合 netdump 确认玩法 UDP 只发往服务端端点，且**不再出现 `255.255.255.255:12336`**。

### 3.7 对端端点表的端口/MTU 写成了小端（2026-10-04 已修，客户端实拨地址为证）

**症状**：开了 `DFLEGACY_UDP_RELAY=1` 之后组队**仍然卡「连接中」**，服务端中继**一条都没转发**
（`RX-UDP` 命中 0）——即客户端**从未把玩法数据发给服务端**。

**根因**：`NOTI 11`（对端表）里端口与 MTU 用 `BinaryWriter.Write(ushort/uint)` 写 = **小端**，
而这一族字段是**网络序**。客户端按大端读，于是把 7002 读成 23067 去拨。

**证据（两条互相独立、指向同一结论）**：
1. **客户端自己的 `CMD 2` NAT 报表**（明文，服务端未解密所以能直接看字节）：
   `04 00 02 | A9FE2BED | C0A803B1 | 13C7 | C0050000` —— IP 是**大端**（`A9FE2BED`=169.254.43.237），
   端口 `13C7` 也是**大端**（=5063，与 netdump 里客户端 UDP 源端口 5063 一致）。
   服务端读它用的正是 `BinaryPrimitives.ReadUInt16BigEndian`（`GameDatagramProtocol.cs:63`），两处一致 ⇒ **客户端这一族全是大端**。
2. **客户端实际去拨的地址**：`TCP-CONNECT -> 192.168.3.177:23067`。
   而我们写进对端表的字节是 `5A 1B`；`23067 = 0x5A1B` **正是它的大端读法**（7002 应为 `1B 5A`）。

**修法**（`GameProtocolEngine.CreateUdpPeerInfo`）：`PublicPort` 用 `WriteUInt16BigEndian`、
`Mtu` 用 `WriteUInt32BigEndian`；IPv4 地址本来就是逐字节写的，不受影响。

**测试为什么没拦住（关键教训）**：三处断言用 `ReadUInt16/32LittleEndian` 去**反解**对端表，
它们钉的是"**实现当时的字节序**"，而不是客户端的要求 —— 于是一个错误的字节序被测试**固化成"正确"**。
更早还有一处（`Program.cs:5870`）期望值写的是 `0xC7, 0x13`（5063 的**大端**），
说明**当初是对的**，是后来实现与断言一起滑向小端而没人发现。
**修法**：`TownSyncSmokeTests.ReadPeerTable` 与那条断言一并改为大端，并反向验证：
改回小端 ⇒ **4 条断言同时变红**；改回大端 ⇒ 全绿。

**注意**：`ReadUInt16LittleEndian` 在这份文件里对**别的**字段是对的（`UserId`、`AccountId`），
不要无差别替换 —— 只有"端口/MTU"这两处是网络序。

### 3.8 组队后一个客户端卡死：NAT 探针判定不看长度 ⇒ MTU 保活被回包 ⇒ 无限回环（2026-10-05 已修）

**症状**：两个客户端组队后，**一个卡死（`Responding=False`）、另一个一直「连接中」**。

**根因**：`GameDatagramProtocol.TryCreateNatProbeResponse` 只判 `request.Length == 0 || request[0] is not (1 or 5)` ——
**只看首字节、不看长度**。而客户端的 **MTU 保活包**是 9 字节、**首字节也是 `01`**：
`01 | sequence(4) | party | flag | applicationLength=0`。于是服务端把它当 NAT 探针**逐条回 7 字节**，
客户端收到后又发一条 → **无限回环**，把客户端 CPU 打满 ⇒ 卡死。

**证据（netdump 长度直方图，2 分钟窗口）**：
```
TX 9 字节  1,315,632 条      RX 7 字节  1,315,576 条
日志 268,435,593 字节 = 256 MB（正好是 DumpNetworkMaxMegabytes 上限）
内容恒定：TX hex=010000000000000100   RX hex=02B103A8C0C713
进程状态：Responding = False（无响应）
```
服务端**自己也几乎不写日志**（总共 589 行）——所以这不是"服务端刷屏"，而是**客户端被回包驱动成紧凑循环**。

**修法**：加 `NatProbeLength = 1`，判定改为 `request.Length != NatProbeLength || request[0] is not (1 or 5)`。
真正的探针就是 1 字节（原测试写的就是 `new byte[] { 1 }`），原先的 `Length == 0` 等于**放行一切以 0x01/0x05 开头的包**。

**测试盲区**：原测试只覆盖了 1 字节探针，没有任何"更长但同首字节"的用例，
所以这个"过于宽松的判定"一直没被拦住。已补 3 条回归（9 字节保活 / 10 字节保活 / 空包都**不得**被回包），
并反向验证：改回 `Length == 0` ⇒ **2 条断言变红**；还原 ⇒ 全绿。

⚠️ **配套事实**：8 字节/9 字节保活属于 **MTU 帧**（`MtUdpHeaderLength = 9`），
和 NAT 探针是**两种东西**，别再按首字节混判。

### 3.9 中继已打通，但玩法数据 CRC/密码校验失败（2026-10-05 未解，待查）

同一次真机的**好消息**：中继通道**真的通了** —— 客户端开始把玩法数据发给服务端，服务端也转给了对端：
```
Gameplay UDP relay: forwarded 19 byte(s) from 192.168.3.14:5063 (session 87fb5a46-…) to 1/1 peer endpoint(s).
```
这解决了 §3.6 里"`mt=0`、客户端从不把玩法数据发给服务端"那条长期堵点（前提是 §3.7 的字节序修复 + `DFLEGACY_UDP_RELAY=1`）。

**但每个包都校验失败**：
```
warn: Gameplay UDP application from 192.168.3.14:5063 failed CRC or cipher validation.
```
即服务端**能转发、但解不开**客户端的玩法应用层载荷。下一步要判的是：
这份 19 字节的 `mt` 应用层用的是哪一套 CRC/密码（我们按 `ApplicationCipherKey = 0x002F1214` 解的那套对不上），
以及**对端客户端收到转发包后是否接受**（若两端都解不开，说明是同一处口径问题）。

### 3.10 `recon_probe` 对带必填输入的工具会失败（2026-10-04）

`.recon/recon.config.yml` 的 `smoke-tests` 声明了必填输入 `csproj`。`recon_probe smoke-tests` 会报
`UNDECLARED_PLACEHOLDER: {csproj}`（它按"无参数"渲染），但 `recon_tool smoke-tests` 显式传
`{"csproj": "tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj"}` 是**正常**的（实测 exit 0）。
结论：**带必填输入的工具用 `recon_tool` 调用，别用 `recon_probe` 判断它坏没坏。**

## 4. 待办 / 已知事项

- 用真实客户端做一次完整试玩（这是目前唯一缺失的端到端环节）。**交易已做过双客户端完整试玩**（见 §2.5），城镇同屏/好友的遗留项仍在 §2.3/§2.4 各自的"待验证"里。
- **交易的遗留复验项（不阻塞）**：V8–V12（取消退回、掉线退回、进副本/换区取消、一人只能一笔、交易中丢弃/出售/分解/整理被拒）未逐条真机确认；其中取消与掉线-结算窗口有自动化哨兵，冻结路径由自动化 + 结算复核双保险。若真机发现"同区重载也会取消交易"，说明 `SetUserAreaCommand` 需要加新旧区域差异判断。
- **交易将来可做**（本轮明确不做，设计稿 §2.7）：仓库/时装/宠物栏放入、交付时按同类物品叠堆、等级金币上限（`NOTI 51`/`CMD 26` 0x46/0x47）、`CMD 26` state 4 的语义。
- **本地部署目录（`DF/dist`）仍是双栈修复之前的构建**：当时服务端正在运行、文件被占用，未替换。该修复对 `127.0.0.1` 部署是 no-op，等停服后替换即可。
- 容器编排默认 host 网络；文件末尾的"桥接 + 端口映射"写法未在真实 IPv6 桥接环境验证过。
- 客户端目录的 `ijl15.dll` 已被启动器替换为 DFLegacy 补丁（原版备份为 `ijl15.original.dll`）。
- 若追求"零额外进程"而把中继进一步做进 ijl15：已评估过，**不建议**——ijl15 目前没有任何线程、入口在 `DllMain` 的加载器锁内，且崩溃会带崩游戏进程。

## 5. 环境与构建注意事项

- Windows 上必须用 **Visual Studio 自带的 MSBuild** 才能构建整个解决方案（含两个 `.vcxproj`）：

  ```powershell
  MSBuild.exe .\DFLegacy.Emulator.slnx -m -restore -p:Configuration=Release
  ```

  `dotnet build` 处理 `.slnx` 会因 `.vcxproj` 报 `MSB4278`（见 BUILDING §8）。发布启动器用 MSBuild 的 Publish 目标：`-t:Publish -p:PublishDir=<绝对路径>\`，它会一并输出启动器、`DFLegacy.Relay.Core.dll` 与 `ijl15.dll`。
- **Git Bash 下 MSBuild 开关要用 `-` 前缀**（`-m`、`-restore`、`-p:Configuration=Release`）。写 `/m` `/restore` 会被 MSYS 当成路径转换成 `M:/`、`D:/Program Files/Git/restore`，报 `MSB1008: 只能指定一个项目`。
- 本机**没有安装 cmake**，因此 `build-native.ps1` 不可用；原生模块改由 `.vcxproj` 构建，产物落在 `artifacts/native/{ijl15,random}/<Configuration>/`，服务端发布时会自动带上随机桥 DLL。
  （2026-10-04 复核：PATH 与 VS 安装目录下都搜不到 `cmake.exe`，MSBuild 与 x86 工具集则在。
  因此原生自测也补了 `src/DFLegacy.Ijl15/tests/DFLegacy.Ijl15.Tests.vcxproj` 并挂进 `DFLegacy.Emulator.slnx` ——
  原先自测只在 `CMakeLists.txt` 里定义，没有 cmake 就**跑不到**，等于长期没在跑。两份源文件列表要同步改。）
- **构建被"访问被拒绝 / 无法获取锁文件"挡住时先清孤儿进程**（2026-10-01 实际踩过）：
  `dotnet build` 报 `MSB3491: Could not write lines to file "…obj\Release\net10.0\…AssemblyInfoInputs.cache". Access to the path … is denied`，
  或 restore 报 `Unable to obtain lock file access on …\NuGetScratch\lock\…` 时，先怀疑**残留的 MSBuild 工作进程**而不是权限本身：
  一次失败的构建会留下上百个孤儿 `dotnet` 进程，它们持有 `obj` 与 NuGet 锁。**不要直接删 NuGet 锁文件**——先按启动时间清掉孤儿进程即可：
  ```powershell
  Get-Process dotnet | Where-Object { $_.StartTime -gt (Get-Date '2026-10-01 01:00') } | Stop-Process -Force
  ```
  （按时间窗筛，别误杀正在托管的 node/服务进程；`DFLegacy.Server` 是游戏服务端，不是 `dotnet` 进程名，不受影响。）
  另：`src` 及其下 `obj` 目录曾缺"当前用户完全控制"，由 `diagnose-windows-sandbox-acl` 技能的一条命令补齐（会给这些目录加一条当前用户的完全控制权限、不改文件内容），修好后构建即通过。
- 服务端的 `server.json`、启动器的 `launcher.json` 都以 `PreserveNewest` 复制到发布目录：重复发布前先备份，或发布后改回本机值（见 DEPLOYING §10）。

## 6. 2026-10-01 下午：ALLDIE_MONSTER 不杀怪（boss 死后小怪不自动死）

- **现象**（用户 2026-10-01 15:47 真机，`server-party33.log`）：boss 死亡后**房间里的 6 只小怪不自动死**；
  用户还提到 boss 房打碎箱子/门冒出的怪"正常在 boss 死后一出来就自动死"，当时也不死。
- **根因**：客户端**明确要求全灭**——日志里同时有
  `accepted BOSS_DIE_CHECK … native-all-die=True` 与 `Accepted DFLegacy ALLDIE_MONSTER …`；
  而 `EntranceService` 的 `ALLDIE_MONSTER` 分支**只写一行
  `waiting for normal monster-death confirmations` 就 `continue`**，什么都没做。
  实例侧的 `BossCascadeMonsterDeaths` 只是"标记"，它由 `TryTakeMonsterDeath` 在客户端**逐只上报死亡**时才消费——
  可客户端把"全灭"的裁定权交给了服务端，**不会再逐只报**。于是双方互等，小怪一直站着，
  13 秒后玩家手动打完才 `AllMonstersDefeated` 结算。
- **修法**（提交 `eb2d8ef`）：`ALLDIE_MONSTER` 分支改为
  「`SnapshotAliveMonsters()` 取名单 → `DungeonInstance.ForceAllMonstersDead(名单)` 锁内摘除 →
  逐只 `CreateMonsterDie(uniqueId, [], 0)` + `BroadcastToInstanceAsync`」。
  连锁死亡**不发掉落、不发经验**（与既有 `isBossCascadeDeath` 口径一致）。
  同时修掉一条相关告警路径的语义：boss 房里"最后一只 boss 的 MONSTER_DIE 时还有小怪存活"
  不再是"等客户端级联"，而是由上面的全灭路径收口。
- **自动化**：`DungeonPartySmokeTests.CheckForceAllMonstersDead` —— ①带名单（含一个"客户端本地已清"的 id）
  必须把活怪全摘掉、且被摘的怪不能再被当成新鲜击杀；②无名单兜底分支全清。
  **变异验证**：把 `AliveMonsters.Remove` 跳过 → 两条断言立刻变红。
- **待真机复验**：①boss 死后小怪是否立刻全灭；②**打碎箱子冒出的怪**是否也随 boss 死亡自动死
  ——那种怪是**运行中动态生成**的（不在 `currentDungeonRoom.Monsters` 里，服务端不知道它出生），
  若仍有残留，需要另找"boss 已死 ⇒ 新出现的怪立即死亡"的判据。

## 7. 2026-10-01 15:36 的一条客户端 `ACCESS_VIOLATION` 记录（**很可能是"服务端重启时客户端主动退出"的副作用**）

- **捕获**：看门狗窗口（`G:\tmp\party32\`）第一次拿到**真实异常记录**（此前几轮都是 `records seen: 0`）。
  两个客户端进程在相近时刻各留下一条：

  | 进程 | 时间 | 故障 |
  | --- | --- | --- |
  | `stacks_69984.txt` | `15:36:30` | `ACCESS_VIOLATION flags=0 nparams=2 access=0 badaddr=0xCDCDCDD5` |
  | `stacks_18212.txt` | `15:36:34` | 同上，`badaddr=0xCDCDCDD5` |

  两条的 `fault Eip=0x0078C9F6`、`Eax=0xCDCDCDCD`、`Edx=0`（一条为 `0xFFFE6200`）、`Ebx=0`。
- **与已知那条不是一回事**：§4 第 16/17 条追的硬崩是 `Eip=0x009019C1` 读 `0x223B0000`；本条是 `0x78C9F6` 读 `0xCDCDCDD5`。
- **⚠️ 归因修正（用户 2026-10-01 16:0x 真机口径）**：**"服务端重启后客户端会自行关闭"是已知的、正常的行为**
  （用户原话："这不是服务端问题"）。本条记录出现的时间窗（`15:36:30–34`）正好横跨我那次服务端重启，
  且这两个进程此前在服务端**没有任何 `connected from`**（没连上就被重启波及）。
  ⇒ **当前按"客户端在服务端消失/重启时主动退出，退出路径里带出一次访问违例"来解释**，
  **不要**再把它当成独立的"新硬崩"去追。判据：若以后**没有**服务端重启也出现同一签名，才需要重新立案。
- **保留价值**：签名本身（`0x78C9F6` 读 `0xCDCDCD*`）值得留着——`0xCDCDCD*` 是客户端给"未初始化/已标记删除"
  内存填的模式值，将来若在**正常游戏中**看到它，就说明"某对象（对端角色/怪物）没登记就被解引用"，
  与 §2.3"必须先有 `USERINFO` 记录、后有区域条目"是同一家族。

## 8. 2026-10-01 16:29 真机：我上一版的 ALLDIE 修复**参数写错，把队员会话打断了**（已修）

- **现象**（用户截图 + `server-party34.log`）：队员打掉 boss 后客户端弹「**网络连接被中断**」→ 卡死 → 几秒后闪退；
  队长那边房间虽然没怪了，但**无法通关**，只能手动回城。
- **根因**：我在 `ALLDIE_MONSTER` 分支里写的是
  `GameProtocolEngine.CreateMonsterDie(uniqueId, [], 0)`——第三个参数 `championRewardOrdinal`
  **只接受哨兵值 `-1`（`NoChampionRewardOrdinal`）或 `1..11`**，传 `0` 直接抛
  `ArgumentOutOfRangeException('championRewardOrdinal')`；异常顺着会话冒泡 ⇒ **服务端主动断开该会话**。
  ```
  16:29:16 warn: Entrance session 922fe57b… ended with an error.
      System.ArgumentOutOfRangeException: (Parameter 'championRewardOrdinal')
      at GameProtocolEngine.CreateMonsterDie(…) line 3597
      at EntranceService.HandleClientAsync(…) line 13020
  ```
- **客户端侧的连锁**（看门狗 `G:\tmp\party33\stacks_48852.txt`，进程 `16:25:33` 起、`16:29:21` 消失）：
  `16:29:18` 与 `16:29:21` 各一条
  `ACCESS_VIOLATION … badaddr=0xCDCDCDD5, fault Eip=0x0078C9F6, Eax=0xCDCDCDCD` ——
  **与 §7 那条 15:36 的记录逐字段相同**。⇒ **因果闭合：服务端主动断开会话 ⇒ 客户端在掉线处理路径里踩空闪退**
  （§7 那条"重启后客户端自行关闭"其实也走这条路；它不是独立故障，而是**被断开**的结果）。
- **修法**（提交见下）：`ALLDIE_MONSTER` 分支改为 `CreateMonsterDie(uniqueId)`——用默认哨兵值，不写奖励序号段。
  同时补回归护栏：协议引擎对 `ordinal = 0` 必须抛 `ArgumentOutOfRangeException('championRewardOrdinal')`
  （谁再传 0 都会被同一条异常打死，所以护栏测的是"引擎契约"而不是某个调用点）。
- **顺带确认的事实**：那一轮 `ALLDIE` 到达时服务端存活集合**已经是空的**（日志 `killed 0 remaining monster(s)`），
  而 **16:29:21–23 是队长端自己在逐只上报 7 只怪的死亡**（服务端回 `Ignored stale or unknown monster death`）
  ⇒ **"连锁全灭"本来就由客户端自己广播**，我们只需**别插坏手**。所以服务端那条广播是"有则补、无则无"的幂等兜底，
  真正致命的只是那个非法参数。

## 9. 2026-10-01 17:06 真机：`error : 3525:100` 与 `0x78C9F6` 崩溃是**同一个全局对象**（`0xBF0A9C`）出问题

- **现场**（`server-party35.log` + 用户截图）：队员 圣骑士 清完第一间房、**走到门口**时客户端弹 `error : 3525:100`，
  同一秒服务端才收到它的 `CMD 118 TRACE_ERROR`（`code=8, flag=1`）并把它当掉线处理；
  **队长 test 的门上报（`protocol=48`）比它晚**，因此以 `partySize=1` 处理 → 只有 test 过了门。
  ⇒ **触发点在客户端"走进门"这一步，不是我们发了什么坏包**（我们那一秒还没给它发任何东西）。
- **静态关键发现**（`insns.txt`，基址 0x400000）：
  - 弹窗函数 `sub_8BEAD0` 除了打印 `0xA89E4C` 的格式串，还会写 **`[0xBF0A9C] + 0x15E1E9 = 1`**（`0x8BEB1F–0x8BEB24`）；
  - 而 §7 那条崩溃点 **`0x78C9F6` 读的正是同一个全局**：`0x78C9E6 mov eax,[0xBF0A9C]; … 0x78C9F6 mov cl,[eax+8]`，
    崩的时候 `eax = 0xCDCDCDCD`（EAX 里存的是客户端"未初始化/已释放"填充值）⇒ **EAX 指向的对象本身是脏的**。
  - `0xBF0A9C` 在二进制里被读写数百处（`0x40AA71`、`0x40C7D1` 等），是个**全局单例管理器**。
  ⇒ **两条现象是一条链**：客户端在门口判定里对一个脏/未初始化的管理器解引用 → 走错误上报（3525，code=100）
    → 弹窗路径又要用同一个 `0xBF0A9C` → 于是卡死/闪退。**3525 不是"被服务端弄出来的"，而是本地对象状态不对。**
- **仍未解**：`sub_8BECD0` 只被 `sub_8BEE08` **注册**（`008BEE03 push 0x8becd0`，全二进制仅此一处引用），
  调用点走间接调用，`insns.txt` 里搜不到——**下一步应在弹窗出现时抓客户端栈**（看门狗 + 弹窗时立刻取栈），
  从调用链反推是哪段门口逻辑把 `0xBF0A9C` 用成脏对象。
- **✅ 因果链已闭合（本次静态分析的最终结论）**：把三处代码串起来就完整了——
  1. `0x8B24A0` 起的**清理例程**：`if ([ecx+0x15E1E9] != 0) return;  // 已报错就不再销毁`
     否则 `call 0x8CE690`（销毁世界/地图管理器）→ `mov [0xBF0A9C], 0xCDCDCDCD`（**把全局填成毒值**）。
  2. 弹窗例程 `sub_8BEAD0` 在打印 3525 之前会 **`mov byte ptr [eax+0x15E1E9], 1`**（`eax = [0xBF0A9C]`）
     ——就是上面那个"已报错"标志。
  3. 每帧主循环 tick：`0x433B47 call 0x78C9E0` → `0x78C9E6 mov eax,[0xBF0A9C]` → 判空 → `0x78C9F6 mov cl,[eax+8]`。
  ⇒ **标准流程是**：某个错误 → 弹窗先把 `+0x15E1E9` 置 1 → 随后清理例程看到标志、**跳过销毁与毒化**、直接返回
  （这就是"报错后不销毁管理器"的自我保护）。**只有 `[0xBF0A9C]+0x15E1E9` 还没被置 1 时**，清理例程才会真的销毁并毒化；
  之后下一帧的 tick 一读 `[eax+8]` 就 `ACCESS_VIOLATION`（EAX=`0xCDCDCDCD`）。
  ⇒ **结论：3525 弹窗不是崩溃的原因，而是客户端原本的保护机制；崩溃发生在"清理例程抢先毒化了全局、
  而弹窗的保护标志还没来得及置位"这条竞态上**（同一帧里销毁与 tick 撞车）。
  **排查重点因此转为：是"什么包/什么时序"让客户端在过门这一帧进入了这条清理路径**，
  而不是继续追弹窗本身。
- **旁证（仍待判定）**：17:06:20–29 服务端在**每 2 秒重播一轮**同区角色/名册
  （`protocol=23`/`24`/`9`，共 5 轮），看起来是**客户端在重复请求进区**（服务端每个 `protocol=16` 都回一套）；
  这种"反复重建外来角色"与 §2.3 那条"外来角色必须先有 `USERINFO` 记录"是同一块逻辑，
  值得作为"门口对象为何是脏的"的一条线索继续查（也可能是门卡住的**结果**而非原因）。

## 10. 2026-10-01 17:18 真机：ALLDIE **顺序反了** → 小怪被"直接删掉"、不结算、两人卡住（已修）

- **现象**（用户真机）：打完 boss 后"**小怪直接被删除而不是正常死亡**"，**之后没有触发结算**，队长与队友都卡住。
- **日志**（`server-party35.log`）：
  ```
  17:18:39 accepted BOSS_DIE_CHECK from participant 1 for boss 4; native-all-die=True
  17:18:39 Handled ALLDIE_MONSTER for dungeon 1, room (2,1): killed 7 remaining monster(s) server-side
  17:18:44 Ignored stale or unknown monster death 4 in room (2,1)      ← 客户端随后自己上报死亡，被我们判成陈旧
  ```
- **根因**：§6 里我写的 `HandleAllDieMonsterAsync` 顺序是
  **先 `ForceAllMonstersDead`（清服务端存活集合）→ 再广播 `NOTI 38`**。
  于是客户端随后自己上报的那批死亡，在服务端**已经"不认识这只怪"**（`TryTakeMonsterDeath` 返回 false）
  ⇒ 既走不到正常死亡流程（不掉落/不计击杀统计），房间清空判定也不成立 ⇒ **不结算、双方卡死**。
- **修法**（提交 `ec7dbaf`）：顺序反转 —— **快照 → 逐只广播 `NOTI 38` → 最后才 `ForceAllMonstersDead`**。
  客户端若接着自己上报，`TryTakeMonsterDeath` 仍能给出真实击杀（掉落/经验照常走那条成熟路径）。
  死亡包参数继续用哨兵 `NoChampionRewardOrdinal(-1)`（§8 的教训）。
- **测试**：'广播过的怪在客户端上报时仍算有效击杀'（按生产顺序走一遍：快照 → 广播 → 客户端逐只上报 → 最后清）
  + '广播后仍清空房间'。**变异验证**：把顺序改成"先清后报" → 断言立刻变红。
- **同一轮的旁证（未结案那条硬崩的头一次重复现场）**：看门狗 `G:\tmp\party34\stacks_54256.txt` 在
  `17:07:14` 与 `17:07:17` 两次抓到**同一帧循环**里的
  `ACCESS_VIOLATION badaddr=0x253B0000 / Eip=0x009019C1`（`records seen: 2`，同一 tid `55044`、逐字段相同）
  —— 正是 §4 第 16/17 条那条**未解**硬崩（`0x9019C1` 读 `0x253B0000`，函数入口 `0x901900`）。
  ⇒ 那条硬崩**不是偶发**：它会在同一帧反复触发。**下一次进副本时应重点复现并抓这条**（弹窗那条 3525 是另一条路）。

## 11. 2026-10-01 17:32 真机：ALLDIE 清完小怪**从未重新判定清房** → 小怪"瞬间消失"、无法通关（已修）

- **现象**（用户真机）：boss 死亡时小怪**瞬间消失而不是正常死亡**，**无法通关**（与 §10 的症状相似但根因不同）。
- **日志**（`server-party36.log`，跑的是 §10 修复后的 build）：
  ```
  17:32:55 Handled ALLDIE_MONSTER for dungeon 1, room (2,0): killed 5 remaining monster(s) server-side
  17:33:01 Ignored stale or unknown monster death 4 in room (2,0)
  ```
  **关键缺失**：这一局**没有** `boss map room … cleared by …`、也没有 `NOTI 31`、更没有 `settlement sent`。
- **根因**：清房判定那条链（`DungeonRoomClearRules.Evaluate` → `TryEnableClear` → **`NOTI 31` 房间开放** →
  结算）**只写在"普通 `MONSTER_DIE`"路径里**。
  - 普通路径在"最后一只 boss 死、还有小怪存活"时返回 `LastBossDefeated` + `ForcedMonsterIds≠空`
    ⇒ `ShouldEnableClear=false`，**本意是"等客户端把级联死亡确认完再清"**；
  - 而那个级联现在由 `ALLDIE_MONSTER` 收口，**收口之后没有任何代码重新判定** ⇒ 永远等不到清房 ⇒
    小怪被清掉、房间不开、无法通关。
  （§10 修的是"顺序"（先广播后清），§11 修的是"清完之后没人重判"——两处都在这条链上，缺一不可。）
- **修法**（提交 `9abf9b4`）：抽出 `TryEnableRoomClearAsync`（清房判定 + `TryEnableClear` + `NOTI 31` +
  清房任务触发），**两条路径共用**；`ALLDIE` 清完怪后以"最后一只 boss"为触发怪**重判一次**
  （boss 房会以 `LastBossDefeated` / `AllMonstersDefeated` 收口 ⇒ 可清房）。
- **测试**：规则层钉两条分支（最后一只 boss 触发 / 非 boss 触发，存活集合清空后都必须 `ShouldEnableClear`）。
  **⚠️ 已知覆盖缺口**：规则层测试**抓不到"ALLDIE 调用点缺失"**——变异验证（把那次重判调用删掉）仍全绿。
  该项**只能靠真机日志**验证：出现 `boss map room … cleared by …` + 随后 `settlement sent` 才算过。
- **教训（写给下一段）**：这条链上已经连续踩了三次（§6 没杀、§10 顺序反、§11 没重判）。
  **以后凡是在"清房/结算"附近动 ALLDIE，必须同时核对三件事**：
  ① 死亡包**先广播后摘除**；② 清完**重新判定**清房；③ 判定成立要发 **`NOTI 31`** 并触发 `CompleteClearQuestTriggersAsync`。
- **排查入口**：`insns.txt` 里 `0x78C9F6` 所在函数的反汇编（尚未看过），
  以及客户端目录各自的 `LagLog.txt`（两个客户端目录分开后才有独立归因价值）。

## 12. 2026-10-22 真机：客户端合成输入（B′）T2 单键门 + T3 无人工 UI 验收门 **通过**

**结论先行**：`handshake-login-town` 场景在真机上**全程无人工 UI** 跑通两轮
（launcher 自动登录 → 合成输入选频道 → 关公告 → 选角 → 进城），
verdict 两轮一致：`green-with-human`（pass 3 / needs-human 1 / **fail 0** / missing-evidence 0）。
证据类别：`[抓包]`（netdump 握手/进频道/7001 游戏会话/7003 心跳帧）+ `[真机]`
（城镇截图、驱动时序日志、`DF\auto-input-diag.log` 诊断线）+ `[静态]`（服务端 `Entrance listener ready`）。
操作手册与判据：`docs/superpowers/plans/2026-10-22-b3-real-machine-runbook.md`（含 7 条实测坑位）。

**真机阶段发现并修复的三个实现缺口**（T0/T1 门测不出、只有真机能暴露，提交 `c8e015a`）：

1. **指令文件只装一次**：原实现只在 `Install()` 时调一次 `LoadPlanFile()`，而平台在客户端
   起来**之后**才写计划 ⇒ 永远不被拾取。spec §3.2 本就要求"启动指令文件轮询"（计划文本欠规格）。
   修法：`Pump()` 内非 Armed 态按 50ms 节流轮询；装载新计划时删除旧 `.done` 标记
   （`wait_ready`"标记消失 = 已取走"的语义由此成立）。
2. **帧泵挂点在部分界面不执行**：0x83F996 性能调用点在频道选择界面**不执行**（实测诊断线
   8 秒不出现），纹理状态代理又被 `ScaleFilter=native` 整体跳过 ⇒ 泵死。修法：**泵锚迁到
   `HookedGetAsyncKeyState` 本体**——游戏每帧轮询键盘、全界面有效、合成键态对当次查询
   立即可见（注入时序最优）；纹理代理与性能调用点保留为次级锚。多锚每帧重复调 Pump 的幂等性
   由 Advance 的逐步完成标志**结构性保证**（每条边只触发一次；代码审读确认，未设专门 T0 用例）。
3. **launcher 覆盖客户端 dll**：launcher 每次启动从**自己的目录**安装 ijl15 替换模块 ⇒ 只部署
   `DF\ijl15.dll` 会被旧版覆盖（实测三处哈希比对定位）。部署必须同时覆盖 `DF\town-test\ijl15.dll`。

**验证门全绿**：T0 原生自测（零警告零错误 + 全量 exit 0）→ T2 单键门（netdump +2610 字节 +
选角界面跳转截图 + 无崩溃）→ T3 两轮一致 → 回归四件套（pytest 126 / smoke-tests 全过 /
protocol.s1 verify 5/5 / MSBuild 原生）。

**边界**（如实）：[语义]"人工确认同屏"按设计留 needs-human（机器证据 = 进城帧 + 城镇截图 +
HUD/FPS overlay 可见）；`text` 步骤真机未单独验证（登录已被 launcher 免掉，spec §6.5）；
帧泵锚依赖键盘轮询活跃（spec §6.1 同款升级风险）。

## 13. 2026-10-23 真机：S2 Task 11 Tier 2/Tier 3 验收与收尾 **完成**

**结论**：S2 计划 Task 11（Tier 2/Tier 3 验收与收尾）全部完成。
- **Tier 2 PASS**（D5-RAW-COMPARE 收口）：单体 vs 拆分两档同一构建、同一测试客户端，
  服务端解密明文帧序列 **42 帧逐字节一致**（唯一例外 = protocol 1 频道列表的帧内时间戳
  4 字节，裁定允许；结构级协议集 = 161 会话密钥密文 / 131/132 存档态聊天回放；探测会话
  剔除）。客户端原始帧聚合结构等价。
- **Tier 3 PASS（两档）**：双客户端**全程无人工 UI** 进城（B′ 合成输入 + 焦点交接），
  服务端同屏锚两档一致达成：`entered 1/1 (instance 0); instanceMembers=2,
  foreignAppearances=1`（同实例 + 外来外观投递）。verdict 两档 green-with-human、
  fail 0。[语义]"人工确认同屏"按场景设计留 needs-human。
- **本波真机修复（3 处，均有提交）**：
  1. `ServerOptions.PrivateAreas` 默认改空数组（**ConfigurationBinder 对数组是追加非
     替换**——非空默认使配置永远无法清空私密列表，bindtest 复现 `[1:1, 99:99]`）；
     冒烟夹具显式声明 1:1。提交 a183f88（初审提交曾误带他人未提交端序 hunk，已拆解重做）。
  2. 帧泵焦点门：失焦客户端不消费共享计划文件、不发沿（游戏只在焦点下轮询键盘）；
     配套驱动端**真实点击聚焦**（DNF 的 DirectInput 只在收到真实输入事件后才恢复轮询，
     纯 SetForegroundWindow 无效——实测确认）。提交 506749f。
  3. 双客户端共享 exe 目录的 `.done` 改名良性竞态（源被先完成者取走 = 视为干净）。
- **回归**：MSBuild 原生门零警告零错误 / smoke-tests 干净 worktree 复跑全过 / protocol.s1 5/5 /
  pytest 126 passed。`dotnet build` 的 slnx 有 4 个预存环境错误（C++ targets 缺失 ×3 +
  launcher post-build ×1，非本波引入，记录在 runbook §4.2）。
- **证据与手册**：`docs/superpowers/plans/2026-10-19-s2-tier2-tier3-runbook.md`
  （口径/步骤/判据/坑位）；runs 四份：单体/拆分握手对比 2 份 + 单体/拆分双客户端 2 份；
  工具链在 `.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/work/`。
- **遗留（如实）**：拆分档 channel 链路在 run 尾段偶发 down（不影响验收结论，观察项）；
  走路键注入未带动角色（共享出生房夹具绕开，走路正确性留给需要它的场景）；
  双客户端焦点切换依赖驱动端点击聚焦（Windows 焦点限制 + DNF 输入恢复语义的联合约束）。

## 14. 2026-10-23 真机：客户端**键盘控制打通**（B′ 补齐波：移动/热键可用，文本路径待解）

**结论先行**：合成键盘**已能驱动角色移动**（上下左右四向均实测），热键路径（`GetAsyncKeyState`）
也已用"截图键真的产生截图"证明；**文本（`WM_CHAR`）路径未打通**，且**业户手动键盘在本客户端同样不可用**
（原因见下，已向业户回报）。证据类别：`[静态]` + `[真机]`，逐条给产物。

**根因（静态+真机一致，两条独立证据）**
1. `[真机]` 客户端自己的 `CreateDevice(GUID_SysKeyboard)` 返回 **`0x80070057`、`*ppDevice = NULL`**
   （两次独立运行：`DF\auto-input-diag.log` 2026-10-23 13:24:43、13:28:38）。
2. `[静态]` 客户端对设备指针**不做空值检查**：`sub_8E9540` = `mov eax,[ecx+0D14h]; mov edx,[eax]`，
   `sub_8E9730` 同款（`0x8E985F`/`0x8E9865`）；键态缓冲在 **`[输入对象+0x10D]`、256 字节、DIK 索引**
   （`+0x100` 那条旧口径作废）。
3. `[真机]` 于是**任何**键投递都必崩：第一次投 `VK_LEFT` 计划 → `VEH code=0xC0000005 at=0x8E9865
   modrel=0x4E9865`，正是 `sub_8E9730` 里那次空设备解引用。⇒ "真实键盘能走"在当前客户端不成立。

**修法（提交 `179e486`、`15e2f5e`）**
- `CreateDeviceProxy`：键盘 GUID + 原调用失败 + 输出为空 ⇒ 换成本模块静态**合成键盘设备**
  （32 槽 `IDirectInputDevice8` vtable，每槽按真实 arity 的 `__stdcall` 桩），
  `GetDeviceState` 用 `GetAsyncKeyState` **合并物理键 + 计划合成键**（业主自己的键盘照旧可用）。
- `DikFromVk`：显式枚举 E0 扩展键（`MapVirtualKeyA` 在本机拿不到 E0 前缀，实测 `MAPVK_VK_TO_VSC_EX`
  对 `VK_LEFT` 只给 `0x004B`）；旧实现把 LEFT 写到 `keys[0x4B]`，游戏读的是 `DIK_LEFT=0xCB`。
- 发送期重入：`SendMessage` 同步回调会经 `GetAsyncKeyState` 锚重入泵，而 text 的 down/up 计数器
  在投递**之后**才自增 ⇒ 无限递归。修法：两个 text 循环改为先自增再投递 + `Pump` 增 `g_inTimeline` 闸。

**验收（真机，`work/t4_move_drive.py` / `t5_scope_drive.py` / `t7_ui_keys.py`；像素差口径见 runbook）**
| 项 | 观测 | 结果 |
|---|---|---|
| 静止对照（无输入 2s） | 像素差 6.76% / 4.94% | 待机动画噪声基线 |
| LEFT 按住 1.5s | 像素差 **74.07%** / 73.72%（两轮） | 角色由画面右侧走到左侧、呈行走姿态，客户端存活 |
| UP / RIGHT / DOWN | 像素差 97.85% / 80.46% / 99.25% | 四向均带动角色（RIGHT 在 `sub_8E9730` 的 VK 表里缺失，DI 路径不受影响） |
| 热键 `0x2C`（游戏只轮询它） | `ScreenShot\` 文件 **6 → 7** + 聊天框系统行"ScreenShot File … saved." | `GetAsyncKeyState` 钩子对游戏真实生效 |
| UI 动作 `VK_ESCAPE` | 系统菜单（关闭菜单/设置/键盘设置/…/结束游戏）弹出 | 键盘→UI 动作可用 |
| 文本 `WM_CHAR`（Enter→"abc"→Enter） | 聊天输入行**未弹出**、无消息；进程存活 | **未打通**：同一投递机制下 ESC 可用，故非投递失效；疑为新手区/该界面不响应 Enter 聊天 |

**新增工具（`.superpowers/sdd/2026-10-22-client-auto-input/work/`，均未入仓因 `.superpowers/` 被忽略）**
`kbd_input_map.py`（IDAPython 线性扫：8E9540/8E9430 反汇编 + 全 text 段扫 0x10D/0xD14）、
`t4_move_drive.py`（启动→进城→静止对照→按键，含像素差判据）、`t5_scope_drive.py`（热键/文本/四向）、
`t7_ui_keys.py`（ESC/Enter）、`capture_screen.ps1`（**桌面截取**：`PrintWindow(PW_RENDERFULLCONTENT)`
对本 D3D8 窗口会周期性返回全黑图，实测同一哈希重复出现，故弃用）、`focus.ps1`（`AttachThreadInput` 前台化：
裸 `SetForegroundWindow` 在非前台进程里被拒，实测 `match=False`，而泵对失焦客户端直接早退）、
`di_probe.ps1`/`di_probe_bits.ps1`（独立进程 DirectInput 探针）。

**遗留（如实）**
- 文本路径未通；聊天键是否 Enter、是否受区域限制未定，需在非新手区/设置里确认绑定（菜单含"键盘设置"）。
- 合成设备 vtable 的 arity 正确性目前只有"没崩 + 各路径都跑过"这一层证据；`GetDeviceState` 合并逻辑无单元测试
  （`DikFromVk` 已有表驱动特征化测试 16 条，见 `b7b2391`）。
- 泵仍以键盘轮询为锚（失焦客户端不推进），与 §12 的升级风险同源。

**二轮补齐（同日，绑定表/文本/战斗键）**
- `[真机]` **绑定表从客户端自己读出**（系统菜单 → 键盘设置，`work/t9-2-bindings.png`）：
  `移动=↑←↓→`、`普通攻击=X`、`跳跃=C`、`技能=Z`、`技能2=SPACE`、`宠物攻击=V`。
  与静态 `sub_8E9730` 的 VK→动作表（37→0/38→2/40→3/65..90→55..80/32→13）逐项一致；
  菜单行坐标已实测（`ESC`→点 (318,228) 开、点 (348,336)「取消」关）。
- `[真机]` **文本路径仍未通**：`Enter → "abc" → Enter` 在洛兰（新手区）聊天输入行不出现、无消息、进程存活
  （t6 与 t10 两次分步实验）。注意 t6 那次用的是后来被证伪的 `PrintWindow` 截取，**只有 t10 算数**。
  同机制下 `ESC` 能开系统菜单 ⇒ 不是投递失效。两种待判可能：①区域/频道限制；②聊天框是子窗口，
  应发给 `GetFocus()` 的子窗口。
- `[真机]` **战斗键未验证**：`X` 3.04%、`Z` 1.51%/2.72%，而同轮待机对照 **3.15%** ⇒ 同量级，不能判定生效；
  新手区是否允许攻击/技能未定，最终判据需进副本。
- 方法教训：**像素差必须带同轮待机对照**；按键后**先 sleep 再截**（t8 因截太早把"菜单没弹出"误读成"点击无效"）。

**三轮修正（同日，`[真机]` A/B 实证）：根因不是宿主，是我们替换了对象 vptr**
- A/B（同一对象、同一参数、一次运行内）：走我们的克隆 vtable 时 `CreateDevice(kbd)=0x80070057`、
  且"重试"同样失败；**把 vptr 换回原版后同一个调用返回 `0x0` 并给出真设备 `out=0x16B728D4`**
  （`auto-input-diag.log` 2026-10-23 14:01:27）⇒ **宿主能建键盘设备**，是"给 dinput8 的对象换 vptr"
  让它拒绝服务。上一版 STATUS 里"宿主建不出键盘设备"的表述**作废**（合成设备仍保留为兜底，但不再是主路径）。
- 修法：`PatchVtableEntry()` 直接改宿主镜像里**共享 vtable 的槽位**，对象 vptr 保持原样——
  · `IDirectInput8` 槽 3 = `CreateDevice`；· `IDirectInputDevice8` 槽 9 = `GetDeviceState`；
  `Restore()` 把两个槽写回原函数。
- 设备侧同样踩过这个坑：第一版把设备 vptr 换成克隆后，`Acquire`/`Poll` 被拒、代理**一次都没被调用**
  （14:03 那轮 `GetDeviceState calls` 零条）。同法改成原地补丁后立刻恢复。
- **最终形态真机证据（构建 211456 B，PID 612）**：`CreateDevice(kbd) result=0x0` →
  `GetDeviceState` 代理 **~260 次/秒、`res=0x0`** → LEFT 行走 **74.26%** vs 同轮待机 **6.13%** →
  热键 `0x2C` 截图文件 **7→8** → 原生自测 exit 0。
- `[未验证]` **宿主探针仍不可用**：`ProbeHostKeyboard` 用自己的 DirectInput8Create 调用，
  即使移出 DllMain（首帧 Pump）仍返回 `E_NOINTERFACE`，而游戏自己的同一调用（同 IID/版本/hinst）
  成功 —— 两者的差别没查明，故**不得**用该探针推断宿主能力（已把它降级为纯诊断行）。
- 复审（独立复审人 `reviewer-kbd`，报告 `work/../review-kbd-device.md`）：**Critical 1 / Important 4 / Minor 10**，
  已修 C1（`EnumObjects` 少弹 4 字节 → 栈错位）、I1（探针 IID 字节序）、I2（合成路径无计数）、
  I3（`Restore` 清物理键指针）、I4（caps/instance 不按调用者 `dwSize` 零初始化），
  以及 M1/M3/M4/M5/M6/M7/M8；M2/M9/M10 记为文档级（`acquired` 仅记录语义、IAT 钩为进程级、
  4 条测试依赖 US 布局）。

**战斗键：按业主要求只在副本里验（判据已钉，等"双客户端组队进副本"）**
- 业主裁定：战斗部分必须进副本，跟着两客户端组队进副本时一起尝试。
- 可执行判据：取队伍成形 → 队长走到副本入口并点进入（截图确认加载完成）→ 注入
  `vk=0x58`(普通攻击 X)/`vk=0x5A`(技能 Z)/`vk=0x43`(跳跃 C，对照)，绑定取自客户端自带「键盘设置」；
  证据至少一条：① 服务端日志中的伤害/怪物 HP 变化；② `netdump-<pid>.log` 出现攻击/技能上行帧；
  ③ 副本内动作像素差**显著高于同轮待机对照**（t11 口径：3.04% vs 待机 3.15% 不算通过）。"没崩"不算通过。
- **当前阻塞（实测）**：`tools/acceptance/scenarios/` 无 dungeon 场景；`docker ps` 报 Docker Desktop
  未运行 ⇒ 组队副本所需 relay+stun 容器不可用。故本轮只落判据，不谎称已验证。

**战斗键验收已做成一条命令（提交见 `git log`，`tools/acceptance/combat_gate.py`）**
- 判据是纯函数 `Thresholds`/`judge`：服务端伤害/HP 事件 > netdump 新增上行帧类 >
  像素差（必须 ≥ `max(8%, 3×同轮待机, 待机+4%)`）；计划未被取走判 `missing-evidence`（基础设施问题，
  不是产品失败）；对照组（跳跃）**永不**单独构成 pass。
- 一条命令：`cd tools; python -m acceptance.combat_gate --client-dir G:\Game\DNF_ACT1\DF
  --server-log <run>/server/server-A.log --netdump G:\Game\DNF_ACT1\DF\netdump-<pid>.log --out-dir <run>`
  （前置：两客户端已组队进本；relay+stun 容器必须已起）。
- 自检两条：`python -m acceptance.combat_gate --self-check`（10 条向量）+ `pytest acceptance\tests\test_combat_gate.py`
  （14 条单测）。红线用例 `test_t11_reverse_case_must_fail`：动作 3.04% vs 待机 3.15% **必须 fail**。
- 场景 `scenarios/party-dungeon.yaml`：登录/进城沿用已校准 hex pattern，组队/进本/战斗用 `[语义]`，
  头注释写明"战斗判定由 combat_gate 出"，避免"场景红了但其实没有断言"。
- 全量 pytest **140 passed**（原 126 + 新 14）。RUNBOOK 已加"副本内战斗键验收（一条命令）"一节。
⚠️ 本轮为跑测试停掉了我先前起的 `DFLegacy.Server`（占 7001 使 runner 用例报 PortBusy）；
组队进副本那波请用它自己的 orchestrate 起服务端。

**副本入口探路（真机，2026-10-23，`work/scout/`）：走到了门口，但客户端不发进图请求**
- 可重放路线（已实测）：旅馆→赫顿玛尔城区 `LEFT 500ms → DOWN 2200ms`；城区→副本门口巷(2/8)
  `LEFT 2500ms → UP 300ms`；门口巷→斯顿雪域 `DOWN 350ms → LEFT 1500ms`；斯镇沿 y≈827
  三段 LEFT 到 5/3 门口区。
- **卡点**：两类 `[dungeon gate]` 区里只有带橙 X 的 `StormBattleGate` 岩石，**没有门矩形/传送门**；
  站上锚点四向顶、点岩石、`ESC` 菜单、**40 键全扫**，都打不开「选地下图」界面。
- **硬证据**（`grep scout/server.log`）：全会话**无** `protocol=16`(START_MAP)、**无** `protocol=142`、
  无 `LeaveTownArea`/`NOTI 23`；上行只有位置/心跳/聊天 ⇒ **客户端一次都没发出进图请求**，
  即阻塞在客户端侧条件，不是服务端漏答。⇒ 战斗键**仍未验证**（判据与命令已就绪，缺一条能进本的路径）。
- **顺带得到两个可复用仪器/结论**：
  ① 角色地图坐标可读：`creature+0x24C/+0x250`（实测 222 px/s，与按键时长吻合）——导航可量化；
  ② 城镇换区是 `.map [town movable area]` 的**门矩形包含判定**：城区西门矩形 y 上界 329，
     站 y=330 顶多久都不过场，`UP 0.3s` 进矩形立刻过场（上一轮"卡住"的真因）。
- **下一步（已派同一探路员续做）**：按它自己给的线索查 `.map` 里 `type 1`（真进图门）在哪几张图、
  以及从当前城镇集合是否存在 door 链；可达就真机走进 type 1 门，判定用 `START_MAP/LeaveTownArea` 日志行。

**副本内战斗键：**真机 PASS**（2026-10-23，`work/scout/scout-report3.md` + `work/combat-run2/combat-gate.json`）**
- 进本链路：`protocol=16`（START_MAP）日志原文 `15:44:41 RX … type=1 protocol=16 length=16 records=30 crc=ED984599`；
  同秒 `Dungeon 40 … start map 5601, monsters=4`（「山脊」，疲劳 1→2）；内存 `state=3(DUNGEON)`。
  进本门是 `[dungeon gate]` 区的 **`dest=-1` 门**（不是 `type 1`）；站位必须进**门矩形**
  （`D_StormPass2` 为 `x 14..33, y 354..479`），差几像素就不触发 —— 上一轮"卡住"的真因。
- **combat-gate 判定 `result=pass`**：`control_pct=2.39`；**X** `action_pct=6.81` + **1 类**待机窗口没有的上行帧；
  **Z** `action_pct=9.11` + **1 类**新上行帧；**C**（跳跃，对照）`64.44` 像素差但 0 新帧、按设计不参与判定。
- **边界（如实）**：三项 `server_events=0` —— 本服日志没有"怪物扣血/伤害数值"这类事件行，
  所以"伤害真的结算了"仍是 `[未验证]`；已验证的是**副本内按 X/Z 会产生待机窗口没有的上行帧 +
  画面显著变化**，且城镇里做不到（城镇同样按键只有 3.0%/3.1%，与对照 2.5% 同量级）。
- ⚠️ **本波修掉一个真机缺陷（`59fa2e9`）**：`combat_gate._ps()` 原先把命令行拼成
  `powershell -Command <script> -- <args>`，PS 5.1 把 `--` 吃进脚本 ⇒ `dnf_pid()` 恒返 0、
  截图失败，按 RUNBOOK 原样跑**必然假阴性**（"没有正在运行的 DNF 客户端"），而 `--self-check`
  与 14 条单测都发现不了（纯逻辑不碰进程）。修法：有参数走 `-File`（脚本落 temp、带 BOM），
  无参数用 `-Command`，永不拼 `--`；补 3 条回归钉；真机复验 `pid=61556` 非 0、12 张截图、
  判据给出可解释的 fail。全量 pytest **143 passed**。

**双人进本（进行中，2026-10-23 七轮探路）：已到"队伍可创建、队员未加入"**
- **已达成**：两客户端同一频道/城镇/区域/实例（`Town member … entered 5/1 (instance 0); instanceMembers=2`，两人
  在画面里互相可见，名字牌互证的截图 `p5-03/p5-04`）；**队伍可创建**（服务端
  `PartyService: Party roster 1 published: nameSel 6->6, 1 member(s)` ×3，`scout-report8`）。
- **注入能力扩展**：计划新增 `op:"rclick"`（`dcd…`=`ebcd026`，含 `TestRightClickPrimitive`，只发右键边）；
  另交付 `work/os_click.ps1`（OS 级 `SetCursorPos + mouse_event`，左右键/双击）——
  世界内交互必须走它，因为窗口消息坐标对世界无效。
- **两条否定结论（省下后来人整轮时间）**：
  ① **世界内对同屏玩家右键不会弹菜单**：窗口消息 `rclick` 与 OS 级右键各试两次，坐标经打印校验命中角色，
     画面无任何变化（`p6-05`、`p7-04`、`p7-05`）⇒ 这个 2008 客户端没有"世界右键邀请"。
  ② **键盘设置里没有队伍热键**：`ESC→(318,228)` 面板只有 7 条基本操作（移动/普通攻击X/跳跃C/技能Z/技能2 SPACE/宠物攻击V），
     滚动条点 6 次 + PageDown 均未翻页（`p7-06`、`p7-08`）。
- **UI 坐标（量图法得到，可直接复用）**：右上图标圆心 **推荐伙伴 (567,69)、队伍信息 (567,97)、社区 (567,122)**；
  队伍信息窗 **「创建队伍」(325,295)**、创建子窗 **「确认」(325,325)**；建队后队伍窗**只有**
  **「修改队名」(280,230)** 与 **「解散队伍」(363,230)**（放大读图确认，**无邀请按钮**）。
- **当前卡点**：队友加入入口。「系统推荐的好伙伴」列表在 B 侧为空（[推断] 只推荐未组队的人）；
  正在测"A 建队后由 A 打开推荐窗选人"，以及"社区→好友条目右键/双击"。
- 判据（未变）：服务端 `Party roster … 2 member(s)` 才算成队；随后 `protocol=16` 需出现 **2 次**（两人进本）。

**双人进本 第九轮：队伍能建、邀请入口仍未找到（并推翻了仓库旧结论）**
- **旧的"城镇右键邀请组队"结论作废**。真机（`scout-report9`）：右键**自己能出完整玩家菜单**，
  文字为 `退出组队 / 1:1 交易 / 悄悄话 / 发送邮件 / 添加好友 / 建立师徒关系 / 添加黑名单 / 查看信息`
  —— **没有"组队邀请/申请加入"**；右键**对方角色**则始终不出菜单（窗口消息与 OS 级点击各试两次，
  坐标经打印校验命中角色）。⇒ 以真机为准，不要再按旧结论去"右键邀请"。
- **队伍创建已可用**（服务端 `Party roster 1 published: nameSel 6->6, 1 member(s)` ×3）；
  建队后的队伍窗**只有**「修改队名」(280,230)/「解散队伍」(363,230)，**没有邀请按钮**（放大读图确认）。
- **「系统推荐的好伙伴」列表在本服恒为空**（B 侧与 A 侧各测一次）⇒ 不作为组队入口。
- **判据未变**：服务端出现 `Party roster … 2 member(s)` 才算成队；随后 `protocol=16` 需 **2 次**。
- **剩余三条候选（按成本，交棒）**：
  ① **好友路**：`社区 (567,122)` → 好友页签 → 对方条目用 `os_click -Button right`/双击，看有无"组队邀请"；
     若需先加好友，用玩家菜单里的「添加好友」（菜单已确认存在）。
  ② **网格扫点**：在对方角色名牌下方 ±30 px 做 5×5 的 OS 右键网格（每点后截图），确认"对方角色到底能不能被点中"——
     这是把"右键对方无菜单"从"坐标没打中"里区分出来的唯一廉价办法。
  ③ **静态**：IDA 找发出组队邀请包（CMD 10 peerType=0）的函数，反查其调用者 = 真正的 UI 入口
     （仓库官服符号里 `C*Party*::` 一类可直接对上）。

**勘误（业主纠正 2026-10-23）：世界里出交互菜单是**左键**，右键那条路从一开始就设计错了**
- 反证就在本文件上一段里：`t13` 那轮用**左键**点 (447,395) 直接弹出了赛丽亚的 NPC 菜单
  （并因误点「进入商城」导致客户端干净退出）。既然左键能对 NPC 出菜单，玩家角色的交互菜单同样应在左键。
- 因此上一轮"右键邀请组队"的**实验设计**是错的（来源是仓库旧结论），我据此得到的
  "世界右键无菜单"只是**重复了一个不该问的问题**，不能作为"没有邀请入口"的证据。
- **仍未试过的组合**：`os_click.ps1 -Button left` 点**对方角色**（世界内 OS 级左键）。
  这是"左键出菜单"机制下唯一没测的一格，已派探路员单点验证并把菜单项逐条抄回。
- 记在案的方法论教训：**先检查自己已有的成功案例（NPC 菜单是左键出的），再用它反证新假设**，
  不要因为仓库里有一条旧结论就跳过这一步。

**双人进本 收口（第十一轮）：左键点对方脚部三处均无菜单；只余"好友条目"一个未验分支**
- 三态表（A=召唤师 26812 本机，B=mageco 在其画面 ≈(275,300)，相距 145 px）：
  | 点击点 | 点击前→后坐标 | 移动 | 菜单 |
  |---|---|---|---|
  | (230,352) | (619,234)→**(554,234)** | **走 65 px**（=被当作点地面） | 无 |
  | (230,340) | 不动 | 不动 | **无** |
  | (230,365) | 不动 | 不动 | **无** |
  ⇒ **左键点对方角色也不出菜单**（"不动"无法区分"命中角色"与"该点不可达"，本条不单独作证据）。
- **汇总（本客户端至今）**：世界内左键/右键（窗口消息与 OS 级共 5 种组合）点玩家**全部无菜单**；
  队伍窗只有 创建/修改队名/解散；推荐伙伴双方恒空；键盘设置无组队热键；右键玩家菜单里也无邀请项。
  ⇒ **未发现可用的"组队邀请"入口**，`Party roster … 2 member(s)` 从未出现，`protocol=16` 恒 0。
- **唯一未验分支**：`社区 (567,122)` → 好友页签 → 对条目（`test Lv7 神枪手`）**UI 内右键/双击**是否有「邀请组队」。
- **更强的下一步（建议优先于此分支）**：**静态反查**——在 `DNF.exe` 里找发出组队邀请包
  （`CMD 10 peerType=0`）的函数，再反查其调用者即真正的 UI 入口；仓库有 IDA 工具链与官服符号
  （`C*Party*::` 一类可直接对上）。真机穷举已经证明"世界点击"不是入口，继续盲试 UI 分支的性价比低于一次静态定位。
- 业主纠正的一条机制**已确认在这台客户端上只对 NPC 成立**：左键点 NPC（赛丽亚）出菜单（`t13` 误点「进入商城」即此机制），
  但左键点**玩家角色**不出菜单。

**业主要求的复测（进城后左键点玩家）结果：区域假设被推翻，但多了一个关键判别信号**
- 做法完全按业主指示：`move_window.ps1` 把 B 窗口挪到 (700,300)（两窗错开、截图 origin 分别 960,480 / 700,300，未串窗），
  两人各按一次 `DOWN 800ms` 从 `5/1` 走进 **`5/0` 城镇本体**（服务端 `entered 5/0 (instance 0)` ×2，`instanceMembers=2`）。
- **5/0 里三次 OS 左键点对方角色**（(333,282)/(333,267)/(333,252)）：**三次都无菜单**（`p12-08-left-282/267/252`）。
- **但判别信号变了**：在 `5/1` 点同一批点位的下方时，角色会**朝该点走动**（=被当成点地面）；
  在 `5/0` 三次**角色都不动** ⇒ 点击**落在了玩家身上并被吞掉**，而不是"点空地"。
  也就是说：客户端**认出了这个目标**，但**拒绝/没有**弹出交互菜单。
- **新的首要怀疑（下一轮从这里查）**：我们服务端下发的 `USERINFO`（外来玩家记录）里可能缺"可交互"字段
  （如 `userType`/状态位/队伍与频道标记），客户端于是把对方渲染成"只显示名牌、不可交互"的对象。
  下一步：把我们对外来玩家发的 `USERINFO` 字段与官服勘察目录（`.superpowers/.../official/STATIC-FINDINGS.md`
  的 `USERINFO` 条目）逐字段对比，找那个被写成 0/缺失的字段；同时可用参考实现抓包对照同一场景。
- 备选（成本更低但未验）：**好友条目** UI 内右键/双击（`社区 (567,122)` → 好友页签）。

**🎯 命中（2026-10-23，业主全程旁观那次）：左键点玩家出菜单，菜单里有「邀请组队」**
- 现场：两窗拉开 **A(test)=60796 @ (0,0)**、**B(shizuku)=65496 @ (700,0)**（客户区 640×480，互不重叠）；
  两人各按一次 `DOWN 800ms` 从 5/1 进 **`5/0 (instance 0)`、`instanceMembers=2`**；A 再 `RIGHT 800ms` 分开站位。
- **A 上用 `os_click.ps1 -Button left` 点对方角色 (330,282)** → 弹出玩家菜单（截图
  `work/demo-A-after-click-282.png`），菜单项自上而下：
  **`邀请组队`**、`交易`、`悄悄话`、`发送邮件`、`1:1 窗口聊天`、`添加好友`、`建立师徒关系`、`添加黑名单`、`查看信息`。
- **两处旧结论作废**：
  ① "世界里右键邀请组队" → 实际是**左键**（业主早已指出；我按仓库旧结论先试了右键，属设计错误）；
  ② "玩家菜单里没有邀请项"（`p9-05` 那次捕获）→ 那次测的是**自己/别的上下文**的菜单，不是点对方角色得到的菜单。
- **为什么之前十轮都没成**：一直是**坐标问题**，不是客户端不支持。判别法（本轮验证有效）：
  **点下去角色是否走动** —— 走动=被当成点地面（坐标没打中人）；不走动且出菜单=命中角色。
  5/1 里那些点会走动；5/0 里打到角色身上就不会动。
- **下一步**：点「邀请组队」→ B 侧接受应答窗 → 服务端应出现 `Party roster … 2 member(s)`；
  随后队长进本，`protocol=16` 应出现 **2 次**；两人都在本里即跑 `combat_gate`。

**组队邀请「超时后再邀直接被拒」定论 + 修复（2026-10-23，业主报告）**

业主原话：「客户端收到组队邀请，超时会自动拒绝，但是服务端好像没有处理，后续再尝试要求就直接返回拒绝了」。

- **现场（既有真机日志 `work/server-demo.log`，业主 20:01 那轮，本轮未重跑）**：
  `522` 20:01:15 邀请（`protocol=10`）→ `523` NOTI 7 送达 → `529` **20:01:30（+15.0 s）重邀** →
  `530` **立刻** `TX protocol=11 payload=006400`，且**没有**新的 `Party request from` 行 →
  `536` 20:01:45（+30.0 s）才补一条同样的 006400，**日志里没有任何业务行**。
  全会话 `RX … protocol=11` **零条** ⇒ **客户端应答窗超时不发任何包**，服务端只能靠自己的计时器收尾。
- **根因两条**：① `if (!_pending.TryAdd(...)) → 立刻回 006400`——**连已过期的待决项也照拒绝**；
  ② `RequestWindow = 30 s` **远长于客户端应答窗**，多出来的时间全是"替一扇已经不存在的窗占位"。
- **实测客户端应答窗 ≈ 10 s（两法互证，`work/measure-invite-window/report.md`）**：
  邀请后 **+8 s** 点【是】→ 服务端立刻 `Party roster … 2 member(s)`（窗口还活着）；
  **+12 s** 点【是】→ 零名册、零 `protocol=11`（窗口已消失）；
  1 Hz 桌面序列（`series/f000..f019`）按 **t0=20:47:01**（`server-demo.log:653`）算，窗口在
  **(10.2 s, 11.2 s]** 之间消失（`f008` 有窗、亮度 47.4；`f009` 无窗、38.33）。
  ⚠️ **错误口径**：从序列帧 0（20:47:03.2）算会得 9.04 s（差 2.2 s）——由 `prober-invite` 交叉核对抓出。
- **修法（`src/DFLegacy.Server/PartyService.cs`）**：
  ① `RequestWindow` **30 s → 10 s**（取 10 不取更小：窗口还开着时服务端不能先忘，否则目标点【是】被静默丢掉）；
  ② `TryReservePending`（比较交换）——**只有还活着的待决项有权拒绝**，过期项被新请求顶掉；
  ③ 三条路径各补一行 info 业务日志：`timed out with no answer` / `Took over the invite slot` /
  `Refused a party request … already has a live request open`（业主"感觉服务端没处理"直接来自超时路径零日志）。
- **门（全绿）**：新增 5 条冒烟契约（RED 实跑 4 红 → 绿）、`ijl15_tests.exe` 绿、
  `combat_gate --self-check` + `party_dungeon --self-check` 绿、**pytest 149 passed**。
- **真机复验（新构建，`work/measure-invite-window/server-fixverify.log`）**：
  邀请 20:56:31 → **20:56:42 `timed out with no answer after 10 s`**（+10 s 收尾且**有日志**）→
  **+20 s 重邀 20:56:51 产生新的 `Party request from`**，目标客户端弹出**新窗**
  （`vf-A-at-14s-new-window.png`；同轮 `vf-A-at-11s.png` 证明旧窗在 +11 s 已消失）。
  旧构建同场景（业主那轮 +15 s）是立刻 006400、目标零包；本轮 `Refused a party request` **零条**。
- **部署**：`DF\town-test\DFLegacy.Server.dll` 已换新构建（旧版备份 `DFLegacy.Server.dll.pre-invitefix-20261023`）。
- **边界（如实）**：`[未验证-真机]`「过期项被顶掉」只在单测覆盖——它只在「过期时刻 → 下一次 1 Hz 扫描」
  ≤1 s 的缝里生效，两次卡点尝试都因点击时序抖动没撞上（`verify-takeover.json`）；这段由冒烟用例
  ②（同一个人重邀）与④（别人留下的过期项）覆盖。
- **设计稿勘误**：`docs/superpowers/specs/2026-09-28-party-system-design.md` §5.1/§5.2 的 **30 秒**已标作废
  （以实现为准），并写明"先到者优先只对活着的待决项成立"。
- **产物位置**（`.superpowers/` 与 `docs/` 都不入仓）：
  `work/measure-invite-window/report.md`（量测报告，T_client 与全部行号）、`report.md` 同目录 `lead/`
  下有本轮全部脚本与证据（`probe_accept.py` / `verify_fix.py` / `verify-fix.json` /
  `vf-A-at-11s.png` / `vf-A-at-14s-new-window.png` / `frames.csv` / `series/` / `server-fixverify.log`）；
  现场契约清单 `work/party-invite-timeout/inventory.md`。
- ⚠️ **日志编码**：`DFLegacy.Server` 的 stdout 日志是 **GBK/cp936**。用 `-Encoding utf8` 或 UTF-8 grep
  会把「召唤师」等中文读成乱码、`Select-String '召唤师'` 会**假装日志里没有**（ASCII 部分如
  `protocol=11 … payload=006400` 不受影响）。要搜中文先 `[System.Text.Encoding]::GetEncoding(936)`。
- **沙箱/工具坑**（本轮踩到，别重犯）：本会话的 shell 曾跑在低完整性级别——`Get-Process`/`tasklist`
  对其它进程 `Access denied` 或静默返回空，我据此误判"机器上没有 DNF 在跑"（实际业主那组一直活着，
  靠 `netstat -ano` + 日志还在长才纠正）⇒ **判断"有没有在跑"必须用 netstat/日志**；`dotnet` 的
  MSBuild 节点复用会静默失败（`生成失败 / 0 个错误`）⇒ `-m:1 -nodeReuse:false` +
  `$env:MSBUILDDISABLENODEREUSE='1'`；受限时构建用 `--artifacts-path .tmp\artifacts`，
  冒烟测试的 `$env:TEMP` 要指到工作区内（否则 `AdminAuthSmokeTests` 在 `CreateDirectory` 上红）。

**双人进本 2026-10-23 夜：`Party roster … 2 member(s)` 首次达成；② 卡在 relay（新缺陷）**

**已达成（两轮，均可指认日志）**：`Party roster … 2 member(s)` **第一次真的出现了**（此前 STATUS §14 收口里写的
"从未出现"作废）。
- 第 1 次：`21:39:32 Party roster 2 published … 2 member(s)` + `召唤师 accepted … party 2 now has 2 member(s) (created)`
  （邀请 21:39:31，A 点【是】+2.3 s；画面侧 A/B 双方左上出现「组队中」与对方血条）。
- 第 2 次：`21:48:56 Party roster 1 published … 2 member(s)`（这次是 **A 邀请 B**，队长 = A）——
  为绕开下面的 relay 缺陷，改成**两人先各自走到斯顿雪域副本门口区 5/2、再在门口区成队**。
- 可复放路线（每步用 `creature+0x24C/+0x250` 读坐标闭环，`work/party-dungeon-run/check_progress.py` 判日志）：
  5/1 → `DOWN 800` → 5/0 → `LEFT 6000` → `UP 500`（进 y 354..444 走廊）→ `LEFT 1400` → **5/2**(落点 ~(789,417))
  → `LEFT` 若干到 x≈200。
- 门坐标（`.map` 权威值，`work/scout/stormpass.map.txt` 与 `type1-doors.txt`）：
  5/0 的门矩形 `31 353 19 92 → 5/2`、`31 801 20 112 → 5/3`、`1224 472 95 19 → 5/1`；5/2/5/3 的进图门 `14 354 19 125 dest=-1`。

**② 双人进本未达成，拦路石是 gameplay relay（新发现的真缺陷，不是操作问题）**
- `GameplayDatagramService.ObserveEndpoint`（`:509-530`）把 UDP 端点记在 **IP 级**
  （`_observedByAddress[AddressKey(remote.Address)]`），并把**同一 IP 的所有会话**都设成同一个端点；
  而 NAT 探测/keepalive 包只有 **1 字节**（`GameDatagramProtocol.cs:40` `NatProbeLength=1`，值 0/1/5），
  **不含任何会话身份** ⇒ 两个客户端**同机不同端口**时端点反复互相覆盖。
- 症状（两轮都在；计数口径＝`failed CRC` ∨ `session unknown` ∨ `to 0/0 peer` 三类行合计）：
  业主 20:01 那轮 **78 行**（该日志共 790 行）、本轮 **202 行**（日志 991 行）；若只数 `failed CRC`
  则分别是 39 / 101 行。三种行交替出现，例如 `(session unknown)` + `to 0/0 peer endpoint(s)` ⇒
  客户端玩法通道看门狗约 **33 s** 后弹「与服务器的连接失败，请解除防火墙或改善您的网络环境」，
  并**自己发 `CMD 13` 退出队伍**
  （`21:40:05 RX 86173fc6 … protocol=13 body=3A00` → `Character 9bbab2be… left party 2; 1 member(s) remain.`）。
- 后果：队伍活不过 ~33 s；队长走到门口区要 >60 s ⇒ 组队进本的编排（`DungeonPartySelectionPush` 见
  `the gathering party has no other member (slots=1)`）永远看到 1 人队；本轮真 START_MAP 只有 1 次且是
  **单人本**（`Dungeon 40 … partyMode=0, start map 5601`）。
- **可能的修法**（下一波，未实现）：官方架构里 relay 是 8101-8300 一段端口 —— 即"**每会话一个中继端口**"，
  按**目的端口**归属会话即可精确映射；或利用已有按会话的解密/校验（`failed CRC or cipher validation` 说明
  服务端有每会话密钥）按"能解出谁的包"来归属端点。二者都不需要客户端配合。

**判据修正（工具假通过，已修 + 加红线自检）**：`tools/acceptance/party_dungeon.py` 的
`start_map_count` 原来用 `protocol=16\b`，会把 ①`protocol=161`（心跳，同轮 80+ 条）与
②服务端成对发出的 `TX protocol=16 length=8 payload=0000`（**点「交易」/取消选图**的回执）都数成 START_MAP
——本轮实测：两次误点「交易」后判据给出 `two_player_dungeon=True` 的**假通过**。现在只认
`RX … protocol=16 length=16`（正则 `\bRX\b…protocol=16(?!\d)…length=16`），自检从 4 向量 + 2 parser
扩到 **6 向量 + 4 parser**（新增 TX 回执、心跳两条红线）。

**其它现场记录（都标了未验证）**
- 玩家菜单第一项的位置**必须按当帧截图量**：同一晚 5/0 里是 `(330,302)` 区域、5/2 里是 `(272,302)`，
  而 `(258,331)` 会点到第二项「交易」（本轮踩了两次，`Trade … requested` 两行）。
- `state=2(MAP-SELECT)` 的触发：B 在 x=32 时翻到 2，而 A 走进同一矩形到 (22,372) 后 `state` 一直是 1
  —— 差异**未查明**（`[未验证]`；可能是玩家菜单/交易窗残留挡住了门触发）。
- 本轮所有客户端/服务端都是 Lead 起停：服务端 pid 10680（relay 开，`measure-invite-window\server-fixed-live.log`），
  客户端 40512(test/召唤师)、55448(shizuku/mageco) 在 5/2。旧 dll 备份仍在
  `DF\town-test\DFLegacy.Server.dll.pre-invitefix-20261023`。

**2026-10-24：组队连接与死亡回城修复复核**

- 当前工作树的组队 UDP 修正使用完整 `IP:port` 绑定会话，名册/外观介绍先于 peer 表，
  并修正 peer MTU 字节序；历史双客户端真机已有超过 76 秒保队、同实例全员加载的证据。
  详细原始证据见 `.superpowers/sdd/2026-10-22-client-auto-input/work/relay-session-20261023/report.md`。
- 本轮另修一处确认包丢失：MT UDP 的 16-bit selective ACK 也是以 `0` 开头的 10 字节包，
  旧 `IsNatKeepAlive` 会直接吞掉。现在按捕获的 `00 02 <IPv4 LE> <port LE> 00 00`
  格式识别，并只在副 NAT 反射端口消费；主玩法端口即使收到格式重叠的 ACK 也照常中继。
  真实 loopback UDP 回归覆盖两个监听端口及格式重叠，修复前收包超时，修复后通过。
- 组队死亡由实例共享状态决定：还有活人时死者留本，最后一人死亡后开 10 秒期限，
  到期全队回城；复活会取消期限并使已排队的旧回城请求失效。回城/复活互斥，空闲会话
  按 250ms tick 取信，避免没有 TCP 上行时迟迟不回城。本轮加固会话兜底：组队实例
  即使登记成员死亡失败，也不会退回个人 10 秒计时。
- 本轮最终验证：Release 构建 0 警告/错误，全量 C# 冒烟测试通过
  （`.tmp/party-20261024-final-build.log`、`.tmp/party-20261024-final-smoke.log`）；
  在 `tools` 目录运行 `python -m pytest acceptance/tests/test_party_dungeon.py -q`，29 passed。
  已将这次全量测试使用的 Server/Protocol DLL 与 PDB 更新到 `G:/Game/DNF_ACT1/DF/town-test`，
  逐文件 SHA256 相同；原文件备份在 `backups/party-fix-20261024-a1e17bf8`，未改配置/存档、未重启应用。
- 验证边界：以上自动化不能代替新一轮双客户端实测。历史 stableverify 在全员进图后
  还记录了独立的客户端 `0x9019C1` 异常，其对象损坏来源仍未定位；本轮 ACK 修正
  不构成该客户端异常已经解决的证据。

**2026-10-24 07:30–07:35 双客户端实测通过一轮（仅内置输入）**

- 本地服务端 PID 47576；A=`DF/DNF.exe`（shizuku/mageco，PID 39024），
  B=`Client_test2/DNF.exe`（test/召唤师，PID 75764）。鼠标、键盘和截图都由 IJL15 内置接口执行，
  未调用 computer-use 或外部系统鼠标/键盘注入；两个客户端目录隔离计划。
- 内置输入新增 `auto-input.focus` PID 请求，成功后改名为 `auto-input.focus.done`；
  UI 线程 100ms 定时器使后台窗口也能取激活请求，输入计划仍受前台门槛保护。
  新原生模块构建及兼容测试通过，两个客户端和启动器均已安装同一份 DLL。
- 通过队伍信息窗口创建/加入队伍，07:30:14 成队；到 07:34:52 最终双端截图已保持 4 分 38 秒，
  未自动退队。07:33:29 同一副本实例完成 `all 2 member(s) finished loading`。
- 07:34:03 召唤师先死亡，`all-dead return deadline=(null)`；07:34:22 mageco 才死亡，
  前一名死者已留本 19 秒。全灭期限=07:34:32，07:34:33 两端一起通过
  `party-death-countdown-expired` 回城（10 秒期限 + 扫描延迟），队伍保留。
- 两端回城后探针均为 `state=1(TOWN), town=5, area=2`；进程正常响应，当前两个 PID 的
  VEH 异常记录为 0。最终没有待执行输入计划，客户端与本地测试服保持运行。
- 原始证据：`.tmp/party-live-20261024/report.md`、`result.json`、`server.log` 与 `evidence/`。
  复活取消计时本轮未验证：按 X 的实际时间已晚于双方回城，不能计作复活通过。
  这是一轮成功实测，不能外推为所有偶发连接问题或旧客户端异常都已解决。

**2026-10-24 07:54–08:09 追加四轮：重组、交换队长、倒计时内复活**

- 全部客户端操作仍只走 IJL15 内置输入/聚焦/截图接口。四轮均实际成队且完成同实例双端加载，
  本轮未复现“连接中超时自动退队”。首轮沿用旧版，后三轮使用下面的新唤醒修复。
- 重组后立即进图暴露入场等待：07:55:27 投递选图，申请者直到 07:55:49 的 CMD161 心跳才取走，
  延迟 22 秒；同轮入场邮件延迟 4 秒。原因是入队前已经按“未组队”进入无限 TCP 读，
  跨会话 NOTI9 不会让它重新计算队伍状态。
- 修复：新增 `EntranceSessionWakeSignal`，NOTI9 触发 Pulse；各读分支将原 TCP 读、期限和
  成员变化一起等待，先捕获唤醒代次再检查成员状态，避免丢唤醒。单人无成员变化时保持无限等待，
  不增加无条件轮询。真实 PartyService + loopback TCP 回归、Release 构建及全量冒烟测试通过。
- 已部署新 Server DLL，SHA256=`0626216AD4ECF3C020CEAEEB81B01867858E7FADD85D34555E37BEF701F7EC5A`。
  后三轮分别由 mageco / 召唤师 / mageco 任队长，选图和入场邮件均在投递同一日志秒取走，
  约 1 秒全员完成加载。两次标准全灭回城均约 11 秒，死者在最后一人死亡前分别留本 17/13 秒。
- 复活两次通过：07:56:34 在全灭约 1 秒后复活；08:07:52 在旧期限 08:07:55 前约 3 秒复活。
  两次都取消旧期限，观察至旧期限后 15 秒无回城。末轮 08:08:14 再次死亡，产生新期限
  08:08:24，并于 08:08:25 正常整队回城，不能混作旧计时未取消。
- 另一个未修缺陷如实记录：首轮结束清理时，B 主动回城导致死亡的 A 端崩溃。
  07:58:51.958 收到连续 NOTI9/23，27ms 后同一线程异常，返回链指向失效 Stage 对象虚调用；
  具体致错通知/生命周期原因尚未确认。修复后的三个常规轮次无新 PID 的 VEH 异常，
  不能外推主动回城崩溃已解决。
- 证据在 `.tmp/party-live-20261024-repeat/report.md`、`comparison-facts.md`、两个 watcher JSON、
  `server-wakefix.log`、`r1-crash-excerpt.txt`、`wakefix-build.log`、`wakefix-smoke.log`。
  当前服务端 PID 59396、客户端 A=41600/B=38720 均运行，已回城且保留队伍，无待执行输入计划。

**2026-10-24 08:26–09:20：队友离场导致死亡观战端崩溃已修复并实测**

- 原版已重现：另一队员存活时主动回城，死亡观战端在角色移除后调用失效对象而退出。
  去掉重复 NOTI23 仍复现；在 NOTI9 后追加 NOTI32 也赶不及：客户端最后一次 TCP 接收只有
  NOTI9，约 2ms 后渲染已崩溃。已撤掉无效的追加通知和异步锁，没有改写生命状态。
- 新增 `stage_follow.cpp`：在地图索引移除完成、actor 脱离前同步解除匹配的观战引用，
  调用原生选择器重选仍在地图中的角色，无候选则清空。仅副本状态3、同一地图、目标精确匹配
  被移除远端角色时执行；非副本在读取 Stage 前退出，本地角色保留原生处理。
  初版补丁的城镇镜头回归在实机中发现并修正，最终版跨图和副本回城镜头均正常。
- 安装校验完整指令边界及所调函数签名；实际 DLL ABI/反汇编与独立审查通过。
  原生 Release 与完整兼容测试通过（含释放后渲染、空/失效候选、本地跨图和非副本早退）；
  托管 Release 0 警告/错误，全量 smoke 通过。服务端保留名册已移除角色时不再补发 NOTI23。
- 最终原生 DLL SHA256=`C940A2809D307CB605D18E7371BC90D3F9D537A02834F63778A8CBF5D42A9E73`，
  两客户端及各自启动器四处哈希相同；Server DLL SHA256=
  `B0901BADAE5E088B741B0C0393BB63F993990560FFE48A7E3FDD6F0871BAE3F2`。
  未替换配置和存档，旧文件已备份。
- 三轮离场回归通过：09:10:20 A 死亡/B复活后存活离场；09:12:47 交换队长，B死亡/A存活离场；
  09:19:18 全灭后 A 未复活而退队，B 仍按原定 09:19:26 回城，没有重置期限。
  三轮原生清理日志均命中，离场后观察15秒无异常/退出，受影响端均通过新的内置主线程输入和截图。
  另一次全灭准备超过脚本上限，未计作离场通过；随后双端自然全灭并正常回城，已如实留档。
- 所有客户端操作仅 IJL15 内置输入/聚焦/截图，无 computer-use 或系统输入注入。
  当前服务端 PID64952、A=61204/B=37188 正常运行，双端已回城5/2，无待执行输入或控制锁；
  两个当前 PID 自启动以来 VEH 异常均为0。此次未出现连接超时，不据此宣称偶发网络问题已穷尽。
- 完整记录：`.tmp/party-departure-fix-20261024/report.md`、`final-verification.json`、
  `v5r1-v5r2-independent-verification.json`、`v5r1/`、`v5r2/`、`v5r4/` 与原始日志/截图。

**2026-10-24 09:30–09:43：崩溃修复后追加四轮组队/全灭/复活实测**

- 使用原native `C940A280...D42A9E73` 与Server `B0901BAD...71BAE3F2`，本轮未修改产品代码、
  重启应用或更换版本。四轮真实成队并完成同实例双端加载，选图/入场投递均在同一日志秒或
  约1秒后取走，READY后1–2秒全员加载；没有连接超时、断线或会话错误。
- 两轮自然全灭：首名死者仍留本16/12秒，最后一人死亡才设10秒期限，实际末死后约11/12秒
  双端回城（含扫描及会话处理），没有提前回城。422次双进程采样均存活，新增VEH为0。
- 早复活：09:34:42全灭，原期限52；B作为队员在44复活，取消期限，观察至09:35:07，
  两端仍Dungeon。35:25再次全灭产生新期限35:35，35:36正常回城，属于观察窗口后的新计时。
- 晚复活：09:41:01全灭，原期限11；检测后等6秒按X，B作为队长于08复活，期限取消；
  观察至41:26，两端仍Dungeon，41:49/51原生截图再确认无回城倒计时。A复活币为0，
  故未购买或改库存，末轮改测已有币的B，不声称已验证A实际复活。
- 末轮42:01窗口结束后B主动回城清理，A原生观战引用清理命中，42:11按离场后新期限正常回城，
  无崩溃。四轮都在回城后继续观察15秒，并补双端新鲜内置主线程输入/截图。
- 客户端全部操作仅IJL15内置键鼠/聚焦/截图接口；observer仅只读。当前服务端64952、
  A61204/B37188正常运行，09:43双端Town5/2，无待执行输入或控制锁。此次本机四轮未复现超时，
  不据此宣称穷尽所有偶发网络故障。
- 完整记录：`.tmp/party-postfix-regression-20261024/report.md`、`final-verification.json`、
  `r1/`至`r4/`的冻结日志、两份revive watcher与`evidence/`。

**2026-10-24 09:50–10:05：A补10枚复活币并完成A自身复活取消回城实测**

- 使用测试服正式管理邮件发A/mageco 10枚复活币（item1），邮件1188890501；客户端仅通过
  内置输入打开社区→MAIL→读信→双击附件。09:54:16实际CMD98成功、NOTI13刷新，
  邮件附件清空，slot1/item1数量0→10，原生背包显示0010。没有直接改库存/客户端内存/存档。
- 首轮A作为队长：09:57:50末死，原期限09:58:00；A/UID1于56真实复活，币10→9，
  旧期限取消。09:58:05再次死亡触发新全灭，污染旧期限后15秒窗口，因此不计完整通过；
  双端09:58:16按新期限正常回城，无异常。
- 第二轮改B队长、A队员。A10:03:17末死，原期限10:03:27；检测后等待6秒按X，
  A10:03:24真实复活，币9→8，life alive、deadline=null。A通过内置方向键/C持续跑跳，
  至10:03:42双端仍Dungeon，完整跨旧期限15秒，无回城或新全灭；10:03:45原生截图
  显示A存活、B死亡且无回城倒计时。验证对象是A/UID1，并非此前B样本。
- A10:03:56才再次死亡，晚于观察窗口结束14秒；产生新期限10:04:06，两端按新期限回城，
  与已取消的10:03:27期限区分。第二轮193次双端采样全存活，回城后再观察15秒，新增VEH0。
- 10:05原生背包与持久库存均确认A剩8枚，双端新鲜内置主线程输入/截图成功，Town5/2，
  无待执行输入或控制锁。客户端操作全程IJL15内置接口，产品代码和运行版本未改。
  测试服64952、A61204、B37188保持运行。
- 证据：`.tmp/party-a-revival-20261024/report.md`、`final-verification.json`、
  `final-independent-verification.json`、`coin-claim-independent.json`、两轮冻结日志与`evidence/`。

**2026-10-24 10:09–11:27：队友血条同步及死亡召唤物清理顺序修正**

- 队友mode1/3恢复比例改为目标角色实际虚弱/黑钻投影；城镇缓存基础/详情成对有序发布，
  分钟恢复只刷新同城已知角色，副本不重播HP初始化。再修正原生详情先初始化HP、后加装备上限的顺序，
  改为城镇完整装备/技能加载后初始化一次HP/MP。初始、双方受伤、低恢复及分钟tick均实测一致。
- 联合血条候选副本190次配对current/maxHP零差异；低恢复37时双方A561/1517、B760/2055，
  tick至46后双方A697/1517、B945/2055，12次城镇配对零差异；副本内恢复tick没有回满HP。
- 旧C940已抓到活赫德尔在主人死亡后owner0、authority转给存活队员而继续留在双方战斗列表。
  最终在4C6700角色移除入口、原生回调前执行真实owner-loss策略，并保留42082D覆盖最后存活者
  跳过移除的路径。仅当前副本地图、真实死亡Player的配置1/2召唤物；沿用原setter与状态发送器，
  只发送本机原控制且确实排队的死亡状态，保留宠物/持久召唤及原Stage4C6932修复。
- 托管构建/聚焦/完整smoke和原生完整兼容测试通过；原生0警告/错误。独立源码、真实机器码、
  两端已安装跳转对照通过。最终native F6DE9591E12F6235ABB7A8B21D651AA8061B8EA90FC3B0BF3AAB9FC6DDD69AE5，
  两客户端/两启动器四处一致；Server D4CF4F9077F6434780798898F709FF7C1A831CC38DA326A89263FACBB7CAE548。
- r7有效实机样本：主人死亡时赫德尔仍1352HP，随后双方对象消失，无持续残留；首死未设期限，
  A最后死亡才设11:26:08期限，双端正常回城。44次独立只读采样无VEH/退出/超时或会话错误。
  限制：本轮活召唤实际走原生更新清理，新增Map/NOTI32日志均applied0；不声称已直接命中罕见
  失败顺序分支，该分支由聚焦回归/静态时序核验覆盖。无召唤或先被击杀的其它准备轮均未计通过。
- 11:27双端Town5/2同队，新鲜内置主线程截图/输入成功，HP自/远一致，无待执行计划/控制锁。
  客户端全程仅IJL15内置输入/聚焦/截图，观察只读，无操作电脑技能或内存写入。
  Server71840、A78036、B74228保持运行；配置/存档未直接修改，替换文件均有备份。
- 完整记录：`.tmp/party-hp-summon-fix-20261024/report.md`、`combined-hp/`、
  `bhost/`、`fixed-r1/`至`fixed-r7/`、`final-hp.json`、`evidence/`及构建日志。

**2026-10-24 11:32–12:38：追加7轮入本；两处HP回归已修，跨分钟选图仍失败**

- T1/T2b/T3/T4/T5五次有效捕捉“主人死亡时赫德尔仍有2288/355/948/1853/1913HP”，
  随后约1秒内双方离图，没有持续隐形作战残留。包含B队长/队员、首死/末死、多代重召、
  设置面板及同计划快速接近。新增Map/NOTI32分支仍全部applied0/flushed0，不能声称实机命中。
  T3/T5 helper先超时，随后独立observer才取得有效死亡；T6角色未死、召唤约201秒正常到期，
  不算死亡清理通过。没有观察到VEH崩溃或实际连接超时。
- A在T1/T4全灭倒计时内真实复活两次，旧期限取消；T4观察后再次死亡产生新期限，未混算。
- 修正回城异步Town过渡导致旧死亡HP保留：服务端detail排在名单后，native在stage4暂存完整
  mode1/3，于42FC6F真正Town1完成后按UID/User/Actor注册身份一次初始化。两端实际queued→init
  命中；首次低10回城即四视角A151/B205一致，未等minute tick，Dungeon不回满HP。
- 修正散队后foreign模型销毁、重组仍被已交付缓存压掉：NOTI9全部发送后仅向实际同队同区
  城镇成员强制重放cached mode1，subject gate/注册/basic/instance/Dungeon/live-roster检查保留。
  旧NULL模型会让Town HUD回退满59px（已静态证实）；旧坏跨度46.102秒。新FE9在接受后的首样
  即重建，12:29:15低19重组双方A288/B390匹配，早于下个恢复tick。
- **尚未解决的真实边界：**T7在选图跨12:33:00恢复46→55，A33:02主动入本、B被动跟随。
  A自己697/1517、B看A834/1517，持续21.031秒至首次受击；B自己945恢复55、A看B945恢复46。
  NOTI4在scene2只改恢复，mode1在scene2却初始化HP；被动mailbox还可早于pending恢复安装
  使用旧record。两链必须对齐，不能只更新record、泛化HP重置或等伤害掩盖。本轮产品冻结，
  明确不把整个血条问题判全绿。抓包解密、精确数值与静态链均保留。
- 最终Server FE9FE2A8438CEBB433CF737120D08F7E863DD11AD56B911F71C5691F516FB985，
  native A28CEE6A074522207EEEE535DB282A1A2A7ADF70514C99175DA65CCC4BA01CFE四处一致。
  构建/聚焦/完整smoke及原生完整兼容测试通过，独立锁序/ABI审查通过。
- Server39404、A42952/B51528保持运行，12:37:44双端Town5/2同队，四视角HP一致，无输入锁。
  全程客户端仅IJL15内置输入/聚焦/截图，观察只读；没有电脑操作技能、系统注入或内存写入。
- 总报告 `.tmp/party-summon-targeted-20261024/report.md`；失败边界 `hp-boundary/report.md`，
  最终重组验证 `hp-final/report.md`，各轮/observer/构建及原生截图一并归档。

### 2026-10-24 12:43–13:14：修复跨分钟选图HP；六轮双客户端验证通过

- 修复此前FE9/A28在选图跨分钟时本机HP保旧、foreign mode1按新恢复初始化，以及被动信箱发旧恢复值的双重问题。
  原生NOTI4仅在Town/纯选图初始化本机，mode1/3等装备完整后初始化；识别NOTI30后仍scene2的handoff，禁止加载中回血。
  服务端恢复回调仅排角色ID并唤醒，所有请求/无输入信箱在入本前串行读最新存档恢复字段；不信任可延迟的旧byte。
  入场快照后至本客户端本轮NOTI30实际写完前，恢复字段和通知一起保持；短锁代次保护防止旧加载完成释放或回退新轮。
  单人加载、旧角色/旧弱化/付费恢复、交易安装及Town返回均有保护，原回城延迟初始化与召唤/Stage修复保留。
- 最终Server B56AD63CA94B951796B23CE91C92D71191EDE0081FDE0FF35F1A1CEE87E4B193；
  Native EE768C36AED5CCFC19A0B176181B48C14D6E948874F6C141CF003CECECBD34BA，客户端与两启动器四处一致。
  Release构建、原生完整兼容测试通过；managed完整smoke在最终短锁收口前通过，收口后重建+聚焦测试再通过，含1000轮并发旧/新代次测试。
  独立发送顺序/锁序/ABI审查通过，两端各7处hook机器码已只读核验。
- 真实六轮party ready/all2loaded：普通A队长；A两轮28→37、46→55选图跨分；B两轮73→82、91→100跨分；B整分前入场跨加载。
  被动详情真实TCP均为最新37/55/82/100，初始HP在首次伤害前即一致。R5真实mode1(19)→本端NOTI30→NOTI4(28)，
  两端直接命中selection-recovery-preserved，handoff/正式Dungeon四视角HP保持A288/B390，没有加载中回血。
  R3受伤A1017/B0在Scene3跨分钟82→91时HP保持，活人不补满、死者不复活；自然全灭及首次回Town血条正常。
- 独立观察2400次双端采样、1534同队可评估样本、692稳定Dungeon样本；Dungeon/Town持续>1s血条差异0，VEH0，实际party-entry超时0。
  8次伤害/死亡传播差异均单采样，7次在下一0.390–0.423s样本匹配，1次giveup离开评估范围。
  选图等待的旧foreign缓存单列，入场前收敛。预备菜单误点重登、giveup后未重组形成的单人尝试均排除，不冒充有效轮次。
- Server35720、A4336/B50792留运行，13:11:12两端Town5/2、party2 B队长，四视角A561/1517、B760/2055、恢复37一致。
  全程客户端仅IJL15内置输入/聚焦/截图；无电脑操作技能、系统输入注入、玩法包注入、进程内存/存档直接改值。
  输入计划/锁已清空，辅助观察进程已结束。未提交Git，既有工作区改动保留。
- 总报告 `.tmp/party-selection-hp-fix-20261024/report.md`；原始/独立HP观察 `hp-live/`，完整TCP顺序证据 `tcp-evidence/`，
  内置输入和原生截图 `live/`，部署前运行文件备份 `backups/20261024-125342/`。

### 2026-10-24 13:25–13:58：修队友空技能崩溃；有人协助的洛兰通关、翻牌及正常回城

- **用户随后纠正：13:55:48进入Boss房间由用户操作，不能作为Codex全程独立操作通过；此前完成声明归因不准确。后续需全程内置输入独立重跑。**

- 用户已移两角色到Town1/2洛兰入口。本轮Codex执行的操作只用IJL15内置输入/聚焦/截图；用户协助Boss门段单列，不能纳入Codex独立输入证明。辅助观察QUERY/VM_READ+日志；没有系统输入注入、电脑操作技能、玩法包注入或游戏状态/存档直接改值。
- 旧EE768/B56AD首房实机抓到A4336首发C0000094@60E362（13:28:46），当时未到Boss/翻牌。ESI为A眼中的foreignB。
  真实foreignB详情nPairs0，Bself25技能包含20Lv10；81B stats与20装备完全一致，UDP技能报文逐字节中继完整。
  foreign解析器会清空并重装技能，导致peer技能20读0级，模板参数生成−100、分母−100+100=0后0/0。
- 根修：RequestedUserInfoDetail支持正确u8/u8完整技能快照，party外观工厂与本机同源GetCurrentGameSkills；缓存恢复/重组保留技能，mode3自/他资料卡也携带对应技能，避免再次清空。
  保留mode1 EXP0/81B stats/装备与此前HP恢复、加载保护。原生仅Mage技能20缺失/0级时走原生析构/SEH清理，正常技能执行原计算，未给通用除法改分母。
- 最终Server71C94F549E797FE2B2173E0AAA467AC943B314A8965BBC595901530181ABA22A、ProtocolB72E16EB199568B6880D9779BEAC0957B6FF1B8C36FA755EF8272B46121886BF必须成套部署；
  NativeBF205E0FDC59A8CFB71791141AB3B4DE80ADB25E69D4D4D3C77DC699C4854A40四处一致。
  Managed Release/聚焦/完整smoke通过，原生Release/完整兼容测试通过，实际hook+cleanup Unicorn8场景全通过，独立review无blocker。
  新A53788/B71248实际peer B技能20=10、完整25技能相同，进本及回Town后仍保留；每端8hook映射/机器码核验通过。
- 一次完整有效双人party2/B队长通关：13:41:26–28入本，首房(0,0)/map3；13:46:37正常CMD48到(0,1)/map8；13:55:48到Boss(0,2)/map14。
  13:56:12 Boss死亡LastBossDefeated、双方49→34/35；25/26双72，30/31双73，34/35各4s自动免费牌commit，A列1/B列0。
  双端到cards9/Continue页面，截图保留正常三选项。13:58:30 B内置点击返回Town，真实75 hover0202→confirm0102，
  B settlement-after-card、A settlement-party-return，31秒双Town。**早于120s兜底，且无settlement-eplp-timeout，不能混算超时通过。**
  新完整通关0VEH、Mage guard跳过0，说明实际完整技能根修生效；回Town四视角A1517/B2055、恢复100、无新虚弱。
- 第二房门口停顿已定位为原生本地双人同门集结要求，非server已收包后超时。阈值floor(livingMembers*.5+1)，双人必须同一真实门RECT内；A曾留上门、B单独尝试其它门。
  PVF拓扑与实际NOTI28/29一致；进入Boss的实际操作来自用户，不能作为Codex导航已成功的证据。没有改门逻辑或强制过图。
- 本轮没有在新版本重现旧“翻牌后卡住”；确证修的是通关前空技能崩溃，且当前完整结算/免费翻牌/正常回城可用。
  未改EPLP/奖励业务，未声称手动付费牌、再次挑战、其它地下城选项已实机通过。
- Server47176、A53788/B71248留运行，Town1/2、party2 B队长。总报告 `.tmp/party-clear-fix-20261024/report.md`，
  首发失败 `watch/`、固定版连续观察 `watch-fixed/`、内置输入及原生截图 `live/`、最终独立证明 `final-proof/`。
  部署前运行文件已备份 `backups/20261024-133938/`，工作区原有改动保留，未提交Git。

### 2026-10-24 14:04–14:17：更正上一轮归因，重新全程内置输入独立通关一次

- 用户明确上一轮13:55:48进入Boss由其亲自操作；旧主报告、独立观察、协议和门静态文档均保留纠正，不把旧轮算root独立通过。
- 新轮产品版本保持71C94/B72E16/BF205，无产品改动、无重启。root独占所有客户端内置输入，辅助代理仅QUERY/VM_READ和日志审计。
  14:07:29双人入场、31全员load；随机Maze0，路线(0,1)map1→(1,1)map7→boss(2,1)map13，正常最短Right→Right。
- root按实际门RECT/战斗XYZ导航两端，先离开旧门区再同门集结。初次目标太靠步行边界的03失败记录保留；调整到门内可达点后03b成功。
  两次真实CMD48分别14:11:13与14:14:38，均与本轮A/B内置计划完成记录相符；不借用13:55旧用户操作，未修改门条件或注入过图包。
- root正常攻击清三房，14:16:31 Boss清房、双方49/34/35；免费牌正常4s计时于53/54秒提交，各自成功一次。
  新结算GUID3cb5e605-37a7-4518-a47c-ef083cf886f8：A+8gold/slot12-item27301×1，B+7gold/slot14-item33000×1，无额外奖励键或其它库存变化。
- 14:17:24双端Continue9，root保存两端原生截图后点击B返回Town，27秒真实75 0202→0102，B settlement-after-card/A settlement-party-return，28秒双Town。
  这是正常按钮回城，早于120s兜底。最终四视角A1517/B2055、技能快照一致、8hook版本正确、VEH/缺技能保护跳过0。
- 两客户端和Server保持运行，洛兰入口Town1/2、party2 B队长。报告 `.tmp/party-clear-solo-driver-20261024/report.md`，
  全部本轮操作与截图 `live/`、独立观察 `watch/`、协议奖励 `protocol-audit.md`、最终只读证明 `final-proof/`。

### 2026-10-24：圣职者治疗目标同步与 BOSS 连锁死亡（源码修复，待真机复测）

- 治疗：新增 `priest_heal_target` 原生补丁。可靠技能状态的显式目标通过 `0x0111:userId`、当前用户目录和同地图成员检查重建，
  在缓慢愈合/快速愈合（51/55）实际读取目标前修复 foreign Priest 的目标；失效的显式目标清空，避免沿用上次施法目标。
  本地确实选中自身且原完成调用省略目标时，补入真实自身对象，仍由原生完成入口发送一次技能状态。
  回血量、持续时间、追加效果和特效均继续由原生技能处理。现有日志没有 51/55 实际施放样本，不能将静态依据和测试计作真机通过。
- BOSS：移除 ALLDIE 的空 NOTI38 和直接清除存活记录；最终 BOSS 的 NOTI127 向全实例广播，让每端设置原生 BOSS 已死标记。
  客户端原生 HP0 死亡上报与会话内有期限的兜底共用完整掉落、任务、经验和击杀流程；实例原子认领防止多端重复发奖励。
  死亡结算计数确保所有已认领死亡的奖励和通知完成后才开放 NOTI31；房间代次保护延迟处理不影响新房间的同号怪。
- 箱子/地堡：未释放的小怪保留存活记录，不阻塞 BOSS 房初次通关，清图证据在开启结算时锁存。
  新增 `boss_room_death` 帧更新补丁，在原生完成怪物 HP/阵营/控制权初始化之后，对 BOSS 已死地图中本机负责的敌怪调用原生 HP0 入口。
  仅处理当前地图、敌方、活怪，友方召唤物不受影响；后续箱子怪正常死亡不会重复开启结算。
- 本轮仅修改源码并构建标准产物，未覆盖运行目录、重启服务端或操作客户端。上线需要配套更新服务端和两个客户端的 `ijl15.dll`。
  待双端复测：给队友治疗、自己治疗的双方血量/特效；BOSS 旁小怪死亡动画及掉落；通关后破箱/地堡出怪即时死亡且不重复结算。
- [回放] 最终 .NET Release 构建、`--boss-room-death` 定向测试和完整协议冒烟全部通过，含多 BOSS、并发奖励去重、死亡结算屏障和换房代次回归。
  MSBuild Win32 Release 产品/测试构建及完整 `ijl15_tests.exe` 通过；[静态] 新挂点与 vtable 原始字节均已对实际 `DF/DNF.exe` 核对匹配，独立 ABI/时序审查无阻塞问题。
  本轮构建 Server SHA256 `AB7E2B892316271DD3FB9D3439D57D1BF8620280A5FD601B41AB32723CE409BB`；
  Native SHA256 `F060EEB133ECAC55AAC20FC7C809FB6721073481215A662156F9BB14C0FD7A71`。这些是构建产物校验值，不代表运行中版本已更新。

### 2026-10-24 16:54–19:43：治疗、BOSS小怪死亡/掉落、通关后箱怪及拾取同步实机验收完成

- 本节取代上一节“仅源码、待真机复测”的状态。服务端与双客户端已实际部署，最终四处Native均为
  `83BC64054436ADA8CBDDEA2CDE0E64A682C3C0FAE20CB4227D04AA7475366701`；
  Server `6E99BC20DC0A1027DBF6BA1119467D941619303CC136C18B62AA0F0CDD1CBB68`，
  Protocol `A92D197A0D7D5A4AEBBE44C31AC8C38B36273900BDF4A2A1B371BA3024F4BD3D`。
  Release产品、完整.NET冒烟及最终原生完整测试通过；替换前文件有备份，19:43:36再次核对运行目录hash一致。
- Slow Heal51对队友与自疗均观察到本机真实回血及双端治疗/动画对象、绿色效果；干净队友窗口552→582，
  自疗两次17→47、47→77，各回复30。用户亲自协助操作并确认生效，不算全程root独立输入。
  Fast Heal55未学习、未live，仅有实现和自动化覆盖；治疗恢复/归一分支没有动态命中日志，不单独作因果宣称。
- 最终83BC在A「mageco」45/B「召唤师」52的实际BOSS清场中，UID4由59HP进入0HP/dead及死亡动作4；
  19:09:41.420/.418正常38真实生成ground6金币30，双方地图实体/descriptor均存在；UID5空38随后才各一次31。
  这是真实小怪自然非空掉落，不借用BOSS自身掉落或旧9AF0样本；实例认领、死亡奖励屏障和通关去重保留。
- 拾取补充真实groundId的9字节NOTI86，原7字节包和个人奖励39保留原行为。最终83BC的ground1/item1047
  于18:21:41.275/.278双端收同通知，.398/.399从各自原地图移除，B原生deactivated；A库存+1、B不变，无重复奖励。
- **严格通关后箱怪已通过：**Dungeon2/map117/count6；31于19:40:46.512/.513各一次，
  cards9于19:41:15.747/B16.751；继续态箱11双端仍active、UID5尚无。root19:41:23正常单X破箱后，
  UID5于19:41:24.191双端配对首样即HP0/dead，随后死亡动作5；B25.580真实CMD42 CRC有效，
  双方25.585/.582正常38(drop0)，没有二次31。该箱怪自然空掉落不冒充非空掉落；地堡未单独完成严格释放实测。
- 实验性4C60E2 allDie guard已撤回，最终83BC恢复原生分支，保留frame HP0与服务端结算/拾取修复。
  旧9AF0、提前毁箱、无隐藏怪地图、D3全灭等历史实验/失败尝试均不计本次严格晚出怪通过，未证实的提前毁箱触发者不作归因。
- 19:43:00经正常回城双端scene1满血A1517/1517、B2055/2055；Server71780、A66248、B78920保持运行，
  无待执行auto-input或lock。观察只用进程查询/VM_READ与真实日志；正常输入仅既有IJL15，未写内存/改存档/伪造玩法包。
- 最终报告 [.tmp/dungeon-support-live-20261024/report.md](G:/Game/DNF_ACT1/DFLegacy/.tmp/dungeon-support-live-20261024/report.md)；
  关键证据 `final-runtime/final-deployment-proof.json`、`final-town-proof.json`、`audit/final83bc-pickup-evidence.json`、
  `audit/mageco-run/first-clear-evidence.json`、`cascade-ground6-first.json`、`post-clear-box-evidence.json`、
  `post-clear-box-death-tx.json`、`boss/post-clear117-child5-observation.json`及`live/217-...-clear-proof.json`；
  治疗原始证据 `priest/report.md`。旧final-report-draft已标历史并由最终report取代。




### 2026-10-24 20:34：独立副本 Debug 覆盖层与玩家控制的只读监听

- 新增 `src/DFLegacy.Debug` x86 `debug.dll`，由 IJL15 首个原生文字绘制帧在 DllMain 外延迟加载；渲染通过游戏自身文字接口叠加，城镇隐藏，副本显示，F9 详情/F10 隐藏。面板显示本次 BOSS 房是否有箱/地堡怪、容器脚本索引与坐标、怪物编号/等级及释放/死亡状态。
- 调试默认关闭。服务端开启 `Debug.ClientOverlay.Enabled` 后提前准备 BOSS 房并缓存，实际进入复用同一出怪表/种子；不改变访问/首访/清房状态。生成时机提前会改变本轮随机数消耗顺序，概率规则不变；关闭后恢复原先时机。服务器每250ms原子发布对应区用户ID的本机二进制快照。
- 客户端后台线程读快照，渲染帧不读文件；按区用户ID、进副本时间及1.5秒有效期过滤。无法读取显示等待数据，不误报无怪。独立 `tools/debug/watch_dungeon.py` 只读记录房间、隐藏怪状态与结算变化，不发送输入或修改玩法。
- [回放] 新模块编译通过；C#→C++实际二进制互通与真实debug.dll中文绘制回调、错误/重复ID/过期/错用户过滤、退出副本不渲染、安全退出线程均通过。服务端预生成/首访/复用与快照发布、短暂锁文件恢复、离开后清理测试通过；完整.NET协议冒烟和现有原生完整测试通过。
- 已备份并部署服务端、四处 `ijl15.dll`+`debug.dll`，两客户端 `EnableAutoInput=0`、`EnableDebugOverlay=1`，服务端调试发布开启。20:34启动Server64184、只读监听57652；客户端76084/4272已启动，未注入任何角色操作。启动阶段仅验证IJL15加载，debug.dll等待进入会执行性能绘制的场景后延迟加载；游戏内视觉验收待玩家进图确认，不将绘制回调自动化计作真机截图。
- 配置/使用/构建说明 `tools/debug/README.md`；部署与备份 `.tmp/debug-overlay-20261024/deployment.json`，自动化日志同目录，玩家测试事件保存 `events.jsonl`。本轮未自动进图、战斗或选择角色。

### 2026-10-24：Debug覆盖层首次真机进图双端退出后的修订

- 用户20:39手动组队入图后两个客户端退出。原生自动输入已关闭，没有本轮VEH记录或WER崩溃转储，不能仅凭现象宣称已确定全部闪退原因。服务端/只读监听成功记录D2、boss map117、箱2隐藏UID5，说明真实出怪快照已生成。
- 静态核对发现新桥接错误地调用4073D0（返回+D0对象种类）当玩家ID。现改为只读校验保护整数：场景game+10、UID actor+D4；校验镜像和索引，过渡态返回无值，不调用原生断言/修复路径。地图与本机actor匹配后才绘制。
- Debug文字原先放在Text函数局部GBK数组，多行复用同一栈地址。新增真实DLL回调延后消费用例，旧运行DLL在相同测试目录明确仅此一项失败，修订DLL通过。现每行有跨本帧有效的独立固定缓冲区，所有文字共用原生每帧beginText批次，不逐行重置批次。
- 新增独立debug-overlay-<pid>.log及有限次数被动异常记录，包含模块基址、异常EIP/寄存器和首批进图绘制完成记录；不安装自动输入。Render异常时停用覆盖层，避免扩散到玩法循环。
- 新模块测试与原生兼容测试通过，四处配对DLL已备份替换，未重启Server64184或监听57652；客户端仅重新打开，后续仍由用户手动进图验证。部署证据 .tmp/debug-overlay-20261024/crash-fix-deployment.json；旧DLL回归 .tmp/debug-overlay-20261024/old-dll-regression.log。上述缺陷修正不等于已取得本次实际崩溃的调用栈，真机结果待用户确认。

- [真机补充] 修订后用户继续手动组队入图：A/DF PID8844、B/Client_test2 PID35560均实际加载debug.dll，进图记录正确UID2/UID1并连续完成原生覆盖绘制；只读监听取得双方同run、boss map118、容器0坐标295/229的隐藏UID6。两进程在首次绘制后至少30秒继续存活，未记录exception/overlay-disabled。此项证明新版本已跨过前次进图退出的边界，面板实际可见与可读性仍待玩家确认；root未发送游戏输入。证据 crash-fix-live-observation.json 及两端 debug-overlay-8844/35560.log。

### 2026-10-24 20:54：玩家确认 Debug 与通关后破箱通过，记录里程碑

- 用户明确确认“能够进图，面板正常显示”，并手动在存在箱怪的BOSS房先击败BOSS、再破箱，新怪出来立即死亡，通过。该次只读事件为同run639284430837438787、D2/map118：双方20:53:35箱0/UID6未释放；20:54:02清图结算开启且仍未释放；20:54:08双方状态均已死亡，清图状态保持开启。
- 此项补齐修订版游戏内可见性与实际通关后释放路径的用户验收；原始“自动化不能替代视觉确认”和首次双端退出记录保留为历史。全程本轮玩法输入由用户执行。
- README与新增CHANGELOG记录该里程碑及验收边界；提交覆盖当前可构建的相关源码、测试和验收工具，不含临时运行日志、二进制、存档及本机逆向工作文件。

### 2026-10-24：五城赛丽亚房间隔离、debug v2 浮动面板与怪物位置同步核查

- [静态] ACT1 客户端 Script.pvf 的 town/{Elvengard,HendonMyre,WestCoast,Alfhlyra,StormPass}.twn 中私人 [gate] 区域分别为 1:1、2:5、3:2、4:1、5:1。旧配置仅隔离 1:1，遗漏包括实际使用的雪山 5:1。TownSessionDirectory 固定隔离五处房间；PrivateAreas 保留为空的额外列表。覆盖各房间的名单/外观/移动隔离及 2:1、3:1 普通街道共享回归。
- [实现] debug.dll / IJL15 ABI v2：独立所属浮动窗口，有背景，置于游戏聊天之上；标题拖动、边框缩放、滚轮、圆点收起/恢复（F9）、隐藏/恢复（F10），X/Y/Width/Height 保存在客户端 Config.ini。只在前台游戏副本场景显示，窗口不激活、不转移键盘焦点。两 DLL 必须配套更新。
- [回放] Release .NET 完整冒烟通过（.tmp/sync-inspect/smoke-final.txt），0 编译警告/错误；debug 模块测试 0 failures（含真实窗口创建、所属关系、命中区、圆点/恢复、尺寸保持、焦点保持、隐藏与安全卸载）；既有 ijl15 compatibility tests passed（在测试二进制目录运行）。git diff --check 通过。
- [未验证] computer-use 视觉预览被用户物理 Escape 中止，已停止桌面控制；没有把窗口测试记作游戏内视觉验收。聊天覆盖效果、真实游戏拖动交互与独占全屏待实机确认。
- [分析] 本轮未更改战斗同步。当前主机槽位已全队指向队长；实时移动由原客户端控制，中继仅逐字节转发。原客户端 GP4 会核对房间和对象控制者并应用位置插值/立即修正，GP6 切换每只怪物控制者；队伍主机不等同于每只怪物永久控制者。旧日志发现相同怪物被两端声明不同控制者的移动包，但未与本次神枪手跳变对齐，不能定根因。官服 relay 样本按目的账号转 TCP，封装不能直接等同于 ACT1 的本地 UDP 中继。完整证据与下一步建议见 knowledge/decisions/2026-10-24-monster-position-sync.md。
- [部署] 无 DNF 客户端、无 2311/7001/8080 活动游戏连接时更新 DF、Client_test2、DF/town-test 的两 DLL 以及服务器程序集，逐文件 SHA256 与构建一致。空闲服务端重启 PID=56564，/health=ok，UDP relay 与服务端 debug snapshot 均启动。备份 backups/2026-10-24-seria-overlay-v2-565768eb（含旧模块、server.json、state.json）；清单 .tmp/sync-inspect/deployment.json。首次替换遇退出尚未完成的 DLL 占用，已回滚；等待旧进程退出后重试成功，未修改活存档或运行配置。
### 2026-10-24 21:59–22:04：用户手动完整副本与动作流畅性只读分析

- [真机] 用户手动 21:59:44 成队，21:59:52 进副本2，map101→109→107→119；22:01:41结算，22:02:25全队回城。用户表示本轮队长无可见怪物位置回拉，队员观察队长动作不流畅。
- [抓包/回放] A队长uid2/PID43828→B队员uid1/PID57708：478个GP4位置包与200个GP5动作状态包全部对齐；跨两端socket数据报的时间差中位11ms、p99约16ms。原始入场数据报存在少量未配对/重传组合差异，不能表述为全部UDP零丢包。
- [静态] 原客户端移动补发默认500ms（0xB0E9E8），另有状态变化触发发送（473490/4C9F00）；接收端4C9270负责插值。服务端玩法中继没有固定批发周期，城镇10次/秒限流不用于副本。
- [未验证] 队员动作状态采样的可匹配变化常晚约一个100ms采样周期；这不能精确归因于主线程或后台限帧。本轮没记录逐帧FPS/前台状态，后续应按收包→动作应用→绘制进一步区分。
- [真机] 100ms怪物控制者快照有短暂差异，同一怪物最多连续2个点、跨度约94ms，本轮并无用户确认的可见回拉，不能仅以交接判错。采集14657份，顶层读取错误0，已停止并保存。
- 分析与证据边界：knowledge/decisions/2026-10-24-completed-run-motion-analysis.md；产物 .tmp/monster-sync-manual-20261024-213946/。未修改同步行为或产品代码。

### 2026-10-24：双开后台降帧、队员动作应用和位置插值核查

- [静态/真机] 原生 WM_ACTIVATE 处理 8F76D0 在失焦时把进程设为 BelowNormal、主循环休眠配置设为15ms；8F7170在帧回调之前休眠，影响主线程游戏更新，不只绘制。现场两个后台进程均 BelowNormal。
- [真机] 22:30–22:32只读采样21170份：B队员前台原生FPS中位227.54、后台约32.4；A前台中位210.17、后台约32.4。用户也确认前台约230/后台32。此实验为城镇静止状态，不能冒充上一轮副本的逐帧复现或证明全部顿挫都由此造成。
- [静态] 433B00每帧调用905810消费收包队列，GP5在78EE00同次分派中调用角色状态处理并清除相同待处理动作，未见外层固定100/500ms缓冲；公共4A0920落入混淆区，未取得每个动作实际写入/绘制的精确动态时刻，不宣称所有内部等待均已排除。
- [静态] 473490→4C9F00→4C8C00按实际经过时间逐帧推进运动；4C9270普通分支建立位置修正，0x10分支直接复制运动状态。500ms参数不是动作播放/绘制间隔。后台插值仍推进，但每秒显示与消费机会减少。
- 已确认后台降帧是明显影响因素；保持同步频率、插值参数、客户端补丁和优先级原样。详细证据、限制和后续比较顺序：knowledge/decisions/2026-10-24-background-fps-motion-apply.md；采样 .tmp/frame-pacing-20261024-223010/。

### 2026-10-24 23:18：取消原生后台降速并部署双客户端供手测

- [实现] IJL15新增两处受原字节校验的补丁：8F727D改为无条件跳过主循环Sleep；8F772E移除进入BelowNormal/15ms分支的条件跳转，WM_ACTIVATE仍写入真实激活值，随后统一执行Normal/0ms路径。同步频率、GP4/GP5处理及插值参数未改。
- [回放] Win32 Release构建通过，现有完整ijl15 compatibility tests passed。两份DNF.exe均匹配原始补丁位置；原始EXE文件未改。原生测试与构建日志 .tmp/sync-inspect/no-background-throttle-{build,tests}.log。
- [部署] 用户要求禁用电脑使用技能后，全程通过命令行部署与进程管理；两个旧客户端在城镇收到正常退出请求，15秒未退出后仅终止已校验身份的43828/57708。四处ijl15.dll备份后替换，包括两端启动器目录，启动后再次核对SHA256均为DE7AAA5AFDA16D1DC7D18BD19947382880268BB54DC9A14C7EC7764029574500；未重启Server56564或改活存档。备份 backups/no-background-throttle-20261024-231607，清单 .tmp/sync-inspect/no-background-throttle-deployment.json。
- [真机] 以既有启动器headless模式、原保存账号重开：A63080、B70916。只读核对两端实际执行内存的两处补丁均正确；4C9270前2048字节及4C9F00前1360字节与原EXE一致，B0E9E8仍500。23:18采样A前台约210FPS、B后台约225FPS；两端休眠配置0、进程Normal，后台已不再约32FPS。证据 .tmp/sync-inspect/no-background-throttle-live-proof.json及新采样；副本动作流畅性待用户手测，不把城镇验证当成副本通过。
- 已开启本轮100ms只读记录：两端socket日志、场景/动作/坐标/怪物控制者，并新增前台PID、激活状态、FPS、休眠配置和优先级。Recorder71820，最多一小时，写stop.request可停止；产物 .tmp/no-background-throttle-manual-20261024-231735/，当前指针 .tmp/sync-inspect/active-capture.json。无自动角色输入。

### 2026-10-24 23:44：后台不限帧手测通过，修正组队免费翻牌显示

- [用户验收/真机] 用户完成一轮并确认动作完全流畅；3279份只读采样、顶层错误0，已停止留存。副本中两端后台FPS中位约163，前台约149–154，不要求两端严格同FPS；本轮支持原后台节流是此前顿挫的主要因素，未改同步频率/插值。产物 .tmp/no-background-throttle-manual-20261024-231735/post-throttle-run-summary.json。
- [定位] 用户截图A端两人都19金币、B端两人都20金币+物品32200；实际各自发奖正确。NOTI35把本人的免费预览写入四个槽位，旧CMD74只更新归属和付费物品；客户端免费渲染按owner槽位读预览，并非卡片位置。
- [实现] 组队CMD74附加版本化DFCR免费显示尾部，来源仅为已提交且归属匹配的免费奖励；IJL15在原生翻牌前按owner刷新组0向量，旧记录先正常析构后替换。实际发奖/个人PremiumItem/付费卡/单人原包保持原机制。无重发NOTI35、无额外结算动画。
- [回放] 组队翻牌0 failures、完整.NET协议smoke通过、x86完整compatibility通过，含C#实际回包→C++读取与卡位/owner交叉映射、不同金币/装备、重复和畸形包、Pending/Unknown/Rejected处理。日志 .tmp/card-display-inspect/；未将自动化当作游戏卡面视觉通过。
- [部署/真机] 命令行更新双端/双启动器DLL及服务端程序集，Server77076健康，A18696/B53060已重开，实际执行内存显示补丁已安装，后台修复和原移动参数保留。备份 backups/party-card-display-20261024-234217/；新只读Recorder67180，采样 .tmp/party-card-display-manual-20261024-234405/。卡面待用户下一轮确认。详细结论 knowledge/decisions/2026-10-24-party-free-card-display.md。
- [23:51用户验收通过] 用户再次手动通关并明确确认“这次对了”；截图显示两名成员各自不同装备、各24金币。23:49:57准备物品13201/33300，23:50:20同一settlement两列各增加24金币/一件物品，与截图一致。本轮抓包已停止留存，免费翻牌显示真机验证通过；证据截图 .tmp/card-display-inspect/acceptance-20261024-2351.png。没有自动角色操作，未改同步/插值。

### 2026-10-24：文档与源码固定范围

- README、CHANGELOG补充五城赛丽亚隔离、后台降速取消、免费翻牌显示，以及升级方法和验收边界；对应源码、配置示例、测试及分析记录一并固定。仍未复现的怪物位置修正、TCP回退配置缺口明确保留为待办。
- debug v2浮动窗口尚未正常显示，用户要求后续再修；这部分开发草稿及无关工作区记录保留，不纳入本次已验收修复提交。已有文字版调试覆盖层的历史验收保持有效。
