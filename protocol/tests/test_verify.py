import hashlib
import json
from pathlib import Path

import pytest

from protocol import render, verify

RUN_ID = "20260101T000000Z-abcdef0-town-two-clients"
OLD_RUN_ID = "20251231T000000Z-abcdef0-town-two-clients"
PARTY_RUN_ID = "20260102T000000Z-abcdef0-party-invite"

MAP_HEADER = ["kind", "id", "name", "rodata_va", "dispatcher", "status",
              "tier", "read_count", "write_count", "ext_mark"]
MAP_ROWS = [
    ["CMD", "11", "A", "0x01", "DoA", "mapped", "T4_fields-read+write", "1", "1", "name_only"],
    ["CMD", "12", "B", "0x02", "DoB", "mapped", "T3_fields-read-only", "1", "0", "name_only"],
    ["NOTI", "1", "C", "0x03", "", "no-dispatcher", "T0_no-evidence", "0", "0", "name_only"],
]
OPCODE_HEADER = MAP_HEADER + ["ext_evidence", "runtime_evidence", "notes"]
SCENARIO_HEADER = ["scenario_id", "step_index", "expect_index", "actor", "action", "level",
                   "pattern", "evidence_class", "mapped_opcodes", "verified_run_id",
                   "verdict_result"]
RUN_HEADER = ["run_id", "git_sha", "created_utc", "status", "pass", "needs_human",
              "missing_evidence", "fail", "evidence_class", "verdict_digest_sha256"]
COUNTS = {"pass": 1, "needs-human": 0, "missing-evidence": 0, "fail": 0}


def _tsv(header: list[str], rows: list[list[str]]) -> bytes:
    lines = ["\t".join(header)] + ["\t".join(r) for r in rows]
    return ("\n".join(lines) + "\n").encode("utf-8")


def _verdict_bytes(scenario_id: str, counts: dict) -> bytes:
    payload = {"scenario_id": scenario_id, "status": "green-with-human",
               "invalid_reason": None, "counts": counts, "entries": []}
    return (json.dumps(payload, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def _run_row(run_id: str, digest: str, counts: dict = COUNTS,
             created: str = "2026-01-01T00:00:00Z", evidence: str = "[真机]") -> list[str]:
    return [run_id, "abcdef0", created, "green-with-human",
            str(counts["pass"]), str(counts["needs-human"]),
            str(counts["missing-evidence"]), str(counts["fail"]),
            evidence, digest]


def _build(tmp_path: Path, monkeypatch, runs: list[list[str]] | None = None) -> Path:
    """一致的小树：map 3 行 / 账① 3 行 / 账② 1 行 / run 台账（默认 1 行）+ 自己的 verdicts。"""
    base = tmp_path / "protocol"
    for sub in ("map", "ledger"):
        (base / sub).mkdir(parents=True)
    (base / "map" / "opcode-map.tsv").write_bytes(_tsv(MAP_HEADER, MAP_ROWS))
    (base / "ledger" / "opcodes.tsv").write_bytes(
        _tsv(OPCODE_HEADER, [r + ["", "", ""] for r in MAP_ROWS]))
    (base / "ledger" / "scenarios.tsv").write_bytes(_tsv(SCENARIO_HEADER, [[
        "town-two-clients", "1", "1", "A", "登录并进到城镇", "[存在]",
        "000B2B0000000000000001", "[抓包]", "CMD:11", RUN_ID, "pass"]]))

    verdicts = tmp_path / "verdicts"
    verdicts.mkdir()
    (verdicts / "town-two-clients.json").write_bytes(_verdict_bytes("town-two-clients", COUNTS))
    if runs is None:
        runs = [_run_row(RUN_ID, _digest(tmp_path, "town-two-clients"))]
    (base / "ledger" / "runs.tsv").write_bytes(_tsv(RUN_HEADER, runs))

    render.render_all(base)
    monkeypatch.setattr(verify, "VERDICTS_DIR", verdicts)
    return base


def _digest(tmp_path: Path, scenario_id: str) -> str:
    return hashlib.sha256((tmp_path / "verdicts" / f"{scenario_id}.json").read_bytes()).hexdigest()


def _write_runs(base: Path, rows: list[list[str]]) -> None:
    (base / "ledger" / "runs.tsv").write_bytes(_tsv(RUN_HEADER, rows))


def _append_scenario(base: Path, row: list[str]) -> None:
    path = base / "ledger" / "scenarios.tsv"
    lines = path.read_text(encoding="utf-8").rstrip("\n").split("\n")
    lines.append("\t".join(row))
    path.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))


def _read_tsv(path: Path) -> list[dict]:
    import csv
    with path.open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in row.items()} for row in csv.DictReader(fh, delimiter="\t")]


def _failing(base: Path) -> dict[str, str]:
    return {name: detail for name, ok, detail in verify.checks(base) if not ok}


# ---------------------------------------------------------------- 通过路径

def test_verify_passes_on_consistent_tree(tmp_path, monkeypatch, capsys):
    base = _build(tmp_path, monkeypatch)
    assert verify.verify(base) == 0
    lines = capsys.readouterr().out.splitlines()
    assert sum(1 for line in lines if line.startswith("OK ")) == 5
    assert lines[-1] == "verify: PASS (5/5)"


def test_check_names_and_order_are_fixed(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    assert [name for name, _, _ in verify.checks(base)] == [
        "sha256", "rows_and_keys", "baselines", "internal", "s0_crosscheck"]


def test_verify_is_read_only(tmp_path, monkeypatch, capsys):
    base = _build(tmp_path, monkeypatch)

    def snapshot() -> dict[str, bytes]:
        return {str(p.relative_to(tmp_path)): p.read_bytes()
                for p in sorted(tmp_path.rglob("*")) if p.is_file()}

    before = snapshot()
    verify.checks(base)
    verify.verify(base)
    capsys.readouterr()
    assert snapshot() == before


# ---------------------------------------------------------------- 第 1 条：sha256 / 未列文件

def test_sha_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    p = base / "map" / "opcode-map.tsv"
    p.write_bytes(p.read_bytes().replace(b"CMD\t11\tA", b"CMD\t11\tX"))  # 只改一个字节
    assert verify.verify(base) == 1
    failing = _failing(base)
    assert "sha256" in failing and "opcode-map.tsv" in failing["sha256"]


def test_unlisted_file_fails_completeness(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    (base / "ledger" / "extra.tsv").write_bytes(b"x\n")
    assert verify.verify(base) == 1
    failing = _failing(base)
    assert "未列入清单" in failing.get("sha256", "")
    assert "ledger/extra.tsv" in failing["sha256"]


def test_listed_index_md_is_not_treated_as_unlisted(tmp_path, monkeypatch):
    """Task 3 的 map/index.md 已列入清单 —— 合规状态必须 PASS（防误报）。"""
    base = _build(tmp_path, monkeypatch)
    manifest = json.loads((base / "manifest.json").read_text(encoding="utf-8"))
    assert "map/index.md" in manifest["files"]
    assert verify.verify(base) == 0


# ---------------------------------------------------------------- 第 2 条：行数 / 主键 / 列数

def test_row_count_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    runs = base / "ledger" / "runs.tsv"
    runs.write_bytes(runs.read_bytes() + runs.read_bytes().split(b"\n")[1] + b"\n")
    assert verify.verify(base) == 1
    assert "rows_and_keys" in _failing(base)


def test_duplicate_primary_key_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    scen = base / "ledger" / "scenarios.tsv"
    row = scen.read_bytes().split(b"\n")[1]
    scen.write_bytes(scen.read_bytes() + row + b"\n")
    render.render_all(base)                      # 刷新清单，让 sha256/行数两项重新自洽
    assert verify.verify(base) == 1
    assert "主键重复" in _failing(base).get("rows_and_keys", "")


def test_wrong_column_width_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    runs = base / "ledger" / "runs.tsv"
    lines = runs.read_text(encoding="utf-8").splitlines()
    lines[1] = lines[1] + "\textra"
    runs.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))
    render.render_all(base)
    assert verify.verify(base) == 1
    assert "字段数" in _failing(base).get("rows_and_keys", "")


def test_header_tamper_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    runs = base / "ledger" / "runs.tsv"
    runs.write_bytes(runs.read_bytes().replace(b"\tpass\t", b"\tPASS\t", 1))
    render.render_all(base)
    assert verify.verify(base) == 1
    assert "表头" in _failing(base).get("rows_and_keys", "")


# ---------------------------------------------------------------- 第 3 条：基线

def _rewrite_manifest(base: Path, mutate) -> None:
    manifest = json.loads((base / "manifest.json").read_text(encoding="utf-8"))
    mutate(manifest)
    (base / "manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8")


def test_baseline_total_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    _rewrite_manifest(base, lambda m: m["baselines"].__setitem__("opcodes_total", 999))
    assert verify.verify(base) == 1
    assert "baselines" in _failing(base)
    assert "999" in _failing(base)["baselines"]


def test_baseline_tier_and_kind_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)

    def mutate(m: dict) -> None:
        m["baselines"]["tiers"]["T4_fields-read+write"] = 0
        m["baselines"]["cmd"] = 99

    _rewrite_manifest(base, mutate)
    assert verify.verify(base) == 1
    detail = _failing(base)["baselines"]
    assert "T4_fields-read+write" in detail and "cmd" in detail


# ---------------------------------------------------------------- 第 4 条：内部一致

def test_dangling_opcode_reference_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    scen = base / "ledger" / "scenarios.tsv"
    scen.write_bytes(scen.read_bytes().replace(b"CMD:11", b"CMD:99"))
    render.render_all(base)                      # 清单重新自洽，只留内部一致性问题
    assert verify.verify(base) == 1
    assert "CMD:99" in _failing(base).get("internal", "")


def test_opcode_set_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    ops = base / "ledger" / "opcodes.tsv"
    ops.write_bytes(ops.read_bytes().replace(b"NOTI\t1\tC", b"NOTI\t2\tC"))
    render.render_all(base)
    assert verify.verify(base) == 1
    assert "集合不一致" in _failing(base).get("internal", "")


def test_dangling_run_reference_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    ops = base / "ledger" / "opcodes.tsv"
    bogus = "20990101T000000Z-deadbef-town-two-clients"
    ops.write_bytes(ops.read_bytes().replace(
        b"name_only\t\t\t\n", f"name_only\t\t[真机] {bogus} A:4\t\n".encode("utf-8"), 1))
    render.render_all(base)
    assert verify.verify(base) == 1
    failing = _failing(base)
    assert "internal" in failing and bogus in failing["internal"]


# ---------------------------------------------------------------- 第 5 条：S0 交叉核对（N1 版）

def test_s0_count_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    verdict_bytes = _verdict_bytes("town-two-clients", {"pass": 9, "needs-human": 1,
                                                        "missing-evidence": 0, "fail": 0})
    (tmp_path / "verdicts" / "town-two-clients.json").write_bytes(verdict_bytes)
    _write_runs(base, [_run_row(RUN_ID, hashlib.sha256(verdict_bytes).hexdigest())])
    render.render_all(base)
    assert verify.verify(base) == 1
    assert "计数" in _failing(base).get("s0_crosscheck", "")


def test_s0_digest_mismatch_is_detected(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    _write_runs(base, [_run_row(RUN_ID, "0" * 64)])
    render.render_all(base)
    assert verify.verify(base) == 1
    assert "sha256" in _failing(base).get("s0_crosscheck", "")


def test_digest_format_is_enforced(tmp_path, monkeypatch):
    """非 64 位十六进制的摘要必须被抓到（不能只比字符串相等）。"""
    base = _build(tmp_path, monkeypatch)
    _write_runs(base, [_run_row(RUN_ID, "deadbeef")])
    render.render_all(base)
    assert verify.verify(base) == 1
    detail = _failing(base).get("s0_crosscheck", "")
    assert "64 位十六进制" in detail or "64" in detail


def test_missing_verdicts_file_is_reported_not_skipped(tmp_path, monkeypatch):
    base = _build(tmp_path, monkeypatch)
    (tmp_path / "verdicts" / "town-two-clients.json").unlink()
    assert verify.verify(base) == 1
    detail = _failing(base).get("s0_crosscheck", "")
    assert "缺" in detail and "town-two-clients.json" in detail


BAD_COUNTS = {"pass": 9, "needs-human": 9, "missing-evidence": 9, "fail": 9}


def test_historical_run_with_other_counts_does_not_fail(tmp_path, monkeypatch):
    """N1 护栏：更早的 [真机] 行是历史记录，计数与当前 tracked 文件不同**不算错**。"""
    base = _build(tmp_path, monkeypatch)
    _write_runs(base, [_run_row(OLD_RUN_ID, "a" * 64, BAD_COUNTS,
                                created="2025-12-31T00:00:00Z"),
                       _run_row(RUN_ID, _digest(tmp_path, "town-two-clients"), COUNTS,
                                created="2026-01-01T00:00:00Z")])
    render.render_all(base)
    assert verify.verify(base) == 0


def test_latest_run_with_wrong_counts_fails(tmp_path, monkeypatch):
    """同一条护栏的另一半：被改错的是**最新**那一行 ⇒ 必须 FAIL。"""
    base = _build(tmp_path, monkeypatch)
    town = _digest(tmp_path, "town-two-clients")
    _write_runs(base, [_run_row(OLD_RUN_ID, town, COUNTS, created="2025-12-31T00:00:00Z"),
                       _run_row(RUN_ID, town, BAD_COUNTS, created="2026-01-01T00:00:00Z")])
    render.render_all(base)
    assert verify.verify(base) == 1
    assert "计数" in _failing(base).get("s0_crosscheck", "")


def test_historical_run_digest_need_not_match_tracked_file(tmp_path, monkeypatch):
    """历史行的摘要只需是合法 sha256（64 位 hex），不要求等于当前 tracked 文件。"""
    base = _build(tmp_path, monkeypatch)
    _write_runs(base, [_run_row(OLD_RUN_ID, "b" * 64, created="2025-12-31T00:00:00Z"),
                       _run_row(RUN_ID, _digest(tmp_path, "town-two-clients"),
                                created="2026-01-01T00:00:00Z")])
    render.render_all(base)
    assert verify.verify(base) == 0


def test_crosscheck_is_per_scenario_not_hardcoded(tmp_path, monkeypatch):
    """§8.5 按场景取 `<scenario_id>.json`：第二个场景用自己的 tracked 判定。"""
    base = _build(tmp_path, monkeypatch)
    party_counts = {"pass": 2, "needs-human": 1, "missing-evidence": 0, "fail": 0}
    party_bytes = _verdict_bytes("party-invite", party_counts)
    (tmp_path / "verdicts" / "party-invite.json").write_bytes(party_bytes)
    _append_scenario(base, ["party-invite", "1", "1", "A", "邀请 B 组队", "[存在]",
                            "", "", "", PARTY_RUN_ID, "pass"])
    _write_runs(base, [_run_row(RUN_ID, _digest(tmp_path, "town-two-clients"),
                                created="2026-01-01T00:00:00Z"),
                       _run_row(PARTY_RUN_ID, hashlib.sha256(party_bytes).hexdigest(),
                                party_counts, created="2026-01-02T00:00:00Z")])
    render.render_all(base)
    assert verify.verify(base) == 0


def test_evidence_class_is_matched_exactly(tmp_path, monkeypatch):
    """`[真机]（回放）` 不是 `[真机]`：用「包含」匹配会把它当成真机行而误判。"""
    base = _build(tmp_path, monkeypatch)
    fake_id = "20260102T000000Z-abcdef0-town-two-clients"
    bad = {"pass": 9, "needs-human": 9, "missing-evidence": 9, "fail": 9}
    _write_runs(base, [_run_row(RUN_ID, _digest(tmp_path, "town-two-clients"),
                                created="2026-01-01T00:00:00Z"),
                       _run_row(fake_id, "c" * 64, bad, created="2026-01-02T00:00:00Z",
                                evidence="[真机]（回放）")])
    render.render_all(base)
    assert verify.verify(base) == 0


# ---------------------------------------------------------------- 输入缺失

def test_missing_manifest_is_exit_code_2(tmp_path, monkeypatch, capsys):
    base = _build(tmp_path, monkeypatch)
    (base / "manifest.json").unlink()
    assert verify.verify(base) == 2
    assert "manifest.json" in capsys.readouterr().out


def test_checks_never_raise_on_missing_manifest(tmp_path):
    results = verify.checks(tmp_path / "nowhere")
    assert [name for name, _, _ in results] == ["sha256"]
    assert results[0][1] is False
