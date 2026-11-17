# DFOLauncher 单启动器布局 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增原生 `DFOLauncher.exe` 作为外层唯一启动器，首次运行把现有 v1.0.13-r2 客户端整体迁移进 `client\`，并打包/发行 `v1.0.13-r3`。

**Architecture:** 原生 Win32 C++ 启动器只做两件事：启动 `client\DFLegacy.Launcher.exe`，或在旧布局上做一次性同盘改名迁移（迁移日志 + 失败回滚）。客户端目录、清单、收据和更新链保持 v1.0.13-r2 原样，启动器不参与更新。

**Tech Stack:** C++20 / MSVC / CMake（/W4 /WX、静态 CRT、UNICODE）、Win32 API（Toolhelp32、MoveFileW、CreateMutexW、IShellLinkW）、CTest；Python 打包脚本；PowerShell 构建/发行。

**Spec:** `knowledge/decisions/2026-11-17-dfo-launcher-layout-design.md`

## Global Constraints

- 客户端三程序、787 文件清单、收据和序列必须保持 `v1.0.13-r2`；本计划不重新构建客户端二进制，不改清单/收据内容。
- `DFOLauncher.exe` 不进入 787 文件清单，不联网、不读更新清单、不修改收据；哈希固定，后续只能通过新的小 EXE 替换。
- 迁移只在普通本地目录、同卷、客户端和游戏进程已关闭时进行；拒绝符号链接/目录联接/重解析点；任一步失败必须回滚。
- 迁移日志必须原子写入；中断后下次启动能恢复；`client` 存在但入口缺失且无日志时停止，不覆盖。
- 外层最终只有 `DFOLauncher.exe` 和 `client\`；`client` 默认可见、不隐藏；`DFOLauncher.migrate` 只在迁移期间短暂存在。
- 启动器版本：FileVersion `1.0.13.0`，ProductVersion `1.0.13+<40位提交>`；FileDescription `DFLegacy Launcher`，OriginalFilename `DFOLauncher.exe`，ProductName/CompanyName `DFLegacy`，版权沿用现有 EXE（`Copyright (C) 2026 by shizuku30`）。
- 所有新窗口/提示使用原版 DNF 风格；迁移进度与确认/错误沿用 `src/DFLegacy.Startup/startup_prompt.cpp` 的皮肤资源（`classic-message-panel.png`、确认按钮图）。
- 发行：`v1.0.13-r3`；客户端不变；GitHub 只放 stub + 说明，新的 1.1 GB 启动包只上 GitCode；所有附件匿名回读校验。
- 每个任务结束前 commit；提交信息用英文、现在时。

---

## File Structure

- `src/DFLegacy.LauncherStub/CMakeLists.txt`：原生项目、测试目标、版本资源。
- `src/DFLegacy.LauncherStub/DFOLauncher.rc.in`：图标/版本资源模板。
- `src/DFLegacy.LauncherStub/build-native.ps1`：CMake/MSVC 构建 + ctest + 资源。
- `src/DFLegacy.LauncherStub/main.cpp`：入口，参数、互斥量、模式分派。
- `src/DFLegacy.LauncherStub/layout.hpp/.cpp`：布局识别。
- `src/DFLegacy.LauncherStub/process.hpp/.cpp`：客户端/游戏进程检测。
- `src/DFLegacy.LauncherStub/launch.hpp/.cpp`：启动客户端与早期失败检测。
- `src/DFLegacy.LauncherStub/journal.hpp/.cpp`：迁移日志（二进制、原子写）。
- `src/DFLegacy.LauncherStub/migration.hpp/.cpp`：迁移、回滚、恢复、`--restore`。
- `src/DFLegacy.LauncherStub/shortcut.hpp/.cpp`：桌面/开始菜单快捷方式改指向。
- `src/DFLegacy.LauncherStub/ui.hpp/.cpp`：经典风格确认/错误/进度窗口。
- `src/DFLegacy.LauncherStub/tests/fixture.cpp`：测试子进程。
- `src/DFLegacy.LauncherStub/tests/stub_tests.cpp`：CTest 入口。
- `tools/release/launcher_layout.py`：新布局启动包打包/校验。
- `tools/release/tests/test_launcher_layout.py`：打包脚本测试。
- `docs/releases/v1.0.13-r3.md`：发行说明。
- `knowledge/decisions/2026-11-17-dfo-launcher-layout-acceptance.md`：验收记录。

---

### Task 1: 原生项目骨架、版本资源与构建

**Files:**
- Create: `src/DFLegacy.LauncherStub/CMakeLists.txt`
- Create: `src/DFLegacy.LauncherStub/DFOLauncher.rc.in`
- Create: `src/DFLegacy.LauncherStub/main.cpp`
- Create: `src/DFLegacy.LauncherStub/build-native.ps1`
- Create: `src/DFLegacy.LauncherStub/tests/stub_tests.cpp`
- Test: build + PE 属性检查

**Interfaces:**
- Produces: 可构建的 `DFOLauncher.exe`；`build-native.ps1 -Architecture x64 -Version 1.0.13 -Revision <sha> -RunTests` 返回 0；PE 版本资源字段符合 Global Constraints。

- [ ] **Step 1: 写最小项目与空测试**

`CMakeLists.txt`：
```cmake
cmake_minimum_required(VERSION 3.24)
project(DFLegacyLauncherStub LANGUAGES CXX RC)
if(NOT WIN32 OR NOT MSVC)
  message(FATAL_ERROR "DFLegacy.LauncherStub requires Windows and MSVC")
endif()
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
add_compile_options(/W4 /WX /utf-8 /permissive-)
add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
set(DFL_LAUNCHER_VERSION "1.0.13" CACHE STRING "Three-part launcher version")
if(NOT DFL_LAUNCHER_VERSION MATCHES "^[0-9]+\\.[0-9]+\\.[0-9]+$")
  message(FATAL_ERROR "Use a three-part launcher release version")
endif()
string(REPLACE "." "," DFL_VERSION_COMPONENTS "${DFL_LAUNCHER_VERSION}")
set(DFL_PRODUCT_VERSION "${DFL_LAUNCHER_VERSION}")
if(DFL_SOURCE_REVISION)
  if(NOT DFL_SOURCE_REVISION MATCHES "^[0-9a-f]{40}$")
    message(FATAL_ERROR "Set the source revision to a 40-character Git commit")
  endif()
  string(APPEND DFL_PRODUCT_VERSION "+${DFL_SOURCE_REVISION}")
endif()
set(DFL_ICON "${CMAKE_CURRENT_SOURCE_DIR}/../DFLegacy.Launcher/DFLegacy.Launcher.ico")
set(DFL_FILE_DESCRIPTION "DFLegacy Launcher")
set(DFL_EXECUTABLE_NAME "DFOLauncher.exe")
set(DFL_INTERNAL_NAME "DFOLauncher")
configure_file(DFOLauncher.rc.in "${CMAKE_CURRENT_BINARY_DIR}/DFOLauncher.rc" @ONLY)
set_source_files_properties("${CMAKE_CURRENT_BINARY_DIR}/DFOLauncher.rc" PROPERTIES OBJECT_DEPENDS "${DFL_ICON}")
add_executable(DFOLauncher WIN32 main.cpp "${CMAKE_CURRENT_BINARY_DIR}/DFOLauncher.rc")
target_link_libraries(DFOLauncher PRIVATE shell32 ole32 shlwapi)
include(CTest)
if(BUILD_TESTING)
  add_executable(stub_tests tests/stub_tests.cpp)
  add_test(NAME stub_layout_and_migration COMMAND stub_tests)
endif()
```

> 后续任务把 `layout.cpp`、`launch.cpp`、`process.cpp`、`journal.cpp`、`migration.cpp`、`shortcut.cpp` 按任务加入 `DFOLauncher` 和 `stub_tests` 的源列表；`stub_tests` 通过 CTest 参数接收 fixture 路径（`add_test(... COMMAND stub_tests "$<TARGET_FILE:fixture>")`）。

`DFOLauncher.rc.in`：复制 `src/DFLegacy.Startup/launcher.rc.in`，去掉 `DFL_MANAGED_MANIFEST_RESOURCE`，`FileDescription`/`OriginalFilename`/`InternalName` 用上面变量。

`main.cpp` 最小实现：
```cpp
#include <windows.h>
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) { return 0; }
```

`tests/stub_tests.cpp`：
```cpp
int main() { return 0; }
```

`build-native.ps1`：复制 `src/DFLegacy.Startup/build-native.ps1`，把项目目录改成本目录、产物名 `DFOLauncher`、`-DDFL_LAUNCHER_VERSION=...`、`-DDFL_SOURCE_REVISION=...`，`-DFL_LAUNCHER_VERSION` 从 `../DFLegacy.Launcher/LauncherVersion.txt` 默认读取，`-RunTests` 跑 ctest。

- [ ] **Step 2: 构建并验证元数据**

Run:
```powershell
pwsh -NoProfile -File src/DFLegacy.LauncherStub/build-native.ps1 -Architecture x64 -Version 1.0.13 -Revision 18bfc9d3f798e4978243d168402d0f65fcd7a038 -RunTests
$v = (Get-Item artifacts/native/launcher-stub/Release/DFOLauncher.exe).VersionInfo
$v.OriginalFilename; $v.FileDescription; $v.FileVersion; $v.ProductVersion
```
Expected: `DFOLauncher.exe` / `DFLegacy Launcher` / `1.0.13.0` / `1.0.13+18bfc9d...`。

- [ ] **Step 3: Commit**
```bash
git add src/DFLegacy.LauncherStub
git commit -m "Add the DFOLauncher native stub project"
```

---

### Task 2: 布局识别

**Files:**
- Create: `src/DFLegacy.LauncherStub/layout.hpp`, `layout.cpp`
- Test: `tests/stub_tests.cpp`（追加）

**Interfaces:**
- Produces: `namespace dfo { enum class Layout { Migrated, Legacy, Invalid }; Layout Classify(const std::filesystem::path& dir); bool ClientComplete(const std::filesystem::path& client); }`

- [ ] **Step 1: 写失败测试**

```cpp
#include "../layout.hpp"
#include <cassert>
#include <filesystem>
int main() {
    namespace fs = std::filesystem;
    auto root = fs::temp_directory_path() / "dfo-layout-test";
    fs::remove_all(root); fs::create_directories(root / "client");
    fs::create_directories(root / "client" / "sub");
    std::ofstream(root / "client" / "DFLegacy.Launcher.exe") << "x";
    assert(dfo::Classify(root) == dfo::Layout::Migrated);
    fs::remove_all(root / "client");
    std::ofstream(root / "DFLegacy.Launcher.exe") << "x";
    std::ofstream(root / "DFLegacy.Launcher.App.exe") << "x";
    std::ofstream(root / "DFLegacy.Updater.exe") << "x";
    std::ofstream(root / "DFLegacy.Chapter.json") << "{}";
    assert(dfo::Classify(root) == dfo::Layout::Legacy);
    fs::remove_all(root);
    assert(dfo::Classify(root) == dfo::Layout::Invalid);
    return 0;
}
```
（`ClientComplete` 在 Task 7 的完整校验里用；本任务先只测 `Classify`。）

- [ ] **Step 2: 运行并确认失败**

Run: `pwsh -File src/DFLegacy.LauncherStub/build-native.ps1 -Architecture x64 -RunTests`
Expected: 编译失败（layout.hpp 不存在）。

- [ ] **Step 3: 实现**

```cpp
// layout.hpp
#pragma once
#include <filesystem>
namespace dfo {
enum class Layout { Migrated, Legacy, Invalid };
Layout Classify(const std::filesystem::path& dir);
bool ClientComplete(const std::filesystem::path& client);
}
```
```cpp
// layout.cpp
#include "layout.hpp"
#include <windows.h>
namespace fs = std::filesystem;
namespace dfo {
static bool File(const fs::path& p) {
    auto a = GetFileAttributesW(p.c_str());
    return a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY) && !(a & FILE_ATTRIBUTE_REPARSE_POINT);
}
Layout Classify(const fs::path& dir) {
    if (File(dir / "client" / "DFLegacy.Launcher.exe")) return Layout::Migrated;
    if (File(dir / "DFLegacy.Launcher.exe") && File(dir / "DFLegacy.Launcher.App.exe") &&
        File(dir / "DFLegacy.Updater.exe") && File(dir / "DFLegacy.Chapter.json")) return Layout::Legacy;
    return Layout::Invalid;
}
bool ClientComplete(const fs::path& client) {
    return File(client / "DFLegacy.Launcher.exe") && File(client / "DFLegacy.Launcher.App.exe") &&
           File(client / "DFLegacy.Updater.exe") && File(client / "DFLegacy.Bootstrap.ini") &&
           File(client / "DFLegacy.Chapter.json") && File(client / "DNF.exe") && File(client / "Script.pvf");
}
}
```
把 `layout.cpp` 加入 `add_executable(DFOLauncher ...)` 和测试目标（测试直接编译 `layout.cpp`）。

- [ ] **Step 4: 运行测试通过**

Run: `pwsh -File src/DFLegacy.LauncherStub/build-native.ps1 -Architecture x64 -RunTests`
Expected: `stub_layout_and_migration` PASS。

- [ ] **Step 5: Commit**
```bash
git add src/DFLegacy.LauncherStub/layout.* src/DFLegacy.LauncherStub/tests/stub_tests.cpp src/DFLegacy.LauncherStub/CMakeLists.txt
git commit -m "Classify the launcher layout"
```

---

### Task 3: 启动客户端与早期失败检测

**Files:**
- Create: `launch.hpp/.cpp`, `tests/fixture.cpp`
- Modify: `CMakeLists.txt`, `stub_tests.cpp`

**Interfaces:**
- Produces: `namespace dfo { struct LaunchResult { bool started; DWORD exitCode; bool earlyFailure; }; LaunchResult LaunchClient(const std::filesystem::path& root, const std::wstring& arguments, DWORD timeoutMs); }`

- [ ] **Step 1: 写测试与 fixture**

测试统一用一个 `Check`，不要用 `assert`（Release 下 `assert` 会被编译掉）：

```cpp
#include <cstdio>
#include <cstdlib>
static void Check(bool condition, const char* what) {
    if (condition) return;
    std::fprintf(stderr, "FAIL: %s\n", what);
    std::exit(1);
}
```

`tests/fixture.cpp`：解析 `--result N`、`--sleep-ms N`，`Sleep` 后 `return N`。

测试：在临时根建 `client\DFLegacy.Launcher.exe`（复制 fixture 自身，或直接指向 `tests/fixture` 的构建产物），调用 `LaunchClient`：
- fixture `--result 0 --sleep-ms 100` → `started && !earlyFailure && exitCode==0`。
- fixture `--result 3` → `earlyFailure`。
- fixture `--sleep-ms 30000` 且 `timeoutMs=500` → `started && !earlyFailure`（超时视为已启动）。

- [ ] **Step 2: 运行失败**（`LaunchClient` 未定义）。

- [ ] **Step 3: 实现**

```cpp
// launch.cpp
#include "launch.hpp"
#include <windows.h>
namespace dfo {
LaunchResult LaunchClient(const std::filesystem::path& root, const std::wstring& arguments, DWORD timeoutMs) {
    auto exe = root / "client" / "DFLegacy.Launcher.exe";
    std::wstring cmd = L"\"" + exe.wstring() + L"\"";
    if (!arguments.empty()) cmd += L" " + arguments;
    STARTUPINFOW si{ sizeof(si) }; PROCESS_INFORMATION pi{};
    if (!CreateProcessW(exe.c_str(), cmd.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        (root / "client").c_str(), &si, &pi)) return { false, 0, false };
    CloseHandle(pi.hThread);
    auto wait = WaitForSingleObject(pi.hProcess, timeoutMs);
    DWORD code = 0;
    if (wait == WAIT_OBJECT_0) GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hProcess);
    return { true, code, wait == WAIT_OBJECT_0 && code != 0 };
}
}
```
（`CreateProcessW` 的 `lpCurrentDirectory` 用 `client` 的路径；命令行需要可写缓冲区，`cmd.data()` 在 C++17 起可写。）

- [ ] **Step 4: 运行通过**（fixture 路径通过 CTest 参数传入；`add_test` 增加 fixture 路径参数，`stub_tests` 用它复制成临时 client 里的 `DFLegacy.Launcher.exe`）。

- [ ] **Step 5: Commit**
```bash
git add src/DFLegacy.LauncherStub/launch.* src/DFLegacy.LauncherStub/tests
git commit -m "Launch the migrated client and detect early failures"
```

---

### Task 4: 运行中客户端检测与单实例互斥量

**Files:**
- Create: `process.hpp/.cpp`, `mutex` 逻辑并入 `main.cpp`（或 `single_instance.hpp/.cpp`）
- Test: `stub_tests.cpp`

**Interfaces:**
- Produces: `namespace dfo { bool HasRunningClient(const std::filesystem::path& root, DWORD ownPid); bool AcquireSingleInstance(const std::filesystem::path& root); void ReleaseSingleInstance(); }`

- [ ] **Step 1: 写测试**
  - `HasRunningClient`：把 fixture 复制成临时根下 `DFLegacy.Probe.exe`，`--sleep-ms 30000` 启动，断言检测为真；杀掉后断言为假；自身 pid 排除（用一个不在 root 下的进程验证）。
  - `AcquireSingleInstance`：第一次 true，第二次（同进程再次调用）false，释放后 true。

- [ ] **Step 2: 运行失败**。

- [ ] **Step 3: 实现**
  - `HasRunningClient`：`CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)` → `Process32FirstW/NextW` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageNameW` → `PathIsPrefix`（`CompareStringOrdinal` 前缀 + `\\` 边界）比较 root；文件名以 `DFLegacy`/`DNF` 开头（不区分大小写），排除 own pid。
  - `AcquireSingleInstance`：`CreateMutexW(nullptr, TRUE, name.c_str())`，name = `Local\\DFOLauncher-` + 目录路径的 FNV-1a/CRC64 十六进制；`GetLastError()==ERROR_ALREADY_EXISTS` → false（关闭句柄）。

- [ ] **Step 4: 运行通过**。

- [ ] **Step 5: Commit**
  - `git commit -m "Detect running clients and enforce one launcher instance"`

---

### Task 5: 迁移日志（二进制、原子写）

**Files:**
- Create: `journal.hpp/.cpp`
- Test: `stub_tests.cpp`

**Interfaces:**
- Produces: `namespace dfo { struct Journal { std::uint32_t version; std::vector<std::wstring> entries; }; bool WriteJournal(const std::filesystem::path& root, const Journal&, std::wstring& error); std::optional<Journal> ReadJournal(const std::filesystem::path& root, std::wstring& error); void DeleteJournal(const std::filesystem::path& root); }`

- [ ] **Step 1: 写测试**：写 3 个条目 → 读回一致；篡改魔数/长度 → `ReadJournal` 返回 nullopt 且 error 非空；`DeleteJournal` 后读回 nullopt 且 error 为空（不存在视为无日志）。

- [ ] **Step 2: 运行失败**。

- [ ] **Step 3: 实现**
  - 文件 `DFOLauncher.migrate`；格式：`uint32 magic=0x44464F4D ('DFOM')`、`uint32 version=1`、`uint32 count`，每项 `uint32 bytes` + UTF-16LE 字节。
  - 写入 `DFOLauncher.migrate.tmp` → `FlushFileBuffers` → `MoveFileExW(tmp, final, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)`；失败返回 error。
  - 读取时检查大小上限（如 1 MiB）、count 上限（如 10000）、每项长度上限（如 1024 字节）。

- [ ] **Step 4: 运行通过**。

- [ ] **Step 5: Commit**：`git commit -m "Persist the launcher migration journal"`

---

### Task 6: 迁移、校验与回滚

**Files:**
- Create: `migration.hpp/.cpp`
- Modify: `main.cpp`（迁移模式调用）
- Test: `stub_tests.cpp`

**Interfaces:**
- Produces: `namespace dfo { struct MigrationObserver { virtual void OnProgress(std::size_t done, std::size_t total, const std::wstring& name) = 0; }; struct MigrationResult { bool ok; bool rolledBack; std::wstring error; }; MigrationResult Migrate(const std::filesystem::path& root, MigrationObserver& observer); }`（显式/无日志恢复在 Task 8 定义。）

- [ ] **Step 1: 写测试**（临时根造旧布局：入口/App/Updater/Chapter.json/DNF.exe/Script.pvf + `ImagePacks2\a.npk` + `.dflegacy\v4\installed.json`）：
  - `Migrate` 成功 → `client\` 内所有条目存在，外层只剩 `DFOLauncher.exe`（测试里可以只测 `Migrate` 的 root，不要求 stub 存在），日志删除。
  - 失败注入：在 `client\` 里预建同名文件/目录，或把某个顶层文件设为只读/占用（用一个打开的文件句柄 `CreateFileW` 独占），让 `MoveFileW` 失败 → 断言回滚：原布局完整，`client` 删除，日志删除。
  - `RestoreWithoutJournal`（Task 8）成功 → 条目移回外层，`client` 删除；本任务先只实现 `Migrate`，测试里不调用它。

- [ ] **Step 2: 运行失败**。

- [ ] **Step 3: 实现**
  - 列顶层条目：`FindFirstFileW(root\*)`，排除 `.`/`..`、`DFOLauncher.exe`、`DFOLauncher.migrate`、`DFOLauncher.migrate.tmp`。
  - 检查 `client` 不存在；检查 root 和 `client` 不是重解析点（`GetFileAttributesW` 的 `FILE_ATTRIBUTE_REPARSE_POINT`）。
  - 写日志 → 创建 `client` → 逐项 `MoveFileW(root\name, client\name)`；观察者进度；失败：逆序 `MoveFileW(client\name, root\name)`（已移动项），`RemoveDirectoryW(client)`，删日志，返回 rolledBack。
  - 成功后 `ClientComplete(client)`；不完整 → 回滚。
  - 删日志。
  - `Restore`：读日志（或枚举 `client\*`）→ 逆序移回 → 删 `client`/日志；不要求日志（允许显式恢复时日志已被删）——改用 `client` 枚举 + 保留清单（`DFOLauncher.restore` 可选）；本任务按日志实现；显式恢复在 Task 8 处理无日志场景。
  - 所有路径用 `std::filesystem` + `MoveFileW`，错误用 `GetLastError` 转文本。

- [ ] **Step 4: 运行通过**。

- [ ] **Step 5: Commit**：`git commit -m "Migrate the client into the client directory with rollback"`

---

### Task 7: 中断恢复

**Files:**
- Modify: `migration.cpp`（`RecoverIfInterrupted`）、`main.cpp`
- Test: `stub_tests.cpp`

**Interfaces:**
- Produces: `enum class Recovery { None, Completed, RolledBack }; Recovery RecoverIfInterrupted(const std::filesystem::path& root, std::wstring& error);`

- [ ] **Step 1: 写测试**：模拟半迁移（手工把部分条目移进 `client`，写日志）→ `RecoverIfInterrupted` 回滚；把完整客户端移进 `client` 并保留日志 → 返回 `Completed` 并删除日志。

- [ ] **Step 2: 实现**：读日志；若 `ClientComplete(root/client)` → 删日志，Completed；否则按日志逆序移回（日志条目可能还在 root 或已在 client），删 `client`/日志，RolledBack。日志不存在 → None。

- [ ] **Step 3: 测试通过；Commit**：`git commit -m "Recover an interrupted launcher migration"`

---

### Task 8: 启动失败回滚与 `--restore`

**Files:**
- Modify: `main.cpp`、`migration.cpp`
- Test: `stub_tests.cpp`

**Interfaces:**
- Produces: `MigrationResult RestoreWithoutJournal(const std::filesystem::path& root, MigrationObserver& observer);`；`main.cpp` 识别 `--restore`。

- [ ] **Step 1: 写测试**：已迁移布局（`client\` + 完整客户端，无日志）→ `RestoreWithoutJournal` 把条目移回外层、删 `client`。
- [ ] **Step 2: 实现**：`RestoreWithoutJournal` 枚举 `client\*`，逐项 `MoveFileW(client\name, root\name)`，逆序回滚（若失败移回 client）；成功后 `RemoveDirectoryW(client)`。
- [ ] **Step 3: 测试通过；Commit**：`git commit -m "Restore the previous layout after a failed launch"`

---

### Task 9: 经典风格 UI（确认、进度、错误）

**Files:**
- Create: `ui.hpp/.cpp`
- Modify: `CMakeLists.txt`（链接 `../DFLegacy.Startup/startup_prompt.cpp` 和配置 `startup_prompt.rc`、`classic-message-*.png`）、`main.cpp`
- Test: 手动（截图）

**Interfaces:**
- Produces: `namespace dfo { bool Confirm(HINSTANCE, const wchar_t* body); void Error(HINSTANCE, const wchar_t* body); class ProgressWindow; }`

- [ ] **Step 1: 接入经典提示**：CMake 配置 `../DFLegacy.Startup/startup_prompt.rc.in`（`DFL_CLASSIC_SKIN` 指向 `../DFLegacy.Launcher/Assets/Skin`），编译 `../DFLegacy.Startup/startup_prompt.cpp`。`ui.cpp` 复制它的 `ShowStartupPrompt` 为 `ShowClassicConfirm(HINSTANCE, const wchar_t* body, bool* confirmed)`：确认按钮置 `*confirmed=true`，`WM_CLOSE` 置 false；`Error` 仍用原 `ShowStartupPrompt`。
- [ ] **Step 2: 进度窗口**：`ProgressWindow` 创建约 420×140 的窗口，画 `classic-message-panel.png`（GDI+），中间显示“正在整理客户端（n/m）”和当前文件名，底部一个进度条矩形；迁移在工作线程跑，`PostMessage(WM_APP_PROGRESS)` 更新；完成后 `DestroyWindow`。迁移很快，窗口延迟 200ms 显示。
- [ ] **Step 3: 手动验证**：用真实旧布局副本运行，确认窗口风格、文案、进度、确认/取消、错误提示；截图存入验收资产。
- [ ] **Step 4: Commit**：`git commit -m "Add the classic launcher migration UI"`

---

### Task 10: 快捷方式改指向

**Files:**
- Create: `shortcut.hpp/.cpp`
- Test: `stub_tests.cpp`（COM + 临时 .lnk）

**Interfaces:**
- Produces: `namespace dfo { std::size_t RetargetShortcuts(const std::wstring& oldTarget, const std::filesystem::path& newTarget); }`

- [ ] **Step 1: 写测试**：`CoInitializeEx`；用 `IShellLinkW` 在临时目录创建指向 `oldEntry` 的 .lnk；调用 `RetargetShortcuts`；重新读取 .lnk，断言目标 == stub 路径、工作目录 == stub 目录。
- [ ] **Step 2: 实现**：`SHGetKnownFolderPath(FOLDERID_Desktop/FOLDERID_Programs/FOLDERID_CommonPrograms)`；递归枚举 `*.lnk`；`IShellLinkW::GetPath` 与 `oldTarget` 比较（`CompareStringOrdinal` 忽略大小写）；匹配则 `SetPath(newTarget)`、`SetWorkingDirectory(newTarget.parent_path())`、`IPersistFile::Save`；返回修改数量；任何单个失败继续并汇总。
- [ ] **Step 3: 测试通过；Commit**：`git commit -m "Retarget old launcher shortcuts to DFOLauncher"`

---

### Task 11: 打包脚本（新布局启动包）

**Files:**
- Create: `tools/release/launcher_layout.py`
- Create: `tools/release/tests/test_launcher_layout.py`
- Test: pytest

**Interfaces:**
- Produces: `python tools/release/launcher_layout.py --client <dir> --stub <exe> --output <zip>`；返回 0，输出 zip 的 SHA-256；校验失败返回非零。

- [ ] **Step 1: 写失败测试**：合成一个 `client/.dflegacy/v4/installed.json`（`release.manifestBytes` = base64 的 2 文件清单：`DNF.exe`、`DFLegacy.Chapter.json`），生成对应文件；跑脚本；用 `zipfile` 断言 zip 含 `DFOLauncher.exe` 和 `client/DNF.exe`；改坏一个客户端文件后脚本失败。
- [ ] **Step 2: 实现**：读收据 → base64 解码 `release.manifestBytes` → 对每个 `files` 校验 `client/<path>` 的 size/sha256；`total` 上限/路径校验（`PathPolicy` 风格：拒绝绝对路径/`..`）；zip 写入 `DFOLauncher.exe` + 遍历 client；打印 `sha256` 和文件数。
- [ ] **Step 3: 测试通过；Commit**：`git commit -m "Package the DFOLauncher client layout"`

---

### Task 12: 集成验收（真实 v1.0.13-r2 副本）

**Files:**
- Create: `knowledge/decisions/2026-11-17-dfo-launcher-layout-acceptance.md`
- Test: 手工/脚本

- [ ] **Step 1: 准备**：复制一份真实 v1.0.13-r2 固定安装（如 `G:/tmp/c2r2-github` 或 `c2r2-normal`）到新目录，确认收据 `v1.0.13-r2`/seq21、登录可用。
- [ ] **Step 2: 迁移**：把 `DFOLauncher.exe` 放进该目录，运行；确认迁移窗口、`client\` 布局、外层只剩 stub + client。
- [ ] **Step 3: 验证**：启动客户端 → 登录页 `v1.0.13`；收据/哈希不变；账号/显示/`DNF_CHINA.cfg` 保留；进入游戏；更新检查正常（无新版本时通过）；日志无异常。
- [ ] **Step 4: 故障注入**：迁移中途结束 stub → 下次启动恢复；模拟客户端启动失败（改坏 client 入口/用 fixture）→ 回滚提示与 `--restore` 生效。
- [ ] **Step 5: 新包**：用打包脚本生成新布局 zip → 解压 → 运行 stub → 登录。
- [ ] **Step 6: 记录**：把命令、截图、哈希、结果写入验收记录并提交。

---

### Task 13: 发行 `v1.0.13-r3`

**Files:**
- Create: `docs/releases/v1.0.13-r3.md`
- Modify: publish tooling (reuse `update_v4`/`gh` pattern)

- [ ] **Step 1: 发行说明**：迁移/全新安装步骤、布局说明、未签名与 SHA-256、已知边界、内部改名推迟到 v1.0.14。
- [ ] **Step 2: 源码提交**：stub 项目、构建/打包脚本、发行说明、设计/验收文档提交到公共仓库；`main` 推进。
- [ ] **Step 3: 附件**：`DFOLauncher.exe`（GitHub + GitCode，匿名回读）；新布局启动包只上 GitCode（GitHub 限流期）；SHA256SUMS。
- [ ] **Step 4: release 标签** `v1.0.13-r3` 指向源码提交；预发布/正式按发行说明；验证 tag 目标、附件身份、匿名下载。
- [ ] **Step 5: 通知/交接**：更新 `knowledge/decisions/` 验收、设计状态；把公司电脑迁移步骤写给用户。

---

## Self-Review

- Spec coverage：布局（Task 1/2/6）、启动/迁移/回滚/恢复/restore（Task 3/4/6/7/8）、UI（Task 9）、快捷方式（Task 10）、打包（Task 11）、发行（Task 13）、验收（Task 12）、文档（Task 13）；内部改名延后（记录于 spec，不在本计划）。
- 占位符：无 TBD/TODO；每个任务给出具体文件、接口、测试和关键代码；UI 绘制参考 `startup_prompt.cpp`，需要实现时复制其皮肤资源。
- 类型一致性：`Layout`/`ClientComplete`/`LaunchResult`/`Journal`/`MigrationResult`/`Recovery`/`RetargetShortcuts` 在后续任务中名称一致。
- 未覆盖但 spec 要求：`client` 目录可见（Task 13 发行说明写明）；未签名（Task 13）；GitHub 限流（Task 13）。
