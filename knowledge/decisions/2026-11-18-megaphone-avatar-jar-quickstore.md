# 喇叭/装扮销毁/袖珍罐负重/仓库数量弹窗（2026-11-18）

用户报告的四个第二章真机问题与本轮处理。源码基线 `13f45be`，所有服务端改动已构建（Release 0 错误），
原生快速仓库改动已在本机用 NMake + 原镜像跑通 126 项检查。

## 1. 频道/服务器喇叭无效

### 结论（有官方证据）

- **type 11（0x0B）和 12（0x0C）= 服务器喇叭（道具 36）**；**type 13（0x0D）= 频道喇叭（道具 37）**。
  当前服务端把 11/12/13 都当成 “Unsupported SEND_MESSAGE type” 丢弃，所以两种喇叭都无效。
- 客户端只在 **CMD 17 SEND_MESSAGE** 里带上道具槽位，**不另发 CMD 47**；服务端必须在 CMD 17 内扣 1 个道具。
- 原版回包是 **NOTI 118 `MEGAPHONE_MSG`**（不是 NOTI 12），payload：
  `type:u8, channel:u8, uid:u16, nameLen:u32 + name, textLen:u32 + text`。服务器喇叭经 monitor server
  全服扇出，频道喇叭只在本 game server（同频道）`send_all`。
- 证据：官方服务端 `DisPatcher_SendMess` @0x081f3540（type 11/12 分支要求 item 36；type 13 分支要求
  item 37）与真实客户端抓包（C2S type 11 → S2C NOTI 118）；抓包文件在工作区外
  `G:\Work\Server\dnf\dnf-re\temp\capture\relay-container\relay-container-20260912-181445-6216-packets.tsv`，
  本仓库没有副本，属外部只读证据。

### 实现

| 位置 | 改动 |
| --- | --- |
| [GameProtocolEngine.cs](../../src/DFLegacy.Protocol/GameProtocolEngine.cs) | `MegaphoneMessageNotification = 118`、`CreateMegaphoneNotification` |
| [EntranceService.cs](../../src/DFLegacy.Server/EntranceService.cs) | CMD 17 新增 type 11/12/13：校验 itemSpace/slot/道具，扣 1 并持久化，回 NOTI 118；失败回 CMD 17 error 17；成功后补发权威主背包 |
| [CharacterSessionRegistry.cs](../../src/DFLegacy.Server/CharacterSessionRegistry.cs) | 会话租约记录频道号；`NotifyPacketAll`（全服）与 `NotifyPacketChannel`（同频道） |
| 解析器 | type 11/12/13 允许第二章 16 字节零尾垫（与 2/3 同口径） |

测试：`--megaphone-protocol`（NOTI 118 字节与抓包逐字节一致）、`--megaphone`（真实 CMD 17：扣 1、
广播类型/频道/名字/正文、错误道具回 error 17 且不扣）。均 0 失败。

**未验证**：目标 1.0.13 客户端是否也发 type 11（抓包来自 2022 官服客户端）；频道喇叭的跨频道隔离只有
单进程同频道测试；真机双客户端仍需点一次。

## 2. 装扮无法销毁

装扮栏是 item space 1（穿戴是 space 3）。CMD 18 原来只接受 space 0，space 1 在守卫处被直接 `continue`
且**不回包**，客户端表现为“点确认没反应”。现在：

- space 1 与 space 0 走同一条销毁校验/持久化路径，回复 listType=1，并补发一次 list 1 快照；
- 穿戴中的 space 3 仍要求先脱下（不直接销毁）。

测试：`--avatar-destroy`（真实 CMD 18：删 bag 装扮、保留其他槽、主背包/装备不变、错误 id 拒绝）。0 失败。

## 3. 袖珍罐“负重还有一半却超重”

静态核对（本轮用真实 PVF 7110 全表复算）：奖池解析、count 列、单位换算、`TryPlace` 的主背包容量口径
都正确；7110 单次奖励最大 400（0.4 kg），**不可能因为奖励本身超重**。最可能是服务端会话背包与客户端
负重条失同步（先例：2026-11-17 任务道具发放后会话背包没换新），或非堆叠记录的 CountOrValue 被当成
件数放大。处理：

1. 成功的袖珍罐开启后**无条件补发权威主背包快照**（原来只在仓库扩容时发）；
2. `CalculateWeight` 对非堆叠物品固定按 1 件计重，防止迁移/旧记录把模板重量乘以陈旧计数；
3. 拒绝日志补 `main/mainAllowance/carried/carriedLimit/level/job/heaviest`，下次复现能直接区分原因。

测试：`--weight-accounting`（非堆叠计数不放大、堆叠计数仍生效、allowance 边界）。0 失败。
**未验证**：用户原始角色/存档上的真机复现；如果仍失败，日志会指出是上限口径（宠物/换装）还是别的。

## 4. 右键单个物品入仓库仍弹数量框

原生 `ChapterTwoQuickStore` 原来对所有右键都打开原生数量框（含 count=1 的装备/单件），只有
Shift+右键才直接移动。现在 **count ≤ 1 直接走原生移动**（与 Shift 同路径，仍先做原生负重检查），
只有 count > 1 才打开数量框。

测试：`chapter_two_quick_store_tests`（原镜像）126 项 0 失败；本机 NMake 构建（CMake 4.2.1 便携版 +
vcvarsall x86 + TMP 指向工作区，见 `.tmp/tools/build_quickstore.bat`）。

**未验证**：重新构建第二章宿主并装到客户端后的真机右键。宿主已在本机构建成功（见下节），但启动配对受阻。

## 5. 宿主重建与部署受阻（2026-11-18 17:40）

本机补齐了原生构建链（便携 CMake 4.2.1 + VS 2026 x86，`.tmp/tools` 未提交），并完成：

| 产物 | 说明 |
| --- | --- |
| `.tmp/host-build-quickstore/build/Release/chapter_two_presentation_host.exe` | 含快速仓库修复，SHA256 `B816BC0A…`；`Build-PresentationHost.ps1` 全量原生测试 0 失败（快速仓库 126 项） |
| `.tmp/launcher-quickstore/…LocalTest…zip` | 本机测试启动器，嵌入上述宿主（`DFLegacy.Chapter2.Launcher.exe` SHA256 `1E9F43A3…`） |
| `.tmp/launcher-quickstore-normal/…zip` | 普通启动器，同一宿主（SHA256 `A8EB6D68…`） |

部署测试（客户端副本 `G:\tmp\dfo-v1014-quickstore`）失败在**启动配对**，不是宿主逻辑：

- `Build-PresentationHost.ps1` 会把宿主源码里的 `confined()`（要求 capability-free AppContainer）替换成
  `ordinaryDesktopProcess()`（要求非 AppContainer）；
- 启动器实际用 `dnf_startup_trace.exe` 拉起宿主，trace 创建了 AppContainer，于是宿主打印
  `REFUSED: this Chapter Two host requires an ordinary desktop process` 并以 2 退出；
- 反过来若换回要求 AppContainer 的旧宿主，又与 trace 的模式/清单不匹配。

结论：不能用 `Build-PresentationHost.ps1` 直接替换现有 V4 客户端的宿主/启动器。需要走完整的启动包/更新迁移
流程（`Build-StartupBroker.ps1` + 启动器发布 + 已校验安装清单），或找到与 `dnf_startup_trace` 模式匹配的
宿主构建开关。当前测试环境已恢复：服务端回到 `127.0.0.1`，旧宿主客户端重新登录，服务端四项修复生效，
原生快速仓库修复**尚未部署到客户端**。

## 6. F11 面板（未开始实现）


已确认 F11 是启动器进程的 WPF `第二章 · 物品工具`，与 Ctrl+F10 的原生性能面板不同进程。加“右键小地图传送/
无敌”需要新增启动器→宿主控制通道，并把 `test_control` 的位置写入/无敌移植进生产宿主（仅调试宿主）。
本轮没有改面板代码；原生构建链已打通（CMake/NMake），下一波可做。

调试面板 GM 账号：`localqa1`（本机 GM 口 48095 的 bootstrap admin）。密码属于本机测试配置，不写进仓库。
