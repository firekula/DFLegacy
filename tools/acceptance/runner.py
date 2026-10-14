"""建 run 目录、隔离状态、起服务端与 actor。

设计要点（spec §2/§4.2/§4.3）：
  * run 目录可寻址、runId 全自动；
  * 活存档**每 run 复制**，绝不共用 `DF/town-test/data/state.json`；
  * `server.json` **只做文件复制 + 定向修改**（含 "//" 注释键，不能序列化重写）；
  * 起进程前**端口预检**，冲突就明确报错（`open-questions.md` 坑 14：静默失败最坑）。
"""
from __future__ import annotations

import json
import shutil
import socket
import subprocess
import sys
from dataclasses import asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Optional

from .model import Manifest, RunPaths, Scenario, deterministic_view, new_run_id

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
                     overrides: dict[str, str] | None = None) -> dict:
        """起服务端。工作目录必须是 exe 同目录（它读同目录 server.json）。"""
        exe, workdir = Path(exe), Path(workdir)
        sj = Path(server_json) if server_json else workdir / "server.json"
        preflight_ports(parse_server_endpoints(sj))
        if overrides:
            plan = plan_config_overrides(sj, overrides)
            apply_config_overrides(sj, plan)
        else:
            plan = []
        p = subprocess.Popen([str(exe)], cwd=str(workdir),
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self._procs.append(p)
        rec = {"exe": exe.name, "pid": p.pid, "workdir": str(workdir)}
        _, mf = self._load(rp)
        mf.server = rec
        mf.config_overrides = plan
        self._write_manifest(rp, mf)
        return rec

    def start_actors(self, rp: RunPaths, scenario: Scenario,
                     launcher_cmd: Optional[list[str]] = None) -> list[dict]:
        """起每个 actor。`launcher_cmd is None` ⇒ 只用占位进程（测试/骨架用，不启动游戏）。"""
        recs: list[dict] = []
        for a in scenario.actors:
            cmd = list(launcher_cmd) if launcher_cmd else [
                sys.executable, "-c", "import time; time.sleep(3600)"]
            p = subprocess.Popen(cmd, cwd=str(rp.actor_dir(a.name)),
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            self._procs.append(p)
            recs.append({"name": a.name, "driver": a.driver, "pid": p.pid})
        _, mf = self._load(rp)
        by_name = {r["name"]: r for r in recs}
        for a in mf.actors:
            a["pid"] = by_name[a["name"]]["pid"]
        self._write_manifest(rp, mf)
        return recs

    def _load(self, rp: RunPaths) -> tuple[RunPaths, Manifest]:
        raw = json.loads(rp.manifest.read_text(encoding="utf-8"))
        mf = Manifest(**raw)
        self._last = mf
        return rp, mf

    def stop_all(self) -> None:
        for p in self._procs:
            if p.poll() is None:
                p.terminate()
                try:
                    p.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    p.kill()
                    p.wait(timeout=10)
        self._procs.clear()