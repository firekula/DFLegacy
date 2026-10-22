# 客户端合成输入（B′）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 ijl15 内新增合成输入模块（IAT 钩 GetAsyncKeyState + NativeMouseInput 直调），让验收平台用 `auto-input.json` 时间线脚本化驱动真机客户端完成 选角/进城/靠近，无需人点 UI。

**Architecture:** 键盘 = IAT 钩 `GetAsyncKeyState`（IAT 项 `0x9983DC`，合成键态表优先、未命中转发原函数）；鼠标 = 直接调 `NativeMouseInput`（`sub_8FCAA0`，与现有滚轮钩子同款）；每帧泵 = 复用 performance_overlay 的调用点钩子（`0x83F996`），`Pump()` 挂钩子顶部；驱动 = 客户端目录 `auto-input.json` 时间线，执行完改名 `.done`。全部改动在游戏主线程执行，不新增线程。

**Tech Stack:** C++20（ijl15 原生模块，MSBuild `DFLegacy.Ijl15.Tests.vcxproj` + `CMakeLists.txt` 双构建）、原生自测（`ijl15_tests.cpp`，同进程编入被测源码）、Python 3.12（tools/acceptance 驱动端 + pytest）。

**Spec:** `docs/superpowers/specs/2026-10-22-client-auto-input-design.md`

## Global Constraints

- **默认关闭**：`Config.ini [Features] EnableAutoInput=0`；关闭时模块不装钩子、不读文件，客户端行为与现状逐字节一致（spec §3.2）。
- **不新增线程**：注入只在游戏主线程执行（frame 钩子内）；合成键态表与时间线状态只在主线程读写（spec §3.3）。
- **地址常量是 2008DF 专有**：`GetAsyncKeyState` IAT=`0x9983DC`、`NativeMouseInput`=`0x8FCAA0`（`char __cdecl(int,int,int)`）、帧调用点=`0x83F996`、原渲染器=`0x836990`。安装时必须校验现值（IAT 现指针非空；call site 首字节 `0xE8` 且目标==`0x836990`），不匹配 ⇒ 不安装 + `OutputDebugStringA`，客户端照常跑（`mouse_wheel.cpp` 既有模式）。
- **双构建定义**：新增源文件必须**同时**加进 `src/DFLegacy.Ijl15/CMakeLists.txt` 与 `src/DFLegacy.Ijl15/DFLegacy.Ijl15.vcxproj`（仓库既有纪律，onboarding §7）。
- **构建门**：`& "D:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" src\DFLegacy.Ijl15\tests\DFLegacy.Ijl15.Tests.vcxproj -p:Configuration=Release -p:Platform=Win32` 零警告零错误；测试 = 运行产物 `artifacts\native\ijl15-tests-vcx\Release\ijl15_tests.exe` exit 0。
- **鼠标坐标口径**：`sub_8FCAA0` 的 `lParam` 是**游戏客户区坐标**（低字 x、高字 y，游戏内部自己加 `CFD210/CFD214` 视口偏移——反编译已证）；点击序列 = `WM_MOUSEMOVE(0x200)` → `WM_LBUTTONDOWN(513)` → `WM_LBUTTONUP(514)`，down 与 up 间隔 ≥1 帧。
- **提交纪律**：逐文件 pathspec，中文提交信息，禁止 `git add -A`；`docs/` 提交用 `git add -f`。
- **平台侧测试**：`tools` 目录下 `python -m pytest acceptance/tests -q` 全绿（本机 2311 已恢复，当前基线 123 passed，只增不减）。

---

### Task 1: 配置键 + 构建接线 + 安装块（模块骨架）

**Files:**
- Modify: `src/DFLegacy.Ijl15/src/ijl15_config.h`（`EnableMouseWheel` 之后加字段）
- Modify: `src/DFLegacy.Ijl15/src/ijl15_config.cpp`（默认值 + 读取）
- Create: `src/DFLegacy.Ijl15/src/auto_input.h`
- Create: `src/DFLegacy.Ijl15/src/auto_input.cpp`（本任务只做骨架：`Install()` 校验并返回，真实钩子在 Task 3）
- Modify: `src/DFLegacy.Ijl15/CMakeLists.txt`（加 auto_input.cpp）
- Modify: `src/DFLegacy.Ijl15/DFLegacy.Ijl15.vcxproj`（加 auto_input.cpp/h）
- Modify: `src/DFLegacy.Ijl15/src/dnf_patches.cpp`（安装块 + 两个 gate 扩展）
- Modify: `src/DFLegacy.Ijl15/src/performance_overlay.cpp`（钩子顶部调 `Pump()`；文字绘制按 overlay 开关门控）
- Test: `src/DFLegacy.Ijl15/tests/ijl15_tests.cpp`

**Interfaces:**
- Produces: `dfl::ijl15_config::Settings::EnableAutoInput`（bool，默认 false）；`dfl::auto_input::{Install, Restore, IsEnabled, Pump}`；`DrawFpsAndMemory` 每帧先 `Pump()`。

- [ ] **Step 1: 写失败测试**（`ijl15_tests.cpp` 既有断言风格：返回 bool 的检查函数 + 计数；照抄文件内第一个测试的写法）

```cpp
static bool TestAutoInputConfigDefaults() {
    // 默认关闭：不给 Config.ini 该键时必须 false
    return dfl::ijl15_config::Settings{}.EnableAutoInput == false;
}
```
（注册进既有测试表；`ijl15_tests.cpp` 的注册模式照抄文件内既有用例条目。）

- [ ] **Step 2: 跑构建确认失败**：MSBuild 命令见 Global Constraints；预期编译错（`EnableAutoInput` 不存在）。
- [ ] **Step 3: 实现**——`ijl15_config.h` 在 `bool EnableMouseWheel;` 后加 `bool EnableAutoInput;`；`ijl15_config.cpp` 在 `EnableMouseWheel` 的 `DefaultSettings` 行后加 `EnableAutoInput = false,`（照抄该结构体初始化风格），在 `CurrentSettings.EnableMouseWheel = ReadBoolean(...)`（:216-220）后照抄一段：
```cpp
            CurrentSettings.EnableAutoInput = ReadBoolean(
                /*section*/ "Features",
                /*key*/ "EnableAutoInput",
                DefaultSettings.EnableAutoInput);
```
（`ReadBoolean` 的实际参数形态以 :216-220 现场为准，逐字对齐。）骨架 `auto_input.h/.cpp`：
```cpp
// auto_input.h
#pragma once
namespace dfl::auto_input {
    bool Install() noexcept;   // Task 3 补钩子；本任务只做安装门与地址校验
    void Restore() noexcept;
    bool IsEnabled() noexcept; // = ijl15_config::Get().EnableAutoInput
    void Pump() noexcept;      // Task 2/3 填充；本任务空实现
}
// auto_input.cpp
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "auto_input.h"
#include "ijl15_config.h"
namespace dfl::auto_input {
    bool IsEnabled() noexcept { return dfl::ijl15_config::Get().EnableAutoInput; }
    void Pump() noexcept {}
    bool Install() noexcept { return true; }   // Task 3 替换
    void Restore() noexcept {}
}
```
- [ ] **Step 4: 接线**——`dnf_patches.cpp`：①`:457` 的 gate 判定改为
`return dfl::ijl15_config::Get().EnablePerformanceOverlay || dfl::ijl15_config::Get().EnableAutoInput;`
②`:1028` 的安装条件改为
`if ((dfl::ijl15_config::Get().EnablePerformanceOverlay || dfl::ijl15_config::Get().EnableAutoInput) && !dfl::performance_overlay::InstallCallHook())`
③在 `:1129-1134` mouse_wheel 安装块之后、`appliedCount` 判定之前插入：
```cpp
        if (dfl::ijl15_config::Get().EnableAutoInput
            && !dfl::auto_input::Install())
        {
            OutputDebugStringA(
                "DFLegacy.Ijl15: auto-input was not installed.\n");
        }
```
`performance_overlay.cpp`：顶部 `#include "auto_input.h"` + `#include "ijl15_config.h"`；`DrawFpsAndMemory` 开头（:86 函数体第一行前）插入
```cpp
        if (dfl::auto_input::IsEnabled()) { dfl::auto_input::Pump(); }
        if (!dfl::ijl15_config::Get().EnablePerformanceOverlay) { return 0; }
```
（函数其余部分不动。这样 `EnableAutoInput=1` 且 overlay 关时：每帧仍进钩子跑泵，但不画 overlay 文字；原渲染器调用仍被 `NativeMonitoringFlagOffset` 门控，与 overlay 开启时行为一致。）
`CMakeLists.txt` 与 `DFLegacy.Ijl15.vcxproj`：把 `src/auto_input.cpp`、`src/auto_input.h` 按既有条目格式加进源列表（两处都要，漏一处=某条构建路径静默缺模块）。
- [ ] **Step 5: 构建零警告零错误 + 测试绿 + 提交**
```bash
git add src/DFLegacy.Ijl15/src/ijl15_config.h src/DFLegacy.Ijl15/src/ijl15_config.cpp src/DFLegacy.Ijl15/src/auto_input.h src/DFLegacy.Ijl15/src/auto_input.cpp src/DFLegacy.Ijl15/CMakeLists.txt src/DFLegacy.Ijl15/DFLegacy.Ijl15.vcxproj src/DFLegacy.Ijl15/src/dnf_patches.cpp src/DFLegacy.Ijl15/src/performance_overlay.cpp src/DFLegacy.Ijl15/tests/ijl15_tests.cpp
git commit -m "B′: ijl15 合成输入模块骨架（配置键+构建接线+帧泵挂点）"
```

---

### Task 2: 纯逻辑核心 —— JSON 子集解析器 + 时间线状态机 + 键沿表（TDD）

**Files:**
- Modify: `src/DFLegacy.Ijl15/src/auto_input.h`（加核心声明）
- Modify: `src/DFLegacy.Ijl15/src/auto_input.cpp`（加实现）
- Test: `src/DFLegacy.Ijl15/tests/ijl15_tests.cpp`

**Interfaces:**
- Produces（后续 Task 3/4 依赖，签名逐字）：
```cpp
namespace dfl::auto_input {
    enum Op : int { kKey = 0, kClick = 1, kMove = 2, kText = 3 };
    struct Step { int t = 0; Op op = kKey; unsigned vk = 0; int x = 0; int y = 0; int dur = 80; char text[64] = {}; };
    static constexpr int kMaxSteps = 256;
    // 解析 spec §3.1 的 JSON 子集；成功返回 true 并填 steps/count/version；任何非法输入返回 false 且不清空调用方缓冲。
    bool ParsePlan(const char* text, Step* steps, int maxSteps, int* count, int* version);
    // 时间线推进：now = GetTickCount() 口径毫秒。返回本帧注入的键盘沿数（测试用）。
    int Advance(unsigned nowMs);
    // 键沿表（IAT 钩子体调用）：合成按下返回 0x8000，否则原样转发 original。
    short QueryKey(unsigned vk, short original);
    // 文件装载：读 <exe目录>\auto-input.json → ParsePlan → 进入 armed 状态；Task 3 由 Install 调用。
    bool LoadPlanFile();
}
```
- 语义钉死：`key` = `SynthKeyDown(vk)` 立即、`SynthKeyUp` 在 `t+dur`；`click` = 注入 `move→down`（Task 3 接 `sub_8FCAA0`，本任务先记录到可测的注入日志缓冲）+ up 在 `t+dur`；`text` = 逐字符 key（down 间隔 ≥40ms 串行展开）；全部步骤完成且无未决 up ⇒ 状态 `Finished`，`LoadPlanFile` 的完成标记（改名 `.done.json`）在 `Advance` 内触发一次。

- [ ] **Step 1: 写失败测试**（核心三组，全部纯逻辑）

```cpp
static bool TestParsePlanHappyPath() {
    dfl::auto_input::Step steps[dfl::auto_input::kMaxSteps];
    int count = 0, version = 0;
    const char* json = "{\"version\":1,\"steps\":[{\"t\":100,\"op\":\"click\",\"x\":320,\"y\":240},"
        "{\"t\":300,\"op\":\"key\",\"vk\":13,\"dur\":60},{\"t\":500,\"op\":\"text\",\"chars\":\"test\"}]}";
    if (!dfl::auto_input::ParsePlan(json, steps, kMaxSteps, &count, &version)) return false;
    return version == 1 && count == 3
        && steps[0].t == 100 && steps[0].op == dfl::auto_input::kClick
        && steps[0].x == 320 && steps[0].y == 240
        && steps[1].op == dfl::auto_input::kKey && steps[1].vk == 13 && steps[1].dur == 60
        && steps[2].op == dfl::auto_input::kText
        && steps[2].text[0] == 't' && steps[2].text[3] == 't';
}
static bool TestParsePlanRejectsGarbage() {
    dfl::auto_input::Step steps[8]; int count = 0, version = 0;
    const char* bad[] = { "", "{", "{\"version\":2,\"steps\":[]}",      // 版本不符
        "{\"version\":1}", "{\"version\":1,\"steps\":[{\"t\":1,\"op\":\"fly\"}]}" };
    for (const char* s : bad) {
        if (dfl::auto_input::ParsePlan(s, steps, 8, &count, &version)) return false;
        if (count != 0) return false;   // 失败不得污染调用方缓冲
    }
    return true;
}
static bool TestAdvanceKeyEdgeAndFinish() {
    // 装载一条 key 时间线：t=100 按下 dur=60 → t=160 释放 → Finished
    // （装载入口：测试用 ParsePlan+内部 ArmPlan 等价物，避免碰文件系统）
    // 断言：t<100 无键；100≤t<160 QueryKey(13)=0x8000；t≥160 归零且状态 Finished
    // 注入日志：click 步骤在本任务先落到 GetInjectionLog()（Task 3 改为真调用），
    // 断言 click 产生 ["move","down","up"] 三条且坐标正确。
    ... // 按上述语义逐条 check，返回 true/false
}
```
（`GetInjectionLog`：模块内固定 64 条 `struct { int op; unsigned vk; int x; int y; }` 环形缓冲 + `ClearInjectionLog()`，测试与 T2 真机验证共用。）
- [ ] **Step 2: 跑构建+测试确认失败**（`ParsePlan` 等符号不存在 → 编译错）。
- [ ] **Step 3: 实现**——`ParsePlan` 用**手写递归下降**（无第三方依赖）：只接受 root 对象的 `version`(整数==1) 与 `steps`(对象数组)；step 只接受键 `t/op/vk/x/y/dur/chars`（未知键报错）；字符串只支持 `\` `\"` 两种转义；数字为十进制整数；越界/尾随垃圾/深层嵌套一律 false。`Advance`/`QueryKey`/注入日志按上面语义钉死。`LoadPlanFile`：`GetModuleFileNameA` 取 exe 目录 + `\\auto-input.json`，`CreateFileA+ReadFile` 读入，`ParsePlan` 成功才 armed；`Advance` 检测 Finished 时 `MoveFileA(auto-input.json, auto-input.done.json)`（目标已存在则先 DeleteFileA）。
- [ ] **Step 4: 构建+测试绿**；**Step 5: 提交**
```bash
git add src/DFLegacy.Ijl15/src/auto_input.h src/DFLegacy.Ijl15/src/auto_input.cpp src/DFLegacy.Ijl15/tests/ijl15_tests.cpp
git commit -m "B′: 合成输入纯逻辑核心（JSON 子集解析+时间线+键沿表）"
```

---

### Task 3: 注入层 —— IAT 钩 GetAsyncKeyState + NativeMouseInput 直调

**Files:**
- Modify: `src/DFLegacy.Ijl15/src/auto_input.h`（`Install/Restore/Pump` 转正）
- Modify: `src/DFLegacy.Ijl15/src/auto_input.cpp`
- Modify: `src/DFLegacy.Ijl15/tests/ijl15_tests.cpp`（`click` 注入从日志改真调用的开关：仅当 `NativeMouseInput` 地址可读且校验通过才真调；测试进程内不装钩子，仍走日志路径）

**Interfaces:**
- Consumes: Task 2 全部；`dfl::memory_patch::WriteBytes`（memory_patch.h）；`mouse_wheel.cpp` 的 IAT 钩模式（`:767-799 InstallDispatchMessageHook`）。
- Produces: `Install()` = ①校验 IAT `0x9983DC` 现指针非空 → `WriteBytes` 换成 `HookedGetAsyncKeyState`；②`LoadPlanFile()`；`Restore()` = 还原 IAT + `SynthClear`。

- [ ] **Step 1: 写失败测试**：`QueryKey` 直测已在 Task 2；本任务新增「未合成键必须转发 original」：

```cpp
static bool TestQueryKeyForwardsOriginal() {
    // 合成表为空时，QueryKey(0x41, 0x0001) 必须原样返回 0x0001（不得吞掉真实键盘）
    return dfl::auto_input::QueryKey(0x41, 0x0001) == 0x0001
        && dfl::auto_input::QueryKey(0x41, (short)0x8000) == (short)0x8000;
}
```
- [ ] **Step 2: 跑测试确认失败**（当前 QueryKey 已按 Task 2 实现，本步实为守护测试；若已绿，记录后直接进 Step 3——它的价值在 Task 3 改动后防回归）。
- [ ] **Step 3: 实现**——`HookedGetAsyncKeyState`：
```cpp
using GetAsyncKeyStateFn = SHORT(WINAPI*)(int);
GetAsyncKeyStateFn OriginalGetAsyncKeyState = nullptr;
constexpr std::uintptr_t GetAsyncKeyStateIat = 0x009983DC;
SHORT WINAPI HookedGetAsyncKeyState(int vk) noexcept {
    // 合成态优先：表内有记录（无论按下/已释放）都由我们应答，避免真实键盘污染沿
    if (SynthTableHas(vk)) { return SynthTableState(vk); }
    return OriginalGetAsyncKeyState ? OriginalGetAsyncKeyState(vk) : 0;
}
```
`Install()`：读 IAT 现值（复用 mouse_wheel.cpp `:167-177 GetIatValue` 的读法——`ReadValue(0x9983DC, ptr)`）→ 非空 → `dfl::memory_patch::WriteBytes(0x9983DC, &hook, sizeof(hook))` → `LoadPlanFile()`；`Restore()` 反向写回。
鼠标注入（Task 2 的注入日志落地为真调用）：
```cpp
using NativeMouseInputFn = char(__cdecl*)(int, int, int);
constexpr std::uintptr_t NativeMouseInput = 0x008FCAA0;
void InjectMouse(int msg, int x, int y) noexcept {
    const auto fn = reinterpret_cast<NativeMouseInputFn>(NativeMouseInput);
    fn(msg, msg == 513 ? 1 : 0, (short)x | ((short)y << 16));  // wParam: MK_LBUTTON 仅 down
}
// click = InjectMouse(0x200,x,y); InjectMouse(513,x,y); （up 由 Advance 在 t+dur 触发）InjectMouse(514,x,y);
```
（`Install()` 时对 `0x8FCAA0` 做 `IsReadable` 校验——照抄 mouse_wheel.cpp `:116-152`——不可读则只禁用鼠标注入、键盘钩子照装，并 OutputDebugString。）
- [ ] **Step 4: 构建+全量原生测试绿**；**Step 5: 提交**
```bash
git add src/DFLegacy.Ijl15/src/auto_input.h src/DFLegacy.Ijl15/src/auto_input.cpp src/DFLegacy.Ijl15/tests/ijl15_tests.cpp
git commit -m "B′: IAT 钩 GetAsyncKeyState + NativeMouseInput 直调注入"
```

---

### Task 4: 验收平台驱动端（tools/acceptance/auto_input.py）

**Files:**
- Create: `tools/acceptance/auto_input.py`
- Test: `tools/acceptance/tests/test_auto_input.py`

**Interfaces:**
- Produces: `write_plan(client_dir, steps, timeout_s=30) -> Path`（写 `auto-input.json.tmp` → `os.replace` 成 `auto-input.json`，`steps` 是与 §3.1 同构的 dict 列表）；`await_done(client_dir, timeout_s) -> bool`（轮询 `auto-input.done.json`）；`wait_ready(client_dir, timeout_s) -> bool`（等 `.done` 消失 = ijl15 已取走）。
- 语义：`write_plan` 前若存在旧 `auto-input.json`/`.tmp` 先删；`await_done` 到点未完成 → 返回 False（调用方判失败），**绝不猜**。

- [ ] **Step 1: 写失败测试**（tmp_path 假 client_dir；不碰真客户端）

```python
def test_write_plan_is_atomic_and_done_roundtrip(tmp_path):
    from acceptance import auto_input
    steps = [{"t": 100, "op": "click", "x": 320, "y": 240}]
    auto_input.write_plan(tmp_path, steps)
    plan = tmp_path / "auto-input.json"
    assert plan.is_file() and not (tmp_path / "auto-input.json.tmp").exists()
    assert json.loads(plan.read_text(encoding="utf-8"))["steps"] == steps
    assert auto_input.wait_ready(tmp_path, timeout_s=1) is True   # 没有 .done = 已就绪
    (tmp_path / "auto-input.done.json").write_text("{}", encoding="utf-8")
    assert auto_input.wait_ready(tmp_path, timeout_s=1) is False  # .done 在 = 未就绪
    assert auto_input.await_done(tmp_path, timeout_s=1) is True
    assert auto_input.await_done(tmp_path, timeout_s=1) is True   # 幂等
```
- [ ] **Step 2: 跑测试确认失败**；**Step 3: 实现**（标准库 json/os/time；`os.replace` 原子替换；`await_done` 50ms 轮询）。
- [ ] **Step 4: `(cd tools) python -m pytest acceptance/tests -q` 全绿**（基线 123 passed → 123+N）；**Step 5: 提交**
```bash
git add tools/acceptance/auto_input.py tools/acceptance/tests/test_auto_input.py
git commit -m "B′: 验收平台合成输入驱动端（原子写计划+等完成标记）"
```

---

### Task 5: 真机验证（T2 单键门 + T3 验收门）+ 收尾

**Files:**
- Create: `docs/superpowers/plans/2026-10-22-b3-real-machine-runbook.md`（执行手册，`git add -f`）
- Modify: `STATUS.md`（结论与证据类别）

- [ ] **Step 1: T2 真机单键**（Lead 执行，按 runbook）：
  1. 起 `DF\town-test\DFLegacy.Server.exe`（后台），等 2311/8081 监听；
  2. `DFLegacy.Launcher.exe --headless --no-dialog --account test` 起客户端；
  3. **对照**：先截屏 A（无注入，等 2s 截屏 B，diff≈动画基线）；
  4. 写 `auto-input.json`（一条 `click` 到屏幕已知按钮 + 一条 `key`），等 `.done`；
  5. **判据（全部满足才算过）**：`netdump-<pid>.log` 在注入时刻后出现**对应新封包**（点击/按键引发的动作包）；截图 diff 显著大于动画基线；客户端无崩溃无卡死；
  6. 若键盘无反应：先补「click 聚焦输入框」步骤再重试（spec §6 风险 2），仍无反应则记录 open 项并回退鼠标-only。
- [ ] **Step 2: T3 验收门**：`handshake-login-town` 场景 + 选角/进城指令文件，**全程无人工 UI**跑通；verdict 出 `pass`（`[语义]` 行除外，它本来就是 needs-human）。
- [ ] **Step 3: 全量回归**：MSBuild 原生自测（0 警告 0 错误）+ `python -m pytest acceptance/tests -q` 全绿 + `dotnet run --project tests/DFLegacy.SmokeTests -c Release --no-build`（0 failures——本特性不碰 C#，跑一次证不回归）+ `python -m protocol.s1 verify`（5/5）。
- [ ] **Step 4: runbook + STATUS.md 落盘，提交**
```bash
git add -f docs/superpowers/plans/2026-10-22-b3-real-machine-runbook.md
git add STATUS.md
git commit -m "B′: 真机单键/无人工 UI 验收记录"
```

---

## Self-Review

1. **Spec 覆盖**：§3.1 指令文件契约→Task 2/4；§3.2 开关→Task 1；§3.3 帧泵→Task 1 接线 + Task 3 泵体；§4 注入语义→Task 2/3；§5 T0→Task 1/2/3、T2/T3→Task 5；§6 风险 2（聚焦时序）→Task 5 Step 1.6；双构建→Task 1 Step 4。无缺口。
2. **占位符扫描**：无 TBD/TODO；Task 1 的 `ReadBoolean` 参数形态标注了「以现场 ：216-220 为准，逐字对齐」——这是定位指令不是占位符；Task 3 的 `SynthTableHas/SynthTableState` 在 Task 2 已定义（键沿表）。
3. **类型一致性**：`Step`/`ParsePlan`/`QueryKey`/`Advance` 在 Task 2 定义、Task 3/4 按名引用；`NativeMouseInput` 签名 `char __cdecl(int,int,int)` 与反编译一致；WM 常量 513/514/0x200 全文一致。
