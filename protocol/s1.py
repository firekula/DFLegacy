"""S1 CLI：import / ledger。"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
from pathlib import Path

from . import importer, ledger

RAW_DEFAULT = Path(".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out")
BASE_DEFAULT = Path("protocol")
SCENARIOS_DEFAULT = Path("tools/acceptance/scenarios")


def cmd_import(args: argparse.Namespace) -> int:
    stats = importer.import_map(Path(args.raw_dir), Path(args.base))
    print(f"import: {stats['rows']} 行 -> {stats['path']}")
    return 0


def cmd_ledger(args: argparse.Namespace) -> int:
    """把一份真机 verdict 贴回场景账，并派生 opcode 账（spec §5.2–§5.4）。"""
    base = Path(args.base)
    map_path = base / "map" / "opcode-map.tsv"
    with map_path.open(encoding="utf-8", newline="") as fh:
        map_rows = [{k: (v or "") for k, v in row.items()}
                    for row in csv.DictReader(fh, delimiter="\t")]

    verdict = json.loads(Path(args.verdict).read_text(encoding="utf-8"))
    scenario_id = args.verified_scenario
    if verdict.get("scenario_id") != scenario_id:
        raise SystemExit(f"verdict 属于 {verdict.get('scenario_id')!r}，"
                         f"与 --verified-scenario {scenario_id!r} 不符，拒绝落账")

    # verdict.entries 与场景声明顺序一一对应（judge 按 steps→expects 顺序产出）。
    # 这里不盲信顺序：条数不符、或 (actor, action, level) 对不上就报错，绝不臆测对应关系。
    scenarios = ledger.build_scenarios(Path(args.scenarios_dir),
                                       verified={scenario_id: args.verified_run})
    wired = [row for row in scenarios if row["scenario_id"] == scenario_id]
    entries = verdict["entries"]
    if len(wired) != len(entries):
        raise SystemExit(f"verdict 条目数 {len(entries)} 与场景期望数 {len(wired)} 不符，"
                         "拒绝臆测对应关系")
    for row, entry in zip(wired, entries):
        declared = (entry.get("actor"), entry.get("step_action"), entry.get("level"))
        actual = (row["actor"], row["action"], row["level"])
        if declared != actual:
            raise SystemExit(f"verdict 条目与场景期望不对应：{declared} vs {actual}，"
                             "拒绝臆测对应关系")
        row["verdict_result"] = entry["result"]
        row["verdict_detail"] = entry.get("detail", "")

    # verdict_digest = tools/acceptance/verdicts/<scenario>.json（进 git 的归一化摘要）。
    digest_sha256 = hashlib.sha256(Path(args.verdict_digest).read_bytes()).hexdigest()
    runs = ledger.build_runs(Path(args.verdict), Path(args.run_manifest), "[真机]",
                             digest_sha256)
    if runs[0]["run_id"] != args.verified_run:
        raise SystemExit(f"--verified-run {args.verified_run!r} 与 manifest.run_id "
                         f"{runs[0]['run_id']!r} 不符，拒绝落账")

    # 人工外部证据原文由 tiers-external.tsv 直读（账① 才存它；地图行只有 ext_mark）。
    external = {(kind, str(ident)): "; ".join(text for _, text in pairs if text)
                for (kind, ident), pairs in importer.load_external(Path(args.raw_dir)).items()}
    opcode_rows = ledger.apply_runtime_evidence(map_rows, scenarios, external)
    stats = ledger.write_ledgers(base, scenarios, runs, opcode_rows)
    print(f"ledger: {stats}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="s1")
    sub = parser.add_subparsers(dest="cmd", required=True)
    p_import = sub.add_parser("import", help="原始产物 -> 规范 TSV")
    p_import.add_argument("--raw-dir", default=str(RAW_DEFAULT))
    p_import.add_argument("--base", default=str(BASE_DEFAULT))
    p_import.set_defaults(func=cmd_import)
    p_ledger = sub.add_parser("ledger", help="生成两本账")
    p_ledger.add_argument("--base", default=str(BASE_DEFAULT))
    p_ledger.add_argument("--raw-dir", default=str(RAW_DEFAULT))
    p_ledger.add_argument("--scenarios-dir", default=str(SCENARIOS_DEFAULT))
    p_ledger.add_argument("--verified-run", required=True)
    p_ledger.add_argument("--verified-scenario", required=True)
    p_ledger.add_argument("--verdict", required=True, help="runs/<runId>/verdict.json")
    p_ledger.add_argument("--run-manifest", required=True, help="runs/<runId>/manifest.json")
    p_ledger.add_argument("--verdict-digest", required=True,
                          help="tools/acceptance/verdicts/<scenario>.json")
    p_ledger.set_defaults(func=cmd_ledger)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
