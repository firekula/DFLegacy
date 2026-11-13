"""Restore the five migrated jars confirmed in the C2 summoner's inventory.

Only changes their use-type field. Rewards, prices, trade policy and all other
entries remain the C2 versions. Writes a new directory, never installs files.
"""
import argparse
import json
from pathlib import Path
import re
from port import Archive, canonical, field, list_rows, require, verify_views, write_views

C1_SHA = "b66ee59253013fcae78bed9750d731399e567450104dd8d0c3c83a5ef0f7ab8f"
C2_SHA = "4fa23e0270cca7bd3d97f333c9f8f406193db77b37ff3cacfcd3dcb34c2bca00"
IDS = (7125, 7130, 7138, 7148, 7189)
TYPE = re.compile(rb"(?m)^(\[stackable type\][ \t]*)`\[etc\]`[ \t]+0(?=[ \t]*\r?$)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("c1")
    parser.add_argument("c2")
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    c1 = Archive(args.c1, validate_entry_crc=False)
    c2 = Archive(args.c2)
    require(c1.sha == C1_SHA and c2.sha == C2_SHA, "Source hashes differ from reviewed evidence")
    require(c2.native, "C2 input must be the native client view")
    indexes = [list_rows(a.read("stackable/stackable.lst")) for a in (c1, c2)]
    changes, rows = {}, []
    for ident in IDS:
        require(indexes[0][ident] == indexes[1][ident], "Item identity/path changed")
        name = "stackable/" + indexes[1][ident]
        old, current = c1.read(name), c2.read(name)
        require(field(old, "stackable type") == b"`[upgradable legacy]` 1", "Unreviewed source type")
        for tag in ("name", "name2", "int data", "minimum level", "price", "icon"):
            require(canonical(field(old, tag)) == canonical(field(current, tag)), "Changed identity/rewards: " + tag)
        patched, count = TYPE.subn(rb"\g<1>`[upgradable legacy]` 1", current)
        require(count == 1, "Expected exactly one inert C2 type field")
        require(field(patched, "attach type") == field(current, "attach type"), "C2 trade policy changed")
        changes[name] = patched
        rows.append({"id": ident, "path": name, "name": field(current, "name").decode("cp936").strip("`"),
                     "before": field(current, "stackable type").decode(), "after": field(patched, "stackable type").decode()})
    write_views(c2, changes, args.output)
    report = verify_views(c2, changes, args.output)
    c1.unchanged()
    report.update(c1Sha256=c1.sha, items=rows,
                  scope="Five held migrated jars only; cash packages stay disabled; no live installation")
    (args.output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("entriesChecked", "changedEntries", "nativeSha256", "serverSha256")}, indent=2))


if __name__ == "__main__":
    main()
