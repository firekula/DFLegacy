# 第一章与第二章的适配边界

用途：当需求说“这个功能也做到第二章”“第一章修好了，第二章有没有”“两章共用服务端怎么改”时，先判断改动落在哪一层，再决定哪些能复用、哪些必须按章别重做。本文是跨章移植的检查表，不是“两章已完成对齐”的证明。

> 核对日期：2026-11-17，源码基线 `a53e2584e11daa67a4a74a75c5a3d8a2fd5ea6fc`。本文综合当前源码、手册和既有验收记录；本轮只做文档核对，没有重新构建、启动游戏或执行真机验收。客户端细节见 [client-and-native.md](client-and-native.md)，协议见 [protocol-and-data.md](protocol-and-data.md)，资源见 [resources-and-pvf.md](resources-and-pvf.md)，构建发行见 [build-test-release.md](build-test-release.md)。

## 1. 先判断“适配”发生在哪一层

| 层 | 典型内容 | 当前共享方式 | “适配”真正要做的事 |
| --- | --- | --- | --- |
| 服务端业务与存档 | 登录、选角、城镇、组队、副本、奖励、商店、交易 | 同一套 C# 服务端代码，部分行为按章别条件分支 | 找到显式章别开关；同时核对两章的回包、长度和持久化 |
| 客户端游戏逻辑 | 寻路、显示、输入、战斗补丁、界面修复 | 部分算法和源码可参考，但镜像、对象布局、地址、ABI 不同 | 在目标客户端重新确认地址/锚点、安装表和调用链，不能照抄 |
| 启动、资源与发布 | 启动器、原生宿主/broker、PVF/NPK、更新清单 | 产物和包格式基本不共享 | 按章别重新构建、校验资源身份并走对应发行渠道 |

**最重要的判据：源码合入不等于客户端已接入。** 第一章的 40 项固定地址补丁在 [dnf_patches.cpp](../../src/DFLegacy.Ijl15/src/dnf_patches.cpp) 中由 `Apply()` 安装；第二章不调用这条 `Apply()`，而是由自己的宿主、重建镜像和安装表接入等价功能。反过来，第二章服务端分支通过也不能证明第一章客户端行为。已有逐项审计见 [第一章到第二章的客户端逐项接入核对](../decisions/2026-11-12-chapter-two-client-parity.md)。

## 2. 五维对照总表

| 维度 | 第一章 ACT1 | 第二章 Chapter Two | 不能直接复用的点 |
| --- | --- | --- | --- |
| 协议 | 游戏 TCP 走滚动字节解密；`CHECK_CONNECTION` 有明文特例；服务端包由 `GameProtocolEngine` 构包 | 第二章命令走独立 AES 解码门槛；发送时经接收方章节投影 | 同一 opcode 的正文布局、填充、字段扩展和启用条件都可能不同 |
| 资源 | 客户端原生 `Script.pvf`；服务端另一套 `DNF_SCRIPT_PACK` 容器；`ImagePacks2/4` | 客户端必须是第二章原生 PVF；服务端 PVF 与客户端 PVF 成对语义迁移 | 不能把服务端 PVF 改名给客户端；不能假设两种封装字节相同 |
| 原生地址 | `DFLegacy.Client.dll` / `DFLegacy.Bootstrap32.exe`；固定地址补丁、原字节校验、回滚 | `ChapterTwoPresentation.exe` + `QuietStartupBroker.exe`；外部验收源码和重建镜像、锚点变换 | 不能复制 ACT1 地址或 DLL；对象布局、主控和安装表不同 |
| 构建 | `Ijl15/build-native.ps1` 生成 x86 原生模块；托管登录器单独发布 | `Build-PresentationHost.ps1` / `Build-StartupBroker.ps1` 依赖经哈希锁定的外部输入；宿主内嵌进启动器 | 不能靠 `dotnet build` 生成完整第二章客户端；不能拿 ACT1 产物代替 |
| 发布 | `github/stable`；V4 四变体；`client-assets` | `chapter-two` 渠道；`chapter_package.py`；固定入口迁移序列 | 包格式、渠道、固定入口规则和禁止文件不同 |

## 3. 协议适配

### 3.1 第一章当前路径

客户端命令经 `PacketFrame` 读入后，由 [GameClientPayloadCipher.TryDecode](../../src/DFLegacy.Protocol/GameClientPayloadCipher.cs) 处理滚动字节变换：正文前两个字节保留，后续载荷解密，初始候选为种子集合，以“序列 + 明文正文”的 CRC 收敛。`CHECK_CONNECTION` 有直发明文特例，不推进普通随机状态。解码失败时先查 `RX-ENC`、CRC 和候选数，不要把它当成未知 opcode。

服务端到客户端的包由 [GameProtocolEngine](../../src/DFLegacy.Protocol/GameProtocolEngine.cs) 的 partial 文件按领域构包，再由 [GameServerPacket.Encode](../../src/DFLegacy.Protocol/GameServerPacket.cs) 加 6 字节头和固定字节变换。这条变换与入口 AES、客户端滚动种子都不是同一种算法。

### 3.2 第二章不是全局替换包格式

第二章频道解码在 [EntranceService.TryDecodeChapter2Command](../../src/DFLegacy.Server/EntranceService.cs)：要求命令类型、非零 CRC、两个序列字节之后 16 字节对齐的 AES 正文，正文长度限制为 16～1024；先通过 `CHECK_CONNECTION`，后续才允许常规第二章命令解码。

启用条件也在 `EntranceService.HandleClientAsync`：`ChapterTwoRemote` 可开启第二章要求；本地 trial 还要求特定端口、回环端点和 `DFL_S1C2_TRIAL_CHANNEL_CIPHER` 检查。远程第二章模式未通过连接检查会断开。遇到“第二章命令没有进入分发”，先核对这个门槛，不要只改某个 `TryRead*`。

发送方向由 [SerializedWriteStream](../../src/DFLegacy.Server/SerializedWriteStream.cs) 调用 [ChapterTwoPacketProjection.Apply](../../src/DFLegacy.Server/ChapterTwoPacketProjection.cs)。第二章差异集中在投影和专用构包器，例如物品列表条目扩展、附魔字段、克隆时装外观和金币拾取分支。`GameServerPacket.ChapterTwoInventoryEnchantments`、`ChapterTwoAvatarAppearances` 是投影所需旁路数据，不会自动写进第一章原始 payload。

### 3.3 共享服务端的章别条件

两章共用服务端代码，但“共用”不等于“无条件”。常见显式条件包括：

- 连接阶段：`ChapterTwoRemote`、本地 trial 端口/回环/环境变量、`chapter2CheckConnectionAccepted`。
- 发送阶段：按接收方章节选择投影；第二章允许 AES 填充尾部时使用 `HasCommandBodyLength(..., allowChapterTwoPadding: true)`。
- 玩法阶段：第二章塔、墓地、附魔、公会等专用服务和通知；第一章 PvP、区域频道等独立入口。
- 社交阶段：`TownEntryRequest.IsChapterTwo`、组队/好友通道的章节标志和在线状态格式。

修改共享报文时，必须同时核对第一章原始长度/字段和第二章接收长度/字段，避免给所有玩家附加第二章字段。

### 3.4 协议适配检查表

- 明确方向、协议族、包类型、章节；同一个数字 opcode 在不同章节可能不代表同一语义。
- 检查发送端、接收端、投影、错误分支和回包，不只改一边。
- 检查长度、编码（客户端文本通常 GBK/CP936）、定长字符串的 wire 字节数。
- 第二章读取器允许 AES 填充尾部时，沿用现有 `HasCommandBodyLength` / `allowChapterTwoPadding` 规则，不宽泛放开任意尾随字节。
- 第二章新增字段通过旁路属性进入投影，不直接改写第一章 payload。
- 两章各自跑最小协议回归；第二章可参考 `--chapter-two-inventory`、`--cera-gift-padding`、`--chapter-two-avatar-clone`、`--avatar-lifetime`，具体选择器以 [SmokeTests Program](../../tests/DFLegacy.SmokeTests/Program.cs) 为准。
- 不要把协议密钥、本地凭据或真实账号写进文档和测试夹具。

## 4. 资源适配

### 4.1 两种 PVF 角色

PVF 至少有两种角色：

- **客户端原生 PVF**：交给游戏读取，第二章启动器只接受与当前客户端匹配的原生封装。
- **服务端封装 PVF**：由 `ScriptFileSystem` 读取，可能以 `DNF_SCRIPT_PACK` 开头；不能直接交给第二章游戏。

[ChapterTwoClient.ValidateSource](../../src/DFLegacy.Launcher/ChapterTwoClient.cs) 会读取 `Script.pvf` 前 15 字节，发现 `DNF_SCRIPT_PACK` 就明确拒绝；随后按白名单校验 PVF 摘要，并要求 `ImagePacks2` 存在。把服务端封装直接交给第二章客户端曾导致 `std::bad_alloc`，记录见 [第二章客户端原生 PVF 修复](../../docs/acceptance/2026-11-14-c2-native-pvf-release.md)。

### 4.2 语义迁移不是换文件名

第二章的双封装迁移由 [tools/pvf/chapter-two/port.py](../../tools/pvf/chapter-two/port.py) 完成：从固定的第二章输入和 ACT1 证据摘要生成原生/服务端两份 PVF 及验证报告。它是**指定输入的语义迁移**，不是通用转换器；输入摘要、字段守卫和 `validate_entry_crc=False` 的历史证据包兼容路径都不能当通用放行。

资源适配至少要区分：

- 条目 ID、路径、长度、偏移、密钥和 CRC 是否逐项一致。
- 两种容器是否允许“内容相同、文件头不同”；不能强行要求字节相等。
- 客户端 `ImagePacks2/4` 是否完整，`ImagePacks3` 目录是否保留。
- 服务端 Catalog 按什么虚拟路径读取，修改后是否需要同时更新客户端和白名单。
- 资源修改是否进入现有存档/交易事务，还是只影响显示。

已有记录中的 69,399 条记录逐条核对和“资源主体逐字节一致”是那次迁移的样本，不是对所有资源的通用保证。

### 4.3 资源适配检查表

- 先确认章别、角色（客户端原生还是服务端封装）和目标虚拟路径。
- 记录输入 PVF/NPK 的 SHA256、条目数、来源和工具版本。
- 修改后分别验证：服务端 Catalog 能读、客户端能加载、游戏内显示/购买/使用符合预期。
- 需要图片时定位 NPK、目录槽、IMG 和帧号，不把“文件存在”当作显示正确。
- 不删除摘要/字段守卫来“修通”迁移；缺输入时报告缺失，保持原状。

## 5. 原生地址与补丁适配

### 5.1 第一章：固定地址补丁

第一章的原生模块由 [Ijl15/build-native.ps1](../../src/DFLegacy.Ijl15/build-native.ps1) 构建，输出 `DFLegacy.Client.dll`、`DFLegacy.Bootstrap32.exe`、`debug.dll` 等。游戏补丁集中在 `dnf_patches.cpp` / `dnf_patches.h`，当前核对到 40 个条目，覆盖显示、输入、组队、掉落、日志等。

固定地址改动必须验证：

- 目标进程/镜像是支持的版本，原字节与补丁前一致。
- 禁用或不匹配时零写入，不能把错误版本的进程写坏。
- 完整回滚路径和页保护正确。
- 寄存器、栈、调用约定和原有分支不被破坏。
- 对应原生测试和至少一次真实客户端行为验收。

### 5.2 第二章：外部验收源码与重建镜像

第二章不走第一章的 `Apply()`。完整宿主由 [Build-PresentationHost.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-PresentationHost.ps1) 构建，要求 `AcceptedHostSource`、已验证的 `RebuiltImage` 和新的输出目录；脚本对外部验收源码与镜像固定 SHA256，并按唯一源码锚点做变换。broker 由 [Build-StartupBroker.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-StartupBroker.ps1) 构建，同样要求外部 `AcceptedBrokerSource` 和全新输出目录。

宿主和 broker 的分工、位数与内嵌校验见 [第二章当前普通桌面进程链](client-and-native.md#5-第二章当前普通桌面进程链)。关键边界：

- 呈现宿主是 x86，`QuietStartupBroker.exe` 是 x64；不能按托管程序位数推断。
- 启动器必须同时内嵌 `ChapterTwoPresentation.exe` 和 `QuietStartupBroker.exe`，缺一拒绝启动。
- 补丁修改后要重建宿主并重新嵌入；已在运行的游戏进程不会随源码或磁盘 EXE 更新。
- `test_control/` 是独立的 DO-NOT-SHIP 宿主扩展，不在普通 CMake link 中；不能接进普通启动器或当作正常玩法修复。

### 5.3 原生适配检查表

- 先确认镜像/源码身份（路径、SHA256、版本、架构），再谈地址。
- ACT1 的固定地址结论只适用于那份 ACT1 镜像；不要迁移到第二章。
- 第二章外部输入缺失或哈希不匹配时，报告缺什么，停止该构建步骤；不要删校验或用陌生二进制凑路径。
- 需要真实原镜像执行的验收，不能用合成内存夹具替代。
- 改动调用约定、对象布局或主控归属时，单独做生命周期和多人边界验证。
- 与假人/人偶相关的 ACT1 原生 AI 地址只属于第一章研究，见 [区域频道与假人](social-world-and-bots.md#5-原生-ai-研究到什么程度)。

## 6. 构建适配

### 6.1 第一章

- 原生：`Ijl15/build-native.ps1`，Windows、VS C++、CMake ≥3.24、`-A Win32`、C++17、静态 CRT。
- 托管：登录器、更新器、诊断等 .NET 项目。
- 旧合同资源：`release.py` / `ci.py` 生成 `client-assets`；V2/V3 工具包不是 V4 三程序启动布局。

### 6.2 第二章

- 宿主/broker 依赖经哈希锁定的外部输入，不能仅凭 Git 检出复现。
- 启动器是否内嵌第二章由 [ChapterTwoClient.IsBundled](../../src/DFLegacy.Launcher/ChapterTwoClient.cs) 检查程序集资源决定；构建分支是 [Launcher.csproj](../../src/DFLegacy.Launcher/DFLegacy.Launcher.csproj) 的 `ChapterTwoPayloadDir`。
- 运行时按内嵌宿主、broker、脚本的组合哈希释放到客户端 `.dflegacy/chapter-two/<hash前缀>`；可选 D3D11 bridge 使用自己的哈希目录。
- 独立第二章登录器用 `standalone.res` 和真实文件名 `DFLegacy.Chapter2.Launcher.exe`，不等于公开固定入口三程序整包。

### 6.3 版本与最终文件属性

公开固定入口的三个角色是：

| 角色 | 文件 | 资源 |
| --- | --- | --- |
| 原生固定入口 | `DFLegacy.Launcher.exe` | `Startup` 原生资源 |
| 托管登录界面 | `DFLegacy.Launcher.App.exe` | `app.res` |
| 独立更新器 | `DFLegacy.Updater.exe` | `updater.res` |

三个角色使用同一计划发行号构建；第二章默认发行号读取 [LauncherVersion.txt](../../src/DFLegacy.Launcher/LauncherVersion.txt)（核对时为 `1.0.13`）。登录页只显示发行号，Git SHA 留在构建元数据。最终产物要逐个检查文件说明、原始文件名、产品名、发布者、版权、文件版本、产品版本、SHA256 和签名状态，并从最终隔离安装目录实际启动。

### 6.4 构建适配检查表

- 先确认要交付的是第一章、第二章还是共用服务端；不要一套命令套两章。
- 选择章别专用入口和输出目录，不复用另一章的中间产物。
- 原生模块位数、架构、工具链和 CRT 设置与目标客户端一致。
- 客户端补丁改动后重建宿主/内嵌资源；不要只更新源码。
- 最终包检查内嵌哈希、文件版本和实际启动，不只看 `csproj`。

## 7. 发布适配

### 7.1 渠道与包

- 正式 ACT1 是 `github/stable`；第二章是 `chapter-two` 渠道，并有 GitCode 镜像实现。Gitea/Gitee 测试源必须明确选择，不能把历史示例配置复制进正式玩家包。
- ACT1 使用 V4 四变体（x86/x64 × normal/test）和 `update_v4.py`；第二章完整包与跨章清单由 [chapter_package.py](../../tools/release/chapter_package.py) 构建。
- `chapter_package.build()` 要求已验证的 ACT1 完整桥接 `baseline`，第二章后续版本使用同变体 `previous`。
- 第二章客户端 `Script.pvf` 必须是匹配的原生客户端视图；`ImagePacks2/4` 完整，`ImagePacks3` 目录保留。
- 第二章包中禁止 ACT1 `DFLegacy.Client.dll`、`DFLegacy.Debug.dll`、`debug.dll`；服务端与客户端资源要语义匹配，但不要求字节相同。

### 7.2 固定入口与迁移

固定入口不可直接热更：已安装清单固定其哈希，重新编译、修改版本资源或签名都会改变字节。入口字节变化必须发新完整启动包或走已验证迁移，不能禁用 `RequireStableEntry` 或手工改回执。

第二章已有专门迁移：v1.0.11/sequence 16 → v1.0.12-bridge/17 → v1.0.12/18（minimum 17）。`chapter_package.py` 对 `startup_bridge` 检查版本、序列、架构、类型、提交和其他文件不变；后续受 `previous` 和序列下限约束。`fresh_startup` 是新整包入口，不能绕过现有安装的热更限制。

### 7.3 发布适配检查表

- 先确认目标章别、渠道、架构、flavor、sequence、minimum_sequence 和 previous。
- 提供完整目标文件集合；include 模式写了不等于文件存在。
- 遗漏旧文件必须明确 `delete` 或 `unmanage`；迁移只改支持的配置字段。
- 本地产物、匿名回读和远端发布分开记录；本地 verify 不证明公开用户能下载。
- 没有明确授权不做远端发布；发布失败保留现场，不擅自删旧 Release。

## 8. 一次跨章移植的工作流

1. **写清章别和用户可见结果。** 是 ACT1、第二章，还是共用服务端；要看到什么行为；不能只写“把第一章补丁搬过去”。
2. **定位等价路径。** 从启动链、调用方、实现、测试四处确认目标章是否存在对应入口；找不到就记录缺口，不假设同名文件等价。
3. **判断共享层。** 服务端逻辑是否可以按章别条件复用；若共用，补显式分支并同时核对两章回包/存档。
4. **处理客户端差异。** 重新确认目标镜像/源码哈希、地址或锚点、安装表、对象布局和控制权；不复制另一章的固定地址。
5. **处理资源差异。** 确认客户端原生/服务端封装、条目语义、白名单和 `ImagePacks` 完整性；必要时走 `port.py` 的固定输入迁移。
6. **按章别构建。** 使用对应原生/宿主/启动器构建入口；重建并重新嵌入改动后的产物。
7. **验证。** 先跑离线/夹具，再做目标章真机；同时确认另一章没有被共享改动破坏。记录命令、退出码、产物哈希、参与人数、房间和资源身份。
8. **更新文档。** 把长期结论写进对应手册，把单次验收写进 `knowledge/decisions/`；不要把“源码已合入”写成“两章已完成”。

## 9. 常见错误

| 错误说法/做法 | 为什么不对 | 正确做法 |
| --- | --- | --- |
| “源码合并了，所以第二章也有了” | 第二章不调用第一章的 `Apply()`，客户端接入和验收是另一条链 | 检查第二章安装表、宿主和真实运行 |
| “同名文件/同名函数就是同一实现” | 两章的对象布局、偏移、ABI 和调用约定不同 | 读目标章的实际调用方和测试 |
| “把 ACT1 DLL 放进第二章目录” | 第二章不加载第一章补丁；客户端会拒绝或行为不符 | 按第二章宿主/重建镜像构建 |
| “服务端 PVF 改名就能给客户端” | 客户端只接受原生封装；错误格式会导致加载失败 | 用成对的客户端/服务端 PVF 和语义迁移 |
| “摘要不匹配就删校验” | 删校验会掩盖错误的镜像/资源，制造不可复现产物 | 报告缺失输入，向持有者取得合法副本 |
| “放宽 AES 填充检查” | 会让错误长度/尾随字节通过，污染其他报文 | 沿用 `HasCommandBodyLength` / `allowChapterTwoPadding` 的现有边界 |
| “固定入口直接替换” | 已安装清单固定哈希，热更会破坏迁移 | 发完整启动包或走已验证迁移 |
| “第二章通过就等于第一章通过” | 共享服务端分支可能只对一侧生效，客户端也不同 | 分别记录两章验证结果 |
| “一帧出现/一次返回成功就算完成” | 协议、显示、持久化和多人同步是不同层 | 按证据层级分别验收 |

## 10. 证据与继续阅读

- 逐项客户端差异与历史验收：[第一章到第二章的客户端逐项接入核对](../decisions/2026-11-12-chapter-two-client-parity.md)。
- 跨章身份、配置隔离与回退：[chapter transition](../decisions/2026-11-13-chapter-transition.md)。
- 账号迁移：[第一章账号迁移至第二章](../decisions/2026-11-12-chapter-two-account-migration.md)。
- 第二章当前发行与墓地遗留项：[v1.0.13](../../docs/releases/v1.0.13.md)。
- 第二章原生 README 含较早迁移/待验收描述，不能覆盖后续代码和逐项记录。

本文的“已核对”只表示当前源码和既有记录能支持这些边界，不表示两章功能已全部对齐，也不表示本轮执行过构建或真机验收。遇到新改动，按第 8 节重新走一遍，并把新结果写进对应专题页和 `knowledge/decisions/`。
