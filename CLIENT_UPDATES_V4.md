# V4 客户端更新与离线资源发布

v0.1.9 使用 GitHub `firekula/DFLegacy` / `stable`，两架构各有普通与测试变体，V4 序列为 2。同一 Release 保留旧客户端可识别的 V3 过渡包。发布复用 v0.1.8 原生固定入口，仅更新托管程序和补丁；旧安装首次生成校验记录后，普通打开登录界面复用记录。见 [v0.1.9 升级说明](docs/releases/v0.1.9.md)。下文 Gitea 配置和 `.giteatest` 版本是历史隔离测试示例，不能复制进正式整合包。

历史隔离测试来源为 `http://firekula.site:3000/firekula/DFLegacy`，早期 `.giteatest` 标签保持不可变；正式 V4 自 v0.1.8 起已使用 GitHub。固定入口、恢复副本、损坏与中断的早期证据见[报告](artifacts/bootstrap-auto-repair-20261108/REPORT.md)。

## 来源与离线状态

### 固定图标与自动修复（Gitea 测试版）

从 `v0.1.8-giteatest.3` 开始，`DFLegacy.Launcher.exe` 是小型原生入口，原来的快捷方式继续指向它。完整界面移到 `DFLegacy.Launcher.App.exe`；`DFLegacy.Updater.exe` 在校验和修复时运行，打开界面后退出，不安装后台服务。x86 与 x64 分别构建；这不增加 Windows 7 支持。

首次启动、安装记录升级或需要恢复时，入口验证修复程序的摘要，将它复制到独立临时目录再执行，因此可以更换客户端目录中的登录器和更新程序。主修复程序缺失或损坏时，使用 `.dflegacy/bootstrap/` 中经过验证的恢复副本。恢复记录只在修复程序成功结束后原子更新；失败的新程序不会覆盖上一份可用记录。`DFLegacy.Bootstrap.ini` 是严格格式的版本配套描述文件，不要手动编辑摘要或混用不同架构。

安装／修复会先恢复中断事务，完整校验全部受管理文件及配置后置条件，再保存 `.dflegacy/v4/validated.json`。记录与安装目录、来源、架构、类型、完整安装清单摘要及关键 EXE／DLL／启动描述文件的大小和时间绑定。v0.1.9 保留旧原生入口校验／复制更新程序的开销，由新托管更新程序复用完成记录，避免重新读取全部游戏资源。

后续双击原图标通过既有入口与更新程序读取记录及关键文件属性，打开登录界面；不再重复读取全部游戏资源或弹出“校验完成／已是最新／离线校验”提示。登录打开后仍在后台发现新版本，发现更新时沿用下载、安装和文件占用确认流程；无法确认线上最新时通过登录窗口提示信息显示状态，不阻塞登录。单独打开 `DFLegacy.Launcher.App.exe` 仍转回固定入口。

自动诊断助手在执行前仍单独校验 `DFLegacy.Diagnostics.exe` 的摘要，这是启动外部程序的授权检查；用户点击进入游戏时也继续执行完整安装清单校验。这两项与打开登录界面的全资源重复校验分开保留。

记录缺失、损坏、目录／来源／架构／类型不符、关键 EXE／DLL 被删除或属性改变，以及未完成事务，都不能继续使用快速路径，需要重新校验或恢复。旧版记录首次升级可完整校验一次。此合同主动信任两次安装之间的游戏资源状态，**不保证正常登录启动时发现未被另行检查的 PVF／NPK 修改，或保留全部属性的同长度修改**。“检查更新”及独立修复入口仍执行完整内容校验；用户实际启动游戏时的安装清单校验也继续保留。账号和服务器配置保留。游戏或其他程序占用待替换文件时仍需用户确认关闭，不会自动结束无关进程。

v0.1.9 与 v0.1.8 原生入口保持相同字节，发布工具和运行时继续拒绝普通热更替换固定入口。源码中的 `startup.dat`／DPAPI 原生快速入口实验不随 v0.1.9 交付；以后若启用，需要单独的完整启动包或明确入口迁移。若入口本身也被删除，或者更新程序及恢复副本全都损坏，需要重新解压对应架构的完整启动修复包。当前自动恢复不能代替系统权限、磁盘空间或网络故障的处理。

新的布局使用 `tools/release/bootstrap_package.py` 构建。输入是明确列出的公共文件及 SHA256/大小；生成严格描述文件、V4 清单及包含初始恢复副本的 starter ZIP。它不会把玩家配置和本地恢复缓存上传。旧 V2/V3 工具包合同保持不变，不能把三程序布局冒充旧 `client-assets`。详见[固定入口设计](docs/superpowers/specs/2026-11-08-bootstrap-auto-repair-design.md)。

在已有 `launcher.json` 中合并以下字段，不覆盖账号、服务器或其他设置。架构由正在运行的 x86／x64 登录器确定；`UpdateFlavor` 可填 `normal` 或 `test`，留空则读取已安装记录／`client-package.json`。

```json
{
  "UpdateProvider": "gitea",
  "UpdateRepository": "firekula/DFLegacy",
  "UpdateChannel": "gitea-test",
  "UpdateFlavor": "test",
  "DisableGitHubUpdates": false
}
```

| 配置 | 行为 |
| --- | --- |
| `UpdateProvider=github`，`UpdateChannel=stable`，`UpdateServiceUrl` 为空 | 检查 GitHub 对应架构／类型的 V4 清单；首次尚无 V4 安装且仓库没有兼容 V4 清单时保留旧 V3 桥接路径。 |
| `UpdateProvider=github`，显式非空 `UpdateServiceUrl` | 保留该自托管地址及原有 V3 更新路径，不静默改用公共仓库。 |
| `UpdateProvider=gitea`，`UpdateChannel=gitea-test`（本轮测试）或 `gitee`／`gitee-test` | 显式测试源优先于遗留自托管地址；没有完整兼容清单时报错，不回退到 GitHub。 |
| V4 公共来源且 `DisableGitHubUpdates=true` | 该兼容字段在 V4 路径中停用联网检查；仍检查已提交的本地 V4 安装，尚未安装 V4 时沿用本地安装包校验。自托管 V3 保留其既有配置语义。 |

完整 SHA256 校验在安装、修复、显式检查和旧记录升级时执行；正常启动复用完成校验记录，同版本发现也不再读取资源内容。普通版与测试版、x86 与 x64 的清单和程序保持分开，不自动切换。

已安装并验证通过的版本 A 与发现／下载中的 B 分开记录。B 下载失败、取消或断网后，A 的完成记录和关键文件属性仍有效、配置后置条件满足且没有待恢复事务时，可继续使用 A；状态说明“未确认线上最新版本”，无需确认弹窗。没有可信已安装记录或提交中断时必须先校验／恢复。已完成的缓存按内容哈希复用，失败不会清掉其他已验证资源；HTTP Range 被服务端忽略返回 200 时重新读取当前分卷并校验。

## 文件与配置策略

V4 清单是完整目标集，资源可有任意扩展名和嵌套路径，包括明确选择的 EXE、DLL、PVF、NPK、网页和零字节文件。未列入受管理集合的本地文件默认保留。发布器必须有显式包含规则，排除规则优先；`launcher.json`、`Config.ini`、日志、存档、账号目录、更新缓存／备份、版本控制目录及已知私密文件始终排除，也不能通过大小写、路径上跳、Windows 别名或链接绕过。路径分量包含 `~` 紧接数字时拒绝，避免 `LAUNCH~1.JSO`、`IMAGEP~1/a.npk` 等 8.3 短名称绕过保护；使用完整长名称。

上述保护也覆盖 `launcher.json.backup-*`、`Config.ini.bak` 等受保护配置的点号／波浪号后缀副本，以及 `.launcher-save-*` 原子保存临时文件；在任意目录层级、大小写下都先于包含规则排除。这不同于旧 `client-assets`／V2 工具包的固定文件白名单；后者仍保持原合同。不要为了制作 V4 清单修改旧打包器白名单，也不要将完整玩家目录、私有 Git 历史或凭据作为发布输入。

`launcher.json` 是保留并迁移的配置，不是必须与服务器字节完全一致的受管理文件。当前声明式操作仅支持该文件的 `Relay.TcpPorts`、`Relay.UdpPorts`、`Relay.Enabled`、`Relay.Preference`：

```json
[
  {"id":"pvp-ports-7110","file":"launcher.json","operation":"unionPorts","key":"Relay.TcpPorts","value":[7110,7111,7112]},
  {"id":"relay-enabled-default","file":"launcher.json","operation":"setDefault","key":"Relay.Enabled","value":false},
  {"id":"relay-preference-v6","file":"launcher.json","operation":"replaceIf","key":"Relay.Preference","when":"ipv4","value":"ipv6"}
]
```

端口 `value` 使用整数数组，合法范围 1–65535。玩家配置中的端口仍写成逗号分隔字符串，例如 `"2311,7001,8080,7110,7111,7112"`；缺省 TCP／UDP 会先展开各自原有默认端口，再合并新增项，不会挤掉默认登录端口。`replaceIf.when` 对端口可使用数组或旧分隔字符串，按端口语义比较；偏好值为 `ipv4`／`ipv6`，不区分大小写。迁移 ID 不可换用不同含义；重复运行不会重复添加端口。账号、密码、地址、自定义端口及未知字段保留。配置损坏时保留原字节并报错，禁止以空模板替换。

新清单遗漏旧受管理文件时必须声明退役：

```json
[
  {"path":"old/obsolete.dll","action":"delete","sequence":2},
  {"path":"optional/user-editable.dat","action":"unmanage","sequence":2}
]
```

`delete` 只删除客户端历史记录中确实受管理的文件，操作前备份；`unmanage` 保留文件并解除管理。提供最近目标的完整清单作为 `previous`；工具累积其退役和迁移，再加入本次声明。从 A 跳到 C 时仍能处理 B 的退役与迁移。`minimum_sequence` 明确支持的最早基线；低于该基线需要桥接，不把缺少历史当成成功升级。不要省略真实的先前清单来绕过漏选检查。

## 本地构建双架构测试产物

在仓库根目录运行，要求 Python 3.12。按 [BUILDING.md](BUILDING.md#构建不可变-windows-客户端资源) 的原生构建及 `dotnet publish` 参数，从同一提交生成 x86／x64 自包含登录器和匹配补丁。设置 `DFLEGACY_RELEASE_VERSION=v0.1.8-giteatest.1`；源程序的构建版本、V4 `version` 与最终 Release tag 必须一致，`commit` 填该构建的 40 位源提交。测试发布使用独立标签，不占用正式 `v0.1.8`。

V4 直接读取这些构建字节。旧 `release.py build-client-assets`／V3 工具仍只接受 `vX.Y.Z` 或 `dev-YYYYMMDDTHHMMSSZ-<SHA12>`，不能把 `.1`／`.2` 测试标签传给该旧入口。需要 V3 桥接时，另用其允许的独立 dev 版本从同一源提交构建桥接程序，再构建对应 `.1`／`.2` 的 V4 程序；不要伪装成正式 `v0.1.8` 来绕过旧校验。

为每种变体准备明确选择的输入目录。例如 `artifacts/client-update-v4-20261108/inputs/x86-normal`，以及 `x86-test`、`x64-normal`、`x64-test`。每个目录包含该架构的 `DFLegacy.Launcher.exe`、同次构建的 `DFLegacy.Client.dll`，测试版另外放 `debug.dll`；按本次测试范围放入 `Script.pvf` 和选定的 `ImagePacks2/` 资源。不得把账号或整个工作区直接复制进去。登录器取自各自的 `dotnet publish -r win-x86`／`win-x64` 输出，补丁取自同次原生 Release 输出。若使用已有合法版本的 `client-assets`，x86 登录器在 `launcher-x86`，x64 在 `launcher`，公共补丁在 `patch`，仍须核对版本匹配。这些输入目录需要实际构建字节，报告中的微型 schema fixture 不能当成可运行客户端。GUI 每次处理一个变体；同 tag 的四份产物使用下文分组发布命令一次发布。

下面只生成请求文件；它不读取凭据，也不访问远端。保存并运行此 Python 片段，将 `source_commit` 换成已经构建的真实源提交：

```python
import json
from pathlib import Path

base = Path("artifacts/client-update-v4-20261108").resolve()
source_commit = "替换为40位源提交"
for arch in ("x86", "x64"):
    for flavor in ("normal", "test"):
        request = {
            "root": str(base / "inputs" / f"{arch}-{flavor}"),
            "provider": "gitea", "repository": "firekula/DFLegacy",
            "version": "v0.1.8-giteatest.1", "commit": source_commit,
            "architecture": arch, "flavor": flavor, "channel": "gitea-test",
            "sequence": 1, "minimum_sequence": 0,
            "includes": ["DFLegacy.Launcher.exe", "DFLegacy.Client.dll",
                         "Script.pvf", "ImagePacks2/**"] + (["debug.dll"] if flavor == "test" else []),
            "excludes": ["*.pdb", "*.tmp"], "previous": [], "reuse": [],
            "retired": [],
            "migrations": [{"id": "pvp-ports-7110", "file": "launcher.json",
                            "operation": "unionPorts", "key": "Relay.TcpPorts",
                            "value": [7110, 7111, 7112]}]
        }
        base.mkdir(parents=True, exist_ok=True)
        (base / f"request-1-{arch}-{flavor}.json").write_text(
            json.dumps(request, ensure_ascii=False, indent=2), encoding="utf-8")
```

先检查预览，确认所有必需文件确实出现在 `manifest.files`，不能只依据包含模式推断它们存在。资源扫描只选择现有文件；缺少某个输入是否符合本次发布范围由操作者核对。然后生成到输入目录以外的新目录：

```powershell
$v4 = Join-Path $PWD 'tools/release/update_v4.py'
$base = Join-Path $PWD 'artifacts/client-update-v4-20261108'
foreach ($arch in 'x86','x64') {
  foreach ($flavor in 'normal','test') {
    $request = Join-Path $base "request-1-$arch-$flavor.json"
    $bundle = Join-Path $base "v1-$arch-$flavor"
    py -3.12 $v4 preview --request $request
    if ($LASTEXITCODE -ne 0) { throw 'V4 preview failed' }
    py -3.12 $v4 build --request $request --output $bundle
    if ($LASTEXITCODE -ne 0) { throw 'V4 build failed' }
    py -3.12 $v4 verify --bundle $bundle
    if ($LASTEXITCODE -ne 0) { throw 'V4 local verification failed' }
  }
}
```

每个输出包含 `receipt.json`、内容哈希命名的 `assets/dflegacy-v4-<sha256>.bin` 和 `DFLegacy-client-update-v4-gitea-test-<normal|test>-win-<x86|x64>.json`。回执列出完整文件、排除原因、差异、逻辑总量、待上传字节、复用字节、迁移和退役。输出已存在时拒绝覆盖，失败后选择新目录并审查原因。

## 第二版与增量资源

构建 `.2` 前先保留 `.1` 的四个完整清单及其发布资源。每个变体的请求从对应 `.1` 请求复制，改为 `version=v0.1.8-giteatest.2`、`sequence=2`，`previous` 指向**同架构、同类型、同渠道**的 `.1` 清单；`commit` 与更新后的程序构建一致，输出改为 `v2-<arch>-<flavor>`。新的资源或变更文件直接体现在完整输入目录中；遗漏旧文件时必须添加本次 `retired` 声明。原有迁移可由先前清单自动累计，新迁移使用新的 ID。

`reuse` 可以列出同渠道、同类型、其他架构先前已发布的清单，供公共 NPK／PVF／补丁内容复用；不能用它代替本变体的 `previous` 管理历史。构建只为此前没有的内容生成上传附件；未变化资源的 `parts.release` 保留旧不可变 tag。发布前会通过同一来源的匿名结构化附件列表和实际下载核对所有旧引用，不能引用本地尚未发布的构建。资源引用仍存在时不能删除旧 Release；工具提供 `release_references`／`ensure_release_unreferenced` 检查，没有自动删除 Release 的操作。

重新运行上面的 `preview`／`build`／`verify`，检查 `.2` 的上传体积和退役／迁移。缓存已完成分卷会复用，单个逻辑文件的最终长度与 SHA256 仍必须匹配；这不是二进制差分补丁。

## 发布门槛与匿名验证

发布前核对四份产物、完整清单及迁移，并取得测试仓库授权。本轮按用户指示改为 Gitea 隔离测试发布；本文中的命令不是正式 GitHub 发布授权。凭据仅由发布进程环境中的 `DFLEGACY_GITEA_RELEASE_TOKEN`（保留 Gitee／GitHub 各自的专用变量）读取，不能写入请求 JSON、清单、客户端配置、URL 或日志。

`--target-commitish` 是公开发布仓库**已经存在的提交 SHA**，与清单内的源构建 `commit` 分开。空测试仓库需要先按获授权范围建立最小公开说明提交；发布器不会推送源码、私有 Git 历史或自动初始化仓库。

同一 tag 的变体必须一次分组发布。组内来源、仓库、版本、源提交、顺序、渠道和最低基线一致，架构／类型组合唯一；共享资源按哈希与长度合并。先上传并校验全部资源，再上传所有清单。GitHub 与 Gitea 最后开放已校验的草稿；Gitee 没有草稿状态，某个清单可见时其全部资源已经具备。

```powershell
# 仅在测试发布已经获授权、凭据已进入当前进程且产物已审查后执行。
$publicCommit = '替换为公开测试仓库现有40位提交'
py -3.12 $v4 publish `
  --bundle (Join-Path $base 'v1-x86-normal') `
  --bundle (Join-Path $base 'v1-x86-test') `
  --bundle (Join-Path $base 'v1-x64-normal') `
  --bundle (Join-Path $base 'v1-x64-test') `
  --target-commitish $publicCommit
```

每次发布使用新 tag，拒绝重写已有 Release，包括中断后的不完整 Release。上传失败后保留现场，没有自动删除、覆盖或补传流程；审查后通常使用新测试 tag。Gitee 在清单上传阶段失败时可能已有部分变体可发现，但其资源必须已完整；不能重用同 tag 把另一些字节悄悄换进去。

Gitea 发布器目前保守限制单附件不超过 100,000,000 字节，不套用 Gitee 的 1 GB 仓库累计限制。平台预检按分组后的唯一附件计数，并将每份清单计入：GitHub 最多 1000 个附件，单附件必须小于 2 GiB；Gitee 单附件不超过 100,000,000 字节、仓库附件累计不超过 1,000,000,000 字节。每个传输分卷最多 48 MiB（50,331,648 字节）；空文件用一个零字节卷。分卷解决单附件限制，不能解决仓库总量超限。旧资源复用能减少新增上传，但旧 Release 占用仍计入 Gitee 总量；工具明确阻止超限，不静默漏文件或删除历史。

发布后使用新建、无 Cookie／Authorization 的客户端重新下载清单和资源，检查长度、SHA256 与 Range 结果。也可单独运行：

```powershell
py -3.12 $v4 verify --bundle (Join-Path $base 'v1-x86-normal') --remote
```

Gitea 仅接受固定来源 `http://firekula.site:3000` 的 Release 附件路径或结构化附件记录中的 `/attachments/<UUID>`，拒绝跨站重定向、凭据、查询串及片段。HTTP 例外只属于此测试来源，不改变 GitHub／Gitee 的 HTTPS 要求。发现流程按 Gitea 的 `limit=50` 分页；连续三页满页而不能确认遍历结束时失败，不以不完整结果宣称最新版。

Gitee 适配仍保留：先从指定仓库／Release 的 `attach_files` 列表取得附件 ID，再通过规范下载接口取得临时地址。已验证的 `foruda.gitee.com/attach_file/...` 重定向可能有 `attname`、`token`、`ts` 签名查询参数；这些是短时附件地址，不是玩家登录凭据，不持久化或记录其值。HTML 登录页／验证码、陌生来源、截断或哈希错误都失败。实际公开样本忽略 Range 返回 200，因此不能承诺所有 Gitee附件都支持206续传。

## 无 Token 的手动上传交付

此前 Gitee 的 `v0.1.8-v4test.1` 附件已上传，但最终匿名复核受 HTTP 403 限流阻断，第二版未发布；用户随后改用现有 Gitea 的全新 `v0.1.8-giteatest.1`／`.2` 标签。以下保留手动上传方法。GitHub 正式发布没有修改，测试标签使用 Gitea 中已经存在的提交，不推送其他工作区内容。生成的 `handoff.json` 区分实际构建的源码提交与公开仓库标签所指提交，并记录每个输入、ZIP 和附件的 SHA256。

每个标签的上传包包含 `01-resources/`、`02-manifests/`、`UPLOAD.md` 和校验和。先上传并匿名验证所有资源，再上传四份清单；x86/x64 × normal/test 共用一个不可变标签，公共资源只上传一次。只将两个编号目录中的原始文件作为 Release 附件，保留文件名；ZIP 与说明供人工交付，不代替实际资源或清单附件。发布 `.2` 前保留并验证 `.1` 的资源，`.2` 的清单会引用它们。

本轮 `v0.1.8-giteatest.1`／`.2` 使用同一次实际构建：`.2` 为资源更新，复用 `.1` 登录器、客户端 DLL 和测试版 debug.dll 的完整字节，因此二进制版本仍为 `.1`。只改变未被游戏引用的 `UpdateTestAssets/probe.npk`，同目录 `probe.pvf` 保持不变。两份 probe 是合成传输测试数据，**不是有效游戏资源**；绝不生成或替换假的根目录 `Script.pvf`。安装时仍需用户已有的合法兼容游戏文件。

配置测试中，`.1` 将 7110 并入 Relay.TcpPorts，`.2` 保留此前迁移再并入 7111、7112；端口仍写成逗号分隔字符串。用户配置不是可上传资源。供初次启动的桥接包／独立修复副本与这些 Release 附件分别交付。

本地校验能证明附件、跨版本引用、逐文件重组与清单一致，不能证明远端网页上传成功。上传完成后，逐个 bundle 运行上文的 `verify --remote`，再执行 `.1`→`.2`、跨版直达、损坏修复和断网启动验收。若出现部分上传，保留现场并人工审计，或换新标签和递增序号重新生成；不要覆盖已有标签或附件。

## 安装恢复与诊断

文件、配置迁移及安装记录使用同一事务；安装记录最后提交。发生中断先恢复，再判断是否可启动。备份保存的是原始实际字节，即使它原来已损坏；“回滚成功”不等于“旧版健康”。账户／设置写入需要与安装互斥并读取最新配置。提交前处理文件占用，只能在玩家同意后终止可终止的占用进程；助手交接、独立修复程序和实机锁文件验收仍以最终测试报告为准。

独立自包含程序 `DFLegacy.Repair.exe --repair-client <目录>` 不执行安装目录中已损坏的登录器。x86/x64 两份实际程序均完成本地离线验证：完整 V4 安装返回成功，受管理 DLL 损坏且停用联网时拒绝放行，原配置字节保持不变。两架构还完成真实 Gitea 联网修复并生成单独 update 报告。它只处理 V4；旧 V3 未决事务需先按桥接恢复指引处理。修复程序必须放在被修复目录之外。

报告写入 `Logs/DFLegacy/Incidents/`，文件名包含时间、PID 和事件 ID：

| 前缀 | 证据与用途 |
| --- | --- |
| `error-` | 启动、配置、账号或更新异常，带阶段、异常链与关联更新 ID。 |
| `networkerror-` | 有原因与协议／方向／连接 ID／字节计数的中继异常，以及签名验证通过的原生连接失败／断线通知。 |
| `crash-` | 原生未处理异常或确认的异常退出；非零退出码本身不证明某个原生异常，缺少现场会明确标注。 |
| `update-` | 每次校验／下载／安装的摘要和受限逐文件结果，成功也保留；超出预算的明细明确截断。 |

报告包含版本、架构、时间／时区、会话和事件 ID、错误码及有界上下文；凭据脱敏、重复异常合并并限制保留时间／数量／总量，不自动上传。服务器版本无可靠接口时记录未知。正常退出、正常 EOF／频道切换、用户取消及可处理的探针异常不应当作事故。CrashSender 弹窗但进程仍存活的入口尚未验证时，继续使用[显式详细抓取工具](tools/debug/crash_capture/README.md)；普通启动不默认附加调试器。转储可能包含进程内存，反馈前单独审查。

旧客户端先通过相应 V3 桥接包升级登录器，再使用 V4 管理资源；原 `release.py build-client-packages --updates-only --script-pvf ...` 与 `github_publish.py` 未改写。本次也交付四个 `starter-clients` 手动替换工具包，需在隔离游戏副本解压后执行包内 `Enable-Gitea-Test.ps1`，才会切到 Gitea 测试源；脚本备份现有配置并保留其他字段。四包通过生产清单解析器验证，实际程序和资源完成本地与 Gitea 远端更新验收。旧版正式 GitHub 桥接仍未发布或远端验收。

本轮 `.2` 只更新测试资源，二进制及 starter 的 `client-package.json` 仍为 `.1`。实际 V4 安装版本查看 `.dflegacy/v4/installed.json` 中的 `release.tag`；不能仅根据旧安装包清单判断资源版本。现在最新测试版本是 `.2`，新启动会直接安装它，A→B 流程已由自动验收完成。

## 自动基本故障报告（Unreleased）

普通版／测试版、x86／x64 都必须包含对应架构的 `DFLegacy.Diagnostics.exe` 与 Win32 `DFLegacy.CrashCapture.exe`，以及既有固定入口、登录器、更新器和补丁；测试版另含 `debug.dll`。上传助手采用独立自包含单文件，避免依赖机器上安装的 .NET，代价是增加下载体积；V4 仍按实际变化的文件下载。固定原生入口保持前次字节身份。旧 schema-2 读取兼容不含助手的旧包；新资源生成器要求完整助手对。

基本上报默认开启，无首次同意弹窗。当前用户开关保存在 `%LOCALAPPDATA%/DFLegacy/Diagnostics/preference.json`，优先于复制的 `launcher.json` 默认值。更换接收站点或关闭／重新开启都会隔离旧队列；复制客户端目录不会上传另一用户旧记录。设置显示实际接收源、HTTP「未加密」、待发／保留／已发送数及最后回执。`Diagnostics.ServiceUrl` 留空时使用实际选中服务器的账号 API 地址；配置不得指向 GM 或玩家 HTTP 端口。

`Logs/DFLegacy/Incidents` 仍是本地详细事故记录，不作为上传来源。自动队列只包含有界结构化基本 JSON：事件、版本、模块相对位置及有限系统字段；无账号、密码、令牌、聊天、截图、完整配置、路径或内存附件。GPU 驱动／容量仅查询首个枚举显示适配器的有限注册表值，缺失为 unknown；不代表实际游戏渲染器。`system.processArchitecture` 是收集进程架构、`uptimeSeconds` 是操作系统启动时间；顶层 architecture 是事故生产者架构。未观察到的游戏分辨率／窗口／渲染器保持 unknown。

原始小型转储默认仅保存在当前用户 `captures`，独立设置下次游戏启动生效，最多两份、每份 16 MiB；新接收器和上传助手没有转储上传接口。原版 BugTrap 历史外传抑制仅对[已验证 ACT1 原版哈希](knowledge/decisions/2026-11-09-native-diagnostic-capture.md)成立，未知第三方模块不作同等保证。正常退出、恢复异常、正常 EOF、用户取消不算 crash；现场、事后快照、未知异常退出分别标记。

启动时助手可能已占用待更新的程序。更新／修复交接前只向本次拥有的会话发送 capability 验证的关闭标记，保留创建所得 Process 句柄并最多等待 12 秒（助手最终收尾最多 10 秒），再执行原有文件锁预检；不按进程名杀进程。安装失败或取消时重新建立上报会话。游戏仍存活时不停止其原生捕获助手，需先退出游戏。有效安装中的运行时助手不可用保持游戏启动容错；离线缺失／损坏受管理助手则继续阻止启动，联网按可信清单修复，二者边界不混淆。

发布资源只接受固定公共助手文件名。诊断目录、会话／capability、偏好、队列、转储和原有私人配置全部排除；即使误复制到游戏目录，也不能通过宽泛 V4 包选择进入清单。Manager、client-assets 和离线打包器导出的客户端均包含同架构上传助手及 x86 捕获器。

本地完整性补充：登录器在更新检查之前启动上传助手，因此必须先按已安装 V4 记录（优先）或有效旧 schema-2 清单验证助手大小／SHA256，并保持防写入／删除句柄直到创建进程返回。无可信身份、损坏记录、哈希或大小不匹配时只保留本地记录，等待现有可信更新修复，不先执行程序。该修订晚于 `.5` 中间快照，已进入最终隔离预发布 `.6`（构建源 `64f5ab5`），完成匹配私有生产符号、公开资源检查和四变体真实 Gitea 修复；`.5` 保持原字节。
