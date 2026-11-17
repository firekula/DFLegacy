# PVFStudio、NPKStudio 与资源工具协作

用途：从 DFLegacy 的需求找到正确的资源子项目，理解解析、修改、导出和游戏加载之间的接口。常规资源操作先读 [资源与 PVF](resources-and-pvf.md)。本页按 2026-11-17 本机源码核对，不把旧发行说明当当前工作区构建结果。

## 仓库与模块边界

这些是同一开发目录下的相关工程，**不是 DFLegacy 仓库中的 Git 子模块**。下面跨仓库链接假定三个仓库同级；换机可采用同样布局，也可按实际路径调整工具配置。

```text
工作目录/
  DFLegacy/     C# 游戏服务、客户端、原生补丁、特定资源迁移工具
  PVFStudio/    Java PVF 编辑、草稿、解析、写回和资源预览
  NPKStudio/    Java NPK/IMG 编辑、PNG 交换、草稿和包导出
  pvf-dump/     本机 C# 地图勘察小工具，当前没有独立 Git 仓库
```

| 项目 | 接手入口 | 核心模块与范围 |
| --- | --- | --- |
| PVFStudio | [START-HERE](../../../PVFStudio/START-HERE.md)、[手册目录](../../../PVFStudio/docs/handbook/README.md) | `pvf-core` 容器读写；`pvf-workspace` 草稿/文本与二进制编解码；`pvf-cli` 批处理；`pvf-preview` 只读资源与场景预览；`pvf-product` 历史 GUI |
| NPKStudio | [START-HERE](../../../NPKStudio/START-HERE.md)、[手册目录](../../../NPKStudio/docs/handbook/README.md) | `npk-core` 容器/帧读写与工作区；`npk-product` IntelliJ 桌面产品、操作/标签及原生对话框 |
| pvf-dump | [本机 README](../../../pvf-dump/README.md)、[便携说明](pvf-dump.md) | 用 DFLegacy.Protocol 读取指定地图并打印标签或前70行；不写 PVF |
| DFLegacy `tools/pvf` | [工具索引](tools-reference.md)、[资源说明](resources-and-pvf.md) | 面向明确章别和输入摘要的语义迁移，不是通用编辑器 |

只 clone DFLegacy 不会获得其他两个仓库、用户资源、SDK、JBR 或 pvf-dump。交接时分别记录三仓库提交和工作区差异。

## 当前源码状态

- PVFStudio HEAD `b47bb46e4e662cee316d959b64432e0f934f3da5`，源码版本 `0.13.5`。本轮开始时 `pvf-product/` 的 **57 个跟踪文件缺失**，其余四个默认模块仍有源码。`withIntellij=true` 指向缺失产品模块；`intellij-workbench/` 只有本机生成物。不要自动恢复用户删除，也不要把历史桌面可用当成当前完整可重建。
- NPKStudio HEAD `43963abd3b7bed879ac39963ea5dd0af3d6aac53`，源码版本 `0.1.2`，本轮开始工作区干净。
- 这些值只用于本次文档基线。后续任务先 `git status --short` 和 `git rev-parse HEAD` 重新确认。

## 选哪一个解析器

| 实现 | 支持目标/责任 | 不能据此推断 |
| --- | --- | --- |
| [C# PvfArchiveReader](../../src/DFLegacy.Protocol/PvfArchiveReader.cs) | DFLegacy 运行时读取 `DNF_SCRIPT_PACK`，路径索引与解密缓存 | 不是通用多格式编辑器；代码里的 CRC 字段用作解密 key，读成功不等于完整校验器验过 CRC/布局 |
| [C# ScriptFileSystem](../../src/DFLegacy.Server/ScriptFileSystem.cs) | 服务端 Catalog 的 PVF/解包目录只读入口；内部另有一套 PVF 解析代码 | 并未调用上行的 `PvfArchiveReader`；修正一处不会自动修正另一处，也没有通用二进制脚本编译器 |
| [管理器上传校验](../../src/DFLegacy.Manager/PvfIndexValidator.cs) | 流式核验 `DNF_SCRIPT_PACK` 目录和各条目的文件范围 | 不解码脚本、不验证正文 CRC、不证明客户端兼容；上传成功不是完整资源验收 |
| [PVFStudio 格式解析](../../../PVFStudio/docs/handbook/pvf-format-and-parser.md) | Java ACT1/UUID/Legacy 容器与格式身份，带写回需求的保真读取 | 包格式支持不等于其中所有脚本语义可编辑或任意游戏版本都已验收 |
| [PVFStudio 写入](../../../PVFStudio/docs/handbook/editing-and-writing.md) | 同格式保真导出、已有条目替换及受支持结构编辑 | 不是第二章客户端/服务端两种封装互换工具 |
| [第二章 port.py](../../tools/pvf/chapter-two/port.py) | 固定输入的语义迁移，生成 native/server 配对视图，重开逐项核对 | 不可删除输入摘要、字段守卫后当通用转换器 |
| PVFStudio `pvf-preview` | 从 NPK/IMG/ANI 等构建预览与场景数据 | 预览读取能力不是 NPK 编辑合同，不应在此随手实现包保存 |
| [NPKStudio 格式与写入](../../../NPKStudio/docs/handbook/npk-img-format.md) | NPK/IMG v1 编辑；v2 预览只读；源目录槽/名称字节和未知内容保持 | 不能靠放开 UI 按钮获得新格式写支持 |

### 一个容易误用的历史特例

第二章 `port.py` 在读取特定 ACT1 证据包时使用 `validate_entry_crc=False`，同时用固定 `MAIN_SHA` 验证整包身份。它的注释说明该历史 PVFStudio 产物不是 file-ID-seeded CRC 约定。**这是指定历史证据包的兼容路径**，不表示当前 PVFStudio 永远不生成 seeded CRC，也不表示所有 CRC 失败都能关检查。当前 writer 的行为应读当前源码和格式手册。

## DFLegacy 接收资源时的具体边界

### 两套 C# 读取器要一起核对

`PvfArchiveReader.Open/ReadEntry` 与 `ScriptFileSystem.OpenPvf/ReadPvfEntry` 都将整包读入内存，采用 15 字节 `DNF_SCRIPT_PACK` 签名、12 字节小端头部，目录固定项为 20 字节后接路径字节；解密为 `RotateRight(ciphertext ^ key, 6)`。它们都没有重新计算目录/正文 CRC。文件范围在实际读正文时才检查，单纯构造成功不证明每项都可读取。

路径按 CP949 解码，去结尾 NUL、反斜杠改 `/`、去前导 `/`、转小写。重复规范路径使用字典赋值，后项覆盖前项，因此 `FileCount` 可能小于头部 count；这里没有 Studio 那样完整的路径/重叠验证。路径编码不代表正文编码：例如 `QuestCatalog` 用 CP936，多个地图 Catalog 用 CP949；`ReadAllText` 的默认值是 `Encoding.Default`，不是统一的 CP936。改正文时应查消费该条目的 Catalog，不能只根据容器猜编码。

两套读取器均保存打开时的字节快照，正文缓存返回副本。`ScriptFileSystem.ReadAllTextUncached` 只是跳过正文缓存，仍读原来的内存包，**不会重开磁盘 PVF**。替换磁盘文件后须重新创建加载链/重启独立测试服务，再核对 `SourceDescription` 和摘要，不能靠带 `Uncached` 的方法证明加载了新包。

`ScriptFileSystem` 优先选存在的配置 PVF；若它损坏则抛异常，不会静默切换解包目录。只有 PVF 不存在时才考虑 `SkillScriptPath`。目录索引延迟建立、跳过 reparse point，并拒绝大小写不敏感的重复路径；这是开发/测试后备，不是将 UUID/Legacy 自动转为服务端可读格式的能力。

### 管理器上传与编辑器导出不是同一事务

实现入口：[PvfResourceStore.StoreAsync](../../src/DFLegacy.Manager/PvfResourceStore.cs)、[PvfIndexValidator.ValidateAsync](../../src/DFLegacy.Manager/PvfIndexValidator.cs)、[ServerSupervisor.UploadPvfAsync](../../src/DFLegacy.Manager/ServerSupervisor.cs)。

1. Supervisor 门闩拒绝并发上传；服务正在运行或路径受 `DFLegacy__ScriptPvfPath` 环境覆盖控制时拒绝上传。
2. Store 在 `Data/resources` 下新建随机 `.tmp`，流式限长，最大 1 GiB；边写边算整包 SHA256。源上传文件名不决定存储路径。
3. 核验头部、目录长度/条目数量，流式解密目录，检查每项路径长度及补齐后的正文范围。**不会检查路径文本、重复路径、重叠数据、正文 CRC 或游戏语义**。
4. 验证成功后存为 `<sha256>.pvf`。已有同名文件先检查长度和摘要；一致则复用，损坏则以新上传内容替换。资源目录与已有目标的 reparse point 检查不能写成完整 Studio 导出/备份协议。
5. Supervisor 选择新路径，配置提交失败时仅删除本次新创建的候选文件；临时文件在 `finally` 清理。上传本身不将其他服务参数标成已配置，也不触发游戏内验证。

对应 [PvfUploadTests](../../tests/DFLegacy.Manager.Tests/PvfUploadTests.cs) 可在 DFLegacy 根目录用以下参数选择，测试使用仓库夹具及临时目录，不上传真实资源。注意 [测试入口](../../tests/DFLegacy.Manager.Tests/Program.cs) 会先运行第二章配置和日志恢复测试，再进入 `--pvf-only` 分支；不是只执行一个测试类：

```powershell
dotnet run --project tests/DFLegacy.Manager.Tests -- --pvf-only
if ($LASTEXITCODE) { throw 'PVF upload tests failed' }
```

2026-11-17 文档复核实际执行上述命令，退出 0，末尾 `PVF upload: 0 failures`；另执行 `python -B tools/pvf/chapter-two/test_port.py`（6 项）与 `test_client_resources.py`（4 项），均通过。这里验证的是合成/仓库夹具和配置流程，没有运行真实游戏包迁移或客户端加载。

## 从游戏需求到可交付资源

1. **定位语义。** 确认章别、任务/物品/商店 ID、虚拟路径、客户端与服务端当前资源摘要。需要改图片时定位 NPK、目录槽、IMG 与帧号。
2. **选择操作层。** 脚本/字符串表用 PVFStudio workspace 或明确的 DFLegacy 迁移工具；PNG/XY/画布用 NPKStudio。文件名相同不能代替内容身份。
3. **准备独立工作区。** 源包只读，草稿单独保存，输出新路径。记录允许变更的条目/字段；改名要额外处理引用。
4. **导出并独立校验。** 编辑器回读后再用独立验证器比对；只改字段与像素时分别验证，未改的条目/间隙/目录槽也要保持其合同。
5. **配对加载。** DFLegacy 服务端按其支持封装读取；第二章游戏用 native PVF。不要把服务端包装进玩家客户端来试。
6. **隔离真机验收。** 验证入口、目标界面/玩法、原有角色/物品、重登，记录实际加载文件摘要。预览正确不证明客户端正确。
7. **发布。** 按 [构建发布](build-test-release.md) 更新正确章别资源及清单，保留旧基线、迁移与回滚证据。

## DFLegacy 的 Java 导出器如何依赖 PVFStudio

`ExportSeriaMaterials.java` 等工具导入 `studio.pvf.core.*`。它们需要当前 PVFStudio 编译出的核心类，不要求缺失的桌面产品源码。下面示例只构建和编译工具，不读取游戏资源，不发布：

```powershell
# 从 DFLegacy 根目录执行，假定 PVFStudio 同级。
$pvfStudioRoot = (Resolve-Path -LiteralPath '..\PVFStudio').Path
Push-Location $pvfStudioRoot
try {
    .\gradlew.bat :pvf-core:classes
    if ($LASTEXITCODE) { throw 'PVF core build failed' }
} finally { Pop-Location }
$pvfCoreClasses = Join-Path $pvfStudioRoot 'pvf-core\build\classes\java\main'
$toolClasses = Join-Path $PWD 'artifacts\pvf-java-tools-new'
if (Test-Path -LiteralPath $toolClasses) { throw 'Choose a new output directory' }
New-Item -ItemType Directory -Path $toolClasses | Out-Null
javac --release 17 -encoding UTF-8 -cp $pvfCoreClasses -d $toolClasses tools/pvf/seria-materials/ExportSeriaMaterials.java
if ($LASTEXITCODE) { throw 'Exporter compilation failed' }
```

确认输入满足 [赛丽亚导出器说明](../../tools/pvf/seria-materials/README.md) 后，才按实际资源路径运行：

```powershell
# 两个路径必须替换；输出父目录已存在，输出文件不得存在。
java -cp "$pvfCoreClasses;$toolClasses" ExportSeriaMaterials 'C:\inputs\verified-source.pvf' 'C:\outputs\new-export.pvf'
if ($LASTEXITCODE) { throw 'Export or verification failed' }
```

Windows classpath 分隔符是 `;`，Linux 是 `:`。工具的输入摘要、物品集合和修改条目是固定合同；编译通过不代表你的资源满足该合同。修改通用解析器后要同时考虑这些调用者，避免只测 Studio UI。

本轮文档核验实际使用当前 PVFStudio 核心 classes 和 `javac --release 17 -encoding UTF-8` 编译了 `ExportPvpChannels`、`FixBehemothForbiddenUnlock`、`ExportSeriaMaterials`、`InspectMaterials`，四个工具均编译成功；没有据此运行真实游戏资源导出。两套 Studio 各自的核心测试与示例验证结果记录在对应构建/CLI手册中。

## 新格式/新修改操作的交接清单

写明：字节布局与版本识别依据；所有长度/偏移上限；压缩/加密/校验的顺序；文本编码与原始字节保持；可读但只读分支；允许修改集合；空数据/截断/链接循环/重叠范围的处理；新包独立回读结果；游戏实际加载结果。每项指向对应源码与测试，避免仅写“支持 XXX 格式”。

### 修改一个解析器后还要找谁

以下实现相互独立，不能假定其中一个修正会自动传播。先评估相关性，再只对受影响范围修改与验证；不要为了统一代码而抹掉不同格式的约定。

| 本次修改 | 需要同步审查的调用者/独立实现 | 最少交接证据 |
| --- | --- | --- |
| PVF 容器头、目录、加密、CRC 或重定位 | Java core；[Python 独立验证器](../../../PVFStudio/validation/verify_pvf.py)；C# 两套读取器；[第二章 port.py](../../tools/pvf/chapter-two/port.py) | 指定 ACT1/UUID/Legacy 分支；种子依据；无修改与变长修改往返；未改项/填充/空隙保持 |
| 二进制脚本或字符串表 | [BinaryScriptCodec](../../../PVFStudio/pvf-workspace/src/main/java/studio/pvf/workspace/BinaryScriptCodec.java)、workspace；消费脚本的 C# Catalog | token/表索引及编码；脚本与表共同导出；重开草稿与重开输出两种检查 |
| NPK/IMG 索引、帧或解压 | NPKStudio core；[独立 Python 校验器](../../../NPKStudio/tools/check_resources.py)；PVFStudio 的 [NpkResources](../../../PVFStudio/pvf-preview/src/main/java/studio/pvf/preview/NpkResources.java) | 目录槽/版本/帧号；直接和间接链接影响；ARGB 期望；未修改记录和原负载保持 |
| 草稿/导出原子性与错误处理 | workspace、备份与目标发布代码、CLI 返回码、UI 关闭/保存路径 | 操作前后源/草稿/目标摘要；失败发生在提交前还是提交后；重开后的真实状态 |
| 游戏资源语义或章别封装 | 特定迁移工具、服务端 Catalog、客户端实际加载链 | 输入白名单、修改字段、两端资源身份、隔离游戏操作与重登结果 |

同一个字段名 `CRC`、`length`、`offset` 在不同分支不一定有相同种子、是否含补齐、相对基址或有符号解释。涉及这些字段的 PR/交接必须直接引用该分支的格式表，不写笼统的“按标准 PVF/NPK 格式处理”。
