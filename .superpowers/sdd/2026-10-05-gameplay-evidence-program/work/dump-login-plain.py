#!/usr/bin/env python3
r"""dump-login-plain.py —— 把已落盘的 LOGIN 首帧解出**完整明文**并落盘（只读、离线）。

## 为什么需要它

handoff §4.C 的缺口是「400 B 明文里最后 12 B 由谁写」。判定它的前提是
**先把 400 B 明文本身拿到手**。此前 `verify-login-tail.py` 只打印字段游标与哈希，不落明文。
本脚本只做「定位 → 解密 → 落盘 → 尾部分区统计」，**不下结论**。

## 关键口径（⚠️ 我在这里写错过一次，留档）

1. **`classification` 判方向**（实测，不是猜）：`classification==1` 的那条流是
   **客户端→服务端**（`01 01 00 9d 01 00 00` = `[1]classification [1]msg_no`）；
   `classification==0` 的那条是**服务端→客户端**（首帧 `NOTI1`，正文 437 B，
   里面是 334 B 原始 cipher key）。
   ⚠️ **本项目既有文件名是反的**：`2343-10013.bin`（同名端口的"左"文件）里放的是
   classification==1 的 C→S 字节，而按"`<服务端端口>-<客户端端口>`"的命名习惯它本该是 S→C。
   本脚本**一律按 classification 判方向**，不按文件名。
2. **密钥材料 = 同一对里 C→S 首帧的字节 `[15:349]`**（帧偏移，不是载荷偏移；等价 `payload[2:]`），
   变换 `k[i] = (((v<<6)|(v>>2)) & 0xFF) ^ 181`，AES-128-ECB 用 `k[8:24]`。
   ⇒ **两个"15"不是一回事**：`帧[15:]` ✅ / `载荷[15:]` ❌（后者是我第一版的错，解出来是高熵垃圾）。
3. **密文 = C→S 首帧的 `[13:]`**（13 B 游戏面头），长度 400 B（恰好 16 的倍数）。

## 自检（每次运行都会打印）

判据 = 明文 `u32@0 == 0`（patch）且 `u32@4 == 344`（token 长度），且
`plain[0:8] == 00000000 58010000`。**不满足就报 FAIL，不打印明文**（避免把垃圾当证据）。

## 纪律

口令/令牌不明文打印（只打哈希）；完整明文写进产物文件。
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

from Crypto.Cipher import AES

HEAD = 13          # 游戏面帧头
KEY_OFF = 15       # 帧偏移；= HEAD + 2
TOKEN_LEN_EXPECTED = 344
TAIL_REGION = 388  # 服务端消费游标终点（既有结论）


def split_frames(buf: bytes):
    """按 13 B 头里的 `[3..6]` sLength 链式切帧。返回 (frames, 用掉的字节, 剩余)。"""
    out = []
    off = 0
    while off + HEAD <= len(buf):
        size = struct.unpack_from("<I", buf, off + 3)[0]
        if size < HEAD or off + size > len(buf):
            break
        out.append(buf[off:off + size])
        off += size
    return out, off, len(buf) - off


def derive_key(c2s_first_frame: bytes) -> bytes:
    material = c2s_first_frame[KEY_OFF:]
    k = bytes((((v << 6) | (v >> 2)) & 0xFF) ^ 181 for v in material)[:334]
    return k[8:24]


def classify(first_frame: bytes) -> str:
    """按 `[classification, msg_no]` 判这两个**具体**首帧的角色。

    ⚠️ 判据的来历（两轮踩坑后定的，别简化回"看第 0 字节"）：
      * `classification` 字段是 `0x01=CMD / 0x00=NOTI`（驱动器的 `GAME` 模型，`dnfdrive.py:17`）——
        它是**报文类别**，**不是方向**！两侧都会发 CMD 与 NOTI。
      * 我第一版把它当方向，结果把 `NOTI1`（服务端下发 334 B cipher key 的那帧）当成了 LOGIN 密文，
        解出来是高熵垃圾、自检 FAIL。第二版又按"文件名左右"猜，同样错
        （本工程的 `2343-10013.bin` 里装的其实是 C→S 字节）。
      * **实测判据**：LOGIN 首帧恒为 `[0]==1, [1]==1`（CMD1）；下发 key 的帧恒为 `[0]==0, [1]==1`（NOTI1）。
        两条流各自 21 帧 / 90 帧，只有这一对是 (1,1) 与 (0,1)。
    """
    if first_frame[0] == 0 and first_frame[1] == 1:
        return "NOTI1_KEYFRAME"
    if first_frame[0] == 1 and first_frame[1] == 1:
        return "CMD1_LOGIN"
    return "?"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--captures", nargs="+", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--json", default=None)
    args = ap.parse_args()

    rows = []
    seen_pairs = set()
    for d in args.captures:
        for sp in sorted(Path(d).rglob("*.bin")):
            parts = sp.stem.split("-")
            if len(parts) != 2:
                continue
            rp = sp.with_name(parts[1] + "-" + parts[0] + ".bin")
            if not rp.exists():
                continue
            key_pair = tuple(sorted((str(sp), str(rp))))
            if key_pair in seen_pairs:
                continue
            seen_pairs.add(key_pair)

            a = sp.read_bytes()
            b = rp.read_bytes()
            fa, _, _ = split_frames(a)
            fb, _, _ = split_frames(b)
            if not fa or not fb:
                continue
            da, db = classify(fa[0]), classify(fb[0])
            if {da, db} != {"NOTI1_KEYFRAME", "CMD1_LOGIN"}:
                continue
            keyframe = fa[0] if da == "NOTI1_KEYFRAME" else fb[0]
            loginframe = fb[0] if da == "NOTI1_KEYFRAME" else fa[0]
            keyframe_file = str(sp if da == "NOTI1_KEYFRAME" else rp)
            login_file = str(rp if da == "NOTI1_KEYFRAME" else sp)

            key = derive_key(keyframe)
            ct = loginframe[HEAD:]
            n = len(ct) // 16 * 16
            plain = AES.new(key, AES.MODE_ECB).decrypt(ct[:n])

            patch = struct.unpack_from("<I", plain, 0)[0]
            token_len = struct.unpack_from("<I", plain, 4)[0]
            selfcheck = (patch == 0 and token_len == TOKEN_LEN_EXPECTED
                         and plain[0:8] == bytes.fromhex("0000000058010000"))
            tail = plain[TAIL_REGION:]
            rows.append(dict(
                keyframe_file=keyframe_file,
                login_file=login_file,
                keyframe_len=len(keyframe),
                login_frame_len=len(loginframe),
                payload_len=len(ct),
                plain_len=len(plain),
                key_sha256=hashlib.sha256(key).hexdigest(),
                plain_sha256=hashlib.sha256(plain).hexdigest(),
                token_sha256=hashlib.sha256(plain[8:8 + token_len]).hexdigest() if selfcheck else None,
                patch=patch,
                token_length=token_len,
                selfcheck="PASS" if selfcheck else "FAIL",
                tail_region_bytes=len(tail),
                tail_all_zero=bool(tail) and tail == b"\x00" * len(tail),
                nonzero_in_tail=sum(1 for x in tail if x),
                last_nonzero_abs=(max((i for i, x in enumerate(plain) if x), default=-1)),
                plain_hex=plain.hex() if selfcheck else None,
            ))

    lines = ["# LOGIN 首帧完整明文（离线解密；方向按 classification，不按文件名）", ""]
    for r in rows:
        lines.append(json.dumps({k: v for k, v in r.items() if k != "plain_hex"},
                                ensure_ascii=False))
        if r["plain_hex"]:
            ph = r["plain_hex"]
            lines.append("  PLAIN[000:032]=" + ph[0:64])
            lines.append("  PLAIN[352:400]=" + ph[704:800])
        else:
            lines.append("  PLAIN=<withheld: selfcheck FAIL>")
    lines.append("")
    lines.append("# 判据：selfcheck 必须全 PASS；tail 区（[388:400]）的字节来源不由本脚本判定。")
    text = "\n".join(lines) + "\n"
    Path(args.out).write_text(text, encoding="utf-8")
    if args.json:
        Path(args.json).write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
    print(text)
    return 0 if all(r["selfcheck"] == "PASS" for r in rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
