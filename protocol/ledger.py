"""两本账：场景账（期望 ↔ opcode ↔ 哪次 run 验过）与 run 台账。

场景解析复用 tools/acceptance 的 ``load_scenario`` —— 不重写一份 YAML 解析。
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from .importer import MAP_COLUMNS, write_tsv

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
    非法/过短输入报错，绝不猜。
    """
    text = (pattern or "").strip().lower()
    if len(text) < 8:
        raise ValueError(f"帧头不足 4 字节：{pattern!r}")
    try:
        head = bytes.fromhex(text[:12])
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
    base = Path(base)
    write_tsv(base / "ledger" / "scenarios.tsv", SCENARIO_COLUMNS, scenarios)
    write_tsv(base / "ledger" / "runs.tsv", RUN_COLUMNS, runs)
    write_tsv(base / "ledger" / "opcodes.tsv", OPCODE_COLUMNS, opcode_rows)
    return {"scenarios": len(scenarios), "runs": len(runs), "opcodes": len(opcode_rows)}
