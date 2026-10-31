# 本机多客户端验收平台（S0）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把"每次更新都要人起服务端、人起客户端、人决定日志编号、人搬日志"换成**一条命令产出一个可 diff 的判定**。

**Architecture:** 验收的原子单位是"一次 run"。`runner` 建可寻址的 run 目录并起服务端与 N 个 actor（actor 的 `driver` 可以是真人客户端或合成客户端）；`collector` 把每个源的产物归到对应 actor 目录并记录时钟偏移；`verdict` 按场景声明判定，输出**三分**（通过/需人看/缺证据）并把**归一化判定**落到进 git 的目录。判定留版本控制，运行产物不留。

**Tech Stack:** Python 3.12（本机 `python` 已解析到 `D:\Program Files\Python312\python.exe`）；`pytest`；`PyYAML`（**待 Task 0 验证是否已装，未装则用已装的替代或改用内置 JSON 场景 —— 见 Task 0 Step 3**）。不引入其它新依赖。

**Spec:** `docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md`（本计划从它论证而来，执行者两份都要读）

## Global Constraints

逐字取自 spec 与项目硬约束，**每个任务都隐含包含本节**：

- **禁止人工编号**：`runId` 全自动生成（`<UTC时间戳>-<git短哈希>-<场景id>`）。**不得**再出现 `server-round6b`、`party44` 这类人工编号。
- **判定三分**：`pass` / `needs-human` / `missing-evidence`。**不得**只输出一个绿勾。
- **`[语义]` 档不等于失败** —— 它是"当前没有 golden bytes 可自动判定"的诚实标注。
- **每个场景声明必须至少有一条能失败的期望**，否则该场景判定无效（防恒真判定）。
- **`runs/` 不进 git**；**`tools/acceptance/verdicts/` 进 git**（归一化判定）。产物可丢，判定必须留下版本控制轨迹。
- **`state.json` 每 run 隔离**：`runner` 必须给每个 run 独立状态（复制 → 跑 → 归档）；**禁止**让 run 共用 `DF/town-test/data/state.json`。
- **`server.json` 只能文件复制 + 定向修改**，**禁止**用 JSON 序列化重写（会丢掉 `"//"` 注释键）；改动必须记进 `manifest.json`。
- **不改产品代码**：本计划只新增 `tools/acceptance/`，**不动** `src/`、`tests/`（那把是产品的 `tests/`，本计划的测试在 `tools/acceptance/tests/`）。
- 证据六类标记 `[静态]`/`[抓包]`/`[真机]`/`[回放]`/`[推断]`/`[未验证]` + `[作废]`；**判定档** `[存在]/[字节]/[语义]` 是另一套，**不混用**（spec §5.5）。
- 提交**必须带 pathspec**；`.recon/`、`docs/`、`.superpowers/` 在 gitignore ⇒ 新增要 `git add -f`，**`-f` 必须写在 pathspec 之前**。**绝不 `git add -A`**。
- Python 读文本**显式 `encoding="utf-8"`**；跑前设 `$env:PYTHONIOENCODING='utf-8'`。
- 落盘一律 `>` 重定向；**不要**用 `python x.py | Tee-Object out.txt | Select-Object -First N`（`-First` 会截断落盘文件）。
- **不运行真客户端**（除了 Task 6 的人工联机那一次）；Task 1–5 全部可在**不启动任何游戏进程**的前提下测完。

---

## 已知的源事实（写代码时按这些对账，不要凭印象）

| 事实 | 值 | 来源 |
|---|---|---|
| 活存档 | `G:\Game\DNF_ACT1\DF\town-test\data\state.json`，**152,435 B**（实测仍在变） | spec §4.2 本轮实测 |
| netdump 产物 | **`netdump-<pid>.log`，在客户端自己的目录**，每包一行、立即落盘 | `STATUS.md` L1374/L1424；实例 `DF\netdump-53916.log` |
| netdump 开关 | 客户端目录 `Config.ini` 的 `DumpNetwork=1` + 带 netdump 的 `ijl15.dll`；**已存在则不覆盖** | `STATUS.md` L1422；`.recon/onboarding.md` §4 |
| 服务端配置 | `DF\town-test\server.json`，**含 `"//"` 注释键**，与仓库那份**已漂移** | `.recon/onboarding.md` §5b |
| 服务端可执行 | `DF\town-test\DFLegacy.Server.exe`，**读同目录 `server.json`**，工作目录必须是该目录 | `.recon/onboarding.md` §5b |
| 端口互斥 | 官方容器与本项目服务端**都要宿主 `TCP 7001` 与 `UDP 2311`**，同时起会有一个静默失败 | `open-questions.md` 坑 14 |
| 验收目录 | `G:\Game\DNF_ACT1\DF\town-test` | `.recon/onboarding.md` §3 |

---

## File Structure

| 文件 | 责任 |
|---|---|
| `tools/acceptance/__init__.py` | 包标记 + `__version__` |
| `tools/acceptance/model.py` | **纯数据契约**：`Manifest` / `ActorSpec` / `RunPaths` / `Scenario` / `Step` / `Expect` / `Verdict` / `VerdictEntry`。无 I/O 副作用 |
| `tools/acceptance/runner.py` | 建 run 目录、状态隔离、起服务端与 actor、写 `manifest.json`、端口预检 |
| `tools/acceptance/collector.py` | 归集 netdump / 服务端日志 / 探针产物到 `actor-N/`，写时钟偏移 |
| `tools/acceptance/verdict.py` | 按场景期望判定 → `verdict.json`（三分）+ 归一化摘要 |
| `tools/acceptance/cli.py` | 唯一入口：`run` / `collect` / `judge` / `replay` |
| `tools/acceptance/scenarios/*.yaml` | 场景声明（1 个要跑通 + 3 个骨架） |
| `tools/acceptance/verdicts/*.json` | **进 git** 的归一化判定 |
| `tools/acceptance/tests/` | 本计划自己的测试（**与产品 `tests/` 分开**） |
| `runs/<runId>/` | 运行产物（**不进 git**） |

**为什么拆成 model / runner / collector / verdict 四个模块**：它们各自能被独立测试 ——
`runner` 用假 actor 命令测（不需要真客户端）、`collector` 用已落盘的 netdump 样本测、
`verdict` 用构造的产物目录测。`model` 无 I/O，是另外三者的共享契约。

---

### Task 0: 环境前置验证（**先做，可能改变后面的技术选型**）

**Files:**
- Create: `tools/acceptance/tests/test_env.py`

**Interfaces:**
- Consumes: 无
- Produces: 无代码接口；产出**结论**：`pytest` 与 YAML 解析是否可用

- [ ] **Step 1: 写前置检查测试**

```python
"""S0 环境前置：确认测试框架与 YAML 解析可用（不可用就必须换选型，别硬写）。"""
import importlib
import sys


def test_python_version_at_least_312():
    assert sys.version_info[:2] >= (3, 12), f"需要 3.12+，实际 {sys.version.split()[0]}"


def test_pytest_importable():
    import pytest  # noqa: F401


def test_yaml_available():
    mod = None
    for name in ("yaml",):
        try:
            mod = importlib.import_module(name)
            break
        except ImportError:
            continue
    assert mod is not None, "没有 YAML 解析器：改用 JSON 场景（把 scenarios/*.yaml 改 *.json）"
```

- [ ] **Step 2: 跑它，看真实结果**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy
python -m pytest tools/acceptance/tests/test_env.py -v 2>&1 | Select-Object -First 20
```
Expected: 3 passed。**若 `pytest` 缺失或 `yaml` 缺失** —— 不要 `pip install`，
改为：`pytest` 缺失则用标准库 `unittest`（把本计划所有 `pytest` 风格测试改成 `unittest.TestCase`）；
`yaml` 缺失则场景文件改 **JSON**（本计划其余部分只需把 `scenarios/*.yaml` 换名 `*.json` 并改 `_load_scenario`）。
**把实际选型写进 `tools/acceptance/README.md` 并提交**（后续任务据此执行）。

- [ ] **Step 3: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/tests/test_env.py
git commit -m "验收平台S0：环境前置验证（pytest/yaml 可用性）" -- tools/acceptance/tests/test_env.py
```
（若本步改了选型，把 `tools/acceptance/README.md` 一并加进 pathspec。）

---

### Task 1: 数据契约 `model.py`（纯数据，无 I/O）

**Files:**
- Create: `tools/acceptance/__init__.py`
- Create: `tools/acceptance/model.py`
- Test: `tools/acceptance/tests/test_model.py`

**Interfaces:**
- Consumes: 无
- Produces（**后续任务的共享契约，签名逐字固定**）：
  - `class ActorSpec(dataclass, frozen)`: `name:str, driver:str, workdir:Optional[str]=None`
  - `class Expect(dataclass, frozen)`: `description:str, level:str, pattern:Optional[str]=None, evidence:Optional[str]=None`
  - `class Step(dataclass, frozen)`: `actor:str, action:str, expects:tuple[Expect, ...]=()`
  - `class Scenario(dataclass, frozen)`: `id:str, goal:str, actors:tuple[ActorSpec, ...], steps:tuple[Step, ...], expect_t0:str`
  - `class RunPaths(dataclass, frozen)`: `root:Path, run_dir:Path, manifest:Path, server_dir:Path, actor_dir(name:str)->Path, verdict:Path, verdict_digest:Path`
  - `class Manifest(dataclass)`: `run_id:str, git_sha:str, created_utc:str, scenario_id:str, expect_t0:str, actors:list[dict], server:dict, config_overrides:list[dict]`
  - `LEVELS: tuple[str, ...]` = `("[存在]", "[字节]", "[语义]")`
  - `RESULT_LEVELS: tuple[str, ...]` = `("pass", "needs-human", "missing-evidence", "fail")`
  - `new_run_id(scenario_id:str, git_sha:str, now:datetime|None=None) -> str`
  - `deterministic_view(manifest:Manifest) -> dict` —— 去掉 `created_utc`/`run_id`/pid 的视图，供跨 run 比对
  - `load_scenario(path:Path) -> Scenario`（YAML；若 Task 0 判定无 yaml 则读 JSON）
  - `parse_expect(raw) -> Expect`（支持 `"s2c:noti1"` 简写 ⇒ `Expect(description="s2c:noti1", level="[存在]")`）

- [ ] **Step 1: 写失败测试**

```python
"""契约层：纯数据、无 I/O。"""
from datetime import datetime, timezone

import pytest

from acceptance.model import (LEVELS, RESULT_LEVELS, ActorSpec, Expect, Manifest,
                              Scenario, Step, deterministic_view, new_run_id,
                              parse_expect, load_scenario, RunPaths)
from pathlib import Path


def test_level_vocabularies_are_exact():
    assert LEVELS == ("[存在]", "[字节]", "[语义]")
    assert RESULT_LEVELS == ("pass", "needs-human", "missing-evidence", "fail")


def test_new_run_id_is_auto_and_carries_sha_and_scenario():
    rid = new_run_id("town-two-clients", "abc1234",
                     now=datetime(2026, 10, 7, 6, 0, 0, tzinfo=timezone.utc))
    assert rid == "20261007T060000Z-abc1234-town-two-clients"


def test_new_run_id_never_reuses_a_human_serial():
    a = new_run_id("s", "abc1234", now=datetime(2026, 10, 7, 6, 0, 0, tzinfo=timezone.utc))
    b = new_run_id("s", "abc1234", now=datetime(2026, 10, 7, 6, 0, 1, tzinfo=timezone.utc))
    assert a != b


def test_parse_expect_shorthand_defaults_to_existence_level():
    e = parse_expect("s2c:noti1")
    assert e.description == "s2c:noti1"
    assert e.level == "[存在]"
    assert e.pattern is None


def test_parse_expect_mapping_carries_level_and_evidence():
    e = parse_expect({"description": "s2c:noti1", "level": "[字节]",
                      "pattern": "0001", "evidence": "[抓包]"})
    assert (e.level, e.pattern, e.evidence) == ("[字节]", "0001", "[抓包]")


def test_parse_expect_rejects_unknown_level():
    with pytest.raises(ValueError):
        parse_expect({"description": "x", "level": "[猜的]"})


def test_scenario_and_actors_round_trip(tmp_path):
    p = tmp_path / "s.yaml"
    p.write_text(
        "id: town-two-clients\n"
        "goal: 两客户端进城互见\n"
        "expect_t0: server-accept\n"
        "actors:\n"
        "  - {name: A, driver: real-client}\n"
        "  - {name: B, driver: synthetic}\n"
        "steps:\n"
        "  - {actor: A, action: 登录进城, expect: [c2s:login-ok]}\n",
        encoding="utf-8",
    )
    sc = load_scenario(p)
    assert isinstance(sc, Scenario)
    assert sc.id == "town-two-clients"
    assert [a.name for a in sc.actors] == ["A", "B"]
    assert sc.actors[1].driver == "synthetic"
    assert sc.steps[0].expects[0].description == "c2s:login-ok"


def test_run_paths_are_addressed_by_actor_name(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    assert rp.actor_dir("A") == tmp_path / "RID" / "actor-A"
    assert rp.verdict.name == "verdict.json"
    assert rp.verdict_digest.name == "verdict.digest.json"


def test_deterministic_view_drops_time_and_id_but_keeps_roster():
    m = Manifest(run_id="20261007T060000Z-abc1234-s", git_sha="abc1234",
                 created_utc="2026-10-07T06:00:00Z", scenario_id="s",
                 expect_t0="server-accept",
                 actors=[{"name": "A", "driver": "real-client", "pid": 123}],
                 server={"exe": "DFLegacy.Server.exe", "pid": 456},
                 config_overrides=[{"key": "GameplayDatagram.AdvertisedHost", "old": "x", "new": "127.0.0.1"}])
    v = deterministic_view(m)
    assert "created_utc" not in v and "run_id" not in v
    assert v["actors"] == [{"name": "A", "driver": "real-client"}]
    assert v["server"]["exe"] == "DFLegacy.Server.exe" and "pid" not in v["server"]
    assert v["config_overrides"] == [{"key": "GameplayDatagram.AdvertisedHost", "old": "x", "new": "127.0.0.1"}]
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m pytest acceptance/tests/test_model.py -v 2>&1 | Select-Object -First 15
```
Expected: FAIL —— `ModuleNotFoundError: No module named 'acceptance'`（或 `model`）

> 测试的 cwd 固定为 `tools/`（这样 `import acceptance.model` 成立）。后续任务同此。

- [ ] **Step 3: 写实现**

`tools/acceptance/__init__.py`：
```python
"""本机多客户端验收平台（S0）。设计见 docs/superpowers/specs/2026-10-07-local-multiclient-acceptance-s0-design.md"""
__version__ = "0.1.0"
```

`tools/acceptance/model.py`：
```python
"""S0 的纯数据契约。**无 I/O 副作用**，便于另外三个模块独立测试。"""
from __future__ import annotations

import json
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional

LEVELS: tuple[str, ...] = ("[存在]", "[字节]", "[语义]")
RESULT_LEVELS: tuple[str, ...] = ("pass", "needs-human", "missing-evidence", "fail")
DRIVERS: tuple[str, ...] = ("real-client", "synthetic")


@dataclass(frozen=True)
class ActorSpec:
    name: str
    driver: str
    workdir: Optional[str] = None

    def __post_init__(self) -> None:
        if self.driver not in DRIVERS:
            raise ValueError(f"未知 driver {self.driver!r}，允许 {DRIVERS}")


@dataclass(frozen=True)
class Expect:
    description: str
    level: str = "[存在]"
    pattern: Optional[str] = None
    evidence: Optional[str] = None

    def __post_init__(self) -> None:
        if self.level not in LEVELS:
            raise ValueError(f"未知判定档 {self.level!r}，允许 {LEVELS}")


@dataclass(frozen=True)
class Step:
    actor: str
    action: str
    expects: tuple[Expect, ...] = ()


@dataclass(frozen=True)
class Scenario:
    id: str
    goal: str
    actors: tuple[ActorSpec, ...]
    steps: tuple[Step, ...]
    expect_t0: str

    def has_falsifiable_expect(self) -> bool:
        """至少一条期望能失败，否则该场景判定无效（防恒真判定）。"""
        return any(s.expects for s in self.steps)


@dataclass(frozen=True)
class RunPaths:
    root: Path
    run_dir: Path

    @staticmethod
    def for_run(root: Path, run_id: str) -> "RunPaths":
        return RunPaths(root=Path(root), run_dir=Path(root) / run_id)

    @property
    def manifest(self) -> Path:
        return self.run_dir / "manifest.json"

    @property
    def server_dir(self) -> Path:
        return self.run_dir / "server"

    def actor_dir(self, name: str) -> Path:
        return self.run_dir / f"actor-{name}"

    @property
    def verdict(self) -> Path:
        return self.run_dir / "verdict.json"

    @property
    def verdict_digest(self) -> Path:
        return self.run_dir / "verdict.digest.json"


@dataclass
class Manifest:
    run_id: str
    git_sha: str
    created_utc: str
    scenario_id: str
    expect_t0: str
    actors: list[dict] = field(default_factory=list)
    server: dict = field(default_factory=dict)
    config_overrides: list[dict] = field(default_factory=list)

    def to_json(self) -> str:
        return json.dumps(asdict(self), ensure_ascii=False, indent=2, sort_keys=True)


def new_run_id(scenario_id: str, git_sha: str, now: Optional[datetime] = None) -> str:
    """全自动 runId：<UTC时间戳>-<git短哈希>-<场景id>。**禁止人工编号。**"""
    now = now or datetime.now(timezone.utc)
    stamp = now.astimezone(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    return f"{stamp}-{git_sha[:7]}-{scenario_id}"


def deterministic_view(manifest: Manifest) -> dict:
    """跨 run 可比的视图：去掉时间/id/pid，保留名册与配置改动。"""
    return {
        "git_sha": manifest.git_sha[:7],
        "scenario_id": manifest.scenario_id,
        "expect_t0": manifest.expect_t0,
        "actors": [{"name": a["name"], "driver": a["driver"]} for a in manifest.actors],
        "server": {k: v for k, v in manifest.server.items() if k not in ("pid",)},
        "config_overrides": manifest.config_overrides,
    }


def parse_expect(raw: Any) -> Expect:
    if isinstance(raw, str):
        return Expect(description=raw)
    if isinstance(raw, dict):
        unknown = set(raw) - {"description", "level", "pattern", "evidence"}
        if unknown:
            raise ValueError(f"期望里出现未知键 {sorted(unknown)}")
        return Expect(
            description=raw["description"],
            level=raw.get("level", "[存在]"),
            pattern=raw.get("pattern"),
            evidence=raw.get("evidence"),
        )
    raise ValueError(f"期望只能是字符串或映射，收到 {type(raw).__name__}")


def _load_raw(path: Path) -> dict:
    text = Path(path).read_text(encoding="utf-8")
    if Path(path).suffix.lower() in (".yaml", ".yml"):
        import yaml  # Task 0 已验证可用；不可用时场景改 .json
        return yaml.safe_load(text)
    return json.loads(text)


def load_scenario(path: Path) -> Scenario:
    raw = _load_raw(path)
    actors = tuple(
        ActorSpec(name=a["name"], driver=a["driver"], workdir=a.get("workdir"))
        for a in raw["actors"]
    )
    steps = tuple(
        Step(
            actor=s["actor"],
            action=s["action"],
            expects=tuple(parse_expect(e) for e in (s.get("expect") or [])),
        )
        for s in raw["steps"]
    )
    names = {a.name for a in actors}
    for st in steps:
        if st.actor not in names:
            raise ValueError(f"步骤引用了未声明的 actor {st.actor!r}；已声明 {sorted(names)}")
    return Scenario(id=raw["id"], goal=raw["goal"], actors=actors, steps=steps,
                    expect_t0=raw["expect_t0"])
```

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2
Expected: 9 passed

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/__init__.py tools/acceptance/model.py tools/acceptance/tests/test_model.py
git commit -m "验收平台S0：数据契约 model.py（runId 全自动/判定档/场景解析）" -- \
  tools/acceptance/__init__.py tools/acceptance/model.py tools/acceptance/tests/test_model.py
```

---

### Task 2: `runner.py` —— 建 run、状态隔离、起进程（用假 actor 可测）

**Files:**
- Create: `tools/acceptance/runner.py`
- Test: `tools/acceptance/tests/test_runner.py`

**Interfaces:**
- Consumes: `model.py` 的 `RunPaths` / `Manifest` / `Scenario` / `new_run_id` / `deterministic_view`
- Produces:
  - `class PortBusy(RuntimeError)` —— 端口预检失败时抛
  - `parse_server_endpoints(server_json:Path) -> dict[str, tuple[str, int]]` —— 从 `server.json` 抽出**要预检的** `host/port`（只读，不重写）
  - `preflight_ports(endpoints:dict[str, tuple[str,int]], probe=...) -> None` —— 被占用则 `raise PortBusy(条目+占用者)`
  - `isolate_state(template_state:Path, dest_dir:Path) -> Path` —— 把活存档复制到 run 目录，返回新路径
  - `plan_config_overrides(server_json:Path, overrides:dict[str, str]) -> list[dict]` —— **只返回**要改的键/原值/新值，不写盘
  - `class Runner:` `__init__(self, workspace:Path, acceptance_dir:Path, runs_dir:Path)`；
    `prepare(self, scenario:Scenario, git_sha:str, now=None) -> tuple[RunPaths, Manifest]`；
    `start_server(self, rp:RunPaths, exe:Path, workdir:Path) -> dict`；
    `start_actors(self, rp:RunPaths, scenario:Scenario, launcher_cmd:list[str]|None=None) -> list[dict]`；
    `stop_all(self) -> None`

- [ ] **Step 1: 写失败测试**

```python
"""runner：建 run / 状态隔离 / 端口预检 / 配置改动计划（全部用假 actor，不启动游戏）。"""
import json
import socket
import threading

import pytest

from acceptance.model import ActorSpec, Scenario, Step, Expect, RunPaths
from acceptance.runner import (PortBusy, Runner, isolate_state, parse_server_endpoints,
                               plan_config_overrides, preflight_ports)

SERVER_JSON = {
    "DFLegacy": {
        "Admin": {"//Host": "注释不该被当值", "Host": "::", "Port": 8081},
        "Entrance": {"Host": "127.0.0.1", "Port": 2311},
        "Channel": {"Port": 7001},
        "GameplayDatagram": {"AdvertisedHost": "192.168.3.177", "Port": 7002},
    }
}


def _write_server_json(p):
    p.write_text(json.dumps(SERVER_JSON, ensure_ascii=False), encoding="utf-8")
    return p


def test_parse_server_endpoints_skips_comment_keys(tmp_path):
    sj = _write_server_json(tmp_path / "server.json")
    eps = parse_server_endpoints(sj)
    assert eps["Admin"] == ("::", 8081)
    assert eps["Entrance"] == ("127.0.0.1", 2311)
    assert not any("//" in k for k in eps)


def test_preflight_ports_raises_with_offender_named():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.listen(1)
    try:
        with pytest.raises(PortBusy) as ei:
            preflight_ports({"Entrance": ("127.0.0.1", port)})
        assert "Entrance" in str(ei.value) and str(port) in str(ei.value)
    finally:
        s.close()


def test_preflight_ports_passes_when_free():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
    s.close()
    preflight_ports({"Entrance": ("127.0.0.1", port)})  # 不抛


def test_isolate_state_copies_and_never_points_at_the_live_save(tmp_path):
    live = tmp_path / "data" / "state.json"
    live.parent.mkdir(parents=True)
    live.write_text('{"characters":[]}', encoding="utf-8")
    dest = tmp_path / "run" / "server"
    dest.mkdir(parents=True)
    out = isolate_state(live, dest)
    assert out == dest / "state.json"
    assert out.read_text(encoding="utf-8") == '{"characters":[]}'
    out.write_text('{"characters":[1]}', encoding="utf-8")
    assert live.read_text(encoding="utf-8") == '{"characters":[]}', "活存档必须没被改"


def test_plan_config_overrides_reports_old_and_new_without_writing(tmp_path):
    sj = _write_server_json(tmp_path / "server.json")
    before = sj.read_text(encoding="utf-8")
    plan = plan_config_overrides(sj, {"GameplayDatagram.AdvertisedHost": "127.0.0.1"})
    assert plan == [{"key": "GameplayDatagram.AdvertisedHost",
                     "old": "192.168.3.177", "new": "127.0.0.1"}]
    assert sj.read_text(encoding="utf-8") == before, "plan 阶段不得写盘"


def test_runner_prepare_creates_addressed_run_dir(tmp_path):
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=(Step("A", "do", (Expect("x"),)),))
    rp, mf = r.prepare(sc, git_sha="abc1234")
    assert rp.run_dir.parent == tmp_path / "runs"
    assert rp.run_dir.name.startswith("20") and "abc1234" in rp.run_dir.name and rp.run_dir.name.endswith("-s")
    assert mf.scenario_id == "s" and mf.git_sha == "abc1234"
    assert rp.manifest.exists(), "prepare 必须落 manifest.json"
    assert [a["name"] for a in mf.actors] == ["A"]


def test_runner_starts_and_stops_a_fake_actor_and_records_pid(tmp_path):
    r = Runner(workspace=tmp_path, acceptance_dir=tmp_path / "acc", runs_dir=tmp_path / "runs")
    sc = Scenario(id="s", goal="g", expect_t0="server-accept",
                  actors=(ActorSpec("A", "synthetic"),), steps=(Step("A", "do", (Expect("x"),)),))
    rp, _ = r.prepare(sc, git_sha="abc1234")
    recs = r.start_actors(rp, sc, launcher_cmd=["cmd", "/c", "ping -n 30 127.0.0.1"])
    try:
        assert len(recs) == 1
        assert recs[0]["name"] == "A" and isinstance(recs[0]["pid"], int)
        assert rp.actor_dir("A").exists()
    finally:
        r.stop_all()
    assert all(p.poll() is not None for p in r._procs), "stop_all 必须真的结束子进程"
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m pytest acceptance/tests/test_runner.py -v 2>&1 | Select-Object -First 15
```
Expected: FAIL —— `ModuleNotFoundError: No module named 'acceptance.runner'`

- [ ] **Step 3: 写实现**

`tools/acceptance/runner.py`：
```python
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
    """只**计算**要改的键（`A.B.C` 路径），不写盘。返回 `[{key, old, new}]`。"""
    raw = json.loads(Path(server_json).read_text(encoding="utf-8"))
    plan: list[dict] = []
    for dotted, new in overrides.items():
        node: Any = raw
        for part in dotted.split("."):
            node = node[part]
        plan.append({"key": dotted, "old": node, "new": new})
    return plan


def apply_config_overrides(server_json: Path, plan: list[dict]) -> None:
    """按 plan 定向改值，**重写整个文件但保留 `//` 注释键**（它们是普通字符串键）。"""
    raw = json.loads(Path(server_json).read_text(encoding="utf-8"))
    for item in plan:
        node: Any = raw
        for part in item["key"].split(".")[:-1]:
            node = node[part]
        node[item["key"].split(".")[-1]] = item["new"]
    Path(server_json).write_text(
        json.dumps(raw, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


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
```

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2
Expected: 7 passed

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/runner.py tools/acceptance/tests/test_runner.py
git commit -m "验收平台S0：runner（run 目录/状态隔离/端口预检/进程编排）" -- \
  tools/acceptance/runner.py tools/acceptance/tests/test_runner.py
```

---

### Task 3: `collector.py` —— 归集产物与时钟偏移（用已落盘的 netdump 样本可测）

**Files:**
- Create: `tools/acceptance/collector.py`
- Test: `tools/acceptance/tests/test_collector.py`

**Interfaces:**
- Consumes: `model.RunPaths`
- Produces:
  - `NETDUMP_GLOB: str` = `"netdump-*.log"`
  - `find_netdumps(client_dirs:dict[str, Path]) -> dict[str, Path]` —— actor 名 → netdump 路径（**每客户端一个文件**；多份则报错而非猜）
  - `parse_netdump_t0(path:Path) -> Optional[datetime]` —— 从 netdump 行里取第一条时间戳（无则 None）
  - `collect_actor(rp:RunPaths, actor:str, netdump:Path|None, extra:list[Path]=()) -> dict` —— 复制到 `actor-N/`，返回 `{actor, netdump, t0_utc, bytes}`
  - `clock_offset(server_t0:datetime, actor_t0:datetime|None) -> Optional[float]` —— 秒，None 表示无法判定
  - `collect_server(rp:RunPaths, log_dir:Path) -> dict` —— 归集服务端日志到 `server/`
  - `record_offsets(rp:RunPaths, offsets:dict[str, float|None]) -> Path` —— 写 `offsets.json`

- [ ] **Step 1: 写失败测试**

```python
"""collector：归集 + 时钟偏移。用**已落盘的 netdump 样本**当输入，不启动游戏。"""
import datetime as dt
from pathlib import Path

import pytest

from acceptance.collector import (NETDUMP_GLOB, clock_offset, collect_actor,
                                  collect_server, find_netdumps, parse_netdump_t0,
                                  record_offsets)
from acceptance.model import RunPaths

SAMPLE = (
    "[2026-10-07 06:00:00.123] connect 127.0.0.1:2311\n"
    "[2026-10-07 06:00:01.456] TX len=43 hex=F2F7F142\n"
    "[2026-10-07 06:00:02.789] RX len=47 hex=0001002F\n"
)


def test_find_netdumps_maps_actor_to_its_single_file(tmp_path):
    d1, d2 = tmp_path / "c1", tmp_path / "c2"
    d1.mkdir(); d2.mkdir()
    (d1 / "netdump-1234.log").write_text(SAMPLE, encoding="utf-8")
    (d2 / "netdump-5678.log").write_text(SAMPLE, encoding="utf-8")
    got = find_netdumps({"A": d1, "B": d2})
    assert got["A"].name == "netdump-1234.log" and got["B"].name == "netdump-5678.log"


def test_find_netdumps_reports_ambiguity_instead_of_guessing(tmp_path):
    d = tmp_path / "c1"
    d.mkdir()
    (d / "netdump-1.log").write_text(SAMPLE, encoding="utf-8")
    (d / "netdump-2.log").write_text(SAMPLE, encoding="utf-8")
    with pytest.raises(ValueError) as ei:
        find_netdumps({"A": d})
    assert "A" in str(ei.value) and "2" in str(ei.value)


def test_parse_netdump_t0_reads_first_timestamp(tmp_path):
    p = tmp_path / "netdump-1.log"
    p.write_text(SAMPLE, encoding="utf-8")
    assert parse_netdump_t0(p) == dt.datetime(2026, 10, 7, 6, 0, 0, 123000)


def test_parse_netdump_t0_returns_none_when_no_timestamp(tmp_path):
    p = tmp_path / "netdump-1.log"
    p.write_text("TX len=1 hex=00\n", encoding="utf-8")
    assert parse_netdump_t0(p) is None


def test_collect_actor_copies_into_actor_dir_and_reports_t0(tmp_path):
    d = tmp_path / "c1"; d.mkdir()
    src = d / "netdump-1.log"
    src.write_text(SAMPLE, encoding="utf-8")
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.actor_dir("A").mkdir(parents=True)
    rec = collect_actor(rp, "A", src)
    assert (rp.actor_dir("A") / "netdump-1.log").exists()
    assert rec["actor"] == "A" and rec["bytes"] == len(SAMPLE.encode("utf-8"))
    assert rec["t0_utc"] == "2026-10-07T06:00:00.123000Z"


def test_clock_offset_computes_seconds_and_tolerates_missing():
    s = dt.datetime(2026, 10, 7, 6, 0, 5)
    a = dt.datetime(2026, 10, 7, 6, 0, 0)
    assert clock_offset(s, a) == pytest.approx(5.0)
    assert clock_offset(s, None) is None


def test_record_offsets_writes_json(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    out = record_offsets(rp, {"A": 5.0, "B": None})
    assert out.name == "offsets.json"
    assert '"A": 5.0' in out.read_text(encoding="utf-8")


def test_collect_server_copies_logs(tmp_path):
    logs = tmp_path / "logs"; logs.mkdir()
    (logs / "server-1.log").write_text("x", encoding="utf-8")
    rp = RunPaths.for_run(tmp_path / "runs", "RID")
    rp.server_dir.mkdir(parents=True)
    rec = collect_server(rp, logs)
    assert rec["files"] == 1 and (rp.server_dir / "server-1.log").exists()
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m pytest acceptance/tests/test_collector.py -v 2>&1 | Select-Object -First 15
```
Expected: FAIL —— `ModuleNotFoundError: No module named 'acceptance.collector'`

- [ ] **Step 3: 写实现**

`tools/acceptance/collector.py`：
```python
"""把每个源的产物归到对应 actor 目录，并记录时钟偏移。

spec §2.2 纪律 1：**不追求单一交错日志** —— 每 actor/每进程各写各的文件，
用 `offsets.json` 记录偏移，join 阶段事后按时间对齐（这也是 S4 上局域网要用的机制）。
"""
from __future__ import annotations

import datetime as dt
import json
import re
import shutil
from pathlib import Path
from typing import Optional

from .model import RunPaths

NETDUMP_GLOB = "netdump-*.log"
_TS = re.compile(r"^\[(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})\.(\d{3})\]")


def find_netdumps(client_dirs: dict[str, Path]) -> dict[str, Path]:
    """actor 名 -> netdump 路径。**每客户端一个文件**（`STATUS.md` L1424）；
    找到多于一个就报错 —— 不许猜哪一个属于本次 run。"""
    out: dict[str, Path] = {}
    for actor, d in client_dirs.items():
        found = sorted(Path(d).glob(NETDUMP_GLOB))
        if not found:
            raise ValueError(f"actor {actor!r} 的目录 {d} 下没有 {NETDUMP_GLOB}")
        if len(found) > 1:
            raise ValueError(
                f"actor {actor!r} 的目录 {d} 下有 {len(found)} 个 netdump，无法判定哪一个属于本次 run："
                + ", ".join(p.name for p in found))
        out[actor] = found[0]
    return out


def parse_netdump_t0(path: Path) -> Optional[dt.datetime]:
    """取第一条带时间戳的行；没有就 None（**不猜**）。"""
    with Path(path).open(encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = _TS.match(line)
            if m:
                d, t, ms = m.groups()
                base = dt.datetime.strptime(f"{d} {t}", "%Y-%m-%d %H:%M:%S")
                return base.replace(microsecond=int(ms) * 1000)
    return None


def collect_actor(rp: RunPaths, actor: str, netdump: Optional[Path],
                  extra: list[Path] = ()) -> dict:
    dest_dir = rp.actor_dir(actor)
    dest_dir.mkdir(parents=True, exist_ok=True)
    rec: dict = {"actor": actor, "netdump": None, "t0_utc": None, "bytes": 0}
    if netdump is not None:
        netdump = Path(netdump)
        dest = dest_dir / netdump.name
        shutil.copy2(netdump, dest)
        t0 = parse_netdump_t0(netdump)
        rec.update({
            "netdump": dest.name,
            "t0_utc": t0.strftime("%Y-%m-%dT%H:%M:%S.%fZ") if t0 else None,
            "bytes": dest.stat().st_size,
        })
    for e in extra:
        shutil.copy2(e, dest_dir / Path(e).name)
    return rec


def clock_offset(server_t0: dt.datetime, actor_t0: Optional[dt.datetime]) -> Optional[float]:
    """actor 相对服务端的偏移（秒）。无法判定就 None —— **不许默认 0**。"""
    if actor_t0 is None:
        return None
    return (server_t0 - actor_t0).total_seconds()


def collect_server(rp: RunPaths, log_dir: Path) -> dict:
    rp.server_dir.mkdir(parents=True, exist_ok=True)
    n = 0
    for p in sorted(Path(log_dir).glob("*.log")):
        shutil.copy2(p, rp.server_dir / p.name)
        n += 1
    return {"files": n, "dir": str(log_dir)}


def record_offsets(rp: RunPaths, offsets: dict[str, Optional[float]]) -> Path:
    out = rp.run_dir / "offsets.json"
    out.write_text(json.dumps(offsets, ensure_ascii=False, indent=2, sort_keys=True),
                   encoding="utf-8")
    return out
```

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2
Expected: 8 passed

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/collector.py tools/acceptance/tests/test_collector.py
git commit -m "验收平台S0：collector（netdump/日志归集 + 时钟偏移）" -- \
  tools/acceptance/collector.py tools/acceptance/tests/test_collector.py
```

---

### Task 4: `verdict.py` —— 三分判定 + 归一化摘要（**S0 的核心**）

**Files:**
- Create: `tools/acceptance/verdict.py`
- Test: `tools/acceptance/tests/test_verdict.py`

**Interfaces:**
- Consumes: `model.py` 的 `Scenario`/`Step`/`Expect`/`LEVELS`/`RESULT_LEVELS`
- Produces:
  - `class VerdictEntry(dataclass, frozen)`: `actor:str, step_action:str, description:str, level:str, result:str, detail:str=""`
  - `class Verdict(dataclass)`: `scenario_id:str, entries:list[VerdictEntry], invalid_reason:Optional[str]=None`；
    `counts() -> dict[str,int]`（**键恰为 `RESULT_LEVELS` 四项**）；
    `status() -> str`（`"invalid"` / `"red"` / `"green-with-human"` / `"green"`）
  - `GREP_SOURCES: dict[str, str]` = `{"netdump": "netdump-*.log", "server": "*.log"}`
  - `judge(scenario:Scenario, artifact_dirs:dict[str, Path], server_dir:Path) -> Verdict`
  - `write_verdict(rp:RunPaths, v:Verdict) -> tuple[Path, Path]` —— 写 `verdict.json` 与 `verdict.digest.json`
  - `digest(v:Verdict) -> dict` —— **去掉可变量**，只留可 diff 的判定（进 git）

- [ ] **Step 1: 写失败测试**

```python
"""verdict：三分判定 + 归一化摘要 + 防恒真判定。"""
from pathlib import Path

import pytest

from acceptance.model import ActorSpec, Expect, Scenario, Step
from acceptance.model import RunPaths
from acceptance.verdict import (RESULT_LEVELS, Verdict, VerdictEntry, digest, judge,
                                write_verdict)


def _sc(*steps):
    return Scenario(id="s", goal="g", expect_t0="server-accept",
                    actors=(ActorSpec("A", "real-client"), ActorSpec("B", "synthetic")),
                    steps=steps)


def test_counts_keys_are_exactly_the_four_result_levels():
    v = Verdict(scenario_id="s", entries=[VerdictEntry("A", "do", "d", "[存在]", "pass")])
    assert set(v.counts()) == set(RESULT_LEVELS)


def test_existence_level_passes_when_pattern_found_in_actor_netdump(tmp_path):
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("[2026-10-07 06:00:01.000] TX len=43 hex=F2F7F142\n",
                                     encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("F2F7F142"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "pass"
    assert v.status() == "green"


def test_existence_level_fails_loudly_when_absent(tmp_path):
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    sc = _sc(Step("A", "登录", (Expect("DEADBEEF"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "fail"
    assert v.status() == "red"


def test_semantic_level_is_needs_human_not_pass(tmp_path):
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=1 hex=00\n", encoding="utf-8")
    sc = _sc(Step("A", "走到 B 附近", (Expect("A 能看到 B", level="[语义]"),)))
    v = judge(sc, {"A": d}, tmp_path / "server")
    assert v.entries[0].result == "needs-human"
    assert v.status() == "green-with-human"


def test_byte_level_requires_the_declared_pattern_not_just_the_description(tmp_path):
    """`[字节]` 与 `[存在]` 的差别：`[字节]` 必须命中 `pattern`；
    若把 pattern 指向一个不存在的串，即使 description 能命中，也必须 fail。"""
    d = tmp_path / "actor-A"; d.mkdir()
    (d / "netdump-1.log").write_text("TX len=43 hex=F2F7F142\n", encoding="utf-8")
    ok = judge(_sc(Step("A", "登录", (Expect("登录帧", level="[字节]", pattern="F2F7F142"),))),
               {"A": d}, tmp_path / "server")
    assert ok.entries[0].result == "pass"
    bad = judge(_sc(Step("A", "登录", (Expect("登录帧", level="[字节]", pattern="DEADBEEF"),))),
                {"A": d}, tmp_path / "server")
    assert bad.entries[0].result == "fail"


def test_missing_artifact_is_missing_evidence_not_fail(tmp_path):
    sc = _sc(Step("A", "登录", (Expect("F2F7F142"),)))
    v = judge(sc, {"A": tmp_path / "nope"}, tmp_path / "server")
    assert v.entries[0].result == "missing-evidence"


def test_scenario_without_any_expect_is_invalid(tmp_path):
    sc = _sc(Step("A", "do", ()))
    v = judge(sc, {"A": tmp_path}, tmp_path / "server")
    assert v.status() == "invalid"
    assert v.invalid_reason and "没有" in v.invalid_reason


def test_digest_drops_variable_detail_but_keeps_results():
    v = Verdict(scenario_id="s", entries=[
        VerdictEntry("A", "do", "d", "[存在]", "pass", detail="第 12 行命中"),
        VerdictEntry("B", "do2", "d2", "[语义]", "needs-human"),
    ])
    dg = digest(v)
    assert dg["scenario_id"] == "s" and dg["status"] == "green-with-human"
    assert dg["counts"]["pass"] == 1 and dg["counts"]["needs-human"] == 1
    assert all("detail" not in e for e in dg["entries"])
    assert [e["result"] for e in dg["entries"]] == ["pass", "needs-human"]


def test_write_verdict_writes_both_full_and_digest(tmp_path):
    rp = RunPaths.for_run(tmp_path, "RID")
    rp.run_dir.mkdir(parents=True)
    v = Verdict(scenario_id="s", entries=[VerdictEntry("A", "do", "d", "[存在]", "pass")])
    full, dg = write_verdict(rp, v)
    assert full.name == "verdict.json" and dg.name == "verdict.digest.json"
    assert "detail" in full.read_text(encoding="utf-8")
```

- [ ] **Step 2: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m pytest acceptance/tests/test_verdict.py -v 2>&1 | Select-Object -First 15
```
Expected: FAIL —— `ModuleNotFoundError: No module named 'acceptance.verdict'`

- [ ] **Step 3: 写实现**

`tools/acceptance/verdict.py`：
```python
"""三分判定。

spec §3.2：`[存在]`/`[字节]` 自动判定；`[语义]` **自动标记为需人看**（不假装自动）；
产物缺失 ⇒ `missing-evidence`（**不是** fail —— fail 是"证据在、但不符"）。
spec §5.4 + Global Constraints：场景若没有任何期望能失败 ⇒ 判定**无效**（防恒真判定）。
"""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Optional

from .model import RESULT_LEVELS, Scenario, RunPaths

# 供调用方从本模块取用（契约里声明了 `verdict.RESULT_LEVELS`）
__all__ = ["RESULT_LEVELS", "GREP_SOURCES", "Verdict", "VerdictEntry", "judge", "digest",
           "write_verdict"]

GREP_SOURCES: dict[str, str] = {"netdump": "netdump-*.log", "server": "*.log"}


@dataclass(frozen=True)
class VerdictEntry:
    actor: str
    step_action: str
    description: str
    level: str
    result: str
    detail: str = ""

    def __post_init__(self) -> None:
        if self.result not in RESULT_LEVELS:
            raise ValueError(f"未知判定结果 {self.result!r}，允许 {RESULT_LEVELS}")


@dataclass
class Verdict:
    scenario_id: str
    entries: list[VerdictEntry] = field(default_factory=list)
    invalid_reason: Optional[str] = None

    def counts(self) -> dict[str, int]:
        c = {k: 0 for k in RESULT_LEVELS}
        for e in self.entries:
            c[e.result] += 1
        return c

    def status(self) -> str:
        if self.invalid_reason:
            return "invalid"
        c = self.counts()
        if c["fail"]:
            return "red"
        if c["needs-human"] or c["missing-evidence"]:
            return "green-with-human"
        return "green"


def _search(dirs: list[Path], pattern: str) -> Optional[tuple[Path, int, str]]:
    """在给定目录里逐个 glob 搜 pattern；返回 (文件, 行号, 行内容) 或 None。"""
    for d in dirs:
        d = Path(d)
        if not d.exists():
            continue
        for f in sorted(d.glob("*.log")):
            with f.open(encoding="utf-8", errors="replace") as fh:
                for i, line in enumerate(fh, start=1):
                    if pattern in line:
                        return f, i, line.rstrip("\n")
    return None


def judge(scenario: Scenario, artifact_dirs: dict[str, Path], server_dir: Path) -> Verdict:
    if not scenario.has_falsifiable_expect():
        return Verdict(scenario_id=scenario.id,
                       invalid_reason="场景没有任何期望 ⇒ 判定无效（防恒真判定）")

    entries: list[VerdictEntry] = []
    for step in scenario.steps:
        d = artifact_dirs.get(step.actor)
        for ex in step.expects:
            if ex.level == "[语义]":
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "needs-human",
                    detail="语义断言：需人看；升级为自动需先有 golden bytes（spec §3.2）"))
                continue
            if d is None or not Path(d).exists():
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "missing-evidence",
                    detail=f"actor {step.actor!r} 的产物目录不存在：{d}"))
                continue
            pattern = ex.pattern if ex.pattern is not None else ex.description
            hit = _search([Path(d)], pattern)
            if hit is None:
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "fail",
                    detail=f"在 {Path(d)} 的 *.log 里找不到 {pattern!r}"))
            else:
                f, i, line = hit
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "pass",
                    detail=f"{f.name}:{i} {line[:80]}"))
    return Verdict(scenario_id=scenario.id, entries=entries)


def digest(v: Verdict) -> dict:
    """**进 git** 的归一化判定：去掉 detail（含行号/时间），只留可 diff 的结论。"""
    return {
        "scenario_id": v.scenario_id,
        "status": v.status(),
        "invalid_reason": v.invalid_reason,
        "counts": v.counts(),
        "entries": [
            {"actor": e.actor, "step_action": e.step_action,
             "description": e.description, "level": e.level, "result": e.result}
            for e in v.entries
        ],
    }


def write_verdict(rp: RunPaths, v: Verdict) -> tuple[Path, Path]:
    full = rp.verdict
    full.write_text(json.dumps(
        {"scenario_id": v.scenario_id, "status": v.status(),
         "invalid_reason": v.invalid_reason, "counts": v.counts(),
         "entries": [asdict(e) for e in v.entries]},
        ensure_ascii=False, indent=2), encoding="utf-8")
    dg = rp.verdict_digest
    dg.write_text(json.dumps(digest(v), ensure_ascii=False, indent=2, sort_keys=True),
                  encoding="utf-8")
    return full, dg
```

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2
Expected: 9 passed

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/verdict.py tools/acceptance/tests/test_verdict.py
git commit -m "验收平台S0：verdict（三分判定/归一化摘要/防恒真判定）" -- \
  tools/acceptance/verdict.py tools/acceptance/tests/test_verdict.py
```

---

### Task 5: 场景声明 + CLI + 端到端干跑（不启动真客户端）

**Files:**
- Create: `tools/acceptance/scenarios/town-two-clients.yaml`
- Create: `tools/acceptance/scenarios/party-invite.yaml`
- Create: `tools/acceptance/scenarios/town-position-sync.yaml`
- Create: `tools/acceptance/scenarios/trade-open.yaml`
- Create: `tools/acceptance/cli.py`
- Create: `tools/acceptance/verdicts/.gitkeep`
- Test: `tools/acceptance/tests/test_cli_dry_run.py`

**Interfaces:**
- Consumes: Task 1–4 的全部
- Produces:
  - `main(argv:list[str]) -> int`：子命令 `run`（`--scenario`/`--actors`/`--dry-run`/`--runs-dir`）、
    `judge`（`--run-dir`）、`replay`（`--run-dir`，从产物重跑判定）
  - `dry_run(scenario_path:Path, runs_dir:Path, git_sha:str) -> tuple[RunPaths, Verdict]` ——
    **不启动任何游戏进程**，用空 actor 目录跑出 `missing-evidence` 判定；用于验证链路与"缺证据"档
  - `promote_digest(rp:RunPaths, acceptance_dir:Path) -> Path` —— 把归一化判定**提升**到
    `tools/acceptance/verdicts/<scenario>.json`（**这一步才是进 git 的**）

- [ ] **Step 1: 写四个场景声明**

`tools/acceptance/scenarios/town-two-clients.yaml`（**S0 唯一要求跑通**）：
```yaml
id: town-two-clients
goal: 两个客户端各自登录→选角→进城，A 能看到 B
expect_t0: server-accept
actors:
  - {name: A, driver: real-client}
  - {name: B, driver: real-client}
steps:
  - {actor: A, action: 登录并进到城镇, expect: [c2s:login-ok, "s2c:noti1"]}
  - {actor: B, action: 登录并进到城镇, expect: [c2s:login-ok]}
  - actor: A
    action: 走到 B 附近
    expect:
      - {description: "A 收到 B 的 USERINFO 且同屏", level: "[语义]"}
```

`tools/acceptance/scenarios/party-invite.yaml`（骨架）：
```yaml
id: party-invite
goal: A 邀请 B 组队，双方名册一致
expect_t0: server-accept
actors:
  - {name: A, driver: real-client}
  - {name: B, driver: real-client}
steps:
  - {actor: A, action: 邀请 B 组队, expect: [{description: "s2c:noti7", level: "[存在]"}]}
  - {actor: B, action: 接受邀请, expect: [{description: "s2c:noti9", level: "[存在]"}]}
  - {actor: A, action: 查看队伍名册, expect: [{description: "A 的名册含 B", level: "[语义]"}]}
```

`tools/acceptance/scenarios/town-position-sync.yaml`（骨架）：
```yaml
id: town-position-sync
goal: A 移动，B 侧同屏位置随动
expect_t0: server-accept
actors:
  - {name: A, driver: real-client}
  - {name: B, driver: real-client}
steps:
  - {actor: A, action: 在城镇里移动一段距离, expect: [{description: "s2c:position", level: "[存在]"}]}
  - {actor: B, action: 观察 A 的位置, expect: [{description: "B 屏幕上 A 的位置随之变化", level: "[语义]"}]}
```

`tools/acceptance/scenarios/trade-open.yaml`（骨架）：
```yaml
id: trade-open
goal: A 发起交易，双方交易窗打开
expect_t0: server-accept
actors:
  - {name: A, driver: real-client}
  - {name: B, driver: real-client}
steps:
  - {actor: A, action: 右键 B 选择交易, expect: [{description: "s2c:noti8", level: "[存在]"}]}
  - {actor: B, action: 接受交易, expect: [{description: "s2c:noti8", level: "[存在]"}]}
  - {actor: A, action: 放入一件物品, expect: [{description: "s2c:noti15", level: "[存在]"}]}
```

- [ ] **Step 2: 写失败测试（干跑链路）**

```python
"""CLI 干跑：不启动任何游戏进程，验证 run→collect→judge→promote 链路。"""
import json
from pathlib import Path

import pytest

from acceptance.cli import dry_run, main, promote_digest
from acceptance.model import load_scenario

SCEN = Path(__file__).resolve().parents[1] / "scenarios" / "town-two-clients.yaml"


def test_all_four_scenarios_parse_and_have_falsifiable_expects():
    base = Path(__file__).resolve().parents[1] / "scenarios"
    ids = sorted(p.stem for p in base.glob("*.yaml"))
    assert ids == ["party-invite", "town-position-sync", "town-two-clients", "trade-open"]
    for p in sorted(base.glob("*.yaml")):
        sc = load_scenario(p)
        assert sc.has_falsifiable_expect(), f"{sc.id} 没有任何能失败的期望 ⇒ 判定会无效"


def test_dry_run_produces_missing_evidence_not_a_fake_pass(tmp_path):
    sc = load_scenario(SCEN)
    rp, v = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    assert rp.run_dir.exists() and rp.manifest.exists()
    c = v.counts()
    # 干跑没有任何产物 ⇒ 只能出 missing-evidence / needs-human，**不许**出现 pass
    assert c["pass"] == 0
    assert c["missing-evidence"] > 0
    assert v.status() in ("green-with-human",)


def test_run_twice_yields_identical_digest(tmp_path):
    _, v1 = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    _, v2 = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    from acceptance.verdict import digest
    assert digest(v1) == digest(v2), "同一场景两次干跑的归一化判定必须一致"


def test_main_replay_is_read_only_and_returns_zero(tmp_path, capsys):
    rp, _ = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    rc = main(["replay", "--run-dir", str(rp.run_dir)])
    assert rc == 0
    out = capsys.readouterr().out
    assert "status" in out


def test_promote_digest_copies_only_the_normalized_judgement(tmp_path):
    rp, _ = dry_run(SCEN, tmp_path / "runs", git_sha="abc1234")
    acc = tmp_path / "acc"
    dest = promote_digest(rp, acc)
    assert dest == acc / "verdicts" / "town-two-clients.json"
    payload = json.loads(dest.read_text(encoding="utf-8"))
    assert set(payload) == {"scenario_id", "status", "invalid_reason", "counts", "entries"}
    assert all("detail" not in e for e in payload["entries"])


def test_main_run_dry_run_does_not_start_any_process(tmp_path):
    rc = main(["run", "--scenario", str(SCEN), "--actors", "2",
               "--dry-run", "--runs-dir", str(tmp_path / "runs")])
    assert rc == 0
```

- [ ] **Step 3: 跑测试确认失败**

Run:
```powershell
$env:PYTHONIOENCODING='utf-8'
cd G:\Game\DNF_ACT1\DFLegacy\tools
python -m pytest acceptance/tests/test_cli_dry_run.py -v 2>&1 | Select-Object -First 15
```
Expected: FAIL —— `ModuleNotFoundError: No module named 'acceptance.cli'`

- [ ] **Step 4: 写实现**

`tools/acceptance/cli.py`：
```python
"""S0 唯一入口。

  run    <scenario> --actors N [--dry-run]     建 run（+起进程；--dry-run 不起任何游戏进程）
  judge  --run-dir <dir>                        对已有 run 重新判定
  replay --run-dir <dir>                        只读重放：打印已有判定（不改盘）

`promote_digest` 把归一化判定提升到 `tools/acceptance/verdicts/<scenario>.json` —— **这一步才进 git**。
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

from .collector import (clock_offset, collect_actor, collect_server, find_netdumps,
                        parse_netdump_t0, record_offsets)
from .model import RunPaths, load_scenario
from .runner import Runner
from .verdict import Verdict, digest, judge, write_verdict

HERE = Path(__file__).resolve().parent
SCENARIOS = HERE / "scenarios"


def _git_sha(workspace: Path) -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=str(workspace),
                             capture_output=True, text=True, check=True)
        return out.stdout.strip()
    except Exception:
        return "nogit"


def dry_run(scenario_path: Path, runs_dir: Path, git_sha: str) -> tuple[RunPaths, Verdict]:
    """**不启动任何游戏进程**：只建 run 目录、按场景建空的 actor 目录、判定。
    用于验证链路、验证"缺证据"档、以及为真机 run 预演。"""
    sc = load_scenario(scenario_path)
    r = Runner(workspace=runs_dir.parent, acceptance_dir=HERE, runs_dir=runs_dir)
    rp, _ = r.prepare(sc, git_sha=git_sha)
    dirs = {a.name: rp.actor_dir(a.name) for a in sc.actors}
    v = judge(sc, dirs, rp.server_dir)
    write_verdict(rp, v)
    return rp, v


def promote_digest(rp: RunPaths, acceptance_dir: Path) -> Path:
    acc = Path(acceptance_dir)
    dest_dir = acc / "verdicts"
    dest_dir.mkdir(parents=True, exist_ok=True)
    sc_id = json.loads(rp.verdict.read_text(encoding="utf-8"))["scenario_id"]
    dest = dest_dir / f"{sc_id}.json"
    shutil.copy2(rp.verdict_digest, dest)
    return dest


def _cmd_run(args: argparse.Namespace) -> int:
    scen = Path(args.scenario)
    if not scen.is_absolute():
        scen = SCENARIOS / scen
    if not scen.exists() and scen.suffix == "":
        scen = scen.with_suffix(".yaml")
    runs_dir = Path(args.runs_dir)
    sha = _git_sha(runs_dir.parent)
    if args.dry_run:
        rp, v = dry_run(scen, runs_dir, sha)
        print(f"run-dir: {rp.run_dir}")
        print(f"status : {v.status()}")
        print(f"counts : {json.dumps(v.counts(), ensure_ascii=False)}")
        return 0
    # 真机路径：建 run → 提示人工操作 → 归集 → 判定。
    # 本步骤**不启动任何游戏进程**（那需要真人操作客户端，见 RUNBOOK.md）；
    # 它做的是"把一次人工 run 的产物收进来并判定"，因此真机 run 是两步：
    #   (1) 人工按 RUNBOOK 起服务端与客户端、完成动作、关掉；
    #   (2) 跑本命令的 `--collect` 形态做归集与判定。
    if args.collect:
        sc = load_scenario(scen)
        r = Runner(workspace=runs_dir.parent, acceptance_dir=HERE, runs_dir=runs_dir)
        rp = RunPaths.for_run(runs_dir, args.run_id)
        if not rp.manifest.exists():
            print(f"没有这个 run：{rp.manifest}", file=sys.stderr)
            return 1
        dirs = {a.name: rp.actor_dir(a.name) for a in sc.actors}
        v = judge(sc, dirs, rp.server_dir)
        write_verdict(rp, v)
        promote_digest(rp, HERE)
        print(f"run-dir: {rp.run_dir}")
        print(f"status : {v.status()}")
        print(f"counts : {json.dumps(v.counts(), ensure_ascii=False)}")
        return 0
    # 只建 run 目录并打印 runId，供人工 run 使用
    sc = load_scenario(scen)
    r = Runner(workspace=runs_dir.parent, acceptance_dir=HERE, runs_dir=runs_dir)
    rp, _ = r.prepare(sc, git_sha=sha)
    print(f"run-dir: {rp.run_dir}")
    print(f"run-id : {rp.run_dir.name}")
    print("下一步：按 tools/acceptance/RUNBOOK.md 人工完成动作，"
          "然后用 `run --collect --run-id <上行的 run-id>` 归集并判定。")
    return 0


def _cmd_judge(args: argparse.Namespace) -> int:
    """对已有 run 重新判定（产物已在盘上时用）。"""
    run_dir = Path(args.run_dir)
    scen_path = SCENARIOS / f"{args.scenario}.yaml"
    if not scen_path.exists():
        print(f"没有这个场景声明：{scen_path}", file=sys.stderr)
        return 1
    sc = load_scenario(scen_path)
    rp = RunPaths.for_run(run_dir.parent, run_dir.name)
    dirs = {a.name: rp.actor_dir(a.name) for a in sc.actors}
    v = judge(sc, dirs, rp.server_dir)
    write_verdict(rp, v)
    print(json.dumps({"status": v.status(), "counts": v.counts()},
                     ensure_ascii=False, indent=2))
    return 0 if v.status() != "red" else 1


def _cmd_replay(args: argparse.Namespace) -> int:
    run_dir = Path(args.run_dir)
    vp = run_dir / "verdict.json"
    if not vp.exists():
        print(f"没有判定文件：{vp}", file=sys.stderr)
        return 1
    payload = json.loads(vp.read_text(encoding="utf-8"))
    print(json.dumps({"status": payload["status"], "counts": payload["counts"]},
                     ensure_ascii=False, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="acceptance", description="S0 本机多客户端验收平台")
    sub = ap.add_subparsers(dest="cmd", required=True)

    p_run = sub.add_parser("run", help="建一次 run（或 --collect 归集并判定）")
    p_run.add_argument("--scenario", required=True)
    p_run.add_argument("--actors", type=int, default=2)
    p_run.add_argument("--dry-run", action="store_true")
    p_run.add_argument("--collect", action="store_true",
                       help="归集人工 run 的产物并判定（需配 --run-id）")
    p_run.add_argument("--run-id", default=None)
    p_run.add_argument("--runs-dir", default=str(Path.cwd() / "runs"))
    p_run.set_defaults(func=_cmd_run)

    p_j = sub.add_parser("judge", help="对已有 run 判定")
    p_j.add_argument("--run-dir", required=True)
    p_j.add_argument("--scenario", default="town-two-clients")
    p_j.set_defaults(func=_cmd_judge)

    p_r = sub.add_parser("replay", help="只读重放已有判定")
    p_r.add_argument("--run-dir", required=True)
    p_r.set_defaults(func=_cmd_replay)

    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
```

`tools/acceptance/verdicts/.gitkeep`：空文件（占位，让目录进 git）。

- [ ] **Step 5: 跑测试确认通过**

Run: 同 Step 3
Expected: 6 passed

- [ ] **Step 6: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/scenarios tools/acceptance/cli.py tools/acceptance/verdicts/.gitkeep \
        tools/acceptance/tests/test_cli_dry_run.py
git commit -m "验收平台S0：四个场景声明 + CLI 干跑链路" -- \
  tools/acceptance/scenarios tools/acceptance/cli.py tools/acceptance/verdicts/.gitkeep \
  tools/acceptance/tests/test_cli_dry_run.py
```

---

### Task 6: 真机一次 run（**需要人；这是 S0 的验收**）

**Files:**
- Create: `tools/acceptance/RUNBOOK.md`（真人操作步骤）
- Modify: `tools/acceptance/verdicts/town-two-clients.json`（由 `promote_digest` 生成）

**Interfaces:**
- Consumes: Task 1–5 全部
- Produces: 一次真实 run 的产物 + 提升后的判定

**前置检查（先做，避免白干）**：
- [ ] 客户端目录 `Config.ini` 有 `DumpNetwork=1`，且 `ijl15.dll` 是带 netdump 的那份（`STATUS.md` L1422）
- [ ] 没有官方容器在抢端口（`open-questions.md` 坑 14）：`Get-NetTCPConnection -State Listen -LocalPort 7001,2311`
      若被占，**先停占用者**（本项目服务端或 `docker stop dnf`），不要硬起

- [ ] **Step 1: 把活存档备份一份（人工 run 会改它，这是唯一的"不可复现"动作）**

```powershell
Copy-Item 'G:\Game\DNF_ACT1\DF\town-test\data\state.json' `
          "G:\Game\DNF_ACT1\DF\town-test\data\state.before-s0run.json" -Force
```

- [ ] **Step 2: 写 RUNBOOK.md（真人照做）**

必须写清（用实际命令，不留 TODO）：
1. 起服务端：`& 'G:\Game\DNF_ACT1\DF\town-test\DFLegacy.Server.exe'`（**工作目录必须是该目录**）；
2. 起客户端 A、B：用 `DF\town-test\DFLegacy.Launcher.exe` 各起一次，**两个账号不同**；
3. 两个客户端各自：登录 → 选角 → 进城；
4. A 走到 B 附近；
5. 记录**每步的墙上时间**（写进 `RUNBOOK.md` 的表格），供事后与 `offsets.json` 对齐；
6. 关掉两个客户端，**再**关服务端（保证日志写完）；
7. 跑归集与判定：
   ```powershell
   $env:PYTHONIOENCODING='utf-8'
   cd G:\Game\DNF_ACT1\DFLegacy\tools
   python -m acceptance.cli run --scenario town-two-clients --actors 2 --runs-dir ..\runs
   python -m acceptance.cli replay --run-dir ..\runs\<runId>
   ```
8. 把归一化判定提升进 git：
   ```powershell
   python -c "from pathlib import Path; from acceptance.cli import promote_digest; from acceptance.model import RunPaths; rp=RunPaths.for_run(Path(r'..\runs'), r'<runId>'); print(promote_digest(rp, Path('acceptance')))"
   ```

- [ ] **Step 3: 按 RUNBOOK 真跑一次，把真实结果落盘**

记录：`runId`、每步墙上时间、`verdict.json` 的三分计数、以及**哪几项落在"需人看"**。

- [ ] **Step 4: 核对 S0 验收判据（spec §5.3）**

1. **两次一致**：用 `--collect` 对同一次 run 再判一次（或按 RUNBOOK 再走一遍最短路径新建一个 run）
   ⇒ 两次 `verdict.digest.json` 的 `status` 与 `counts` 必须一致；
2. **能变红**：把 `town-two-clients.yaml` 里 `c2s:login-ok` 改成一个不存在的串（例如 `c2s:no-such-frame`），
   然后 `python -m acceptance.cli judge --run-dir ..\runs\<runId>` ⇒ 必须出现 `fail` 且 `status == "red"`
   （`judge` 在 red 时返回码 1，这本身就是可脚本化的判据）；**改回**并再判一次确认回到非红；
3. **自解释**：只看 `runs/<runId>/` 能回答"跑的是什么/谁参与/T0/哪几项要人看"。

- [ ] **Step 5: 提交**

```bash
cd /g/Game/DNF_ACT1/DFLegacy
git add tools/acceptance/RUNBOOK.md tools/acceptance/verdicts/town-two-clients.json
git commit -m "验收平台S0：真机 run 的 RUNBOOK 与归一化判定" -- \
  tools/acceptance/RUNBOOK.md tools/acceptance/verdicts/town-two-clients.json
```

---

## 明确不做（防范围蔓延）

- **窗口自动化**（驱动闭源客户端 UI）—— 独立大坑。
- **局域网** —— S4；本计划全部按 `127.0.0.1`。
- **多进程拆分** —— S2；`runner` 只需**能**编排多进程（`start_server` 可被调多次），不要求进程已拆。
- **1171 条协议地图** —— S3。
- **party15 崩溃修复** —— 所有者已明确不阻塞。
- **改产品代码**（`src/`、产品 `tests/`）—— 本计划只新增 `tools/acceptance/`。
- **`pip install` 任何东西** —— Task 0 若发现缺依赖，改选型而不是装包。
