# 第二章单人掉落聚拢

## 范围

将第一章普通玩家在单人副本中输入精确文本 `牛来` 的行为移植到第二章。服务端仍只允许存活、非 PvP、有有效运行房间、非队伍实例的本角色请求；只给请求者发 NOTI 12 Type16 标记，不改掉落账本、不发拾取、不生成物品。第二章投影保留标记包原有字节。

第二章实现独立位于 `src/DFLegacy.ChapterTwo.Native/ChapterTwoLootGather.cpp` 和 `ChapterTwoLootGatherHooks.cpp`。第一章代码未修改。聚拢在已有性能帧回调 `DrawPerformance` 开始处消费一次队列；只有性能帧安装成功后宿主才安装聊天入口。没有新增独立帧补丁或测试控制接口。

## 原生证据

只读分析映像：`dist/c2-win-20261111-loopback2/manual-restore/DNFS1C2/DNF-rebuilt-v3-probe.exe`。

SHA256：`9BC32EFADD05A2FBF407C85C73AC66D221C5577E7A2234C981C3BBA0CB9A8BDB`。

ImageBase `0x400000`，SizeOfImage `0xE57000`。逐段原始字节、SHA256、Capstone 反汇编存于 `dist/chapter-two-overnight/loot-gather/native-evidence.txt`，由同目录 `capture-evidence.py` 从该映像生成。

| 用途 | 第二章证据 |
| --- | --- |
| 聊天入口 | `0x9272D0`，9 字节 prologue `55 8B EC 81 EC 00 01 00 00` |
| 参数及返回 | `0x927326` 读取 `[ebp+0xC]` 类型；`0x927381` 用 `movzx` 读取 `[ebp+0x10]` 的 word UID；`0x927358` 为 `ret 0x18` |
| 普通聊天路径 | 回放 prologue 后继续 `0x9272D9`，保留六个参数及 ECX |
| 游戏/副本 UI | `0xD564AC` / `0xD56D1C`；UI `+0x200010` 为 stage，stage `+0xB0` 为 map，与既有隔离测试控制代码相同 |
| 本地副本角色 | `0xD7A8BC`；actor `+0xE0` kind `0x111`、`+0xE8` protected UID、`+0xFC` map |
| 存活判定 | vtable `+0x3D0 = 0x6F4500`；该函数读取 actor `+0x1524` 是否非零，不沿用第一章 protected dead 字段 |
| 队伍成员 | `0xD56D04`；`0x403890` 返回四个 `+0x10+slot*4` 成员指针，只允许唯一成员等于 registry own user；user `+0x168` 为副本 actor |
| 副本玩家身份 | `0xD56CF4` registry `+0x2C` 为 own user，`+0xC/+0x10` 为双链表头/数量，节点 `+8/+12` 为 UID/user；actor protected UID 必须与 protected `0xD7A8E4` 及请求 UID 一致。不读取 user `+8` 作为副本身份 |
| secure 管理器 | `0xF35AF0`、key `0xF35AF4`；两份数组位于管理器 `+0x44/+0x48`；`0xA47740` 扩容函数确认 `+4` 为已分配容量 |
| 地面类型 | RTTI `CNRDItemObject`；vtable `0xAEC474`；构造器 `0x435611` 赋该表并调用 `0x483F30` |
| 地面字段 | `0x483F30` 设 kind `+0xE0=0x121`，descriptor `+0x840`，pending `+0x845`；`0x4EE575` 明确跳过 pending 对象 |
| 活跃标志 | vtable `+0x64=0x406AE0` 返回 `+0x160`；不是第一章的 `+0x128` |
| 原生坐标 | vtable `+0x84=0x487050`、`+0x90=0x487070`，通过 actor `+0xF4` 控制器跳转至 `0x4F2FD0` / `0x4F2E70` |
| 写入语义 | `0x4F2FD0` 设置 dirty `+0x8C`，把整数转 float 后调用真实 vector setter，`ret 8` |
| vector 类型 | plain getter/setter `0x489BE0/0x489BF0`；protected `0x4F4210/0x4F4230` |

## 守卫与生命周期

收到精确 Type16 标记并确认 self UID 后，记录当前 game/stage/map/actor/user 身份及线程、时间。UID 截成低 16 位，与原始 native 读取方式一致。原标记必须以 NUL 终止；普通 Type16 聊天、其他类型、其他 UID 都走原入口。

初版移植曾沿用 user `+8` 值作为身份。准备私有真机宿主时，重新核对既有 `test_control/README.md` 与原始映像夹具，发现其已经记录过第二章副本该字段为零的真实拒绝；现在使用同一套有界 registry 绑定与双 protected UID 校验。`c2-identity-red.log` 保留零字段/正确 registry 的失败复现，`c2-identity-green.log` 保留修正后的通过；负例包含 own user 错配、节点 key/value 错配、前后向链接损坏、循环、数量越界、全局 UID 错配及重复 UID。

下一帧只能在同一线程、1500ms 内执行。重复校验副本、成员唯一性、存活和全部身份；只枚举当前 map `+0x10/+0x14` actor 列表（上限 4096），拒绝另一个玩家、未知地面布局、重复物品 UID、非法列表及无法验证的坐标。每次 setter 前重新比对全列表和本物品身份，避免上一个 native 调用改变对象集合。只移动活跃且非 pending 的地面 actor，X/Y 取玩家当前坐标，保留 Z。

只调用 native setter 和 getter，不直接写 protected 浮点数。日志使用 `OutputDebugStringA` 的 `DFLegacy Chapter Two: solo loot` 前缀，由 broker 收集；游戏无需客户端目录写权限。日志每进程上限 128 条，空闲帧不写日志。

## 验证

- `dist/chapter-two-overnight/loot-gather/core-test.cmd`：首轮 RED 为 `exact Type16 marker must be recognized`；实现后 PASS。覆盖单人、存活、切图、普通聊天、非活跃/pending、重复 ID、列表上限、镜像损坏、重复新掉落、每个 setter 后身份变化和非坐标字段不变。
- `dist/chapter-two-overnight/loot-gather/hook-test.cmd`：真实 x86 naked shim PASS，验证 ECX/六参数保留、低 16 位 UID、消费/透传、一次性队列和过期丢弃。只将测试中的原始 continuation 重定向至合成函数。
- 全量 Release 构建首次运行揭示运行时低地址 `VirtualAlloc` 与 CRT 堆保留段冲突；夹具已改为链接进测试映像的 `.gameres` 可写不可执行静态段，在写入前检查地址属于预留数组，不再运行时抢占地址或重试。使用现成 CMake Release 构建 `chapter_two_loot_gather_hook_tests` 并执行 PASS；证据为 `hook-static-reserve-build.log`、`hook-static-reserve.log` 和 `hook-static-reserve-layout.log`。
- `dist/chapter-two-overnight/loot-gather/image-test.cmd`：真实第二章 PE 安装测试 0 failures，覆盖原始映像、损坏签名、参数错误、重复安装、写失败及半写失败回滚。
- `dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -- --loot-gather`：0 failures，包括第二章投影后的逐字节标记包验证。

上述项目是离线测试。真实 getter/setter 的移动与后续拾取效果另外通过下述隔离真机验证，不将组队、死亡等离线负例计作真机覆盖。

## 2026-11-12 隔离真机证据

独立审核记录为 `dist/c2-parity-20261112/live-evidence/loot-live-verification.md` 与同名 `.json`。JSON 包含按原始快照重新计算的检查结果、native 日志事件和来源 SHA256；可运行同目录 `verify_loot_live.py` 重算。

普通生产宿主 SHA256 `3C107DA70E48B394026536244C5FEFCC20D995DA7EA7E21FADD5E96DD5648711` 已经由隔离启动器进入赛丽亚房间，城镇 `牛来` 保持普通聊天显示，见 `03-production-town.png`、`04-city-command.png`。

副本掉落实测使用私有 test-control 宿主 SHA256 `FA38249ED3F2198DDBEFE958D48F0B69BF04CF32C77697AAD92D36EEBD61C3BD`，PID `53244`，run `dist/c2-parity-20261112/live-control/runs/20261112-120934-097-91bee44b`。它链接当前生产源码及全部生产补丁；入口与生产入口仅相差 `Policy.h` 和 `test_control::Install`。私有辅助用于走路、调整角色位置及对当前活怪执行原生击杀；没有生成/领取物品的辅助操作。聚拢命令与 X 拾取均由真实 UI 输入。该私有测试接口没有加入产品构建。

| 实测 | 原始证据及结果 |
| --- | --- |
| 第一房物品 | `loot-before/after/picked.json`：同一 UID `1`、actor、descriptor 从 `(413,313,0)` 移到 `(700,240,0)`，native `moved=1`；聚拢前后库存/金币不变；X 后掉落消失，存档仅新增 slot `10` 的 Item `18046`（纱绒短靴）×`1`，金币仍为 `2061`。截图 `07`–`09`。 |
| 换房后重复聚拢金币 | `repeat-before/after/picked.json`：同一 UID `2,3` 及其 actor/descriptor 从 `(426,199,0)`、`(593,296,0)` 移到 `(850,240,0)`，native `moved=2`；聚拢没有改变库存/金币；X 后两掉落消失，UI 显示获得 `20` 和 `18` 金币，存档 `2061→2099`，库存不变。截图 `10`–`12`。 |
| 空地重复命令 | `empty-regather.json`：地面 `0→0`，native `queued→gathered moved=0`；`character-empty-regather.json` 库存、金币均与已拾取状态一致。截图 `13-empty-regather.png`。 |

`loot-capture-guard-before.json` 记录真实 registry 仅有自身、party 槽 `[own,0,0,0]`，全部 Capture 守卫满足。地面采样在读取前后重核身份和完整 actor 集合，保留 active/pending、XYZ 与原始 descriptor；物品 ID/数量及金币结果来自独立角色存档，未声称采集了完整服务端地面账本。最终私有 `native-14.log` 为 `EXIT 00000000`，run 的 `result.json` 为 `completed` / `exit_code=0`。

本轮没有真机覆盖组队、死亡、PvP 或切房瞬间旧队列拒绝；这些仍仅有离线证据。也不将私有宿主的副本实测写成普通生产宿主的副本操作记录。

## 隔离真机操作

1. 使用包含本变更的第二章隔离服务端及本地补丁宿主，确认 native 日志有 `solo loot installed-9272D0`。
2. 普通账号单人进副本，打出金币和物品，离开掉落位置；正常聊天输入 `牛来`。
3. native 日志应顺序出现 `solo loot queued` 与 `solo loot gathered ... moved=N`，画面中可见掉落移动至角色脚边；正常拾取应生效且不重复生成。
4. 再打出新掉落，重复命令；检查重复指令、已通关 Boss 房、普通聊天透传。
5. 城镇、死亡、组队/PvP 条件不得触发聚拢；换房后旧队列不得移动新房间对象。真机证据应另记录实际日志、截图和使用的宿主 SHA256。
