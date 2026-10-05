#!/usr/bin/env python3
"""dnfdrive.py —— 官服 DNF 客户端驱动器：帧层 + 密码层。

**两套帧头**（这是本文件最重要的设计点；来源与证据写在每一套下面，不做自动嗅探，
调用方按"打哪个端口"显式选模型 —— spec §4.2 明确"不许让一个驱动器同时说两代"、
"不许把世代差异藏进代码里"）：

  CHANNEL (11 字节) —— 用于 7001（登录/频道层）
    [0] category   [1] id
    [2..5] size    uint32 LE **含头**
    [6..9] reserved（发 0）
    [10] ackOk(1)
    证据：[静态] STATIC-FINDINGS.md §4.4 的访问器证据；[抓包] B0 实测 VM 与 docker
          两条 7001 C 流都恰好铺满（帧 43/27/11/11 = 92）、S 流（47/43/7220|7235/139|140）。

  GAME (13 字节) —— 用于游戏面（docker 30011 / VM 10013）
    [0]    classification(1)   0x01=CMD / 0x00=NOTI
    [1]    msg_no(1)           opcode 号
    [2]    garvage(1)          官方字段名就是 garvage（拼写如此）；实测取值 0/1/2
    [3..6] sLength(4)          uint32 LE **含整帧**
    [7..10] checksum(4)        4 字节校验（算法仍未解，见下）
    [11..12] sequence(2)       uint16 LE **递增序号**（C→S 实测从 0 起、逐帧 +1，100%）

    ⚠️ **这是从 10 字节改成 13 字节的（2026-10-05 晚），判据有两条，互相独立**：
      ① **官方 cfg 白纸黑字**：`/home/neople/game/cfg/siroco11.cfg` L82–89
             header_classification = 0 1     header_msg_no  = 5 1
             header_sLength        = 1 4     //header_garvage = 6 1
             header_checksum       = 7 4     header_sequence = 11 2
             header_load_interval  = 10      header_mangled_length = **13**
         字段集合与宽度：1+1+1+4+4+2 = **13**，与客户端观测到的布局一致
         （客户端侧的字段顺序是 class,msg_no,garvage,sLength,checksum,sequence；
           官方那份 cfg 写的是内部链接的顺序，字段与宽度相同、次序不同）。
      ② **C→S 的 `[11..12]` 是完美递增序号**：四条 C→S 流实测 20/20、20/20、20/20、5/5
         逐帧 +1 起于 0。**载荷不可能是完美计数器** ⇒ 这两个字节必是头字段
         ⇒ 头至少 13 字节（`work/nail-header13.py`、`work/nail-header13-verify.py`）。

    `[作废]` 旧说法"10 字节头"。10 字节模型**切帧仍然正确**（切帧只看 `[3..6]` 的 size，
    与头长无关 —— 实测两个模型在全部 12 条流上都恰好铺满），所以那个错误**没有污染**
    任何切帧结论（B0 的"恰好铺满"、Task 11 的重放比对都仍成立）；
    它污染的只是**载荷起点**：载荷应从 13 起，不是从 10 起。

    `[7..9]` 的语义也已随之定案：它**不是**独立字段，而是 4 字节 `checksum` 的前 3 字节。
    checksum 的**算法仍未解**（adler32/crc32/sum32 在 90 帧上全 0 命中），
    但**不阻塞**：原样重放整帧可通过。

密码层（[抓包]+[静态] 既有结论）：
  * 会话密钥 = 服务端 `id=12` 帧正文里 `int32 status` 之后的前 16 字节。
    正文起点按 **所选帧头长度** 计算，不写死 11 —— 这是上一版计划里被标为"读法、需对照"的地方。
  * `id=10` / `id=3` 的正文 = `count(1) | zlib(AES-128-ECB(明文))`（既有实证脚本
    decode-official-channel-table.py 已解出过 13312 B 的频道表）。

用法（库）:
    from dnfdrive import Conn, CHANNEL, GAME, FrameError
    c = Conn("127.0.0.1", 7001, model=CHANNEL)
    c.send_frame(0, 11, payload)
    for f in c.recv_frames():
        ...
"""
from __future__ import annotations

# ---------------------------------------------------------------------------
# 目录级坑（不是某个脚本的问题）：本目录里有个 `token.py`（计划 Task 10 指定的
# 文件名），而 Python 标准库也有 `token`。只要本目录在 sys.path 上
# （以脚本方式跑本目录里的任何文件时必然如此），这条链就会命中我们的 `token.py`：
#     dataclasses -> inspect -> linecache -> tokenize -> `from token import *`
# 症状是 `ImportError: cannot import name 'dataclass' from partially initialized
# module 'dataclasses'`，看上去像循环导入，实际是文件名遮蔽。
#
# 修法（**顺序是关键**，反过来不成立）：先把本目录从 sys.path 摘掉，**再**导入 ——
# 然后就拿到标准库的 `token`，按名字钉进 sys.modules，此后谁再 import 都命中它。
# 这三步的顺序用三组对照实测过（见 work/out/driver-30011-selftest.txt 的引文）：
#   不装守卫 -> FAIL；摘路径前的 import -> 守卫自己也被遮蔽 -> FAIL；先摘再导 -> OK。
# 本模块是 driver/ 下所有使用者的**共同**导入点，所以守卫放在这里，全员受益。
import os as _os
import sys as _sys

_HERE = _os.path.dirname(_os.path.abspath(__file__))
_sys.path[:] = [p for p in _sys.path if p and _os.path.abspath(p) != _HERE]
import token as _stdlib_token            # noqa: E402
_sys.modules.setdefault("token", _stdlib_token)
del _os, _sys, _HERE
# ---------------------------------------------------------------------------

import socket
import struct
import zlib
from dataclasses import dataclass
from typing import Iterator, List, Optional, Tuple

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

# ---------------------------------------------------------------- 帧模型


@dataclass(frozen=True)
class FrameModel:
    name: str
    hdr: int
    size_off: int          # size 字段在头内的偏移
    cat_off: int
    id_off: int
    ack_off: Optional[int]  # 没有就 None

    def pack(self, cat: int, fid: int, payload: bytes) -> bytes:
        size = self.hdr + len(payload)
        head = bytearray(self.hdr)
        head[self.cat_off] = cat & 0xFF
        head[self.id_off] = fid & 0xFF
        struct.pack_into("<I", head, self.size_off, size)
        if self.ack_off is not None:
            head[self.ack_off] = 1
        return bytes(head) + payload


CHANNEL = FrameModel("CHANNEL(11B, 7001)", hdr=11, size_off=2, cat_off=0, id_off=1, ack_off=10)
GAME = FrameModel("GAME(13B, 30011/10013)", hdr=13, size_off=3, cat_off=0, id_off=1, ack_off=None)


class FrameError(Exception):
    pass


@dataclass
class Frame:
    off: int
    cat: int
    fid: int
    size: int
    raw: bytes

    @property
    def payload(self) -> bytes:
        raise NotImplementedError  # 需要模型才知道头长；用 split() 而不是属性

    def split(self, model: FrameModel) -> bytes:
        return self.raw[model.hdr:]


def parse_frames(buf: bytes, model: FrameModel) -> Tuple[List[Frame], int]:
    """返回 (帧列表, 已消费的字节数)。**不吞错**：帧长非法就停下并让调用方决定。"""
    out: List[Frame] = []
    off = 0
    while off + model.hdr <= len(buf):
        size = struct.unpack_from("<I", buf, off + model.size_off)[0]
        if size < model.hdr or off + size > len(buf):
            break
        out.append(Frame(off, buf[off + model.cat_off], buf[off + model.id_off],
                         size, buf[off:off + size]))
        off += size
    return out, off


def frames_iter(chunks: Iterator[bytes], model: FrameModel) -> Iterator[Frame]:
    """把 TCP 字节流切成帧（跨 recv 边界）。"""
    buf = b""
    for chunk in chunks:
        buf += chunk
        got, used = parse_frames(buf, model)
        for f in got:
            yield f
        buf = buf[used:]


# ---------------------------------------------------------------- 密码层


def aes_ecb_decrypt(key: bytes, data: bytes) -> bytes:
    if len(data) % 16:
        raise FrameError(f"AES 输入不是 16 的整数倍: {len(data)}")
    dec = Cipher(algorithms.AES(key), modes.ECB()).decryptor()
    return dec.update(data) + dec.finalize()


def aes_ecb_encrypt(key: bytes, data: bytes) -> bytes:
    enc = Cipher(algorithms.AES(key), modes.ECB()).encryptor()
    return enc.update(data) + enc.finalize()


def zlib_wrap(data: bytes) -> bytes:
    return zlib.compress(data)


def zlib_unwrap(data: bytes) -> bytes:
    return zlib.decompress(data)


def body_of(frame: Frame, model: FrameModel) -> bytes:
    """帧正文（去掉所选模型的头）。"""
    return frame.split(model)


def session_key_from(frames: List[Frame], model: FrameModel, sid: int = 12) -> Optional[bytes]:
    """会话密钥 = `sid` 号帧正文的 **[4..20)**（`int32 status` 之后的 16 字节）。

    ⚠️ 计划 Task 9 写的是 `[5..21)`，**实测是错的**（差一个字节）。定案方式是可证伪的
    （2026-10-05，B0 的 VM 抓包 `streams-vm/7001-7159.bin`）：拿候选密钥去解 `id=10` 那坨
    13312 B 密文 ——
        `[4:20)` = `32 30 32 36 ... 33 30 00 00`（= ASCII "20261005122830" + 2×NUL）
                 → 明文可打印率 **97.75%**、374 条串、开头 `[dungeon]` / `<4::channel_info_dname_4>`
        `[5:21)` → 可打印率 36.66%、无线索（垃圾）
        `[0:16)` → 41.42%（垃圾）
    所以密钥是"status 之后的 16 字节"，不是"status 之后第 2 个字节起的 16 字节"。
    找不到该帧返回 None（**不猜**）。
    """
    for f in frames:
        if f.fid == sid:
            body = body_of(f, model)
            if len(body) >= 20:
                return body[4:20]
    return None


def decode_channel_blob(frame: Frame, model: FrameModel, key: bytes,
                        count_byte: bool = True) -> bytes:
    """解 `id=10`/`id=3` 那种正文：`count(1) | zlib(AES(明文))`。

    count_byte=False 时直接按 `zlib(AES(明文))` 解（两种形状都在实物流里出现过，
    由调用方按帧 id 决定，**不自动试探**）。
    """
    body = body_of(frame, model)
    blob = body[1:] if count_byte else body
    return aes_ecb_decrypt(key, zlib_unwrap(blob))


# ---------------------------------------------------------------- 连接


class Conn:
    def __init__(self, host: str, port: int, model: FrameModel = CHANNEL, timeout: float = 6.0):
        self.model = model
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.settimeout(timeout)

    def send_frame(self, cat: int, fid: int, payload: bytes = b"") -> None:
        self.sock.sendall(self.model.pack(cat, fid, payload))

    def send_raw(self, data: bytes) -> None:
        self.sock.sendall(data)

    def recv_frames(self, max_frames: int = 256, idle_timeout: float = 1.5) -> List[Frame]:
        """读到"对端不再发"为止（每次 recv 超时就收工）。"""
        self.sock.settimeout(idle_timeout)
        out: List[Frame] = []
        try:
            for f in frames_iter(iter(lambda: self.sock.recv(65536), b""), self.model):
                out.append(f)
                if len(out) >= max_frames:
                    break
        except (socket.timeout, TimeoutError):
            pass
        return out

    def recv_bytes(self, max_bytes: int = 1 << 20, idle_timeout: float = 1.5) -> bytes:
        """原样收字节（自检要比对"整帧长度序列"，不需要解析时用这个）。"""
        self.sock.settimeout(idle_timeout)
        buf = b""
        try:
            while len(buf) < max_bytes:
                chunk = self.sock.recv(65536)
                if not chunk:
                    break
                buf += chunk
        except (socket.timeout, TimeoutError):
            pass
        return buf

    def close(self) -> None:
        try:
            self.sock.close()
        except OSError:
            pass


def frame_sizes(buf: bytes, model: FrameModel) -> List[int]:
    """只要整帧长度序列（Task 9 的判据就是它）。"""
    got, used = parse_frames(buf, model)
    return [f.size for f in got]


if __name__ == "__main__":  # 极小的自检：模型的自洽性
    for mdl in (CHANNEL, GAME):
        f = mdl.pack(1, 7, b"abc")
        back, used = parse_frames(f, mdl)
        assert used == len(f) and len(back) == 1 and back[0].fid == 7, mdl.name
        assert back[0].split(mdl) == b"abc", mdl.name
        print(f"{mdl.name}: hdr={mdl.hdr} size_off={mdl.size_off} 自洽 OK")
