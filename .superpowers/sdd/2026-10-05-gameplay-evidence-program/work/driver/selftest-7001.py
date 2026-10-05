#!/usr/bin/env python3
"""selftest-7001.py —— 7001（登录/频道层）已知答案自检 = **M1 的硬门 S3**。

判据（全部满足才算过；任一不满足 ⇒ 触发 spec §6 的 **S3**：冻结所有 L2 结论，先修驱动器）：

  ① **结构性不变**：帧数相同、`(cat,id)` 序列相同、且**解密后的明文长度**相同
     （`id=10` → 13312 B；`id=3` → 128 B）。**更强的一条**：`id=10` 的明文**逐字节相同**。
  ② `id=3` 帧：正文 → zlib → AES-128-ECB 解密 = **128 B**，且首 4 字节 = `01 00 00 00`
  ③ `id=12` 帧：正文首 `int32 status` = **0**

⚠️ **判据① 与计划的差别（带证据，不是为了让判据通过而放松）**：
  计划 Task 9 Step 4 写的是"四个回复帧的**整帧长度**分别等于 47/43/7239/139"。
  实测（2026-10-05，同一靶子 docker 容器，用 B0 记录的 C 流原样重放）：
      replay : 7469 B, 帧长 [47, 43, **7240**, **139**]
      capture: 7465 B, 帧长 [47, 43, **7235**, **140**]
  差 4 字节，**不是驱动器错了**，而是：
    * 会话密钥 = 登录时刻（replay 20261005232132 vs capture 20261005200808）
    * ⇒ `id=10`/`id=3` 的 AES 密文不同 ⇒ `zlib(密文)` 的**压缩长度**随之差几字节
    * 但两者**解密后的明文长度完全相同**，且 `id=10` 的 13312 B 明文**逐字节相同**
      （`[dungeon]` / `channel_info_dname_*` 频道表），`id=3` 首 4 字节同为 `01 00 00 00`
  所以"整帧长度恒定"这个假设不成立；把它降级为**信息行**（下面照旧打印，便于对照），
  判据改为上头的结构性不变 + 明文一致性。

期望值**不写死在代码里**，而是从同一靶子的记录抓包里取（`<capture-s.bin>`）——
这样"判据"与"我编的期望值"能分开。两个文件都必须是**单向流**（pcap-streams.py 的产物）。

⚠️ 另两处偏移与计划的差异（都在 `dnfdrive.py` 里有证据，见其 docstring）：
  * 会话密钥 = `id=12` 正文 **`[4:20)`**（计划的 `[5:21)` 实测为错）
  * `id=3`/`id=10` 正文 = `zlib(AES(明文))`，**无 `count(1)` 前缀**

用法:
  selftest-7001.py --verify <replay-s.bin> <capture-s.bin> <out.txt>
"""
import argparse
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from dnfdrive import (CHANNEL, aes_ecb_decrypt, body_of, parse_frames,  # noqa: E402
                      session_key_from, zlib_unwrap)


def describe(tag, buf):
    frames, used = parse_frames(buf, CHANNEL)
    sizes = [f.size for f in frames]
    return frames, sizes, (used == len(buf)), f"{tag}: {len(buf)} B, {len(frames)} 帧, " \
                                              f"恰好铺满={used == len(buf)}, 帧长={sizes}"


def check_frame(frames, fid, key, out):
    """返回 (ok, 描述列表)。"""
    lines = []
    f = next((x for x in frames if x.fid == fid), None)
    if f is None:
        return False, [f"   !! 缺 id={fid} 帧"]
    body = body_of(f, CHANNEL)
    if fid == 12:
        status = struct.unpack_from("<i", body, 0)[0]
        ok = (status == 0)
        lines.append(f"   id=12 正文 {len(body)} B, status={status}  "
                     f"-> {'PASS' if ok else 'FAIL'}（判据③ 要求 0）")
        return ok, lines
    # id=3 / id=10：zlib(AES(明文))
    try:
        ct = zlib_unwrap(body)
    except Exception as e:
        return False, [f"   !! id={fid} 正文 zlib 解压失败: {e}"]
    if len(ct) % 16:
        return False, [f"   !! id={fid} zlib->{len(ct)} B，不是 16 的整数倍，无法 AES 解密"]
    pt = aes_ecb_decrypt(key, ct)
    ok = (fid != 3) or (len(pt) == 128 and pt[:4] == b"\x01\x00\x00\x00")
    lines.append(f"   id={fid} 正文 {len(body)} B -> zlib {len(ct)} B -> AES {len(pt)} B, "
                 f"首4B={pt[:4].hex(' ')}  -> {'PASS' if ok else 'FAIL'}"
                 + ("（判据② 要求 128 B 且 01 00 00 00）" if fid == 3 else ""))
    if fid != 3 and len(pt) > 0:
        head = pt[:48].split(b"\n")[0]
        lines.append(f"      明文首行: {head!r}")
    return ok, lines


def plaintext_of(frames, fid, key):
    """解出 id=10 / id=3 的明文（zlib(AES(明文))）。失败返回 None。"""
    f = next((x for x in frames if x.fid == fid), None)
    if f is None or key is None:
        return None
    try:
        return aes_ecb_decrypt(key, zlib_unwrap(body_of(f, CHANNEL)))
    except Exception:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--verify", nargs=3, metavar=("REPLAY_S", "CAPTURE_S", "OUT"),
                    required=True)
    a = ap.parse_args()
    replay_p, capture_p, out_p = a.verify

    got = Path(replay_p).read_bytes()
    want = Path(capture_p).read_bytes()
    L = ["7001 已知答案自检（M1 硬门 S3）",
         f"重放回复: {replay_p}",
         f"期望来源: {capture_p}（同一靶子的记录抓包）", ""]

    rf, rs, rexact, rline = describe("replay ", got)
    wf, ws, wexact, wline = describe("capture", want)
    L += [rline, wline, ""]
    L.append("   信息行（**不是判据**，见 docstring 的证据）：整帧长度序列 "
             f"{'一致' if rs == ws else '不一致'}"
             + ("" if rs == ws else f"  （replay={rs} vs capture={ws}）")
             + " —— 会话密钥不同 ⇒ 密文不同 ⇒ zlib 压缩长度差几字节")
    if not (rexact and wexact):
        L.append("   ⚠️ 有一侧没有恰好铺满 —— 可能收到截断/多余字节")

    key_r = session_key_from(rf, CHANNEL)
    key_w = session_key_from(wf, CHANNEL)
    L.append(f"   会话密钥（重放 / 记录）= {key_r.hex() if key_r else None} / "
             f"{key_w.hex() if key_w else None}")
    L.append("")

    # ---- 判据① 结构性不变 + 明文一致性
    seq_r = [(f.cat, f.fid) for f in rf]
    seq_w = [(f.cat, f.fid) for f in wf]
    pt10_r, pt10_w = plaintext_of(rf, 10, key_r), plaintext_of(wf, 10, key_w)
    pt3_r, pt3_w = plaintext_of(rf, 3, key_r), plaintext_of(wf, 3, key_w)
    c1_seq = (seq_r == seq_w) and bool(seq_w)
    c1_pt_len = (pt10_r is not None and pt10_w is not None and len(pt10_r) == len(pt10_w)
                 and pt3_r is not None and pt3_w is not None and len(pt3_r) == len(pt3_w))
    c1_pt_same = (pt10_r is not None and pt10_r == pt10_w)
    L.append(f"判据① 结构性不变:")
    L.append(f"   (cat,id) 序列: replay={seq_r} capture={seq_w} -> "
             f"{'一致' if c1_seq else '不一致'}")
    L.append(f"   解密明文长度: id=10 {len(pt10_r) if pt10_r else None} vs "
             f"{len(pt10_w) if pt10_w else None}；id=3 {len(pt3_r) if pt3_r else None} vs "
             f"{len(pt3_w) if pt3_w else None} -> {'一致' if c1_pt_len else '不一致'}")
    L.append(f"   （更强）id=10 明文逐字节相同: {c1_pt_same}")
    ok1 = c1_seq and c1_pt_len and c1_pt_same
    L.append(f"   => 判据① {'PASS' if ok1 else 'FAIL'}")
    L.append("")

    ok2 = ok3 = False
    if key_r:
        ok3, lines = check_frame(rf, 12, key_r, L)
        L += lines
        ok2, lines = check_frame(rf, 3, key_r, L)
        L += lines
    else:
        L.append("   !! 重放回复里没有 id=12 帧，拿不到会话密钥 -> 判据②③无法判定（记 FAIL）")

    verdict = ok1 and ok2 and ok3
    L += ["", f"VERDICT: {'PASS' if verdict else 'FAIL -> S3（冻结所有 L2 结论，先修驱动器）'}"]
    Path(out_p).write_text("\n".join(L) + "\n", encoding="utf-8")
    print("\n".join(L))
    return 0 if verdict else 1


if __name__ == "__main__":
    sys.exit(main())
