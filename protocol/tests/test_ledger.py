import hashlib
import json
import shutil
from pathlib import Path

import pytest

from protocol import importer, ledger, s1

REPO_SCENARIOS = Path(__file__).resolve().parents[2] / "tools/acceptance/scenarios"
CORE_SCENARIOS = ("town-two-clients", "party-invite", "trade-open", "town-position-sync")


def _copy_core_scenarios(directory):
    directory.mkdir(parents=True, exist_ok=True)
    for name in CORE_SCENARIOS:
        shutil.copyfile(REPO_SCENARIOS / (name + ".yaml"), directory / (name + ".yaml"))
    return directory


@pytest.fixture
def spec_scenarios(tmp_path):
    # These assertions specify the original four-scenario contract. Additional
    # production scenarios must not silently change this fixture's 12 rows.
    return _copy_core_scenarios(tmp_path / "core-scenarios")


# ---------------------------------------------------------------- parse_frame

def test_parse_frame_decodes_id_and_size():
    assert ledger.parse_frame("000B2B0000000000000001") == (11, 43)
    assert ledger.parse_frame("000C2F0000000000000001") == (12, 47)


@pytest.mark.parametrize("bad", ["", "0", "zz", "000B", "010B2B00", "000B2B00000000"])
def test_parse_frame_rejects_short_input_without_guessing(bad):
    """短于 10 字节帧头一律报错：4–5 字节只带 length 的低 2 字节，猜出来的长度是错的。"""
    with pytest.raises(ValueError) as excinfo:
        ledger.parse_frame(bad)
    assert "10 字节" in str(excinfo.value)


@pytest.mark.parametrize("bad", ["zzzzzzzzzzzzzzzzzzzz",
                                 "000B2B000000000000zzzz",
                                 "000B2B0000000000000z"])
def test_parse_frame_rejects_non_hex_of_full_length(bad):
    """长度够 10 字节但含非 hex 字符：必须走「非十六进制」分支，不能被长度分支掩盖。"""
    with pytest.raises(ValueError) as excinfo:
        ledger.parse_frame(bad)
    assert "非十六进制" in str(excinfo.value)


def test_parse_frame_rejects_four_byte_prefix_that_would_lie():
    """4 字节前缀 `010B2B00` 的真值不是 43（完整头 `010B2B00010000000000` 是 65579）。"""
    with pytest.raises(ValueError):
        ledger.parse_frame("010B2B00")


def test_parse_frame_keeps_the_high_length_bytes_of_a_full_header():
    """钉住 u32 LE：高字节非 0 时必须算进去，防止退回 u16/大端。"""
    assert ledger.parse_frame("010B2B00010000000000") == (11, 65579)


# ---------------------------------------------------------------- scenarios

def test_scenario_rows_cover_all_four_scenarios_and_match_s0_expects(spec_scenarios):
    rows = ledger.build_scenarios(spec_scenarios, verified={})
    assert len(rows) == 12
    assert {r["scenario_id"] for r in rows} == {
        "town-two-clients", "party-invite", "trade-open", "town-position-sync"}
    tc = [r for r in rows if r["scenario_id"] == "town-two-clients"]
    assert [r["level"] for r in tc] == ["[存在]", "[存在]", "[存在]", "[语义]"]
    assert {r["mapped_opcodes"] for r in tc if r["mapped_opcodes"]} == {"CMD:11", "CMD:12"}


def test_semantic_expect_has_no_pattern_and_no_opcode(spec_scenarios):
    rows = ledger.build_scenarios(spec_scenarios, verified={})
    sem = next(r for r in rows if r["level"] == "[语义]")
    assert sem["pattern"] == "" and sem["mapped_opcodes"] == ""


def test_verified_run_and_result_land_in_scenario_rows(spec_scenarios):
    rows = ledger.build_scenarios(spec_scenarios,
                                  verified={"town-two-clients": "RID-1"},
                                  results={("town-two-clients", 1, 1): "pass"})
    row = next(r for r in rows if r["scenario_id"] == "town-two-clients"
               and r["step_index"] == "1" and r["expect_index"] == "1")
    assert row["verified_run_id"] == "RID-1" and row["verdict_result"] == "pass"


def test_semantic_row_keeps_the_run_that_judged_it(spec_scenarios):
    """[语义] 行也有归属 run（该 run 对它出过结论），只是结果是 needs-human。"""
    rows = ledger.build_scenarios(spec_scenarios,
                                  verified={"town-two-clients": "RID-1"},
                                  results={("town-two-clients", 3, 1): "needs-human"})
    sem = next(r for r in rows if r["scenario_id"] == "town-two-clients" and r["level"] == "[语义]")
    assert sem["verified_run_id"] == "RID-1" and sem["verdict_result"] == "needs-human"


# ---------------------------------------------------------------- evidence

def test_runtime_evidence_is_written_back_to_the_matching_opcode():
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


def test_detail_line_reads_the_file_line_not_the_last_colon():
    """真机 detail 含 `127.0.0.1:2311`，对整串 rsplit(':') 会取到 2311（brief 的 bug）。"""
    real = ("netdump-59988.log:4 2026-10-18 17:19:57.986 T=65244 TX TCP "
            "127.0.0.1:4525 -> 127.0.0.1:2311 len=11 h")
    assert ledger._detail_line(real) == "4"
    assert ledger._detail_line("语义断言：需人看；升级为自动需先有 golden bytes（spec §3.2）") == ""
    assert ledger._detail_line("") == ""


def test_ext_evidence_comes_from_tiers_external_not_from_map_rows():
    """账① 的 ext_evidence 必须由调用方从 tiers-external.tsv 传入：地图行没有这一列。"""
    map_rows = [{"kind": "CMD", "id": "1"}, {"kind": "CMD", "id": "2"}]
    out = {r["id"]: r for r in ledger.apply_runtime_evidence(
        map_rows, [], {("CMD", "1"): "证据甲; 证据乙"})}
    assert out["1"]["ext_evidence"] == "证据甲; 证据乙"
    assert out["2"]["ext_evidence"] == ""


# ---------------------------------------------------------------- columns

def test_ledger_columns_match_spec_order():
    """列序就是落盘契约（spec §5.2/§5.3/§5.4）；对调 pass/needs_human 必须在这里红。"""
    assert ledger.SCENARIO_COLUMNS == (
        "scenario_id", "step_index", "expect_index", "actor", "action", "level",
        "pattern", "evidence_class", "mapped_opcodes", "verified_run_id", "verdict_result")
    assert ledger.RUN_COLUMNS == (
        "run_id", "git_sha", "created_utc", "status",
        "pass", "needs_human", "missing_evidence", "fail",
        "evidence_class", "verdict_digest_sha256")
    assert ledger.OPCODE_COLUMNS == importer.MAP_COLUMNS + (
        "ext_evidence", "runtime_evidence", "notes")


# ---------------------------------------------------------------- merge 规则

def _scenario_row(**over):
    row = {"scenario_id": "town-two-clients", "step_index": "1", "expect_index": "1",
           "actor": "A", "action": "登录并进到城镇", "level": "[存在]",
           "pattern": "000B2B0000000000000001", "evidence_class": "[抓包]",
           "mapped_opcodes": "CMD:11", "verified_run_id": "RID-1", "verdict_result": "pass"}
    row.update(over)
    return row


def test_merge_scenarios_keeps_existing_non_empty_values():
    old = [_scenario_row()]
    new = [_scenario_row(pattern="", mapped_opcodes="", verified_run_id="", verdict_result="")]
    assert ledger.merge_scenarios(old, new) == old


def test_merge_scenarios_prefers_the_newer_run_regardless_of_ingestion_order():
    """归属按 runs.created_utc 判新旧：老 run 后摄取也不能把最新 run 顶掉（确定性）。"""
    newest = _scenario_row(verified_run_id="RID-B", verdict_result="pass")
    older = _scenario_row(verified_run_id="RID-A", verdict_result="needs-human")
    created = {"RID-A": "2026-01-01T00:00:00Z", "RID-B": "2026-01-02T00:00:00Z"}
    forward = ledger.merge_scenarios([older], [newest], created)
    backward = ledger.merge_scenarios([newest], [older], created)
    assert forward == backward
    assert forward[0]["verified_run_id"] == "RID-B"
    assert forward[0]["verdict_result"] == "pass"       # 结果跟着归属 run 走，不混搭


def test_merge_opcodes_unions_runtime_evidence_with_semicolon_space():
    old = [{"kind": "CMD", "id": "11", "ext_evidence": "证据甲", "runtime_evidence": "[真机] R1 A:4"}]
    new = [{"kind": "CMD", "id": "11", "ext_evidence": "证据甲", "runtime_evidence": "[真机] R2 A:5"}]
    merged = ledger.merge_opcodes(old, new)
    assert merged[0]["runtime_evidence"] == "[真机] R1 A:4; [真机] R2 A:5"
    assert merged[0]["ext_evidence"] == "证据甲"
    kept = ledger.merge_opcodes(merged, [{"kind": "CMD", "id": "11"}])
    assert kept[0]["runtime_evidence"] == "[真机] R1 A:4; [真机] R2 A:5"


def test_merge_opcodes_evidence_order_is_independent_of_ingestion_order():
    """证据串按自身内容排序落盘：先摄 R1 还是先摄 R2，得到的文件应逐字节相同。"""
    r1 = {"kind": "CMD", "id": "11", "runtime_evidence": "[真机] R1 A:4"}
    r2 = {"kind": "CMD", "id": "11", "runtime_evidence": "[真机] R2 A:5"}
    assert (ledger.merge_opcodes([r1], [r2])[0]["runtime_evidence"]
            == ledger.merge_opcodes([r2], [r1])[0]["runtime_evidence"])


def test_merge_runs_updates_same_run_and_sorts_by_created_utc():
    old = [{"run_id": "R2", "created_utc": "2026-01-02T00:00:00Z", "status": "green"}]
    new = [{"run_id": "R1", "created_utc": "2026-01-01T00:00:00Z", "status": "green"},
           {"run_id": "R2", "created_utc": "2026-01-02T00:00:00Z", "status": "green-with-human"}]
    merged = ledger.merge_runs(old, new)
    assert [r["run_id"] for r in merged] == ["R1", "R2"]
    assert merged[1]["status"] == "green-with-human"


# ---------------------------------------------------------------- 端到端装配

def _read_tsv(path: Path) -> list[dict]:
    import csv
    with Path(path).open(encoding="utf-8", newline="") as fh:
        return [{k: (v or "") for k, v in row.items()} for row in csv.DictReader(fh, delimiter="\t")]


def _make_env(tmp_path: Path) -> dict:
    base, raw = tmp_path / "base", tmp_path / "raw"
    rows = [{"kind": "CMD", "id": str(i), "name": f"CMD_{i}", "rodata_va": "0x0",
             "dispatcher": "", "status": "mapped", "tier": "T0_no-evidence",
             "read_count": "0", "write_count": "0", "ext_mark": "name_only"}
            for i in (11, 12, 13)]
    importer.write_tsv(base / "map" / "opcode-map.tsv", importer.MAP_COLUMNS, rows)
    raw.mkdir(parents=True, exist_ok=True)
    (raw / "tiers-external.tsv").write_bytes(
        "# kind\tid\tmark\tevidence\n"
        "CMD\t11\tfield_read\t证据甲\n"
        "CMD\t11\tbyte_verified\t证据乙\n".encode("utf-8"))
    return {"base": base, "raw": raw, "tmp": tmp_path,
            "scenarios": _copy_core_scenarios(tmp_path / "core-scenarios")}


def _town_entries(lines: tuple[str, str, str]) -> list[dict]:
    """town-two-clients 的 verdict.entries（真机 detail 形状：含 file:line 与 IP:port）。"""
    a1, a2, b1 = lines
    return [
        {"actor": "A", "step_action": "登录并进到城镇", "description": "c2s:login-ok（首个握手帧）",
         "level": "[存在]", "result": "pass",
         "detail": f"netdump-111.log:{a1} 2026-10-18 17:19:57.986 T=65244 TX TCP "
                   f"127.0.0.1:4525 -> 127.0.0.1:2311 len=11 h"},
        {"actor": "A", "step_action": "登录并进到城镇", "description": "s2c:noti1（服务端首个回包）",
         "level": "[存在]", "result": "pass", "detail": f"netdump-111.log:{a2} ..."},
        {"actor": "B", "step_action": "登录并进到城镇", "description": "c2s:login-ok（首个握手帧）",
         "level": "[存在]", "result": "pass", "detail": f"netdump-222.log:{b1} ..."},
        {"actor": "A", "step_action": "走到 B 附近", "description": "A 收到 B 的 USERINFO 且同屏",
         "level": "[语义]", "result": "needs-human",
         "detail": "语义断言：需人看；升级为自动需先有 golden bytes（spec §3.2）"},
    ]


def _party_entries() -> list[dict]:
    return [
        {"actor": "A", "step_action": "邀请 B 组队", "description": "s2c:noti7",
         "level": "[存在]", "result": "pass", "detail": "netdump-333.log:2 ..."},
        {"actor": "B", "step_action": "接受邀请", "description": "s2c:noti9",
         "level": "[存在]", "result": "pass", "detail": "netdump-444.log:2 ..."},
        {"actor": "A", "step_action": "查看队伍名册", "description": "A 的名册含 B",
         "level": "[语义]", "result": "needs-human", "detail": "语义断言：需人看"},
    ]


def _write_run_inputs(tmp_path: Path, *, run_id: str, scenario_id: str,
                      entries: list[dict], created_utc: str) -> tuple[Path, Path, Path]:
    counts = {"pass": 0, "needs-human": 0, "missing-evidence": 0, "fail": 0}
    for entry in entries:
        counts[entry["result"]] += 1
    verdict = {"scenario_id": scenario_id, "status": "green-with-human",
               "invalid_reason": None, "counts": counts, "entries": entries}
    verdict_path = tmp_path / f"verdict-{run_id}.json"
    verdict_path.write_text(json.dumps(verdict, ensure_ascii=False), encoding="utf-8")
    manifest_path = tmp_path / f"manifest-{run_id}.json"
    manifest_path.write_text(json.dumps({"run_id": run_id, "git_sha": "abcdef0",
                                         "created_utc": created_utc,
                                         "scenario_id": scenario_id}),
                             encoding="utf-8")
    digest_path = tmp_path / f"digest-{run_id}.json"
    digest_path.write_bytes(b'{"digest": 1}\n')
    return verdict_path, manifest_path, digest_path


def _ledger_argv(env: dict, *, run_id: str, scenario_id: str, verdict: Path,
                 run_manifest: Path, digest: Path) -> list[str]:
    return ["ledger", "--base", str(env["base"]), "--raw-dir", str(env["raw"]),
            "--scenarios-dir", str(env["scenarios"]), "--verified-run", run_id,
            "--verified-scenario", scenario_id, "--verdict", str(verdict),
            "--run-manifest", str(run_manifest), "--verdict-digest", str(digest)]


def _run_ledger(env: dict, *, run_id: str, scenario_id: str, entries: list[dict],
                created_utc: str) -> Path:
    """走真实 CLI 装配（argparse + cmd_ledger），只用 tmp_path 里的假产物。"""
    verdict_path, manifest_path, digest_path = _write_run_inputs(
        env["tmp"], run_id=run_id, scenario_id=scenario_id, entries=entries,
        created_utc=created_utc)
    rc = s1.main(_ledger_argv(env, run_id=run_id, scenario_id=scenario_id,
                              verdict=verdict_path, run_manifest=manifest_path,
                              digest=digest_path))
    assert rc == 0
    return digest_path


def test_cmd_ledger_end_to_end_writes_three_ledgers(tmp_path):
    env = _make_env(tmp_path)
    digest = _run_ledger(env, run_id="RID-TEST-1", scenario_id="town-two-clients",
                         entries=_town_entries(("4", "8", "4")),
                         created_utc="2026-01-01T00:00:00Z")
    base = env["base"]
    scenarios = _read_tsv(base / "ledger" / "scenarios.tsv")
    runs = _read_tsv(base / "ledger" / "runs.tsv")
    opcodes = {r["id"]: r for r in _read_tsv(base / "ledger" / "opcodes.tsv")
               if r["kind"] == "CMD"}

    town = [r for r in scenarios if r["scenario_id"] == "town-two-clients"]
    assert [r["verdict_result"] for r in town] == ["pass", "pass", "pass", "needs-human"]
    assert {r["verified_run_id"] for r in town} == {"RID-TEST-1"}
    assert len(scenarios) == 12

    assert len(runs) == 1 and runs[0]["run_id"] == "RID-TEST-1"
    assert runs[0]["pass"] == "3" and runs[0]["needs_human"] == "1"
    assert runs[0]["verdict_digest_sha256"] == hashlib.sha256(digest.read_bytes()).hexdigest()
    assert runs[0]["evidence_class"] == "[真机]"

    # external 的键必须是 (kind, id)：字符串键写法会在这里红
    assert opcodes["11"]["ext_evidence"] == "证据甲; 证据乙"
    assert opcodes["11"]["runtime_evidence"] == "[真机] RID-TEST-1 A:4,B:4"
    assert opcodes["12"]["runtime_evidence"] == "[真机] RID-TEST-1 A:8"
    assert opcodes["13"]["runtime_evidence"] == "" and opcodes["13"]["ext_evidence"] == ""


def test_cmd_ledger_merges_runs_and_does_not_clear_other_scenarios(tmp_path):
    """两个 town run 追加而不是覆写；再跑 party-invite 不得清空 town 的归属。"""
    env = _make_env(tmp_path)
    _run_ledger(env, run_id="RID-A", scenario_id="town-two-clients",
                entries=_town_entries(("4", "8", "4")), created_utc="2026-01-01T00:00:00Z")
    _run_ledger(env, run_id="RID-B", scenario_id="town-two-clients",
                entries=_town_entries(("5", "9", "5")), created_utc="2026-01-02T00:00:00Z")
    _run_ledger(env, run_id="RID-C", scenario_id="party-invite",
                entries=_party_entries(), created_utc="2026-01-03T00:00:00Z")
    base = env["base"]

    runs = _read_tsv(base / "ledger" / "runs.tsv")
    assert [r["run_id"] for r in runs] == ["RID-A", "RID-B", "RID-C"]  # created_utc 升序
    assert {r["verdict_digest_sha256"] for r in runs} == {
        hashlib.sha256((env["tmp"] / f"digest-{rid}.json").read_bytes()).hexdigest()
        for rid in ("RID-A", "RID-B", "RID-C")}

    scenarios = _read_tsv(base / "ledger" / "scenarios.tsv")
    assert len(scenarios) == 12
    town = [r for r in scenarios if r["scenario_id"] == "town-two-clients"]
    assert {r["verified_run_id"] for r in town} == {"RID-B"}  # 同场景后者更新；party 那轮不清空
    assert [r["verdict_result"] for r in town] == ["pass", "pass", "pass", "needs-human"]
    party = [r for r in scenarios if r["scenario_id"] == "party-invite"]
    assert {r["verified_run_id"] for r in party} == {"RID-C"}

    opcodes = {r["id"]: r for r in _read_tsv(base / "ledger" / "opcodes.tsv")
               if r["kind"] == "CMD"}
    # 多 run 的证据按 "; " 累积（spec §5.2）
    assert opcodes["11"]["runtime_evidence"] == (
        "[真机] RID-A A:4,B:4; [真机] RID-B A:5,B:5")
    assert opcodes["12"]["runtime_evidence"] == "[真机] RID-A A:8; [真机] RID-B A:9"
    assert opcodes["11"]["ext_evidence"] == "证据甲; 证据乙"  # 后续 run 不丢外部证据


def test_ledger_ingestion_order_does_not_change_any_ledger(tmp_path):
    """正序摄取与逆序摄取必须得到逐字节相同的三份台账（manifest sha256 才稳定）。"""
    batch = (("RID-A", "2026-01-01T00:00:00Z", ("4", "8", "4")),
             ("RID-B", "2026-01-02T00:00:00Z", ("5", "9", "5")))

    def ingest(order: str) -> dict[str, bytes]:
        env = _make_env(tmp_path / order)
        rows = batch if order == "forward" else tuple(reversed(batch))
        for run_id, created_utc, lines in rows:
            _run_ledger(env, run_id=run_id, scenario_id="town-two-clients",
                        entries=_town_entries(lines), created_utc=created_utc)
        ledger_dir = env["base"] / "ledger"
        return {name: (ledger_dir / name).read_bytes()
                for name in ("scenarios.tsv", "runs.tsv", "opcodes.tsv")}

    forward, backward = ingest("forward"), ingest("backward")
    assert forward == backward
    # 两种顺序的最终归属都必须是 created_utc 最新的 RID-B（不是"最后摄取者"）
    town = [r for r in _read_tsv(tmp_path / "forward" / "base" / "ledger" / "scenarios.tsv")
            if r["scenario_id"] == "town-two-clients"]
    assert {r["verified_run_id"] for r in town} == {"RID-B"}
    assert [r["verdict_result"] for r in town] == ["pass", "pass", "pass", "needs-human"]


# ---------------------------------------------------------------- N1：输入缺失/不符 ⇒ exit 2

def test_cmd_ledger_missing_verdict_file_exits_2(tmp_path, capsys):
    """spec §7：`ledger` 输入缺失 = 2，且不吐 traceback（N1）。"""
    env = _make_env(tmp_path)
    _, manifest_path, digest_path = _write_run_inputs(
        tmp_path, run_id="RID-TEST-1", scenario_id="town-two-clients",
        entries=_town_entries(("4", "8", "4")), created_utc="2026-01-01T00:00:00Z")
    rc = s1.main(_ledger_argv(env, run_id="RID-TEST-1", scenario_id="town-two-clients",
                              verdict=tmp_path / "nope-verdict.json",
                              run_manifest=manifest_path, digest=digest_path))
    assert rc == 2
    out = capsys.readouterr().out
    assert "ledger: FAIL (2)" in out and "nope-verdict.json" in out


def test_cmd_ledger_invalid_run_manifest_json_exits_2(tmp_path, capsys):
    """run manifest 非法 JSON ⇒ 2（ValueError 路径），不吐 traceback（N1）。"""
    env = _make_env(tmp_path)
    verdict_path, manifest_path, digest_path = _write_run_inputs(
        tmp_path, run_id="RID-TEST-1", scenario_id="town-two-clients",
        entries=_town_entries(("4", "8", "4")), created_utc="2026-01-01T00:00:00Z")
    manifest_path.write_text("{ oops not json", encoding="utf-8")
    rc = s1.main(_ledger_argv(env, run_id="RID-TEST-1", scenario_id="town-two-clients",
                              verdict=verdict_path, run_manifest=manifest_path,
                              digest=digest_path))
    assert rc == 2
    out = capsys.readouterr().out
    assert "ledger: FAIL (2)" in out


def test_cmd_ledger_scenario_mismatch_exits_2(tmp_path, capsys):
    """spec §7 的「不符」也算 2：verdict 的场景与 --verified-scenario 不一致。"""
    env = _make_env(tmp_path)
    verdict_path, manifest_path, digest_path = _write_run_inputs(
        tmp_path, run_id="RID-TEST-1", scenario_id="town-two-clients",
        entries=_town_entries(("4", "8", "4")), created_utc="2026-01-01T00:00:00Z")
    rc = s1.main(_ledger_argv(env, run_id="RID-TEST-1", scenario_id="party-invite",
                              verdict=verdict_path, run_manifest=manifest_path,
                              digest=digest_path))
    assert rc == 2
    out = capsys.readouterr().out
    assert "ledger: FAIL (2)" in out and "不符" in out


def test_added_scenario_is_discovered_without_restricting_ledger_to_four_names(spec_scenarios):
    text=(spec_scenarios / "town-two-clients.yaml").read_text(encoding="utf-8")
    (spec_scenarios / "additional.yaml").write_text(text.replace("town-two-clients", "extra-contract"), encoding="utf-8")
    rows=ledger.build_scenarios(spec_scenarios, verified={})
    extra=[row for row in rows if row["scenario_id"]=="extra-contract"]
    assert len(rows)==16 and len(extra)==4
    assert [row["level"] for row in extra]==["[存在]", "[存在]", "[存在]", "[语义]"]
    assert {row["scenario_id"] for row in rows}==set(CORE_SCENARIOS)|{"extra-contract"}
