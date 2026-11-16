"""Restore the three original Chapter Two avatar compounder offers."""

import argparse
import json
from pathlib import Path
import re

from port import Archive, require, verify_views, write_views

SOURCE_SHA = "1818f80e8da4dc4bef919121bbe429c5f3f7067062eda9cb230b8a6cd2282c00"
SHOP = "etc/newcashshop.etc"
OFFERS = {100033: (1, 1000), 100044: (5, 4500), 100045: (10, 8500)}


def restore_shop(data):
    items = list(re.finditer(rb"(?ms)^\[item\][ \t]*\r?\n(.*?)^\[/item\][ \t]*\r?$", data))
    require(len(items) == 1, "Expected one item section in the Chapter Two cash shop")
    section = items[0]
    restored = section[1]
    for commodity, (quantity, price) in OFFERS.items():
        identifier = str(commodity).encode("ascii")
        require(not re.search(rb"(?m)^[ \t]*" + identifier + rb"[ \t]", data),
                f"Active commodity {commodity} already exists")
        fields = rb"[ \t]+".join(str(value).encode("ascii") for value in (commodity, 21, quantity, 0, 0, price))
        pattern = rb"(?m)^([ \t]*)//(" + fields + rb"[ \t]+`[^`\r\n]+`[ \t]+0[ \t]+0[ \t]*)(?=\r?$)"
        restored, count = re.subn(pattern, rb"\1\2", restored)
        require(count == 1, f"Missing or ambiguous historical offer {commodity}")
    return data[:section.start(1)] + restored + data[section.end(1):]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    source = Archive(args.source)
    require(source.native and source.sha == SOURCE_SHA, "Expected the native Chapter Two v1.0.10 PVF")
    changes = {SHOP: restore_shop(source.read(SHOP))}
    write_views(source, changes, args.output)
    report = verify_views(source, changes, args.output)
    report["offers"] = {key: {"item": 21, "quantity": quantity, "cash": price}
                        for key, (quantity, price) in OFFERS.items()}
    (args.output / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {report['entriesChecked']} identical client/server entries. Only {SHOP} changed.")


if __name__ == "__main__":
    main()
