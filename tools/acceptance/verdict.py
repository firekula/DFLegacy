"""三分判定。

spec §3.2：`[存在]`/`[字节]` 自动判定；`[语义]` **自动标记为需人看**（不假装自动）；
产物缺失 ⇒ `missing-evidence`（**不是** fail —— fail 是"证据在、但不符"）。
"产物"按**证据类**收窄（`[抓包]` = netdump），且本平台自己写的 `launcher.log` 永不算证据。
spec §5.4 + Global Constraints：场景若没有任何期望能失败 ⇒ 判定**无效**（防恒真判定）。
"""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Optional

from .model import RESULT_LEVELS, Expect, Scenario, RunPaths

# 供调用方从本模块取用（契约里声明了 `verdict.RESULT_LEVELS`）
__all__ = ["RESULT_LEVELS", "GREP_SOURCES", "EVIDENCE_SOURCES", "PLATFORM_METADATA_FILES",
           "Verdict", "VerdictEntry", "judge", "digest", "write_verdict"]

# 证据来源种类 → 搜索域 glob。这是**产物形态**的名字，**不是**六类证据记号
# （[静态]/[抓包]/[真机]/[回放]/[推断]/[未验证]），也不是判定档（[存在]/[字节]/[语义]）。
# `judge` 真的用它选搜索域（终审 I-2：它曾经是"声明了全仓没人用"的死常量）。
GREP_SOURCES: dict[str, str] = {"netdump": "netdump-*.log", "all": "*.log", "server": "server-*.log"}

# 六类证据记号 → 来源种类。只给**产物形态确定**的类别登记：`[抓包]` 就是 net_dump.cpp 写的
# netdump（STATUS.md L1424：每客户端一个文件）；`[静态]` 是服务端侧期望（逐进程日志 server-*.log）。
# 其余类别（[真机]/[回放]/[推断]/[未验证]）的产物形态未定 ⇒ 按 all 处理，不假装知道它们长什么样（硬约束 8：不编证据）。
EVIDENCE_SOURCES: dict[str, str] = {"[抓包]": "netdump", "[静态]": "server"}
DEFAULT_SOURCE = "all"


def is_server_expect(expect: Expect) -> bool:
    """`[静态]` 标注的服务端侧期望：pattern 在 server_dir 的 `server-*.log` 里找。"""
    return expect.evidence == "[静态]"

# 本平台自己写的元数据，**永不算证据**：cli.py 的 on_started 往每个 actor 目录写 launcher.log
# （PID 归因的中间产物）。把它当证据会两头出错（终审 I-2 实测 P1/P4）：
# netdump 缺失被判成 fail（"产品帧没出现"的错误诊断）、pattern 误命中被判成假 pass。
PLATFORM_METADATA_FILES: frozenset[str] = frozenset({"launcher.log"})


def evidence_glob(expect: Expect) -> str:
    """该期望的搜索域 glob：来源由 `expect.evidence`（六类记号）决定，未声明/未知 = 全部日志。"""
    return GREP_SOURCES[EVIDENCE_SOURCES.get(expect.evidence or "", DEFAULT_SOURCE)]


def _evidence_files(d: Path, expect: Expect) -> list[Path]:
    """该目录里该期望**可作证据**的文件：按来源种类收窄 + 排除平台元数据。"""
    return [f for f in sorted(Path(d).glob(evidence_glob(expect)))
            if f.name not in PLATFORM_METADATA_FILES]


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


def _search(files: list[Path], pattern: str) -> Optional[tuple[Path, int, str]]:
    """在给定的**证据文件**里逐个搜 pattern；返回 (文件, 行号, 行内容) 或 None。

    搜哪些文件由调用方按证据类选好（`_evidence_files`）—— 本函数不自己 glob。
    """
    for f in files:
        with Path(f).open(encoding="utf-8", errors="replace") as fh:
            for i, line in enumerate(fh, start=1):
                if pattern in line:
                    return Path(f), i, line.rstrip("\n")
    return None


def judge(scenario: Scenario, artifact_dirs: dict[str, Path], server_dir: Path) -> Verdict:
    if not scenario.has_falsifiable_expect():
        return Verdict(scenario_id=scenario.id,
                       invalid_reason=("场景没有任何能失败的期望 ⇒ 判定无效（防恒真判定）："
                                       "`[语义]` 档只出 needs-human、永远不 fail，不算可失败期望"))

    entries: list[VerdictEntry] = []
    for step in scenario.steps:
        d = artifact_dirs.get(step.actor)
        for ex in step.expects:
            if ex.level == "[语义]":
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "needs-human",
                    detail="语义断言：需人看；升级为自动需先有 golden bytes（spec §3.2）"))
                continue
            if is_server_expect(ex):
                entries.append(_judge_server_expect(step, ex, server_dir))
                continue
            if d is None or not Path(d).exists():
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "missing-evidence",
                    detail=f"actor {step.actor!r} 的产物目录不存在：{d}"))
                continue
            # 该来源**有没有**可作证据的产物 —— 这决定"缺证据"还是"证据在但不符"
            files = _evidence_files(Path(d), ex)
            if not files:
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "missing-evidence",
                    detail=(f"actor {step.actor!r} 的产物目录 {Path(d)} 下没有可作证据的文件"
                            f"（来源 glob {evidence_glob(ex)!r}；"
                            f"平台元数据 {sorted(PLATFORM_METADATA_FILES)} 不算证据）")))
                continue
            pattern = ex.pattern if ex.pattern is not None else ex.description
            hit = _search(files, pattern)
            if hit is None:
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "fail",
                    detail=(f"在 {Path(d)} 的证据文件（{evidence_glob(ex)!r}）里"
                            f"找不到 {pattern!r}")))
            else:
                f, i, line = hit
                entries.append(VerdictEntry(
                    step.actor, step.action, ex.description, ex.level, "pass",
                    detail=f"{f.name}:{i} {line[:80]}"))
    return Verdict(scenario_id=scenario.id, entries=entries)


def _judge_server_expect(step, ex: Expect, server_dir: Path) -> VerdictEntry:
    """服务端侧期望：从 `server_dir` 的 `server-*.log` 搜 pattern（`server_dir` 真正被读）。"""
    sdir = Path(server_dir) if server_dir is not None else None
    if sdir is None or not sdir.exists():
        return VerdictEntry(
            step.actor, step.action, ex.description, ex.level, "missing-evidence",
            detail=f"服务端产物目录不存在：{server_dir}")
    files = _evidence_files(sdir, ex)
    if not files:
        return VerdictEntry(
            step.actor, step.action, ex.description, ex.level, "missing-evidence",
            detail=(f"服务端产物目录 {sdir} 下没有可作证据的文件（来源 glob {evidence_glob(ex)!r}）"))
    pattern = ex.pattern if ex.pattern is not None else ex.description
    hit = _search(files, pattern)
    if hit is None:
        return VerdictEntry(
            step.actor, step.action, ex.description, ex.level, "fail",
            detail=(f"在 {sdir} 的证据文件（{evidence_glob(ex)!r}）里找不到 {pattern!r}"))
    f, i, line = hit
    return VerdictEntry(
        step.actor, step.action, ex.description, ex.level, "pass",
        detail=f"{f.name}:{i} {line[:80]}")


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