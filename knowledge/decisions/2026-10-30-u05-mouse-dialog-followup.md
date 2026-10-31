# U05 鼠标门与八类弹窗接续验收

2026-10-30。本轮继续 U05 剩余 debug 数据／CLI，保留 U07 同步／GP42 和 U08 卡顿。源码位于 `D:\codex_worktree\party-chat-gold\DFLegacy`，新增只读字段，没有修改服务端业务或新增 hook。四端验收后已关闭测试并恢复正常文件，日常发布包未覆盖。

## 结果与边界

| 项目 | 实现与验收 | 保留范围 |
|---|---|---|
| type1 鼠标早期门 | `ui.command_input.mouse` 提供独立 scope、锁级、`checker_flag90_raw` 和 `physical_excluded`。四端进入城镇时，同 PID／启动时间／actor／checker／UID 的原生样本均见 level2→0 | 这是物理按钮检查器的早期排除条件。上层白名单可生成命令；城镇地面点击另有路径。flag90=true、level1 仅静态／夹具覆盖 |
| 按钮状态处理暂缓 | `ui.mouse_button_state={scope:"engine_mouse_button_state",deferred:bool}`。自然打开只读邮件列表时，四端均捕获原生 CFD208 的1→0 | 不清空旧 held 状态，坐标与回调仍运行；DirectInput 分支不排队，只有 WM 消息分支排队重放。不是全鼠标阻断或操作完成回执 |
| 八类弹窗与分层 | 覆盖十六进制 **07/09/23/25/30/56/59/6A**，显式输出 `covered_class_ids`。四端新增07创建队伍、09添加／移除好友窗均打开、附着、order2，再取消并移除 | 56密码窗仅原生合同和夹具，未实机进入。通用通知不在八类内；空列表不代表所有 UI 关闭 |
| CLI | **24 条实机等待 matched**：21 条窗内标量＋3 条按钮门释放条件；另有16条保留 SDK 快照回放 | 最后一组附加实机检查因 helper 导入错误未执行，不能计为实机通过。四端全局未知条件的回放均正确超时 |

`ui.input_blocked` 继续为 null。键盘 type0 与鼠标 type1 分开；读取鼠标失败时保留已独立证明的键盘状态。旧 v2 缺新增字段时仍兼容且未知；没有显式覆盖列表的旧数据只接受原五类窗。

## ACT1 与官服逻辑依据

原 ACT1 EXE SHA256 为 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5`；官服 ELF 为 `8859C99D8290DC5FB58151444138CF2697B2BFFF12D47D6159596A03F20B5261`。业务时序对照既有官服还原记录，客户端布局和分支按该 ACT1 原文件核对；官服服务端不能证明客户端 UI 地址或全局输入谓词。

- `8BA7F0` 的 type1 路径检查 checker+68 锁级及+90字节，之后还检查实际按钮和消费状态。`8BAAD0` 上层存在白名单合成命令分支；普通城镇地面点击不只经过此 checker。独立原始机器码 oracle 覆盖144组组合，0失败。
- CFD208 的三处读／置位／清位签名分别在 `8FCAE8/7993C4/8FCC9D`。导出只接受稳定重复读取的0/1；没有调用 setter、消费队列或人为拉长 waiting。
- USERINFO 进入世界时取得 type0/type1 优先级2锁，NOTI136释放 type1；现有 `EntranceService.SendEnterGameWorldCompleteAsync` 已按此顺序适配。此次保留自然进入与释放，不修改服务端等待逻辑。
- 新增07／09／56构造链及专用虚表已核对。类号≤0F属于 manager+C8低层，10～17属于+CC，≥18属于+D0；当前八类只用低层和高层。每次采集共享所需层的前后快照，每层最多128节点，活动／待关闭列表各最多64个受支持窗。
- 损坏层只使对应 placement 未知，不抹去已知登记窗；同层 token／标志与顺序须一致，跨层 token 不可别名。不同层可以都有 order0。不调用原生 getter，不以根／层标志推断像素可见性。

原始范围、字节与审查见[静态证据](assets/2026-10-30-u05-mouse-dialog-followup/static/manifest.json)及[独立代码复核](assets/2026-10-30-u05-mouse-dialog-followup/static/implementation-review.json)。

## 四端实机证据

使用上一阶段已关闭的私有测试档克隆，私有端口47001。启动前建立新鲜备份，拒绝残留输入计划。普通输入经原 IJL 文件接口，读状态优先 SDK／CLI；没有重复已关闭的邀请、MP施放、掉落、击杀或同步矩阵。

| 端 | 本轮 PID | bootstrap 原生样本数 | 邮件按钮门 |
|---|---:|---:|---|
| A | 72288 | 165 | 首个有效邮件列表窗口捕获1→0 |
| B | 55920 | 150 | 首窗捕获1→0 |
| C | 31384 | 155 | 首窗捕获1→0 |
| D | 48384 | 146 | 首窗未捕获；第二次有界采样捕获1→0 |

精确启动时间、UID与对象绑定见[身份清单](assets/2026-10-30-u05-mouse-dialog-followup/identity-ledger.json)。原生 sampler 仅以只读权限读取所属进程，核对 PID／路径／启动时间／原文件和签名；请求10ms或2ms休眠不代表实际采样分辨率。SDK正常导出仍为目标200ms，短脉冲由另存的原生研究样本证明，**不是 SDK 历史采样**，也不从漏采推断门不存在。

07／09在四端均见根及层 shown/enabled=true、attached=true、order2；开窗 keyboard_lock_level=1、mouse lock=0，取消后键盘恢复0且对应条目移除。创建队伍名保留原版默认值，好友名为空，均取消而未确认。初次探测 P 键实际打开了竞技场不可用通知，该通知已关闭，不算八类覆盖；邮件入口采用社区中的邮件图标。A 的一次城镇导航点被原生路线拒绝，未强制移动。

五张关键原图均为原生截图文件，未改图：

- [A 创建队伍](assets/2026-10-30-u05-mouse-dialog-followup/A-party-create.jpg)
- [B 添加好友](assets/2026-10-30-u05-mouse-dialog-followup/B-friend-add.jpg)
- [C 创建队伍](assets/2026-10-30-u05-mouse-dialog-followup/C-party-create.jpg)
- [D 添加好友](assets/2026-10-30-u05-mouse-dialog-followup/D-friend-add.jpg)
- [D 空收件列表](assets/2026-10-30-u05-mouse-dialog-followup/D-receive-mail.jpg)（首窗截图，不是 deferred=true 的像素证明）

最后16条附加 CLI 因导入路径错误，在执行条件前失败；关闭后用实际保留快照和历史采集时钟补验，明确记为[回放](assets/2026-10-30-u05-mouse-dialog-followup/closed-sdk-cli-replay.json)。不为计数重新启动四端。

## 回归、产物与保护

最终 debug SHA256 **`B4E03F330153AF6D60BB10B7EB61D3D6D3DCD008DCB722226028BB61BC921E69`**，四端均实际加载；实机后未修改产品源码。IJL仍为 `0103C7DCE1FA8AEA5233D7A02C6E1115F83598A8E3A02EF85AC8834F24B2871A`，本轮未改其源码。此前队友／副本 MP 施放矩阵仍属于[前阶段的实际构建](2026-10-30-u05-mp-input-dialog-verification.md)，本轮仅做城镇 MP 基线读取，不将旧矩阵归到 B4。

- 原生新增用例先失败16条，再实现并通过。最终 client-state、navigation、party、真实 PVF town-route及完整 overlay／ABI夹具均退出0。
- Python新增3个方法先 RED 后 GREEN；定向64项／131子测试通过。完整回归 **617项通过、553子测试、0跳过、退出0**，含协议测试。日志仍有既有 Unicorn `mem_map` Windows faulthandler堆栈，测试继续完成，未宣称无诊断输出。
- IJL未变，复用前阶段同哈希的完整兼容回归，不重复其已完成矩阵。托管业务未改，未重复托管全套。
- 独立复核未发现代码或最终身份／计数问题；复核者对网络与恢复只核对摘要，未独立重解原始 netdump 或读取存档。归档脚本另对当前产品源码／二进制、14份恢复文件及8项保护哈希逐项校验。

[网络头审计](assets/2026-10-30-u05-mouse-dialog-followup/outgoing-header-audit.json)从私有通道每条 TX TCP 流的第0字节严格解析10字节明文头，无重同步、截断或残字节；未解密正文。创建队伍12、加／删好友63／64、发信97、提取附件98均为0；邮件查询99为 A1/B1/C1/D3，不将这些头计数说成业务提交证明。私有档4账号的好友／黑名单／现金、7角色的金币及各背包／装备字段均未变；邮件观察、虚弱和最后在线元数据不作不变承诺。

**14个临时替换文件已恢复，8项保护哈希保持**；正常存档 SHA256 仍为 `E0E2FB4BB53B9775463E9F54905CE9CB3426D72A9EB0BDADF845642ABB98A283`。任务客户端、启动器和服务端均已关闭，停服前活动通道会话为0。归档不包含正常存档、私有原始账号档、凭据或加密网络原文。

完整计数见[验证清单](assets/2026-10-30-u05-mouse-dialog-followup/verification.json)。共38个证据文件，[manifest](assets/2026-10-30-u05-mouse-dialog-followup/manifest.json)列出除自身外37项的哈希。剩余范围仍为全局输入阻断、56密码窗实机、其他未覆盖 UI及未证明的技能分支；U07／GP42、U08继续保留。
