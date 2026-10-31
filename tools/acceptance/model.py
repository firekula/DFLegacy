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
        """至少一条期望**能失败**，否则该场景判定无效（防恒真判定，spec §5.4 / 硬约束 7）。

        判据按 `verdict.judge` 的实际语义：`[语义]` 档只出 needs-human、**永远不 fail**
        （spec §3.2）⇒ 它不算"能失败"的期望；只有 `[语义]` 期望的场景必须判无效。
        """
        return any(ex.level != "[语义]" for s in self.steps for ex in s.expects)


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
    servers: dict[str, dict] = field(default_factory=dict)
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
        "servers": {
            name: {k: v for k, v in entry.items() if k != "pid"}
            for name, entry in manifest.servers.items()
        },
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
