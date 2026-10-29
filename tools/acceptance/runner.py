"""建 run 目录、隔离状态、起服务端与 actor。

设计要点（spec §2/§4.2/§4.3）：
  * run 目录可寻址、runId 全自动；
  * 活存档**每 run 复制**，绝不共用 `DF/town-test/data/state.json`；
  * `server.json` **只做文件复制 + 定向修改**（含 "//" 注释键，不能序列化重写）；
  * 起进程前**端口预检**，按端点各自声明的协议（TCP / UDP）分别探测，冲突就明确报错
    （`open-questions.md` 坑 14：静默失败最坑；补缺裁决要求 UDP 2311 必须用 SOCK_DGRAM 检查）。
"""
from __future__ import annotations

import json
import os
import select
import shutil
import socket
import subprocess
import sys
import time
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Optional

from .state_seed import is_data_path_key
from .model import ActorSpec, Manifest, RunPaths, Scenario, deterministic_view, new_run_id

COMMENT_PREFIX = "//"

# server.json 里**以 `Port` 键声明且是 UDP** 的服务。证据：src/DFLegacy.Server/Program.cs:339-340
# 的启动横幅把 CharacterDatagram 标成 `{Host}:{Port}/udp`（Entrance/GameProbe 没有 `/udp` 后缀），
# 且 CharacterDatagramService.cs:20 用 UdpClient，EntranceService.cs:171 / ProbeService.cs:23 用
# TcpListener ⇒ 默认 TCP，只有它是 UDP。
# 注：GameplayDatagramService.cs:37-38 也是 UDP（Program.cs:345-346 各自带 `/udp`），端口键是
# `PrimaryPort`/`SecondaryPort`（不是 `Port`），由 parse_server_endpoints 单独解析。
UDP_PORT_SERVICES: frozenset[str] = frozenset({"CharacterDatagram", "GameplayDatagram"})


class PortBusy(RuntimeError):
    """端口预检失败。消息里必须能看出**哪个条目、哪个端口**。"""


class ServerStartupError(RuntimeError):
    """服务端起得来但没在超时内开始监听（真机实测启动约 5.4 秒，见 `wait_for_listen`）。"""


@dataclass(frozen=True)
class Endpoint:
    """server.json 里的一个监听端点。

    协议必须随端点声明、并参与预检判断：本机 `DF/town-test/server.json` 里
    `Entrance{0.0.0.0, 2311}`（TCP）与 `CharacterDatagram{0.0.0.0, 2311}`（UDP）
    **同为 2311 但协议不同**，只看端口号会互相误报。
    """
    host: str
    port: int
    protocol: str = "tcp"


def parse_server_endpoints(server_json: Path) -> dict[str, Endpoint]:
    """抽出"服务名 -> Endpoint(host, port, protocol)"。跳过 `//` 注释键；缺 host 的条目默认 0.0.0.0。

    除单端口服务外，还解析：
      * `GameplayDatagram.PrimaryPort` / `SecondaryPort`（都是 UDP，键 `GameplayDatagramPrimary`/`GameplayDatagramSecondary`）；
      * `Entrance.AdditionalPorts`（TCP，键 `EntranceAdditional<i>`）；
      * `Processes.Channel.LinkPort`（TCP，键 `ChannelLink`）。
    """
    raw = json.loads(Path(server_json).read_text(encoding="utf-8"))["DFLegacy"]
    out: dict[str, Endpoint] = {}

    def host_of(cfg: dict) -> str:
        return str(cfg.get("Host") or cfg.get("AdvertisedHost") or "0.0.0.0")

    for svc, cfg in raw.items():
        if not isinstance(cfg, dict):
            continue
        if "Port" in cfg:
            protocol = "udp" if svc in UDP_PORT_SERVICES else "tcp"
            out[svc] = Endpoint(host_of(cfg), int(cfg["Port"]), protocol)
        if svc == "GameplayDatagram":
            for key in ("Primary", "Secondary"):
                port_key = key + "Port"
                if port_key in cfg:
                    out[f"GameplayDatagram{key}"] = Endpoint(host_of(cfg), int(cfg[port_key]), "udp")
        if svc == "Entrance" and isinstance(cfg.get("AdditionalPorts"), list):
            for i, port in enumerate(cfg["AdditionalPorts"]):
                out[f"EntranceAdditional{i}"] = Endpoint(host_of(cfg), int(port), "tcp")

    processes = raw.get("Processes")
    if isinstance(processes, dict) and isinstance(processes.get("Channel"), dict):
        link = processes["Channel"]
        if "LinkPort" in link:
            out["ChannelLink"] = Endpoint("0.0.0.0", int(link["LinkPort"]), "tcp")
    return out


# 每个服务端进程的静态声明：就绪端点键（parse_server_endpoints 的键名）与日志文件名。
# 端口值**不硬编码**——由 server.json 派生（见 server_process_specs）。
SERVER_SPECS: dict[str, dict[str, str]] = {
    "game": {"ready": "Channel", "log": "server-game.log"},
    "channel": {"ready": "Entrance", "log": "server-channel.log"},
}


def server_process_specs(server_json: Path) -> dict[str, list[Endpoint]]:
    """从 server.json 派生每个服务端进程绑定的端点（键 = SERVER_SPECS 的进程名）。

    channel 绑 `Entrance.Port` + `Entrance.AdditionalPorts`（去掉 `Channel.Port`）+ `Processes.Channel.LinkPort`；
    game 绑 `Channel.Port` + `GameplayDatagram`/`CharacterDatagram` 的 UDP。
    端口预检与 disjoint 校验都按 (host, port, protocol)，不是端口号。
    """
    eps = parse_server_endpoints(server_json)
    channel_port = eps["Channel"].port if "Channel" in eps else None
    channel: list[Endpoint] = []
    game: list[Endpoint] = []
    for key, ep in eps.items():
        if key in ("Entrance", "ChannelLink"):
            channel.append(ep)
        elif key.startswith("EntranceAdditional") and (channel_port is None or ep.port != channel_port):
            channel.append(ep)
        elif key == "Channel" or key == "CharacterDatagram" or key.startswith("GameplayDatagram"):
            game.append(ep)
    return {"game": game, "channel": channel}


def _is_split_server(server_json: Path) -> bool:
    """拆分布局 = `Processes.Channel.Enabled=true`（channel 进程接管入口族，game 只绑 Channel.Port）。"""
    try:
        raw = json.loads(Path(server_json).read_text(encoding="utf-8"))["DFLegacy"]
    except (OSError, ValueError, KeyError, TypeError):
        return False
    processes = raw.get("Processes")
    return (isinstance(processes, dict)
            and isinstance(processes.get("Channel"), dict)
            and processes["Channel"].get("Enabled") is True)


def _probe(host: str, port: int, protocol: str = "tcp") -> bool:
    """True = 该端口已被占用。`::` 与 `0.0.0.0` 都按 IPv4 通配探测。

    UDP 必须用 `SOCK_DGRAM`（TCP 与 UDP 是两个协议名字空间，用 TCP 探 UDP 会把"已占用"
    误报成空闲 ⇒ 服务端起得来但玩不通，终审 R-3/I-1）。UDP 分支**不开 SO_REUSEADDR**：
    Windows 上它等价于允许重复绑定（UDP 没有 TIME_WAIT，也不需要它），开了就再也探不出占用。
    """
    fam = socket.AF_INET6 if host == "::" else socket.AF_INET
    bind_host = "" if host in ("0.0.0.0", "::") else host
    is_udp = protocol == "udp"
    s = socket.socket(fam, socket.SOCK_DGRAM if is_udp else socket.SOCK_STREAM)
    try:
        if not is_udp:
            s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((bind_host, port))
        return False
    except OSError:
        return True
    finally:
        s.close()


def preflight_ports(endpoints: dict[str, Endpoint],
                    probe: Callable[[str, int, str], bool] = _probe) -> None:
    """对每个声明端点按其**自己的协议**探测；占用就报 PortBusy（消息含服务名/端口/协议）。"""
    busy = [f"{svc}={ep.host}:{ep.port}/{ep.protocol}"
            for svc, ep in sorted(endpoints.items())
            if probe(ep.host, ep.port, ep.protocol)]
    if busy:
        raise PortBusy("以下端口已被占用，拒绝启动（避免静默失败）：" + "; ".join(busy))


def probe_host(host: str) -> str:
    """把监听用的通配地址换成可连接的探测地址。

    `0.0.0.0` / `::` 是服务端**绑定**用的通配地址，不是能 connect 的目标；
    本机真机 launcher 探的就是 `127.0.0.1:<EntrancePort>`（launcher.json 的 EntranceHost）。
    """
    return "127.0.0.1" if host in ("0.0.0.0", "::", "") else host


def _tail(path: Path, limit: int = 800) -> str:
    """读日志尾部用于报错。文件可能还没建或正被写入 ⇒ 失败时返回说明而不是抛异常。"""
    try:
        text = Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        return f"(读不到日志 {path}：{exc})"
    return text[-limit:].strip() or "(日志为空)"


def _default_server_json(scenario: Scenario) -> Path:
    """兜底：取 `server.json` 时优先用场景里声明的路径，否则回落到当前工作目录。

    真机调用总是显式传 `server_json`；这里只是让 `wait_until_listening` 单独调用时不崩。
    """
    declared = getattr(scenario, "server_json", None)
    return Path(declared) if declared else Path.cwd() / "server.json"


def _can_connect(host: str, port: int, timeout_s: float = 0.3) -> bool:
    """True = 能建立 TCP 连接（= 有人在监听并 accept）。**总耗时**受 timeout_s 约束。

    为什么**不能**用 `_probe`（bind 探测）判断"是否就绪"：[真机实测 2026-10-15]
    `_probe` 开了 `SO_REUSEADDR`，Windows 上对 `127.0.0.1` 的监听者再用它 bind 同一 4 元组
    **会成功** ⇒ 明明有服务在监听却报"空闲"（实测：LISTEN 5486/OwningProcess 80592，
    bind 探测仍报 FREE）。bind 探测适合"起进程前查占用"，判断"是否已开始服务"必须用连接。

    为什么不用 `socket.create_connection`：[真机 2026-10-15] 服务端启动中（正在索引
    48343 个脚本、约 3 秒）时，连到 127.0.0.1:2311 的 SYN 会**被丢弃**，而
    `create_connection` 的 timeout 是**每次尝试**的超时、Windows 上会重试 ⇒ 单次调用
    实际阻塞约 20 秒，直接把就绪门卡死（实测 traceback：socket.py:850 TimeoutError →
    runner.py 探针）。这里改用非阻塞 connect + `select` 把**总时长**钉在 timeout_s 内。
    """
    fam = socket.AF_INET6 if ":" in host else socket.AF_INET
    s = socket.socket(fam, socket.SOCK_STREAM)
    s.setblocking(False)
    try:
        s.connect((host, port))
        return True
    except BlockingIOError:
        pass
    except OSError:
        return False
    try:
        _, writable, _ = select.select([], [s], [], timeout_s)
    except OSError:
        return False
    finally:
        pass
    if not writable:
        s.close()
        return False
    ok = s.getsockopt(socket.SOL_SOCKET, socket.SO_ERROR) == 0
    s.close()
    return ok


def wait_for_listen(host: str, port: int, timeout_s: float = 60.0,
                    should_stop: Optional[Callable[[], bool]] = None,
                    poll_interval_s: float = 0.25,
                    probe: Optional[Callable[[str, int, str], bool]] = None,
                    on_tick: Optional[Callable[[float], None]] = None,
                    tick_interval_s: float = 10.0) -> None:
    """等到 `host:port` 能建立 TCP 连接为止；超时或进程提前退出就明确失败。

    存在原因 [真机 2026-10-15]：服务端启动要**约 5.4 秒**（索引 48343 个脚本 + 各 catalog），
    而 launcher 的 `EnsureServerReadyAsync` 在 `StartServer=false` 时**只探一次就抛错**。
    因此"起完服务端立刻起 actor"必然失败：实测报
    `入口服务 127.0.0.1:2311 未监听，且 StartServer=false`。

    `on_tick(已等待秒数)` 每 `tick_interval_s` 调用一次，给操作者可见的进展：
    [真机 2026-10-18] 冷缓存下索引脚本可能远超 60 秒，而 .NET 输出按块缓冲（不满 4KB 不落盘）
    ⇒ 日志显示"0 字节"，操作者无法分辨它是在加载还是已经死了。
    """
    target = probe_host(host)
    check = probe if probe is not None else (lambda h, p, _proto: _can_connect(h, p))
    started = time.monotonic()
    deadline = started + timeout_s
    last_tick = started
    while True:
        # 探针本身也可能抛（网络栈的瞬时错误）；就绪探测绝不能因此把整条命令崩掉。
        try:
            ready = check(target, port, "tcp")
        except OSError:
            ready = False
        if ready:
            return
        if should_stop is not None and should_stop():
            raise ServerStartupError(
                f"服务端进程在 {target}:{port} 开始监听之前就退出了")
        now = time.monotonic()
        if now >= deadline:
            raise ServerStartupError(
                f"服务端在 {timeout_s}s 内没有在 {target}:{port} 开始监听")
        if on_tick is not None and now - last_tick >= tick_interval_s:
            last_tick = now
            on_tick(now - started)
        time.sleep(poll_interval_s)


def isolate_state(template_state: Path, dest_dir: Path) -> Path:
    """把活存档复制到 run 目录。**复制不是移动** —— 活存档必须保持原样。"""
    dest_dir = Path(dest_dir)
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / "state.json"
    shutil.copy2(template_state, dest)
    return dest


def plan_config_overrides(server_json: Path, overrides: dict[str, str]) -> list[dict]:
    """只**计算**要改的键（`A.B.C` 路径），不写盘。返回 `[{key, old, new}]`。
    路径以 `DFLegacy` 子树为根（与测试和实际 server.json 结构一致）。"""
    raw = json.loads(Path(server_json).read_text(encoding="utf-8"))["DFLegacy"]
    plan: list[dict] = []
    for dotted, new in overrides.items():
        node: Any = raw
        for part in dotted.split("."):
            node = node[part]
        plan.append({"key": dotted, "old": node, "new": new})
    return plan


def _skip_ws(text: str, i: int) -> int:
    while i < len(text) and text[i] in " \t\r\n":
        i += 1
    return i


def _skip_string(text: str, i: int) -> int:
    """text[i] == '"'；返回闭合引号之后的下标（转义不提前闭合）。"""
    i += 1
    n = len(text)
    while i < n:
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            return i + 1
        i += 1
    raise ValueError("server.json 字符串未闭合")


def _skip_value(text: str, i: int) -> int:
    """返回值最后一个字节之后的下标（不含尾随空白）。"""
    if text[i] == '"':
        return _skip_string(text, i)
    if text[i] in "{[":
        depth = 0
        n = len(text)
        while i < n:
            if text[i] == '"':
                i = _skip_string(text, i)
                continue
            if text[i] in "{[":
                depth += 1
            elif text[i] in "}]":
                depth -= 1
                if depth == 0:
                    return i + 1
            i += 1
        raise ValueError("server.json 括号未闭合")
    j = i
    while j < len(text) and text[j] not in ",}]":
        j += 1
    return j


def _find_member(text: str, obj_start: int, key: str) -> tuple[int, int]:
    """在 text[obj_start] == '{' 的对象里找成员 key，返回其值的 (start, end) 下标。

    字符串感知扫描：`"//"` 注释键就是普通字符串键；其余字节一律不动。
    """
    i = _skip_ws(text, obj_start + 1)
    n = len(text)
    while i < n and text[i] != "}":
        if text[i] != '"':
            raise ValueError("server.json 成员键不是字符串")
        key_start = i
        i = _skip_string(text, i)
        k = json.loads(text[key_start:i])
        i = _skip_ws(text, i)
        if i >= n or text[i] != ":":
            raise ValueError(f"server.json 成员 {k!r} 缺少 ':'")
        i = _skip_ws(text, i + 1)
        v_start = i
        i = _skip_value(text, i)
        v_end = i
        while v_end > v_start and text[v_end - 1] in " \t\r\n":
            v_end -= 1
        if k == key:
            return v_start, v_end
        i = _skip_ws(text, i)
        if i < n and text[i] == ",":
            i = _skip_ws(text, i + 1)
    raise KeyError(f"server.json 里找不到键 {key!r}")


def _object_start_for_path(text: str, parts: list[str]) -> int:
    """返回 `DFLegacy` 子树里 parts 路径（可为空）指向的对象的 '{' 下标。"""
    i = _skip_ws(text, 0)
    if i >= len(text) or text[i] != "{":
        raise ValueError("server.json 顶层不是对象")
    obj_start, _ = _find_member(text, i, "DFLegacy")
    for part in parts:
        obj_start, _ = _find_member(text, obj_start, part)
    if text[obj_start] != "{":
        raise ValueError(f"server.json 路径 {'/'.join(parts)!r} 不是对象")
    return obj_start


def _encode_new_value(old_text: str, new: Any) -> str:
    """替换文本保持旧值的 JSON 形态：字符串值加引号；旧值是标量且新值也合法时裸写。"""
    if isinstance(new, str) and not old_text.startswith('"'):
        try:
            json.loads(new)
            return new
        except ValueError:
            pass
    return json.dumps(new, ensure_ascii=False)


def apply_config_overrides(server_json: Path, plan: list[dict]) -> None:
    """按 plan 定向改值：**只替换目标键的值**，其余字节（含 `//` 注释键、空白、排版）原样保留。

    Global Constraint：server.json 不得用 json.dumps 序列化重写（它携带 `"//"` 注释键与
    原排版），必须做字节级定向替换；签名与 plan 列表契约与 plan_config_overrides 一致。
    """
    text = Path(server_json).read_text(encoding="utf-8")
    edits: list[tuple[int, int, str]] = []
    for item in plan:
        parts = item["key"].split(".")
        obj_start = _object_start_for_path(text, parts[:-1])
        v_start, v_end = _find_member(text, obj_start, parts[-1])
        edits.append((v_start, v_end, _encode_new_value(text[v_start:v_end], item["new"])))
    for s, e, rep in sorted(edits, reverse=True):
        text = text[:s] + rep + text[e:]
    Path(server_json).write_text(text, encoding="utf-8")


class Runner:
    def __init__(self, workspace: Path, acceptance_dir: Path, runs_dir: Path) -> None:
        self.workspace = Path(workspace)
        self.acceptance_dir = Path(acceptance_dir)
        self.runs_dir = Path(runs_dir)
        self._procs: list[subprocess.Popen] = []
        self._log_handles: list[Any] = []
        # 服务端进程句柄：`wait_until_listening` 靠 `Popen.poll()` 判断"是否已退出"。
        self._server_proc: Optional[subprocess.Popen] = None
        # 逐进程句柄（拆分布局下 channel/game 各自判活）。
        self._server_procs: dict[str, Optional[subprocess.Popen]] = {}

    def prepare(self, scenario: Scenario, git_sha: str,
                now: Optional[datetime] = None) -> tuple[RunPaths, Manifest]:
        rid = new_run_id(scenario.id, git_sha, now=now)
        rp = RunPaths.for_run(self.runs_dir, rid)
        rp.server_dir.mkdir(parents=True, exist_ok=True)
        for a in scenario.actors:
            rp.actor_dir(a.name).mkdir(parents=True, exist_ok=True)
        mf = Manifest(
            run_id=rid,
            git_sha=git_sha,
            created_utc=(now or datetime.now(timezone.utc)).astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            scenario_id=scenario.id,
            expect_t0=scenario.expect_t0,
            actors=[{"name": a.name, "driver": a.driver, "workdir": a.workdir}
                    for a in scenario.actors],
        )
        self._write_manifest(rp, mf)
        return rp, mf

    def _write_manifest(self, rp: RunPaths, mf: Manifest) -> None:
        rp.manifest.write_text(mf.to_json(), encoding="utf-8")

    def start_server(self, rp: RunPaths, exe: Path, workdir: Path,
                     name: str = "game",
                     server_json: Path | None = None,
                     overrides: dict[str, str] | None = None,
                     env: dict[str, str] | None = None,
                     log_path: Path | None = None,
                     server_args: Optional[list[str]] = None) -> dict:
        """起服务端进程。工作目录必须是 exe 同目录（它读同目录 server.json）。

        `name` 是进程名（`game`/`channel`），写进 `manifest.servers[name]`——逐进程各记一条、
        互不覆写；端口预检也只探测**本进程**声明的端点（否则第二个进程会把第一个进程已监听的
        端口误报成占用）。

        `env` 显式追加环境变量（如 DFLegacy__DataPath 指向 run 私有存档）；
        `log_path` 给定时 stdout/stderr 定向到该文件，句柄保留到 `stop_all` 关闭，
        否则维持原来的 DEVNULL 行为。`server_args` 供测试用假服务端，真机不传。

        **本方法只负责起进程，不等待监听** —— 等监听是独立的
        `wait_until_listening`（真机必须调它，原因见该方法的 docstring）。
        """
        exe, workdir = Path(exe), Path(workdir)
        sj = Path(server_json) if server_json else workdir / "server.json"
        my_endpoints = {f"{name}[{i}]": ep
                        for i, ep in enumerate(server_process_specs(sj).get(name, []))}
        preflight_ports(my_endpoints)
        # `DFLegacy__DataPath` 必须是绝对路径：服务端按 AppContext.BaseDirectory（exe 目录）
        # 解析它，相对路径会被写到别处（实测会静默写进 `DF\runs\...`，run 私有副本原封不动
        # ⇒ 存档隔离失效）。这里宁可启动前响亮失败，也不要静默写错地方。
        data_paths = {value for key, value in (env or {}).items() if is_data_path_key(key)}
        if len(data_paths) > 1:
            raise ValueError("Conflicting explicit DataPath aliases")
        data_path = next(iter(data_paths)) if data_paths else None
        if data_path is not None and not Path(data_path).is_absolute():
            raise ValueError(
                f"DFLegacy__DataPath 必须是绝对路径，得到 {data_path!r}："
                f"服务端按 exe 目录解析它，相对路径会写到 run 目录之外")
        if overrides:
            plan = plan_config_overrides(sj, overrides)
            apply_config_overrides(sj, plan)
        else:
            plan = []
        cmd = [str(exe)] + [str(a) for a in (server_args or [])]
        popen_env = {**os.environ, **env} if env else None
        if data_path is not None:
            # .NET accepts both colon and double-underscore aliases. Retaining
            # an inherited alias makes provider enumeration order choose a save.
            popen_env = {key: value for key, value in popen_env.items()
                         if not is_data_path_key(key)}
            popen_env["DFLegacy__DataPath"] = data_path
        if log_path is not None:
            log_path = Path(log_path)
            log_path.parent.mkdir(parents=True, exist_ok=True)
            handle = log_path.open("wb")
            try:
                p = subprocess.Popen(cmd, cwd=str(workdir), env=popen_env,
                                     stdout=handle, stderr=subprocess.STDOUT)
            except BaseException:
                handle.close()  # Popen 失败不得泄漏句柄（不登记进 _log_handles）
                raise
            self._log_handles.append(handle)
        else:
            p = subprocess.Popen(cmd, cwd=str(workdir), env=popen_env,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self._procs.append(p)
        self._server_proc = p
        self._server_procs[name] = p
        rec = {"exe": exe.name, "pid": p.pid, "workdir": str(workdir)}
        _, mf = self._load(rp)
        mf.servers[name] = rec
        mf.config_overrides = plan
        self._write_manifest(rp, mf)
        return rec

    def start_actors(self, rp: RunPaths, scenario: Scenario,
                     launcher_cmd: Optional[list[str]] = None,
                     actor_commands: dict[str, list[str]] | None = None,
                     on_started: Optional[Callable[[ActorSpec, subprocess.Popen], Optional[int]]] = None,
                     before_start: Optional[Callable[[ActorSpec], None]] = None
                     ) -> list[dict]:
        """起每个 actor。`launcher_cmd is None` ⇒ 只用占位进程（测试/骨架用，不启动游戏）。

        `actor_commands` 给每个 actor 各自的命令（真机时是每个账号一份 Launcher 调用），
        必须与场景 actor 一一对应，缺/多都明确报错且不启动任何进程。
        `on_started(actor, proc)` 在启动下一个 actor 之前调用，返回真实客户端 PID 时
        记入 rec 与 manifest 的 `client_pid`（供归集 netdump-<pid>.log 用）。
        """
        if actor_commands is not None:
            names = {a.name for a in scenario.actors}
            extra = sorted(set(actor_commands) - names)
            missing = sorted(names - set(actor_commands))
            if extra or missing:
                raise ValueError(
                    f"actor_commands 与场景 actor 不匹配：多余 {extra}，缺失 {missing}")
        recs: list[dict] = []
        try:
            for a in scenario.actors:
                if actor_commands is not None:
                    cmd = list(actor_commands[a.name])
                elif launcher_cmd is not None:
                    cmd = list(launcher_cmd)
                else:
                    cmd = [sys.executable, "-c", "import time; time.sleep(3600)"]
                if before_start is not None:
                    before_start(a)
                p = subprocess.Popen(cmd, cwd=str(rp.actor_dir(a.name)),
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                self._procs.append(p)
                rec = {"name": a.name, "driver": a.driver, "pid": p.pid}
                if on_started is not None:
                    client_pid = on_started(a, p)
                    if client_pid is not None:
                        rec["client_pid"] = client_pid
                recs.append(rec)
        except BaseException:
            self.stop_all()
            raise
        _, mf = self._load(rp)
        by_name = {r["name"]: r for r in recs}
        for a in mf.actors:
            rec = by_name[a["name"]]
            a["pid"] = rec["pid"]
            if "client_pid" in rec:
                a["client_pid"] = rec["client_pid"]
        self._write_manifest(rp, mf)
        return recs

    def _load(self, rp: RunPaths) -> tuple[RunPaths, Manifest]:
        raw = json.loads(rp.manifest.read_text(encoding="utf-8"))
        mf = Manifest(**raw)
        self._last = mf
        return rp, mf

    def record_run_config(self, rp: RunPaths, entries: list[dict]) -> None:
        """读回 manifest、设 config_overrides、写回（UTF-8）。"""
        _, mf = self._load(rp)
        mf.config_overrides = entries
        self._write_manifest(rp, mf)

    def wait_until_listening(self, rp: RunPaths, scenario: Scenario,
                             server_json: Path | None = None,
                             timeout_s: float = 60.0,
                             probe: Optional[Callable[[str, int, str], bool]] = None,
                             on_tick: Optional[Callable[[float], None]] = None,
                             server_proc: Optional[subprocess.Popen] = None) -> dict:
        """等服务端进程的**各自就绪端点**开始监听；返回等待报告（供 manifest/打印）。
        （拆分布局：channel 等 Entrance、game 等 Channel.Port；单体模式只等 Entrance。）

        存在原因 [真机 2026-10-15]：真服务端启动约 5.4 秒才监听 2311，而 launcher 的
        `EnsureServerReadyAsync` 在 `StartServer=false` 时**只探一次**就抛错
        （src/DFLegacy.Launcher/Program.cs:336-347）⇒ 起完服务端立刻起 actor 必然失败。
        端口取自 server.json 的 `Entrance`（不写死端口）；`server.json` 缺该端点或不可读时
        返回 `skipped`（不假装等过）。

        **"进程提前退出"必须用 `Popen.poll()` 判断**，不能用 `os.kill(pid,0)`：
        [真机/实测 2026-10-18 终审 Important] 本机对一个已退出（`poll()=3`）的子进程调
        `os.kill(pid,0)` 仍返回成功（Popen 还持有句柄，PID 未回收）⇒ 快速失败分支不触发，
        秒退的服务端要拖满 `--server-start-timeout`（默认 300s）才报"超时"。
        因此这里**只认 `Popen`**：`server_proc` 显式传入，否则用 `start_server` 记下的
        `self._server_proc`。未起过服务端（都为空）时不启用退出检测。
        """
        sj = Path(server_json) if server_json else _default_server_json(scenario)
        try:
            endpoints = parse_server_endpoints(sj)
        except (OSError, ValueError, KeyError, TypeError):
            endpoints = {}
        if _is_split_server(sj):
            return self._wait_split_processes(rp, sj, endpoints, timeout_s, probe, on_tick)

        entrance = endpoints.get("Entrance")
        if entrance is None:
            return {"skipped": True, "reason": f"{sj} 未声明 Entrance 端点",
                    "host": None, "port": None, "waited_ms": 0}

        proc = server_proc if server_proc is not None else self._server_proc
        should_stop = (lambda: proc.poll() is not None) if proc is not None else None
        started = time.monotonic()
        try:
            kw: dict = {} if probe is None else {"probe": probe}
            if on_tick is not None:
                kw["on_tick"] = on_tick
            wait_for_listen(entrance.host, entrance.port, timeout_s=timeout_s,
                            should_stop=should_stop, **kw)
        except ServerStartupError as exc:
            log = rp.server_dir / SERVER_SPECS["game"]["log"]
            raise ServerStartupError(
                f"{exc}；服务端日志尾部（{log}）：\n{_tail(log)}"
            ) from exc
        return {"skipped": False, "host": probe_host(entrance.host),
                "port": entrance.port, "waited_ms": int((time.monotonic() - started) * 1000)}

    def _wait_split_processes(self, rp: RunPaths, sj: Path, endpoints: dict,
                              timeout_s: float,
                              probe: Optional[Callable[[str, int, str], bool]],
                              on_tick: Optional[Callable[[float], None]]) -> dict:
        """拆分布局：channel 等 Entrance、game 等 Channel.Port，各自判活、各自报日志尾部。"""
        processes: list[dict] = []
        for name in ("channel", "game"):
            ready_key = SERVER_SPECS[name]["ready"]
            ep = endpoints.get(ready_key)
            if ep is None:
                continue
            proc = self._server_procs.get(name)
            should_stop = (lambda p=proc: p.poll() is not None) if proc is not None else None
            started = time.monotonic()
            try:
                kw: dict = {} if probe is None else {"probe": probe}
                if on_tick is not None:
                    kw["on_tick"] = on_tick
                wait_for_listen(ep.host, ep.port, timeout_s=timeout_s,
                                should_stop=should_stop, **kw)
            except ServerStartupError as exc:
                log = rp.server_dir / SERVER_SPECS[name]["log"]
                raise ServerStartupError(
                    f"{name} 进程：{exc}；日志尾部（{log}）：\n{_tail(log)}"
                ) from exc
            processes.append({"name": name, "host": probe_host(ep.host),
                              "port": ep.port,
                              "waited_ms": int((time.monotonic() - started) * 1000)})
        if not processes:
            return {"skipped": True, "reason": f"{sj} 未声明任何服务端就绪端点",
                    "host": None, "port": None, "waited_ms": 0, "processes": []}
        first = processes[0]
        return {"skipped": False, "host": first["host"], "port": first["port"],
                "waited_ms": first["waited_ms"], "processes": processes}

    def stop_all(self) -> None:
        """逆序回收（先 actor 后 server），并关闭日志句柄。

        真机顺序要求"先关两个真实客户端，再关服务端"（RUNBOOK），
        这里用 `_procs` 的入栈顺序反过来停，正好满足。
        """
        for p in reversed(self._procs):
            if p.poll() is None:
                p.terminate()
                try:
                    p.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    p.kill()
                    p.wait(timeout=10)
        self._procs.clear()
        for handle in self._log_handles:
            try:
                handle.close()
            except Exception:
                pass
        self._log_handles.clear()