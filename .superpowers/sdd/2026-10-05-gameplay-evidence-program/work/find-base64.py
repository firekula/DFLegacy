#!/usr/bin/env python3
r"""find-base64.py —— 在载荷里找 base64 串（因为 decrypt 的第一步就是 base64_decode）。

## 为什么之前没找到令牌

我先前一直假设帧里的令牌是**裸 256 字节二进制签名**，扫的是"256 B 滑窗能否被公钥解成
type-1"。但 `Taiwan::AuthGarenaToken::decrypt` 反汇编出来的第一步是：

```
call base64_decode(std::string const&, std::vector<unsigned char>&)
call COpensslRSA::Decrypt(0x9404754 /* BSS 里的全局，启动时从 publickey.pem 初始化 */, ...)
cmp  size, 0x68        ; 结果 ≤ 104 字节 = GarenaAuthData
```

⇒ 帧里的令牌很可能是 **base64 文本**（可打印 ASCII），根本不该用二进制滑窗去找。

## 本脚本做什么

1. 找出载荷里**最长的可打印串**，并判断它像不像 base64（字符集 + 长度 %4）
2. 对每个候选做 base64 解码，看解出来是不是 **256 字节**（RSA-2048）
3. 若是 256 字节，再用候选公钥做模幂，看是不是 PKCS#1 type-1

用法: find-base64.py <frame-stream.bin> [<pubkey.pem> ...]
"""
from __future__ import annotations

import base64
import binascii
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402

B64CHARS = set(b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=")


def main() -> int:
    frame_p = Path(sys.argv[1])
    pub_paths = [Path(x) for x in sys.argv[2:]]

    keys = {}
    if pub_paths:
        from cryptography.hazmat.primitives import serialization
        for p in pub_paths:
            n = serialization.load_pem_public_key(p.read_bytes()).public_numbers()
            keys[p.name] = n

    blob = frame_p.read_bytes()
    frames, _ = dnfdrive.parse_frames(blob, GAME)
    if not frames:
        print("切不出帧")
        return 1
    payload = frames[0].raw[GAME.hdr:]
    print(f"{frame_p.name}  首帧载荷 {len(payload)} B")
    printable = sum(1 for b in payload if 32 <= b < 127)
    print(f"  可打印率 {printable / len(payload):.1%}")
    print()

    # 1) 最长可打印串
    runs = re.findall(rb"[ -~]{8,}", payload)
    print(f"可打印串（≥8 B）共 {len(runs)} 段，按长度排序前 8:")
    for r in sorted(runs, key=len, reverse=True)[:8]:
        isb64 = all(c in B64CHARS for c in r)
        print(f"  len={len(r):<5} b64字符集={isb64}  {r[:80]!r}")

    # 2) 每段试 base64 解码
    print()
    print("对每段尝试 base64 解码，找 256 B 的结果:")
    found = False
    for r in sorted(runs, key=len, reverse=True):
        s = r.rstrip(b"=")
        pad = b"=" * ((4 - len(s) % 4) % 4)
        cand = s + pad
        try:
            raw = base64.b64decode(cand, validate=True)
        except (binascii.Error, ValueError):
            continue
        if len(raw) in (256, 128) or len(raw) % 16 == 0:
            print(f"  {len(r)} 字符 -> {len(raw)} B   {raw[:24].hex(' ')}")
            if len(raw) == 256:
                found = True
                for kn, nums in keys.items():
                    blk = pow(int.from_bytes(raw, "big"), nums.e, nums.n).to_bytes(256, "big")
                    t1 = blk[0] == 0 and blk[1] == 1
                    print(f"      [{kn}] 模幂后头 4 字节 {blk[:4].hex(' ')}  type-1={t1}")
                    if t1:
                        try:
                            pad_end = blk.index(b"\x00", 2)
                            msg = blk[pad_end + 1:]
                            print(f"      *** 解开! 被签内容 {len(msg)} B: {msg.hex(' ')}")
                        except ValueError:
                            pass
    if not found:
        print("\n  没找到能解成 256 B 的 base64 串")
    return 0


if __name__ == "__main__":
    sys.exit(main())
