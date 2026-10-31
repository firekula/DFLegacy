# 多人副本官服对齐重写 — Phase 0（证据脊柱与工具）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 产出"官服语义 ↔ 本客户端可达性 ↔ 现状 ↔ 裁决"的**可复核对齐清单**及其**自动化工具链**，让随后 S1–S8 每一片都能在开工前拿到齐备的 8 字段条目。

**Architecture:** 三个 Python 工具（客户端协议面查询、官服侧抽取、清单 lint/verify）先把"查表/查处理器/查调用点"固化；随后按六块填充清单（③ 块作试点，因为它证据最新最全）；最后产出切片卡与覆盖矩阵。工具与清单都在 git-ignored 的 `.superpowers/` 下，代码仓库只增加计划与（必要时）`.recon` 工具条目。

**Tech Stack:** Python 3（`pefile` + `capstone`，系统 `python` 已具备）、官方 ELF 逆向助手（`elf_syms.py` / `calls_in.py` / `dis_range.py` / `packet_layout.py` / `xrefs_elf.py` / `reads_client_cmds.py` / `packet_senders.py`）、PowerShell（`pwsh`）、Git Bash（IDA，仅必要时代码调用）、Markdown。

**Spec:** `docs/superpowers/specs/2026-10-02-party-dungeon-official-rewrite-design.md`（本计划只实现其 §5 Phase 0 与 §7.3 里的口径更正；S1–S8 的实施细节在本计划完成后逐片另出）

## Global Constraints

以下为项目级硬约束，**每个任务默认全部适用**（逐字取自 spec 与交接说明）：

- **工作位置**：分支 `feat/party-official-realignment` + 隔离工作树 `.worktrees/party-official-realignment`（基线 `db46e38`）。**不在 main 上写代码**；合并/推送前先问用户。
- **证据类别**只用六种：`[静态]` `[抓包]` `[真机]` `[回放]` `[推断]` `[未验证]`；推翻旧结论时旧结论**保留**并标 `[作废]` + 指向推翻它的证据。台账只增不删。
- **禁** `git checkout` / `git restore` / `git clean`（撤销只能用 edit/write 反向改）。
- **每个可编译且定向通过的增量立刻中文提交**；串行 build/test，不与其他代理并行构建。
- **Python 侧**：所有新工具必须 `--selftest` 自证（项目既有约定，参考 `ref_proxy.py --selftest`）；工具不得依赖网络。
- **清单条目的"可重跑命令"必须自证**：命令以断言管道结尾（`| findstr /C:"<期望子串>"`），断言失败即非零退出。
- **单人零回归**：任何组队专用逻辑以 `IsParty` 为门；单人包序列逐字节不变。
- **查表前置**：任何官方通知/命令落地前先查通知跳转表与上行命令表；不可达的进裁决表，不许"先发了试"。
- **不许补包试错**、**不许静默丢官服机制**、**不许同一状态两个真源**。
- **真机动作先与用户确认时间**（需要用户操作两个客户端：`G:/Game/DNF_ACT1/DF/town-test`，账号 `test`/`test`、`shizuku`/`shiruku`）。
- **路径口径**：证据库与台账在**主检出** `G:/Game/DNF_ACT1/DFLegacy`；工作树只放代码与 `docs/`。
- 本 Phase **不改任何生产代码**（`src/`、`tests/` 零改动）；验证命令仍须全绿以证明未伤基线：
  ```
  cd G:/Game/DNF_ACT1/DFLegacy
  $tmp=Join-Path (Get-Location) '.tmp\card-tests'; New-Item -ItemType Directory -Force -Path $tmp | Out-Null; $env:TEMP=$tmp; $env:TMP=$tmp
  dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --party-card
  dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --store-write-transaction
  dotnet run --project tests/DFLegacy.SmokeTests/DFLegacy.SmokeTests.csproj -c Release -- --dungeon-reward-commit
  dotnet build src/DFLegacy.Server/DFLegacy.Server.csproj -c Release --no-incremental
  ```

## 文件结构（本 Phase 新建/修改）

| 路径（相对主检出） | 责任 |
| --- | --- |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/client_surface.py` | 客户端协议面查询：`noti` / `noti-fields` / `cmds` / `global-refs` |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/official_surface.py` | 官服侧抽取：`func` / `layout` / `callers` / `skeleton` |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/lint_inventory.py` | 清单 8 字段结构校验（含 fixture 自证） |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/verify_inventory.py` | 跑通每条"可重跑命令"并要求退出码 0（自证断言已内建） |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/README.md` | 工具用法与命令行（含工作目录、依赖、产物） |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/alignment-inventory.md` | 对齐清单（PD-xxxx，六块） |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/client-capability.md` | 客户端能力表（通知表 + 上行命令表 + 字段读法） |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/slices.md` | 切片卡 S1–S8 + 覆盖矩阵 |
| `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/progress.md` | 台账（已存在，追加只增不删） |
| `docs/superpowers/specs/2026-10-02-party-dungeon-official-rewrite-design.md` | 设计稿（已提交 `e659916`，本 Phase 只在 §7.3 相关处做一次口径更正的引用） |
| `STATUS.md`（工作树内） | §2.7 P4 与第 19 条的口径更正（Phase 0 结束时；纯文档） |

**复用（不复制）**：`../2026-09-29-dungeon-party/work/` 下的官方侧助手与 `../friends-protocol-recon/work/dnf.py`（客户端 PE/capstone 助手）。新工具一律用绝对路径 `sys.path.insert` 引入它们，禁止复制粘贴。

---

### Task 1: 客户端协议面工具 `client_surface.py`

**Files:**
- Create: `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/client_surface.py`
- Test: 同文件 `--selftest`

**Interfaces:**
- Consumes: `../friends-protocol-recon/work/dnf.py`（提供 `u32(va)`、`dis(va,count)`、`cstr(va)`、`sections()`、`find_calls_to(target)`）
- Produces（后续任务与所有切片都要用的命令行）:
  - `python client_surface.py noti <id>` → 打印 `handler=0x????????` 或 `handler=DEFAULT(0x42A069)`、`default=yes/no`、前 12 条指令
  - `python client_surface.py noti-fields <id>` → 按顺序打印该处理器里 `0x8f5120/0x8f5140/0x8f5160`（byte/short/int 读取器）调用序列
  - `python client_surface.py cmds --log <server-log>` → TSV：`cmd \t name \t official_dispatcher \t seen_in_log \t our_server_const`
  - `python client_surface.py global-refs <hex-va>` → 该全局的直接 A1/A3/C7-05 访问点清单（`myUid=0xb75a40` 就是这么证出来的）

- [ ] **Step 1: 写自证测试（先红）**

在文件末尾写 `--selftest`，断言以下**项目已记录的真值**（`STATIC-FINDINGS.md` §B.1/§B.3 与本次会话新证）：

```python
def selftest() -> int:
    failures = []
    def expect(label, got, want):
        if got != want:
            failures.append(f"{label}: got {got!r}, want {want!r}")

    # 通知跳转表：case N = NOTI N，default = 0x42A069（工作树外，主检出路径）
    expect("noti 39 handler", handler_of(39), 0x421EA7)
    expect("noti 86 handler", handler_of(86), 0x427104)
    expect("noti 67 is default", handler_of(67), 0x42A069)   # STATIC-FINDINGS.md:329-334
    expect("noti 25 is default", handler_of(25), 0x42A069)
    expect("noti 11 handler", handler_of(11), 0x41C7E7)      # STATIC-FINDINGS.md:291

    # NOTI 11 每成员 7 字段；**线性序列首位是计数字节**（实现时实测 8 项）
    expect("noti-fields 11", read_sequence(11), ["byte", "short", "int", "int", "short", "int", "byte", "int"])

    # NOTI 86 的 5 号子类型是空操作：处理器体内只有 1/2 两个比较，其余 jmp 0x42740F → 0x42A069
    insns = dnf.dis(handler_of(86), 30)
    expect("noti86 type1 compare", any("cmp    byte ptr [ebp - 0x61f8], 1" in i for i in insns), True)
    expect("noti86 type2 compare", any("cmp    byte ptr [ebp - 0x61f8], 2" in i for i in insns), True)
    expect("noti86 default jump", any("jmp    0x42740f" in i for i in insns), True)

    # 全局引用扫描：myUid 全局 0xb75a40 有**两个**写入点（A3 赋值 + C7 05 重置为 0xFFFF 哨兵）。
    # 实现时实测更正：只认 A1/A3 会漏掉 C7 05 形式（见台账"更正"）。
    refs = global_refs(0xB75A40)
    expect("myUid write sites",
           [(s, k, imm) for s, k, _raw, imm in refs if k in ("WRITE", "WRITE-IMM")],
           [(0x4093AC, "WRITE", None), (0x99300D, "WRITE-IMM", 0xFFFF)])

    for f in failures:
        print("FAIL", f)
    print(f"client_surface selftest: {len(failures)} failures")
    return 1 if failures else 0
```

- [ ] **Step 2: 跑自证，确认红**

Run:
```
cd G:/Game/DNF_ACT1/DFLegacy/.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work
python client_surface.py --selftest
```
Expected: `ModuleNotFoundError` 或 `NameError`（函数尚未实现）——**不是** 断言失败。

- [ ] **Step 3: 实现四个子命令**

要点（照抄本节即可，不需要额外设计）：
1. 路径引导：用 `os.path.dirname(os.path.abspath(__file__))` 拼出主检出根，再 `sys.path.insert` 引入 `../friends-protocol-recon/work`（`dnf.py`）与 `../2026-09-29-dungeon-party/work`（`elf_syms.py` 等）。
2. `handler_of(id)` = `dnf.u32(0x42A081 + 4*id)`。
3. `read_sequence(id)`：取 `handler_of(id)` 起 0x600 字节，用 `capstone` 线性解码，遇到 `call` 目标 ∈ {0x8F5120:"byte", 0x8F5140:"short", 0x8F5160:"int", 0x8F5180:"str"} 就记录；遇到 `ret` 停。**只统计第一个基本块链**，遇到 `jmp/call 0x42A069` 即停（避免把相邻处理器算进来）。
4. `cmds`：读 `../2026-09-29-dungeon-party/work/official/opcode_cmd.txt`（`编号\t名字`）与 `catalog_dispatchers.txt`（名字含 `GET_ITEM` 的行取 `process=` 地址）；用正则从 `--log` 统计 `protocol=(\d+)` 出现次数；`our_server_const` 从 `src/DFLegacy.Protocol/GameProtocolEngine.cs` 里 `public const byte \w+Command = (\d+);` 正则抽取。
5. `global-refs <va>`：扫 `dnf.DATA` 找该 4 字节小端，判定前 1 字节 `0xA1`（读）/`0xA3`（写）/前 2 字节 `0xC7 0x05`（立即数写），VA 用 `dnf.sections()` 反查。**注意**：opcode 在地址前 **1** 字节，不是 5。
6. `main()` 用 `argparse` 分派子命令；`--selftest` 优先执行。

- [ ] **Step 4: 跑自证，确认绿**

Run: `python client_surface.py --selftest`
Expected: `client_surface selftest: 0 failures`，退出码 0。若 `noti-fields 11` 与期望不符，**先读** `STATIC-FINDINGS.md:289-305` 核对，再改工具而不是改期望（期望来自文档化真值）。

- [ ] **Step 5: 真值交叉：用真机日志跑一次上行命令表**

Run:
```
python client_surface.py cmds --log ../../2026-10-01-party-loot-card-alignment/real-client-run-42-server.log | Select-String -Pattern "^46\b|^48\b|^40\b|^135\b|^169\b"
```
Expected（**实现时实测修正过**）：`46/48/40` 的 `seen_as_cmd` 均 > 0；`135` 与 `169` 均 = 0（`135` 客户端从不发是 `STATUS.md:700-702` 的记录；`169` 见 `STATUS.md:784-785`）。
**必须分 type 统计**：`CMD 39 = FISHING` 与 `NOTI 39 = GET_ITEM` 是两套命名空间，混在一起会把通知的条数算到命令头上（第一版就这么错过；见台账更正）。把输出贴进台账。

- [ ] **Step 6: 提交**

```bash
cd G:/Game/DNF_ACT1/DFLegacy/.worktrees/party-official-realignment
git add -A   # .superpowers/ 被 ignore，本步通常无变更；若有则只提交 docs/
git commit -m "工具：客户端协议面查询（noti/noti-fields/cmds/global-refs + 自证）" --allow-empty
```
> 说明：工具本体在 git-ignored 目录，**不进仓库**；本步的提交只为台账记录"工具已就绪"这一里程碑（`--allow-empty` 允许无文件变更）。若你不希望留空提交，可跳过 Step 6，改为在 `progress.md` 记一行。

---

### Task 2: 官服侧抽取工具 `official_surface.py`

**Files:**
- Create: `.superpowers/sdd/2026-10-02-party-dungeon-official-rewrite/work/official_surface.py`
- Test: 同文件 `--selftest`

**Interfaces:**
- Consumes: `../2026-09-29-dungeon-party/work/{elf_syms.py,calls_in.py,dis_range.py,packet_layout.py,xrefs_elf.py}` 与 `work/official/{game_r.syms.txt,catalog_dispatchers.txt,opcode_cmd.txt,opcode_noti.txt,packet_senders.txt}`
- Produces:
  - `python official_surface.py func <name|0xVA>` → `地址 大小 名字`（名字走符号表，支持 `DisPatcher_GetItem::process` 与 `_ZN18DisPatcher_GetItem7processE...` 两种写法）
  - `python official_surface.py layout <func>` → 该函数内所有 `put_header` 点及其后紧随的 `put_*` 序列（含 class/cmd 常量）
  - `python official_surface.py callers <func>` → 调用点清单（`xrefs_elf.py` 包装）
  - `python official_surface.py skeleton <func>` → 输出 8 字段 PD 条目骨架（行为/裁决留 `TODO(填)`，lint 会拒绝 TODO 入库）
  - 全局开关 `--syms <符号文件>`（默认 `official/game_r.syms.txt`）与 `--elf <二进制>`（默认 `official/df_game_r.bin`）：**同一工具要能服务官服 game 与 relay 两个二进制**（relay 的 `--dump` 产物格式与 `game_r.syms.txt` 完全一致，见 Task 7 Step 1）

- [ ] **Step 1: 写自证测试（先红）**，锚定**本次会话已复核的真值**：

```python
def selftest() -> int:
    failures = []
    def expect(label, got, want):
        if got != want:
            failures.append(f"{label}: got {got!r}, want {want!r}")

    # DisPatcher_GetItem::process = 0x081c35c0 / 1832（game_r.syms.txt 可查）
    expect("getitem process addr", func_addr("DisPatcher_GetItem::process"), 0x081C35C0)
    expect("getitem process size", func_size("DisPatcher_GetItem::process"), 1832)
    # CParty::get_item = 0x085a3b98 / 10332
    expect("party get_item addr", func_addr("CParty::get_item"), 0x085A3B98)

    # NOTI 86 广播：put_header(class=0, cmd=0x56) @0x081c3a58，put_byte(5) @0x081c3a6b，send_all @0x081c3ac1
    sites = header_sites("DisPatcher_GetItem::process")
    expect("one header site", len(sites), 1)
    expect("header class", sites[0]["class"], 0)
    expect("header cmd", sites[0]["cmd"], 0x56)
    expect("header put at", sites[0]["addr"], 0x081C3A58)
    expect("byte 5 at", sites[0]["puts"][0], (0x081C3A6B, "put_byte", 5))
    expect("send_all at", sites[0]["sender"], (0x081C3AC1, "GameWorld::send_all"))

    sites73 = header_sites("Dispatcher_Select_Card")   # CMD 74（名字取自 catalog_dispatchers.txt）
    expect("cmd74 header cmd", sites73[0]["cmd"], 0x4A)
    return _report(failures)
```

- [ ] **Step 2: 跑自证确认红**：`python official_surface.py --selftest` → ImportError/NameError。

- [ ] **Step 3: 实现**

要点：
1. `func_addr/func_size`：解析 `game_r.syms.txt`（`地址\t大小\tFUNC\t名字`）与 `catalog_dispatchers.txt`（`名字 process=0x…(大小) dispatch_sig=…`）；名字支持 `A::b` → `_ZN…` 反查（用 `elf_syms.demangle` 的同构实现或直接模糊匹配 `名字里含 A 与 b`）。
2. `header_sites(func)`：在函数 `[addr, addr+size)` 内用 capstone 解码，找 `call 0x080CB8FC`（`InterfacePacketBuf::put_header`）；**从该调用点往回读 2 条 `mov dword ptr [esp+K], imm`** 得 `(class, cmd)`；再往后直到下一个 `put_header`/`ret`，收集 `put_byte/put_short/put_int/put_str` 调用点与其**立即数或局部变量名**（`[ebp-0x28]` 这种就原样打印，并在注释里标"局部变量，需人工追"）；`finalize` 之后的第一个 `call` 记为该包的发送者（`GameWorld::send_all` / `CUser::Send` / `CParty::send_to_party`）。
3. `layout` = `header_sites` 的人类可读渲染（可直接复用 `packet_layout.py` 的 `PUT` 映射表）。
4. `skeleton`：输出

```markdown
### PD-<块><序号> <一句话标题（TODO(填)）>
- 官服行为: TODO(填)
- 官服证据: <函数名>@<地址>(<大小>)；关键指令 <put_header 地址>/<send 地址>
- 可重跑命令: `python official_surface.py layout <函数名> | findstr /C:"<cmd 十六进制>"`
- 客户端可达性: TODO(填；先跑 client_surface.py noti <id>)
- 现状: TODO(填；文件:行)
- 裁决: TODO(填)
- 验收判据: TODO(填)
- 归属切片: TODO(填)
```

- [ ] **Step 4: 跑自证确认绿**：Expected `official_surface selftest: 0 failures`。
  若 `Dispatcher_Select_Card` 的 cmd 不是 `0x4A`，**先**用 `findstr /C:"Select_Card" catalog_dispatchers.txt` 确认名字，再决定是工具错还是名字错（CMD 74 = `0x4A` 是 `opcode_cmd.txt` 的既定事实）。

- [ ] **Step 5: 把本次会话的手工脚本并入**：将 `../2026-10-01-party-loot-card-alignment/work-scan-myuid.py` 的逻辑合并为 `client_surface.py global-refs`（Task 1 Step 3.5 已实现），并在原文件顶部加一行注释指向新位置（保留原文件，不删——台账纪律）。

- [ ] **Step 6: 提交**：同 Task 1 Step 6 的口径（工具在 ignore 目录，提交只记里程碑）。

---

### Task 3: 清单骨架 + `lint_inventory.py` + `verify_inventory.py`

**Files:**
- Create: `.../2026-10-02-party-dungeon-official-rewrite/alignment-inventory.md`（骨架 + 格式说明）
- Create: `.../work/lint_inventory.py`、`.../work/verify_inventory.py`
- Create（fixture）: `.../work/fixtures/inventory-bad.md`、`.../work/fixtures/inventory-good.md`

**Interfaces:**
- Produces:
  - `python lint_inventory.py <file.md>` → 每条违规一行 `文件:行: 原因`，退出码 = 违规数 ? 1 : 0
  - `python verify_inventory.py <file.md> [--only PD-xxxx] [--list]` → 逐条跑"可重跑命令"（在 `--cwd` 指定的目录，默认 `../2026-09-29-dungeon-party/work`），任一非零退出即整体非零
- 清单条目格式（lint 的判据，**逐字固定**）：

```markdown
### PD-3001 官服拾取广播是 NOTI 86 SERVER_BROADCAST
- 官服行为: 拾取成功后服务端向全场广播 class0/cmd0x56，载荷 u8 子类型 5 + u16 拾取者 uid + u32 索引
- 官服证据: DisPatcher_GetItem::process@0x081c35c0(1832)；put_header@0x081c3a58、put_byte(5)@0x081c3a6b、GameWorld::send_all@0x081c3ac1
- 可重跑命令:
  ```
  python dis_range.py 0x081c35c0+0x728 --show 0x081c3a20,0x081c3ad0 | findstr /C:"0x081c3a58"
  ```
- 客户端可达性: NOTI 86 → handler 0x427104；只实现子类型 1/2，type 5 落 default(0x42A069) = 空操作
- 现状: 我们发 NOTI 39 给拾取者本人（GameProtocolEngine.cs:3655-3711）；无 86 包
- 裁决: 对齐
- 验收判据: 单测断言 NOTI 86 包字节 = 05+uid+索引；真机：拾取后两端日志出现 protocol=86（本客户端画面不消失，见裁决说明）
- 归属切片: S5
```

- [ ] **Step 1: 写 fixture 与 lint 自证（先红）**

`fixtures/inventory-bad.md` 故意违反 5 条规则：缺 `- 现状:` 行、`- 裁决: 大概吧`、`- 归属切片: S9`、`- 官服证据:` 里既无 `0x` 地址也非"无"、条目标题写成 `### PD-3 标题`（编号必须是块号+两位序号）。`fixtures/inventory-good.md` 用上面那条 `PD-3001` 作为唯一合法条目。

自证断言（写在 `lint_inventory.py --selftest`）：

```python
def selftest() -> int:
    bad = lint(Path("fixtures/inventory-bad.md").read_text(encoding="utf-8"))
    good = lint(Path("fixtures/inventory-good.md").read_text(encoding="utf-8"))
    failures = []
    if len(bad) != 5:
        failures.append(f"bad fixture: lint 报 {len(bad)} 条，期望 5 条：{bad}")
    if good:
        failures.append(f"good fixture: 不应有违规，实得 {good}")
    for f in failures: print("FAIL", f)
    print(f"lint_inventory selftest: {len(failures)} failures")
    return 1 if failures else 0
```

- [ ] **Step 2: 跑自证确认红**：`python lint_inventory.py --selftest` → `FileNotFoundError`（fixture 未建）。

- [ ] **Step 3: 实现 lint**：条目切分 `^### (PD-\d{4}) (.*)$`；要求紧随的 8 个 `- <字段>:` 行**齐全且顺序正确**；`裁决 ∈ {对齐, 保留, 无法实现, 待真机}`；`归属切片 ∈ S1..S8`；`官服证据` 必须含 `0x[0-9a-fA-F]{6,8}` 或字面量 `无`；`可重跑命令` 的围栏里至少一条命令且含 `findstr /C:`（自证管道）；含 `TODO(填)` 即违规（骨架禁止入库）。

- [ ] **Step 4: 再跑自证确认绿**：Expected `lint_inventory selftest: 0 failures`。

- [ ] **Step 5: 写 `verify_inventory.py` 的自证**：fixture 里放两条命令——一条 `python -c "print('ok')" | findstr /C:"ok"`（应通过）与一条 `python -c "pass" | findstr /C:"ok"`（应失败）；断言 `verify(fixture)` 报 1 条失败且退出码 1。实现：提取围栏内命令 → `subprocess.run(cmd, shell=True, cwd=args.cwd)` → 非零即记失败并打印该条 stdout/stderr。

- [ ] **Step 6: 建清单骨架文件**：写入格式说明（6 条裁决定义 + 8 字段模板 + 编号规则 + "可重跑命令必须自证"），六块各留一个 `## <块名>` 标题，**不留任何 PD 条目**（条目由 Task 4–7 填）。

- [ ] **Step 7: 跑 lint 与 verify 于骨架**：`python lint_inventory.py ../alignment-inventory.md` → 0 违规；`python verify_inventory.py ../alignment-inventory.md --list` → 空清单。

- [ ] **Step 8: 提交**：口径同 Task 1 Step 6。

---

### Task 4: 填充 ③ 掉落与拾取（试点块，12 条）

**Files:**
- Modify: `.../alignment-inventory.md`（新增 `## ③ 掉落与拾取（S5）` 下的 PD-3001..PD-3012）
- Modify: `.../progress.md`

**Interfaces:**
- Consumes: Task 1/2 的工具命令行
- Produces: 12 条 8 字段齐备的条目；`lint` 0 违规、`verify` 全通

- [ ] **Step 1: 用工具抽骨架**（每条先跑 `official_surface.py skeleton <函数>` 与 `client_surface.py noti <id>`，把输出里的地址/处理器填进条目，**不要凭记忆写地址**）

- [ ] **Step 2: 逐条填 8 字段**，条目与证据来源如下（**每条都必须自己跑一遍复现命令再落笔**）：

| ID | 一句话 | 主证据 | 备注 |
| --- | --- | --- | --- |
| PD-3001 | 官服拾取成功广播 NOTI 86(class0/cmd0x56, type5) | `DisPatcher_GetItem::process@0x081c35c0`，`put_header@0x081c3a58`、`send_all@0x081c3ac1` | 本次会话新证（见上方示例条目，可直接用） |
| PD-3002 | 客户端 86 号处理器只实现子类型 1/2，type5 = 空操作 | `client_surface.py noti 86` → `0x427104` | 裁决 `对齐` + 后果说明 |
| PD-3003 | 客户端删地面物只有两处、门 = 载荷 uid == myUid | `client_surface.py global-refs 0xb75a40` + `dnf.find_calls_to(0x417c40)` | 结论：无服务端包能删他人地面物 |
| PD-3004 | NOTI 39 item 载荷 = u16+u16+4×u8+u16+u16；4 字节=按槽数量，非零触发 `" %s[%d]"` 行 | `dnf.dis(0x422275,120)`；字符串 `0x9998bc` | 与 run #42 `TX protocol=39 payload=020002…` 交叉 |
| PD-3005 | 官服对 CMD 46 回 ok/error（cmd 0x2e） | `DisPatcher_GetItem::send@0x081c3ce8`（`official_surface.py func`） | 裁决 `待真机`：我们重复拾取时不回包 |
| PD-3006 | CMD 46 的官服读取字段序列 | `reads_client_cmds.py` 无此项时用 `packet_reads.py 0x081c335c+0x250` | 与客户端发包体 `20 00 01 00 00 00 97 09` 对照 |
| PD-3007 | `CParty::get_item` 7 参数与 3 个出参语义 | `CParty::get_item@0x085a3b98(10332)` | 需读伪码，标 `[推断]` 直到逐字段证实 |
| PD-3008 | `CParty::IsRoutingState` 的判据 | `official_surface.py func CParty::IsRoutingState` | 决定"直接拾取 vs 掷骰"分支 |
| PD-3009 | routing 生命周期（7 个成员函数 + NOTI 281/282 + CMD 301） | `packet_senders.txt:207-209,458`；`STATIC-FINDINGS.md:119-157` | 证据闭合前不实现阈值（spec §11） |
| PD-3010 | `CItem::IsRoutingItem` 的品质阈值 | 同上 | 裁决暂 `[未验证]` → 写 `待真机` 或 `保留` |
| PD-3011 | NOTI 40 掉地载荷与客户端建物路径 | `Client NOTI 40 handler = 0x422CC3`（`client_surface.py noti 40`） | 与 `CreateDropItem` 逐字段对照 |
| PD-3012 | 掉落生成器（每个房间的被动掉落集合） | 官服 `DropCatalog`/`Script.pvf` 与参考实现日志 `generate_dungeon_drops` | 现状对齐检查 |

- [ ] **Step 3: 跑 lint**

Run: `cd .../work; python lint_inventory.py ../alignment-inventory.md`
Expected: 无输出、退出码 0。

- [ ] **Step 4: 跑 verify（逐条真跑命令）**

Run: `python verify_inventory.py ../alignment-inventory.md --only PD-3001` 然后去掉 `--only` 全跑
Expected: 每条 `OK <id>`，末行 `0 failures`。**任何一条失败都要改条目而不是放宽工具**（命令写错就修命令，结论写错就改结论并留痕）。

- [ ] **Step 5: 台账 + 提交**

`progress.md` 追加：12 条完成、verify 通过、发现的与 spec 不符之处（若有）。提交口径同 Task 1 Step 6。

---

### Task 5: 填充 ① 组队本体 + ② 进副本与换房（约 20 条）

**Files:**
- Modify: `.../alignment-inventory.md`（`## ① 组队本体（S1）`、`## ② 进副本与换房（S3·S4）`）
- Modify: `.../progress.md`

**Interfaces:**
- Consumes: Task 1/2 工具；`STATIC-FINDINGS.md` 的 §2.1/§2.2/附录 A.1/A.2/A.3/A.5 与 §B.1/B.2/B.3
- Produces: ①② 块条目齐备；`lint`/`verify` 全通

> **CMD 类条目的标准配方（每条都照这个走，不要凭记忆写地址）**：在 `../2026-09-29-dungeon-party/work/` 下：
> 1. 取名字：`python -c "print([l for l in open('official/opcode_cmd.txt',encoding='utf-8') if l.split(chr(9))[0]=='<编号>'])"`
> 2. 取派发器：`findstr /C:"<名字含的关键词>" official/catalog_dispatchers.txt`
> 3. 取包布局：`python official_surface.py layout <派发器类名>.process`
> 4. 取客户端是否真的发：`python client_surface.py cmds --log ../../2026-10-01-party-loot-card-alignment/real-client-run-42-server.log`
> 5. 取客户端处理器的字段读法（若是通知）：`python client_surface.py noti-fields <id>`

- [ ] **Step 1: ① 组队本体 10 条**：

| ID | 一句话 | 主证据 |
| --- | --- | --- |
| PD-1001 | NOTI 26 = host（队长）槽位，全队同值 | `CParty::send_host_info@0x0859cdd2`；客户端 `client_surface.py noti 26` → `0x41F144` + 使用点 `0x78D2B1`（`STATIC-FINDINGS.md:307-327`） |
| PD-1002 | NOTI 11 每成员 7 字段（客户端只读 7） | `STATIC-FINDINGS.md:289-305`；裁决 `保留`（不改） |
| PD-1003 | NOTI 153 + CMD 169 实时信息 | `CParty::send_party_realtime_info@0x0859cd24`、`get_party_realtime_info@0x0859cbac`；客户端 `noti 153`；`cmds --log` 看 169 是否出现 |
| PD-1004 | CMD 13 LEAVE_PARTY → `leave_user(party,user,2)` | `DisPatcher_LeaveParty::process@0x081c4256`（`STATUS.md:693-694`） |
| PD-1005 | CMD 45 GIVEUP_GAME → 同一个 `leave_user(...,2)` | `DisPatcher_GiveUpGame::process`（`STATUS.md:688-692`） |
| PD-1006 | 最后一人擅自退出**保留** 1 人队 | `CParty::leave_user@0x0859c114`（`STATUS.md:744-752`） |
| PD-1007 | `leave_user` 不发 NOTI 10；`walkout_member` 才发 | `CParty::SendWalkout@0x0859cb00`（`STATUS.md:667-670`） |
| PD-1008 | CMD 329/336 队伍位置变更（336 队长专属） | `catalog_dispatchers.txt` 的 `Dispatcher_Change_PartyMemberPosition`；客户端 `cmds --log` |
| PD-1009 | CMD 4 `SELECT_CHARACTER` 的官服收尾语义（同会话换角色） | `opcode_cmd.txt` 第 4 项 = `SELECT_CHARACTER`；`DisPatcher_SelectCharac@0x081c6380(1189)`（`findstr /C:"SelectCharac" official/catalog_dispatchers.txt`）；现状见 `STATUS.md:601` |
| PD-1010 | 队长继任/主机迁移 | `CParty::leave_user` 内的 `change_manager` 调用点：`python official_surface.py callers CParty::leave_user`；找不到独立符号就记 `无` + 在 `callers` 输出里标出实际被调函数地址 |

- [ ] **Step 2: ② 进副本与换房 10 条**：

| ID | 一句话 | 主证据 |
| --- | --- | --- |
| PD-2001 | `CParty::game_start` 的完整调用序列 | `CParty::game_start@0x0859d718(2489)`；`calls_party_dungeon.txt` |
| PD-2002 | 同区域是硬前提 | `STATIC-FINDINGS.md:72`；`STATUS.md:604` |
| PD-2003 | 入场报文序列（外观 → 名册 → NOTI 26/11/28/29） | `STATUS.md:364`（探针真机结论）→ 裁决 `保留` 并注明证据类别是 `[真机]` |
| PD-2004 | CMD 48 官服 read 10 字段 | `DisPatcher_MoveMap::read@0x081c47ba(726)`；`reads_client_cmds.txt` |
| PD-2005 | "门口全员到齐"的官服判据 | `CParty::move_map`（`calls_party_dungeon.txt`）→ **决定 DoorAssembly 去留**（spec §9） |
| PD-2006 | CMD 40 2×int + `check_allmember_loading` + 清零 loaded | `DisPatcher_FinishLoad::read@0x08264b66(151)`；`STATIC-FINDINGS.md:190-212` |
| PD-2007 | 全员齐才广播空载荷 NOTI 30 给全队 | 同上；客户端 `noti 30` → `0x4200B6` |
| PD-2008 | NOTI 29 载荷构造 | `CBattle_Field::MakePacket@0x0830911a`；客户端 `noti 29` → `0x41F7D3` |
| PD-2009 | 空 boss 房可结算的官服判据 | `STATUS.md:602`；需在官服找等价判据，找不到记 `无` |
| PD-2010 | 怪物死亡三态（普通/Boss/ALLDIE） | 前身 spec §3.3：`docs/superpowers/specs/2026-10-01-dungeon-official-alignment-design.md:107-117`；官服侧用 `official_surface.py callers` 找 `CBattle_Field` 的死亡入口 |

- [ ] **Step 3: 跑 lint + verify**：命令同 Task 4 Step 3/4（Expected：0 违规、0 failures）。

- [ ] **Step 4: 台账 + 提交**。

---

### Task 6: 填充 ④ 结算与翻牌 + ⑥ 附属（约 14 条）

**Files:**
- Modify: `.../alignment-inventory.md`（`## ④ 结算与翻牌（S6）`、`## ⑥ 副本内其它（S7）`）
- Modify: `.../progress.md`

- [ ] **Step 1: ④ 结算与翻牌 8 条**：

| ID | 一句话 | 主证据 |
| --- | --- | --- |
| PD-4001 | `dungeon_clear_state` 2→3→4→5 与四个定时器 key | `STATUS.md:588-595`（官服定时器静态实证） |
| PD-4002 | CMD 73 上行每槽 ushort = 疲劳状态（0/1/0xFFFF） | `Dispatcher_Card_Select_Right_State`；`STATUS.md:592-593` |
| PD-4003 | CMD 74 上行 + `SendEachCardInfo` 全队广播 | `Dispatcher_Select_Card`；客户端 `dnf.dis(0x42...)`+ run #42 逐字节 |
| PD-4004 | `CompleteCardSelect` 自动补位算法 | `ida_cardflow_out.txt:51-87` |
| PD-4005 | NOTI 34/35 载荷 | `CParty::makeBestClearTimePacket`；`GameProtocolEngine.cs:3742-3800` 对照 |
| PD-4006 | 回城定时器：CMD 75 EPLP + key 20/21（**间隔参数位序未验证**） | `STATUS.md:626-634`；裁决 `待真机` |
| PD-4007 | NOTI 67 客户端 default → 不发 | `client_surface.py noti 67` → `0x42A069` |
| PD-4008 | `dungeon_clear` 逐人发放 + `SetEPLPState(2)` + key 21 | `STATUS.md:595` |

- [ ] **Step 2: ⑥ 附属 6 条**：

| ID | 一句话 | 主证据 |
| --- | --- | --- |
| PD-6001 | 副本内死亡 `die_user` ≠ 退队 | `STATUS.md:672-674` |
| PD-6002 | 疲劳 NOTI 36 每房各自扣 | `STATUS.md:605`；客户端 `noti 36` → `0x42119F` |
| PD-6003 | 地狱模式入场券扣谁 | `STATUS.md:599`（现状只扣队长）→ 官服证据待补 |
| PD-6004 | NOTI 31 房间开放 | 客户端 `noti 31` → `0x41FE50` |
| PD-6005 | 位置同步 CMD 37 / NOTI 22/23/24 | `reads_client_cmds.txt` 的 `DisPatcher_SetPos::read`；`STATUS.md:425` 的红线（**绝不用 NOTI 24 拉地图**） |
| PD-6006 | 组队经验/物品分配系数 | `clear-card rewards prepared` 的 `partyMemberCount` 与官服对照 |

- [ ] **Step 3: 跑 lint + verify**；**Step 4: 台账 + 提交**。（命令同 Task 4。）

---

### Task 7: ⑤ 跨端中继勘察（relay/stun 二进制）

**Files:**
- Create: `.../2026-10-02-party-dungeon-official-rewrite/relay-recon.md`
- Modify: `.../alignment-inventory.md`（`## ⑤ 跨端 UDP 中继（S2·S8）`，PD-5001..PD-5006）
- Modify: `.../progress.md`

**Interfaces:**
- Consumes: `../2026-09-29-dungeon-party/work/official/template/neople/relay/df_relay_r`（ELF32、418,098 B、**含 `.symtab` 255,204 B**）、`stun/df_stun_r`（131,126 B）、`relay/cfg/relay_200.cfg`、`calls_p2p.txt`、`calls_reqpeer.txt`
- Produces: relay 的**包格式/端口/会话键**结论 + 6 条 PD 条目 + 与客户端可达性的对照（**只做勘察，不实现中继**——落地是 S8）

- [ ] **Step 1: 复用 `elf_syms.py` 读 relay 符号表**（路径口径：从 `../2026-09-29-dungeon-party/work/` 起算，relay 在 `official/template/neople/relay/` 下）

Run:
```
cd G:/Game/DNF_ACT1/DFLegacy/.superpowers/sdd/2026-09-29-dungeon-party/work
python elf_syms.py official/template/neople/relay/df_relay_r --dump official/relay.syms.txt
python elf_syms.py official/template/neople/relay/df_relay_r --grep "Relay|relay|main" --limit 40
```
Expected: 首行 `# …: 共 2733 个符号`（**已实测**：relay 是完整符号 ELF32）；生成 `official/relay.syms.txt`（格式与 `game_r.syms.txt` 一致，可直接喂给 `official_surface.py --syms official/relay.syms.txt`）；grep 命中若干函数名。
若符号数为 0（被 strip）→ 立即停止本任务并在报告中记 `[未验证]`，改走真机抓包路线（`ref-proxy`）。

- [ ] **Step 2: 读 `relay_200.cfg` 与启动脚本**：`cat relay/cfg/relay_200.cfg`、`template/init/run/start_relay.sh`，记录端口与参数写进报告。

- [ ] **Step 3: 定位 relay 的主循环与包分类**

Run（`<主循环地址>` 取 Step 1 grep 命中的函数；`calls_in.py` 的 `--elf` 指向 relay）:
```
python calls_in.py <主循环地址>+<大小> --elf official/template/neople/relay/df_relay_r
```
Expected: 能列出 relay 对每个连接的读/写与"按 key 配对两个连接"的调用序列（这是中继的本质）。把关键地址与序列写进 `relay-recon.md`，并明确回答：**relay 是"按会话键把 A 的字节转给 B"还是"解析游戏包"**（前者=纯转发，后者=需复刻解析）。

- [ ] **Step 4: 填 PD-5001..PD-5006**：

| ID | 一句话 | 主证据 |
| --- | --- | --- |
| PD-5001 | relay 进程的主循环与配对逻辑 | 本任务 Step 1–3 的地址 |
| PD-5002 | relay 端口/会话键/配置项 | `relay_200.cfg`、`start_relay.sh` |
| PD-5003 | CMD 2 契约 + ACK + 转发其他用户 | `DisPatcher_SetIP::dispatch_sig@0x081f468c`、转发点 `0x081f4c71`（`STATIC-FINDINGS.md:111`） |
| PD-5004 | CMD 100/479/599 与 `DisPatcher_ResPeer` | `DisPatcher_ResPeer::dispatch_sig@0x081f088e(9341)`；`calls_p2p.txt` |
| PD-5005 | NOTI 11/26 构造者与调用点 | `CParty::send_party_ipinfo@0x0859cea2`、`send_host_info` |
| PD-5006 | 游戏数据报（`mt`）到底发给谁 | `STATUS.md:582-587` 的 UDP 审计（现状 `mt=0`）→ 裁决 `待真机`，观察点写清楚 |

- [ ] **Step 5: 跑 lint + verify**；**Step 6: 台账 + 报告 + 提交**。

---

### Task 8: 切片卡 + 覆盖矩阵 + 工具 README + STATUS 口径更正

**Files:**
- Create: `.../slices.md`（S1–S8 切片卡 + 覆盖矩阵）
- Create: `.../work/README.md`（工具用法）
- Modify: `.../client-capability.md`（由 Task 1 的 `noti`/`noti-fields`/`cmds` 输出汇总；若 Task 1 未单独成文则本步创建）
- Modify（工作树内）: `STATUS.md`（§2.7 P4 与第 19 条的口径更正）

**Interfaces:**
- Consumes: Task 1–7 的全部产出
- Produces: 后续每个切片开工所需的输入（切片卡 + 覆盖矩阵 + 工具用法）

- [ ] **Step 1: 生成覆盖矩阵**：在 `slices.md` 顶部写一张表，行 = 六块，列 = `条目数 / 已 verify / 裁决分布（对齐·保留·无法实现·待真机）`。数据用 `lint_inventory.py --stats`（本步给 lint 加一个只读的 `--stats` 输出，不改判据）。

- [ ] **Step 2: 写 S1–S8 切片卡**（每片一卡，字段固定：范围 / 依赖 / **该片删除的旧实现（文件:行，开工时填精确值）** / 自动化用例 / 真机验收点 / DoD / 风险）。顺序与内容照 spec §5.3 与 §2 的"切片"列：`S1 ① → S2 ⑤勘察与地址表 → S3 ②入场 → S4 ②换房 → S5 ③ → S6 ④ → S7 ⑥ → S8 ⑤落地`。

- [ ] **Step 3: 写 `work/README.md`**：每个工具一行用法 + 工作目录 + 依赖（`pefile`/`capstone`）+ 产物路径 + `--selftest` 说明。要求：**照抄本计划里的命令行**，不要重写。

- [ ] **Step 4: 更正 `STATUS.md` 口径**（工作树内，纯文档）：
  - §2.7 表里 P4 行的"修法见注释"改为：`❌ 未通过（客户端版本限制：官服 NOTI 86(type5) 在本客户端不实现，静态已证；服务端无可用移除机制）`；
  - 第 19 条末尾追加一段"2026-10-02 更正"，写清：官服那条 7 字节包是 **NOTI 86** 而**不是** NOTI 39 的变体；本客户端 86 号 case 只实现子类型 1/2；客户端删地面物仅两处且以"我是拾取者"为门；因此"另发一条移除包"在本客户端是空操作 —— 并标 `[作废]` 指向 `GameProtocolEngine.cs:3682-3690` 的旧结论；
  - 证据用本次会话的命令行（见 `progress.md`）。

- [ ] **Step 5: 跑四条基线命令**确认 Phase 0 未伤生产代码（Expected：三条 0 failures、构建 0 警告 0 错误）。

- [ ] **Step 6: 提交**

```bash
cd G:/Game/DNF_ACT1/DFLegacy/.worktrees/party-official-realignment
git add -f STATUS.md
git commit -m "文档：STATUS §2.7 更正拾取移除信号的口径（官服是 NOTI 86；本客户端不实现子类型 5）"
```

- [ ] **Step 7: 收尾**：`progress.md` 写 Phase 0 完成结论（含 `lint`/`verify`/基线三条证据），并把"下一步 = S1 切片计划"写成一行交接。

---

## Self-Review（写完计划后对 spec 逐节核对）

1. **Spec 覆盖**：§1 目标 → 全局约束与每条的"8 字段 + 裁决"；§2 六块 → Task 4/5/6/7 各块的 PD 条目；§3 两层证据与四条裁决 → Task 3 的 lint 判据 + Task 1/2 的双工具；§4 目标架构 → 本 Phase 不涉及（由 S1 起的切片计划落实）；§5.1 清单 → Task 3/4/5/6/7；§5.2 能力表 → Task 1 `cmds`/`noti-fields` + Task 8 `client-capability.md`；§5.3 切片卡 → Task 8；§5.4 工具固化 → Task 1/2/3 + Task 8 `README.md`；§5.5 分层 → 本计划只做第一层；§6 分片执行 → 切片卡里写 DoD；§7.3 旧 P 表口径 → Task 8 Step 4；§8 红线 → 全局约束逐条；§9 取代清单 → Task 5 PD-2005（DoorAssembly 去留）与 Task 4 PD-3009（routing 取证）；§10 风险 → Task 7 的 relay 符号表失败分支 + Task 8 Step 5。**无遗漏项。**
2. **占位符扫描**：本计划不含 "TBD/TODO/稍后补"；`TODO(填)` 只出现在**工具生成的骨架**里，且 lint 明令禁止它入库。每个代码步骤都给了代码或逐条要点；每个文档步骤都给了条目表与证据地址。
3. **类型/命名一致**：`client_surface.py` 的子命令名（`noti`/`noti-fields`/`cmds`/`global-refs`）在 Task 1、5、6、8 中写法一致；`official_surface.py` 的子命令（`func`/`layout`/`callers`/`skeleton`）在 Task 2、4、5、6、7 中一致；`lint_inventory.py` / `verify_inventory.py` 的入参（清单路径）与 `--only/--list/--stats` 在各处一致；PD 编号规则（块号+两位序号）在 Task 3 的 lint 与 Task 4/5/6/7 的条目表中一致。
4. **一处刻意的不一致（已声明）**：Task 1/2/3 的"提交"步骤在工具位于 git-ignored 目录时用 `--allow-empty`，并给了"跳过提交、改记台账"的替代——这是为了让"工具就绪"成为可追溯里程碑，同时不污染仓库。
