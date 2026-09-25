# DFLegacy 构建指南

本文档说明如何从源码构建 DFLegacy 的各个组成部分，以及各平台的能力边界。
部署、配置与升级流程见 [DEPLOYING.md](DEPLOYING.md)；模块职责见 [README.md](README.md)。

## 1. 平台与工具链总览

| 项目 | 目标框架 | 可构建平台 | 需要的工具链 |
| --- | --- | --- | --- |
| `DFLegacy.Server` | `net10.0` | Windows、Linux、macOS | .NET 10 SDK |
| `DFLegacy.Protocol` | `net10.0` | Windows、Linux、macOS | .NET 10 SDK |
| `DFLegacy.Relay` | `net10.0` | Windows、Linux、macOS | .NET 10 SDK |
| `tests/DFLegacy.SmokeTests` | `net10.0` | Windows、Linux、macOS | .NET 10 SDK |
| `DFLegacy.Launcher` | `net10.0-windows`（WinForms） | 仅 Windows | .NET 10 SDK + PowerShell 7 + CMake + VS C++ |
| `DFLegacy.Ijl15` | 原生 C++17，32 位 | 仅 Windows | VS C++ + CMake ≥ 3.24 |
| `DFLegacy.RandomNative` | 原生 C++17，64 位 | 仅 Windows | VS C++ + CMake ≥ 3.24 |

两个原生项目在各自的 `CMakeLists.txt` 开头就强制了平台与指定位宽，非 Windows 或位宽不符会直接报错：

- `src/DFLegacy.Ijl15/CMakeLists.txt`：`NOT WIN32` 报错；必须 `-A Win32`（32 位，因为要替换客户端的 `ijl15.dll`）。
- `src/DFLegacy.RandomNative/CMakeLists.txt`：`NOT WIN32` 报错；必须 `-A x64`（64 位，配合 64 位服务端进程）。

服务端本身**没有强制性的原生依赖**：`DFLegacy.RandomNative.dll` 只用于 RDSEED 硬件随机，由 `GameRandomSource` 在 Windows + 64 位进程下探测加载，缺失或加载失败时回退到 `RandomNumberGenerator` / `Random.Shared`。因此 Linux 与 macOS 上可以只构建服务端；也可以直接用容器构建，见 [DEPLOYING.md §5](DEPLOYING.md#5-容器部署docker--podman)。

## 2. 前置条件

### 2.1 全部平台

- **.NET 10 SDK**。运行服务端还需要 ASP.NET Core 10 运行时（安装 SDK 即包含）。

### 2.2 Windows：原生模块与启动器

- **Visual Studio**（2026 / 2022 / 2019），安装"使用 C++ 的桌面开发"工作负载。
- **CMake 3.24 或更新版本**，且能生成 Visual Studio 工程（构建脚本会自动在 `Visual Studio 18 2026` / `17 2022` / `16 2019` 中挑选可用的生成器）。
- **PowerShell 7（`pwsh`）**：构建脚本与启动器发布都通过 `pwsh` 调用。

### 2.3 Linux / macOS

- **.NET 10 SDK**。不构建原生模块与启动器，因此不需要 C++ 工具链。

## 3. Windows：完整构建（推荐）

在仓库根目录打开 **Visual Studio 开发者 PowerShell**，构建混合解决方案：

```powershell
msbuild .\DFLegacy.Emulator.slnx /m /restore /p:Configuration=Release
```

若 `msbuild` 不在 `PATH`（未打开开发者命令行），可直接用完整路径，例如
`"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"`。

在 **Git Bash** 里执行时，MSBuild 开关必须用 `-` 前缀（`-m -restore -p:Configuration=Release`）。写 `/m`、`/restore` 会被 MSYS 当成路径转换成 `M:/`、`D:/Program Files/Git/restore`，报 `MSB1008: 只能指定一个项目`。

`DFLegacy.Emulator.slnx` 同时包含两个 `.vcxproj`，只有 .NET Framework 版 MSBuild（即 Visual Studio 自带的 `msbuild`）能构建它们；`dotnet build` 不能，原因见 [§8](#8-常见构建问题)。

服务端项目在 Visual Studio / `msbuild` 下会把原生随机桥作为项目引用一起构建（`DFLegacy.Server.csproj` 中的 `BuildRandomNativeReference` 条件为 `'$(MSBuildRuntimeType)' == 'Full'`），并把产物复制到输出目录。

原生产物固定落在：

```text
artifacts/native/ijl15/Release/ijl15.dll                      # 32 位客户端补丁
artifacts/native/random/Release/DFLegacy.RandomNative.dll     # 64 位硬件随机桥
```

## 4. Windows：只构建服务端

只需要服务端时，用 .NET SDK 即可，不依赖 Visual Studio 的 C++ 组件：

```powershell
dotnet build .\src\DFLegacy.Server\DFLegacy.Server.csproj -c Release
```

发布到目录：

```powershell
dotnet publish .\src\DFLegacy.Server\DFLegacy.Server.csproj -c Release -o .\dist\DFLegacy.Server
```

两点说明：

- 这种构建方式**不会**构建原生随机桥，服务端自动回退到托管随机。若此前已经用 §3 或 §7 生成了 `artifacts/native/random/Release/DFLegacy.RandomNative.dll`，发布时会自动带上它（csproj 里的 `Content` 条件为"文件存在或处于完整 MSBuild"）。
- 发布目录会生成 `web.config`，那是 `Microsoft.NET.Sdk.Web` 的默认产物。服务端只用 Kestrel，不注册 IIS/HTTP.sys，该文件在非 IIS 部署下不生效，可忽略。

## 5. Linux 与 macOS：构建并发布服务端

```bash
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release
dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -o ./dist/DFLegacy.Server
```

需要独立运行时（目标机没装 .NET 10）或需要跨平台发布时，显式指定 RID：

```bash
# 在目标平台上直接发布（框架依赖）
dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -o ./dist/DFLegacy.Server

# 自包含，不要求目标机安装 .NET
dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release \
  -r linux-x64 --self-contained true -o ./dist/DFLegacy.Server
```

可用的服务端 RID：`linux-x64`、`linux-arm64`、`osx-x64`（Intel Mac）、`osx-arm64`（Apple 芯片）。在 Windows 上也可以交叉发布这些目标，产物直接拷到目标机运行。

注意：

- **不要用 `DFLegacy.Emulator.slnx`**。它包含两个 `.vcxproj`，Linux 与 macOS 上的 .NET MSBuild 无法构建（参见 §8）。
- 发布产物包含当前平台的应用宿主（Linux 上为 ELF、macOS 上为 Mach-O 可执行文件 `DFLegacy.Server`），因此既可以直接运行它，也可以用 `dotnet DFLegacy.Server.dll` 启动。交叉发布时必须指定 `-r`。
- 原生随机桥与客户端补丁均为 Windows 专属，Linux 与 macOS 上不存在也不需要。
- macOS 自带 ICU 与时区数据，无需额外安装；Linux 的最小化系统需要，见 [DEPLOYING.md §4](DEPLOYING.md#4-linux-与-macos-无头服务端部署)。

## 6. 启动器与原生模块

### 6.1 启动器（仅 Windows）

```powershell
dotnet publish .\src\DFLegacy.Launcher\DFLegacy.Launcher.csproj -c Release -o .\dist\DFLegacy.Server
```

发布过程会通过 `BuildIjl15` 目标调用 `pwsh` 执行 `src/DFLegacy.Ijl15/build-native.ps1`，因此这一步**必须**具备 PowerShell 7、CMake 和 Visual Studio C++ 生成器；缺失会在发布时报错，而不是静默跳过。

发布产物包含启动器本体、`launcher.json`、`Config.ini`、`DFLegacy.Relay.Core.dll` 和 32 位 `ijl15.dll`。按 [README](README.md#构建与启动) 的默认做法，启动器与服务端发布到同一目录即可。

`launcher.json` 的 `Relay`（见 [DEPLOYING.md §6.4](DEPLOYING.md#64-让-ipv4-客户端连接-ipv6-服务器dflegacyrelay)）由启动器在自身进程内运行，中继核心作为项目引用自动随发布输出，因此无需额外步骤。只有不使用启动器、要单独运行中继时，才需要额外发布那个独立可执行文件：

```powershell
dotnet publish .\src\DFLegacy.Relay\DFLegacy.Relay.csproj -c Release -o .\dist\DFLegacy.Server
```

### 6.2 原生模块单独构建

两个脚本都支持 `-Configuration Debug|Release` 与可选的 `-RunTests`：

```powershell
# 32 位客户端补丁 -> artifacts/native/ijl15/<Configuration>/ijl15.dll
pwsh .\src\DFLegacy.Ijl15\build-native.ps1 -Configuration Release

# 64 位硬件随机桥 -> artifacts/native/random/<Configuration>/DFLegacy.RandomNative.dll
pwsh .\src\DFLegacy.RandomNative\build-native.ps1 -Configuration Release

# 顺带跑原生单元测试
pwsh .\src\DFLegacy.RandomNative\build-native.ps1 -Configuration Release -RunTests
```

脚本支持通过 `CMAKE_EXE` 环境变量指定 `cmake` 路径；未设置时依次查找 `%ProgramFiles%\CMake\bin\cmake.exe` 与 `PATH` 中的 `cmake.exe`。若构建目录里缓存的生成器与当前可用的生成器不一致，脚本会先删除 `artifacts/native/...` 下的构建目录再重新配置。

## 7. 发布产物一览

| 发布目标 | 命令 | 主要产物 |
| --- | --- | --- |
| 服务端 | `dotnet publish src/DFLegacy.Server/DFLegacy.Server.csproj -c Release -o <dir>` | `DFLegacy.Server.dll` / `.exe`、`DFLegacy.Protocol.dll`、`server.json`、`wwwroot/`、`web.config`；若已构建则含 `DFLegacy.RandomNative.dll` |
| 启动器 | `dotnet publish src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -o <dir>` | `DFLegacy.Launcher.exe`、`DFLegacy.Protocol.dll`、`launcher.json`、`Config.ini`、`ijl15.dll` |
| 客户端中继 | `dotnet publish src/DFLegacy.Relay/DFLegacy.Relay.csproj -c Release -o <dir>` | `DFLegacy.Relay.exe` / `.dll`、`DFLegacy.Relay.deps.json`、`DFLegacy.Relay.runtimeconfig.json` |

`server.json` 与 `launcher.json` 都以 `PreserveNewest` 方式复制，源文件比目标新时会覆盖目标。反复发布到同一个目录前请先备份这两个文件，详见 [DEPLOYING.md](DEPLOYING.md#10-升级流程)。

## 8. 常见构建问题

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `MSB4278: 导入的文件 "$(VCTargetsPath)\Microsoft.Cpp.Default.props" 不存在` | 用 `dotnet build` 构建了 `.vcxproj`（直接指定，或通过 `.slnx`） | 改用 Visual Studio 开发者 PowerShell 里的 `msbuild`；或只构建 `.csproj`（`dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj`） |
| 启动器发布失败：`pwsh` 不是内部或外部命令 | 启动器的 `BuildIjl15` 目标依赖 PowerShell 7 | 安装 PowerShell 7，或改为在 Visual Studio 中构建整个解决方案 |
| 构建脚本报 `CMake 3.24 or newer is required` | CMake 未安装或版本过低 | 安装 CMake ≥ 3.24，或用 `CMAKE_EXE` 指定路径 |
| 构建脚本报 `A Visual Studio C++ CMake generator is required` | 未安装 Visual Studio C++ 生成器 | 在 Visual Studio 安装程序里勾选"使用 C++ 的桌面开发" |
| 服务端启动日志提示随机源回退 | 未提供或无法加载 `DFLegacy.RandomNative.dll`（Linux、32 位进程、CPU 不支持 RDSEED 等） | 属正常回退，不影响玩法；需要硬件随机时在 Windows 64 位环境按 §3/§6.2 构建并随服务端一起发布 |
| 发布后 `server.json` 的 PVF 路径变回示例值 | `PreserveNewest` 复制覆盖了本机配置 | 发布前备份 `server.json`，发布后改回本机路径 |

## 9. 测试

冒烟测试覆盖协议编解码、PVF 数据解析与主要业务规则：

```powershell
dotnet run --project .\tests\DFLegacy.SmokeTests\DFLegacy.SmokeTests.csproj -c Release
```

默认使用内置的临时 fixture 数据，可在任意平台运行。把环境变量 `DF2008_REAL_PVF_PATH` 指向真实的 `Script.pvf`，会额外启用一批"真实数据"检查：

```powershell
$env:DF2008_REAL_PVF_PATH = 'D:\DFLegacy\DF2008\Script.pvf'
dotnet run --project .\tests\DFLegacy.SmokeTests\DFLegacy.SmokeTests.csproj -c Release
```

这些检查针对特定 PVF 版本编写：PVF 版本不一致时，涉及文件清单、商城价格、宠物进化等条目可能失败，属数据差异而非构建问题；判定时要结合失败项的具体名称。

原生模块自带测试，通过构建脚本的 `-RunTests` 开关运行（见 §6.2）。
