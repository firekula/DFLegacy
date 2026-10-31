#!/usr/bin/env python3
r"""hyp-session-key.py —— 验一个假设：游戏面首帧的载荷是用 **7001 会话密钥**加密的。

## 假设的来源

1. 游戏面 C→S 首帧载荷 403 B **整体不透明**（可打印率 38%，无 `00 00 00 xx`，
   148 个 256 B 滑窗**没有一个**在容器公钥或 VM 公钥下解成 PKCS#1 type-1）。
2. 但登录令牌明明是个 256 B 的签名 —— 它总得在某个地方过线。
3. ⇒ 推测：客户端先与 7001 建立会话（拿到会话密钥），再用它加密游戏面的登录帧。

## 怎么验（可证伪）

拿 VM 抓包里 **7001 S 流**的 `id=12` 帧正文 `[4:20)`（= 会话密钥，Task 9 已定案的做法），
去解 **游戏面 C 流首帧**的载荷，按几种常见形状试：

  a. `AES-ECB(payload)`          —— 直接解
  b. `AES-ECB(payload[1:])`      —— 跳过 1 字节前缀（id=3/id=10 那种 count 字节）
  c. `zlib(AES-ECB(payload))`    —— 先解压
  d. `AES-ECB(zlib(payload))`    —— 先解密再解压

判据：解出来的东西**可打印率显著高于 38%**，或能解出 `00 00 01` 之类的结构。
**若四种都还是高熵垃圾，假设被否证**（那就说明密钥来源不对，或者根本不是这条路）。

用法: hyp-session-key.py <7001-s2c.bin> <game-c2s.bin>
"""
from __future__ import annotations

import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import CHANNEL, GAME     # noqa: E402


def printable(b: bytes) -> float:
    return sum(1 for x in b if 32 <= x < 127) / max(1, len(b))


def show(tag: str, data: bytes) -> None:
    print(f"  {tag:<28} len={len(data):<5} 可打印率={printable(data):6.1%}")
    print(f"      前 48: {data[:48].hex(' ')}")
    asc = "".join(chr(b) if 32 <= b < 127 else "." for b in data[:64])
    print(f"      ascii: {asc}")


def main() -> int:
    s2c = Path(sys.argv[1]).read_bytes()
    c2s = Path(sys.argv[2]).read_bytes()

    cf, cused = dnfdrive.parse_frames(s2c, CHANNEL)
    print(f"7001 S 流: {len(s2c)} B, {len(cf)} 帧（铺满={cused == len(s2c)}）")
    for f in cf:
        print(f"  id={f.fid:<4} size={f.size}")
    key = None
    for f in cf:
        if f.fid == 12:
            body = f.raw[CHANNEL.hdr:]
            key = body[4:20]
            print(f"  => id=12 正文 {len(body)} B，取 [4:20) 作会话密钥: {key.hex()}")
    if key is None:
        print("没找到 id=12 帧，假设无法检验")
        return 2

    gf, gused = dnfdrive.parse_frames(c2s, GAME)
    print(f"\n游戏面 C 流: {len(c2s)} B, {len(gf)} 帧（铺满={gused == len(c2s)}）")
    payload = gf[0].raw[GAME.hdr:]
    print(f"首帧载荷 {len(payload)} B，可打印率 {printable(payload):.1%}（基线）\n")

    print("按假设试解：")
    tried = 0

    def attempt(tag: str, fn):
        nonlocal tried
        try:
            out = fn()
            tried += 1
            show(tag, out)
        except Exception as e:
            print(f"  {tag:<28} 失败: {type(e).__name__}: {e}")

    attempt("a. AES-ECB(payload)", lambda: dnfdrive.aes_ecb_decrypt(key, payload[:len(payload) // 16 * 16]))
    attempt("b. AES-ECB(payload[1:])", lambda: dnfdrive.aes_ecb_decrypt(key, payload[1:1 + (len(payload) - 1) // 16 * 16]))
    attempt("c. zlib(AES(payload[:256]))", lambda: zlib.decompress(dnfdrive.aes_ecb_decrypt(key, payload[:256])))
    attempt("d. AES(zlib(payload))", lambda: dnfdrive.aes_ecb_decrypt(key, zlib.decompress(payload)))

    print(f"\n共试 {tried} 种；若全部仍是高熵（可打印率 ≈ 30–45%），假设被否证。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
