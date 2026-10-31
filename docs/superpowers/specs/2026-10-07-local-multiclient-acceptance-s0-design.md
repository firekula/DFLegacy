# 本机多客户端验收平台（S0）设计

日期：2026-10-07 · 工作区：`G:\Game\DNF_ACT1\DFLegacy` · 分支：`main`

> 本文是 **S0 一个子项目**的设计。它服务的总目标是"完整还原官方服务端"，
> 但**总目标不在本文范围内** —— S1–S4 的路线图在 §8，各自独立立项。

## 0. 需求来源与三次修正（留档，因为每次修正都换了方向）

本项目所有者在本轮对话里给了三次关键澄清，**前两次我理解错了，必须留档**：

| # | 我的初始理解 | 所有者的实际意思 | 影响 |
|---|---|---|---|
| 1 | "换台电脑" = 换开发机 ⇒ 要做**可移植的取数流水线** | "换台电脑" = **多台电脑连进来组队**（docker 部署 + 局域网） | 目标从"取数"改为"多客户端联机验收" |
| 2 | 优先级：先解决取数自动化 | **优先级：先在本机把多客户端完整还原跑通，再上局域网** | S0 聚焦本机可复现验收，局域网归 S4 |
| 3 | 目标形态待定 | **本机按官方拆进程**（与现有 spec 的"架构级仿制"一致）；**party15 崩溃不阻塞** | 进程拆分归 S2，S0 的 runner 必须能编排多进程 |

**决定性的一句原话**（本轮）：
> "意思每次更新，在另一台电脑上都需要我自己手动操作和复制日志"

⇒ 痛点不是"取数慢"，而是**验收流程无法自动复现**：每次更新都要人起服务端、人起客户端、人决定日志编号、人把日志搬到一起。

## 1. 现状：工具都在，缺的是编排层（附证据）

**结论：S0 不是从零开始，而是把已经存在的东西串起来。**

| 能力 | 现状 | 证据 |
|---|---|---|
| 客户端自身 socket 打印 | ✅ **已是产品源码**（原生模块 + 原生自测） | `src/DFLegacy.Ijl15/src/net_dump.cpp`(31,384 B)、`net_dump.h`(3,986 B)；`STATUS.md` §3.4 记其用法与判定标准 |
| 启动器 | ✅ 在产品树内 | `DF/town-test/DFLegacy.Launcher.exe`；`STATUS.md` 记其构建命令 |
| 服务端 | ✅ 在产品树内 | `DF/town-test/DFLegacy.Server.exe`；读**同目录** `server.json`（`.recon/onboarding.md` §5b 实测） |
| 诊断探针 | ✅ 都有，但都是散的一次性脚本 | `client-stacks`（recon 已声明工具）、`.tmp/watch-party-hang.ps1`、`.tmp/analyze_hang.py`、`.tmp/scan_exception.py`、`.tmp/check-server-from-client.ps1`、`.tmp/find-client-netdump.ps1` |
| 对照抓包 | ✅ recon 已声明工具 | `ref-proxy`、`capture-analyze`（`.recon/recon.config.yml`） |
| 协议字节级断言 | ✅ 已声明工具 | `smoke-tests` |
| **编排 / 归因 / 判定** | ❌ **不存在** | 见下 |

**"不存在"的直接证据 —— 手工编号这个结构性缺陷**：

```
G:\Game\DNF_ACT1\DF\town-test\  :  server-round6.log  server-round6b.log  server-test.log
                                   server-town.log    server-trade.log    server-party44.log
G:\Game\DNF_ACT1\DFLegacy\.tmp\ :  build1..8.log  mut-a/b.log  mut-cd-a/b.log  mut-te.log
                                   mut-gate.log  mut-keep.log  mut-pr.log  mut-rb.log
                                   server-tail-at-hang-{15844,54944,55684,62384,66280,68360}.txt
                                   watch30..36.txt
```

- 日志名**语义化 + 人编号**（`round6`→`round6b`、`party15`→`party44`）⇒ **下一个编号由人决定**；
- `server-tail-at-hang-<pid>.txt` ×6 ⇒ 每次诊断都要人手工抓一份 tail；
- `.tmp/` 是**产物坟场**（数十个 `*.log` 与补丁/消息文件混放），不是可复现的流程。

⇒ 每次更新都要人重复上述决策。**这就是"没办法自动化"的机制性原因。**

## 2. 目标架构：验收的原子单位是"一次 run"

### 2.1 运行目录（取代语义化手编号）

```
runs/<runId>/                      runId = <UTC时间戳>-<git短哈希>-<场景id>，全自动生成
  manifest.json                    提交哈希 / 靶子配置 / actor 名册 / T0 / 每 actor 时钟偏移
  server/                          服务端日志 + 帧追踪 + 使用的 server.json 副本
  actor-1/                         netdump-*.log + 客户端栈 + 诊断探针产物
  actor-2/  …
  verdict.json                     判定结果（可 diff、可进 git）
```

### 2.2 三条纪律

1. **不追求单一交错日志。** 多进程 / 多机下做不到。改为**每 actor、每进程各写各的文件**，
   `manifest.json` 记 **T0** 与**每个 actor 的时钟偏移**，**join 阶段事后按时间对齐**。
   ⇒ 这条正是 **S4 上局域网要用的机制**，现在定死，以后不改。
2. **actor 是"声明的角色"，不是"必须是真人"。** 每个 actor 声明 `driver`：
   - `real-client` —— 真人点 UI，自动采集（客户端闭源带来的硬限制）
   - `synthetic` —— 用项目既有驱动器直接发包
   - （以后可加 `window-automation`，**不在 S0**）

   **场景层与采集层不区分二者** ⇒ 矩阵可以先跑起来，再逐步把 actor 换成可全自动的形态。
3. **一次 run 一条命令**：`run <scenario> --actors N` → 起服务端 + 起 N 个 actor + 采集 + 出 `verdict.json`。
   人的动作被缩到"按提示点几下 UI"。

### 2.3 组件（各自单一职责、可独立测）

| 组件 | 职责 | 不做 |
|---|---|---|
| `runner` | 建 run 目录、起服务端与 actor、写 manifest、**保证初始状态可复现** | 不解析日志、不判定 |
| `collector` | 把每个源（服务端日志 / netdump / 客户端栈）归到对应 actor 目录，写时钟偏移 | 不判定 |
| `verdict` | 按场景声明的期望做判定，输出 `verdict.json` | 不起进程 |

## 3. 场景格式与判定

### 3.1 场景是声明式数据（可进 git），不是脚本

```yaml
# scenarios/town-two-clients.yaml
id: town-two-clients
goal: 两个客户端各自登录→选角→进城，A 能看到 B
actors:
  - {name: A, driver: real-client}
  - {name: B, driver: real-client}
steps:
  - {actor: A, action: "登录并进到城镇", expect: [c2s:login-ok, s2c:noti1]}
  - {actor: B, action: "同上"}
  - {actor: A, action: "走到 B 附近", expect: "A 收到 B 的 USERINFO 且同屏"}
expect_t0: server-accept
```

**`action` 是意图，`driver` 是实现** ⇒ 同一份场景文件在 `real-client` 与 `synthetic` 下都应可跑。
这是 S0 能同时服务"真人双开验收"与"以后全自动回归"的关键。

### 3.2 期望分三级证据档（复用项目既有 `[真机]/[抓包]/[回放]` 纪律，不另造术语）

| 档 | 含义 | 判定方式 |
|---|---|---|
| `[存在]` | 该帧出现、长度对 | **自动** |
| `[字节]` | 有 golden bytes 可比 | **自动**（只在已有证据的字段上声明） |
| `[语义]` | 断言语义（"A 能看到 B"） | **自动标记为"需人看"**，并**写明升级为自动需要什么证据** |

⇒ `verdict.json` 输出的是"**N 项通过 / M 项需人看 / K 项缺证据**"三分，**不是**一个绿勾。
**S0 不假装万物可自动判定** —— 这是刻意设计，不是未完成。

### 3.3 判定原语：跨侧字节比对

客户端侧 netdump 有 `TX len=N hex=…`，服务端侧有帧追踪。
对同一段流量核对 `len`/`hex` 是否一致，即可区分**客户端直连**与**经服务端/中继转发**。
项目已在 `STATUS.md` §3.4 用过此方法；S0 把它做成**自动断言**。

## 4. 与现有工具的接缝（只编排，不重写）

| 组件 | 处置 |
|---|---|
| `net_dump`（产品源码） | collector 定位每客户端的 `netdump-<pid>.log`，归到 `actor-N/` |
| `DFLegacy.Launcher` | runner 用它起客户端，**不改它** |
| `DFLegacy.Server` | runner 起它；每 run **独立 `server.json`**（注意 `.recon/onboarding.md` §5b：运行实例读同目录那份） |
| `client-stacks` / `analyze_hang.py` / `watch-party-hang.ps1` / `scan_exception.py` | 收编为 verdict 的**可选探针**，按场景需要调用 |
| recon 的 `ref-proxy` / `capture-analyze` / `smoke-tests` | **不并入 S0**；S0 的 `[回放]` 档复用 `smoke-tests` |
| `verdict` | **S0 唯一的新东西**；把 `STATUS.md` §2.6/§2.7 的人工验收清单**翻译**成场景声明 |
| `.recon/` 的 8 个工具 | S0 不碰 |

### 4.1 已核实的两条采集事实（collector 直接依赖）

| 事实 | 值 / 依据 |
|---|---|
| netdump 产物 | **`netdump-<pid>.log`，在客户端自己的目录里，每包一行、立即落盘**（`STATUS.md` L1374/L1424；既有实例 `DF\netdump-53916.log`）⇒ **每个客户端一个文件**，collector 按 pid 归到 `actor-N/` |
| 开关 | 客户端目录 `Config.ini` 的 `DumpNetwork=1` + 带 netdump 的 `ijl15.dll`（`STATUS.md` L1422）；**已存在则不覆盖**（`.recon/onboarding.md` §4）⇒ runner 必须**显式校验**开关，不能假设 |

### 4.2 关键接缝：`state.json` 每 run 隔离

`DF/town-test/data/state.json` 是**活存档**，被所有 run 共用 ⇒ 上次 run 的角色/物品会污染下次 run，
这是"**每次结果不一样**"的隐藏来源。`runner` 必须给每个 run 独立状态（复制 → 跑 → 归档），
否则判定不可复现。

**已核实**：该文件确实存在且是活的 —— `152,435 B`，mtime `2026/10/7 06:00`（本轮实测）。

⚠️ **代价（所有者已确认接受）**：存档不再跨 run 累积 ⇒"接着上次进度玩"的习惯要改。
跨 run 保留进度需要另设"基线存档"机制，**不在 S0**。

### 4.3 服务端配置的真实形态（runner 复制时要小心）

`DF/town-test/server.json` **不是裸值文件**：每个键旁边有 `"//"` 形式的注释字符串
（例如 `"//Host": "2026-10-04 局域网双机测试：…"`），且在**运行目录那份**与**仓库那份**已经漂移
（`.recon/onboarding.md` §5b 实测 `Admin.Host`/`Entrance.Host` 不同）。
⇒ runner 必须**以运行目录那份为模板**复制到 run 目录，且**不要**试图用 JSON 序列化重写它
（会丢掉注释）；用文件复制 + 定向修改，并在 `manifest.json` 里记录"改了什么键、原值、新值"。

## 5. S0 的完成定义（收紧，避免变成做不完的大坑）

### 5.1 文件布局与工具入口（S0 新增的只有这些）

```
tools/acceptance/                     ← S0 新增（产品树内，随 git 走）
  run.ps1                             唯一入口：run <scenario> --actors N
  runner/                             建 run 目录 / 起服务端与 actor / 写 manifest / 状态隔离
  collector/                          归集 netdump / 服务端日志 / 探针产物 + 写时钟偏移
  verdict/                            按场景期望判定 → verdict.json
  scenarios/                          *.yaml 场景声明（见 §3.1）
    town-two-clients.yaml             S0 唯一要求"跑通"的场景
    party-invite.yaml                 声明骨架（只需能被解析 + 正确报"缺实现"）
    town-position-sync.yaml           同上
    trade-open.yaml                   同上
  verdicts/                           ← **进 git**：归一化判定摘要（可 diff）
runs/<runId>/                         ← **不进 git**：运行产物（日志/抓包，可能很大）
```

**为什么 `runs/` 不进 git 而 `verdicts/` 进**：产物可丢，**判定必须留在版本控制里** ——
否则"这次判定变了没有"就又要靠人对比日志，回到 §1 的机制性缺陷。

### 5.2 S0 要求跑通的场景

> **两客户端登录→进城→互相可见**，一次命令跑完，产物落 `runs/<runId>/`，判定输出三分，
> **归一化判定**落 `tools/acceptance/verdicts/town-two-clients.json` 且可 diff。
> 外加 **3 个场景的声明骨架**（组队邀请、同屏位置、交易），骨架只需能被 `runner` 解析并**正确报"缺实现"**。

### 5.3 验收判据

1. 同一条命令连跑两次，**归一化判定一致**（允许 runId/时间戳/pid 不同）；
2. 故意破坏一处（例如改掉某个期望的帧长）⇒ 判定**必须变红**（证明判定不是恒真）；
3. 产物目录自解释：只看 `runs/<runId>/` 能回答"这次跑的是什么、谁参与了、T0 是什么、哪几项要人看"。

### 5.4 S0 自身怎么测（三个组件各自独立可测）

- `runner`：用**假 actor 命令**（例如 `cmd /c echo`）替代客户端即可测"建目录/写 manifest/状态隔离/端口检查"，**不需要真客户端**；
- `collector`：用**已落盘的 netdump 样本**当输入测归集与时钟偏移，**不需要真客户端**
  （样本命名见 §4.1：`netdump-<pid>.log`，`DF\netdump-53916.log` 是既有实例）；
- `verdict`：用**构造的产物目录**测判定三分与"变红"，
  且**每个场景声明必须至少有一条能失败的期望**，否则该场景的判定视为无效
  （防恒真判定 —— 本项目刚在覆盖度任务里踩过"断言非恒真"这条纪律）。


### 5.5 术语冲突说明（避免与既有 recon 证据分类混淆）

本文的 `[存在]/[字节]/[语义]` 是**判定档**，与项目既有的六类证据标记
（`[静态]`/`[抓包]`/`[真机]`/`[回放]`/`[推断]`/`[未验证]`）**不是同一套东西**，不要互用。
场景期望里可以同时写，例如：`expect: {frame: "s2c:noti1", level: "[字节]", evidence: "[抓包]"}`。

## 6. 明确不做（防止范围蔓延）

- **窗口自动化**（驱动闭源客户端 UI）—— 独立的大坑，不在 S0。
- **局域网联机** —— S4。
- **多进程拆分** —— S2；S0 的 runner 只需**能**编排多进程，不要求进程已拆。
- **1171 条协议地图** —— S3。
- **party15 崩溃的修复** —— 所有者已明确不阻塞（先搭流程）。
- **重写任何既有工具** —— S0 只做编排、归因、判定。

## 7. 风险与已知缺口

| 风险 | 说明 | 处置 |
|---|---|---|
| `[语义]` 档占比可能很高 | 若大量期望只能标"需人看"，S0 的自动化收益被削弱 | §5 判据 2 强制"判定必须能变红"；后续按场景逐步把 `[语义]` 升级为 `[字节]` |
| 真人 driver 不可重复 | 真人点击时序天然有抖动 ⇒ 严格逐帧比对可能不稳 | 判定按**存在 + 关键字节**，不按全帧严格时序；`synthetic` driver 用于严格回归 |
| 客户端启动耗时/端口占用 | 官方栈与本项目服务端在宿主端口上**互斥**（`open-questions.md` 坑 14 实测） | runner 启动前做端口占用检查并**明确报错**，不静默失败 |
| `state.json` 隔离改变习惯 | 见 §4.1 | 已确认接受 |

## 8. 后续子项目路线图（各自独立立项，不在 S0）

| # | 子项目 | 一句话 |
|---|---|---|
| **S1** | 地图格式与进度账 | 机读 + 人读的协议地图、覆盖面账、校验和、可换机自检（复用已建好的 `coverage_*` 流水线思路） |
| **S2** | 多进程拆分 | 按官方边界拆 8 个关键进程（`coserver`/`manager`/`channel`/`game`/`bridge`/`relay`/`stun`/`dbmw`）；接口先用已实证的通信层（4 字节头 `u16 id + u16 size`、TCP 心跳 id 4202、业务走 UDP） |
| **S3** | 协议字段填充 | 按场景需要批量反编译 handler → 字段序列 → 写地图；**官服（`llnut/dnf` 17 进程）与 OpenDNF 两条独立对照线** |
| **S4** | 局域网联机 | 把 S0 的靶子从 `127.0.0.1` 换成局域网地址，验收**同一套矩阵** |

## 9. 术语与口径（避免后人误读）

- **S0 是子项目编号**，不是"第零阶段"；总目标（完整还原）没有编号。
- **actor** = 一次 run 里的一个参与者角色，不等于"一个真人"，也不等于"一个进程"。
- **`[语义]` 档不等于失败** —— 它是"当前没有 golden bytes 可自动判定"的诚实标注。
- **runId 全自动生成**；本项目**禁止**再出现 `server-round6b` 这类人工编号（见 §1 证据）。
