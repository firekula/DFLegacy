#!/usr/bin/env python3
"""demangle-syms.py — 把 nm 转储的第四列名字批量 demangle。

为什么用 sidecar 的 c++filt：本机 python `cxxfilt` 不可用
（`LibraryNotFound: Cannot find any of libraries: ('c',)`，Windows 上找不到 libc），
而 `dfl-recon` 里有 binutils 2.44 的 c++filt（2026-10-05 实测可用）。
sidecar 的 netns 可能已失效 —— 那是抓包的事，纯 CPU 任务不受影响。

输入/输出都是 `addr\tsize\tTYPE\tname` 四列 TSV，行数必须一一对应。
用法: demangle-syms.py <src.syms.txt> <dst.tsv>
"""
import subprocess
import sys
from pathlib import Path

CXXFILT = ["docker", "exec", "-i", "dfl-recon", "c++filt"]


def main(src: str, dst: str) -> None:
    rows = Path(src).read_text(encoding="utf-8", errors="replace").splitlines()
    names = []
    for ln in rows:
        parts = ln.split("\t", 3)
        names.append(parts[3] if len(parts) >= 4 else "")
    proc = subprocess.run(CXXFILT, input="\n".join(names).encode(),
                          capture_output=True)
    if proc.returncode != 0:
        sys.exit(f"c++filt 退出码 {proc.returncode}: {proc.stderr[:400]!r}")
    demangled = proc.stdout.decode("utf-8", errors="replace").splitlines()
    assert len(demangled) == len(rows), \
        f"c++filt 行数不一致: {len(demangled)} vs {len(rows)}"
    out = []
    for ln, dem in zip(rows, demangled):
        parts = ln.split("\t", 3)
        if len(parts) >= 4:
            parts[3] = dem
        out.append("\t".join(parts))
    Path(dst).write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"{len(out)} rows -> {dst}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
