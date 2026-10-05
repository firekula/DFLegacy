# 官服玩法面证据链工程 · M0 与 M1 驱动器前置 —— 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 产出 `df_game_r` 的**全量派发器/处理器/命令映射**（M0 的"分母"），并建成**能在已知答案上自检通过的最小客户端驱动器**（M1 的前置门）——让后续每个玩法单元都有一张"知道我们不知道什么"的总表和一个可自动回归的靶子。

**Architecture:** 三个独立层次：①**静态层**——用 sidecar `c++filt` 把 342,530 个 mangled 符号解名，抽出全量派发器目录，再把 `CMD 0..607`/`NOTI 0..562` 映射到派发器与处理器；②**证据层**——四条可证伪判据 + 一次人机采集批次，把"目录是全的"与"官方游戏面长什么样"变成有原始字节支撑的事实；③**工具层**——把可复用能力申报进 `.recon`，把一次性脚本的结论写进知识层后退役。

**Tech Stack:** Python 3.12（本机 `python`）、Docker（容器 `dnf` 是活靶、sidecar `dfl-recon` 提供 `c++filt`/binutils）、IDA Pro 8.3（`idat.exe` + `df_game_r.bin.idb`）、Git Bash（`D:/Program Files/Git/bin/bash.exe`，仅 IDA 包装链需要）、`.recon` 工具注册表。

**Spec:** `docs/superpowers/specs/2026-10-05-gameplay-evidence-program-design.md`（**执行者必须同时读它**：本计划只覆盖该 spec 的 M0 + M1 驱动器前置，见 spec §5.5）

## Global Constraints

- **工作区**：`G:\Game\DNF_ACT1\DFLegacy`。本计划所有产物落在**新的**程序目录
  `P = G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program`（下称 `$P`），
  结构：`$P\work\`（脚本）、`$P\work\out\`（产物）、`$P\work\captures\`（抓包）、`$P\work\driver\`（驱动器）。
- **两个既有目录（只读引用，不要改动）**：
  - `O = G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official`（官服二进制与符号转储）
  - `W = G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work`（登录链那轮的脚本、抓包与 `elfrecon.py`）
  下文一律用 `$O` / `$W`，**不要**再各自定义（上一版计划里出现过 `$P\..\..\..` 这种脆弱相对路径，已废）。
- **本计划的范围**：**只有** spec 的 M0（工具与分母）+ M1 的驱动器前置。
  **驱动器的"2008 方言"不在本计划内** —— 它属于 M1 各单元（L2b 靶），帧层本身与方言无关（方言由**帧脚本**提供）。
- **不改产品代码**：本计划**不碰** `src/`、`tests/`、`STATUS.md`、`server.json`。只产出分析产物与 `.recon` 申报。
- **工作区有大量无关未提交改动**：每次提交**必须用 pathspec 限定**（`git commit -m "..." -- <paths>`），
  绝不 `git add -A`。`docs/` 与 `.superpowers/` 被 gitignore ⇒ 需 `git add -f`。
- **证据纪律**（`.recon/onboarding.md` §4）：六类标记 `[静态] [抓包] [真机] [回放] [推断] [未验证]` + `[作废]`；
  被推翻的结论**不删原文**，标 `[作废]` 并指向推翻它的证据；每条写进知识层的结论**必须附一条可重跑命令**。
- **探针纪律**：临时脚本命名可识别、观测完退役、**不进入生产路径**；价值写进结论而非留在代码里。
- **活靶纪律**：容器 `dnf` 是跑着的完整官服栈。**只读优先**；任何写操作要在批次清单里显式列出；
  批次结束必须恢复环境（`open-questions.md` 坑 14 的互斥处理）。
- **端口与世代（易踩）**：宿主 `TCP 7001`、`UDP 2311` 现被 `DFLegacy.Server`（pid 17428）占着，
  容器侧 `7001/tcp -> <none>`、`2311/udp -> <none>`；容器已发布的是 `30011→宿主 30021`、`31011→宿主 31021`、`5505`。
  **2008 客户端 = 2311 握手 / 7001 登录+玩法；官服那代 = 7001 握手 / 30011 游戏**，两代端口**整体错开一格**。
- **编码坑**：Windows 上 Python `Path.read_text()` 默认 GBK ⇒ 读任何文本**显式 `encoding="utf-8"`**。
- **PowerShell 坑**：`python x.py | Tee-Object out.txt | Select-Object -First N` 会**截断落盘文件**（上游被提前终止）
  ⇒ 落盘一律用重定向 `> out.txt`；参数名不要用 `$Pid`（只读自动变量，大小写不敏感）。
- **权限坑**：若 `python`/`pwsh` 在某个目录 `Permission denied`（而编辑工具能写），那是 Windows 文件权限退化，
  用 `diagnose-windows-sandbox-acl` 的脚本修（本会话已修过一次，回滚脚本在 `G:\Game\DNF_ACT1\DFLegacy._acl-report\`）。
- **IDA 单写**：同一时刻只允许一个 IDA 任务；`df_game_r.bin.idb` 是 170 MB，一次加载**分钟级**。
- **先写预期再跑**：每个任务都要先写下"如果失败会看到什么"，否则它不是实验。
- **提交粒度**：每个任务末尾提交一次，提交信息写清"产出 + 证据类别"。

---

### Task 1: 环境与路径体检（前置门）

**Files:**
- Create: `$P\work\env-baseline.txt`（体检输出留档）
- Create: `$P\work\out\`、`$P\work\captures\`（目录）

**Interfaces:**
- Produces: `$P` 目录结构；`env-baseline.txt` 里记录**宿主端口占用者 pid**、**容器发布端口**、**关键资产是否存在**、
  **本机 demangler 可用性**。后续所有任务假定这些值为真。

- [ ] **Step 1: 写"失败的形状"**

本任务失败 = 下列任一为假，且**不要**当成"稍后再说"：写不出 `$P` 下的文件（权限退化）、
`dfl-recon` 里没有 `c++filt`、`df_game_r.bin`/`.idb` 缺一、`opcode_cmd.txt`/`opcode_noti.txt` 缺一、`docker` 不可用。

- [ ] **Step 2: 建目录并采集基线**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work"
New-Item -ItemType Directory -Force -Path "$P\work\out","$P\work\captures","$P\work\driver" | Out-Null
@(
  "== date ==", (Get-Date -Format o),
  "== docker ==", (docker version --format '{{.Server.Version}}' 2>&1),
  "== containers ==", (docker ps --format '{{.Names}} {{.Status}}' 2>&1),
  "== sidecar c++filt ==", (docker exec dfl-recon sh -lc 'command -v c++filt' 2>&1),
  "== host 7001/2311 owners ==", (Get-NetTCPConnection -State Listen -LocalPort 7001,2311 -ErrorAction SilentlyContinue | ForEach-Object { "$($_.LocalPort) <- pid $($_.OwningProcess)" }),
  "== container ports (json) ==", (docker inspect dnf --format '{{json .NetworkSettings.Ports}}' 2>&1),
  "== assets ==", (@("$O\df_game_r.bin","$O\df_game_r.bin.idb","$O\opcode_cmd.txt","$O\opcode_noti.txt","$O\game_r.syms.txt","$O\packet_senders.txt","$O\catalog_dispatchers.txt","$W\elfrecon.py","$W\pcap-streams.py","$W\container-privatekey.pem","$W\container-game-publickey.pem") | ForEach-Object { "$(Test-Path $_) $_" }),
  "== ida ==", (Test-Path "D:\Program Files (x86)\IDA_Pro_8.3\idat.exe")
) | Out-File "$P\work\env-baseline.txt" -Encoding utf8
Get-Content "$P\work\env-baseline.txt"
```

- [ ] **Step 3: 判定并记录**

预期形状：`dfl-recon` 打印 `/usr/bin/c++filt`；11 个资产全 `True`；`idat.exe` `True`；
宿主 7001/2311 显示同一个 pid（本会话实测是 **17428**，即 `DFLegacy.Server`）；
容器端口 JSON 里 `7001/tcp` 与 `2311/udp` 是 `null`（= 没绑上），而 `30011/tcp→30021`、`31011/udp→31021`、`5505/tcp→5505` 有值。
**若宿主端口为空**（说明 DFLegacy 没在跑）⇒ 在 `env-baseline.txt` 末尾记一行 `NOTE: 7001/2311 free at baseline`，
并**在 Task 8 之前不要启动任何占用它们的进程**。

- [ ] **Step 4: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/env-baseline.txt
git commit -m "证据链工程 M0：环境与路径基线（[真机] 只读体检）" -- .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/env-baseline.txt
```

---

### Task 2: 解名 352,694 个符号 → `demangled-syms.tsv`

**Files:**
- Create: `$P\work\demangle-syms.py`
- Create: `$P\work\out\demangled-syms.tsv`
- Create: `$P\work\out\demangle-check.txt`（验收输出）

**Interfaces:**
- Produces: `demangled-syms.tsv`，**制表符分四列** `addr\tsize\tTYPE\tname`，行数与源文件**逐行对应**。
  后续 Task 3/4/5 都从这一份消费，**不再读原 mangled 文件**。
- 约定：源文件 `$O\game_r.syms.txt` 的三列格式为 `addr\tsize\tTYPE\tname`（name 为第 4 列，可能含空格/括号）。

- [ ] **Step 1: 先写下验收与"失败的形状"**

验收三条：① 行数等于源文件行数（**352,694**）；② 抽样 5 个已知符号解名正确；③
**含 `Dispatcher` 的行数从 0 变成 > 0**（源文件里 demangle 后的 `Dispatcher_*` 条目为 **0**，这是本任务的可证伪判据）。
失败形状：`c++filt` 行数与输入不一致（脚本会 `assert`）、或第 ①②条不满足。

- [ ] **Step 2: 写脚本**

```python
#!/usr/bin/env python3
"""demangle-syms.py — 把 nm 转储的第四列名字批量 demangle。

为什么用 sidecar 的 c++filt：本机 python `cxxfilt` 不可用
（`LibraryNotFound: Cannot find any of libraries: ('c',)`，Windows 上找不到 libc），
而 `dfl-recon` 里有 binutils 2.44 的 c++filt（2026-10-05 实测可用）。
sidecar 的 netns 可能已失效 —— 那是抓包的事，纯 CPU 任务不受影响。

输入/输出都是 `addr\tsize\tTYPE\tname` 四列 TSV，行数必须一一对应。
用法: demangle-syms.py <src.syms.txt> <dst.tsv>
"""
import subprocess
import sys
from pathlib import Path

CXXFILT = ["docker", "exec", "-i", "dfl-recon", "c++filt"]


def main(src: str, dst: str) -> None:
    rows = Path(src).read_text(encoding="utf-8", errors="replace").splitlines()
    names = []
    for ln in rows:
        parts = ln.split("\t", 3)
        names.append(parts[3] if len(parts) >= 4 else "")
    proc = subprocess.run(CXXFILT, input="\n".join(names).encode(),
                          capture_output=True)
    if proc.returncode != 0:
        sys.exit(f"c++filt 退出码 {proc.returncode}: {proc.stderr[:400]!r}")
    demangled = proc.stdout.decode("utf-8", errors="replace").splitlines()
    assert len(demangled) == len(rows), \
        f"c++filt 行数不一致: {len(demangled)} vs {len(rows)}"
    out = []
    for ln, dem in zip(rows, demangled):
        parts = ln.split("\t", 3)
        if len(parts) >= 4:
            parts[3] = dem
        out.append("\t".join(parts))
    Path(dst).write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"{len(out)} rows -> {dst}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
```

- [ ] **Step 3: 跑之前先量"before"**

```powershell
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
(Get-Content "$O\game_r.syms.txt" | Measure-Object -Line).Lines
(Get-Content "$O\game_r.syms.txt" | Where-Object { $_ -match '\bDis[Pp]atcher_' }).Count
```
预期：`352694` 与 **`0`**（第二个 0 就是本任务的判据基线；若非 0，说明源文件已被人解名过，记录实际情况）。

- [ ] **Step 4: 执行解名**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
python "$P\work\demangle-syms.py" "$O\game_r.syms.txt" "$P\work\out\demangled-syms.tsv"
```
预期末行：`352694 rows -> ...\demangled-syms.tsv`。耗时量级：几十秒（一次性把 35 万行喂给容器）。

- [ ] **Step 5: 跑验收**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$D = "$P\work\out\demangled-syms.tsv"
$sel = Select-String -Path $D -Pattern 'onCS_ASK_CHANNEL_INFO|DisPatcher_Login::dispatch_sig|account_password_check|CSimpleSSO::checkValidPassword|CRijndael::EncryptBlock'
@(
  "rows = $((Get-Content $D | Measure-Object -Line).Lines)",
  "Dispatcher lines = $((Get-Content $D | Where-Object { $_ -match '\bDis[Pp]atcher_' }).Count)",
  "--- samples ---", $sel | ForEach-Object { $_.Line }
) | Out-File "$P\work\out\demangle-check.txt" -Encoding utf8
Get-Content "$P\work\out\demangle-check.txt"
```
**必须**看到：`rows = 352694`、`Dispatcher lines > 0`、5 个抽样里至少 3 个解成可读的 C++ 名
（`onCS_ASK_CHANNEL_INFO` 与 `CSimpleSSO::checkValidPassword` 在 `game_r` 里应存在；不存在就如实记录"该符号不在这个二进制里"，**不要**改判据去迁就）。

- [ ] **Step 6: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/demangle-syms.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/demangle-check.txt
git commit -m "证据链工程 M0：符号解名工具与验收（[静态] 352694 行，Dispatcher 条目 0 -> N）" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/demangle-syms.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/demangle-check.txt
```
（`demangled-syms.tsv` 是 20+ MB 的派生物，**不进 git**，在接续块里写明"可由工具重跑生成"。）

---

### Task 3: 全量派发器目录 `catalog_dispatchers_full.txt`

**Files:**
- Create: `$P\work\extract-dispatchers.py`
- Create: `$P\work\out\catalog_dispatchers_full.txt`
- Reference（对照，不修改）: `$O\catalog_dispatchers.txt`（既有 **84** 条：45 条有真 `dispatch_sig`）

**Interfaces:**
- Produces: `catalog_dispatchers_full.txt`，每行
  `<family>\tprocess=0x<addr>(<size>)\tdispatch_sig=0x<addr>(<size>)\tfile=<所属 .cpp（若符号表里有 FILE 关系）>`
  家族名统一去前缀（`Dispatcher_X` 与 `DisPatcher_X` 与 `ARAD::Arad_Dispatcher_X_Impl` 归并为 `X`，并在行内保留原始拼写）。
- Task 4/5/7 消费本文件的家族名与地址。

- [ ] **Step 1: 写"失败的形状"**

失败 = ① 家族数为 0（解名没生效）；② 既有 84 条里有条目在新目录里找不到且无法解释；③ 有家族没有 `dispatch_sig` 且没有 `process`（无地址的条目不是证据）。

- [ ] **Step 2: 写脚本**

```python
#!/usr/bin/env python3
"""extract-dispatchers.py — 从解名后的符号表抽出全量派发器家族。

三种命名族都要收（Neople 自己不一致）：
  DisPatcher_X::dispatch_sig / DisPatcher_X::process
  Dispatcher_X::...            （既有 catalog 用的是这个拼写）
  ARAD::Arad_Dispatcher_X_Impl::...
归一化家族名 = 去掉上述前后缀的 X；行内保留原始拼写以便回溯。

用法: extract-dispatchers.py <demangled-syms.tsv> <out.txt>
"""
import re
import sys
from pathlib import Path

PATTERNS = [
    re.compile(r"(?:^|::)(?:DisPatcher|Dispatcher)_([A-Za-z0-9_]+?)(?:::|\()"),
    re.compile(r"ARAD::Arad_Dispatcher_([A-Za-z0-9_]+?)_Impl(?:::|\()"),
]


def rows(p):
    for ln in Path(p).read_text(encoding="utf-8", errors="replace").splitlines():
        parts = ln.split("\t", 3)
        if len(parts) < 4:
            continue
        addr, size, typ, name = parts
        try:
            addr_i, size_i = int(addr, 16), int(size)
        except ValueError:
            continue
        yield addr_i, size_i, typ, name


def family_of(name):
    for rx in PATTERNS:
        m = rx.search(name)
        if m:
            return m.group(1)
    return None


def main(src, dst):
    fams = {}
    for addr, size, typ, name in rows(src):
        fam = family_of(name)
        if fam is None:
            continue
        rec = fams.setdefault(fam, {"process": None, "dispatch_sig": None, "spellings": set()})
        rec["spellings"].add(name.split("(")[0])
        if name.endswith("dispatch_sig") or "::dispatch_sig(" in name:
            rec["dispatch_sig"] = (addr, size)
        elif name.endswith("process") or "::process(" in name:
            if rec["process"] is None or size > rec["process"][1]:
                rec["process"] = (addr, size)
    lines = [f"# df_game_r 全量派发器目录（生成自 demangled-syms.tsv；{len(fams)} 个家族）"]
    for fam in sorted(fams):
        r = fams[fam]
        proc = f"0x{r['process'][0]:08x}({r['process'][1]:5d})" if r["process"] else "0x00000000(    0)"
        sig = f"0x{r['dispatch_sig'][0]:08x}({r['dispatch_sig'][1]:5d})" if r["dispatch_sig"] else "0x00000000(    0)"
        spell = " | ".join(sorted(r["spellings"])[:3])
        lines.append(f"{fam}\tprocess={proc}\tdispatch_sig={sig}\t{spell}")
    Path(dst).write_text("\n".join(lines) + "\n", encoding="utf-8")
    n_sig = sum(1 for r in fams.values() if r["dispatch_sig"])
    print(f"{len(fams)} families, {n_sig} with dispatch_sig -> {dst}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
```

- [ ] **Step 3: 跑**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
python "$P\work\extract-dispatchers.py" "$P\work\out\demangled-syms.tsv" "$P\work\out\catalog_dispatchers_full.txt"
```
预期：打印 `N families, M with dispatch_sig`，其中 **N 应远大于 84**（既有目录只覆盖组队/副本子集）。**把实测的 N、M 原样记录**——这两个数就是 spec §5.4 要的"分母"的第一部分。

- [ ] **Step 4: 与既有 84 条对照（同源交叉，spec §2.2 判据 3）**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
$old = (Get-Content "$O\catalog_dispatchers.txt" | Where-Object { $_ -match '^Dispatcher_' } | ForEach-Object { ($_ -split '\s+')[0] -replace '^Dispatcher_','' })
$new = (Get-Content "$P\work\out\catalog_dispatchers_full.txt" | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { ($_ -split "`t")[0] })
"old=$($old.Count) new=$($new.Count)"
"missing in new: " + (($old | Where-Object { $_ -notin $new }) -join ', ')
"new but not old: $((($new | Where-Object { $_ -notin $old }).Count)) families"
```
预期：`missing in new` **为空**（老目录是子集）；`new but not old` 的数量即"之前没清的库存"。
**若 missing 非空** ⇒ 逐个解释（是真的没有 `Dispatcher_` 前缀，还是我的正则漏了某种拼写）——**不许跳过**。

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/extract-dispatchers.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/catalog_dispatchers_full.txt
git commit -m "证据链工程 M0：全量派发器目录（[静态] 家族数从 84 扩到 N）" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/extract-dispatchers.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/catalog_dispatchers_full.txt
```

---

### Task 4: `opcode-map.tsv` —— 分母的核心（1171 条命令 → 派发器）

**Files:**
- Create: `$P\work\elfrecon.py`（**从 `$W\elfrecon.py` 原样复制**，不改内容；避免跨目录 import 的相对路径问题）
- Create: `$P\work\game-scan-imm.py`（照抄已验证的按节扫描方式）
- Create: `$P\work\map-opcodes.py`
- Create: `$P\work\out\opcode-map.tsv`、`$P\work\out\opcode-map-residual.txt`
- Reference: `$O\opcode_cmd.txt`（608 条）、`$O\opcode_noti.txt`（563 条）、`$O\packet_senders.txt`（1248 个 `put_header` 点）

**Interfaces:**
- Produces: `game-scan-imm.py <elf> --targets 0xVA [...] [--limit N]` —— 打印每个目标地址的命中指令。
- Produces: `map-opcodes.py <cmd.txt> <noti.txt> <catalog_dispatchers_full.txt> <out.tsv>` ——
  输出 TSV，列 `kind(CMD|NOTI)\tid\tname\trodata_va\tdispatcher\tstatus`，
  其中 `status ∈ {mapped, no-dispatcher, unused, inter-server, unknown}`。
  **`unknown` 就是 spec §2.3 的失败模式，必须计数并进残余报告。**
- Task 7 消费：`unknown` 计数是 M0 的退出判据之一。

- [ ] **Step 1: 写"失败的形状"并先看表结构**

失败形状：① `unknown` 占比很高（说明注册是运行期构造 ⇒ 按 spec §2.3 改判）；② 找不到任何 xref（说明 `.rodata` 地址不是被直接引用的）。

```powershell
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
# 看表本身的形状：三条相邻命令的名字地址是否连续、间隔多少
Get-Content "$O\opcode_cmd.txt" -TotalCount 8
python -c "
rows=[l.split('\t') for l in open(r'$O\opcode_cmd.txt',encoding='utf-8') if l and not l.startswith('#')]
print('第一条与第二条的地址差:', int(rows[1][2],16)-int(rows[0][2],16))
print('第一条与第三条的地址差:', int(rows[2][2],16)-int(rows[0][2],16))
"
```

- [ ] **Step 2: 写按节扫描工具（不要整段扫）**

已验证的教训：从**整个 PT_LOAD 段**线性反汇编会失步，失败形状是**空输出**；必须**按可执行节**扫。

```python
#!/usr/bin/env python3
"""game-scan-imm.py — 在 ELF32 的可执行节里找"立即数/位移 == 目标地址"的指令。

与 extra-table-scan.py 同一套扫描方式（按节，不按段）——按段扫会失步且静默返回空。
用法: game-scan-imm.py <elf> --targets 0xVA [0xVA ...] [--limit N]
"""
import argparse
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

HERE = Path(__file__).resolve().parent
ELFREcon = HERE / "elfrecon.py"   # Task 4 已把它从 $W 复制到本目录


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("elf")
    ap.add_argument("--targets", nargs="+", type=lambda x: int(x, 0), required=True)
    ap.add_argument("--limit", type=int, default=8)
    a = ap.parse_args()

    spec = spec_from_file_location("elfrecon", ELFREcon)
    m = module_from_spec(spec); spec.loader.exec_module(m)
    elf = m.Elf(a.elf)
    md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True
    hits = {t: [] for t in a.targets}
    for sec in elf.sections:
        if not (sec["flags"] & 0x4) or sec["type"] == 8 or sec["size"] == 0:
            continue
        blob = elf.data[sec["offset"]:sec["offset"] + sec["size"]]
        for ins in md.disasm(blob, sec["addr"]):
            for op in ins.operands:
                v = op.imm if op.type == X86_OP_IMM else (op.mem.disp if op.type == X86_OP_MEM else None)
                if v in hits:
                    hits[v].append((ins.address, f"{ins.mnemonic} {ins.op_str}"))
    for t in a.targets:
        print(f"== 0x{t:x}: {len(hits[t])} hit(s)")
        for addr, txt in hits[t][:a.limit]:
            print(f"   0x{addr:x}  {txt}")


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: 用 3 条命令探路，找出"谁在读这张表"**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work"
Copy-Item "$W\elfrecon.py" "$P\work\elfrecon.py" -Force      # 原样复制，不改内容
python "$P\work\game-scan-imm.py" "$O\df_game_r.bin" --targets 0xaa39d8 0xaa39f8 0xaa3a10
```
预期：至少有一条命令的地址被**指令**引用（那就是读表的地方）。若三条都 0 命中 ⇒ 改用 IDA 的 xref（Task 6 的工具），
并在残余报告里记"`.rodata` 地址不被直接引用，改用 IDA xref 定位"。

- [ ] **Step 4: 定出映射机制，并用 10 条命令证明（4a）**

目标：找到"opcode → 派发器"的那张表或那个 `switch`。判据：
**取 10 条已知语义的命令**（如 `CMD 1 LOGIN`、`CMD 2 SET_UDP_IP_PORT`、`CMD 4 SELECT_CHARACTER`、
`CMD 0 CHECK_CONNECTION`、`NOTI 0 CHECK_CONNECTION` 等），手工顺着找到的机制走一遍，
10 条里 **≥8 条**指向一个**名字与语义相符**的派发器（如 LOGIN → `DisPatcher_Login`）。
**若 <8 条** ⇒ 机制找错了，回到 Step 3（换 xref 目标、或用 IDA 反编译该函数），**不要**开始批量映射。

- [ ] **Step 5: 批量映射并出残余（4b/4c）**

先把 Step 4 定出的机制写成**一个函数**（骨架如下：契约固定，机制由 Step 4 填入）。
**骨架的意义**：输出格式、状态取值、残余计数是固定的，执行者只需填 `classify()` 的实现。

```python
#!/usr/bin/env python3
"""map-opcodes.py — 把 CMD/NOTI 号映射到派发器家族。

机制（Step 4 定出后填在这里，必须写明地址依据）：
  <在这里写：哪张表/哪个函数把 opcode 变成派发器，表基址/比较逻辑的证据地址>

用法: map-opcodes.py <opcode_cmd.txt> <opcode_noti.txt> <catalog_dispatchers_full.txt> <out.tsv>
"""
import sys
from pathlib import Path

STATUSES = {"mapped", "no-dispatcher", "unused", "inter-server", "unknown"}


def load_table(path):
    """返回 [(id:int, name:str, rodata_va:int)]，跳过注释行。"""
    rows = []
    for ln in Path(path).read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        parts = ln.split("\t")
        rows.append((int(parts[0]), parts[1], int(parts[2], 16)))
    return rows


def load_families(path):
    """返回 {family: (process_addr, dispatch_sig_addr)}。"""
    fams = {}
    for ln in Path(path).read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        name, proc, sig, _spell = ln.split("\t")
        p = int(proc.split("=")[1].split("(")[0], 16)
        s = int(sig.split("=")[1].split("(")[0], 16)
        fams[name] = (p, s)
    return fams


def classify(kind, oid, name, rodata_va, families):
    """<Step 4 定出的机制>。

    返回 (dispatcher:str, status:str)；status 必须是 STATUSES 之一。
    判不出就返回 ("", "unknown") —— 绝不猜。
    """
    raise NotImplementedError("填入 Step 4 定出的机制")


def main(cmd_txt, noti_txt, catalog, out_tsv):
    families = load_families(catalog)
    lines = ["kind\tid\tname\trodata_va\tdispatcher\tstatus"]
    counts = {}
    for kind, path in (("CMD", cmd_txt), ("NOTI", noti_txt)):
        for oid, name, va in load_table(path):
            disp, status = classify(kind, oid, name, va, families)
            assert status in STATUSES, f"非法状态 {status}"
            counts[status] = counts.get(status, 0) + 1
            lines.append(f"{kind}\t{oid}\t{name}\t0x{va:08x}\t{disp}\t{status}")
    Path(out_tsv).write_text("\n".join(lines) + "\n", encoding="utf-8")
    total = sum(counts.values())
    print(f"total={total} " + " ".join(f"{k}={v}" for k, v in sorted(counts.items())))


if __name__ == "__main__":
    main(*sys.argv[1:5])
```

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
python "$P\work\map-opcodes.py" "$O\opcode_cmd.txt" "$O\opcode_noti.txt" `
  "$P\work\out\catalog_dispatchers_full.txt" "$P\work\out\opcode-map.tsv" | Tee-Object "$P\work\out\opcode-map-summary.txt"
# 残余分布（注意：这里不接 Select-Object -First，避免截断落盘）
Import-Csv "$P\work\out\opcode-map.tsv" -Delimiter "`t" | Group-Object status |
  ForEach-Object { "$($_.Name) = $($_.Count)" } | Out-File "$P\work\out\opcode-map-residual.txt" -Encoding utf8
Get-Content "$P\work\out\opcode-map-residual.txt"
```
`unknown` 的行**原样留在** `opcode-map.tsv` 里（不丢、不猜）。

- [ ] **Step 6: 判据自检（spec §2.2 判据 1）**

`mapped + no-dispatcher + unused + inter-server + unknown = 1171`（608 + 563），**且 `unknown` 必须被显式计数**。
**若 `unknown` 超过 200**（约 17%）⇒ 触发 spec §2.3 的改判分支：**停在这里**，写一份
`$P\work\out\opcode-map-residual.txt` 的说明（哪些族无归属、xref 结果如何），
并把"转运行期观测"作为新任务提交给评审门 —— **不许**用猜测把 `unknown` 填掉。

- [ ] **Step 7: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
B=.superpowers/sdd/2026-10-05-gameplay-evidence-program
git add -f $B/work/game-scan-imm.py $B/work/map-opcodes.py $B/work/elfrecon.py \
           $B/work/out/opcode-map-residual.txt $B/work/out/opcode-map-summary.txt
git commit -m "证据链工程 M0：opcode -> 派发器映射与残余（[静态] 1171 条，unknown=N）" -- \
  $B/work/game-scan-imm.py $B/work/map-opcodes.py $B/work/elfrecon.py \
  $B/work/out/opcode-map-residual.txt $B/work/out/opcode-map-summary.txt
```
（`opcode-map.tsv` 是 1171 行的派生物，**不进 git**，在接续块里写明可由工具重跑生成。）

---

### Task 5: 处理器目录 `catalog_handlers_full.txt`

**Files:**
- Create: `$P\work\extract-handlers.py`
- Create: `$P\work\out\catalog_handlers_full.txt`

**Interfaces:**
- Consumes: `catalog_dispatchers_full.txt`（家族 + `dispatch_sig` 地址）、`$O\packet_senders.txt`（1248 个 `put_header` 点）。
- Produces: `catalog_handlers_full.txt`，每行 `<family>\thandler=0x<addr>(<size>)\tsource=<dispatch_sig|put_header>`。
  **现状是空文件（0 条数据）**，本任务要让它非空且可追溯。

- [ ] **Step 1: 写"失败的形状"**

失败 = ① 文件仍为空；② 有 `dispatch_sig` 的家族里超过一半出不了 handler 且无法解释；
③ handler 地址落在二进制外（明显的解析错误）。

- [ ] **Step 2: 先看 `packet_senders.txt` 的形状，再写脚本**

**不要**凭空假设它的格式 —— 先打印前 5 行，再决定解析方式（下面的脚本用"每行取第一个 `0x…`"的容错解析，若实际格式不同就改这一段）。

```powershell
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
Get-Content "$O\packet_senders.txt" -TotalCount 5
```

```python
#!/usr/bin/env python3
"""extract-handlers.py — 从派发器与 put_header 点抽出处理器目录。

两个来源，每条记录都带 source 以便回溯：
  dispatch_sig : 反汇编 dispatch_sig 的函数体，取直接的 call 目标
  put_header   : packet_senders.txt 里每个 put_header 点（容错解析：每行第一个 0x…）
过滤：目标必须落在可执行节内、且其符号 size >= 16（排除 thunk/PLT 桩）。

用法: extract-handlers.py <catalog_dispatchers_full.txt> <packet_senders.txt> <elf> <out.txt>
"""
import re
import sys
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path

from capstone import Cs, CS_ARCH_X86, CS_MODE_32

HERE = Path(__file__).resolve().parent


def load_families(path):
    fams = []
    for ln in Path(path).read_text(encoding="utf-8").splitlines():
        if not ln or ln.startswith("#"):
            continue
        name, proc, sig, _ = ln.split("\t")
        sig_a, sig_s = sig.split("=")[1].split("(")
        fams.append((name, int(sig_a, 16), int(sig_s.rstrip(")"))))
    return fams


def main(catalog, senders, elf_path, out):
    spec = spec_from_file_location("elfrecon", HERE / "elfrecon.py")
    m = module_from_spec(spec); spec.loader.exec_module(m)
    elf = m.Elf(elf_path)
    syms = sorted((s for s in elf.func_symbols() if s["value"]), key=lambda s: s["value"])

    def sym_at(va):
        best = None
        for s in syms:
            if s["value"] <= va and (s["size"] == 0 or va < s["value"] + s["size"]):
                if best is None or s["value"] > best["value"]:
                    best = s
        return best

    md = Cs(CS_ARCH_X86, CS_MODE_32); md.detail = True
    lines = ["# family\thandler\tsource\tname"]
    n_sig = n_ph = 0
    for fam, sig_va, sig_size in load_families(catalog):
        if not sig_va or not sig_size:
            continue
        off = elf.va_to_off(sig_va)
        if off is None:
            continue
        for ins in md.disasm(elf.data[off:off + sig_size], sig_va):
            if ins.mnemonic != "call":
                continue
            tgt = ins.operands[0].imm if ins.operands and ins.operands[0].type == 2 else None
            if not tgt:
                continue
            s = sym_at(tgt)
            if s and s["size"] >= 16:
                lines.append(f"{fam}\t0x{tgt:08x}\tdispatch_sig\t{s['name']}")
                n_sig += 1
    for ln in Path(senders).read_text(encoding="utf-8", errors="replace").splitlines():
        mm = re.search(r"0x([0-9a-fA-F]{6,8})", ln)
        if not mm:
            continue
        va = int(mm.group(1), 16)
        s = sym_at(va)
        lines.append(f"(unknown-family)\t0x{va:08x}\tput_header\t{s['name'] if s else '?'}")
        n_ph += 1
    Path(out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"dispatch_sig handlers={n_sig}, put_header points={n_ph} -> {out}")


if __name__ == "__main__":
    main(*sys.argv[1:5])
```

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$O = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-09-29-dungeon-party\work\official"
python "$P\work\extract-handlers.py" "$P\work\out\catalog_dispatchers_full.txt" "$O\packet_senders.txt" `
  "$O\df_game_r.bin" "$P\work\out\catalog_handlers_full.txt"
"handlers = $((Get-Content "$P\work\out\catalog_handlers_full.txt" | Where-Object { $_ -and -not $_.StartsWith('#') }).Count)"
```
预期：非空，且数量远大于既有子集里的 45（那 45 个是有 `dispatch_sig` 的家族）。

- [ ] **Step 3: 交叉核对（同源交叉）**

有 `dispatch_sig` 的家族里，**≥80%** 应至少产出一个 handler；不足则逐个解释（可能是尾调用/thunk）。

- [ ] **Step 4: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
B=.superpowers/sdd/2026-10-05-gameplay-evidence-program
git add -f $B/work/extract-handlers.py $B/work/out/catalog_handlers_full.txt
git commit -m "证据链工程 M0：处理器目录（[静态] 从 0 条到 N 条）" -- \
  $B/work/extract-handlers.py $B/work/out/catalog_handlers_full.txt
```

---

### Task 6: 申报两个 `.recon` 工具并实测

**Files:**
- Modify: `.recon/recon.config.yml`（在 `tools:` 列表末尾追加，**不动既有 6 个**）
- Create: `$P\work\ida-game.py`（IDA 取数脚本，供 `ida-game` 工具调用）

**Interfaces:**
- Produces: `.recon` 里新增 `demangle-catalog` 与 `ida-game` 两个工具；`recon_status` 报告**无配置问题**。
- 后续 Task 4 的 Step 3 与所有静态取证都用 `ida-game`。

- [ ] **Step 1: 写"失败的形状"**

失败 = `recon_status` 报配置问题；或工具按 example 跑不出非空产物；或 `ida-game` 因 IDA 单写冲突失败。

- [ ] **Step 2: 追加配置**

```yaml
  # ------------------------------------------------ 静态层（官服 df_game_r）
  - name: demangle-catalog
    kind: external
    summary: 用 sidecar 的 c++filt 把 nm 转储的名字批量解名（纯净、无网依赖）
    invoke: ["python", "{project_root}/.superpowers/sdd/2026-10-05-gameplay-evidence-program/work/demangle-syms.py", "{src}", "{outfile}"]
    access: read
    inputs:
      src:     { type: string, required: true }
      outfile: { type: string, required: true }
    outputs:
      - { path: "{outfile}", required: true, nonEmpty: true }
    output: { format: text }
    timeoutMs: 600000
    examples:
      - src: ".superpowers/sdd/2026-09-29-dungeon-party/work/official/game_r.syms.txt"
        outfile: ".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/demangled-syms.tsv"
    caveats: >-
      依赖容器 sidecar `dfl-recon` 里的 `c++filt`（binutils 2.44，2026-10-05 实测可用）。
      **本机 python `cxxfilt` 不可用**（`LibraryNotFound: Cannot find any of libraries: ('c',)`，Windows 无 libc），别去试它。
      sidecar 的 netns 可能在 `docker restart dnf` 后失效 —— 那影响抓包，**不影响本工具**（纯 CPU 任务）。
      爆炸半径：只读源文件、只写 outfile；35 万行一次性喂给容器，几十秒级。产物 20+MB，别提交进 git。

  - name: ida-game
    kind: external
    summary: 在 df_game_r.bin.idb 上跑 IDAPython（对官服游戏服取数）
    invoke: ["{bash}", "{project_root}/.recon/run-ida.sh", "{script}", "{outfile}", "{args}"]
    access: mutate
    inputs:
      bash:    { type: string, required: true }
      script:  { type: string, required: true }
      outfile: { type: string, required: true }
      args:    { type: string, required: true }
    outputs:
      - { path: "{outfile}", required: true, nonEmpty: true }
    output: { format: text }
    timeoutMs: 900000
    examples:
      - bash: "D:/Program Files/Git/bin/bash.exe"
        script: ".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/ida-game.py"
        outfile: ".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/ida-smoke.txt"
        args: "smoke"
    caveats: >-
      **必须**用 Git Bash（PATH 里的 bash 是 WSL 的，会把 /d/... 当不存在）。
      **关键差异**：本工具的目标库是 `df_game_r.bin.idb`（170 MB，官服游戏服），
      不是客户端 `DNF.exe.idb` —— 通过环境变量 `DNF_IDB=` 覆盖 `run-ida.sh` 的默认库；
      若 `run-ida.sh` 不支持该覆盖，先按其既有约定加（**不要**另写一份包装，那会制造"同一能力两份"）。
      IDA **单写**：同一时刻只能有一个 IDA 任务，且 170 MB 库**加载分钟级** ⇒ 批量取数要一次脚本跑完，别一条一条跑。
      爆炸半径：idat 退出时把标注写回 `df_game_r.bin.idb`；分析本身不破坏它，但并发会。
```

- [ ] **Step 3: 写 smoke 脚本并实测**

`$P\work\ida-game.py`（以 `idc.ARGV` 收参：`ARGV[1]=outfile`、`ARGV[2..]=args`；本步只实现 `smoke` 分支）：

```python
# ida-game.py —— 在 df_game_r.bin.idb 上取数（供 .recon 的 ida-game 工具调用）
# 参数约定与 run-ida.sh 一致：ARGV[1] = outfile，ARGV[2..] = args
import idaapi
import idautils
import idc

OUT = idc.ARGV[1]
ARGS = idc.ARGV[2:]
lines = []


def emit(s):
    lines.append(str(s))


def smoke():
    idb = idaapi.get_path(idaapi.PATH_TYPE_IDB)
    emit(f"IDB = {idb}")
    names = list(idautils.Names())
    emit(f"symbols = {len(names)}")
    hits = [(ea, nm) for ea, nm in names if "Dispatcher_" in nm or "DisPatcher_" in nm]
    emit(f"dispatcher-named symbols = {len(hits)}")
    for ea, nm in hits[:3]:
        emit(f"  0x{ea:08x}  {nm}")


if ARGS and ARGS[0] == "smoke":
    smoke()
else:
    emit(f"unknown args: {ARGS}")

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
idc.qexit(0)
```

实测（走 `recon_tool`，**不要**用 `recon_probe` —— `STATUS.md` §3.10 记了它对带必填输入的工具会失败）：

```
tool: ida-game
args: { bash: "D:/Program Files/Git/bin/bash.exe",
        script: ".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/ida-game.py",
        outfile: ".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/ida-smoke.txt",
        args: "smoke" }
```

**预期**：`IDB = …\df_game_r.bin.idb`；`symbols` 数万以上；`dispatcher-named symbols` **> 0**。
若 `IDB` 指到 `DNF.exe.idb` ⇒ **覆盖没生效，停下来修**（这条错了后面全是错的）。
若 `run-ida.sh` 目前不支持 `DNF_IDB=` 覆盖 ⇒ **在它里面加**（它是权威实现，`.recon/run-ida.sh` 只是薄包装；
**不要**另写第二份包装，那会制造"同一能力两份、不知道哪份对"）。

- [ ] **Step 4: 复核 `recon_status`**

```powershell
# 经 recon_status / recon_tool_list 检查：工具数应为 8，且"配置校验：无问题"
```
**必须**看到 `配置校验：无问题`。有报错就修到没有为止（配置坏了会拒绝所有工具调用）。

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .recon/recon.config.yml .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/ida-game.py
git commit -m "证据链工程 M0：申报 demangle-catalog 与 ida-game 两个 recon 工具（含 caveats）" -- \
  .recon/recon.config.yml .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/ida-game.py
```

---

### Task 7: M0 验收四判据 + 分母报告（含 S2 输入与接续块）

**Files:**
- Create: `$P\work\out\M0-acceptance.txt`（四判据的原始输出）
- Create: `$P\M0-denominator-report.md`（**给评审门看的那一份**）
- Create: `$P\RESUME.md`（**接续块**，规格见 spec §6.1）

**Interfaces:**
- Consumes: Task 2–6 的全部产物。
- Produces: 一份能回答"分母多大、还剩多少没归属、能不能进 M1、要不要触发 S2"的报告。

- [ ] **Step 1: 跑四条判据（spec §2.2）并把原始输出落盘**

1. 命令映射无余数（`unknown` 计数 + 归属分布）
2. 两个 demangler 结果一致（Task 2 的 `demangled-syms.tsv` vs `ida-game` 的 `Dispatcher_*` 名单，**差异逐条解释**）
3. 模块清单交叉（`*Dispatcher*.cpp` 的 `FILE` 条目数 vs 家族数**同量级**）
4. **抽样反证**：随机抽 **10** 个家族，用其 `dispatch_sig` 地址反查 `.rodata` 里它注册的名字串，**必须命中**

判据 4 的具体做法：对每个抽样家族，用 `game-scan-imm.py --targets <dispatch_sig 地址>` 找调用点，
再用 `channel-disasm.py` 的同类方式反汇编该点附近，看是否引用到含家族名的字符串（名字串可在符号表里按家族名搜到其 `.rodata` 地址）。

- [ ] **Step 2: 写分母报告**

`M0-denominator-report.md` 必须**逐条回答**（不许含糊）：
① 派发器家族总数 N、其中带 `dispatch_sig` 的 M；
② `CMD`/`NOTI` 各多少条、`mapped` 多少、`unknown` 多少、其余状态各多少；
③ 四条判据各自的**结论 + 原始输出位置**；
④ **S2 判定**：客户端可达命令数（`mapped` 里排除 `inter-server`/`unused`）是否超过"预期"的 2×
   —— "预期"写死为：**M1–M10 的单元总共需要覆盖约 300–600 条命令**（spec §5.2 的功能面清单按每面 10–40 条估）；
⑤ **能否进 M1**：一句话结论 + 依据。
⑥ **待评估队列**：本阶段冒出来的"官方有、`STATUS.md` 没有"的家族清单（只登记，不展开）。

- [ ] **Step 3: 写接续块（防中断，spec §6.1）**

`$P\RESUME.md` 必须自包含：产物清单（含**哪些未进 git、怎么重跑生成**）、
所有可重跑命令、当前未决项、环境事实（宿主端口占用者 pid、容器发布端口、sidecar 状态、ACL 修复与否）、
以及"下一步是 M1 的哪一件事"。**判据：一个没参与过本任务的会话只读这份文件就能接着干。**

- [ ] **Step 4: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
B=.superpowers/sdd/2026-10-05-gameplay-evidence-program
git add -f $B/work/out/M0-acceptance.txt $B/M0-denominator-report.md $B/RESUME.md
git commit -m "证据链工程 M0 出口：四判据 + 分母报告 + 接续块（评审门输入）" -- \
  $B/work/out/M0-acceptance.txt $B/M0-denominator-report.md $B/RESUME.md
```

**⛔ 评审门**：Task 7 之后**停下来**，把 `M0-denominator-report.md` 交给项目所有者。
由他决定：进 M1、或触发 S2（缩小范围）、或先处置待评估队列。**不得到答复不要开始 Task 8。**

---

### Task 8: 批次 B0 —— 官服全链路已知答案采集（**人机批次**）

**Files:**
- Create: `$P\work\captures\e2e-official-to-game.pcap`、`$P\work\captures\streams\`（单向流）
- Create: `$P\work\out\B0-batch-report.md`
- Reference: `open-questions.md` 坑 11/14（端口落差与互斥）、`.recon/onboarding.md`（启动器用法）

**Interfaces:**
- Produces: 一份**从登录到进游戏**的官服抓包（含 `30011`/`31011`），这是 Task 11（游戏面自检）的**已知答案**，
  同时填掉 `SPEC-login-to-channel.md` §7 里"官服那代客户端↔游戏服（30011）帧头未抓到"那一行。
- 关键前提：**7001/2311 必须让给容器** ⇒ 先停 `DFLegacy.Server`，批次结束**按坑 14 的步骤恢复**。

- [ ] **Step 1: 批次前清单（写下来，逐条执行）**

1. `Get-Process DFLegacy.Server` 记下 pid（**注意**：受限模式下 `Get-Process` 可能"看不见"在跑的进程 ⇒ 用 `Get-NetTCPConnection -LocalPort 7001` 交叉验证，见 `open-questions.md` 坑 24）；
2. `Stop-Process -Name DFLegacy.Server -Force`；
3. `docker restart dnf`（**重启才会重新绑定 7001/tcp、2311/udp**）；
4. **补宿主 TCP 转发**（`netsh` 只做 TCP；本批次只要 TCP —— 目标是把 `30011` 的**登录交换**抓下来，
   UDP `31011` 是进副本之后的玩法数据，**本批次不涉及**，因此**不补 UDP 转发**，并在报告里写明这个决定）：
   ```powershell
   netsh interface portproxy add v4tov4 listenport=30011 listenaddress=127.0.0.1 connectport=30021 connectaddress=127.0.0.1
   netsh interface portproxy show v4tov4
   ```
   撤销用 `netsh interface portproxy delete v4tov4 listenport=30011 listenaddress=127.0.0.1`（**批次收尾必须撤**）。
5. 确认：`docker inspect dnf --format '{{json .NetworkSettings.Ports}}'` 里 `7001/tcp` **不再是 `null`**；
   `Test-NetConnection 127.0.0.1 -Port 30011` 为 True。
6. 抓包：`dnf` 容器里**没有 tcpdump** ⇒ 用**共享 `dnf` netns 的 sidecar**，且旧 sidecar 很可能挂着已失效的 netns（坑 9）：
   ```powershell
   docker rm -f dfl-recon
   # 重建：从被删容器的原始配置还原同样的参数（--network container:dnf + 装 binutils/tcpdump 的那套）
   # 先留证据再删：docker inspect dfl-recon --format '{{json .Config}}' / '{{json .HostConfig}}'
   ```
   **做法**：删之前先把 `docker inspect dfl-recon --format '{{json .HostConfig}}'` 与 `.Config.Cmd/Image` 抄进报告，
   重建时照抄；重建后必须验 `ip -o addr show` 里**有 `eth0`**（没有就是又挂到了失效 netns）。
7. **人机部分**：用参考包（`G:\Work\OpenDNF\_work\opendnf-win-0.0.3`）的 llnut 启动器 + 2012 客户端，账号口令见 `%APPDATA%\llnut-launcher\credentials.dat`（DPAPI，只有当前 Windows 用户能解）—— 走到**选服 → 进频道 → 选角色 → 进游戏**；
8. 停抓包、拷出 `channel/log/` 与 `game/log/`（**重启即截断**，坑 15）。

- [ ] **Step 2: 采集后立即固化为单向流**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work"
python "$W\pcap-streams.py" "$P\work\captures\e2e-official-to-game.pcap" "$P\work\captures\streams" --filter-port 30011
python "$W\pcap-streams.py" "$P\work\captures\e2e-official-to-game.pcap" "$P\work\captures\streams" --filter-port 7001
```
预期：`30011` 出现**非空**的 C/S 流（**这是本次批次的核心成功判据**；上一轮那次是 0，因为端口落差）。
若仍为 0 ⇒ 在报告里记"失败 + 已排除的原因"，并**不要**伪造任何"官方游戏面"结论。

- [ ] **Step 3: 恢复环境（坑 14 的步骤，必须做）**

```powershell
docker stop dnf
& "G:\Game\DNF_ACT1\DF\town-test\DFLegacy.Server.exe"   # 工作目录必须是这个目录（它读同目录 server.json）
docker start dnf
```
（起回来的容器会因为宿主占用而绑不上 7001 —— 这是**已知且可接受**的状态，与批次前一致。）

- [ ] **Step 4: 写批次报告**

`B0-batch-report.md` 必须含：批次前后**端口占用对照**、做了什么写操作（逐条）、
抓到的流清单与**首帧十六进制**、以及三栏结果：**预期到了 / 预期没到 / 预期外来了**。

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/B0-batch-report.md
git commit -m "证据链工程 B0：官服全链路已知答案采集（[抓包] 30011/31011 首帧 + 环境恢复）" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/B0-batch-report.md
```

---

### Task 9: 驱动器框架 + **7001 已知答案自检**（硬门 S3）

**Files:**
- Create: `$P\work\driver\dnfdrive.py`（协议层 + 帧编解码 + AES/zlib）
- Create: `$P\work\driver\selftest-7001.py`
- Create: `$P\work\out\driver-7001-selftest.txt`

**Interfaces:**
- Produces: `dnfdrive.py` 提供
  `Conn(host, port)`、`send_frame(cat, opcode, payload)`、`recv_frame()`、
  `aes_ecb_encrypt/decrypt(key16, data)`、`zlib_wrap/unwrap(data)`。
  **帧模型是 11 字节头**：`[0]category [1]id [2..5]size(LE,含头) [6..9]保留(发 0) [10]ackOk(1)`，载荷从偏移 11 起。
- Task 10/11 消费这套接口；**两套方言通过"帧脚本"驱动，不写死在代码里**。

- [ ] **Step 1: 写"失败的形状"**

失败 = 自检拿不到与抓包一致的回复（长度或内容不符）。**触发 spec 的 S3：冻结所有 L2 结论，先修驱动器。**

- [ ] **Step 2: 实现帧层与密码层（照既有已验证结论写）**

- 11 字节头（`STATIC-FINDINGS.md` §4.4 的访问器证据）；
- 会话密钥 = 服务端 `12` 号帧正文的 `[5..21)`（`int32 status` 之后的前 16 字节）；
- `id=10`/`id=3` 的正文 = `count(1) | zlib(AES-128-ECB(明文))`（`decode-official-channel-table.py` 已实证）。

- [ ] **Step 3: 自检分两半跑（容器内重放、宿主校验）**

**为什么分两半**：`dnf` 容器**没有 python3**，而驱动器是 Python；
且容器侧 `7001/tcp` 没发布到宿主（被 `DFLegacy.Server` 占着）⇒ 宿主也连不进去。
所以：**容器内只做"原样重放抓到的 C 流字节"**，宿主侧做解析与判定。

**3a 容器内重放（把 C 流与 S 流都用原始字节带进去）**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work"
docker cp "$W\captures\streams\36828-7001.bin" dnf:/tmp/c.bin
docker exec dnf bash -lc 'exec 3<>/dev/tcp/127.0.0.1/7001; cat /tmp/c.bin >&3; timeout 6 cat <&3 > /tmp/s.bin; exec 3<&-; wc -c < /tmp/s.bin'
docker cp dnf:/tmp/s.bin "$P\work\out\replay-s.bin"
```
预期：`wc -c` 打印 **7468**（= 47+43+7239+139，与抓包的 S 流等长）。
**若不足 7468**：把超时放宽到 15 s 再试一次；仍不足则在自检记录里写清**收到了多少、缺哪一帧**（这属于 S3 的差异证据）。

**3b 宿主侧校验（写进 `selftest-7001.py` 的 `--verify` 分支）**

```python
# selftest-7001.py --verify <replay-s.bin> <capture-s.bin> <out.txt>
import struct, sys, zlib
from pathlib import Path
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

def frames(d):                      # 11 字节头：size 含头
    off = 0
    while off + 11 <= len(d):
        size = struct.unpack_from("<I", d, off + 2)[0]
        yield off, d[off + 1], size, d[off:off + size]
        if size < 11 or off + size > len(d):
            return
        off += size

def main(replay, capture, out):
    got, want = Path(replay).read_bytes(), Path(capture).read_bytes()
    lines, ok = [], True
    g = [(i, s) for i, _p, s, _f in frames(got)]
    w = [(i, s) for i, _p, s, _f in frames(want)]
    lines.append(f"replay frames={g}  capture frames={w}")
    if [s for _i, s in g] != [s for _i, s in w]:
        ok = False
        lines.append("!! 整帧长度不一致 -> S3（冻结 L2 结论，先修驱动器）")
    # 会话密钥 = 12 号帧正文 [5:21)（10 字节头读法下正文从第 10 字节起，故 +5）
    key = None
    for _o, pid, _s, f in frames(got):
        if pid == 12:
            key = f[11 + 5:11 + 21]          # 11 字节头 + status(4) + 头 1 字节 = 从载荷起第 5 字节
    for _o, pid, _s, f in frames(got):
        if pid == 3:
            body = f[11:]                     # 11 字节头之后
            raw = zlib.decompress(body[1:])   # body[0] = ackOk/计数（既有结论）
            pt = Cipher(algorithms.AES(key), modes.ECB()).decryptor().update(raw)
            lines.append(f"id=3 inflated={len(raw)} plain={len(pt)} head={pt[:4].hex(' ')}")
            ok &= (len(pt) == 128 and pt[:4] == b"\x01\x00\x00\x00")
    lines.append("VERDICT: " + ("PASS" if ok else "FAIL -> S3"))
    Path(out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))

if __name__ == "__main__":
    main(*sys.argv[1:4])
```

⚠️ **上面这两处偏移（`11+5` 与 `f[11:]`）是"读法"而不是"抄来的常数"** —— 写代码时要用
`decode-official-channel-table.py`（已在本会话跑通、能解出 `siroco`/`#ch.11`/30011 的那份）对照一遍：
**同一份 `capture` 上两个脚本必须解出同样的 128 字节**。不一致就说明偏移错了，先修偏移。

- [ ] **Step 4: 判定**

判据（全部满足才算过）：① 四个回复帧的**整帧长度**分别等于 `47/43/7239/139`；
② `id=3` 解密后长度 **128** 且首 4 字节是 `01 00 00 00`；③ `id=12` 正文前 4 字节 `status = 0`。
**任一不满足 ⇒ 停（S3）**，把差异写进 `driver-7001-selftest.txt` 并报告。

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
B=.superpowers/sdd/2026-10-05-gameplay-evidence-program
git add -f $B/work/driver/dnfdrive.py $B/work/driver/selftest-7001.py $B/work/out/driver-7001-selftest.txt
git commit -m "证据链工程 M1 前置：驱动器帧层/密码层 + 7001 已知答案自检（[回放] 长度 47/43/7239/139 全中）" -- \
  $B/work/driver/dnfdrive.py $B/work/driver/selftest-7001.py $B/work/out/driver-7001-selftest.txt
```

---

### Task 10: 驱动器 `30011` 方言骨架 + 令牌签发（**离线**验证）

**Files:**
- Create: `$P\work\driver\token.py`
- Create: `$P\work\out\token-offline-check.txt`

**Interfaces:**
- Produces: `token.sign(uid: int, key_path: str) -> bytes`（256 字节 RSA-2048 签名）、
  `token.verify(sig: bytes, uid: int, pub_path: str) -> bool`。
- Task 11 用它构造游戏面登录。

- [ ] **Step 1: 写"失败的形状"**

失败 = 我们用容器私钥签的令牌**验不过**容器公钥；或签出来的**不是 256 字节**。

- [ ] **Step 2: 按已实证的格式实现**

依据（本会话已实测）：令牌 = **裸 PKCS#1 v1.5 type-1 块**（`rsa` crate 的 `Pkcs1v15Sign::new_unprefixed()`），
被签内容 = **46 字节**：`00 00 00 <uid 大端>` + `01 ×32` + `55 91 45 10` + `01 04 03 03 01 01`；
三个账号互证（uid 1/2/3 只差第 3 字节）。

密钥：`$W\container-privatekey.pem`（其模数 `0xbba4ee40…` **等于** `$W\container-game-publickey.pem`）。

- [ ] **Step 3: 离线验证并落盘**

```powershell
$P = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-gameplay-evidence-program"
$W = "G:\Game\DNF_ACT1\DFLegacy\.superpowers\sdd\2026-10-05-official-login-channel\work"
python "$P\work\driver\token.py" --sign-uid 1 --key "$W\container-privatekey.pem" `
  --verify-with "$W\container-game-publickey.pem" --out "$P\work\out\token-offline-check.txt"
```
判据：① 签名为 **256** 字节；② 用公钥**验签通过**；③ 用公钥做模幂后，前 4 字节是 `00 01`、第 3..? 字节是 `00 00 00 01`（uid=1）。
**注意 `[推断]` 标注**：46 字节里除 uid 外的 42 字节语义**仍未解**（spec 里已记），本任务**只要求与已抓到的三条令牌逐字节可比**（用抓包里那三条做回归）：签出来的结构必须与它们**同形**。

- [ ] **Step 4: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add -f .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/driver/token.py \
           .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/token-offline-check.txt
git commit -m "证据链工程 M1 前置：令牌签发/验签（[静态]+[抓包] 三账号同形回归）" -- \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/driver/token.py \
  .superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out/token-offline-check.txt
```

---

### Task 11: 驱动器 `30011` 已知答案自检（**= M1 的第一个门**）

**Files:**
- Create: `$P\work\driver\selftest-30011.py`
- Create: `$P\work\out\driver-30011-selftest.txt`

**Interfaces:**
- Consumes: Task 8 的 `streams`（已知答案）、Task 10 的 `token.py`、Task 9 的帧层。
- Produces: 游戏面自检结论；**通过后 M1 的 L2 才允许开跑**。

- [ ] **Step 1: 写"失败的形状"并声明依赖**

失败 = ① Task 8 抓到的 `30011` 流为空（⇒ 本任务**阻塞**，回到 Task 8 的失败处置，**不许**用猜测替代）；
② 我们重放 B0 的首帧却拿不到**同长度**的回复。

- [ ] **Step 2: 用 B0 的抓包定义期望值（不是我编的）**

从 `$P\work\captures\streams\` 里取 `30011` 的 **C 流首帧**（客户端→游戏服的第一帧）与 **S 流首帧**：
把它们的**整帧长度与 opcode** 写进 `driver-30011-selftest.txt` 作为期望值；
再用 `token.py` 把其中"客户端令牌"字段替换为**我们自己签的**（uid 取自容器 DB 里一个可丢弃账号），
重放并比对回复长度与 opcode。

**注意**：若 B0 的 C 流首帧不含可见令牌（可能加密），则本任务退化为**逐字节原样重放**（令牌由抓包提供），
并**显式记录**"令牌替换未验证" —— 那属于 M1 单元②（登录/鉴权）的工作，不在本计划内。

骨架（契约固定；`extract_expectation()` 里按 B0 落地后的真实形状填）：

```python
#!/usr/bin/env python3
"""selftest-30011.py — 游戏面（30011）已知答案自检。

期望值**不写在代码里**，而是从 B0 批次抓到的单向流里抽出来 —— 这样"判据"与
"我编的期望值"能区分开。抽不出来就报错退出，绝不退化成"随便比对一下"。

用法: selftest-30011.py --capture-dir <streams-dir> --out <report.txt> [--replay]
"""
import argparse
import struct
import sys
from pathlib import Path


def frames(d, hdr=11):
    off = 0
    while off + hdr <= len(d):
        size = struct.unpack_from("<I", d, off + 2)[0]
        yield off, d[off + 1], size
        if size < hdr or off + size > len(d):
            return
        off += size


def extract_expectation(capture_dir):
    """从 B0 抓包抽 (client_first=(opcode,len), server_first=(opcode,len))。

    <按 B0 落地后的真实文件命名与形状填；找不到就 raise，不许返回默认值>
    """
    raise NotImplementedError("按 B0 的真实产物填；抽不到就报错")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--capture-dir", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--replay", action="store_true",
                    help="真的重放（默认只抽期望值并打印，便于先核对再打靶）")
    a = ap.parse_args()
    exp = extract_expectation(a.capture_dir)
    lines = [f"expectation: {exp}"]
    if a.replay:
        lines.append("<重放结果：opcode/长度 与期望比对；不一致即 FAIL -> 记入差异证据>")
    Path(a.out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 3: 跑并判定**

判据（全部满足才算过）：① 原样重放拿到与抓包**同 opcode、同长度**的回复；
② 替换令牌后的行为**与预期一致**（或按 Step 2 的退化分支记录"未验证"）；
③ 原始字节全部落盘。

- [ ] **Step 4: 提交 + 更新接续块**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
B=.superpowers/sdd/2026-10-05-gameplay-evidence-program
git add -f $B/work/driver/selftest-30011.py $B/work/out/driver-30011-selftest.txt $B/RESUME.md
git commit -m "证据链工程 M1 门：驱动器 30011 已知答案自检（[回放] 与 B0 抓包同形）" -- \
  $B/work/driver/selftest-30011.py $B/work/out/driver-30011-selftest.txt $B/RESUME.md
```

---

## 完成本计划后应当为真的东西（收尾清单）

- [ ] `$P\work\out\` 里有：`demangled-syms.tsv`、`catalog_dispatchers_full.txt`、`catalog_handlers_full.txt`、
      `opcode-map.tsv`、`M0-acceptance.txt`、`driver-7001-selftest.txt`、`token-offline-check.txt`、`driver-30011-selftest.txt`；
- [ ] `$P\M0-denominator-report.md` 逐条回答了 spec §2.2 的四判据，并给出 S2 判定与"能否进 M1"；
- [ ] `.recon` 有 **8** 个工具且 `recon_status` 报**无配置问题**；
- [ ] `$P\RESUME.md` 能让**没参与过的会话**接手；
- [ ] 环境恢复原状（容器在跑、`DFLegacy.Server` 在跑、端口占用与 Task 1 基线一致）；
- [ ] 没有把任何 `[推断]` 写成事实；`unknown` 计数与残余清单都在案。
