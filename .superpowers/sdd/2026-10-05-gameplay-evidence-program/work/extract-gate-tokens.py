#!/usr/bin/env python3
r"""extract-gate-tokens.py — 从 5505 抓包里重新推导出登录令牌（**回归基准的唯一来源**）。

为什么必须有这个脚本：Task 10 要求"我们签出来的令牌与**抓到的**三条令牌同形"。
如果那三条令牌是按上一轮脚本里的**硬编码常量**来比，那就是转述而非证据（违反
`.recon/onboarding.md` §4）。这里让它们从原始字节可复算，并且可证伪。

来源链（每一环都有出处；最后一环由本脚本完成）：
  1. `$W\captures\e2e-login.pcap` —— [抓包]；单向流由 `$W\pcap-streams.py` 切出
  2. `streams5505\5505-<cport>.bin` = HTTP 响应方向（按 Content-Length 切报文；
     **不要**用 regex —— 上一版 `decrypt-gate-rpc.py` 的 regex 把 `<b64>==HTTP` 一起吃进去，
     导致"全部静默失败"，见 `gate-rpc-decode.py` 的 docstring）
  3. body = base64 → raw = `nonce12 | ct | tag16`，AES-256-GCM
  4. 密钥 `GATE_AES_KEY` —— 出处：容器 14 个 neople 进程的 `/proc/<pid>/environ`
     （`$W\proc-cmdline-cwd-environ.txt` 第 38/126/…/1182 行，14 处相同）
  5. 明文 = `0|<base64>`；`<base64>` 解出 256 B = RSA-2048 签名
  6. 用公钥做模幂（s^e mod n）→ PKCS#1 v1.5 type-1 块 → 被签内容 46 B

用法:
  extract-gate-tokens.py <streams5505-dir> <pubkey.pem> [--json out.json]
退出码：任何一条链断掉（解不开 / 不是 type-1 / 被签长度不是 46）都返回非 0 —— 不静默降级。
"""
from __future__ import annotations

import argparse
import base64
import json
import re
import sys
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

# `gate.log` "AES-256-GCM cipher initialized" + 容器 env GATE_AES_KEY（见模块 docstring 第 4 条）
GATE_AES_KEY = bytes.fromhex(
    "a1b2c3d4e5f6789012345678901234567890abcdef0123456789abcdef012345")

MSG_LEN = 46
SIG_LEN = 256
TOKEN_RX = re.compile(r"(\d+)\|([A-Za-z0-9+/]+={0,2})\Z")


def http_messages(blob: bytes):
    """按 Content-Length 切 HTTP 报文，产出 (start_line, body)。"""
    pos, n = 0, len(blob)
    while pos < n:
        m = blob.find(b"\r\n\r\n", pos)
        sep = 4
        if m < 0:
            m = blob.find(b"\n\n", pos)
            sep = 2
        if m < 0:
            return
        head = blob[pos:m].decode("latin1")
        lines = [ln for ln in head.replace("\r\n", "\n").split("\n") if ln]
        start = lines[0] if lines else ""
        clen = 0
        for ln in lines[1:]:
            if ln.lower().startswith("content-length:"):
                clen = int(ln.split(":", 1)[1].strip())
        body_at = m + sep
        yield start, blob[body_at:body_at + clen]
        pos = body_at + clen


def open_gcm(raw: bytes):
    """`nonce12|ct|tag16`（gate 的约定；另一种排列只作廉价对照）。失败返回 (None, None)。"""
    if len(raw) < 28:
        return None, None
    for name, nonce, ct, tag in (
        ("nonce12|ct|tag16", raw[:12], raw[12:-16], raw[-16:]),
        ("tag16|nonce12|ct", raw[-12:], raw[16:-12], raw[:16]),
    ):
        try:
            return name, AESGCM(GATE_AES_KEY).decrypt(nonce, ct + tag, None)
        except Exception:
            continue
    return None, None


def recover_block(sig: bytes, nums) -> bytes:
    """公钥运算：s^e mod n。这就是"验签"本身（裸块没有摘要，所以没有可校验的摘要）。"""
    return pow(int.from_bytes(sig, "big"), nums.e, nums.n).to_bytes(len(sig), "big")


def extract(streams_dir: Path, pub_path: Path, verbose: bool = True) -> dict:
    pub = serialization.load_pem_public_key(pub_path.read_bytes())
    nums = pub.public_numbers()
    if pub.key_size // 8 != SIG_LEN:
        raise SystemExit(f"非预期的公钥长度 {pub.key_size}（期望 RSA-2048 = {SIG_LEN} B）")

    tokens: dict[int, dict] = {}
    problems: list[str] = []
    for f in sorted(streams_dir.glob("5505-*.bin")):
        cport = f.stem.split("-")[1]
        for i, (start, body) in enumerate(http_messages(f.read_bytes()), 1):
            if not body or not start.startswith("HTTP"):
                continue
            try:
                raw = base64.b64decode(body, validate=True)
            except Exception:
                continue                      # 非 base64（如裸 `127.0.0.1`）不是令牌
            layout, plain = open_gcm(raw)
            if plain is None:
                problems.append(f"{f.name} resp#{i}: {len(raw)} B 没有任何 GCM 布局能解开")
                continue
            m = TOKEN_RX.match(plain.decode("latin1"))
            if not m:
                continue                      # 明文不是 `<n>|<b64>`（如 success / wrong_credentials）
            sig = base64.b64decode(m.group(2))
            if len(sig) != SIG_LEN:
                problems.append(f"{f.name} resp#{i}: 签名 {len(sig)} B ≠ {SIG_LEN}")
                continue
            blk = recover_block(sig, nums)
            if not (blk[0] == 0x00 and blk[1] == 0x01):
                problems.append(f"{f.name} resp#{i}: 块头 {blk[:4].hex(' ')} 不是 type-1")
                continue
            pad = blk.index(b"\x00", 2)
            msg = blk[pad + 1:]
            if len(msg) != MSG_LEN:
                problems.append(f"{f.name} resp#{i}: 被签内容 {len(msg)} B ≠ {MSG_LEN}")
                continue
            uid = int.from_bytes(msg[:4], "big")
            rec = {
                "uid": uid,
                "prefix": int(m.group(1)),
                "base64": m.group(2),
                "sig_hex": sig.hex(),
                "msg_hex": msg.hex(" "),
                "ps_len": pad - 2,
                "source": f"{streams_dir.name}/{f.name} resp#{i}",
                "gcm_layout": layout,
            }
            if uid in tokens and tokens[uid]["base64"] != m.group(2):
                problems.append(f"uid={uid} 在抓包里有两个不同令牌（{tokens[uid]['source']} / {rec['source']}）")
            tokens.setdefault(uid, rec)
            if verbose:
                print(f"  {rec['source']:<44} uid={uid}  PS={rec['ps_len']}xFF  "
                      f"msg={len(msg)} B  prefix={rec['prefix']}")
    if problems:
        for p in problems:
            print(f"  !! {p}", file=sys.stderr)
        raise SystemExit(f"{len(problems)} 个问题导致令牌链不完整 —— 不静默降级")
    if not tokens:
        raise SystemExit(f"{streams_dir} 里没有抽出任何令牌")
    return tokens


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="从 5505 抓包重新推导登录令牌")
    ap.add_argument("streams_dir", type=Path)
    ap.add_argument("pubkey", type=Path)
    ap.add_argument("--json", type=Path, help="把结果写到 JSON（供 token.py 的回归用）")
    ap.add_argument("--quiet", action="store_true")
    a = ap.parse_args(argv)

    print(f"公钥 : {a.pubkey}  (RSA-{serialization.load_pem_public_key(a.pubkey.read_bytes()).key_size})")
    print(f"抓包 : {a.streams_dir}")
    tokens = extract(a.streams_dir, a.pubkey, verbose=not a.quiet)
    print(f"\n抽出 {len(tokens)} 条令牌，uid = {sorted(tokens)}")
    for uid, rec in sorted(tokens.items()):
        print(f"  uid={uid}: {rec['msg_hex']}")
    const = {rec["msg_hex"][12:] for rec in tokens.values()}   # 去掉前 4 字节 uid
    print(f"\n除 uid 外的 42 字节在 {len(tokens)} 条之间是否完全一致: {len(const) == 1}")
    if a.json:
        a.json.write_text(json.dumps({str(k): v for k, v in sorted(tokens.items())},
                                     indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"-> {a.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
