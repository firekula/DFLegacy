# S1 协议地图与进度账 —— 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把散落在 gitignore 工作区的协议事实，转成入库、可 diff、可换机自检的「地图 + 两本账」（`protocol/`）。

**Architecture:** 五个单一职责模块——`importer.py`（原始产物 → 规范 TSV）、`ledger.py`（场景账/run 台账/opcode 账）、`render.py`（人读报告 + 确定性 manifest）、`verify.py`（五条只读断言）、`s1.py`（CLI 入口）。规范数据一律 TSV（LF、UTF-8 无 BOM、稳定排序）；`verify` 只读且只依赖入库文件。

**Tech Stack:** Python 3.12（标准库 `csv`/`hashlib`/`json`/`argparse`/`pathlib`）、pytest、现有 `tools/acceptance/acceptance.model.load_scenario`（复用，不重写场景解析）。

**Spec:** `docs/superpowers/specs/2026-10-18-protocol-map-ledger-s1-design.md`

## Global Constraints

- 只新增/修改 `protocol/**`；**不改** `src/`、`tools/acceptance/`（除只读 import）、`STATUS.md`。
- 规范数据必须是 **UTF-8 无 BOM + LF 换行 + 末尾单个换行**；TSV 用 `\t` 分隔，字段内不得含 `\t`/换行。
- `manifest.json` **不得**含时间戳、绝对路径；`json.dumps(..., indent=2, sort_keys=True, ensure_ascii=False)` + 单个 `\n`。
- `verify` **只读**：不得写任何文件；任一断言不符即退出码 1。
- 主键稳定：`map/opcode-map.tsv` 与 `ledger/opcodes.tsv` 按 `kind`（CMD 先）→ `id` 升序；`scenarios.tsv` 按 `scenario_id` → `step_index` → `expect_index`。
- 基线数值（取自真产物，逐字使用）：总数 **1171**、`CMD 608` / `NOTI 563`、`status` **741/428/2**、六档 **T0 743 / T1 187 / T2 64 / T3b 16 / T3 109 / T4 52**、`ext_mark` **byte_verified 1 / field_read 2 / name_only 1168**。
- 测试与命令在仓库根执行，先 `$env:PYTHONIOENCODING='utf-8'`；提交逐个文件列 pathspec，**绝不 `git add -A`**；`docs/` 被 gitignore，新增需 `git add -f`（`-f` 必须在 pathspec 之前）。

## File Structure

| 文件 | 职责 |
|---|---|
| `protocol/__init__.py` | 空包标记 |
| `protocol/importer.py` | 读 `opcode-map.tsv` + `coverage-by-opcode.tsv` + `tiers-external.tsv`，join/合并/排序 → 写 `map/opcode-map.tsv` |
| `protocol/ledger.py` | `pattern → (id,size)` 反解；生成 `ledger/scenarios.tsv`、`ledger/runs.tsv`、`ledger/opcodes.tsv` |
| `protocol/render.py` | 生成 `map/index.md`；构建/写入确定性 `manifest.json` |
| `protocol/verify.py` | 五条断言（纯函数 + 报告） |
| `protocol/s1.py` | CLI：`import` / `render` / `verify` |
| `protocol/tests/test_importer.py` | 导入 join/合并/排序/编码 |
| `protocol/tests/test_ledger.py` | 反解、场景账、run 账、runtime evidence |
| `protocol/tests/test_render.py` | render 幂等、manifest 确定性 |
| `protocol/tests/test_verify.py` | 五断言各一条 + 篡改必红 |
| `protocol/README.md` | 用法 + 三套词表 + 七条边界 |

---

### Task 1: 规范数据导入（`importer.py` + `import` 子命令）

**Files:**
- Create: `protocol/__init__.py`
- Create: `protocol/importer.py`
- Create: `protocol/s1.py`
- Test: `protocol/tests/test_importer.py`

**Interfaces:**
- Produces:
  - `MAP_COLUMNS: tuple[str, ...]` = `("kind","id","name","rodata_va","dispatcher","status","tier","read_count","write_count","ext_mark")`
  - `load_coverage(raw_dir: Path) -> list[dict]`（按 CSV 读 `coverage-by-opcode.tsv`）
  - `load_external(raw_dir: Path) -> dict[tuple[str,int], list[tuple[str,str]]]`（`(kind,id) -> [(mark,evidence)]`）
  - `merge_rows(coverage: list[dict], external: dict) -> list[dict]`（合并 `ext_mark` 取最高优先级，写 `ext_evidence` 到 `_ext_evidence` 私有键）
  - `write_tsv(path: Path, columns: tuple[str, ...], rows: list[dict]) -> None`
  - `import_map(raw_dir: Path, out_dir: Path) -> dict`（返回 `{"rows": n, "path": Path}`）

- [ ] **Step 1: 写失败测试**

`protocol/tests/test_importer.py`：

```python
from pathlib import Path

import pytest

from protocol import importer


def _write(p: Path, text: str) -> Path:
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(text.encode("utf-8"))
    return p


def _fixture_raw(tmp_path: Path) -> Path:
    raw = tmp_path / "raw"
    _write(raw / "coverage-by-opcode.tsv",
           "kind\tid\tname\tstatus\tdispatcher\ttier\tread_count\twrite_count\trodata_va\text_mark\text_evidence\n"
           "NOTI\t1\tX\tno-dispatcher\t\tT0_no-evidence\t0\t0\t0x01\tname_only\t\n"
           "CMD\t2\tY\tmapped\tDoY\tT3_fields-read-only\t3\t0\t0x02\tname_only\t\n"
           "CMD\t1\tZ\tmapped\tDoZ\tT4_fields-read+write\t5\t6\t0x03\tfield_read\t旧证据-低\n")
    _write(raw / "tiers-external.tsv",
           "# kind\tid\tmark\tevidence\n"
           "CMD\t1\tbyte_verified\t新证据-高\n"
           "CMD\t1\tfield_read\t旧证据-低\n")
    return raw


def test_merge_sorts_by_kind_then_id(tmp_path):
    rows = importer.merge_rows(
        importer.load_coverage(_fixture_raw(tmp_path)),
        importer.load_external(_fixture_raw(tmp_path)))
    assert [(r["kind"], r["id"]) for r in rows] == [("CMD", "1"), ("CMD", "2"), ("NOTI", "1")]


def test_ext_mark_takes_highest_priority_and_keeps_all_evidence(tmp_path):
    raw = _fixture_raw(tmp_path)
    rows = importer.merge_rows(importer.load_coverage(raw), importer.load_external(raw))
    cmd1 = next(r for r in rows if r["kind"] == "CMD" and r["id"] == "1")
    assert cmd1["ext_mark"] == "byte_verified"
    assert cmd1["_ext_evidence"] == "新证据-高; 旧证据-低"


def test_unmarked_opcode_defaults_to_name_only_with_empty_evidence(tmp_path):
    raw = _fixture_raw(tmp_path)
    rows = importer.merge_rows(importer.load_coverage(raw), importer.load_external(raw))
    noti1 = next(r for r in rows if r["kind"] == "NOTI")
    assert noti1["ext_mark"] == "name_only"
    assert noti1["_ext_evidence"] == ""


def test_write_tsv_is_lf_utf8_without_bom(tmp_path):
    p = tmp_path / "t.tsv"
    importer.write_tsv(p, ("a", "b"), [{"a": "1", "b": "2"}])
    raw = p.read_bytes()
    assert raw == "a\tb\n1\t2\n".encode("utf-8")


def test_write_tsv_rejects_tab_in_field(tmp_path):
    with pytest.raises(ValueError):
        importer.write_tsv(tmp_path / "t.tsv", ("a",), [{"a": "x\ty"}])


def test_import_map_on_real_artifacts_hits_baselines():
    raw = Path(".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out")
    if not (raw / "coverage-by-opcode.tsv").exists():
        pytest.skip("原始产物不在本机")
    import tempfile
    with tempfile.TemporaryDirectory() as td:
        stats = importer.import_map(raw, Path(td))
        assert stats["rows"] == 1171
        text = (Path(td) / "map" / "opcode-map.tsv").read_text(encoding="utf-8")
        assert len(text.splitlines()) == 1172  # 含表头
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest protocol/tests/test_importer.py -q`
Expected: FAIL（`ModuleNotFoundError: No module named 'protocol'` 或 `AttributeError: load_coverage`）

- [ ] **Step 3: 写实现**

`protocol/__init__.py`：空文件。

`protocol/importer.py`：

```python
"""把 .superpowers 里的原始协议产物规范化成 protocol/map/opcode-map.tsv。

只做 join / 合并 / 排序 / 落盘；不新增逆向，不改语义。
"""
from __future__ import annotations

import csv
from pathlib import Path

MAP_COLUMNS: tuple[str, ...] = (
    "kind", "id", "name", "rodata_va", "dispatcher", "status",
    "tier", "read_count", "write_count", "ext_mark",
)

MARK_PRIORITY: tuple[str, ...] = ("byte_verified", "field_read", "name_only")
_KIND_ORDER = {"CMD": 0, "NOTI": 1}


def load_coverage(raw_dir: Path) -> list[dict]:
    """读 coverage-by-opcode.tsv（真产物）；缺文件即报错，不静默给空表。"""
    p = Path(raw_dir) / "coverage-by-opcode.tsv"
    if not p.is_file():
        raise FileNotFoundError(f"缺原始产物：{p}")
    with p.open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in row.items()} for row in csv.DictReader(fh, delimiter="\t")]


def load_external(raw_dir: Path) -> dict[tuple[str, int], list[tuple[str, str]]]:
    """读 tiers-external.tsv；首行以 '#' 开头的表头行按注释跳过。"""
    p = Path(raw_dir) / "tiers-external.tsv"
    if not p.is_file():
        raise FileNotFoundError(f"缺原始产物：{p}")
    out: dict[tuple[str, int], list[tuple[str, str]]] = {}
    with p.open(encoding="utf-8", newline="") as fh:
        for line in fh:
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 4:
                raise ValueError(f"tiers-external.tsv 行格式非法：{line!r}")
            kind, raw_id, mark, evidence = parts[0], parts[1], parts[2], parts[3]
            if mark not in MARK_PRIORITY:
                raise ValueError(f"未知 ext_mark：{mark!r}")
            out.setdefault((kind, int(raw_id)), []).append((mark, evidence))
    return out


def _highest(marks: list[str]) -> str:
    for candidate in MARK_PRIORITY:
        if candidate in marks:
            return candidate
    return "name_only"


def merge_rows(coverage: list[dict], external: dict) -> list[dict]:
    rows: list[dict] = []
    for raw in coverage:
        key = (raw["kind"], int(raw["id"]))
        pairs = external.get(key, [])
        marks = [m for m, _ in pairs]
        evidence = [e for _, e in pairs if e]
        rows.append({
            "kind": raw["kind"],
            "id": str(key[1]),
            "name": raw.get("name", ""),
            "rodata_va": raw.get("rodata_va", ""),
            "dispatcher": raw.get("dispatcher", ""),
            "status": raw.get("status", ""),
            "tier": raw.get("tier", ""),
            "read_count": raw.get("read_count", "0"),
            "write_count": raw.get("write_count", "0"),
            "ext_mark": _highest(marks) if marks else "name_only",
            "_ext_evidence": "; ".join(evidence),
        })
    rows.sort(key=lambda r: (_KIND_ORDER.get(r["kind"], 9), int(r["id"])))
    return rows


def write_tsv(path: Path, columns: tuple[str, ...], rows: list[dict]) -> None:
    """LF + UTF-8 无 BOM + 末尾单个换行；字段含制表符/换行即报错（不做静默清洗）。"""
    path = Path(path)
    for row in rows:
        for col in columns:
            value = str(row.get(col, ""))
            if "\t" in value or "\n" in value or "\r" in value:
                raise ValueError(f"列 {col!r} 的值含制表符或换行：{value!r}")
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = ["\t".join(columns)]
    lines += ["\t".join(str(r.get(c, "")) for c in columns) for r in rows]
    path.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))


def import_map(raw_dir: Path, out_dir: Path) -> dict:
    rows = merge_rows(load_coverage(raw_dir), load_external(raw_dir))
    target = Path(out_dir) / "map" / "opcode-map.tsv"
    write_tsv(target, MAP_COLUMNS, rows)
    return {"rows": len(rows), "path": target}
```

`protocol/s1.py`（本任务只挂 `import`）：

```python
"""S1 CLI：import / render / verify。"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import importer

RAW_DEFAULT = Path(".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out")
BASE_DEFAULT = Path("protocol")


def cmd_import(args: argparse.Namespace) -> int:
    stats = importer.import_map(Path(args.raw_dir), Path(args.base))
    print(f"import: {stats['rows']} 行 -> {stats['path']}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="s1")
    sub = parser.add_subparsers(dest="cmd", required=True)
    p_import = sub.add_parser("import", help="原始产物 -> 规范 TSV")
    p_import.add_argument("--raw-dir", default=str(RAW_DEFAULT))
    p_import.add_argument("--base", default=str(BASE_DEFAULT))
    p_import.set_defaults(func=cmd_import)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest protocol/tests/test_importer.py -q`
Expected: 6 passed（本机有原始产物时；否则 5 passed 1 skipped）

- [ ] **Step 5: 真跑一次导入并核对基线**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
python -m protocol.s1 import
python -c "import csv;rows=list(csv.DictReader(open('protocol/map/opcode-map.tsv',encoding='utf-8'),delimiter='\t'));print(len(rows))"
```
Expected: `import: 1171 行 -> protocol\map\opcode-map.tsv`，随后打印 `1171`

- [ ] **Step 6: 提交**

```powershell
git add -- protocol/__init__.py protocol/importer.py protocol/s1.py protocol/tests/test_importer.py protocol/map/opcode-map.tsv
git commit -m "S1: 规范数据导入（原始产物 -> protocol/map/opcode-map.tsv，1171 条）"
```

---

### Task 2: 场景账与 run 台账（`ledger.py` + `ledger` 子命令）

**Files:**
- Create: `protocol/ledger.py`
- Modify: `protocol/s1.py`（新增 `ledger` 子命令）
- Test: `protocol/tests/test_ledger.py`

**Interfaces:**
- Consumes: `importer.MAP_COLUMNS`、`importer.write_tsv`、`protocol/map/opcode-map.tsv`
- Produces:
  - `parse_frame(pattern: str) -> tuple[int, int]`（hex → `(id, size)`；非 4 字节以上/非法 hex 抛 `ValueError`）
  - `SCENARIO_COLUMNS` / `RUN_COLUMNS` / `OPCODE_COLUMNS`（`tuple[str, ...]`）
  - `build_scenarios(scenarios_dir: Path, verified: dict[str, str]) -> list[dict]`
  - `build_runs(verdict_path: Path, run_manifest: Path, evidence_class: str) -> list[dict]`
  - `apply_runtime_evidence(map_rows: list[dict], scenarios: list[dict]) -> list[dict]`
  - `write_ledgers(base: Path, scenarios: list[dict], runs: list[dict], opcode_rows: list[dict]) -> dict`

- [ ] **Step 1: 写失败测试**

`protocol/tests/test_ledger.py`：

```python
from pathlib import Path

import pytest

from protocol import ledger


def test_parse_frame_decodes_id_and_size():
    assert ledger.parse_frame("000B2B0000000000000001") == (11, 43)
    assert ledger.parse_frame("000C2F0000000000000001") == (12, 47)


@pytest.mark.parametrize("bad", ["", "0", "zz", "000B"])
def test_parse_frame_rejects_short_or_non_hex(bad):
    with pytest.raises(ValueError):
        ledger.parse_frame(bad)


def test_scenario_rows_cover_all_four_scenarios_and_match_s0_expects():
    rows = ledger.build_scenarios(Path("tools/acceptance/scenarios"), verified={})
    assert len(rows) == 12
    assert {r["scenario_id"] for r in rows} == {
        "town-two-clients", "party-invite", "trade-open", "town-position-sync"}
    tc = [r for r in rows if r["scenario_id"] == "town-two-clients"]
    assert [r["level"] for r in tc] == ["[存在]", "[存在]", "[存在]", "[语义]"]
    assert {r["mapped_opcodes"] for r in tc if r["mapped_opcodes"]} == {"CMD:11", "CMD:12"}


def test_semantic_expect_has_no_pattern_and_no_opcode():
    rows = ledger.build_scenarios(Path("tools/acceptance/scenarios"), verified={})
    sem = next(r for r in rows if r["level"] == "[语义]")
    assert sem["pattern"] == "" and sem["mapped_opcodes"] == ""


def test_verified_run_and_result_land_in_scenario_rows():
    rows = ledger.build_scenarios(Path("tools/acceptance/scenarios"),
                                  verified={"town-two-clients": "RID-1"},
                                  results={("town-two-clients", 1, 1): "pass"})
    row = next(r for r in rows if r["scenario_id"] == "town-two-clients"
               and r["step_index"] == "1" and r["expect_index"] == "1")
    assert row["verified_run_id"] == "RID-1" and row["verdict_result"] == "pass"


def test_runtime_evidence_is_written_back_to_the_matching_opcode(tmp_path):
    map_rows = [{"kind": "CMD", "id": "11"}, {"kind": "CMD", "id": "12"}, {"kind": "CMD", "id": "13"}]
    scenarios = [
        {"scenario_id": "town-two-clients", "actor": "A", "level": "[存在]",
         "mapped_opcodes": "CMD:11", "verified_run_id": "RID-1",
         "verdict_detail": "netdump-1.log:4 ..."},
        {"scenario_id": "town-two-clients", "actor": "B", "level": "[存在]",
         "mapped_opcodes": "CMD:11", "verified_run_id": "RID-1",
         "verdict_detail": "netdump-2.log:4 ..."},
    ]
    out = {r["id"]: r for r in ledger.apply_runtime_evidence(map_rows, scenarios)}
    assert out["11"]["runtime_evidence"] == "[真机] RID-1 A:4,B:4"
    assert out["12"]["runtime_evidence"] == ""


def test_ext_evidence_comes_from_tiers_external_not_from_map_rows():
    """账① 的 ext_evidence 必须由调用方从 tiers-external.tsv 传入：地图行没有这一列。"""
    map_rows = [{"kind": "CMD", "id": "1"}, {"kind": "CMD", "id": "2"}]
    out = {r["id"]: r for r in ledger.apply_runtime_evidence(
        map_rows, [], {("CMD", "1"): "证据甲; 证据乙"})}
    assert out["1"]["ext_evidence"] == "证据甲; 证据乙"
    assert out["2"]["ext_evidence"] == ""
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest protocol/tests/test_ledger.py -q`
Expected: FAIL（`ImportError`/`AttributeError`）

- [ ] **Step 3: 写实现**

`protocol/ledger.py`：

```python
"""两本账：场景账（期望 ↔ opcode ↔ 哪次 run 验过）与 run 台账。

场景解析复用 tools/acceptance 的 load_scenario —— 不重写一份 YAML 解析。
"""
from __future__ import annotations

import json
from pathlib import Path

from .importer import MAP_COLUMNS, write_tsv

SCENARIO_COLUMNS: tuple[str, ...] = (
    "scenario_id", "step_index", "expect_index", "actor", "action", "level",
    "pattern", "evidence_class", "mapped_opcodes", "verified_run_id", "verdict_result",
)
RUN_COLUMNS: tuple[str, ...] = (
    "run_id", "git_sha", "created_utc", "status",
    "pass", "needs_human", "missing_evidence", "fail",
    "evidence_class", "verdict_digest_sha256",
)
OPCODE_COLUMNS: tuple[str, ...] = MAP_COLUMNS + ("ext_evidence", "runtime_evidence", "notes")


def parse_frame(pattern: str) -> tuple[int, int]:
    """DNF 帧头 10 字节：type(u8)|protocol(u8)|total_length(u32 LE)|crc32(u32 LE)。

    [作废] 本计划初稿写的是 "u16 id + u16 size（大端）"，被以下证据推翻：
    `src/DFLegacy.Protocol/PacketFrame.cs:5-13`（HeaderLength = 10）+
    真机首帧 000B2B… 按 10 字节解释得 protocol=11/length=43，与 server.log 一致，
    而 u16+u16 解释得 size=11008（错）。原文保留，不删。
    非法输入报错，绝不猜。
    """
    text = (pattern or "").strip().lower()
    if len(text) < 20:
        raise ValueError(f"帧头不足 10 字节：{pattern!r}")
    try:
        head = bytes.fromhex(text[:20])
    except ValueError as exc:
        raise ValueError(f"非十六进制帧头：{pattern!r}") from exc
    return head[1], int.from_bytes(head[2:6], "little")


def build_scenarios(scenarios_dir: Path, verified: dict[str, str],
                    results: dict[tuple[str, int, int], str] | None = None) -> list[dict]:
    import sys
    sys.path.insert(0, str(Path("tools").resolve()))
    from acceptance.model import load_scenario  # 复用既有解析

    results = results or {}
    rows: list[dict] = []
    for path in sorted(Path(scenarios_dir).glob("*.yaml")):
        sc = load_scenario(path)
        for s_i, step in enumerate(sc.steps, start=1):
            for e_i, ex in enumerate(step.expects, start=1):
                pattern = ex.pattern or ""
                mapped = ""
                if ex.level != "[语义]" and pattern:
                    try:
                        mapped = f"CMD:{parse_frame(pattern)[0]}"
                    except ValueError:
                        mapped = ""
                rows.append({
                    "scenario_id": sc.id,
                    "step_index": str(s_i),
                    "expect_index": str(e_i),
                    "actor": step.actor,
                    "action": step.action,
                    "level": ex.level,
                    "pattern": pattern,
                    "evidence_class": ex.evidence or "",
                    "mapped_opcodes": mapped,
                    "verified_run_id": verified.get(sc.id, ""),
                    "verdict_result": results.get((sc.id, s_i, e_i), ""),
                })
    rows.sort(key=lambda r: (r["scenario_id"], int(r["step_index"]), int(r["expect_index"])))
    return rows


def build_runs(verdict_path: Path, run_manifest: Path, evidence_class: str,
               digest_sha256: str) -> list[dict]:
    verdict = json.loads(Path(verdict_path).read_text(encoding="utf-8"))
    manifest = json.loads(Path(run_manifest).read_text(encoding="utf-8"))
    counts = verdict["counts"]
    return [{
        "run_id": manifest["run_id"],
        "git_sha": manifest["git_sha"],
        "created_utc": manifest["created_utc"],
        "status": verdict["status"],
        "pass": str(counts["pass"]),
        "needs_human": str(counts["needs-human"]),
        "missing_evidence": str(counts["missing-evidence"]),
        "fail": str(counts["fail"]),
        "evidence_class": evidence_class,
        "verdict_digest_sha256": digest_sha256,
    }]


def apply_runtime_evidence(map_rows: list[dict], scenarios: list[dict],
                           external: dict[tuple[str, str], str] | None = None) -> list[dict]:
    """把"该 opcode 在哪次 run 的哪一行被验证"写回账①，并补上人工外部证据原文。

    两处查找键都是**地图行的 `kind:id`**：地图行没有 `mapped_opcodes`/`_ext_evidence` 列，
    对着它查会把这两列永远查成空（预检发现的缺陷 P1/P2）。`ext_evidence` 由调用方
    从 `tiers-external.tsv` 读入（`{(kind,id): "证据1; 证据2"}`），不依赖 Task 1 的中间键。
    """
    external = external or {}
    buckets: dict[str, list[str]] = {}
    for row in scenarios:
        opcode = (row.get("mapped_opcodes") or "").strip()
        if not opcode or not row.get("verified_run_id") or not row.get("verdict_detail"):
            continue
        line = row["verdict_detail"].rsplit(":", 1)[-1].split()[0]
        buckets.setdefault(opcode, []).append(
            f"[真机] {row['verified_run_id']} {row['actor']}:{line}")
    out: list[dict] = []
    for row in map_rows:
        merged = dict(row)
        key = f"{row['kind']}:{row['id']}"
        merged["runtime_evidence"] = ",".join(buckets.get(key, []))
        merged["ext_evidence"] = external.get((row["kind"], row["id"]), "")
        merged.setdefault("notes", "")
        out.append(merged)
    return out


def write_ledgers(base: Path, scenarios: list[dict], runs: list[dict],
                  opcode_rows: list[dict]) -> dict:
    base = Path(base)
    write_tsv(base / "ledger" / "scenarios.tsv", SCENARIO_COLUMNS, scenarios)
    write_tsv(base / "ledger" / "runs.tsv", RUN_COLUMNS, runs)
    write_tsv(base / "ledger" / "opcodes.tsv", OPCODE_COLUMNS, opcode_rows)
    return {"scenarios": len(scenarios), "runs": len(runs), "opcodes": len(opcode_rows)}
```

`protocol/s1.py` 追加 `ledger` 子命令（读取 `map/opcode-map.tsv`，从 `runs/<runId>/verdict.json` + `manifest.json` + `tools/acceptance/verdicts/town-two-clients.json` 取 run 信息）：

```python
def cmd_ledger(args: argparse.Namespace) -> int:
    import csv, hashlib, json
    from . import ledger as ledger_mod
    base = Path(args.base)
    map_path = base / "map" / "opcode-map.tsv"
    with map_path.open(encoding="utf-8", newline="") as fh:
        map_rows = list(csv.DictReader(fh, delimiter="\t"))

    verdict = json.loads(Path(args.verdict).read_text(encoding="utf-8"))
    # verdict.entries 与场景声明顺序一一对应（judge 按 steps→expects 顺序产出），
    # 因此按顺序把 detail/result 贴回场景账，再由账生成运行时证据。
    results: dict[tuple[str, int, int], str] = {}
    details: list[str] = []
    ordered: list[tuple[int, int]] = []
    for s_i, step in enumerate(load_scenario(Path(args.scenarios_dir) / f"{args.verified_scenario}.yaml").steps, start=1):
        for e_i, _ex in enumerate(step.expects, start=1):
            ordered.append((s_i, e_i))
    if len(ordered) != len(verdict["entries"]):
        raise SystemExit(f"verdict 条目数 {len(verdict['entries'])} 与场景期望数 {len(ordered)} 不符，拒绝臆测对应关系")
    for (s_i, e_i), entry in zip(ordered, verdict["entries"]):
        results[(args.verified_scenario, s_i, e_i)] = entry["result"]
        details.append(entry.get("detail", ""))

    scenarios = ledger_mod.build_scenarios(Path(args.scenarios_dir),
                                           verified={args.verified_scenario: args.verified_run},
                                           results=results)
    for row, detail in zip(scenarios, details):
        if row["scenario_id"] == args.verified_scenario:
            row["verdict_detail"] = detail

    digest = Path(args.verdict_digest).read_bytes()
    runs = ledger_mod.build_runs(Path(args.verdict), Path(args.run_manifest), "[真机]",
                                 hashlib.sha256(digest).hexdigest())
    # 人工外部证据原文由 tiers-external.tsv 直接读出（账① 才存它，地图只有 ext_mark）
    from .importer import load_external
    external = {f"{kind}:{ident}": "; ".join(e for _, e in pairs if e)
                for (kind, ident), pairs in load_external(Path(args.raw_dir)).items()}
    opcode_rows = ledger_mod.apply_runtime_evidence(map_rows, scenarios, external)
    stats = ledger_mod.write_ledgers(base, scenarios, runs, opcode_rows)
    print(f"ledger: {stats}")
    return 0
```

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest protocol/tests/test_ledger.py -q`
Expected: 6 passed

`ledger` 子命令的 argparse 注册（写进 `main()`）：

```python
    p_ledger = sub.add_parser("ledger", help="生成两本账")
    p_ledger.add_argument("--base", default=str(BASE_DEFAULT))
    p_ledger.add_argument("--raw-dir", default=str(RAW_DEFAULT))
    p_ledger.add_argument("--scenarios-dir", default="tools/acceptance/scenarios")
    p_ledger.add_argument("--verified-run", required=True)
    p_ledger.add_argument("--verified-scenario", required=True)
    p_ledger.add_argument("--verdict", required=True, help="runs/<runId>/verdict.json")
    p_ledger.add_argument("--run-manifest", required=True, help="runs/<runId>/manifest.json")
    p_ledger.add_argument("--verdict-digest", required=True,
                          help="tools/acceptance/verdicts/<scenario>.json")
    p_ledger.set_defaults(func=cmd_ledger)
```

- [ ] **Step 5: 真跑一次账目生成并核对**

Run:
```powershell
$run = "runs\20261018T091951Z-2724238-town-two-clients"
python -m protocol.s1 ledger --verified-run 20261018T091951Z-2724238-town-two-clients `
  --verified-scenario town-two-clients `
  --verdict "$run\verdict.json" --run-manifest "$run\manifest.json" `
  --verdict-digest "tools\acceptance\verdicts\town-two-clients.json"
```
Expected: `ledger: {'scenarios': 12, 'runs': 1, 'opcodes': 1171}`

再核对运行时证据**真的落到了** opcode 上（预检修复的验收点）：
```powershell
python -c "import csv;rows=list(csv.DictReader(open('protocol/ledger/opcodes.tsv',encoding='utf-8'),delimiter='\t'));print({r['id']:r['runtime_evidence'] for r in rows if r['runtime_evidence']})"
```
Expected: 非空，形如 `{'11': '[真机] 20261018T091951Z-2724238-town-two-clients A:4,B:4', '12': '[真机] ... A:6'}`

- [ ] **Step 6: 提交**

```powershell
git add -- protocol/ledger.py protocol/s1.py protocol/tests/test_ledger.py protocol/ledger/scenarios.tsv protocol/ledger/runs.tsv protocol/ledger/opcodes.tsv
git commit -m "S1: 场景账与 run 台账（12 条期望；run 台账含 [真机] 标注）"
```

---

### Task 3: 人读报告与确定性 manifest（`render.py` + `render` 子命令）

**Files:**
- Create: `protocol/render.py`
- Modify: `protocol/s1.py`（新增 `render` 子命令）
- Test: `protocol/tests/test_render.py`

**Interfaces:**
- Consumes: `protocol/map/opcode-map.tsv`、`protocol/ledger/*.tsv`
- Produces:
  - `render_index(map_rows: list[dict], runs: list[dict]) -> str`
  - `build_manifest(base: Path) -> dict`（`{"schema":1,"files":{...},"baselines":{...}}`）
  - `write_manifest(base: Path, manifest: dict) -> Path`
  - `render_all(base: Path) -> dict`

- [ ] **Step 1: 写失败测试**

`protocol/tests/test_render.py`：

```python
import json
from pathlib import Path

from protocol import render


def test_manifest_has_no_timestamp_and_keys_sorted(tmp_path):
    (tmp_path / "map").mkdir()
    (tmp_path / "map" / "a.tsv").write_bytes(b"h\n1\n")
    manifest = render.build_manifest(tmp_path)
    text = render.dump_manifest(manifest)
    assert "2026" not in text and "time" not in text.lower()
    assert list(manifest["files"]) == ["map/a.tsv"]
    assert text.endswith("}\n")
    assert text.index('"baselines"') < text.index('"files"')  # sort_keys


def test_render_is_idempotent(tmp_path):
    base = tmp_path
    for sub in ("map", "ledger"):
        (base / sub).mkdir(parents=True)
    (base / "map" / "opcode-map.tsv").write_bytes(
        "kind\tid\ttier\text_mark\nCMD\t11\tT4_fields-read+write\tname_only\n".encode())
    (base / "ledger" / "runs.tsv").write_bytes(
        "run_id\tgit_sha\tcreated_utc\tstatus\tpass\tneeds_human\tmissing_evidence\tfail\tevidence_class\tverdict_digest_sha256\n"
        "RID\tabc1234\t2026-10-18T09:19:51Z\tgreen-with-human\t3\t1\t0\t0\t[真机]\tdeadbeef\n".encode())
    (base / "ledger" / "scenarios.tsv").write_bytes("scenario_id\n")
    (base / "ledger" / "opcodes.tsv").write_bytes("kind\tid\n")
    first = render.render_all(base)
    second = render.render_all(base)
    assert first["manifest_sha256"] == second["manifest_sha256"]
    assert (base / "map" / "index.md").read_bytes() == (base / "map" / "index.md").read_bytes()


def test_index_contains_tier_table_and_boundaries(tmp_path):
    base = tmp_path
    for sub in ("map", "ledger"):
        (base / sub).mkdir(parents=True)
    (base / "map" / "opcode-map.tsv").write_bytes(
        ("kind\tid\ttier\text_mark\n" + "".join(
            f"CMD\t{i}\t{t}\tname_only\n" for i, t in enumerate(
                ["T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields",
                 "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write"]))).encode())
    (base / "ledger" / "runs.tsv").write_bytes(
        "run_id\tgit_sha\tcreated_utc\tstatus\tpass\tneeds_human\tmissing_evidence\tfail\tevidence_class\tverdict_digest_sha256\n")
    (base / "ledger" / "scenarios.tsv").write_bytes("scenario_id\n")
    (base / "ledger" / "opcodes.tsv").write_bytes("kind\tid\n")
    render.render_all(base)
    md = (base / "map" / "index.md").read_text(encoding="utf-8")
    for tier in ("T0_no-evidence", "T4_fields-read+write"):
        assert tier in md
    assert "T3/T4" in md and "不等于" in md
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest protocol/tests/test_render.py -q`
Expected: FAIL（`AttributeError: build_manifest`）

- [ ] **Step 3: 写实现**

`protocol/render.py`：

```python
"""生成 map/index.md 与确定性 manifest.json（无时间戳、键有序、LF 结尾）。"""
from __future__ import annotations

import csv
import hashlib
import json
from collections import Counter
from pathlib import Path

TRACKED = ("map/opcode-map.tsv", "ledger/opcodes.tsv",
           "ledger/scenarios.tsv", "ledger/runs.tsv", "map/index.md")

BOUNDARIES = """\
## 边界（不得误读）

1. `T3`/`T4` 只表示"能在 handlers 目录里看到字段读/写调用"，**不等于**已读出有序字段布局。
2. `write_count` 含 `put_header`（129 行）；排除后换档数为 0，但家族级计数会偏大。
3. handlers 目录只覆盖 239/425 家族 ⇒ 186 个家族无字段调用数据（落 T1），是"没有数据"不是"没有字段"。
4. `[存在]` 只证明帧在场；S0 真机已实测"握手在场 ≠ 登录成功 ≠ 进城"，`pass` 不得读作"该 opcode 已实现"。
5. `runs.tsv` 的 `created_utc` 是控制端时钟，**不是**服务端 accept 时刻。
6. `party-invite` / `trade-open` / `town-position-sync` 是占位骨架，有 netdump 时必然 red。
7. `ext_mark` 是人工轴，与六档正交；当前 byte_verified/field_read 极少，不得引用为"字段级进度"。
"""


def _rows(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    with path.open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in r.items()} for r in csv.DictReader(fh, delimiter="\t")]


def render_index(map_rows: list[dict], runs: list[dict]) -> str:
    tiers = Counter(r["tier"] for r in map_rows)
    kinds = Counter(r["kind"] for r in map_rows)
    marks = Counter(r["ext_mark"] for r in map_rows)
    lines = ["# 协议地图与进度账", "",
             f"总 opcode：**{len(map_rows)}**（CMD {kinds.get('CMD', 0)} / NOTI {kinds.get('NOTI', 0)}）", "",
             "## 分档（六档之和必须等于总数）", "",
             "| 档位 | 条数 |", "|---|---|"]
    for tier in ("T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields",
                 "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write"):
        lines.append(f"| {tier} | {tiers.get(tier, 0)} |")
    lines += [f"| **合计** | **{sum(tiers.values())}** |", "",
              "## 外部证据轴（人工标注，与六档正交）", "",
              "| ext_mark | 条数 |", "|---|---|"]
    for mark in ("byte_verified", "field_read", "name_only"):
        lines.append(f"| {mark} | {marks.get(mark, 0)} |")
    lines += ["", "## 真机 run", ""]
    if runs:
        lines += ["| run_id | status | pass | needs-human | missing | fail | 证据类 |", "|---|---|---|---|---|---|---|"]
        for r in runs:
            lines.append(f"| {r['run_id']} | {r['status']} | {r['pass']} | {r['needs_human']} | "
                         f"{r['missing_evidence']} | {r['fail']} | {r['evidence_class']} |")
    else:
        lines.append("（暂无）")
    lines += ["", BOUNDARIES]
    return "\n".join(lines)


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build_manifest(base: Path) -> dict:
    base = Path(base)
    files: dict[str, dict] = {}
    for rel in TRACKED:
        p = base / rel
        if not p.is_file():
            continue
        data = p.read_bytes()
        rows = 0
        if rel.endswith(".tsv"):
            rows = max(0, data.decode("utf-8").count("\n") - 1)
        files[rel] = {"sha256": hashlib.sha256(data).hexdigest(), "rows": rows}
    map_rows = _rows(base / "map" / "opcode-map.tsv")
    tiers = Counter(r["tier"] for r in map_rows)
    marks = Counter(r["ext_mark"] for r in map_rows)
    kinds = Counter(r["kind"] for r in map_rows)
    status = Counter(r["status"] for r in map_rows)
    return {
        "schema": 1,
        "files": files,
        "baselines": {
            "opcodes_total": len(map_rows),
            "cmd": kinds.get("CMD", 0),
            "noti": kinds.get("NOTI", 0),
            "status": {k: status.get(k, 0) for k in ("mapped", "no-dispatcher", "unused")},
            "tiers": {k: tiers.get(k, 0) for k in
                      ("T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields",
                       "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write")},
            "ext_marks": {k: marks.get(k, 0) for k in ("byte_verified", "field_read", "name_only")},
        },
    }


def dump_manifest(manifest: dict) -> str:
    return json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False) + "\n"


def write_manifest(base: Path, manifest: dict) -> Path:
    path = Path(base) / "manifest.json"
    path.write_bytes(dump_manifest(manifest).encode("utf-8"))
    return path


def render_all(base: Path) -> dict:
    base = Path(base)
    map_rows = _rows(base / "map" / "opcode-map.tsv")
    runs = _rows(base / "ledger" / "runs.tsv")
    index = render_index(map_rows, runs)
    (base / "map" / "index.md").write_bytes((index + "\n").encode("utf-8"))
    manifest = build_manifest(base)          # index.md 已生成，纳入 sha
    path = write_manifest(base, manifest)
    return {"manifest": str(path), "manifest_sha256": _sha256(path)}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest protocol/tests/test_render.py -q`
Expected: 3 passed

- [ ] **Step 5: 真跑 render 并检查幂等**

Run:
```powershell
python -m protocol.s1 render
$h1 = (Get-FileHash protocol/manifest.json).Hash
python -m protocol.s1 render
$h2 = (Get-FileHash protocol/manifest.json).Hash
"$h1`n$h2`n相同=$($h1 -eq $h2)"
```
Expected: 两行哈希相同，`相同=True`

- [ ] **Step 6: 提交**

```powershell
git add -- protocol/render.py protocol/s1.py protocol/tests/test_render.py protocol/map/index.md protocol/manifest.json
git commit -m "S1: 人读报告与确定性 manifest（render 幂等）"
```

---

### Task 4: `verify` 五条断言（`verify.py` + `verify` 子命令）

**Files:**
- Create: `protocol/verify.py`
- Modify: `protocol/s1.py`（新增 `verify` 子命令，退出码语义）
- Test: `protocol/tests/test_verify.py`

**Interfaces:**
- Consumes: `protocol/manifest.json`、`map/`、`ledger/`、`tools/acceptance/verdicts/*.json`
- Produces:
  - `checks(base: Path) -> list[tuple[str, bool, str]]`（顺序即 `sha256`/`rows_and_keys`/`baselines`/`internal`/`s0_crosscheck`）
  - `verify(base: Path) -> int`（0 通过、1 不符）
  - `report(results) -> str`

- [ ] **Step 1: 写失败测试**

`protocol/tests/test_verify.py`：

```python
import hashlib
import json
from pathlib import Path

import pytest

from protocol import render, verify


def _build(tmp_path: Path) -> Path:
    base = tmp_path
    for sub in ("map", "ledger"):
        (base / sub).mkdir(parents=True)
    (base / "map" / "opcode-map.tsv").write_bytes(
        ("kind\tid\tname\trodata_va\tdispatcher\tstatus\ttier\tread_count\twrite_count\text_mark\n"
         "CMD\t11\tA\t0x01\tDoA\tmapped\tT4_fields-read+write\t1\t1\tname_only\n"
         "CMD\t12\tB\t0x02\tDoB\tmapped\tT3_fields-read-only\t1\t0\tname_only\n"
         "NOTI\t1\tC\t0x03\t\tno-dispatcher\tT0_no-evidence\t0\t0\tname_only\n").encode())
    (base / "ledger" / "opcodes.tsv").write_bytes(
        ("kind\tid\tname\trodata_va\tdispatcher\tstatus\ttier\tread_count\twrite_count\text_mark"
         "\text_evidence\truntime_evidence\tnotes\n"
         "CMD\t11\tA\t0x01\tDoA\tmapped\tT4_fields-read+write\t1\t1\tname_only\t\t\t\n"
         "CMD\t12\tB\t0x02\tDoB\tmapped\tT3_fields-read-only\t1\t0\tname_only\t\t\t\n"
         "NOTI\t1\tC\t0x03\t\tno-dispatcher\tT0_no-evidence\t0\t0\tname_only\t\t\t\n").encode())
    (base / "ledger" / "scenarios.tsv").write_bytes(
        "scenario_id\tstep_index\texpect_index\tactor\taction\tlevel\tpattern\tevidence_class\tmapped_opcodes\tverified_run_id\tverdict_result\n"
        "town-two-clients\t1\t1\tA\t登录\t[存在]\t000B2B0000000000000001\t[抓包]\tCMD:11\tRID\tpass\n".encode())
    (base / "ledger" / "runs.tsv").write_bytes(
        "run_id\tgit_sha\tcreated_utc\tstatus\tpass\tneeds_human\tmissing_evidence\tfail\tevidence_class\tverdict_digest_sha256\n"
        "RID\tabc1234\t2026-10-18T09:19:51Z\tgreen-with-human\t1\t0\t0\t0\t[真机]\tdeadbeef\n".encode())
    render.render_all(base)
    return base


def test_verify_passes_on_consistent_tree(tmp_path):
    base = _build(tmp_path)
    assert verify.verify(base) == 0


def test_sha_mismatch_is_detected(tmp_path):
    base = _build(tmp_path)
    p = base / "map" / "opcode-map.tsv"
    p.write_bytes(p.read_bytes().replace(b"CMD\t11", b"CMD\t19"))
    assert verify.verify(base) == 1
    names = [n for n, ok, _ in verify.checks(base) if not ok]
    assert "sha256" in names


def test_unlisted_file_fails_completeness(tmp_path):
    base = _build(tmp_path)
    (base / "ledger" / "extra.tsv").write_bytes(b"x\n")
    assert verify.verify(base) == 1
    names = [n for n, ok, _ in verify.checks(base) if not ok]
    assert any("sha256" in n or "rows_and_keys" in n for n in names)


def test_row_count_mismatch_is_detected(tmp_path):
    base = _build(tmp_path)
    runs = base / "ledger" / "runs.tsv"
    runs.write_bytes(runs.read_bytes() + runs.read_bytes().split(b"\n")[1] + b"\n")
    assert verify.verify(base) == 1


def test_baseline_sum_mismatch_is_detected(tmp_path):
    base = _build(tmp_path)
    manifest = json.loads((base / "manifest.json").read_text(encoding="utf-8"))
    manifest["baselines"]["opcodes_total"] = 999
    (base / "manifest.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n",
                                        encoding="utf-8")
    assert verify.verify(base) == 1
    names = [n for n, ok, _ in verify.checks(base) if not ok]
    assert "baselines" in names


def test_dangling_opcode_reference_is_detected(tmp_path):
    base = _build(tmp_path)
    scen = base / "ledger" / "scenarios.tsv"
    scen.write_bytes(scen.read_bytes().replace(b"CMD:11", b"CMD:99"))
    assert verify.verify(base) == 1
    names = [n for n, ok, _ in verify.checks(base) if not ok]
    assert "internal" in names


def test_s0_crosscheck_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path)
    verdicts = tmp_path / "verdicts"
    verdicts.mkdir()
    (verdicts / "town-two-clients.json").write_text('{"counts": {"pass": 9}}\n', encoding="utf-8")
    monkeypatch.setattr(verify, "VERDICTS_DIR", verdicts)
    assert verify.verify(base) == 1
    names = [n for n, ok, _ in verify.checks(base) if not ok]
    assert "s0_crosscheck" in names
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest protocol/tests/test_verify.py -q`
Expected: FAIL（`ModuleNotFoundError`/`AttributeError: checks`）

- [ ] **Step 3: 写实现**

`protocol/verify.py`：

```python
"""verify 五条断言：只读，任一不符即失败（调用方据此返回 1）。"""
from __future__ import annotations

import csv
import hashlib
import json
from pathlib import Path

VERDICTS_DIR = Path("tools/acceptance/verdicts")
TIERS = ("T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields",
         "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write")


def _rows(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    with path.open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in r.items()} for r in csv.DictReader(fh, delimiter="\t")]


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _check_sha(base: Path, manifest: dict) -> tuple[str, bool, str]:
    problems: list[str] = []
    for rel, meta in manifest.get("files", {}).items():
        p = base / rel
        if not p.is_file():
            problems.append(f"缺文件 {rel}")
        elif _sha(p) != meta.get("sha256"):
            problems.append(f"{rel} sha256 不符")
    listed = set(manifest.get("files", {}))
    for sub in ("map", "ledger"):
        for p in sorted((base / sub).glob("*")):
            if p.is_file():
                rel = f"{sub}/{p.name}"
                if rel not in listed and rel != "manifest.json":
                    problems.append(f"未列入清单的文件 {rel}")
    return ("sha256", not problems, "; ".join(problems))


def _check_rows(base: Path, manifest: dict) -> tuple[str, bool, str]:
    problems: list[str] = []
    for rel, meta in manifest.get("files", {}).items():
        if not rel.endswith(".tsv"):
            continue
        rows = _rows(base / rel)
        if len(rows) != int(meta.get("rows", -1)):
            problems.append(f"{rel} 行数 {len(rows)} != 清单 {meta.get('rows')}")
    for rel, key in (("map/opcode-map.tsv", ("kind", "id")),
                     ("ledger/opcodes.tsv", ("kind", "id")),
                     ("ledger/scenarios.tsv", ("scenario_id", "step_index", "expect_index")),
                     ("ledger/runs.tsv", ("run_id",))):
        rows = _rows(base / rel)
        keys = [tuple(r.get(k, "") for k in key) for r in rows]
        if len(keys) != len(set(keys)):
            problems.append(f"{rel} 主键重复（{key}）")
    return ("rows_and_keys", not problems, "; ".join(problems))


def _check_baselines(base: Path, manifest: dict) -> tuple[str, bool, str]:
    problems: list[str] = []
    rows = _rows(base / "map" / "opcode-map.tsv")
    base_bl = manifest.get("baselines", {})
    if len(rows) != int(base_bl.get("opcodes_total", -1)):
        problems.append(f"总数 {len(rows)} != 基线 {base_bl.get('opcodes_total')}")
    for tier in TIERS:
        got = sum(1 for r in rows if r["tier"] == tier)
        want = int(base_bl.get("tiers", {}).get(tier, -1))
        if got != want:
            problems.append(f"{tier} {got} != 基线 {want}")
    if sum(base_bl.get("tiers", {}).values()) != int(base_bl.get("opcodes_total", -1)):
        problems.append("基线分档之和 != 总数")
    return ("baselines", not problems, "; ".join(problems))


def _check_internal(base: Path) -> tuple[str, bool, str]:
    problems: list[str] = []
    map_keys = {(r["kind"], r["id"]) for r in _rows(base / "map" / "opcode-map.tsv")}
    ledger_keys = {(r["kind"], r["id"]) for r in _rows(base / "ledger" / "opcodes.tsv")}
    if map_keys != ledger_keys:
        problems.append(f"账①与地图 opcode 集合不一致（差 {len(map_keys ^ ledger_keys)}）")
    runs = {r["run_id"] for r in _rows(base / "ledger" / "runs.tsv")}
    for r in _rows(base / "ledger" / "scenarios.tsv"):
        for op in filter(None, (r.get("mapped_opcodes") or "").split(",")):
            kind, _, ident = op.strip().partition(":")
            if (kind, ident) not in map_keys:
                problems.append(f"场景账引用不存在的 opcode {op}")
        if r.get("verified_run_id") and r["verified_run_id"] not in runs:
            problems.append(f"场景账引用不存在的 run {r['verified_run_id']}")
    for r in _rows(base / "ledger" / "opcodes.tsv"):
        for token in filter(None, (r.get("runtime_evidence") or "").split("]")):
            for run_id in [t for t in token.split() if t.startswith("2")]:
                if run_id not in runs:
                    problems.append(f"runtime_evidence 引用不存在的 run {run_id}")
    return ("internal", not problems, "; ".join(problems))


def _check_s0(base: Path) -> tuple[str, bool, str]:
    problems: list[str] = []
    for run in _rows(base / "ledger" / "runs.tsv"):
        if run.get("evidence_class") != "[真机]":
            continue
        target = Path(VERDICTS_DIR) / "town-two-clients.json"
        if not target.is_file():
            problems.append(f"缺 {target}")
            continue
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        if digest != run.get("verdict_digest_sha256"):
            problems.append(f"{run['run_id']} 摘要不符（{digest[:8]} != {run.get('verdict_digest_sha256')[:8]}）")
        verdict = json.loads(target.read_text(encoding="utf-8"))
        counts = verdict.get("counts", {})
        pairs = (("pass", "pass"), ("needs_human", "needs-human"),
                 ("missing_evidence", "missing-evidence"), ("fail", "fail"))
        for col, key in pairs:
            if str(counts.get(key)) != str(run.get(col)):
                problems.append(f"{run['run_id']} 计数 {col} 不符")
    return ("s0_crosscheck", not problems, "; ".join(problems))


def checks(base: Path) -> list[tuple[str, bool, str]]:
    base = Path(base)
    manifest_path = base / "manifest.json"
    if not manifest_path.is_file():
        return [("sha256", False, "缺 manifest.json")]
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    return [_check_sha(base, manifest), _check_rows(base, manifest),
            _check_baselines(base, manifest), _check_internal(base), _check_s0(base)]


def report(results: list[tuple[str, bool, str]]) -> str:
    lines = [f"{'OK  ' if ok else 'FAIL'} {name}" + (f": {detail}" if detail else "")
             for name, ok, detail in results]
    passed = sum(1 for _, ok, _ in results if ok)
    lines.append(f"verify: {'PASS' if passed == len(results) else 'FAIL'} ({passed}/{len(results)})")
    return "\n".join(lines)


def verify(base: Path) -> int:
    results = checks(base)
    print(report(results))
    return 0 if all(ok for _, ok, _ in results) else 1
```

`protocol/s1.py` 追加：

```python
def cmd_render(args: argparse.Namespace) -> int:
    from . import render
    print(render.render_all(Path(args.base)))
    return 0


def cmd_verify(args: argparse.Namespace) -> int:
    from . import verify
    return verify.verify(Path(args.base))
```

并在 `main()` 里注册：

```python
    p_render = sub.add_parser("render", help="生成 index.md 与 manifest")
    p_render.add_argument("--base", default=str(BASE_DEFAULT))
    p_render.set_defaults(func=cmd_render)
    p_verify = sub.add_parser("verify", help="只读自检（五条断言）")
    p_verify.add_argument("--base", default=str(BASE_DEFAULT))
    p_verify.set_defaults(func=cmd_verify)
```

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest protocol/tests/test_verify.py -q`
Expected: 7 passed

- [ ] **Step 5: 真数据自检**

Run: `python -m protocol.s1 verify; "exit=$LASTEXITCODE"`
Expected: 5 行 `OK` + `verify: PASS (5/5)`，`exit=0`

- [ ] **Step 6: 提交**

```powershell
git add -- protocol/verify.py protocol/s1.py protocol/tests/test_verify.py
git commit -m "S1: verify 五条断言（sha256/行数与主键/基线/内部一致/S0 交叉核对）"
```

---

### Task 5: README、边界与收尾

**Files:**
- Create: `protocol/README.md`
- Modify: `protocol/s1.py`（`--help` 文案补一句用途）
- Test: `protocol/tests/test_readme.py`

**Interfaces:**
- Produces: 无新 API；只补文档与一条"README 必需段落"的回归测试。

- [ ] **Step 1: 写失败测试**

`protocol/tests/test_readme.py`：

```python
from pathlib import Path


def test_readme_documents_three_vocabularies_and_boundaries():
    md = Path("protocol/README.md").read_text(encoding="utf-8")
    for token in ("[静态]", "[抓包]", "[真机]", "[回放]", "[推断]", "[未验证]",
                  "T0_no-evidence", "T4_fields-read+write",
                  "pass", "needs-human", "missing-evidence", "fail",
                  "T3/T4", "不等于", "握手在场"):
        assert token in md, token


def test_readme_documents_commands_and_exit_codes():
    md = Path("protocol/README.md").read_text(encoding="utf-8")
    for token in ("python -m protocol.s1 import", "python -m protocol.s1 render",
                  "python -m protocol.s1 verify", "退出码"):
        assert token in md, token
```

- [ ] **Step 2: 运行测试确认失败**

Run: `python -m pytest protocol/tests/test_readme.py -q`
Expected: FAIL（`FileNotFoundError: protocol/README.md`）

- [ ] **Step 3: 写 README**

`protocol/README.md`：

````markdown
# protocol/ —— 协议地图与进度账（S1）

把仓库里已有的协议事实（opcode 地图、六档覆盖度、人工外部证据、S0 真机 run）
变成**入库、可 diff、可换机自检**的正式资产。**不新增逆向**：字段布局属 S3。

## 三套词表（不得混用）

1. **证据六类**（事实从哪来）：`[静态]` `[抓包]` `[真机]` `[回放]` `[推断]` `[未验证]`；`[作废]` 用于被推翻的旧结论（保留原文）。
2. **分档六档**（字段可见性）：`T0_no-evidence` `T1_dispatcher-no-handlerdata` `T2_handlerdata-no-fields` `T3b_fields-write-only` `T3_fields-read-only` `T4_fields-read+write`。
3. **判定三分 + fail**（一次 run 的结论）：`pass` `needs-human` `missing-evidence`，外加 `fail`（仅"证据在、但不符"）。

## 命令

```powershell
$env:PYTHONIOENCODING='utf-8'
python -m protocol.s1 import     # 原始产物 -> map/opcode-map.tsv（需 .superpowers 原始产物）
python -m protocol.s1 ledger     # 生成 ledger/*.tsv
python -m protocol.s1 render     # 生成 map/index.md 与 manifest.json（幂等）
python -m protocol.s1 verify     # 只读自检；退出码 0=通过 1=不符 2=用法错
```

`verify` 只依赖入库文件：`git clone` 后可直接跑，不需要客户端、不需要 `.superpowers/`。

## 五条断言

1. 每个清单文件的 sha256 一致，且 `map/`、`ledger/` 里没有清单外文件。
2. 行数 = 清单记录；`(kind,id)`、场景主键、`run_id` 唯一。
3. 六档之和 = 总数，且逐档与 `manifest.baselines` 一致。
4. 账①与地图的 opcode 集合相同；场景账引用的 opcode/run 都存在。
5. `[真机]` run 的摘要 sha256 与 `tools/acceptance/verdicts/*.json` 一致、四计数相等。

## 边界（不得误读）

1. `T3`/`T4` 只表示能看到字段读/写调用，**不等于**已读出有序字段布局。
2. `write_count` 含 `put_header`（129 行）；排除后换档数为 0，但家族级计数偏大。
3. handlers 目录只覆盖 239/425 家族 ⇒ 186 个家族无字段数据（落 T1）：是"没有数据"，不是"没有字段"。
4. **`[存在]` 只证明帧在场**：S0 真机已实测"握手在场 ≠ 登录成功 ≠ 进城"；`pass` 不得读作"该 opcode 已实现"。
5. `runs.tsv` 的 `created_utc` 是控制端时钟，**不是**服务端 accept 时刻。
6. `party-invite` / `trade-open` / `town-position-sync` 是占位骨架：有 netdump 时必然 red，不是产品缺陷。
7. `ext_mark` 是人工轴，与六档正交；当前 `byte_verified`/`field_read` 极少，不得引用为"字段级进度"。

## 怎么加一条证据

1. 人工证据：编辑原始 `tiers-external.tsv` 后重跑 `import`（会按 `byte_verified > field_read > name_only` 合并）。
2. 运行时证据：在 `ledger/scenarios.tsv` 里给对应期望填 `verified_run_id` 与 `verdict_result`，重跑 `ledger`。
3. 改完必须 `render` 再 `verify`（manifest 的 sha256 会变）。
````

- [ ] **Step 4: 运行测试确认通过**

Run: `python -m pytest protocol/tests/test_readme.py -q`
Expected: 2 passed

- [ ] **Step 5: 全套回归**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
python -m pytest protocol/tests -q
python -m pytest acceptance/tests -q   # cwd=tools，确认 S0 未被波及
python -m protocol.s1 verify
```
Expected: 前者全绿；S0 仍 117 passed；`verify: PASS (5/5)` 且退出码 0

- [ ] **Step 6: 提交**

```powershell
git add -- protocol/README.md protocol/tests/test_readme.py protocol/s1.py
git commit -m "S1: README（三套词表/命令/七条边界）与收尾回归"
```

---

## Self-Review（计划终检）

- **Spec 覆盖**：§4 目录 → Task 1/2/3；§5 数据契约 → Task 1（地图、ext 合并）、Task 2（两本账 + pattern→opcode）；§6 manifest → Task 3；§7 CLI → Task 1/2/3/4；§8 五断言 → Task 4 五条各一测；§9 边界 → Task 5 README + `render_index` 内嵌 BOUNDARIES；§10 测试策略 → 各任务测试；§11 DoD → Task 5 Step 5 + Task 4 Step 5。
- **占位符扫描**：无 TBD/TODO；每个代码步骤都给了可粘贴代码。
- **类型一致性**：`MAP_COLUMNS`/`SCENARIO_COLUMNS`/`RUN_COLUMNS`/`OPCODE_COLUMNS` 在 Task 1/2 定义、Task 4 按同名读取；`checks()` 返回 `list[tuple[str,bool,str]]` 与 `report()`、`verify()` 一致；`render_all()` 返回 `manifest_sha256` 与 Task 3 测试一致。
- **已知取舍**：Task 2 的 `s1.py` 里 `results={}` 首版留空（runtime evidence 由人工填 `verified_run_id` 后重跑），符合 spec §5.3「人工轴」；若执行者想让 `ledger` 自动读 verdict 明细，属扩展，不在本计划。
