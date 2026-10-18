"""两本账：场景账（期望 ↔ opcode ↔ 哪次 run 验过）与 run 台账。

场景解析复用 tools/acceptance 的 ``load_scenario`` —— 不重写一份 YAML 解析。
"""
from __future__ import annotations

import csv
import json
import sys
from pathlib import Path

# ``_KIND_ORDER`` 是地图主键顺序的唯一事实源（CMD 先 → id 升序）；共用它，避免两处排序漂移。
from .importer import _KIND_ORDER, MAP_COLUMNS, write_tsv

#: 仓库根（本文件在 ``<root>/protocol/ledger.py``）。用绝对路径而不是 ``Path("tools")``，
#: 这样从任何工作目录调用都能找到 S0 的场景解析器。
_REPO_ROOT = Path(__file__).resolve().parents[1]

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


def _load_scenario(path: Path):
    """按 S0 的契约解析场景 YAML（复用，不重写）。"""
    tools = str(_REPO_ROOT / "tools")
    if tools not in sys.path:
        sys.path.insert(0, tools)
    from acceptance.model import load_scenario

    return load_scenario(path)


def parse_frame(pattern: str) -> tuple[int, int]:
    """从帧十六进制里取 ``(protocol id, 声明总长度)``。

    帧头是 10 字节传输头 ``type(u8) | protocol(u8) | total_length(u32 LE) | crc32(u32 LE)``
    —— 这不是从 plan 的散文里抄的，而是两路独立证据：服务端实现
    ``src/DFLegacy.Protocol/PacketFrame.cs:6-9``（``ProtocolId`` 在偏移 1、长度在偏移 2）
    与真机日志互证（``000A6637…`` ⇒ protocol 10 / length 0x3766=14182，与 server.log
    ``protocol=10 length=14182`` 逐字吻合；type=1 的帧首字节确为 ``01``）。

    长度字段占 4 字节，**必须凑满 10 字节帧头才允许解码**：只给 4–5 字节时高字节缺失，
    ``int.from_bytes`` 会把它们当 0，于是 ``010B2B00`` 会"猜"成 43（真值 65579）。
    因此守卫与切片都按 10 字节对齐；不足或任何位置非 hex 一律 ``ValueError``，绝不猜。
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
    """四个 S0 场景的每条期望一行；``verified``/``results`` 已由调用方查好，不在这里猜。"""
    results = results or {}
    rows: list[dict] = []
    for path in sorted(Path(scenarios_dir).glob("*.yaml")):
        sc = _load_scenario(path)
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
    """真机 run 台账：只收产出了 verdict 的 run（spec §5.4 收录边界）。"""
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


def _detail_line(detail: str) -> str:
    """从 ``verdict.judge`` 的 detail（``<证据文件>:<行号> <行内容>``）里取行号。

    取**首个空白分隔的 token** 再切 ``:`` —— 行内容里本身含 ``IP:port`` 等冒号，
    对整串 ``rsplit(":")`` 会取到错误的数字（真产物里会得到 ``A:2311``）。形状不符即返回
    空串：``fail``/``missing-evidence``/``[语义]`` 的 detail 是中文散文，绝不当作证据行号。
    """
    token = (detail or "").split(" ", 1)[0]
    _, sep, lineno = token.rpartition(":")
    return lineno if sep and lineno.isdigit() else ""


def apply_runtime_evidence(map_rows: list[dict], scenarios: list[dict],
                           external: dict[tuple[str, str], str] | None = None) -> list[dict]:
    """把「该 opcode 在哪次 run 的哪一行被验证」写回账①，并补上人工外部证据原文。

    两处查找键都是**地图行的 ``kind:id``**：地图行没有 ``mapped_opcodes``/``ext_evidence``
    列，对着它查会把这两列永远查成空（预检发现的缺陷 P1/P2）。``ext_evidence`` 由调用方
    从 ``tiers-external.tsv`` 读入（``{(kind, id): "证据1; 证据2"}``，id 为字符串），
    不依赖 Task 1 的中间键。
    """
    external = external or {}
    # opcode -> runId -> ["A:4", "B:4"]；同一 run 的多个 actor 用 "," 合并成一个 [真机] 标注。
    buckets: dict[str, dict[str, list[str]]] = {}
    for row in scenarios:
        opcode = (row.get("mapped_opcodes") or "").strip()
        run_id = (row.get("verified_run_id") or "").strip()
        line = _detail_line(row.get("verdict_detail") or "")
        if not opcode or not run_id or not line:
            continue
        buckets.setdefault(opcode, {}).setdefault(run_id, []).append(
            f"{row.get('actor', '')}:{line}")
    out: list[dict] = []
    for row in map_rows:
        merged = dict(row)
        key = f"{row['kind']}:{row['id']}"
        merged["runtime_evidence"] = "; ".join(
            f"[真机] {run_id} {','.join(lines)}"
            for run_id, lines in buckets.get(key, {}).items())
        merged["ext_evidence"] = external.get((row["kind"], row["id"]), "")
        merged.setdefault("notes", "")
        out.append(merged)
    return out


def write_ledgers(base: Path, scenarios: list[dict], runs: list[dict],
                  opcode_rows: list[dict]) -> dict:
    """把本次 run 的行**合并**进已有台账再落盘（不是覆写）；返回合并后的行数。

    台账是可累积的历史：跑另一个场景/另一次 run 不能把上一次的 ``verified_run_id``
    或 ``runtime_evidence`` 静默抹掉（审查 I2）。合并规则见 ``merge_*``。
    """
    base = Path(base)
    ledger_dir = base / "ledger"
    # runs 先合并：scenarios 的归属要用它的 created_utc 判新旧（与摄取顺序无关）。
    merged_runs = merge_runs(_read_tsv(ledger_dir / "runs.tsv", RUN_COLUMNS), runs)
    run_created = {r["run_id"]: r["created_utc"] for r in merged_runs}
    merged_scenarios = merge_scenarios(
        _read_tsv(ledger_dir / "scenarios.tsv", SCENARIO_COLUMNS), scenarios, run_created)
    merged_opcodes = merge_opcodes(
        _read_tsv(ledger_dir / "opcodes.tsv", OPCODE_COLUMNS), opcode_rows)
    write_tsv(ledger_dir / "scenarios.tsv", SCENARIO_COLUMNS, merged_scenarios)
    write_tsv(ledger_dir / "runs.tsv", RUN_COLUMNS, merged_runs)
    write_tsv(ledger_dir / "opcodes.tsv", OPCODE_COLUMNS, merged_opcodes)
    return {"scenarios": len(merged_scenarios), "runs": len(merged_runs),
            "opcodes": len(merged_opcodes)}


def _read_tsv(path: Path, columns: tuple[str, ...]) -> list[dict]:
    """读已有台账；文件不存在返回空表。表头与契约不符即报错（不合并进错的形状）。"""
    path = Path(path)
    if not path.is_file():
        return []
    with path.open(encoding="utf-8", newline="") as fh:
        reader = csv.DictReader(fh, delimiter="\t")
        header = tuple(reader.fieldnames or ())
        if header != columns:
            raise ValueError(f"{path} 的表头与列契约不符：{header} != {columns}"
                             "（先按新列重新生成，不要往旧形状里合并）")
        return [{k: (v or "") for k, v in row.items()} for row in reader]


def _scenario_key(row: dict) -> tuple[str, str, str]:
    return (row["scenario_id"], row["step_index"], row["expect_index"])


def merge_scenarios(existing: list[dict], incoming: list[dict],
                    run_created: dict[str, str] | None = None) -> list[dict]:
    """按 ``(scenario_id, step_index, expect_index)`` 合并，归属取 ``created_utc`` 最新的 run。

    - 普通列：新值为空时不覆盖已有非空值（跑另一个场景不清空旧归属）；
    - ``verified_run_id``/``verdict_result``：只有 ``run_created`` 证明新 run **更晚**
      （或同一个 run、或该行本来没有归属）时才覆盖，否则保留旧值。于是同一批 run
      无论正序还是逆序摄取，``scenarios.tsv`` 都逐字节相同（manifest 的 sha256 才稳定）；
    - ``run_created`` 为 None，或两个 runId 有一个在表里查不到时，退化为「新值非空即覆盖」。
    """
    merged: dict[tuple[str, str, str], dict] = {_scenario_key(r): dict(r) for r in existing}
    for row in incoming:
        key = _scenario_key(row)
        old = merged.get(key)
        if old is None:
            merged[key] = dict(row)
            continue
        updated = {col: (row.get(col) or old.get(col, "")) for col in SCENARIO_COLUMNS}
        updated["verified_run_id"], updated["verdict_result"] = _pick_attribution(
            old, row, run_created)
        merged[key] = updated
    return sorted(merged.values(),
                  key=lambda r: (r["scenario_id"], int(r["step_index"]),
                                 int(r["expect_index"])))


def _pick_attribution(old: dict, new: dict,
                      run_created: dict[str, str] | None) -> tuple[str, str]:
    """该行归给哪个 run：``created_utc`` 更晚者胜出；同一 run 则采用新结果。"""
    old_run = old.get("verified_run_id", "")
    new_run = new.get("verified_run_id", "")
    if not new_run:
        return old_run, old.get("verdict_result", "")
    if not old_run:
        return new_run, new.get("verdict_result", "")
    if new_run == old_run:
        return new_run, new.get("verdict_result") or old.get("verdict_result", "")
    if run_created:
        old_at, new_at = run_created.get(old_run), run_created.get(new_run)
        if old_at is not None and new_at is not None and new_at <= old_at:
            return old_run, old.get("verdict_result", "")
    return new_run, new.get("verdict_result", "")


def merge_runs(existing: list[dict], incoming: list[dict]) -> list[dict]:
    """按 ``run_id`` 合并：同 run 以新值更新，异 run 追加；按 ``created_utc`` 升序落盘。"""
    merged: dict[str, dict] = {r["run_id"]: dict(r) for r in existing}
    for row in incoming:
        merged[row["run_id"]] = {col: row.get(col, "") for col in RUN_COLUMNS}
    return sorted(merged.values(), key=lambda r: (r["created_utc"], r["run_id"]))


def _merge_evidence(old: str, new: str) -> str:
    """把两段以 ``"; "`` 分隔的证据并集化（去重 + 按内容排序；spec §5.2 多条用 "; "）。

    排序不只是好看：runId 以 UTC 时间戳开头，按条目排序 = 按 run 时间排序，且让落盘结果
    **与摄取顺序无关** —— 无论先并哪个 run、重复跑几次，三份 TSV 都逐字节相同。
    """
    items = {item.strip() for chunk in (old or "", new or "")
             for item in chunk.split(";") if item.strip()}
    return "; ".join(sorted(items))


def merge_opcodes(existing: list[dict], incoming: list[dict]) -> list[dict]:
    """按 ``(kind, id)`` 合并：地图 10 列以新值为准，证据列"空不覆盖非空"、多 run 累积。"""
    merged: dict[tuple[str, str], dict] = {
        (r["kind"], r["id"]): dict(r) for r in existing}
    for row in incoming:
        key = (row["kind"], row["id"])
        old = merged.get(key)
        if old is None:
            merged[key] = dict(row)
            continue
        updated = dict(old)
        for col in MAP_COLUMNS:
            updated[col] = row.get(col, old.get(col, ""))
        updated["ext_evidence"] = row.get("ext_evidence") or old.get("ext_evidence", "")
        updated["runtime_evidence"] = _merge_evidence(
            old.get("runtime_evidence", ""), row.get("runtime_evidence", ""))
        updated["notes"] = row.get("notes") or old.get("notes", "")
        merged[key] = updated
    return sorted(merged.values(),
                  key=lambda r: (_KIND_ORDER.get(r["kind"], 9), int(r["id"])))
