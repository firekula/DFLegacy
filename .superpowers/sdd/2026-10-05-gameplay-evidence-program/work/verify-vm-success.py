#!/usr/bin/env python3
r"""verify-vm-success.py —— 把"VM 成功路径复现"这件事做成可判定的结果（Task 11 补完）。

## 背景：Task 11 原本只做到"拒绝路径 PASS"

Task 11 交付时，VM 的成功路径**未能复现**，原因是当时缺一个在本机有效的账号令牌
（B0 抓的那份属于另一套库）。2026-10-05 晚，本会话打通 SSH + DB 之后：
  1. 确认 VM 的官服栈在跑、游戏面在 10013；
  2. 确认真实账号与口令（`firekula` / UID 18000008）；
  3. 请所有者用启动器登录一次，在 **VM 上**用 tcpdump 抓到活账号的完整登录
     （C→S 1141 B/21 帧，S→C 46174 B/90 帧）；
  4. **原样重放** C→S 首帧。

## 本脚本判什么

判据（全部满足才算 PASS）：
  ① 抓包侧两条流按 `GAME`(10 字节头) 切帧**恰好铺满**；
  ② 重放回复的 `(cat,id,size)` 序列是抓包 S 流序列的**前缀**，且**至少覆盖到 `CMD 1`**
     （`CMD 1` = LOGIN 的应答，长度 95 = 成功；31 = 失败/拒绝）；
  ③ 重放回复里 `CMD 1` 的长度必须是 **95**（成功），不是 31。

③ 是关键：单纯"序列对上"可能是**拒绝路径**（`NOTI1 NOTI183 NOTI183 CMD1(31) NOTI199`）。
   拒绝路径的前 4 帧与成功路径**完全相同**，只有 `CMD 1` 的长度把它区分开。
   —— 这条是踩过坑才写下来的：我第一次空场重放拿到 8 帧就以为成功了，
      必须核对 `CMD 1` 的长度才能区分。

用法: verify-vm-success.py <c2s.bin> <s2c.bin> <replay-raw.bin> <out.txt>
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / "driver"))

import dnfdrive                        # noqa: E402
from dnfdrive import GAME              # noqa: E402

LOGIN_SUCCESS_LEN = 95
LOGIN_REJECT_LEN = 31


def sig(frames):
    return [(f.cat, f.fid, f.size) for f in frames]


def describe(s, limit=14):
    out = []
    for c, i, sz in s[:limit]:
        kind = "CMD" if c == 1 else ("NOTI" if c == 0 else f"?{c:02x}")
        out.append(f"{kind}{i}({sz})")
    return " ".join(out) + (" …" if len(s) > limit else "")


def main() -> int:
    c2s_p, s2c_p, rep_p, out_p = (Path(a) for a in sys.argv[1:5])
    lines: list[str] = []

    def emit(s: str = "") -> None:
        lines.append(s)
        print(s)

    emit("=" * 78)
    emit("VM 成功路径复现 —— 判定（Task 11 补完）")
    emit("=" * 78)

    c2s = c2s_p.read_bytes()
    s2c = s2c_p.read_bytes()
    rep = rep_p.read_bytes()

    cf, cu = dnfdrive.parse_frames(c2s, GAME)
    sf, su = dnfdrive.parse_frames(s2c, GAME)
    rf, ru = dnfdrive.parse_frames(rep, GAME)

    ok = True
    emit(f"抓包 C→S : {c2s_p.name}  {len(c2s)} B  {len(cf)} 帧  铺满={cu == len(c2s)}")
    emit(f"抓包 S→C : {s2c_p.name}  {len(s2c)} B  {len(sf)} 帧  铺满={su == len(s2c)}")
    emit(f"重放回复 : {rep_p.name}  {len(rep)} B  {len(rf)} 帧  铺满={ru == len(rep)}")
    ok &= (cu == len(c2s)) and (su == len(s2c)) and (ru == len(rep))
    emit(f"① 三条流都恰好铺满: {ok}")
    emit()

    cs, ss, rs = sig(cf), sig(sf), sig(rf)
    emit(f"抓包 C→S 首帧: {describe(cs, 3)}")
    emit(f"抓包 S→C 序列: {describe(ss)}")
    emit(f"重放回复序列 : {describe(rs)}")
    emit()

    prefix_ok = rs == ss[:len(rs)]
    emit(f"② 重放序列是否为抓包序列的前缀: {prefix_ok}（重放 {len(rs)} 帧 / 抓包 {len(ss)} 帧）")
    ok &= prefix_ok

    login = [(c, i, sz) for (c, i, sz) in rs if c == 1 and i == 1]
    emit(f"   重放里有 CMD 1: {bool(login)}  长度={[x[2] for x in login]}")
    covers = any(sz >= 90 for _c, _i, sz in login)
    emit(f"   覆盖到 LOGIN 应答（≥90 B）: {covers}")

    if login:
        sz = login[0][2]
        emit(f"③ CMD 1 长度 = {sz}"
             f"  -> {'LOGIN 成功' if sz == LOGIN_SUCCESS_LEN else 'LOGIN 失败/拒绝' if sz == LOGIN_REJECT_LEN else '未知'}")
        ok &= (sz == LOGIN_SUCCESS_LEN)
    else:
        emit("③ 没有 CMD 1 —— 无法判定")
        ok = False

    emit()
    emit("附：拒绝路径长什么样（同一序列的前 4 帧与成功路径相同，靠 CMD 1 长度区分）")
    emit("   拒绝: NOTI1(450) NOTI183(31) NOTI183(31) CMD1(31) NOTI199(31)")
    emit("   成功: NOTI1(450) NOTI183(31) NOTI183(31) NOTI160(23) NOTI173(703) CMD1(95) …")
    emit()
    emit(f"VERDICT: {'PASS —— VM 成功路径已在已知答案上复现' if ok else 'FAIL'}")
    out_p.parent.mkdir(parents=True, exist_ok=True)
    out_p.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\n-> {out_p}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
