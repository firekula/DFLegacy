# S2 Tier 2 / Tier 3 验收 runbook（2026-10-23 实测通过）

> 覆盖 S2 计划 Task 11 Step 1–3。工具与驱动脚本位于
> `.superpowers/sdd/2026-10-19-s2-multiprocess-split-first-slice/work/`（tier2_compare.py /
> tier3_dual_drive.py / server_mode.py / seed_same_area.py / focus.ps1），B′ 侧的
> capture.ps1 / auto_input 驱动在 `.superpowers/sdd/2026-10-22-client-auto-input/work/`。

## 0. 前置

1. **同版本部署**（版本混搭拓扑是 S2 复审 F-3 点名过的风险）：把当前构建的
   `DFLegacy.Server.exe/.dll`、`DFLegacy.Protocol.dll`、`DFLegacy.InterProcess.dll`、
   `DFLegacy.RandomNative.dll`、`DFLegacy.Channel.exe/.dll` + runtime/deps json 拷进
   `DF\town-test\`（**排除** server.json / data / wwwroot / 客户端文件）。
2. **ijl15.dll 两处**：`DF\ijl15.dll` 与 `DF\town-test\ijl15.dll`（launcher 每次启动从自身
   目录安装替换模块，只换 DF\ 会被旧版覆盖）。
3. 客户端 `DF\Config.ini`：`EnableAutoInput=1`、`DumpNetwork=1`、`ScaleFilter=linear`
   （native 会整体跳过 scaler 链）。
4. 端口空闲：TCP/UDP 2311、TCP 7001/7004/8081。
5. **存档夹具**：`seed_same_area.py` 把所有角色种到 town 1/area 0（登录仍从 1/1 出生，
   种存档是为一致性）；跑前备份 `data\state.json`，跑完恢复。

## 1. Tier 2：单体 vs 拆分 帧序列对比（Step 1/2）

```powershell
cd G:\Game\DNF_ACT1\DFLegacy
# 单体档：town-test/server.json 无 Processes 段（原始配置）
python .superpowers\sdd\2026-10-22-client-auto-input\work\t3_drive.py     # handshake-login-town 单 actor
# 拆分档：
python .superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work\server_mode.py split
python .superpowers\sdd\2026-10-22-client-auto-input\work\t3_drive.py
python .superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work\tier2_compare.py runs\<mono-run> runs\<split-run>
```

**对比口径（D5-RAW-COMPARE 收口，2026-10-22 刷新裁定 1 的落地）**：
- **服务端明文帧**（服务端即解密者，其日志 RX/TX 行的 `type/protocol/length/body` 是解密
  后视图）：按会话 guid 分组、剔除探测会话（orchestrate 的 wait_until_listening 在拆分档
  会开一条只收 greeting 的 TCP 探测连接；单体档的等待只查端口不建连）后逐帧比：
  - 逐字节比 body，**时间戳例外**：同一帧内 ≤1 个连续 4 字节段不同（protocol 1 频道列表
    的帧内墙钟）→ 记为 exception，仍 PASS；
  - **结构级协议集 {161, 131, 132}** 只比 (direction, type, protocol)：161 载荷密封于随机
    会话密钥（D5-RAW-COMPARE 的固有限制）；131/132 附近聊天内容取决于 run 私有存档的
    离线聊天回放，与拓扑无关。
- **客户端原始帧**（netdump）：随机密钥使密文不可比，只比聚合结构（RX/TX 行数、字节总量，
  容差 ±3 行/±96 字节——归因是 **TCP 256 字节分片的时序抖动**；拆分档的探测连接是
  编排侧↔服务端、不经过客户端，不贡献客户端行数差异）。

**2026-10-23 结果**：单体 `runs/20261023T003949Z-85eba2d-handshake-login-town` vs
拆分 `runs/20261023T004125Z-85eba2d-handshake-login-town` → **42 帧逐字节一致**
（时间戳例外 1 处：protocol 1 @hex 58..61），客户端结构等价 → **Tier 2 PASS**。
（记录：`work/tier2_compare.py` 的 exit 0 输出即判定证据。）

## 2. Tier 3：两档双客户端进城 + 同屏（Step 3）

```powershell
# 夹具：出生房 1/1 临时共享（服务端代码默认已修，见 §3），两档同用
python .superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work\server_mode.py split   # 或 mono 后把 PrivateAreas 置 []
python -c "import json;p=r'G:\Game\DNF_ACT1\DF\town-test\server.json';d=json.load(open(p,encoding='utf-8'));d['DFLegacy']['TownSync']['PrivateAreas']=[];open(p,'w',encoding='utf-8').write(json.dumps(d,ensure_ascii=False,indent=2))"
python .superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work\tier3_dual_drive.py
```

**焦点机制（本波的关键发明，全自动）**：
- 客户端帧泵（`HookedGetAsyncKeyState` 锚）只在「本进程拥有前台窗口」时运行（`Pump` 顶部
  焦点门，commit 506749f）——游戏只在获得焦点时轮询键盘，失焦客户端既不消费共享计划文件
  也不发沿，双客户端共享一个 exe 目录因此是安全的；
- 切焦点用 **真实鼠标点击**（focus.ps1：目标窗口置顶不激活 → SetCursorPos 到窗口中心 →
  mouse_event down/up）：DNF 的 DirectInput 只在收到**真实输入事件**后才恢复轮询，纯
  SetForegroundWindow 只改焦点状态、无输入事件，游戏自己的激活判定不认（实测：确认前台
  仍不执行计划；真实点击后立即执行）。点击本身被游戏忽略（Spike 1），只有焦点副作用生效；
- 驱动在计划执行期间每 10 秒未完成即重新点击聚焦（焦点可能被抢回）。

**判定锚（服务端日志，无视觉依赖）**：
`TownSessionDirectory ... entered 1/1 (instance 0); instanceMembers=2, foreignAppearances=1`
——同 instance、成员数 2、第二个客户端进区时收到第一个成员的外观（同屏的服务器侧证据；
[语义]"人工确认同屏"仍按场景设计留 needs-human）。

**2026-10-23 结果**：
- 拆分档 `runs/20261023T021722Z-85eba2d-town-two-clients`：instance 0 / members=2 /
  foreignAppearances=1 → verdict green-with-human（pass 3 / needs-human 1 / fail 0）；
- 单体档 `runs/20261023T022135Z-85eba2d-town-two-clients`：同锚全达成，verdict 同 → **Tier 3 PASS**。

## 3. 本波代码修复（真机暴露，均已提交）

- **`ServerOptions.PrivateAreas` 默认改空数组**（a183f88）：`ConfigurationBinder.Get<T>`
  对已初始化的数组属性是**追加**不是替换——原默认 `["1:1"]` 使配置无论写 `[]` 还是别的
  都永远保留 1/1 私密（真机实测 + bindtest 复现 `bound: [1:1, 99:99]`）。改空默认后，
  所有部署的 server.json 显式 `["1:1"]` 行为不变，测试夹具可显式清空。冒烟夹具
  `CheckPrivateAreasAsync` 相应显式声明 `["1:1"]`。
- **帧泵焦点门 + `.done` 改名良性竞态**（506749f）：见 §2 焦点机制；双客户端同目录下先完成者
  改名成功、后完成者发现源文件已被取走 → 视为干净（否则终审闩会误抑制第二个客户端）。
- **提交卫生（复审 Critical 修复记录）**：初审提交（d3bc82e，已重做、现为死哈希，仅历史记录）曾把另一工作流的未提交
  ReadPeerTable 大端读法 hunk 一起 pathspec 提交（该文件的既有 ` M` 状态未走 stash-guard），
  造成提交树冒烟红。已软重置拆解重做：a183f88 / 506749f 只含本波改动；该端序 hunk 以
  patch 形式存于 `work/their-readpeertable.patch.txt` 并还原回工作区（属于其所属工作流，
  须与其未提交的 writer 大端修正一并提交）。
- **双客户端 `.done` 终态**：后完成者在自己的 rename 前会先 `DeleteFileA(g_donePath)`，
  若先完成者已建好 `.done` 且后完成者的 rename 因源消失而落空，run 结束可能**没有**
  `.done` 标记。驱动以计划文件消失为完成信号（实测可靠）；任何以后以 `.done` 存在为
  信号的工具必须知道这一终态。
- **测试层缺口（如实）**：帧泵焦点门无自动化覆盖（OS 交互门难单测，仅真机证据）；
  走路键注入未生效（见 §4.3）。

## 4. 已知坑位 / 观察（如实记录）

1. **拆分档 channel 链路在 run 尾段偶发 down**（10:00:40 起的 ChannelLinkService
   "link failed" 警告，此时两客户端已进镇、验收不受影响）——channel 进程存活时长与
   game 的链路保活需要后续观察，不影响本任务结论。
2. **`dotnet build DFLegacy.Emulator.slnx` 有 4 个预存环境错误**：3 个 vcxproj 的
   MSB4278（dotnet 自带 MSBuild 无 C++ targets——原生门用 MSBuild.exe 跑，本波
   零警告零错误 exit 0）+ 1 个 Launcher.csproj 后构建 MSB3073（与本次改动无关的
   post-build 脚本失败）。C# 侧 `dotnet build src\DFLegacy.Server` 0 错误、
   smoke-tests exit 0。
3. **走路键未带动角色出房间**（VK 37/39 按住 4 秒无位置转发）——可能被进城公告对话框
   吞掉或房间门需要交互；本波以共享出生房夹具绕开，走路注入的正确性留给需要它的场景
   单独验证。
4. 两客户端屏幕位置重叠：点击聚焦依赖置顶 z-order 两步法（topmost→notopmost）确保点击
   落在目标窗口上。

## 5. 回归（Step 4）

| 命令 | 结果 |
|---|---|
| MSBuild Tests.vcxproj Release|Win32（原生门） | 零警告零错误，exit 0（本波两次） |
| `dotnet run --project tests/DFLegacy.SmokeTests -c Release` | **干净 worktree 复跑 All passed，exit 0**（§7 证据；脏工作区含他人未提交端序 hunk，不在本记录口径内） |
| `python -m protocol.s1 verify` | PASS (5/5) |
| `python -m pytest acceptance/tests -q` | 126 passed |
| `dotnet build DFLegacy.Emulator.slnx` | 4 个预存环境错误（见 §4.2，非本波引入） |

## 6. 恢复现场（验完必做）

```powershell
python .superpowers\sdd\2026-10-19-s2-multiprocess-split-first-slice\work\server_mode.py mono
# server.json 需回到「无 Processes 段」的原始形态（本波实测按备份恢复：删除 Processes 键）
# data\state.json 从 pre-s2t11 备份恢复；DF\Config.ini 的 EnableAutoInput 按需保留
```

## 7. 提交树干净冒烟复跑（复审 Critical 修复证据）

```powershell
git worktree add ..\dfl-s2t11-clean HEAD
cd ..\dfl-s2t11-clean
dotnet run --project tests/DFLegacy.SmokeTests -c Release    # 期望 All passed, exit 0
cd ..; git worktree remove dfl-s2t11-clean
```

本波实录（2026-10-23）：干净 worktree（HEAD=本波提交）冒烟 **All passed / exit 0**。
