"""S0 唯一入口。

  run    <scenario> --actors N [--dry-run]     建 run（+起进程；--dry-run 不起任何游戏进程）
  judge  --run-dir <dir>                        对已有 run 重新判定
  replay --run-dir <dir>                        只读重放：打印已有判定（不改盘）

`promote_digest` 把归一化判定提升到 `tools/acceptance/verdicts/<scenario>.json` —— **这一步才进 git**。

`run --collect`（controller ruling）把一次人工 run 的产物归集进 run 目录再判定：每个 actor 的
`netdump-<pid>.log` 取自 `--client-dir`（pid 由 `--actor-pid A=1234,B=5678` 给出，名字必须匹配
场景 actor）；可选 `--server-log` **显式单文件**复制服务端日志（绝不整目录 glob）；时钟偏移写入
`offsets.json`，其中 **server_t0 以 manifest.created_utc 近似（= 控制端时钟）**，netdump 时间戳
是**本地时间**（net_dump.cpp 用 GetLocalTime），按本机时区换算成 UTC 再参与偏移运算
（与 collector.collect_actor 记录的 t0_utc 一致；不许 replace(tzinfo=UTC) 误标）。
"""
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from datetime import datetime, timezone
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


def _parse_actor_pids(spec: str, known: set[str]) -> dict[str, str]:
    """解析 `--actor-pid A=1234,B=5678` 为 name->pid。名字必须匹配场景 actor（不许猜）。
    未列出的 actor 按"无产物"处理（判定会给 missing-evidence）。"""
    out: dict[str, str] = {}
    for part in spec.split(","):
        part = part.strip()
        if not part:
            continue
        if "=" not in part:
            raise ValueError(f"--actor-pid 条目不是 name=pid：{part!r}")
        name, pid = (x.strip() for x in part.split("=", 1))
        if name not in known:
            raise ValueError(f"--actor-pid 里的 actor {name!r} 不在场景中：{sorted(known)}")
        if not pid.isdigit():
            raise ValueError(f"--actor-pid 的 pid 不是数字：{pid!r}")
        out[name] = pid
    return out


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
        if not args.client_dir or not args.actor_pid:
            print("--collect 需要 --client-dir 与 --actor-pid（A=1234,B=5678）", file=sys.stderr)
            return 2
        client_dir = Path(args.client_dir)
        try:
            pids = _parse_actor_pids(args.actor_pid, {a.name for a in sc.actors})
        except ValueError as e:
            print(str(e), file=sys.stderr)
            return 2
        # server_t0 以 manifest.created_utc 近似（= 控制端时钟，见模块 docstring）；
        # netdump 时间戳是本地时间（GetLocalTime）⇒ 换算成 UTC 再算偏移（不许贴 UTC 标签）。
        server_t0 = datetime.strptime(
            json.loads(rp.manifest.read_text(encoding="utf-8"))["created_utc"],
            "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
        offsets: dict[str, float | None] = {}
        for a in sc.actors:
            pid = pids.get(a.name)
            source = client_dir / f"netdump-{pid}.log" if pid is not None else None
            if source is not None and not source.exists():
                source = None  # 缺失不猜：该 actor 无产物，判定会给 missing-evidence
            collect_actor(rp, a.name, source)
            t0 = parse_netdump_t0(source) if source is not None else None
            if t0 is not None:
                t0 = t0.astimezone(timezone.utc)  # naive 本地时间 → UTC（astimezone 从本机时区换算）
            offsets[a.name] = clock_offset(server_t0, t0)
        record_offsets(rp, offsets)
        if args.server_log:
            server_log = Path(args.server_log)
            if server_log.exists():
                rp.server_dir.mkdir(parents=True, exist_ok=True)
                shutil.copy2(server_log, rp.server_dir / server_log.name)  # 显式单文件复制
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
    p_run.add_argument("--client-dir", default=None,
                       help="客户端目录（netdump-<pid>.log 所在；--collect 用）")
    p_run.add_argument("--actor-pid", default=None,
                       help="归集映射 A=1234,B=5678，名字须匹配场景 actor（--collect 用）")
    p_run.add_argument("--server-log", default=None,
                       help="单个服务端日志文件，显式复制进 run（可选）")
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