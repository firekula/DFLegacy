# pvf-dump 地图勘察工具

用途：说明相邻 `pvf-dump/` 小工程，避免把临时工具当成通用 PVF CLI。对应源码为 [Program.cs](../../../pvf-dump/Program.cs) 与 [pvf-dump.csproj](../../../pvf-dump/pvf-dump.csproj)。当前没有独立 Git 仓库，也不属于 DFLegacy 跟踪文件；换机需额外保留这两个源码文件或用 PVFStudio CLI 完成同类读取。本页保存在 DFLegacy 内作为便携说明。

## 实际行为

- .NET 10 控制台，项目引用 `../DFLegacy/src/DFLegacy.Protocol/DFLegacy.Protocol.csproj`。
- 源 PVF 路径硬编码为原工作机 `G:\Game\DNF_ACT1\DF\Script.pvf`；**不能通过命令行切换包路径**。换机必须核对/调整 `Program.cs` 或选择另一个已有工具，不能把路径作为参数传入。
- 用 `PvfArchiveReader` 读 `map/map.lst`，按 CP936 解码，再用数字 ID 找 `map/` 下文件。
- 参数是一个或多个整数地图 ID，默认 `4`。无效整数会抛异常，不存在的 ID/文件会打印缺失提示。
- `DUMP_TAGS=1` 打印标签，优先于 `DUMP_FULL=1`；后者只打印前 **70 行**，不是完整文件。
- 默认输出地图相对路径、`dungeon start area`、`player number` 和第一行 `monster`。源码注释提到的 `[path info]` 并非当前默认输出字段；读取的 `pathgate pos` 当前没有打印。
- 只读游戏资源；不创建草稿、不修改 PVF、不验证全部包结构。它依赖的读取器只支持 DNF_SCRIPT_PACK 封装。

## 已核对路径后的运行示例

在 `pvf-dump/` 目录执行，先确认硬编码资源属于预期章别：

```powershell
dotnet run --project pvf-dump.csproj -- 4 5
```

它会产生正常 .NET `bin/obj` 构建输出。不要把成功打印一张地图当作全资源验证。

需要标签模式时记录并恢复当前进程的环境变量：

```powershell
$previousDumpTags = $env:DUMP_TAGS
try {
    $env:DUMP_TAGS = '1'
    dotnet run --project pvf-dump.csproj -- 4
    if ($LASTEXITCODE) { throw 'Map inspection failed' }
} finally { $env:DUMP_TAGS = $previousDumpTags }
```

选择工具建议：只读一个地图字段可用此工具；多格式读取/完整导出条目/脚本编解码用 [PVFStudio CLI](../../../PVFStudio/docs/handbook/cli-and-api.md)；第二章双封装迁移用 [资源子项目协作](resource-subprojects.md) 中的专门链路。
