#!/usr/bin/env python3
r"""token.py —— 官服游戏面登录令牌的签发 / 验签（Task 10 的产物）。

## 令牌是什么（结论 + 证据）

令牌 = **裸 PKCS#1 v1.5 type-1 块**的 RSA-2048 签名（256 B），即 `rsa` crate 的
`Pkcs1v15Sign::new_unprefixed()`：没有 DigestInfo 前缀，所以用公钥做模幂就能把被签内容
**完整读出来** —— 这正是"它是什么"能被证伪的原因。

    块 = 00 01 | FF × 207 | 00 | 被签内容(46 B)
    被签内容 = uid(4 B, 大端) | 01 × 32 | 55 91 45 10 | 01 04 03 03 01 01

## 证据

* `[抓包]` **三条令牌互证**（uid 1/2/3）：来源链可复算 ——
  `$W\captures\e2e-login.pcap` → `pcap-streams.py` 切出 `streams5505\*.bin`
  → HTTP body base64 → `nonce12|ct|tag16` AES-256-GCM（密钥 `GATE_AES_KEY`，出自容器
  14 个进程的 `/proc/<pid>/environ`）→ 明文 `0|<base64>` → 公钥模幂 → 46 B。
  复算命令见 `work\extract-gate-tokens.py`；**本模块不硬编码任何令牌**，
  以免"同形"退化成与常量比。
* `[静态]` 密钥：`$W\container-privatekey.pem` 的模数 == `$W\container-game-publickey.pem`
  （`work\rsa-pem-modulus.py`）⇒ 用容器私钥签出来的东西，官方游戏服按同一对密钥验得过。
* **实测修正**：46 B 里 `01` 的个数是 **32**（不是 34），后接 10 B 常量
  `55 91 45 10 01 04 03 03 01 01`。复算：
  `python work\token.py --check --streams <streams5505> --key ... --verify-with ...`
* `[推断]` 除 uid 外那 42 字节的**语义仍未解**（像是版本/标志位）。本模块只保证
  **与抓到的令牌逐字节可比**，不声称知道它们是什么意思。

## 独立校验（为什么不能只自签自验）

只用本模块自签自验是**循环论证**：同一处 bug 在签/验两侧同时成立时自检照样 PASS。
所以 `--openssl-check` 会额外让 **Git Bash 里那份 OpenSSL 3.5.8**（与 PyCryptodome
完全不同的代码基）用**公钥**做一次模幂运算，把我方签名恢复成块，再核对块结构 ——
这才让"我们真的能用这把私钥签出该公钥认的签名"变成第三方结论。

用法:
    token.py --sign-uid 1 --key <priv.pem> --verify-with <pub.pem> [--out f.json]
    token.py --check --streams <streams5505-dir> --key <priv.pem> --verify-with <pub.pem> --out <report.txt>
    token.py --verify-sig <sig.hex|@file> --uid 1 --verify-with <pub.pem>
"""
from __future__ import annotations

# ---------------------------------------------------------------------------
# 坑：本文件叫 `token.py`（计划 Task 10 指定的路径），而 Python 标准库里有 `token`。
# 以脚本方式跑时，解释器会把本文件所在目录放到 sys.path[0]，于是标准库链
# `cryptography -> dataclasses -> inspect -> linecache -> tokenize -> token`
# 会解析到**本文件**（它此时只在 sys.modules 里叫 `__main__`，不叫 `token`），
# 报 `ImportError: cannot import name 'EXACT_TOKEN_TYPES' from 'token'`。
#
# **顺序是关键**：下文先摘掉本目录、再 import，三步顺序由三组对照实测
# （不装守卫 -> FAIL；摘路径前 import -> 守卫自己被遮蔽 -> FAIL；先摘再导 -> OK）。
# 同一守卫也放在 `dnfdrive.py` 里（它是 driver/ 下的共同导入点）——
# 本文件保留一份是为了**不依赖** dnfdrive 也能单独跑。
import os as _os
import sys as _sys

_HERE = _os.path.dirname(_os.path.abspath(__file__))
_sys.path[:] = [p for p in _sys.path
                if p and _os.path.abspath(p) != _HERE]
import token as _stdlib_token           # noqa: E402
_sys.modules.setdefault("token", _stdlib_token)
del _os, _sys, _HERE
# ---------------------------------------------------------------------------

import argparse
import base64
import json
import re
import subprocess
import sys
from pathlib import Path

from Crypto.PublicKey import RSA
from cryptography.hazmat.primitives import serialization

# ---------------------------------------------------------------- 格式常量（都来自实测）

SIG_LEN = 256          # RSA-2048
PS_LEN = 207           # type-1 填充的 0xFF 字节数：256 - 3(00 01 00) - 46
MSG_LEN = 46
UID_LEN = 4
FLAG_RUN = b"\x01" * 32            # 实测 32 个 0x01（曾误读为 34，见 §"实测修正"）
TRAILER = bytes.fromhex("55 91 45 10 01 04 03 03 01 01")   # 10 B，语义 [未验证]
assert UID_LEN + len(FLAG_RUN) + len(TRAILER) == MSG_LEN
assert 3 + PS_LEN + MSG_LEN == SIG_LEN

BASH = r"D:\Program Files\Git\bin\bash.exe"
TOKEN_RX = re.compile(r"(\d+)\|([A-Za-z0-9+/]+={0,2})\Z")


# ---------------------------------------------------------------- 被签内容 / 填充


def sign_message(uid: int) -> bytes:
    """构造被签的 46 字节：uid 大端 | 01×32 | 10 B 常量。"""
    if not 0 <= uid <= 0xFFFFFFFF:
        raise ValueError(f"uid 超出 4 字节: {uid}")
    return uid.to_bytes(UID_LEN, "big") + FLAG_RUN + TRAILER


def emsa_pkcs1_type1(msg: bytes, k: int = SIG_LEN) -> bytes:
    """裸 PKCS#1 v1.5 **type-1** 编码块（签名用），无 DigestInfo。

    与标准 `EMSA-PKCS1-v1_5` 的差别只有一点：那里 DigestInfo 跟在 `00` 后面，
    这里跟的就是被签内容本身（`new_unprefixed`）。
    """
    if len(msg) > k - 11:
        raise ValueError(f"被签内容 {len(msg)} B 超过 k-11 = {k - 11}")
    ps = b"\xff" * (k - 3 - len(msg))
    return b"\x00\x01" + ps + b"\x00" + msg


def emsa_decode_type1(block: bytes) -> bytes:
    """从块里取回被签内容。**严格**：形状不对就抛异常，不返回"差不多"的东西。"""
    if len(block) != SIG_LEN:
        raise ValueError(f"块长度 {len(block)} ≠ {SIG_LEN}")
    if block[0] != 0x00 or block[1] != 0x01:
        raise ValueError(f"块头 {block[:2].hex(' ')} 不是 PKCS#1 type-1")
    pad = block.find(b"\x00", 2)
    if pad < 0:
        raise ValueError("type-1 块里找不到分隔用的 00")
    if set(block[2:pad]) != {0xFF}:
        raise ValueError("填充段不是纯 0xFF")
    if pad - 2 < 8:
        raise ValueError(f"填充段只有 {pad - 2} 字节（PKCS#1 要求 ≥ 8）")
    return block[pad + 1:]


# ---------------------------------------------------------------- 密钥


class Key:
    """私钥（签发）+ 公钥（验签）。两者**独立载入**：私钥文件坏掉时验签仍然可信。"""

    def __init__(self, priv_path: str | Path | None = None,
                 pub_path: str | Path | None = None):
        self.priv = RSA.import_key(Path(priv_path).read_bytes()) if priv_path else None
        if pub_path:
            self.pub = serialization.load_pem_public_key(Path(pub_path).read_bytes())
        elif self.priv is not None:
            self.pub = serialization.load_pem_public_key(
                self.priv.public_key().export_key("PEM"))
        else:
            self.pub = None
        if self.pub is not None and self.pub.key_size != SIG_LEN * 8:
            raise SystemExit(f"公钥是 RSA-{self.pub.key_size}，本模块只处理 RSA-2048")
        self._nums = self.pub.public_numbers() if self.pub is not None else None

    # -- 签发 ---------------------------------------------------------------
    def sign(self, uid: int) -> bytes:
        """返回 256 B 签名。私钥运算走 `pow(em, d, n)`（教科书 RSA，无 CRT 加速；
        2048 位一次约几毫秒，够用且**没有库把填充再包一层**的风险）。"""
        if self.priv is None:
            raise ValueError("没有私钥，无法签发")
        em = emsa_pkcs1_type1(sign_message(uid))
        s = pow(int.from_bytes(em, "big"), self.priv.d, self.priv.n)
        return s.to_bytes(SIG_LEN, "big")

    # -- 验签 ---------------------------------------------------------------
    def public_op(self, sig: bytes) -> bytes:
        """公钥运算 s^e mod n —— 这就是验签（裸块没有摘要，可校验的就是块本身）。"""
        if self._nums is None:
            raise ValueError("没有公钥")
        if len(sig) != SIG_LEN:
            raise ValueError(f"签名 {len(sig)} B ≠ {SIG_LEN}")
        return pow(int.from_bytes(sig, "big"), self._nums.e, self._nums.n).to_bytes(SIG_LEN, "big")

    def verify(self, sig: bytes, uid: int) -> bool:
        """签名是否等于"uid 的那 46 字节"的合法签名。任何形状问题都返回 False（不抛）。"""
        try:
            return emsa_decode_type1(self.public_op(sig)) == sign_message(uid)
        except Exception:
            return False

    # -- 让密钥可核对 ---------------------------------------------------------
    @property
    def modulus_hex(self) -> str:
        return hex(self._nums.n)


# ---------------------------------------------------------------- 独立校验（OpenSSL）


def _posix(p: Path) -> str:
    s = str(p.resolve()).replace("\\", "/")
    return "/" + s[0].lower() + s[2:]


def openssl_verifyrecover(sig: bytes, pub_path: Path, tmpdir: Path) -> tuple[bool, bytes, str]:
    """让 Git Bash 的 OpenSSL 用公钥把签名恢复成**被签内容**。返回 (是否成功, 内容, 说明)。

    实测形状（2026-10-05，`work/out/_tmp-openssl-probe.sh`）：`pkeyutl -verifyrecover`
    对裸 type-1 块**会剥掉填充并只输出消息本身**（uid=1 那条 → 46 B，逐字节等于我们
    构造的内容；不是 256 B 的块）。所以这里比的是"内容"，不是"块"。
    填充格式非法时它**返回非 0 且不输出** —— 正好可以当反例判据。

    注意**不要**用 `pkeyutl -sign -rawin` 当"第三方签发"来对照：实测它会把输入**再包
    一层** PKCS#1 v1.5 + SHA-256 DigestInfo（同一次实测），所以它不是裸块签发器。
    """
    if not Path(BASH).exists():
        return False, b"", f"找不到 Git Bash: {BASH}"
    sig_f = tmpdir / "sig.bin"
    sig_f.write_bytes(sig)
    r = subprocess.run(
        [BASH, "-lc",
         f'openssl pkeyutl -verifyrecover -pubin -inkey "{_posix(pub_path)}" '
         f'-in "{_posix(sig_f)}"'],
        capture_output=True)
    if r.returncode != 0:
        return False, r.stdout, f"openssl 拒绝该签名（退出码 {r.returncode}）"
    return True, r.stdout, "ok"


# ---------------------------------------------------------------- --check（离线自检）


def _load_streams_tokens(streams_dir: Path, pub_pem: Path) -> dict[int, dict]:
    """用 `extract-gate-tokens.py` 的同一套逻辑从抓包重推令牌（**不硬编码**）。"""
    from importlib.util import module_from_spec, spec_from_file_location
    helper = Path(__file__).resolve().parent.parent / "extract-gate-tokens.py"
    spec = spec_from_file_location("extract_gate_tokens", helper)
    m = module_from_spec(spec)
    spec.loader.exec_module(m)
    return m.extract(streams_dir, pub_pem, verbose=False)


def run_check(args) -> int:
    lines: list[str] = []
    ok_all = True

    def emit(s: str = "") -> None:
        lines.append(s)
        print(s)

    key = Key(args.key, args.verify_with)
    emit("=" * 74)
    emit("token.py 离线自检（Task 10）")
    emit("=" * 74)
    emit("来源与证据类别（每条结论都挂着它）：")
    emit("  [静态] 密钥：私钥 PEM 的模数 == 公钥 PEM 的模数（下面第 1 行结果即此判据）")
    emit("  [抓包] 三条真实令牌：来源链 =")
    emit("           $W\\captures\\e2e-login.pcap")
    emit("           -> pcap-streams.py 切出 streams5505\\*.bin")
    emit("           -> body base64 -> nonce12|ct|tag16 AES-256-GCM")
    emit("              （密钥 GATE_AES_KEY，出自容器 14 个进程的 /proc/<pid>/environ）")
    emit("           -> 明文 `0|<base64>` -> 公钥模幂 -> 46 B 被签内容")
    emit("         重算命令：work\\extract-gate-tokens.py（本自检的 §6 会自动调用它）")
    emit("         注意：**本模块不硬编码任何令牌**，所以'同形'不是与常量比出来的")
    emit("  [回放] §5 的 OpenSSL 校验：另一份独立实现（Git Bash 里 3.5.8）用公钥复算")
    emit("  [推断] 那 46 B 里**除 uid 外的 42 字节**（01×32 + 55 91 45 10 01 04 03 03 01 01）")
    emit("         语义**仍未解**（像版本/标志位）。本模块只保证与抓到的令牌逐字节可比，")
    emit("         **不**声称知道它们是什么意思 —— 别在后续单元里把它们当已知语义用。")
    emit()
    emit(f"私钥        : {args.key}")
    emit(f"公钥        : {args.verify_with}")
    emit(f"模数 n      : {key.modulus_hex[:34]}…")
    priv_n = key.priv.n
    pub_n = key._nums.n
    emit(f"私钥模数 == 公钥模数 : {priv_n == pub_n}"
         f"{'' if priv_n == pub_n else '   <<< 不成立 ⇒ 后面的结论全部无效'}")
    ok_all &= (priv_n == pub_n)
    emit(f"被签内容模板 : {sign_message(1).hex(' ')}   ({MSG_LEN} B)")
    emit(f"  = uid(4,BE) | 01×{len(FLAG_RUN)} | {TRAILER.hex(' ')}")
    emit("")

    # 1) 容量自检：签名的长度与块形状
    msg1 = sign_message(args.sign_uid)
    em = emsa_pkcs1_type1(msg1)
    emit(f"[1] 期望块长度 {len(em)} B，前缀 {em[:4].hex(' ')}，填充 {em[2:2 + PS_LEN][:1].hex()}×{PS_LEN}")
    ok1 = len(em) == SIG_LEN and em[0] == 0 and em[1] == 1 and set(em[2:2 + PS_LEN]) == {0xFF}
    emit(f"    {'PASS' if ok1 else 'FAIL'}")
    ok_all &= ok1

    # 2) 签发（我们自己的实现）
    import tempfile
    sig = key.sign(args.sign_uid)
    emit(f"[2] 签发 uid={args.sign_uid}: 签名 {len(sig)} B  "
         f"head={sig[:8].hex(' ')}…")
    ok2 = len(sig) == SIG_LEN
    emit(f"    签名长度为 {SIG_LEN} : {'PASS' if ok2 else 'FAIL'}")
    ok_all &= ok2

    # 3) 用公钥验签 + 核形状
    block = key.public_op(sig)
    emit(f"[3] 公钥模幂后块头: {block[:16].hex(' ')}")
    ok3a = block[:2] == b"\x00\x01"
    recovered = None
    try:
        recovered = emsa_decode_type1(block)
    except Exception as e:
        emit(f"    取被签内容失败: {e}")
    ok3b = recovered == msg1
    emit(f"    前两字节 00 01            : {'PASS' if ok3a else 'FAIL'}")
    emit(f"    取回内容 == 构造的 46 B   : {'PASS' if ok3b else 'FAIL'}")
    if recovered is not None:
        emit(f"    取回内容 uid 字段(BE)     : {int.from_bytes(recovered[:4], 'big')}")
    ok_all &= (ok3a and ok3b)

    # 4) 反向：错误的 uid 必须验不过（否则 verify() 是恒真函数）
    ok4 = (not key.verify(sig, args.sign_uid + 1)) and key.verify(sig, args.sign_uid)
    emit(f"[4] 反例：verify(sig, uid={args.sign_uid}) = True, "
         f"verify(sig, uid={args.sign_uid + 1}) = False  : {'PASS' if ok4 else 'FAIL'}")
    ok_all &= ok4

    # 5) 独立实现校验（OpenSSL，另一份代码基）+ 反例
    with tempfile.TemporaryDirectory() as td:
        good, osl, why = openssl_verifyrecover(sig, Path(args.verify_with), Path(td))
        # 反例：把签名翻一位，OpenSSL 必须拒绝（否则它什么都没验）
        broken = bytes([sig[0] ^ 0x01]) + sig[1:]
        n_good, _, _ = openssl_verifyrecover(broken, Path(args.verify_with), Path(td))
    if good:
        emit(f"[5] OpenSSL(独立实现) 恢复出的被签内容: {osl.hex(' ')}")
        emit(f"    长度 {len(osl)} B（实测：verifyrecover 会剥掉填充，只给内容）")
        ok5 = (osl == msg1)
        emit(f"    OpenSSL 取回内容 == 我们的 46 B : {'PASS' if ok5 else 'FAIL'}")
    else:
        ok5 = False
        emit(f"[5] OpenSSL 校验未能执行: {why}")
    emit(f"    反例：翻转签名 1 bit 后 OpenSSL {'拒绝（PASS）' if not n_good else '竟然还接受（FAIL）'}")
    ok5 &= (not n_good)
    ok_all &= ok5

    # 6) 与抓到的令牌同形（回归基准从抓包重推，不是硬编码常量）
    if args.streams:
        emit("")
        emit(f"[6] 与抓到的令牌回归（重推自 {args.streams}）")
        try:
            caught = _load_streams_tokens(Path(args.streams), Path(args.verify_with))
        except SystemExit as e:
            caught = {}
            emit(f"    抓包重推失败: {e}")
        if not caught:
            emit("    FAIL —— 抽不到令牌；按 Task 10 Step 3 的要求，这属于必须报错的情形")
            ok_all = False
        for uid, rec in sorted(caught.items()):
            caught_sig = base64.b64decode(rec["base64"])
            shape = {
                "len": len(caught_sig) == SIG_LEN,
                "type1": key.public_op(caught_sig)[:2] == b"\x00\x01",
                "msg": key.verify(caught_sig, uid),
            }
            const_tail_same = (
                emsa_decode_type1(key.public_op(caught_sig))[UID_LEN:]
                == sign_message(uid)[UID_LEN:])
            emit(f"    uid={uid}  ({rec['source']})")
            emit(f"      抓到的签名 {len(caught_sig)} B; type-1={shape['type1']}; "
                 f"被签内容 == 我们为该 uid 构造的 46 B: {shape['msg']}")
            emit(f"      除 uid 外的 42 字节与模板一致: {const_tail_same}")
            ok_all &= (shape["len"] and shape["type1"] and shape["msg"] and const_tail_same)
    else:
        emit("[6] 跳过抓包回归（未给 --streams）")

    emit("")
    verdict = "PASS" if ok_all else "FAIL"
    emit(f"VERDICT: {verdict}")
    if args.out:
        Path(args.out).parent.mkdir(parents=True, exist_ok=True)
        Path(args.out).write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"\n-> {args.out}")
    return 0 if ok_all else 1


# ---------------------------------------------------------------- main


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="官服游戏面登录令牌：签发 / 验签")
    ap.add_argument("--sign-uid", type=int, help="签发该 uid 的令牌（打印 hex）")
    ap.add_argument("--verify-sig", help="验签：hex 字符串或 @文件")
    ap.add_argument("--check", action="store_true", help="跑离线自检")
    ap.add_argument("--streams", type=Path, help="shots 目录（streams5505），用于回归")
    ap.add_argument("--key", type=Path, help="私钥 PEM")
    ap.add_argument("--verify-with", type=Path, help="公钥 PEM")
    ap.add_argument("--out", type=Path, help="自检报告落盘路径")
    ap.add_argument("--json", type=Path, help="签发结果落盘 JSON")
    a = ap.parse_args(argv)

    if a.check:
        if not (a.key and a.verify_with):
            ap.error("--check 需要 --key 与 --verify-with")
        return run_check(a)

    key = Key(a.key, a.verify_with)

    if a.verify_sig:
        if not a.verify_with:
            ap.error("--verify-sig 需要 --verify-with")
        raw = a.verify_sig
        sig = Path(raw[1:]).read_bytes() if raw.startswith("@") else bytes.fromhex(raw)
        uid = a.sign_uid if a.sign_uid is not None else 0
        good = key.verify(sig, uid)
        print(f"verify(sig, uid={uid}) = {good}")
        if good:
            print(f"  被签内容: {emsa_decode_type1(key.public_op(sig)).hex(' ')}")
        return 0 if good else 1

    if a.sign_uid is not None:
        sig = key.sign(a.sign_uid)
        print(sig.hex())
        print(f"# uid={a.sign_uid}  {len(sig)} B", file=sys.stderr)
        if a.out:
            # 计划的 Step 3 命令行带了 --out 但没带 --check；说明意图是"把这步落盘"。
            # 所以这里也写盘（内容 = 签名 + 自验结果），而不是静默忽略 --out。
            blk = key.public_op(sig)
            ok = key.verify(sig, a.sign_uid)
            rep = [
                "token.py —— 签发（Task 10 Step 3 落盘）",
                f"uid          : {a.sign_uid}",
                f"私钥         : {a.key}",
                f"公钥         : {a.verify_with}",
                f"被签内容     : {sign_message(a.sign_uid).hex(' ')}",
                f"签名长度     : {len(sig)} B  {'PASS' if len(sig) == SIG_LEN else 'FAIL'}",
                f"signature    : {sig.hex()}",
                f"公钥模幂块头 : {blk[:16].hex(' ')}",
                f"前 4 字节 00 01 + uid: {blk[:2].hex(' ')} / "
                f"{int.from_bytes(emsa_decode_type1(blk)[:4], 'big')}",
                f"verify(sig, uid): {ok}",
                "",
                "注意：本文件是 --sign-uid 的落盘；带判据的完整离线自检要跑 "
                "--check（见 --help）。",
            ]
            a.out.parent.mkdir(parents=True, exist_ok=True)
            a.out.write_text("\n".join(rep) + "\n", encoding="utf-8")
            print(f"-> {a.out}", file=sys.stderr)
        if a.json:
            a.json.write_text(json.dumps({
                "uid": a.sign_uid,
                "signature_hex": sig.hex(),
                "message_hex": sign_message(a.sign_uid).hex(" "),
                "sig_len": len(sig),
            }, indent=2) + "\n", encoding="utf-8")
        return 0

    ap.print_help()
    return 2


if __name__ == "__main__":
    sys.exit(main())
