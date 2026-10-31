#!/usr/bin/env python3
r"""vmget.py —— 从 VM 取文件到本地（并做编码转换）。

用法:
    vmget.py <远端路径> <本地路径> [--from-encoding gb18030] [--binary]

为什么不直接用 vmssh.py "cat"：终端会把 GB18030 的中文显示成乱码，
而我们要读的是登录服的源码（大量中文注释/配置），**必须拿到干净的字节**。
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vmssh import connect                      # noqa: E402


def get(remote: str, local: Path, enc: str | None, binary: bool) -> int:
    cli = connect()
    try:
        sftp = cli.open_sftp()
        data = sftp.file(remote, "rb").read()
        sftp.close()
    finally:
        cli.close()
    local.parent.mkdir(parents=True, exist_ok=True)
    if binary or enc is None:
        local.write_bytes(data)
        print(f"{remote} -> {local}  {len(data)} B（原样）")
        return 0
    text = data.decode(enc, "replace")
    local.write_text(text, encoding="utf-8")
    print(f"{remote} -> {local}  {len(data)} B（{enc} -> utf-8）")
    return 0


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    enc = None
    if "--from-encoding" in sys.argv:
        enc = sys.argv[sys.argv.index("--from-encoding") + 1]
    if len(args) < 2:
        raise SystemExit(__doc__)
    sys.exit(get(args[0], Path(args[1]), enc, "--binary" in sys.argv))
