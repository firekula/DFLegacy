"""verify 五条断言：**只读**、只依赖入库文件；任一不符即失败（CLI 退出码 1）。

五条断言（spec §8）：
1. ``sha256``：清单必须恰含 6 份受跟踪产物且每个文件的 sha256 与磁盘一致；``map/``、``ledger/``
   下（**递归**）不得有未列入清单的文件。
2. ``rows_and_keys``：每份 TSV 的数据行数 = 清单 rows；列数与表头一致；主键唯一。
3. ``baselines``：总数 / kind / status / 六档 / ext_mark 与 ``manifest.baselines`` 一致，六档之和 = 总数。
4. ``internal``：账①与地图的 opcode 集合相等；``scenarios`` 的 opcode 与 run 引用都存在。
5. ``s0_crosscheck``（**N1 修订版**）：``runs.tsv`` 收全部真机 run，而
   ``tools/acceptance/verdicts/<scenario_id>.json`` 是"该场景当前结论"（每次 run 覆盖提升）。
   因此对**每个场景**只把 ``created_utc`` 最晚的 ``[真机]`` 行与它做 sha256 + 四计数核对；
   更早的 ``[真机]`` 行是历史记录，只校验摘要是 64 位十六进制，不要求等于当前 tracked 文件。
"""
from __future__ import annotations

import csv
import hashlib
import json
import re
from pathlib import Path

from .importer import MAP_COLUMNS
from .ledger import OPCODE_COLUMNS, RUN_COLUMNS, SCENARIO_COLUMNS

_REPO_ROOT = Path(__file__).resolve().parents[1]
VERDICTS_DIR = _REPO_ROOT / "tools" / "acceptance" / "verdicts"
"""tracked 判定目录（绝对路径：随 ``--base`` 指向任何副本都能找到本仓库的 tracked 文件）。"""

TIERS = ("T0_no-evidence", "T1_dispatcher-no-handlerdata", "T2_handlerdata-no-fields",
         "T3b_fields-write-only", "T3_fields-read-only", "T4_fields-read+write")
STATUSES = ("mapped", "no-dispatcher", "unused")
MARKS = ("byte_verified", "field_read", "name_only")
KIND_BASELINES = (("cmd", "CMD"), ("noti", "NOTI"))
COUNT_PAIRS = (("pass", "pass"), ("needs_human", "needs-human"),
               ("missing_evidence", "missing-evidence"), ("fail", "fail"))
BASELINE_OPCODES_TOTAL = 1171
"""spec §8.3 写死的总数基线（六档之和 = 1171）：防"数据与清单一起缩水"绕过基线检查。"""

TRACKED_FILES = (
    "map/opcode-map.tsv", "map/s2s-map.tsv", "ledger/opcodes.tsv",
    "ledger/scenarios.tsv", "ledger/runs.tsv", "map/index.md",
)
"""spec §6 的「6 文件契约」。

这里**不再 import** ``render.TRACKED``：verify 是校验方，清单常量若与生成方共用同一符号，
"两边一起改"就会同时成立（自证）。两边内容一致由测试钉住（``test_verify.py`` 的 6 文件契约用例）。
"""

DIGEST_RE = re.compile(r"[0-9a-f]{64}")
"""摘要必须是 64 位小写十六进制。"""

RUN_IN_EVIDENCE_RE = re.compile(r"\[(?:真机|回放)\]\s+(\S+)")
"""``[真机] <runId> A:4,B:4`` 里的 runId。"""

KEY_COLUMNS = {
    "map/opcode-map.tsv": ("kind", "id"),
    "map/s2s-map.tsv": ("kind", "id"),
    "ledger/opcodes.tsv": ("kind", "id"),
    "ledger/scenarios.tsv": ("scenario_id", "step_index", "expect_index"),
    "ledger/runs.tsv": ("run_id",),
}
EXPECTED_COLUMNS = {
    "map/opcode-map.tsv": MAP_COLUMNS,
    "map/s2s-map.tsv": MAP_COLUMNS,
    "ledger/opcodes.tsv": OPCODE_COLUMNS,
    "ledger/scenarios.tsv": SCENARIO_COLUMNS,
    "ledger/runs.tsv": RUN_COLUMNS,
}


def _read_tsv(path: Path) -> tuple[tuple[str, ...], list[dict], list[int]]:
    """返回 (表头, 数据行, 每行字段数)；缺文件返回空。"""
    path = Path(path)
    if not path.is_file():
        return (), [], []
    with path.open(encoding="utf-8", newline="") as fh:
        reader = csv.reader(fh, delimiter="\t")
        header = tuple(next(reader, ()))
        rows: list[dict] = []
        widths: list[int] = []
        for fields in reader:
            widths.append(len(fields))
            rows.append({name: (fields[i] if i < len(fields) else "")
                         for i, name in enumerate(header)})
    return header, rows, widths


def _rows(path: Path) -> list[dict]:
    return _read_tsv(path)[1]


# ---------------------------------------------------------------- 第 1 条

def _check_sha(base: Path, manifest: dict) -> tuple[str, bool, str]:
    problems: list[str] = []
    files = manifest.get("files", {}) or {}
    # spec §6「6 文件契约」：清单必须**恰好**是这 6 份受跟踪产物。
    # 少了 ⇒ "删掉整本台账 + 从清单移除"就能过；多了 ⇒ 清单被塞进不受管理的文件。
    missing = sorted(set(TRACKED_FILES) - set(files))
    extra = sorted(set(files) - set(TRACKED_FILES))
    if missing:
        problems.append(f"清单缺受跟踪产物：{', '.join(missing)}")
    if extra:
        problems.append(f"清单含非受跟踪产物：{', '.join(extra)}")
    for rel, meta in sorted(files.items()):
        path = Path(base) / rel
        if not path.is_file():
            problems.append(f"缺文件 {rel}")
            continue
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != meta.get("sha256"):
            problems.append(f"{rel} sha256 {digest[:8]}… != 清单 {str(meta.get('sha256'))[:8]}…")
    # 未列文件扫描必须**递归**：嵌套文件同样是"绕过清单加文件"（终审 I2）。
    for sub in ("map", "ledger"):
        for path in sorted((Path(base) / sub).rglob("*")):
            if path.is_file():
                rel = path.relative_to(base).as_posix()
                if rel not in files:
                    problems.append(f"未列入清单的文件 {rel}")
    return ("sha256", not problems, "; ".join(problems))


# ---------------------------------------------------------------- 第 2 条

def _check_rows(base: Path, manifest: dict) -> tuple[str, bool, str]:
    problems: list[str] = []
    for rel, meta in sorted((manifest.get("files", {}) or {}).items()):
        if not rel.endswith(".tsv"):
            continue
        _, rows, _ = _read_tsv(Path(base) / rel)
        try:
            want = int(meta.get("rows"))
        except (TypeError, ValueError):
            problems.append(f"{rel} 清单 rows 非法：{meta.get('rows')!r}")
            continue
        if len(rows) != want:
            problems.append(f"{rel} 行数 {len(rows)} != 清单 {want}")
    for rel, expected in EXPECTED_COLUMNS.items():
        path = Path(base) / rel
        if not path.is_file():
            continue                                  # 缺文件由第 1 条报
        header, rows, widths = _read_tsv(path)
        if header != expected:
            problems.append(f"{rel} 表头与契约不符：{header}")
        bad = [i for i, width in enumerate(widths, start=2) if width != len(header)]
        if bad:
            problems.append(f"{rel} 有 {len(bad)} 行字段数 != {len(header)}（首个在第 {bad[0]} 行）")
        keys = [tuple(row.get(col, "") for col in KEY_COLUMNS[rel]) for row in rows]
        if len(keys) != len(set(keys)):
            problems.append(f"{rel} 主键重复（{KEY_COLUMNS[rel]}）")
    return ("rows_and_keys", not problems, "; ".join(problems))


# ---------------------------------------------------------------- 第 3 条

def _check_baselines(base: Path, manifest: dict) -> tuple[str, bool, str]:
    problems: list[str] = []
    rows = _rows(Path(base) / "map" / "opcode-map.tsv")
    baselines = manifest.get("baselines", {}) or {}

    def compare(label: str, got: int, want) -> None:
        try:
            want_int = int(want)
        except (TypeError, ValueError):
            problems.append(f"{label} 基线缺失或非整数：{want!r}")
            return
        if got != want_int:
            problems.append(f"{label} {got} != 基线 {want_int}")

    compare("opcodes_total", len(rows), baselines.get("opcodes_total"))
    compare("opcodes_total（硬编码基线）", len(rows), BASELINE_OPCODES_TOTAL)
    for key, kind in KIND_BASELINES:
        compare(key, sum(1 for r in rows if r.get("kind") == kind), baselines.get(key))
    # S2S 是独立基线：读 s2s-map.tsv 单独计数，绝不混进上面 opcode-map 的六档/status/mark 循环。
    compare("s2s", len(_rows(Path(base) / "map" / "s2s-map.tsv")), baselines.get("s2s"))
    for tier in TIERS:
        compare(tier, sum(1 for r in rows if r.get("tier") == tier),
                (baselines.get("tiers") or {}).get(tier))
    for state in STATUSES:
        compare(f"status.{state}", sum(1 for r in rows if r.get("status") == state),
                (baselines.get("status") or {}).get(state))
    for mark in MARKS:
        compare(f"ext_mark.{mark}", sum(1 for r in rows if r.get("ext_mark") == mark),
                (baselines.get("ext_marks") or {}).get(mark))

    tier_sum = sum(1 for r in rows if r.get("tier") in TIERS)
    if tier_sum != len(rows):
        problems.append(f"六档之和 {tier_sum} != 实际总数 {len(rows)}")
    if tier_sum != BASELINE_OPCODES_TOTAL:
        problems.append(f"六档之和 {tier_sum} != 硬编码基线 {BASELINE_OPCODES_TOTAL}")
    return ("baselines", not problems, "; ".join(problems))


# ---------------------------------------------------------------- 第 4 条

def _check_internal(base: Path) -> tuple[str, bool, str]:
    problems: list[str] = []
    map_keys = {(r.get("kind", ""), r.get("id", ""))
                for r in _rows(Path(base) / "map" / "opcode-map.tsv")}
    ledger_keys = {(r.get("kind", ""), r.get("id", ""))
                   for r in _rows(Path(base) / "ledger" / "opcodes.tsv")}
    if map_keys != ledger_keys:
        problems.append(f"账①与地图的 opcode 集合不一致（对称差 {len(map_keys ^ ledger_keys)} 个）")

    runs = {r.get("run_id", "") for r in _rows(Path(base) / "ledger" / "runs.tsv")}
    for row in _rows(Path(base) / "ledger" / "scenarios.tsv"):
        for token in filter(None, (row.get("mapped_opcodes") or "").split(",")):
            kind, _, ident = token.strip().partition(":")
            if (kind, ident) not in map_keys:
                problems.append(f"场景账引用不存在的 opcode {token.strip()}")
        run_id = row.get("verified_run_id") or ""
        if run_id and run_id not in runs:
            problems.append(f"场景账引用不存在的 run {run_id}")
    for row in _rows(Path(base) / "ledger" / "opcodes.tsv"):
        for run_id in RUN_IN_EVIDENCE_RE.findall(row.get("runtime_evidence") or ""):
            if run_id not in runs:
                problems.append(f"runtime_evidence 引用不存在的 run {run_id}")
    return ("internal", not problems, "; ".join(problems))


# ---------------------------------------------------------------- 第 5 条

def scenario_of_run(run_id: str) -> str:
    """runId 的格式见 ``tools/acceptance/model.new_run_id``：``<UTC>-<git短哈希>-<scenario_id>``。"""
    parts = (run_id or "").split("-", 2)
    return parts[2] if len(parts) == 3 else ""


def _check_s0(base: Path) -> tuple[str, bool, str]:
    problems: list[str] = []
    declared = {(r.get("scenario_id") or "").strip()
                for r in _rows(Path(base) / "ledger" / "scenarios.tsv")}
    latest: dict[str, dict] = {}
    for run in _rows(Path(base) / "ledger" / "runs.tsv"):
        if (run.get("evidence_class") or "") != "[真机]":     # 精确匹配，不做包含
            continue
        digest = (run.get("verdict_digest_sha256") or "").strip()
        if not DIGEST_RE.fullmatch(digest):
            problems.append(f"{run.get('run_id')}: verdict_digest_sha256 {digest!r} "
                            "不是 64 位十六进制")
        scenario = scenario_of_run(run.get("run_id", ""))
        if not scenario:
            problems.append(f"{run.get('run_id')}: 无法从 runId 解析场景"
                            "（期望 <UTC>-<git短哈希>-<scenario_id>）")
            continue
        if scenario not in declared:
            problems.append(f"{run.get('run_id')}: 场景 {scenario!r} 未出现在 scenarios.tsv")
            continue
        current = latest.get(scenario)
        stamp = (run.get("created_utc") or "", run.get("run_id") or "")
        if current is None or stamp > (current.get("created_utc") or "", current.get("run_id") or ""):
            latest[scenario] = run

    for scenario in sorted(latest):
        run = latest[scenario]
        target = Path(VERDICTS_DIR) / f"{scenario}.json"
        if not target.is_file():
            problems.append(f"{scenario}: 缺 {target}（§8.5 要求与该场景当前 tracked 判定核对）")
            continue
        data = target.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest != (run.get("verdict_digest_sha256") or "").strip():
            problems.append(f"{run.get('run_id')}: {target.name} sha256 {digest[:8]}… != "
                            f"记录 {str(run.get('verdict_digest_sha256'))[:8]}…")
        try:
            counts = (json.loads(data.decode("utf-8")).get("counts") or {})
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            problems.append(f"{target}: 无法解析 counts（{exc}）")
            continue
        for column, key in COUNT_PAIRS:
            if str(counts.get(key)) != str(run.get(column)):
                problems.append(f"{run.get('run_id')}: 计数 {column}={run.get(column)!r} != "
                                f"{target.name} 的 {counts.get(key)!r}")
    return ("s0_crosscheck", not problems, "; ".join(problems))


# ---------------------------------------------------------------- 入口

def load_manifest(base: Path) -> dict | None:
    """manifest.json 是 verify 唯一必需的输入；缺失/无法解析/结构非法一律返回 None。

    结构要求：顶层对象、``files`` 是对象、``baselines``（若存在）是对象 —— 否则后续断言会
    以 traceback 崩掉，而 spec §7 要求"非法 manifest"是一次可读的断言不符。
    """
    path = Path(base) / "manifest.json"
    if not path.is_file():
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if not isinstance(data, dict):
        return None
    if not isinstance(data.get("files"), dict):
        return None
    if not isinstance(data.get("baselines", {}), dict):
        return None
    return data


def checks(base: Path) -> list[tuple[str, bool, str]]:
    """五条断言的 (名字, 是否通过, 细节)，顺序固定。只读。"""
    base = Path(base)
    manifest = load_manifest(base)
    if manifest is None:
        return [("sha256", False,
                 "缺或结构非法的 manifest.json（verify 的唯一输入清单；数据不可信）")]
    return [_check_sha(base, manifest), _check_rows(base, manifest),
            _check_baselines(base, manifest), _check_internal(base), _check_s0(base)]


def report(results: list[tuple[str, bool, str]]) -> str:
    lines = [f"OK {name}" if ok else f"FAIL {name}: {detail}" for name, ok, detail in results]
    passed = sum(1 for _, ok, _ in results if ok)
    lines.append(f"verify: {'PASS' if passed == len(results) else 'FAIL'} ({passed}/{len(results)})")
    return "\n".join(lines)


def verify(base: Path) -> int:
    """0 通过 / 1 任一断言不符（含 manifest.json 缺失或非法）。只打印，不写文件。

    用法错（子命令/参数不合法）由 argparse 返回 2，不经过这里 —— 见 spec §7。
    """
    results = checks(Path(base))
    print(report(results))
    return 0 if all(ok for _, ok, _ in results) else 1
