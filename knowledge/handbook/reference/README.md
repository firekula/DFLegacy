# 参考表范围与重建

这些 TSV 是定位工具，不是运行状态或功能覆盖率证明。基线见 [当前状态](../current-state.md)。表格保持 UTF-8，可用编辑器、`rg` 或电子表格查看。所有 `path` 均相对仓库根目录。

| 文件 | 范围 | 限制 |
| --- | --- | --- |
| [files.tsv](files.tsv) | Git 跟踪的 src/tests/tools/deploy/protocol/third_party/.gitea 文件及根非 Markdown 文件 | 不枚举游戏目录、忽略的构建物、外部私有资产；分类只是定位提示 |
| [documents.tsv](documents.tsv) | Git 跟踪的既有 Markdown 与首标题，排除本手册 | 标题不是当前状态；旧路径/结论可能过时 |
| [configuration-properties.tsv](configuration-properties.tsv) | 列明配置源文件的 public 自动属性声明及源码行号 | 不展开继承、位置 record 参数、运行期赋值或所有配置解析分支；不是完整 JSON schema |
| [http-routes.tsv](http-routes.tsv) | src 下 C# `MapGet/Post/Put/Delete/Patch` 的字面量路径和接收变量 | group_prefix 只解析同文件 `var x = y.MapGroup("...")`；实际路由还有鉴权、端口和动态注册约束 |

检索示例（在仓库根目录）：

```powershell
rg -n 'Cemetery|Dungeon|Warehouse' knowledge/handbook/reference/files.tsv
rg -n 'AdvertisedHost|DataPath' knowledge/handbook/reference/configuration-properties.tsv
rg -n 'diagnostics|/config' knowledge/handbook/reference/http-routes.tsv
rg -n '墓地|版本|native' knowledge/handbook/reference/documents.tsv
```

## 重建步骤

在仓库根目录将下面 Python 代码保存到 `.tmp/generate_handbook_reference.py`，再运行 `python .tmp/generate_handbook_reference.py`。只读取 Git 文件列表和文本内容，覆盖本目录四份 TSV；不会启动服务、扫描运行配置或访问网络。依赖 Python 标准库和 Git。新增源码文件需已经加入 Git 索引才会纳入列表。重建后检查 diff；源码行号在代码变化后应刷新。

```python
from pathlib import Path
import csv
import io
import re
import subprocess

root = Path(subprocess.check_output(
    ['git', 'rev-parse', '--show-toplevel'], text=True).strip())
paths = sorted(p for p in subprocess.check_output(
    ['git', 'ls-files', '-z'], cwd=root).decode('utf-8').split('\0') if p)
out = root / 'knowledge/handbook/reference'
out.mkdir(parents=True, exist_ok=True)

def read(path):
    return (root / path).read_text(encoding='utf-8-sig', errors='replace')

def write(name, header, rows):
    stream = io.StringIO(newline='')
    writer = csv.writer(stream, delimiter='\t', lineterminator='\n')
    writer.writerow(header)
    writer.writerows(rows)
    (out / name).write_bytes(stream.getvalue().encode('utf-8'))
    print(name, len(rows))

def kind(path):
    lower = path.lower()
    ext = Path(path).suffix.lower()
    if ext == '.md':
        return '说明'
    if '/tests/' in lower or lower.startswith('tests/') or '_tests.' in lower or Path(path).name.startswith('test_'):
        return '测试或测试支持（以文件内容为准）'
    if ext in {'.csproj', '.vcxproj', '.slnx', '.props', '.targets'} or Path(path).name == 'CMakeLists.txt':
        return '项目/构建定义'
    if ext in {'.json', '.yaml', '.yml', '.ini', '.xml', '.toml', '.tsv'}:
        return '配置/数据/清单'
    if ext in {'.cs', '.cpp', '.c', '.h', '.hpp', '.py', '.ps1', '.cmd', '.sh', '.command', '.java', '.js', '.cjs', '.xaml', '.html', '.htm', '.css'}:
        return '实现/脚本/界面'
    return '资源/许可证/其他'

prefixes = ('src/', 'tests/', 'tools/', 'deploy/', 'protocol/', 'third_party/', '.gitea/')
selected = [p for p in paths if p.startswith(prefixes) or ('/' not in p and not p.endswith('.md'))]
rows = [('/'.join(p.split('/')[:2]) if '/' in p else '(root)', kind(p), p) for p in selected]
write('files.tsv', ['group', 'kind', 'path'], rows)

rows = []
for path in paths:
    if not path.lower().endswith('.md') or path.startswith('knowledge/handbook/'):
        continue
    title = next((line.lstrip('#').strip() for line in read(path).splitlines() if re.match(r'^#{1,6}\s+', line)), '')
    rows.append((path, title))
write('documents.tsv', ['path', 'first_heading'], rows)

configs = [
    'src/DFLegacy.Server/ServerOptions.cs',
    'src/DFLegacy.Server/ProcessOptions.cs',
    'src/DFLegacy.Server/PvpOptions.cs',
    'src/DFLegacy.Server/FallbackRelayOptions.cs',
    'src/DFLegacy.Manager/ManagerSettings.cs',
    'src/DFLegacy.Launcher/Models/LauncherOptions.cs',
    'src/DFLegacy.Diagnostics.Core/DiagnosticSettings.cs',
]
rows = []
for path in configs:
    current = ''
    for number, line in enumerate(read(path).splitlines(), 1):
        type_match = re.search(r'\b(?:class|record|struct)\s+(\w+)', line)
        if type_match and not line.lstrip().startswith('//'):
            current = type_match.group(1)
        if re.match(r'\s*public\s+.+\{\s*get;', line):
            rows.append((path, number, current, line.strip()))
write('configuration-properties.tsv', ['path', 'line', 'declaring_type', 'declaration'], rows)

rows = []
for path in paths:
    if not path.startswith('src/') or not path.endswith('.cs'):
        continue
    source = read(path)
    groups = {name: prefix for name, prefix in re.findall(
        r'var\s+(\w+)\s*=\s*\w+\.MapGroup\(\s*"([^"]+)"', source)}
    for match in re.finditer(r'(\w+)\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"', source):
        receiver, method, route = match.groups()
        rows.append((path, source.count('\n', 0, match.start()) + 1,
                     method.upper(), receiver, groups.get(receiver, ''), route))
write('http-routes.tsv', ['path', 'line', 'method', 'receiver', 'group_prefix', 'route_literal'], rows)
```

检查两次执行结果应完全一致。列表缺文件时先检查 `git ls-files` 与忽略规则，不通过枚举整块硬盘补齐；本机账号、存档、私钥和游戏资源不属于索引目标。
