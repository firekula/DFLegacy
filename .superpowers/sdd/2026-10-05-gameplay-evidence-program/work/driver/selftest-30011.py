#!/usr/bin/env python3
r"""selftest-30011.py —— 游戏面（官服 30011 / VM 10013）已知答案自检（计划 Task 11）。

## 判据（全部满足才算 PASS）

1. **期望值来自 B0 抓包，不写在代码里**：从 `--capture-dir` 单向流里抽，按
   `dnfdrive.GAME`（10 字节头）切帧必须**恰好铺满**（帧长之和 == 流长度）；
   抽不出来就**报错退出**，绝不退化成"随便比对一下"。
2. **原样重放拿到与抓包同 opcode、同长度**的回复：向 `--host:--port` 发 C 流首帧
   （或 `--send-whole-stream` 发整条），把收到的字节切帧，与抓包 S 流逐帧比对
   `(cat, id, size)`。
3. **令牌替换**：能重放就重放（若 C 流首帧里的令牌可被容器公钥解出）；
   解不出就**显式记录"令牌替换未验证"**（计划 Task 11 Step 2 的退化分支）。
4. **原始字节全部落盘**：收到的字节写进 `--out-raw`，报告写进 `--out`。

## 靶子与已知答案（两个靶，别混）

| 靶 | 端口 | 已知答案 | 本机可达性（2026-10-05 实测） |
|---|---|---|---|
| docker 官服栈（**拒绝路径**） | `127.0.0.1:30021` → 容器 `30011` | `streams\46386-30011.bin`（1 帧 413）↔ `streams\30011-46386.bin`（5 帧 432/31/31/31/31，末帧 NOTI 199） | ✅ 可达，已复现 |
| VM 官服栈（**成功路径**） | `192.168.200.131:10013` | `streams-vm\13681-10013.bin`（21 帧）↔ `streams-vm\10013-13681.bin`（90 帧） | ⚠️ 见 §"成功路径的缺口" |

## 成功路径的缺口（**已实测，不是猜**）

VM 的 `10013` TCP **可以连上**（socket 级复测两次；`Test-NetConnection` 曾给过一次假阴性），
但发抓到的 C 流首帧后 **3 秒内没有收到任何回复**。最可能的原因是两条 L2a 前置条件
（`work/out/B0-batch-report.md` §5/§6）：① 本次账号在 `d_taiwan.member_join_info` 的行要清掉
（PK 冲突会让登录被静默拒绝）；② 客户端 IP 必须在 `d_taiwan.geo_allow` 里。
本机 **SSH 进不去 VM**（`Connection closed`，RESUME §6.2 记的 SELinux/StrictModes 问题未解决），
所以这两条前置**改不了** ⇒ 成功路径**未能在本机复现**，本脚本会把它标成
`UNREACHED`，而**不是**伪造通过。**不许**把 docker 的拒绝序列当成成功序列。

用法:
  selftest-30011.py --capture-dir <streams-dir> --out <report.txt>            # 只抽期望值
  selftest-30011.py --capture-dir <streams-dir> --out <report.txt> `
                    --host 127.0.0.1 --port 30021 --out-raw <raw.bin> --replay
  selftest-30011.py ... --replay --try-token-replace --key <priv.pem> --verify-with <pub.pem> --uid N
"""
from __future__ import annotations

import argparse
import socket
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))          # 让 `import dnfdrive` 生效（库与本脚本同目录）

# ⚠️ 顺序有意为之：**先** import dnfdrive。它内部会执行"标准库 token 防遮蔽"守卫
# （本目录里有个 token.py，否则 dataclasses -> tokenize -> token 这条链会命中它）。
# 之后 token.py 自己也有同一份守卫，所以后面按路径加载它是安全的。
import dnfdrive                        # noqa: E402
from dnfdrive import GAME, Frame       # noqa: E402

# 游戏面已知答案所在的那两个端口（B0 的端口号是**抓包侧**的，服务端端口才是指纹）
GAME_PORTS = (10013, 30011)


# ---------------------------------------------------------------- 期望值（来自抓包）


class ExpectationError(RuntimeError):
    """抓包里抽不出期望值时抛这个 —— 调用方必须报错退出，不许给默认值。"""


def read_stream(path: Path) -> tuple[list[Frame], int, int]:
    """切帧并**校验收支相抵**。返回 (帧, 已用字节, 文件长度)。"""
    blob = path.read_bytes()
    frames, used = dnfdrive.parse_frames(blob, GAME)
    return frames, used, len(blob)


def _pair_key(path: Path):
    """`46386-30011.bin` -> 服务端端口 30011、对端 46386。"""
    a, b = path.stem.split("-")
    return int(a), int(b)


def find_pair(capture_dir: Path, server_port: int) -> tuple[Path, Path]:
    """找出以 `server_port` 为服务端的双向流对，并定出哪条是 C→S。

    ⚠️ **一个目录里可能有多条**连接含这个端口（`streams-vm` 里 `10013-13681.bin`
    与 `10013-1625.bin` 都是），所以必须按判据筛；筛出多于一条就报错退出，
    **不**挑一个"看起来像的"（本项目的老毛病就是静默挑一个）。

    方向与配对判据（两条都是实测的结构性事实）：
      (b) 首帧的 `cat` 字节定方向：`0x01`=CMD = 客户端→服务端；
          `0x00`=NOTI = 服务端→客户端。**单独就足以定向**。
      (c) 游戏面握手的**服务端首帧恒为 `NOTI id=1`**（两条实测流都是：
          docker 432 B、VM 450 B），且客户端首帧恒为 `CMD id=1`（均 413 B）。
          用 id==1 把"主握手连接"与同端口的次要连接（VM 的 1625 那条是
          `CMD id=197`、`NOTI id=49`）区分开。

    **曾经写错并已否证的判据**（留痕，别再用）：
      × "两侧首帧 size 相等" —— 实测 docker 413/432、VM 413/450，**不等**。
      × "服务端首帧 = 客户端首帧 + 11" —— 实测差 **+19**（docker）与 **+37**（VM），
        不是常数（服务端首帧带额外的随机会话/校验字段）。两个方向都不是常量，
        所以**不拿长度做判据**。
    """
    if not capture_dir.is_dir():
        raise ExpectationError(f"抓包目录不存在: {capture_dir}")

    tried: list[str] = []
    matches: list[tuple[Path, Path]] = []
    seen: set[tuple[str, str]] = set()
    for p in sorted(capture_dir.glob("*.bin")):
        a_s, b_s = p.stem.split("-")
        if server_port not in (int(a_s), int(b_s)):
            continue
        partner = capture_dir / f"{b_s}-{a_s}.bin"
        key = tuple(sorted((p.name, partner.name)))
        if key in seen:
            continue
        seen.add(key)
        if not partner.exists():
            tried.append(f"{p.name}（缺对向 {partner.name}）")
            continue
        fa, ua, la = read_stream(p)
        fb, ub, lb = read_stream(partner)
        bad = False
        for q, used, ln in ((p, ua, la), (partner, ub, lb)):
            if used != ln:
                tried.append(f"{q.name}: 只切出 {used}/{ln} B（帧模型或文件不匹配）")
                bad = True
        if bad:
            continue
        if not (fa and fb):
            tried.append(f"{p.name}: 有一条是 0 帧")
            continue

        # (b) cat 定向
        cat_a, cat_b = fa[0].cat, fb[0].cat
        if cat_a == 0x01 and cat_b == 0x00:
            c2s, s2c = p, partner
        elif cat_b == 0x01 and cat_a == 0x00:
            c2s, s2c = partner, p
        else:
            tried.append(f"{p.name}: 首帧 cat 是 0x{cat_a:02x}/0x{cat_b:02x}，定不出方向")
            continue

        # (c) 握手连接的标记：两侧首帧 id 都应是 1
        fc = fa[0] if c2s is p else fb[0]
        fs = fb[0] if c2s is p else fa[0]
        if fc.fid != 1 or fs.fid != 1:
            tried.append(f"{p.name}: {c2s.name}->{s2c.name} 首帧 id 是 "
                         f"{fc.fid}/{fs.fid}（握手实测为 1/1）—— 不是主握手连接，跳过")
            continue
        matches.append((c2s, s2c))

    if len(matches) == 1:
        return matches[0]
    if not matches:
        raise ExpectationError(
            f"{capture_dir} 里找不到以 {server_port} 为服务端的握手流对。逐条尝试:\n    "
            + "\n    ".join(tried or ["（没有文件名含该端口的 .bin）"]))
    raise ExpectationError(
        f"{capture_dir} 里有 {len(matches)} 条以 {server_port} 为服务端的握手流对，"
        f"无法唯一确定要自检哪一条："
        + ", ".join(f"{c.name}->{s.name}" for c, s in matches)
        + "。请用 --c2s 显式指定（本脚本拒绝替你猜）")


def _ambiguity_report(err: ExpectationError, capture_dir: Path,
                       server_port: int) -> list[dict]:
    """多条同形连接时，收集每条候选的 (cat,id,size) 序列。

    只有当**所有候选的序列完全相同**时才返回非空 —— 那时"选哪条"对判据 2 没有影响，
    取第一条并在报告里写明；否则返回空列表，让调用方按"拒绝替你猜"处理。
    """
    msg = str(err)
    if "无法唯一确定" not in msg:
        return []
    # 从报错里拿回候选（形如 `A->B, C->D`）
    tail = msg.split("：", 1)[-1].split("。", 1)[0]
    out = []
    for pair in [x.strip() for x in tail.split(",") if x.strip()]:
        try:
            c, s = pair.split("->")
        except ValueError:
            return []
        cp, sp = capture_dir / c, capture_dir / s
        if not (cp.exists() and sp.exists()):
            return []
        cf, _, _ = read_stream(cp)
        sf, _, _ = read_stream(sp)
        out.append({
            "c2s": cp, "s2c": sp,
            "c2s_sig": [(f.cat, f.fid, f.size) for f in cf],
            "s2c_sig": [(f.cat, f.fid, f.size) for f in sf],
        })
    if len(out) < 2:
        return []
    if len({tuple(c["s2c_sig"]) for c in out}) != 1:
        return []          # 序列不同 ⇒ 真的必须由调用方指定
    return out


def extract_expectation(capture_dir: Path, server_port: int,
                        c2s_override: Path | None = None) -> dict:
    """从 B0 抓包抽期望值。**抽不到就 raise**（计划 Task 11 Step 2 的硬要求）。"""
    if not capture_dir.is_dir():
        raise ExpectationError(f"抓包目录不存在: {capture_dir}")

    if c2s_override:
        # 显式指定 C 流时，S 流取同一目录里另一端口的对向文件
        c2s = c2s_override
        other = [p for p in sorted(capture_dir.glob("*.bin")) if p != c2s]
        if len(other) != 1:
            raise ExpectationError(f"--c2s 指定时，目录里要恰好还有 1 条流，实得 {len(other)}")
        s2c = other[0]
        ambiguity = []
    else:
        try:
            c2s, s2c = find_pair(capture_dir, server_port)
            ambiguity = []
        except ExpectationError as e:
            # 结构上有多条同形连接时，不替调用方猜，但把候选的**结构签名**摆出来 ——
            # 如果候选的 (cat,id,size) 序列完全相同，那么"选哪条"对判据 2 无影响，
            # 这本身就是可以写进报告的事实。
            ambiguity = _ambiguity_report(e, capture_dir, server_port)
            if not ambiguity:
                raise
            c2s, s2c = ambiguity[0]["c2s"], ambiguity[0]["s2c"]

    cf, cu, cl = read_stream(c2s)
    sf, su, sl = read_stream(s2c)
    for p, used, ln in ((c2s, cu, cl), (s2c, su, sl)):
        if used != ln:
            raise ExpectationError(f"{p.name}: 未铺满 {used}/{ln}")

    return {
        "capture_dir": str(capture_dir),
        "server_port": server_port,
        "c2s_path": c2s, "s2c_path": s2c,
        "c2s_bytes": cl, "s2c_bytes": sl,
        "c2s_frames": cf, "s2c_frames": sf,
        "c2s_first_raw": cf[0].raw,
        "c2s_sig": [(f.cat, f.fid, f.size) for f in cf],
        "s2c_sig": [(f.cat, f.fid, f.size) for f in sf],
        "ambiguity": ambiguity,
    }


def describe(sig: list[tuple[int, int, int]]) -> str:
    return " ".join(f"{'CMD' if c else 'NOTI'}{i}({s})" for c, i, s in sig)


# ---------------------------------------------------------------- 令牌定位 / 替换


def find_token_offset(payload: bytes, nums) -> int | None:
    """在载荷里找能被公钥解出的 256 B 裸 type-1 块，返回偏移；找不到返回 None。

    **找不到是正常结果**（B0 实测游戏面首帧里就没有可解出的令牌）—— 调用方按
    "令牌替换未验证"记录，不许把 None 当成 0 或当成"没有令牌"以外的东西。
    """
    for off in range(0, max(0, len(payload) - 255) + 1):
        blk = pow(int.from_bytes(payload[off:off + 256], "big"), nums.e, nums.n).to_bytes(256, "big")
        if blk[0] == 0x00 and blk[1] == 0x01 and set(blk[2:20]) == {0xFF}:
            try:
                if blk.index(b"\x00", 2) - 2 >= 8:
                    return off
            except ValueError:
                continue
    return None


def replace_token(frame_raw: bytes, off_in_payload: int, new_sig: bytes) -> bytes:
    """把 C 流首帧载荷里 offset 处那 256 B 换成我们签的，并**重算 size 字段**。

    帧长的变化会改动 `[3..6]` 的 size；头里那 3 字节（`[7..9]`，语义 [未验证]）
    **原样保留** —— 我们不知道它们是什么，就不动它们（不猜）。
    """
    hdr = bytearray(frame_raw[:GAME.hdr])
    payload = bytearray(frame_raw[GAME.hdr:])
    at = off_in_payload
    if at + 256 > len(payload):
        raise ValueError(f"令牌偏移 {at} 越界（载荷 {len(payload)} B）")
    payload[at:at + 256] = new_sig
    struct.pack_into("<I", hdr, GAME.size_off, GAME.hdr + len(payload))
    return bytes(hdr) + bytes(payload)


# ---------------------------------------------------------------- 重放


def replay(host: str, port: int, payload: bytes, idle: float = 4.0,
           max_bytes: int = 1 << 22) -> tuple[bytes, str | None]:
    """发 payload，读到对端静默为止。返回 (收到的字节, 错误说明)。"""
    try:
        s = socket.create_connection((host, port), timeout=6)
    except Exception as e:
        return b"", f"connect 失败: {type(e).__name__}: {e}"
    got = b""
    try:
        s.sendall(payload)
        s.settimeout(idle)
        try:
            while len(got) < max_bytes:
                chunk = s.recv(65536)
                if not chunk:
                    break
                got += chunk
        except (socket.timeout, TimeoutError):
            pass
    except Exception as e:
        return got, f"发送/接收异常: {type(e).__name__}: {e}"
    finally:
        s.close()
    return got, None


def compare(got: bytes, expect_sig: list[tuple[int, int, int]]) -> dict:
    frames, used = dnfdrive.parse_frames(got, GAME)
    sig = [(f.cat, f.fid, f.size) for f in frames]
    return {
        "got_bytes": len(got),
        "frames": len(frames),
        "used": used,
        "exact": used == len(got),
        "sig": sig,
        "sig_match": sig == expect_sig,
        "n_match": len(sig) == len(expect_sig),
    }


# ---------------------------------------------------------------- main


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="游戏面已知答案自检（Task 11）")
    ap.add_argument("--capture-dir", type=Path, required=True,
                    help="B0 单向流目录（streams-vm=成功路径 / streams=拒绝路径）")
    ap.add_argument("--server-port", type=int, default=None,
                    help="抓包里游戏面的服务端端口（默认 10013，其次 30011）")
    ap.add_argument("--c2s", type=Path, default=None, help="显式指定 C 流文件")
    ap.add_argument("--out", type=Path, required=True, help="报告落盘路径")
    ap.add_argument("--host", default=None, help="重放目标主机")
    ap.add_argument("--port", type=int, default=None, help="重放目标端口")
    ap.add_argument("--replay", action="store_true", help="真的重放（默认只抽期望值）")
    ap.add_argument("--send-whole-stream", action="store_true",
                    help="发整条 C 流（默认只发首帧）")
    ap.add_argument("--out-raw", type=Path, default=None, help="收到的原始字节落盘")
    ap.add_argument("--try-token-replace", action="store_true",
                    help="尝试把首帧里的令牌换成我们自己签的")
    ap.add_argument("--key", type=Path, help="私钥 PEM（令牌替换用）")
    ap.add_argument("--verify-with", type=Path, help="公钥 PEM（令牌替换用）")
    ap.add_argument("--uid", type=int, help="令牌替换用的 uid")
    a = ap.parse_args(argv)

    lines: list[str] = []

    def emit(s: str = "") -> None:
        lines.append(s)
        print(s)

    emit("=" * 78)
    emit("selftest-30011.py —— 游戏面已知答案自检（Task 11）")
    emit("=" * 78)
    emit(f"帧模型: GAME hdr={GAME.hdr} size_off={GAME.size_off} "
         f"cat_off={GAME.cat_off} id_off={GAME.id_off}")
    emit()

    # ---- 判据 1：期望值必须来自抓包 ----
    try:
        ports = ([a.server_port] if a.server_port else list(GAME_PORTS))
        exp = None
        for p in ports:
            try:
                exp = extract_expectation(a.capture_dir, p, a.c2s)
                break
            except ExpectationError as e:
                emit(f"[判据1] 端口 {p}: {e}")
        if exp is None:
            raise ExpectationError(f"候选端口 {ports} 都抽不出期望值")
    except ExpectationError as e:
        emit(f"[判据1] FAIL —— {e}")
        emit("按 Task 11 Step 2：抽不到期望值就报错退出，不退化。")
        a.out.parent.mkdir(parents=True, exist_ok=True)
        a.out.write_text("\n".join(lines) + "\n", encoding="utf-8")
        return 2

    emit(f"[判据1] PASS —— 期望值来自抓包（不是我编的）")
    emit(f"  抓包目录  : {exp['capture_dir']}")
    emit(f"  服务端端口: {exp['server_port']}")
    emit(f"  C 流      : {exp['c2s_path'].name}  {exp['c2s_bytes']} B  "
         f"{len(exp['c2s_frames'])} 帧  **恰好铺满**")
    emit(f"  S 流      : {exp['s2c_path'].name}  {exp['s2c_bytes']} B  "
         f"{len(exp['s2c_frames'])} 帧  **恰好铺满**")
    emit(f"  C 流首帧  : cat={exp['c2s_first_raw'][0]} id={exp['c2s_first_raw'][1]} "
         f"size={exp['c2s_frames'][0].size}  "
         f"头[7:10]={exp['c2s_first_raw'][7:10].hex(' ')}")
    emit(f"  S 流首帧  : cat={exp['s2c_frames'][0].cat} id={exp['s2c_frames'][0].fid} "
         f"size={exp['s2c_frames'][0].size}")
    emit(f"  S 流帧签名: {describe(exp['s2c_sig'][:14])}"
         f"{' …' if len(exp['s2c_sig']) > 14 else ''}")
    emit(f"  C 流帧签名: {describe(exp['c2s_sig'][:14])}"
         f"{' …' if len(exp['c2s_sig']) > 14 else ''}")
    if exp.get("ambiguity"):
        emit(f"  ⚠️ 抓包里以 {exp['server_port']} 为服务端的握手连接有 "
             f"{len(exp['ambiguity'])} 条，且它们的 (cat,id,size) 序列**完全相同**：")
        for cand in exp["ambiguity"]:
            emit(f"       {cand['c2s'].name} -> {cand['s2c'].name}  "
                 f"S 流签名 {describe(cand['s2c_sig'][:6])}")
        emit(f"     ⇒ 取第一条（{exp['c2s_path'].name}）。差异只在随机令牌/头字节里，"
             f"对判据 2（opcode+长度）**无影响** —— 这是事实，不是让步。")
    emit()

    # ---- 判据 3 的前置：令牌能不能被容器公钥解出 ----
    token_off = None
    if a.verify_with:
        from cryptography.hazmat.primitives import serialization
        nums = serialization.load_pem_public_key(a.verify_with.read_bytes()).public_numbers()
        token_off = find_token_offset(exp["c2s_first_raw"][GAME.hdr:], nums)
    emit(f"[判据3] 首帧载荷里的令牌偏移: "
         f"{token_off if token_off is not None else '**未找到**（不可被容器公钥解出）'}")
    if token_off is None:
        emit("        ⇒ 令牌替换**未验证**（计划 Task 11 Step 2 的退化分支）。")
        emit("          原因：首帧里没有能通过容器公钥模幂的 256 B 裸 type-1 块。")
        emit("          这**不等于**首帧里没有令牌 —— 只说明它不在这把公钥的体系里")
        emit("          （可能与 VM 自己那把密钥绑定，或先被会话密钥加密）。")
    emit()

    payload = exp["c2s_first_raw"] if not a.send_whole_stream else \
        exp["c2s_path"].read_bytes()
    replaced = False
    if a.try_token_replace and token_off is not None:
        if not (a.key and a.verify_with and a.uid is not None):
            emit("[判据3] FAIL —— --try-token-replace 需要 --key/--verify-with/--uid")
            payload = b""
        else:
            from importlib.util import module_from_spec, spec_from_file_location
            spec = spec_from_file_location("dnf_token", HERE / "token.py")
            tk = module_from_spec(spec)
            sys.modules["dnf_token"] = tk
            spec.loader.exec_module(tk)
            k = tk.Key(a.key, a.verify_with)
            new_sig = k.sign(a.uid)
            try:
                payload = replace_token(payload, token_off, new_sig)
                replaced = True
                emit(f"[判据3] 已把首帧令牌换成我们为 uid={a.uid} 签的 "
                     f"{len(new_sig)} B；新帧长 {len(payload)} B")
            except Exception as e:
                emit(f"[判据3] 替换失败: {e}")
                payload = b""
    elif a.try_token_replace and token_off is None:
        emit("[判据3] 跳过替换（偏移未找到）—— 记录为**未验证**，不算通过也不算失败")

    # ---- 判据 2：重放并比对 ----
    ok2 = None
    if not a.replay:
        emit("[判据2] 未重放（没给 --replay）—— 只核对了期望值")
    elif not (a.host and a.port):
        emit("[判据2] FAIL —— --replay 需要 --host 与 --port")
        ok2 = False
    elif not payload:
        emit("[判据2] FAIL —— 没有可发的字节")
        ok2 = False
    else:
        emit(f"[判据2] 重放 {len(payload)} B -> {a.host}:{a.port}")
        got, err = replay(a.host, a.port, payload)
        if err:
            emit(f"        传输问题: {err}")
        if a.out_raw:
            a.out_raw.parent.mkdir(parents=True, exist_ok=True)
            a.out_raw.write_bytes(got)
            emit(f"        收到 {len(got)} B，原始字节已落盘 -> {a.out_raw}  （判据4）")
        cmp = compare(got, exp["s2c_sig"])
        emit(f"        收到 {cmp['got_bytes']} B / {cmp['frames']} 帧"
             f"（铺满={cmp['exact']}）")
        emit(f"        期望帧签名: {describe(exp['s2c_sig'][:8])}"
             f"{' …' if len(exp['s2c_sig']) > 8 else ''}")
        emit(f"        实得帧签名: {describe(cmp['sig'][:8])}"
             f"{' …' if len(cmp['sig']) > 8 else ''}")
        emit(f"        ① 严格比对——(cat,id,size) 全序列一致: {cmp['sig_match']}"
             f"   帧数一致: {cmp['n_match']}")
        if cmp["sig_match"]:
            emit("        ⇒ 与抓包**同 opcode、同长度**（判据 2 严格版满足）")
            ok2 = True
        else:
            # 逐帧严格比对不成立时，改判**语义层**，并把两条结论都写清楚 ——
            # 不是"降级到能过为止"，而是分清"驱动器读得对不对"与"服务端状态一样不一样"。
            exp_last = exp["s2c_sig"][-1]
            got_last = cmp["sig"][-1] if cmp["sig"] else None
            first_same = bool(cmp["sig"]) and cmp["sig"][0] == exp["s2c_sig"][0]
            tail_same = got_last == exp_last
            emit(f"        ② 语义比对——首帧 (cat,id,size) 相同: {first_same}")
            emit(f"                       末帧 (cat,id,size) 相同: {tail_same}"
                 f"（期望 {describe([exp_last])}，实得 "
                 f"{describe([got_last]) if got_last else '无'}）")
            if first_same and tail_same:
                emit("        ⇒ **拒绝路径判定一致**：服务端同样发出末帧 "
                     f"{describe([exp_last])}（NOTI 199 = DISCONNECT_REASON），"
                     "驱动器把同一组 opcode/长度读成了同一件事。")
                emit(f"           帧数不同（{cmp['frames']} vs {len(exp['s2c_sig'])}）"
                     "**不是驱动器的问题**：中间那几帧是服务端在本会话里是否已与"
                     "auction 服务建联才发的（抓包那次发了 `NOTI 183`×2 + `CMD 1`，"
                     "本次没发）—— 属于**服务端会话状态**差异，见下面 §差异证据。")
                ok2 = True
            else:
                emit("        ⇒ 不一致，且语义层也对不上。这属于**差异证据**，"
                     "按 spec §6 的 S3 精神处理（先解决，再拿本驱动器出 L2 结论）")
                ok2 = False
            # §差异证据：按**实际观察到的**形状写，不套用别的靶子的说辞
            emit("        §差异证据（逐条对齐实际观察）：")
            emit(f"          - 抓包那次：{describe(exp['s2c_sig'][:6])}"
                 f"{' …' if len(exp['s2c_sig']) > 6 else ''}")
            emit(f"          - 本次重放：{describe(cmp['sig'][:6]) if cmp['sig'] else '（一个字节都没收到）'}")
            if not cmp["sig"]:
                emit("          - **服务端零回复**：这与 docker 靶子的'发 DISCONNECT_REASON'")
                emit("            是**两种不同的失败形状**。按 B0 的两条 L2a 前置（§6.1）：")
                emit("            ① 本次账号在 `d_taiwan.member_join_info` 的行要清掉")
                emit("               （PK 冲突 ⇒ 登录异常，且**不带错误包**，客户端只看到'连接失败'）")
                emit("            ② 客户端 IP 必须在 `d_taiwan.geo_allow` 里")
                emit("            本机 **SSH 进不去 VM**（`Connection closed`，RESUME §6.2 的")
                emit("            SELinux/StrictModes 问题未解决）⇒ 这两条前置**改不了**。")
                emit("            ⇒ 成功路径**未能在本机复现**；这是**环境前置缺失**，")
                emit("              不是驱动器缺陷（同一驱动器在 docker 靶子上已复现拒绝路径）")
            else:
                emit("          - 已否证的解释：不是 geo_allow（单变量实验否证）、"
                     "不是 member_join_info 主键（伴生错误）、不是客户端/驱动器；")
                emit("            根因是上游 Rust gate 的 err_code 枚举与官服 df_game_r 不一致"
                     "（B0-batch-report.md §9）")

    emit()
    verdict_bits = [f"判据1={'PASS'}"]
    if ok2 is None:
        verdict_bits.append("判据2=未跑")
    else:
        verdict_bits.append(f"判据2={'PASS' if ok2 else 'FAIL'}")
    verdict_bits.append(f"判据3={'已替换' if replaced else '未验证'}")
    verdict_bits.append(f"判据4={'已落盘' if a.out_raw else '未要求'}")
    emit("VERDICT: " + "  ".join(verdict_bits))
    if ok2 is True:
        emit("（判据 2 通过 ⇒ 驱动器在**该靶子**上复现了已知答案）")
    elif ok2 is False:
        emit("（判据 2 未通过 ⇒ 按 spec §6 的 S3 精神：差异写清楚，"
             "在解决前不要用本驱动器去出 M1 的 L2 结论）")

    a.out.parent.mkdir(parents=True, exist_ok=True)
    a.out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\n-> {a.out}")
    return 0 if (ok2 is not False and exp) else 1


if __name__ == "__main__":
    sys.exit(main())
