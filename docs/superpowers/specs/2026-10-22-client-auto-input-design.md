# 客户端合成输入（B′）设计稿 —— ijl15 进程内注入

> 状态：设计稿 v1（2026-10-22）。作者：Lead。基于两轮 spike 的实证结论。
> 最终目标：验收平台能脚本化驱动真机客户端完成 选角/进城/靠近，**无需人点 UI**，
> 服务 Task 11 的 Tier 2/Tier 3 双客户端验收。

## 1. 背景与实证依据（两轮 spike 结论，均已实测）

### 1.1 前提：OS 级合成输入不可行（Spike 1）

五路实测（SendKeys 回车 / SendInput 鼠标点击 / SetCursorPos / 带对照打字 diff / 静态导入表）
一致表明：`SendInput`/`SendKeys`/`SetCursorPos`/`mouse_event` 等 OS 层注入**到达不了** DNF 2008
客户端的输入处理。静态证据：客户端导入 `dinput8.dll`（`DirectInput8Create`@0x9580C0）。
（附带修正：打字未进登录框可能叠加了「输入框未聚焦」因素，见 §6 风险 2。）

### 1.2 输入子系统结构（Spike 2，IDA 反编译 `0x8FC800–0x8FD400`）

| 组件 | 地址 | 结论 |
|---|---|---|
| DirectInput 设备建立 | `sub_8FC8F0` | `DirectInput8Create`@0x8FC96D → `CreateDevice`(vtable+12) → `SetDataFormat`(vtable+44) → `SetCooperativeLevel`(vtable+52) → `Acquire`(vtable+28) |
| **设备类型** | GUID@0xA906C0 | **`GUID_SysMouse` + `c_dfDIMouse`**（numObjs=11）——**纯鼠标，不是键盘**（pefile 直读字节证实） |
| 鼠标事件队列分发器 | `sub_8FCC80` | 出队（`dword_CFD228/22C/230`）→ 逐个调 `sub_8FCAA0`；末尾 `ClientToScreen`+`SetCursorPos` |
| NativeMouseInput | `sub_8FCAA0` | `char __cdecl (int msg, int wParam, int lParam)`：处理 513/514/516/517（WM_LBUTTONDOWN/UP、WM_MBUTTONDOWN/UP），更新鼠标状态全局量（X=`dword_CFD1EC`、Y=`dword_CFD1F0`、左键=`CFD200/201`、右键=`CFD202/203`） |
| **键盘轮询** | `sub_8FC410`、`sub_8FACE0` | 各 2 次调用 **`GetAsyncKeyState`**（IAT 项 **`0x9983DC`**）——键盘走**异步键态轮询**，不是 DirectInput、不是 WM_CHAR |
| 既有注入先例 | `mouse_wheel.cpp` | IAT 钩 `DispatchMessageA`（0x0099846C）+ 直接调 `NativeMouseInput` 喂 `WM_MOUSEWHEEL` —— **进程内注入机制已在本仓库验证可用** |

### 1.3 结论

进程内注入可行，且键盘有**单一干净落点**：游戏的全部键盘轮询都经过 IAT 项 `0x9983DC` 的
`GetAsyncKeyState`。钩住它 = 一次覆盖两个轮询函数（`sub_8FC410`/`sub_8FACE0`）。
鼠标则复用既有 `NativeMouseInput` 直接调用。

## 2. 方案取舍（键盘注入）

- **方案 A（采纳）：IAT 钩 `GetAsyncKeyState`**（IAT 项 `0x9983DC`）。与 `mouse_wheel.cpp` 的
  `DispatchMessageA` IAT 钩子同一模式（`dfl::memory_patch::WriteBytes` 改 IAT 槽 + 转发原函数）。
  一次覆盖两个轮询函数；不依赖对每个游戏函数的逆向；游戏升级时只有 IAT 地址可能变。
- ~~方案 B：直接调/钩 `sub_8FC410`/`sub_8FACE0`~~ —— 需逐函数逆向签名与调用时机，工作量大且脆。

## 3. 架构

```
验收平台 (tools/acceptance)
    │ 写 auto-input.json（指令时间线）
    ▼
客户端目录 auto-input.json
    │ ijl15 每帧轮询（D3D8 Present 或既有帧钩子内）
    ▼
ijl15 合成输入模块（新增 src/DFLegacy.Ijl15/src/auto_input.cpp/.h）
    ├── 键盘：IAT 钩 GetAsyncKeyState@0x9983DC
    │     钩子逻辑：查合成键态表 → 命中返回假态（按下/释放沿）
    │                 → 未命中转发原 GetAsyncKeyState
    └── 鼠标：直接调 sub_8FCAA0 (NativeMouseInput)
          WM_MOUSEMOVE / WM_LBUTTONDOWN / WM_LBUTTONUP（与滚轮钩子同款调用）
```

### 3.1 指令文件契约（auto-input.json）

- 位置：客户端 exe 同目录（`DNF.exe` 旁），与 `netdump-<pid>.log` 同目录约定一致。
- 形状：`{"version":1, "steps":[{"t":<ms since start>, "op":"key"|"click"|"move"|"text", ...}]}`
  - `key`：`{"vk":13}`（VKcode）；`click`：`{"x":320,"y":240}`（**游戏 640x480 客户区坐标**）；
  - `move`：`{"x":..,"y":..}`；`text`：`{"chars":"abc"}`（逐字符展开为 key down/up）。
- 时序：`t` 相对「指令文件被 ijl15 载入那一刻」。执行完整个时间线后把文件改名
  `auto-input.done.json`（完成标记，供平台轮询），不再重复执行。
- 原子性：平台**写临时文件后 rename**，ijl15 只读完整文件（避免读到半截 JSON）。
- 文件不存在 / JSON 非法 / `version` 不匹配 ⇒ 不注入、不打日志刷屏（一次性 OutputDebugString）。

### 3.2 开关与默认行为

- `Config.ini [Features] EnableAutoInput=0`（**默认关**）。关 = 模块完全不安装钩子、不读文件，
  生产行为与现状逐字节一致。
- 开 = 安装 GetAsyncKeyState IAT 钩子 + 启动指令文件轮询。

### 3.3 每帧驱动点

复用既有渲染帧路径（`d3d8_scaler`/`performance_overlay` 所在的 D3D8 钩子）每帧检查：
- 时间线是否推进到下一步；到点则投喂（键盘改合成表 / 鼠标调 `sub_8FCAA0`）。
- **不新增线程**（避免与游戏输入轮询的线程亲和问题）；全部在游戏主线程钩子内执行。

## 4. 注入语义细节

- 键盘：`GetAsyncKeyState(vk)` 返回 `SHORT`，高位 = 按下。合成表记录「按下/已释放」；
  游戏轮询是**沿敏感**的（按下沿触发一次动作），所以合成层要模拟：down 帧 → 保持 N 帧 → up。
- 鼠标：点击 = `WM_LBUTTONDOWN`(513) + `WM_LBUTTONUP`(514) 各调一次 `sub_8FCAA0`，
  坐标装进 lParam（`LOWORD=x, HIWORD=y`，游戏客户区 640x480）；移动 = `WM_MOUSEMOVE`(0x200)。
  这与 `mouse_wheel.cpp` 现有调用形状完全一致（它喂的就是 `WM_MOUSEWHEEL`）。
- 点击前先 `move` 到目标坐标（游戏状态量 `CFD1EC/F0` 决定点击命中位置）。

## 5. 验证门（Tier 0 → 3）

| 门 | 命令/判据 |
|---|---|
| T0 单元 | 原生自测（`DFLegacy.Ijl15.Tests.vcxproj`，仓库既有自测路径）：合成键态表状态机、JSON 解析、IAT 钩安装/还原 |
| T1 静态 | 构建后 `DumpBin`/pefile 确认 ijl15 导出与钩子字节在位；`EnableAutoInput=0` 时与现版二进制行为一致 |
| T2 真机单键 | 起服务端+客户端到登录框：注入一个合成键/点击 → **net_dump 出现对应封包** + 截图 diff 出现预期 UI 变化（对照实验：不注入时无变化） |
| T3 验收 | `handshake-login-town` 场景在 `EnableAutoInput=1` + 指令文件下**全程无人工点击**跑通（verdict pass）；随后 Task 11 Tier 2/3 用它跑双客户端 |

## 6. 风险与代价（每条带代价）

1. **IAT 钩覆盖面**：`GetAsyncKeyState` 全进程只有 2 个调用点（已实测枚举），且都在输入轮询 ⇒
   影响面可控。代价：若未来客户端补丁新增调用点，合成层会波及（触发条件：客户端更新）。
2. **UI 时序**：登录框可能需先点击聚焦才收键盘（上轮 spike 的教训）。代价：脚本必须包含
   「先 click 聚焦 → 再 text」的步骤；选角/进城的按钮坐标需要一次性人工标定进指令文件。
3. **原生模块双构建定义**：源文件列表同时写在 `CMakeLists.txt` 与 `DFLegacy.Ijl15.vcxproj`
   （仓库既有纪律：新增源文件必须两处都加）。代价：漏一处 = 某条构建路径静默缺模块。
4. **不新增线程**：全部注入在游戏主线程钩子内执行。代价：注入时机受帧率影响（640x480 窗口
   模式帧率足够，毫秒级时间线按帧粒度执行，误差一帧内）。
5. **本模式未覆盖**：`text` 操作对输入法/全角字符的处理（验收路径只需 ASCII 账号名，够用）。

## 7. 非目标

- 不做通用的「游戏机器人」框架；只覆盖验收所需的 key/click/move/text 四种操作。
- 不改动服务端；不动 `net_dump`；不新增第三方钩子库（复用 `dfl::memory_patch`）。
- 不在本设计内解决 Task 11 的 Tier 2 归一化（那是验收脚本层的事）。
