"""S1 CLI：import / render / verify。"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import importer

RAW_DEFAULT = Path(".superpowers/sdd/2026-10-05-gameplay-evidence-program/work/out")
BASE_DEFAULT = Path("protocol")


def cmd_import(args: argparse.Namespace) -> int:
    stats = importer.import_map(Path(args.raw_dir), Path(args.base))
    print(f"import: {stats['rows']} 行 -> {stats['path']}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="s1")
    sub = parser.add_subparsers(dest="cmd", required=True)
    p_import = sub.add_parser("import", help="原始产物 -> 规范 TSV")
    p_import.add_argument("--raw-dir", default=str(RAW_DEFAULT))
    p_import.add_argument("--base", default=str(BASE_DEFAULT))
    p_import.set_defaults(func=cmd_import)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
