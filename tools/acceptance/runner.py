"""建 run 目录、隔离状态、起服务端与 actor。

设计要点（spec §2/§4.2/§4.3）：
  * run 目录可寻址、runId 全自动；
  * 活存档**每 run 复制**，绝不共用 `DF/town-test/data/state.json`；
  * `server.json` **只做文件复制 + 定向修改**（含 "//" 注释键，不能序列化重写）；
  * 起进程前**端口预检**，冲突就明确报错（`open-questions.md` 坑 14：静默失败最坑）。
"""
from __future__ import annotations

import json
import os
import shutil
import socket
import subprocess
import sys
from dataclasses import asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Optional

from .model import ActorSpec, Manifest, RunPaths, Scenario, deterministic_view, new_run_id

COMMENT_PREFIX = "//"


class PortBusy(RuntimeError):
    """端口预检失败。消息里必须能看出**哪个条目、哪个端口**。"""


def parse_server_endpoints(server_json: Path) -> dict[str, tuple[str, int]]:
    """抽出"服务名 -> (host, port)"。跳过 `//` 注释键；缺 host 的条目默认 0.0.0.0。"""
    raw = json.loads(Path(server_json).read_text(encoding="utf-8"))["DFLegacy"]
    out: dict[str, tuple[str, int]] = {}
    for svc, cfg in raw.items():
        if not isinstance(cfg, dict):
            continue
        if "Port" not in cfg:
            continue
        host = cfg.get("Host") or cfg.get("AdvertisedHost") or "0.0.0.0"
        out[svc] = (str(host), int(cfg["Port"]))
    return out


def _probe(host: str, port: int) -> bool:
    """True = 该端口已被占用。`::` 与 `0.0.0.0` 都按 IPv4 通配探测。"""
    fam = socket.AF_INET6 if host == "::" else socket.AF_INET
    bind_host = "" if host in ("0.0.0.0", "::") else host
    s = socket.socket(fam, socket.SOCK_STREAM)
    try:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((bind_host, port))
        return False
    except OSError:
        return True
    finally:
        s.close()


def preflight_ports(endpoints: dict[str, tuple[str, int]],
                    probe: Callable[[str, int], bool] = _probe) -> None:
    busy = [f"{svc}={host}:{port}" for svc, (host, port) in sorted(endpoints.items())
            if probe(host, port)]
    if busy:
        raise PortBusy("以下端口已被占用，拒绝启动（避免静默失败）：" + "; ".join(busy))


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
                     server_json: Path | None = None,
                     overrides: dict[str, str] | None = None,
                     env: dict[str, str] | None = None,
                     log_path: Path | None = None,
                     server_args: Optional[list[str]] = None) -> dict:
        """起服务端。工作目录必须是 exe 同目录（它读同目录 server.json）。

        `env` 显式追加环境变量（如 DFLegacy__DataPath 指向 run 私有存档）；
        `log_path` 给定时 stdout/stderr 定向到该文件，句柄保留到 `stop_all` 关闭，
        否则维持原来的 DEVNULL 行为。`server_args` 供测试用假服务端，真机不传。
        """
        exe, workdir = Path(exe), Path(workdir)
        sj = Path(server_json) if server_json else workdir / "server.json"
        preflight_ports(parse_server_endpoints(sj))
        if overrides:
            plan = plan_config_overrides(sj, overrides)
            apply_config_overrides(sj, plan)
        else:
            plan = []
        cmd = [str(exe)] + [str(a) for a in (server_args or [])]
        popen_env = {**os.environ, **env} if env else None
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
        rec = {"exe": exe.name, "pid": p.pid, "workdir": str(workdir)}
        _, mf = self._load(rp)
        mf.server = rec
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