# 构建、测试与发行交接

本文面向第一次接手仓库的开发者或 agent。命令从仓库根目录执行；路径示例中的 `<...>` 必须替换，不能原样运行。本文整理源码合同，不表示本次已运行构建、实机验收或远端发布。测试细节见 [测试与证据](testing-and-evidence.md)，部署见 [运维](operations.md)；两章构建/发行差异的系统检查见 [章别适配](chapter-adaptation.md)。

## 1. 先确定要交付什么

| 任务 | 使用入口 | 不要混淆 |
| --- | --- | --- |
| 修改 Server / Manager / 协议 | `dotnet build`，随后控制台测试 | `.NET` 测试项目主要通过 `dotnet run` 执行，`dotnet test` 成功不代表这些用例执行 |
| ACT1 Windows 原生补丁 | [Ijl15/build-native.ps1](../../src/DFLegacy.Ijl15/build-native.ps1) | 32 位游戏原生模块与 x86/x64 托管启动器是两个维度 |
| ACT1 旧合同资源、服务端归档 | [release.py](../../tools/release/release.py)、[ci.py](../../tools/release/ci.py) | `client-assets`、V2/V3 工具包不是 V4 三程序启动布局 |
| V4 任意受管理资源 | [update_v4.py](../../tools/release/update_v4.py)、[bootstrap_package.py](../../tools/release/bootstrap_package.py) | 本地 build/verify 与远端 publish 分开；V4 不是二进制差分 |
| 第二章原生宿主、broker | [Build-PresentationHost.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-PresentationHost.ps1)、[Build-StartupBroker.ps1](../../src/DFLegacy.ChapterTwo.Native/Build-StartupBroker.ps1) | 依赖经哈希锁定的外部输入，不可拿 ACT1 DLL 代替 |
| 第二章独立测试服务包 | [Build-Windows.ps1](../../deploy/chapter-two/Build-Windows.ps1) | 测试包清单不是完整通用发行清单，也不含通用 `client-assets` |
| 第二章独立测试登录器 | [Build-ChapterTwoLauncher.ps1](../../deploy/chapter-two/Build-ChapterTwoLauncher.ps1) | 这是独立 EXE 打包入口，不等于公开固定入口三程序整包 |
| 第二章公开整包 / 跨章清单 | [chapter_package.py](../../tools/release/chapter_package.py) | 当前是 Python 模块 API；不要虚构 `chapter_package.py publish` 命令 |

无明确发行授权时，做到本地新目录中的可审查产物、清单、验证结果即可。推送源码、创建/公开 Release、上传附件、推送镜像、切换真实部署、改变玩家更新源属于另外的操作范围。

## 2. 固定工具链及平台边界

工具版本以当前检出的源码为准，升级工具链须另行验证，不要把“装最新版”当作环境修复。

| 工具 / 环境 | 仓库要求与证据 |
| --- | --- |
| .NET SDK | [global.json](../../global.json)：`10.0.402`，`rollForward=latestPatch`，不允许预发布；同一 feature band 内可使用补丁更新 |
| Python | `3.12+`；Windows 文档用 `py -3.12`，Linux 用 `python3`；发行 Python 工具本身使用标准库 |
| Python 测试依赖 | [requirements-ci.txt](../../tools/release/requirements-ci.txt) 固定版本和 wheel SHA-256；包括 pytest 9.1.1、PyYAML 6.0.2、Pillow 12.1.0、pefile 2024.8.26、capstone 5.0.6、unicorn 2.1.4。安装整份锁定清单，不逐个随意升级 |
| Windows 原生 | Visual Studio C++ x86/x64 工具与 Windows SDK，CMake `3.24+`，PowerShell 7 (`pwsh`)；脚本通过 `vswhere`/CMake generator 查找工具 |
| Node | 当前 [Gitea 说明](../../.gitea/README.md) 为 Node 20；浏览器行为测试使用 `node --test`，不是实际浏览器端到端验收 |
| 容器 | 可用 Docker daemon / buildx；多架构输出另外需要适当 builder/QEMU；只有 Docker CLI 不够 |
| ACT1 镜像 | [根 Dockerfile](../../Dockerfile) 固定 SDK `10.0.402-noble`、runtime `10.0.13-noble`（Ubuntu 24.04）；镜像内验证 Windows 资源，但不编译 WPF/C++ |
| 第二章镜像 | [第二章 Dockerfile](../../deploy/chapter-two/Dockerfile) 同样使用上述镜像版本，托管项目 `--self-contained false`，运行时由镜像提供 |

Windows 快速环境核对（只读）：

```powershell
git status --short
git rev-parse HEAD
dotnet --version
py -3.12 --version
pwsh --version
cmake --version
node --version
Get-Content -LiteralPath 'src/DFLegacy.Launcher/LauncherVersion.txt'
```

自包含的服务端发行包不要求目标机器另装 .NET，但仍需要受支持系统的原生依赖；Linux 包需要 ICU、时区数据等。WPF/实际游戏与 Windows 原生测试须在 Windows 运行。Linux 可以交叉发布其他 RID；“生成 macOS/arm64 包”不代表已在相应硬件运行。macOS 旧发行说明明确未签名、未公证，不能把生成成功描述为已完成苹果分发验证。

## 3. 最小本地构建和测试

先记录提交与工作区状态；不要为获得“干净提交”删除用户修改。构建可在已授权的隔离检出中进行。以下命令写入 `bin/obj/artifacts`，不部署到游戏目录。

```powershell
dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release
if ($LASTEXITCODE) { throw 'Server build failed' }
dotnet build src/DFLegacy.Manager/DFLegacy.Manager.csproj -c Release
if ($LASTEXITCODE) { throw 'Manager build failed' }
dotnet run --project tests/DFLegacy.SmokeTests -c Release
if ($LASTEXITCODE) { throw 'Smoke tests failed' }
dotnet run --project tests/DFLegacy.Manager.Tests -c Release -- --http --server-dir "$PWD/src/DFLegacy.Server/bin/Release/net10.0"
if ($LASTEXITCODE) { throw 'Manager tests failed' }
py -3.12 tools/release/release.py test
if ($LASTEXITCODE) { throw 'Release tests failed' }
```

预期：每条退出码为 0，实际测试执行器给出通过结果。Manager HTTP 测试会创建临时数据、启动本机子进程和监听端口。上例不会连接真实部署，但也不是“完全不启动进程”。若任务只要求文档，不应顺便执行它们。

完整的 Python 环境、安全测试集、Windows 依赖测试和资源测试在 [测试矩阵](testing-and-evidence.md#2-测试矩阵) 中列出。Linux 将 `py -3.12` 改成符合版本要求的 `python3`；不要把 PowerShell 变量或反引号续行直接粘贴到 Bash。

## 4. Windows 原生与不可变资源

ACT1 构建入口：

```powershell
pwsh -NoProfile -File src/DFLegacy.Ijl15/build-native.ps1 -Configuration Release -RunTests
if ($LASTEXITCODE) { throw 'ACT1 native build/tests failed' }
pwsh -NoProfile -File src/DFLegacy.RandomNative/build-native.ps1 -Configuration Release -RunTests
if ($LASTEXITCODE) { throw 'RandomNative build/tests failed' }
```

常见输出在 `artifacts/native/ijl15/Release/`：`ijl15.dll`、`DFLegacy.Client.dll`、`DFLegacy.Bootstrap32.exe`、`debug.dll`。Ijl15 CMake 测试包括 Debug 模块。游戏原生组件为 32 位；RandomNative 是 Windows 服务端可选的 64 位随机源，缺少它时服务端有托管回退，不能因此宣称原生路径已测试。

构建旧合同 `client-assets` 优先使用 [BUILDING.md](../../BUILDING.md#构建不可变-windows-客户端资源) 或审查后的 `ci.py windows`。它们完成：

1. 同一源提交构建原生模块。
2. x86/x64 WPF 登录器各自以自包含、单文件、禁用裁剪方式发布，显式传入同次原生输出。
3. 加入同架构 `DFLegacy.Diagnostics.exe` 和 Win32 `DFLegacy.CrashCapture.exe`；测试包另含同次 `debug.dll`。
4. `build-client-assets` 创建安全模板和 SHA-256 清单；`verify --client-assets` 校验。
5. 生成各架构/类型的客户端工具或更新包；普通包不能因输入目录存在 debug 而开启测试功能。

不要从某个正在玩的目录反向复制资源。模板账号、密码、账号列表应为空，`StartServer=false`，`ClientPath=DNF.exe`。不包含个人配置、游戏存档、日志、私有凭据。V4 可明确选择有权分发的 PVF/NPK，但这不扩大旧 `client-assets` 白名单。

`ci.py windows` 会下载其源码锁定大小与 SHA-256 的旧版项目补丁 ZIP，用于完整登录器兼容测试；它不是纯离线命令。`--client-only` 省略服务端归档，不省略原生、登录器测试和客户端构建。脚本固定输出目录，不应在有既存产物的目录反复尝试覆盖；失败后保留日志并选择新隔离检出。

## 5. 版本、三个 EXE 和最终文件属性

必须遵守根 [AGENTS.md](../../AGENTS.md)：第二章默认发行号从 [LauncherVersion.txt](../../src/DFLegacy.Launcher/LauncherVersion.txt) 读取（本手册核对时为 `1.0.13`，不是对线上最新版的判断）。登录页从正在运行的托管程序读取，只显示发行号如 `v1.0.13`；Git SHA 留在构建元数据。

公开固定入口布局的三个角色是：

| 角色 | 文件 | 资源 |
| --- | --- | --- |
| 原生固定入口 | `DFLegacy.Launcher.exe` | [Startup](../../src/DFLegacy.Startup/README.md) 原生资源 |
| 托管登录界面 | `DFLegacy.Launcher.App.exe` | `app.res` |
| 独立更新器 | `DFLegacy.Updater.exe` | `updater.res` |

三个角色使用同一计划发行号构建；独立第二章登录器使用 `standalone.res` 和其真实文件名 `DFLegacy.Chapter2.Launcher.exe`。各角色使用独立 publish/intermediate 目录，避免 apphost/资源串用。`Startup/build-native.ps1 -Version ... -Revision ... -BuildManagedResources` 生成相应资源，再通过 `-p:DFLegacyLauncherWin32Resource=<对应res>` 传给托管 publish。完整参数与职责见 [Startup README](../../src/DFLegacy.Startup/README.md)。

在**最终打包目录**逐个检查，而不只看 csproj：

```powershell
$finalPackage = 'C:\replace-with-reviewed-final-package'
Get-ChildItem -LiteralPath $finalPackage -Filter '*.exe' -File | ForEach-Object {
    $v = $_.VersionInfo
    [pscustomobject]@{
        File = $_.Name; Description = $v.FileDescription; OriginalName = $v.OriginalFilename
        Product = $v.ProductName; Publisher = $v.CompanyName; Copyright = $v.LegalCopyright
        FileVersion = $v.FileVersion; ProductVersion = $v.ProductVersion
    }
}
Get-ChildItem -LiteralPath $finalPackage -Filter '*.exe' -File | Get-FileHash -Algorithm SHA256
Get-ChildItem -LiteralPath $finalPackage -Filter '*.exe' -File | Get-AuthenticodeSignature | Select-Object Path,Status
```

预期：说明、原始名、产品名、发布者、版权符合实际角色；版本与本次发行计划一致；哈希来自最终字节。签名检查可能是 `NotSigned`，必须如实记录。还需从最终隔离安装目录实际启动，核对显示版本、账号/设置保留、关闭操作、错误提示和更新交接。

第二章角色构建还需核对最终 App 的内嵌宿主与 broker。将资源摘要与发布线 `Build-LauncherRoles.ps1` 中经过验证的输入摘要对照。真机启动后再核对本次实际解包的 `ChapterTwoPresentation.exe` 摘要，避免复用构建目录里的旧宿主。功能初始化日志只说明该模块已安装，版本以产物摘要为准。

**固定入口不可直接热更。** 已安装清单固定其哈希；重新编译、修改版本资源或签名都会改变字节。不可为了统一属性而直接替换旧入口、改旧清单、禁用校验。应使用新的完整启动包或已验证迁移。历史 ACT1 版本曾刻意复用旧入口字节，不应据此默认为以后所有发行豁免统一版本规则。

第二章已有专门迁移：v1.0.11/sequence 16 → v1.0.12-bridge/17 → v1.0.12/18（minimum 17）。[chapter_package.py](../../tools/release/chapter_package.py) 对 `startup_bridge` 检查版本、序列、架构、类型、提交和其他文件不变；后续受 `previous` 和序列下限约束。`fresh_startup` 是新整包入口，不能绕过现有安装的热更限制。计划新版本前重读实现，不把历史 17/18 当作可重复使用的新序列。

## 6. 第二章构建依赖

第二章不能仅凭 Git 检出复现所有原生产物。宿主构建需要 `AcceptedHostSource`、已验证 `RebuiltImage`、实际商城 NPK；broker 需要 `AcceptedBrokerSource`。脚本验证固定 SHA-256，缺少或不匹配时应报告所缺输入，请持有者提供合法副本，禁止删除哈希检查“修通构建”。可先做无此依赖的托管/工具测试。

以下只构建新目录，示例路径须替换为审核过的输入：

```powershell
pwsh -NoProfile -File src/DFLegacy.ChapterTwo.Native/Build-PresentationHost.ps1 `
  -AcceptedHostSource 'C:\inputs\accepted-host-source' `
  -RebuiltImage 'C:\inputs\DNF-rebuilt-v3-probe.exe' `
  -ShopLayoutTestNpk 'C:\inputs\sprite_interface_cashshop.NPK' `
  -OutputDirectory 'C:\builds\chapter2-host-new'
pwsh -NoProfile -File src/DFLegacy.ChapterTwo.Native/Build-StartupBroker.ps1 `
  -AcceptedBrokerSource 'C:\inputs\accepted-broker-source' `
  -OutputDirectory 'C:\builds\chapter2-broker-new'
```

逐条检查退出码。`TownMapTestPvf` 是宿主脚本的可选真实资源输入，不能把缺少该资源的结果写成该路径已验证。宿主/游戏输入应有权使用；原生映像测试通过不替代真机画质、输入法、商城、双开和进图验收。

`Build-ChapterTwoLauncher.ps1` 需要完整 `NativeAssetsDirectory`、上述构建的 `PresentationHost`、`StartupBroker`，可选 `D3D11Bridge` 固定为脚本认可的 dgVoodoo2 2.87.6 MS/x86/D3D8.dll。`-LocalTest` 明确选择本机测试构建；正常发行不能误带测试开关。脚本输出 EXE、ZIP、哈希及构建日志，并检查文件版本和原始文件名；仍需补齐本节所有最终属性/实际启动检查。

`chapter_package.build()` 还要求已验证的 ACT1 完整桥接 `baseline`，第二章后续版本用同变体 `previous`。第二章客户端的 `Script.pvf` 必须是匹配的原生客户端视图，不能塞入以 `DNF_SCRIPT_PACK` 开头的服务端格式；`ImagePacks2/4` 必须完整，`ImagePacks3` 目录也应保留。包中禁止 ACT1 `DFLegacy.Client.dll`、`DFLegacy.Debug.dll`、`debug.dll`。服务端与客户端资源要语义匹配，但两种 PVF 格式不应强行要求字节相同。

## 7. 服务端归档与镜像

发行身份变量使用 `DFLEGACY_RELEASE_VERSION`、`DFLEGACY_RELEASE_COMMIT`。**不要设置通用 `VERSION=v...`**，MSBuild 大小写不敏感，可能把它当 NuGet Version 导致 restore 失败。使用 `ci.py metadata` 前设置 `BUILD_COMMIT` 和 `BUILD_REF`，它校验 HEAD 与触发提交一致，输出正式或 `dev-<UTC>-<SHA12>` 身份。

已有同提交、同版本且已校验的 `client-assets` 后，通用本地归档：

```powershell
$releaseVersion = '<已确定的发行版本或合法dev版本>'
$releaseCommit = (git rev-parse HEAD).Trim()
py -3.12 tools/release/release.py publish --rid win-x64 --version $releaseVersion --commit $releaseCommit --client-assets client-assets --output 'artifacts/release-new-win-x64' --verification '仅填写本次实际完成的验证'
if ($LASTEXITCODE) { throw 'Local packaging failed' }
py -3.12 tools/release/release.py verify 'artifacts/release-new-win-x64.zip' --version $releaseVersion --commit $releaseCommit
```

这里的 `release.py publish` 是本地组包，不上传。支持 `win-x64/linux-x64/linux-arm64/osx-x64/osx-arm64`；Windows ZIP，其他 tar.gz 保留执行位。输出目录/归档必须不存在，禁止向运行目录 publish。最终归档应有 Manager、`runtime/server`、`runtime/channel`、`client-assets`、模板和 `release-manifest.json`，无 `data/` 和私人 PVF。

根 Dockerfile 构建前必须有已校验的 `client-assets/`，版本/提交要一致。第二章 Dockerfile 不使用这套资源，但运行时要求外部只读挂载服务端 PVF。使用显式 Dockerfile，不凭镜像名猜章节：

```bash
# 本地构建示例：变量已设置、Windows资源已准备；不推送镜像。
docker buildx build --platform linux/amd64 --build-arg DFLEGACY_RELEASE_VERSION="$DFLEGACY_RELEASE_VERSION" --build-arg DFLEGACY_RELEASE_COMMIT="$DFLEGACY_RELEASE_COMMIT" --build-arg SOURCE_DATE_EPOCH="$SOURCE_DATE_EPOCH" --provenance=false --sbom=false --load -t dflegacy:local-review .
# 第二章单独构建，不会自动纳入第一章 client-assets。
docker buildx build -f deploy/chapter-two/Dockerfile --platform linux/amd64 --build-arg DFLEGACY_RELEASE_VERSION="$DFLEGACY_RELEASE_VERSION" --build-arg DFLEGACY_RELEASE_COMMIT="$DFLEGACY_RELEASE_COMMIT" --load -t dflegacy-chapter-two:local-review .
```

`SOURCE_DATE_EPOCH` 应来自构建提交的 Git 时间。ACT1 多架构与 OCI 导出详见 [BUILDING](../../BUILDING.md#容器构建)。重试发行优先复用已过门禁的 artifact；基础镜像/apt/NuGet 输入变动会改变字节，不能覆盖同版本已发布产物。

## 8. V4 本地准备与远端发行边界

[CLIENT_UPDATES_V4.md](../../CLIENT_UPDATES_V4.md) 是协议说明，但包含多期历史例子。正式 ACT1 是 `github/stable`；第二章是 `chapter-two` 渠道，并有 GitCode 镜像实现。Gitea/Gitee 测试源必须明确选择，不能把历史示例配置复制进正式玩家包。

V4 请求包含根目录、来源/仓库、version/40 位 commit、architecture、flavor、channel、递增 sequence、minimum_sequence、includes/excludes、previous/reuse、retired/migrations。从已审查的同类型请求复制并逐项核对；不能凭目录名称猜架构。提供**完整目标集合**；预览只选择确实存在的文件，写了 include 模式不证明文件存在。

```powershell
$requestPath = 'C:\builds\reviewed-request.json'
$bundlePath = 'C:\builds\new-v4-bundle'
py -3.12 tools/release/update_v4.py preview --request $requestPath
if ($LASTEXITCODE) { throw 'Preview failed' }
py -3.12 tools/release/update_v4.py build --request $requestPath --output $bundlePath
if ($LASTEXITCODE) { throw 'Build failed' }
py -3.12 tools/release/update_v4.py verify --bundle $bundlePath
if ($LASTEXITCODE) { throw 'Local verification failed' }
```

输出为 receipt、完整 manifest、哈希命名分卷。检查 `manifest.files`、全部必需 EXE/DLL/资源、排除原因、逻辑总量/新增上传量、退役/迁移。`previous` 是同变体完整历史；`reuse` 仅复用内容，不能替代历史。遗漏旧文件必须明确 `delete` 或 `unmanage`；迁移只改支持的配置字段，不覆盖账号、地址和自定义端口。已有资源引用旧 tag 时不能删除旧 Release。

完整整合包（starter/7z）的打包契约：压缩包根目录直接是游戏目录，不能带 `.tmp/...` staging 前缀；必须内置 `.dflegacy/v4/installed.json`、`.dflegacy/bootstrap/recovery.ini` 和 Updater 恢复副本（receipt 结构参考 `tools/release/chapter_package.create_starter`），否则新解压目录首次启动会被判为未受管并失败。完整包的正式外层入口是根目录 `DFOLauncher.exe`，它启动 `client\DFLegacy.Launcher.exe` 后退出；该文件不在 V4 清单内，更新不会生成或删除。v1.0.17 初次整包即因缺少这些状态而首启失败，见 [决策记录](../../knowledge/decisions/2026-11-20-v1.0.17-full-package-startup-fix.md)。

远端发布前，取得本次目标仓库/标签/渠道的明确授权；只在此前未授权时请求确认，不重复请求已获授权的同一动作。先完成可审查的四变体产物和本地验证，再执行 [V4 分组发布入口](../../CLIENT_UPDATES_V4.md#发布门槛与匿名验证)：同 tag 的 x86/x64 × normal/test 一次提交，`--target-commitish` 是公开仓库已经存在的目标提交，**不是**私有构建 SHA 的别名。专用 Token 只从进程环境读取，不写请求、日志、URL 或包内。

发布顺序是先上传/验证内容资源，再上传清单；支持草稿的平台最后公开。脚本拒绝改写既存 Release，失败后保留现场、审查并一般选择新 tag，不能擅自删除重发。发布后对每个 bundle 执行 `verify --remote`，它匿名回读资源；本地 verify 不证明公开用户能下载。旧 V3 桥接由原 `github_publish.py` 路径维护，不能假定 V4 自动为所有旧客户端建立桥接。

## 9. 签名与发布证据

当前已核对入口提供 SHA-256、大小、来源/路径约束、安装记录与事务校验；没有找到自动 Authenticode 签名、独立清单公钥签名或证书发放流程。`bundle.py` 的 `.NET bundle signature`、PE `signature`、GitCode 临时 signed upload URL 都不是项目发行数字签名。不要写“签名验证通过”来描述这些摘要检查。

若本次发行要求数字签名，先确认实际签名方案、证书持有者和安全密钥使用方式，再实施；仓库没有的步骤应标为缺口。签名会改变 EXE 字节，必须在最终摘要/清单生成前完成，并重新处理固定入口约束。不得把私钥/PFX 或 Token放进仓库和客户端包。

保留：构建提交、工具版本、精确命令/退出码、原生与托管日志、最终 SHA-256、PE 属性、签名状态、发布清单/资源清单、目标 tag/渠道/架构、匿名回读结果、真机验收矩阵。需要事故定位时，用 [retain_native_symbols.py](../../tools/release/retain_native_symbols.py) 保留与最终原生 PE 的 CodeView GUID/age 匹配的私有 PDB；不要把“同名 PDB”当匹配证明，也不要把符号和私有路径装入公开资源。

## 10. 已知文档冲突与停止条件

- [.gitea/workflows/release.yaml](../../.gitea/workflows/release.yaml) 名叫 release，实际 workflow 为 `source-tests`，仅 Linux 测试，`releases/packages: none`。不要声称 push 会自动发布或执行 Windows 门禁。
- [DEPLOYING](../../DEPLOYING.md) 和 [DEPLOYMENT_MANAGER](../../DEPLOYMENT_MANAGER.md) 仍有“V4 开发中/尚未正式发布”旧段落；V4 文档首段已记录正式 GitHub v0.1.10。以当前代码和选定 tag 的实际 artifact 为准；未查询远端时不要判断最新线上版本。
- [第二章原生 README](../../src/DFLegacy.ChapterTwo.Native/README.md) 包含较早迁移/待验收状态，不能覆盖后续代码与逐项验收记录。文档中提到存在的外部资源，不代表新机器已经拥有。
- 发现错误的章节/PVF 格式、缺外部哈希锁定输入、版本/提交不一致、固定入口变更无迁移、验收失败、包内私密数据、未知签名要求时，保留产物与日志，停止该发行步骤并说明缺口；不能篡改门禁使其通过。
