# DFLegacy 构建与发行

本仓库提供独立 Web Manager、游戏 Server、可选 Channel、Windows WPF 登录器和 Windows 原生补丁。部署发行包不需要安装 .NET；源码构建使用 `global.json` 固定的 .NET SDK 10.0.402（允许同一 feature band 的补丁更新），Python 3.12+，以及 Git。操作系统仍需满足 [.NET 系统依赖](https://learn.microsoft.com/dotnet/core/install/)；Linux 需要 ICU 与时区数据。

发行环境变量与 Docker build args 使用 `DFLEGACY_RELEASE_VERSION`、`DFLEGACY_RELEASE_COMMIT`。不要将 `v1.2.3` 一类标签放入通用 `VERSION`：MSBuild 会忽略环境属性名大小写并把它当作 NuGet Version，导致 restore 失败。发行工具启动构建子进程时会清除遗留的该变量；step output 的 `version`/`commit` 名称不受影响。

| 产物 | 构建主机与额外工具 | 首版运行验证范围 |
| --- | --- | --- |
| win-x64 | Windows x64、VS C++、CMake 3.24+、PowerShell 7 | Windows 管理器及原生测试；真实客户端另做手工验收 |
| linux-x64 | Linux x64 + .NET SDK | 管理器 HTTP、子进程、解压包及容器检查 |
| linux-arm64、osx-x64、osx-arm64 | Linux 上交叉 publish | 摘要、依赖和归档模式；不代表目标机器实测 |
| OCI linux/amd64、linux/arm64 | Linux、Docker buildx，预先配置多架构 builder/QEMU | amd64 容器检查；arm64 仅构建时需明确标注 |
| Windows 客户端资源 | Windows 原生工具链与 WPF | 同一提交构建一次，复用于所有包和镜像 |

## 本地测试

在仓库根目录执行；默认冒烟使用可生成的 fixture，不依赖私有 PVF：

```powershell
dotnet run --project tests/DFLegacy.SmokeTests -c Release
dotnet run --project tests/DFLegacy.SmokeTests -c Release -- --deployment-hosting
dotnet run --project tests/DFLegacy.Manager.Tests -c Release -- --http --server-dir "$PWD/src/DFLegacy.Server/bin/Release/net10.0"
py -3.12 tools/release/release.py test
```

Linux 将最后一行改为 `python3 tools/release/release.py test`。测试入口是控制台程序，必须实际 `dotnet run` 并检查退出码；仅运行 `dotnet test` 不能证明这些测试已执行。Manager 的 `--server-dir` 必须指向已构建的 Server 目录；前一条默认冒烟会构建相应程序。

现有无客户端 Python 工具测试也属于 CI 必过项；测试依赖（含传递依赖）固定版本与 PyPI wheel SHA-256，发行 CLI 本身仍只使用标准库。在独立虚拟环境运行：

```powershell
py -3.12 -m venv artifacts/python-test-env
./artifacts/python-test-env/Scripts/python.exe -m pip install --require-hashes --only-binary=:all: -r tools/release/requirements-ci.txt
$env:PYTHONPATH = Join-Path $PWD 'tools'
./artifacts/python-test-env/Scripts/python.exe -m pytest tools/acceptance/tests tools/debug/test_drop_control.py -q
```

Linux 使用 `python3 -m venv` 与虚拟环境的 `bin/python`，运行 pytest 时设 `PYTHONPATH="$PWD/tools"`。这些测试只使用临时文件、模拟进程和本机 socket，不需要游戏客户端；端口或环境错误应修复或如实报告，不能用跳过用例制造通过结果。

Windows 原生与登录器行为测试：

```powershell
pwsh -NoProfile -File src/DFLegacy.Ijl15/build-native.ps1 -Configuration Release -RunTests
pwsh -NoProfile -File src/DFLegacy.RandomNative/build-native.ps1 -Configuration Release -RunTests
$ijl15 = Join-Path $PWD 'artifacts/native/ijl15/Release/ijl15.dll'
dotnet run --project src/DFLegacy.Launcher/tests/DFLegacy.Launcher.Tests.csproj -c Release -p:BuildNativeAssets=false "-p:Ijl15PrebuiltPath=$ijl15"
```

IJL15 的 CMake 测试也构建和测试 `debug.dll`。32 位 IJL15/Debug 供游戏客户端使用，64 位 RandomNative 供 Windows Server 可选使用；后者不可用时服务端使用托管随机源。真实 PVF 专项、跨机器连接和图形客户端进图不是普通无资源 CI 的替代项，必须单独记录结果。

## ACT1 原生调试专项与历史验收

原生跨语言测试现在有可再生的专用夹具，见 [JSON 导出](tests/fixtures/native-debug/README.md)、[有界原始指令](tests/fixtures/act1-motion-horizon/README.md)和[城镇脚本子集](tests/fixtures/act1-town-navigation/README.md)。默认 Python 回放不需要安装游戏或提供完整 PVF。需要执行实际编译 DLL 的探针时，先构建 Windows 原生项目并设置 `DFLEGACY_GAMEPLAY_PROBE_DLL`、`DFLEGACY_NATIVE_LOOP_DLL` 为本次 `ijl15.dll`。

Windows CI 在原生构建后重新生成 JSON 夹具、核对实际安装的运动补丁，再运行完整 Python 套件；JUnit 中出现任何跳过都会使该门禁失败。CI 为原生城镇控制器设置 `DFL_TOWN_ROUTE_CONTROLLER_PVF`，指向有界 PVF 子集；本机也可通过此变量选择子集或原始资源。夹具只证明离线原始指令、解析、模型和序列化合同，不扩充历史实机验收结论。

当前源码、实机构建归属与未闭合边界见[交接](HANDOFF-2026-10-30.md)。合并前该分支记录的完整 Python 为 620 项／569 子测试；此数字对应原始 ACT1 及所需原生导出夹具均已提供的本机环境，不表示新机器缺少数据时条件用例也已执行，也不替代合并结果的验证。

```powershell
$env:PYTHONPATH = Join-Path (Get-Location) 'tools'
python -B -m pytest -q
MSBuild.exe .\src\DFLegacy.Debug\tests\DFLegacy.Debug.Tests.vcxproj /m /p:Configuration=Release /p:Platform=Win32
.\artifacts\native\debug-tests\Release\debug_tests.exe --client-state
```

`--client-navigation`、`--client-party`、`--town-route`、`--frame-profile`、`--gameplay-trace`、`--native-loop` 可分别运行模型／IO 回归。真实城镇控制器通过 `DFL_TOWN_ROUTE_CONTROLLER_PVF=<原始Script.pvf>` 提供原 PVF，完整 overlay 测试还需 C# 生成的 fixture，见 [debug 构建验证](tools/debug/README.md)。缺夹具的 skip 必须单列；自动化回归不要求重开已完成的实机矩阵。

合并前 [2026-10-30 门前终点修复](knowledge/decisions/2026-10-30-dungeon-gate-map-target-verification.md)记录的 debug 为 `0082559C…`，IJL 为 `0103C7DC…`／ABI v2；这些是历史实载身份，不是新合并构建的身份。

## 构建不可变 Windows 客户端资源

发行目录必须是新目录。先从同一个干净提交构建上述 Windows 原生项目，使用同一输出目录中的独立补丁、引导器和调试模块。登录器必须以自包含单文件形式发布；引导器内嵌，工具校验运行时、WPF 与内嵌 `.deps.json` 引用的依赖，替换发行配置并生成 SHA-256 清单。

```powershell
$env:DFLEGACY_RELEASE_COMMIT = (git rev-parse HEAD).Trim()
$env:DFLEGACY_RELEASE_VERSION = 'v1.2.3' # 正式版必须对应同名标签；开发版用下述 metadata 生成
$nativeOutput = Join-Path $PWD 'artifacts/native/ijl15/Release'
$clientPatch = Join-Path $nativeOutput 'DFLegacy.Client.dll'
$clientBootstrap = Join-Path $nativeOutput 'DFLegacy.Bootstrap32.exe'
$debug = Join-Path $nativeOutput 'debug.dll'
$launcherOutput = Join-Path $PWD ("artifacts/launcher-" + $env:DFLEGACY_RELEASE_VERSION)
dotnet publish src/DFLegacy.Launcher/DFLegacy.Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -p:PublishTrimmed=false -p:BuildNativeAssets=false "-p:ClientPatchPrebuiltPath=$clientPatch" "-p:ClientBootstrapPrebuiltPath=$clientBootstrap" -p:ContinuousIntegrationBuild=true -o $launcherOutput
py -3.12 tools/release/release.py build-client-assets --launcher-output $launcherOutput --client-patch $clientPatch --debug $debug --config src/DFLegacy.Ijl15/Config.ini --version $env:DFLEGACY_RELEASE_VERSION --commit $env:DFLEGACY_RELEASE_COMMIT --output client-assets
py -3.12 tools/release/release.py verify client-assets --client-assets --version $env:DFLEGACY_RELEASE_VERSION --commit $env:DFLEGACY_RELEASE_COMMIT
py -3.12 tools/release/release.py build-client-packages --client-assets client-assets --output artifacts --version $env:DFLEGACY_RELEASE_VERSION --commit $env:DFLEGACY_RELEASE_COMMIT
```

容器要生成两套玩家包时，再以同一参数发布 `-r win-x86` 到独立目录，并将该目录通过 `--launcher-x86-output` 传给 `build-client-assets`。CI 会构建并验证两个架构。x86 与 x64 登录器都使用同一次构建的 32 位原生补丁与引导器。

完整四附件发行必须提供同次原生构建的 `--debug` 资源：`DFLegacy-client-update-v2-win-x64.zip`、`DFLegacy-client-update-v2-test-win-x64.zip`、`DFLegacy-client-tools-win-x64.zip`、`DFLegacy-client-tools-test-win-x64.zip`。普通包只包含登录器和独立补丁，测试包另外包含同版本 `debug.dll`；普通包不会因资源目录存在 debug 而包含或启用它。测试模式增加本地诊断，自动输入和其他测试控制仍需显式开启。工具包包含安装清单、摘要和迁移说明，不含玩家配置或游戏文件。

完整登录器模板账号、密码与账号列表为空，`StartServer=false`，`ClientPath=DNF.exe`。不得把游戏客户端、PVF、存档、个人路径、凭据或 CI secret 放入资源。若只准备普通版管理器资源，可省略 `--debug`；此时不能构建完整四附件或导出测试包。

同一个干净提交的自动化入口为：

```powershell
$env:BUILD_COMMIT = (git rev-parse HEAD).Trim()
$env:BUILD_REF = 'refs/heads/main'
py -3.12 tools/release/ci.py metadata
# metadata 显示 dev-<提交UTC时间>-<SHA前12位>，CI也写入环境与step outputs
py -3.12 tools/release/ci.py windows --version <显示的版本> --commit $env:BUILD_COMMIT
```

`windows` 入口实际运行原生和登录器测试，构建 Windows 发行包并启动解压后的 Manager 检查 `/health` 与停服时 `/ready=503`，生成四个公开客户端附件及 `artifacts/client-assets.tar.gz`，后者用于跨主机传输。不要在同一个工作目录重复调用；保留输出后换干净检出或新目录。

## 五个平台的自包含发行包

```powershell
py -3.12 tools/release/release.py publish --rid win-x64 --version $env:DFLEGACY_RELEASE_VERSION --commit $env:DFLEGACY_RELEASE_COMMIT --client-assets client-assets --output artifacts/DFLegacy-v1.2.3-win-x64 --verification 'Windows构建；尚未做真实客户端验收'
py -3.12 tools/release/release.py verify artifacts/DFLegacy-v1.2.3-win-x64.zip --version $env:DFLEGACY_RELEASE_VERSION --commit $env:DFLEGACY_RELEASE_COMMIT
```

RID 支持 `win-x64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。Linux/macOS 使用 `.tar.gz` 保留执行位；Windows 使用 ZIP。工具默认调用 `dotnet publish --self-contained true` 构建 Manager、Server、Channel，分发相同版本的 Windows 资源；可通过 `--manager-output`、`--server-output`、`--channel-output` 提供事先发布的完整目录。验证针对最终归档，也检查相同版本/提交、文件摘要、依赖与安全模板。摘要不替代签名或信任来源。

发行根目录包含 Manager 运行时和 `wwwroot`、`runtime/server/`、`runtime/channel/`、`client-assets/`、`templates/`、`resources/README.txt`、三种启动脚本与 `release-manifest.json`。不包含 `data/` 或 `Script.pvf`；不能向正在使用的安装目录 publish。归档和目录已存在时直接拒绝覆盖。

在 Linux 干净检出上接收同 SHA 的 Windows artifact 后：

```bash
python3 tools/release/ci.py linux --version "$DFLEGACY_RELEASE_VERSION" --commit "$DFLEGACY_RELEASE_COMMIT" --assets-archive /absolute/client-assets.tar.gz
```

它安全解包并校验资源，生成其他四个平台的归档，实际启动解压后的 linux-x64 管理器。macOS 包未签名、未公证；首次运行策略与系统依赖见 [部署指南](DEPLOYING.md)。本机没有相应运行环境时，清单应写“交叉构建、未运行验证”，不要改成实机通过。

## 容器构建

根目录需存在已校验的 `client-assets/`，版本/提交与构建参数必须一致。Dockerfile 的恢复层包括 Manager、Server、Channel、Protocol 与 InterProcess 项目引用，镜像内不编译 WPF/Windows C++。

```bash
export SOURCE_DATE_EPOCH="$(git show -s --format=%ct "$DFLEGACY_RELEASE_COMMIT")"
docker buildx build --platform linux/amd64 --build-arg DFLEGACY_RELEASE_VERSION="$DFLEGACY_RELEASE_VERSION" --build-arg DFLEGACY_RELEASE_COMMIT="$DFLEGACY_RELEASE_COMMIT" --build-arg SOURCE_DATE_EPOCH="$SOURCE_DATE_EPOCH" --provenance=false --sbom=false --load -t dflegacy:local .
docker buildx build --platform linux/amd64,linux/arm64 --build-arg DFLEGACY_RELEASE_VERSION="$DFLEGACY_RELEASE_VERSION" --build-arg DFLEGACY_RELEASE_COMMIT="$DFLEGACY_RELEASE_COMMIT" --build-arg SOURCE_DATE_EPOCH="$SOURCE_DATE_EPOCH" --provenance=false --sbom=false --output type=oci,dest=artifacts/dflegacy-image.oci.tar,rewrite-timestamp=true .
```

多架构 builder、QEMU 与 Docker daemon 由部署者预先提供，脚本不更改 Docker 全局设置。基础镜像显式使用 Ubuntu 24.04 Noble（[.NET 10 默认发行版说明](https://learn.microsoft.com/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu)），安装系统 Python 并断言至少 3.12。只有 Docker 客户端可用不能证明镜像构建或运行验收完成。

SDK stage 使用构建主机平台，通过目标 RID 交叉发布；runtime stage 使用目标平台，避免在 arm64 模拟环境下运行整套 SDK 编译。

CI 使用 [Git 提交时间固定 BuildKit 时间戳](https://docs.docker.com/build/ci/github-actions/reproducible-builds/)，并关闭非确定性的 provenance/SBOM 附件、清理 apt 动态日志、规范化 ASP.NET 生成的 HTTP Last-Modified。重试发布应复用原先通过门禁的 artifacts；基础镜像、apt 或 NuGet 内容变动会改变构建输入，此时完整 inventory 预留应拒绝覆盖已有版本。

## 源码与产物发布

Gitea 接收源码并运行 [只读测试](.gitea/README.md)。Windows、Linux 和 macOS 的独立服务端归档可手工构建。

在干净的发布检出中，使用 `python tools/release/ci.py windows --client-only --version <版本> --commit <提交>` 构建并测试 Windows 客户端资源。使用生成的 `client-assets` 从同一提交构建 Linux amd64/arm64 OCI 镜像，按上文命令推送到 `ghcr.io/firekula/dflegacy`。

传给 `python tools/release/github_publish.py --updates-only --version <版本> --commit <提交>` 四个 `--archive` 参数，分别指向 `DFLegacy-client-update-v2[-test]-win-{x64,x86}.zip`。发布器校验普通／测试类型、程序摘要和共同补丁，完整上传后公开并匿名回读。玩家使用管理后台生成的对应架构安装包接入游戏。


## Windows 离线客户端打包器

```powershell
dotnet publish src/DFLegacy.ClientPackager -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/client-packager
```

运行输出目录里的 `DFLegacy.ClientPackager.exe`。发行资源目录选择含双架构 `client-assets/manifest.json` 的上一级目录。资源可由上文的 `ci.py windows --client-only` 生成，也可从匹配的发行镜像中提取。首次使用可导入部署数据目录下的 `config` 文件夹，其中有 `server.json` 和 `manager.json`。Compose 覆盖值以线上实际配置为准。填写连接地址、账号 API、端口与中继偏好后，可保存为 JSON 方案。

「仅生成」可离线生成普通版或测试版安装包，内含 x86 和 x64 两套 ZIP。无需 Script.pvf、部署管理服务或 Python。「生成并发布到 GitHub」另外生成四个更新 ZIP，使用 Python 3.12 和 GitHub 发布凭据，复用发布器草稿与摘要校验流程。请保留输出目录内的 `tools/release`，并为 Git Credential Manager 配好可写凭据，或在启动进程的环境中设置 `DFLEGACY_GITHUB_RELEASE_TOKEN`。更新包沿用资源清单的版本和提交，发布新版本前先用新版本号构建资源。同版本仅允许重试同一套字节。更新 ZIP 不包含服务器连接配置。

发布 v0.1.3 时，使用该版 `client-assets` 在本机生成并上传更新。内嵌充值页面和自动移鼠标缩放由登录器与原生补丁一起更新，现有测试充值接口可继续由 v0.1.2 服务端提供。
