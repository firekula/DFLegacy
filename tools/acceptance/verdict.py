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
            # 检查是否有任何 .log 文件可搜（区分"缺证据"与"证据在但不符"）
            log_files = sorted(Path(d).glob("*.log"))
            if not log_files:
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "missing-evidence",
                    detail=f"actor {step.actor!r} 的产物目录 {Path(d)} 下没有 *.log 文件"))
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