"""Restore proven ACT1 jar shops/types into pinned C2 resources; never installs."""
import argparse
import json
from pathlib import Path
import re
from port import Archive, canonical, field, list_rows, require, verify_views, write_views

C1_SHA = "b66ee59253013fcae78bed9750d731399e567450104dd8d0c3c83a5ef0f7ab8f"
C2_SHA = "c20741d48a641a525f7c214fbede555783fb737b344d14eeada47aeca8337669"
TYPE = re.compile(rb"(?m)^(\[stackable type\][ \t]*)`\[etc\]`[ \t]+0(?=[ \t]*\r?$)")
SALES = re.compile(rb"(?ms)(^\s*\[sell item\][^\r\n]*\r?\n).*?(^\s*\[/sell item\])")


def build_changes(c1, c2):
    require(c1.sha == C1_SHA and c2.sha == C2_SHA, "Unreviewed source hashes")
    old_index, new_index = [list_rows(a.read("stackable/stackable.lst")) for a in (c1, c2)]
    changes, audit = {}, []
    for ident, relative in old_index.items():
        before = c1.read("stackable/" + relative)
        if field(before, "stackable type") != b"`[upgradable legacy]` 1":
            continue
        require(new_index.get(ident) == relative, f"Jar identity changed: {ident}")
        path = "stackable/" + relative
        current = c2.read(path)
        for tag in ("price", "int data", "minimum level", "icon", "lottery use cost"):
            require(canonical(field(before, tag)) == canonical(field(current, tag)), f"Jar {ident} changed {tag}")
        current_type = field(current, "stackable type")
        if current_type != b"`[upgradable legacy]` 1":
            require(current_type == b"`[etc]` 0", f"Unknown use type for jar {ident}")
            replacement, count = TYPE.subn(rb"\g<1>`[upgradable legacy]` 1", current)
            require(count == 1, f"Jar type match count {ident}: {count}")
            changes[path] = replacement
        audit.append({"id": ident, "path": path, "changed": path in changes,
                      "name": field(before, "name").decode("cp936").strip("`")})
    require(len(audit) == 107, "Unexpected C1 jar inventory")
    for n in range(1, 6):
        path = f"itemshop/regacy{n}.shp"
        old, current = c1.read(path), c2.read(path)
        require(field(old, "NPC") == field(current, "NPC"), "Shop NPC identity changed")
        matches = list(SALES.finditer(old))
        require(len(matches) == 1, "Source sale block is ambiguous")
        old_block = matches[0].group(0)
        replacement, count = SALES.subn(lambda _: old_block, current)
        require(count == 1, "Target sale block is ambiguous")
        changes[path] = replacement
    return changes, audit


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("c1")
    p.add_argument("c2")
    p.add_argument("output", type=Path)
    a = p.parse_args()
    c1, c2 = Archive(a.c1, validate_entry_crc=False), Archive(a.c2)
    changes, jars = build_changes(c1, c2)
    write_views(c2, changes, a.output)
    report = verify_views(c2, changes, a.output)
    c1.unchanged()
    report.update(sourceC1Sha256=c1.sha, jars=jars, scope="Five NPC shops and original jar use types only")
    (a.output / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("entriesChecked", "changedEntries", "nativeSha256", "serverSha256")}, indent=2))


if __name__ == "__main__":
    main()
