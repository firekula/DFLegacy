#!/usr/bin/env python3
r"""nail-token-hunt.py —— 用"我们算得出来的令牌"去帧里搜，把令牌定位/否证。

## 为什么这个测试是决定性的

已知：
  * `logincon.php` L177/L185 逐字给出了令牌构造：`sprintf("%08x"+01×32+尾巴, uid)`
    再用 `openssl_private_encrypt`（裸 PKCS#1 type-1）签，base64 后返回。
  * 登录 PHP 的私钥明文就能读到（`rsa.php`）⇒ **我们能算出任意 uid 的令牌**。
  * 同账号两次登录的 403 B 载荷 **402/403 字节相同**；不同账号只有 4/403 相同
    ⇒ 载荷是**按账号确定**的 —— 这正是"对 uid 的确定性签名"的特征。

但用公钥直接扫 256 B 滑窗**没有命中**（两把公钥都试过）。所以令牌若在帧里，必然
被**某种逐字节可逆的变换**包过。本脚本就用"我们自己算出来的正确签名"当**已知明文**，
去反推那个变换：

  对每个偏移 o，取窗口 W = payload[o:o+256]，计算 W XOR S（S = 我们算的签名）。
  若结果是**单一字节重复** ⇒ 令牌在 o 处、被"异或一个常数"包着 ⇒ **变换就破了**。
  结果若不是常数，就把该偏移的"不同取值个数"记下来 —— 接近 1 的就是候选。

同时也试：直接相等、按位取反、以及 byte-wise 加/减常数。

用法: nail-token-hunt.py <frame-stream.bin> <uid> <login-privkey.pem>
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402

sys.path.insert(0, str(HERE / "driver"))
from importlib.util import module_from_spec, spec_from_file_location   # noqa: E402

_spec = spec_from_file_location("dnf_token", HERE / "driver" / "token.py")
tk = module_from_spec(_spec)
sys.modules["dnf_token"] = tk
_spec.loader.exec_module(tk)


def main() -> int:
    frame_p, uid_s, key_p = sys.argv[1], sys.argv[2], sys.argv[3]
    uid = int(uid_s)

    blob = Path(frame_p).read_bytes()
    frames, _ = dnfdrive.parse_frames(blob, GAME)
    payload = frames[0].raw[GAME.hdr:]
    print(f"帧: {Path(frame_p).name}  首帧载荷 {len(payload)} B  uid={uid}")

    key = tk.Key(key_p)
    msg = tk.sign_message(uid)
    sig = key.sign(uid)
    print(f"我们自己算的令牌（用登录 PHP 的私钥）:")
    print(f"  被签内容 : {msg.hex(' ')}")
    print(f"  签名     : {sig.hex()}")
    print(f"  公钥模数 : {key.modulus_hex[:40]}…")
    print()

    # 1) 直接出现？
    print(f"  [1] 签名是否原样出现在载荷里: {payload.find(sig) if sig in payload else '否'}")
    print(f"  [2] 取反后是否出现: "
          f"{payload.find(bytes(b ^ 0xFF for b in sig)) if bytes(b ^ 0xFF for b in sig) in payload else '否'}")

    # 2) 每个偏移：W XOR S 是否单一字节重复
    print()
    print("  [3] 逐偏移试『异或常数』变换（W XOR S 若恒定 ⇒ 破译）:")
    best = []
    n_off = max(0, len(payload) - 255)
    for o in range(n_off):
        w = payload[o:o + 256]
        x = bytes(a ^ b for a, b in zip(w, sig))
        if len(set(x)) == 1:
            print(f"      *** 命中 @偏移 {o}: W = S XOR {x[0]:#04x} ***")
        best.append((len(set(x)), o))
    best.sort()
    print(f"      试了 {n_off} 个偏移；'xor 后取值种类'最小的 5 个：")
    for kinds, o in best[:5]:
        print(f"        偏移 {o:>3}: xor 后取值种类 {kinds}/256"
              f"{'  ← 可能' if kinds < 64 else ''}")

    # 3) 逐偏移试『加常数』『减常数』
    print()
    print("  [4] 逐偏移试『加/减常数』变换:")
    for label, fn in (("加", lambda a, b: (a - b) & 0xFF),
                      ("减", lambda a, b: (a + b) & 0xFF)):
        hits = []
        for o in range(n_off):
            w = payload[o:o + 256]
            x = bytes(fn(a, b) for a, b in zip(w, sig))
            if len(set(x)) == 1:
                hits.append((o, x[0]))
        print(f"      {label}常数: {hits if hits else '无命中'}")

    print()
    print("判读：任一变换命中 ⇒ 令牌就在那 256 B 且变换已破；全无命中 ⇒")
    print("      帧里的令牌用**更强**的变换（如分组密码/流密码），或不是这把钥匙签的。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
