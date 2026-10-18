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
