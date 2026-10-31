# U05 队友／城镇 MP、键盘门与弹窗层级

2026-10-30。用户要求保留 U07／U08，本轮在 `D:\codex_worktree\party-chat-gold\DFLegacy` 补 U05 数据及 CLI。新增只读观测，没有修改服务端业务、正常存档或新增原生 hook。正常目录已恢复，产物留在 worktree。

## 结果与支持范围

| 项目 | 本轮结果 | 边界 |
|---|---|---|
| 队友／城镇 MP | `mp/max_mp` 新增 `mp_source={actor_token,local}`；本机与队友、城镇与副本均可观测。四端实际施放 Z 后，16 组 UID×接收端均观察到消耗后回升，包含 12 条队友路径 | 队友值是接收端原生内存观测，来源明确；不等于服务端权威 MP，也不承诺网络时序完全相同 |
| 键盘输入门 | `ui.command_input` 导出原命令检查器 type0 的 `keyboard_lock_level/keyboard_suppressed`，附本机 UID、actor、checker 和固定 scope | 锁级 0 仅说明此早期门开放；其他场景／角色／焦点／鼠标门不在该谓词中。全局 `ui.input_blocked` 仍为 null |
| 弹窗状态 | 五类原生活动窗新增 `placement`：层 token、附着、正向层序及层 shown/enabled 原值。四端 class0x25 实测打开、附着和应答后移除 | 当前覆盖十六进制 23/25/30/59/6A；本轮实机仅0x25，其他类由既有证据与原生夹具覆盖。不是全量 UI、像素可见或全局阻断 |
| CLI | 摘要包含本机／队友 MP 与来源；既有 `get_known_field/--wait` 可查询新增标量。四端8个 MP／键盘条件 matched；全局未知条件正确超时 | 旧 v2 缺新增字段仍未知；不把 null 当0或false |
| 文本输入 | 修复 JSON `\r/\n` 无法进入既有 Return/WM_CHAR 路径的问题；先 RED 后 GREEN | 原始控制字节及其他未支持转义继续拒绝。`\r\n` 是两个 Return 槽，单次激活用 `\r` |

## 原生与官服依据

原 ACT1 EXE SHA256 `6B4850BEAF67EC61ADB04AE17F119F16278DD05E97B8FFCA871A9BDB547DE0D5`。官服 ELF SHA256 `8859C99D8290DC5FB58151444138CF2697B2BFFF12D47D6159596A03F20B5261`；保留技能分派／状态检查的原始范围。客户端布局、键盘和弹窗依据该 ACT1 原文件，官服 ELF 的业务检查不证明客户端内存地址或 UI 行为。

- 原 UI `7C86BE→user+110→panel+34`，`7C8E1B/27` 调用 max/current MP getter。城镇的 `user+10C` 是展示对象，MP 另取经 UID、getter 和 user 绑定校验的 `+110` 战斗统计对象。
- `+CC=0x111` 的共同构造链覆盖原版五个玩家族；`+D0` 是独立登记类别，构造为0，`4C7AE0` 挂图写111。城镇／selector 只限定支持0或111，副本仍要求111；不以城镇的0否定正确统计布局。
- MP getter、secure 双镜像、float32基础值再乘截断沿用原合同。坏镜像、负数、非有限、溢出和身份变化保持未知；当前值不强制压到最大值。未新增对所有运行时 x87 边界的证明。
- `493770` 从本机 `+2384` 取 checker，零时返回静态对象地址 `B5DE68`；`8BA7F0` 在键盘 type0 锁级非零时跳过物理键读取。`8BA030` 是优先级锁，非引用计数；聊天和原生 waiting 的设置／释放路径已保留。
- 原 `799EF0` 将当前五类窗根附到 manager+D0 层。读取高层 `9A1608` 的 sentinel 双链表，最多128节点，核对 head/count、前后链、重复节点／根和两遍一致性；失败仅令 placement 未知。活动窗最多64个，继续排除关闭队列和缓存池。

[原始字节、反汇编及合同](assets/2026-10-30-u05-mp-input-dialog/static/)分开保存。旧 `review-contract.json` 的 D0=111 是第一版保守门；补充 `kind-category-contract.json` 明确修正城镇边界，旧文件哈希未改。

## 四端证据

使用已关闭测试阶段的私有档克隆，关闭不相关 profiling/probe，普通操作经原 IJL 文件接口及 debug CLI。没有重做已关闭的邀请、击杀、掉落、技能和同步验收矩阵；组队、入图、清出安全房间仅作本轮数据验收准备。

1. 四端聊天门均为 `0→1→1→0→0`。聊天期间短按向右方向键，位移为0，释放后向右移动约55–56px；其他三端保持锁级0。没有发送聊天文字。
2. MP 窗口保留231条唯一 `(role,sequence)` 数值投影、924个 UID-MP 观察。四端基线为 A2101/B200/C270/D300；最低值范围为 A2095、B194–195、C264、D296，全部接收端见消耗后回升。窗口尾部分值未回满，不能写成全部回满。
3. 投影经严格消费者读取后保存；每端前后完整快照 actor 绑定稳定，924个来源与端点绑定一致。没有把231条投影说成逐帧完整内存转储。该窗口采样耗时43–186μs仅描述此样本，不关闭 U08。
4. B/C/D 在组队准备时观测 class0x25。A 的原版回城和退出菜单直接执行，未出现预设确认窗；A 恢复后补原生组队应答窗，四端均见 attached/order0/根及层标志，实际应答后移除。
5. 回城后四端 MP 均有绑定来源；A 新身份段的非满 `777/2101` 与 B 接收端 UID1 的777一致。A 的聊天／施放为 PID9440；补弹窗、非满城镇 MP、最终 CLI 为 PID59328，均保留精确启动时间。B/C/D 身份不变，不宣称 A 全流程同 PID。

五张关键原图：

- [A 空聊天与键盘门](assets/2026-10-30-u05-mp-input-dialog/A-chat-lock.jpg)
- [B 原生组队应答窗](assets/2026-10-30-u05-mp-input-dialog/B-peer-prompt.jpg)
- [C 施放后 MP264/270](assets/2026-10-30-u05-mp-input-dialog/C-mp-consumption.jpg)
- [D 回城 MP300/300](assets/2026-10-30-u05-mp-input-dialog/D-town-mp.jpg)
- [A 补齐弹窗验收](assets/2026-10-30-u05-mp-input-dialog/A-peer-prompt-final.jpg)

## 构建、回归和恢复

实机矩阵 debug SHA256 `538EB8C1B0D72C3921B22096328F5D28BC18E7D6D0017990B510F5BB3769F07F`；IJL `0103C7DCE1FA8AEA5233D7A02C6E1115F83598A8E3A02EF85AC8834F24B2871A`。

最终 debug 为 `B2CC1403E76EB8EA5F2C5459AD6F9F21CE3CADE083594AA600327836407FCD68`：收尾只新增非城镇 MP 必须与已采 actorToken 一致的守卫。第3次 peer binding 读发生替换的原生竞争夹具已 RED→GREEN，异常时仅 MP 未知，正常绑定路径不变。独立复核认为无需重复四端矩阵；最终新 hash **未重新实载**，不把旧 hash 的矩阵冒充新二进制实测。

- 完整 Python：613通过／1条件跳过／537子断言／退出0，含协议回归。随后指定既有原生导出夹具，单独补验原跳过项1通过；合计614个不同用例通过。
- 完整 IJL compatibility、debug overlay fixture、client state/navigation/party、真实 PVF town controller、frame/gameplay/native-loop 均退出0。
- Python 日志仍出现既有 Unicorn `mem_map` Windows faulthandler 异常堆栈，测试继续并退出0；原始日志保留，未称为无诊断输出。
- 14个临时替换文件按新鲜备份恢复，8项保护哈希保持；所有本任务客户端／启动器／服务端已关闭。正常存档 SHA256 仍为 `E0E2FB4BB53B9775463E9F54905CE9CB3426D72A9EB0BDADF845642ABB98A283`。日常发布包未覆盖。

见[验证清单](assets/2026-10-30-u05-mp-input-dialog/verification.json)、[文件哈希](assets/2026-10-30-u05-mp-input-dialog/manifest.json)及同目录过滤数据。原生拒绝转义的残留计划仅在所属客户端关闭后按精确哈希隔离；菜单正常退出段、A身份更替与定位失败阶段保留在私有运行目录，无正常档回写。

U07 历史同步／GP42 与 U08 剩余卡顿继续保留。其他原生玩家族、未覆盖 UI、全局输入阻断和未证明的技能分支仍按未知处理。
