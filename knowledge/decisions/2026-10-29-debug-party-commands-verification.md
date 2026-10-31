# 原生组队操作接口与同组副本验收

2026-10-29：debug 已增加 `request`、`accept`、`reject`，并通过双客户端实际拒绝、再次邀请和接受。随后完成了同组普通洛兰的战斗、两次集结过门、Boss 击杀、免费牌结算和回城；回城后两端队伍均保留 UID 1、2。操作主要读取 debug 数据，下面的关键画面均为客户端原生 PrintScreen 原图。

## 接口与验证

入口是 `tools/debug/client_party.py`，包括 `state` 查询。正常两端已启用 `ExportState=1`、`EnablePartyCommands=1`，原有 SnapshotDirectory 保持不变。使用方式见[接口说明](../../tools/debug/README.md)。邀请状态包含来源 UID、剩余毫秒和邀请序号。

原版 ACT1 的请求函数 `4FE9B0`、接受窗口回调 `821DA0` 已独立核对。拒绝分支是原窗口更新函数内联逻辑，接口通过原始客户端编码器发送同样的 CMD11/error100，再执行原窗口关闭；没有外部业务包注入。原服务端实际收到请求 CMD10、拒绝 CMD11/error100、第二次请求及接受 CMD11。

命令绑定 PID、进程创建时间、UID、场景代次、角色、地图、区域及邀请指纹。实机重新投递已接受的邀请命令得到 `replayed_request`；新 ID 的过期命令得到 `expired_request`；新鲜时间戳配旧角色/地图上下文得到 `identity_mismatch`。三项均没有增加服务端收到的组队回复数。短暂观测失败、发送缓冲区忙、多个待处理窗口及非组队邀请的保护另有回归测试。

第一轮实际拒绝时遇到 Windows 回执文件瞬时占用，客户端拒绝已经发出，消费者却没读到回执。现已增加有界读取重试，并测试只重读、不重发；随后邀请与接受流程通过。

## 同组完整副本

| 检查 | 实际结果 |
|---|---|
| 身份 | A：mageco / UID 1 / Lv45；B：heal1024 / UID 2 / Lv6；两端原生名单均为 1、2 |
| 副本 | 普通洛兰；独立服务端逻辑实例一致；路线 `(0,1) MAP2 → (1,1) MAP7 → (2,1) MAP13` |
| 战斗 | B 原生攻击后，同一怪物 HP 两端均从 50 降为 34；A 击杀后两端移除；各房间分别清空 |
| 换房 | A 单独进门时维持原房间；B 进入门区后全队换房，两端代次 8→9；再次共同进入 Boss 房，代次 10 |
| 旧上下文 | 换房后使用原地图上下文，控制器在生成原生输入前拒绝 |
| Boss | 普通 MAP13 的投掷哥布林，初始 HP147；最终两端 HP0、死亡标志及 boss_dead 均确认 |
| 结算 | 免费牌自动定时结算：A 列0、7金币；B 列1、8金币，双方 accepted/published 均为 true |
| 持久结果 | A 经验 +30；B 经验 +354。B 的物品29901因背包满转为一封奖励邮件，持久附件数量1；附件仍在邮件中 |
| 回城 | 服务端以 settlement-after-card / settlement-party-return 返回两人；双方恢复 Town，队伍名单仍为 1、2 |

普通攻击、冲刺及已有原生技能快捷键用于战斗。技能 MP、冷却和可用性仍没有导出；技能快捷键尝试由原客户端检查条件，没有把这些未知字段标成已就绪。

两端成队：

![A 成队](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/01-party-A.jpg)
![B 成队](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/02-party-B.jpg)

双方进入同一 Boss 房：

![A Boss 房](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/03-boss-room-A.jpg)
![B Boss 房](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/04-boss-room-B.jpg)

结算后的继续菜单。免费牌经过4秒定时自动领取；下图是翻牌结束后的菜单，翻牌归属和入账另由原生收包、服务端结算和持久状态核对：

![A 结算后菜单](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/05-settlement-menu-A.jpg)
![B 结算后菜单](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/06-settlement-menu-B.jpg)

回城后队伍保留：

![A 回城](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/07-town-A.jpg)
![B 回城](D:/codex_worktree/party-chat-gold/DFLegacy/knowledge/decisions/assets/2026-10-29-debug-party-commands/08-town-B.jpg)

## 构建、测试与交付

- debug 源码 `a5b625a`；消费者回执重试 `da5a762`。debug.dll SHA256：`B31E9342E5278A41EECE1E8439A9594F7F6CD6C6DFBCD1683223E143683B4570`。
- ijl15 保持已交付 `19C0CC26…`，服务端保持 `59666d9b…`。本组没有改变服务端组队业务。
- Python 80 项通过；原生 party、runtime、state、navigation 及真实 C# fixture/DLL 测试均0失败。独立源码审查发现的邀请序号续期、发送器阻塞/残留缓冲及重初始化问题均已修正并回归。
- 四处 debug.dll 均为实测构建。A 正常 runtime 的50个适用产物匹配；受保护的正常 server.json 单独保留。B 仅核对本次四处部署中的相关 debug 文件，不宣称整个服务端包一致。
- 两端正常配置仅在上次交付版本上新增 `EnablePartyCommands=1`；原路径保留。8个原生输入接口恢复，隔离测试进程已停止；原 EXE、PVF、原存档与其余保护项核对通过。

8张图片的原字节、紧凑[验收数据](assets/2026-10-29-debug-party-commands/summary.json)和[SHA256清单](assets/2026-10-29-debug-party-commands/manifest.json)已保留。完整原始收包、状态、失败尝试和输入收据位于独立私有验收目录。

本轮覆盖普通洛兰，不包含新增冒险级 MAP6416 布局，也不代替四客户端稀有度/史诗骰子矩阵、主动背包丢弃可见性、通用模态 UI 或怪物同步专项诊断。后续继续按[列表](2026-10-28-restoration-backlog.md)推进。
