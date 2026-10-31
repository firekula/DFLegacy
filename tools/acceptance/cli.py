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

from .actor_pid import AmbiguousPIDError, LauncherFailedError, PIDTimeoutError
from .collector import (clock_offset, collect_actor, collect_server, find_netdumps,
                        parse_netdump_t0, record_offsets)
from .model import RunPaths, load_scenario
from .orchestrate import (capture_client_pid, launcher_command,
                          parse_actor_accounts, write_steps)
from .runner import Runner, ServerStartupError, _is_split_server, isolate_state
from .verdict import Verdict, digest, judge, write_verdict
from .state_seed import seed_state as clone_seed_state

HERE = Path(__file__).resolve().parent
SCENARIOS = HERE / "scenarios"


def _confirm(message: str) -> None:
    """等真人完成一步 UI 动作。测试可 monkeypatch。"""
    input(message + "（完成后按回车）")


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


def _client_prereqs(client_dir: Path) -> list[str]:
    """返回前置问题清单（空列表=通过）。不启动任何进程。"""
    problems: list[str] = []
    dnf = client_dir / "DNF.exe"
    ijl = client_dir / "ijl15.dll"
    cfg = client_dir / "Config.ini"
    if not dnf.is_file():
        problems.append(f"缺少客户端可执行：{dnf}")
    if not ijl.is_file():
        problems.append(f"缺少 ijl15.dll：{ijl}")
    if not cfg.is_file():
        problems.append(f"缺少 Config.ini：{cfg}")
    else:
        text = cfg.read_text(encoding="utf-8", errors="replace").replace(" ", "")
        if "DumpNetwork=1" not in text:
            problems.append(f"{cfg} 未设置 DumpNetwork=1（netdump 不会产生）")
    return problems


def _orchestration_paths(args: argparse.Namespace) -> dict:
    client_dir = Path(args.client_dir)
    launcher_exe = (Path(args.launcher_exe) if args.launcher_exe
                    else client_dir / "town-test" / "DFLegacy.Launcher.exe")
    server_exe = (Path(args.server_exe) if args.server_exe
                  else client_dir / "town-test" / "DFLegacy.Server.exe")
    channel_exe = (Path(args.channel_exe) if args.channel_exe
                   else server_exe.parent / "DFLegacy.Channel.exe")
    launcher_log = (Path(args.launcher_log) if args.launcher_log
                    else launcher_exe.parent / "launcher.log")
    return {"client_dir": client_dir, "launcher_exe": launcher_exe,
            "server_exe": server_exe, "channel_exe": channel_exe,
            "launcher_log": launcher_log}


def orchestrate(scenario_path: Path, runs_dir: Path, git_sha: str, *,
                actor_accounts: str, paths: dict, expected_actors: int | None = None,
                server_start_timeout: float = 300.0, seed_state: Path | None = None) -> int:
    """一条命令：建 run + 隔离存档 + 起服务端与 N 个 actor + 真人 UI + 归集判定。

    存档隔离靠 `DFLegacy__DataPath` 指向 run 私有副本（绝不写活存档）；
    真实客户端 PID 从 launcher.log **本次新增**行归因，不允许人工抄或猜。
    `server_start_timeout`：等服务端开始监听的上限。默认 300 秒而不是 60：
    [真机 2026-10-18] 冷缓存下索引 48343 个脚本可能远超 60 秒，而 .NET 输出按块缓冲、
    不满 4KB 不落盘 ⇒ 日志会显示"0 字节"，60 秒超时会把一次正常的冷启动误判成失败。
    """
    sc = load_scenario(scenario_path)
    names = {a.name for a in sc.actors}
    if expected_actors is not None and expected_actors != len(sc.actors):
        print(f"--actors {expected_actors} 与场景 {sc.id} 声明的 {len(sc.actors)} 个 actor 不符",
              file=sys.stderr)
        return 2
    try:
        accounts = parse_actor_accounts(actor_accounts, names)
    except ValueError as e:
        print(str(e), file=sys.stderr)
        return 2
    if set(accounts) != names:
        print(f"--actor-account 必须覆盖全部 actor：需要 {sorted(names)}，"
              f"得到 {sorted(accounts)}", file=sys.stderr)
        return 2
    client_dir = paths["client_dir"]
    launcher_exe, server_exe = paths["launcher_exe"], paths["server_exe"]
    channel_exe = paths["channel_exe"]
    launcher_log = paths["launcher_log"]
    problems = _client_prereqs(client_dir)
    for p in (launcher_exe, server_exe, server_exe.parent / "server.json"):
        if not p.is_file():
            problems.append(f"缺少文件：{p}")
    if problems:
        for p in problems:
            print(p, file=sys.stderr)
        return 2

    # run 目录必须**绝对化**：`DFLegacy__DataPath` 会作为字符串交给服务端，而服务端用的是
    # AppContext.BaseDirectory（exe 目录），**不是**它的工作目录 —— 相对路径会被解析到别处
    # （实测：`--runs-dir ..\runs` 让服务端把存档写进 `DF\runs\<runId>\server\`，
    #  run 私有副本原封不动 ⇒ 存档隔离静默失效、活存档反而暴露在错误目录树下）。
    runs_dir = Path(runs_dir).resolve()
    client_dir = Path(client_dir).resolve()
    launcher_exe, server_exe = Path(launcher_exe).resolve(), Path(server_exe).resolve()
    channel_exe = Path(channel_exe).resolve()
    launcher_log = Path(launcher_log).resolve()

    server_json = server_exe.parent / "server.json"
    split = _is_split_server(server_json)
    if split and not channel_exe.is_file():
        print(f"拆分模式缺少 channel 可执行：{channel_exe}", file=sys.stderr)
        return 2

    r = Runner(workspace=runs_dir.parent, acceptance_dir=HERE, runs_dir=runs_dir)
    rp, _ = r.prepare(sc, git_sha=git_sha)
    try:
        seed_record = clone_seed_state(server_json, rp.server_dir, seed_state=seed_state)
    except (OSError, ValueError) as error:
        print(f"无法准备隔离存档：{error}", file=sys.stderr)
        return 2
    (rp.server_dir / "state-seed.json").write_text(
        json.dumps(seed_record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    shutil.copy2(server_json, rp.server_dir / "server.json")
    run_state = rp.server_dir / "state.json"
    client_pids: dict[str, int | None] = {}
    try:
        if split:
            # 拆分模式：channel 先起（它接管 Entrance，客户端从入口端口进），再起 game；
            # 两进程共读同一份 server.json，就绪由 wait_until_listening 按进程各自判活。
            r.start_server(rp, exe=channel_exe, workdir=server_exe.parent,
                           name="channel",
                           server_json=server_json,
                           env={"DFLegacy__DataPath": str(run_state)},
                           log_path=rp.server_dir / "server-channel.log")
            r.start_server(rp, exe=server_exe, workdir=server_exe.parent,
                           name="game",
                           server_json=server_json,
                           env={"DFLegacy__DataPath": str(run_state)},
                           log_path=rp.server_dir / "server-game.log")
        else:
            r.start_server(rp, exe=server_exe, workdir=server_exe.parent,
                           name="game",
                           server_json=server_json,
                           env={"DFLegacy__DataPath": str(run_state)},
                           log_path=rp.server_dir / "server-game.log")
        r.record_run_config(rp, [{"key": "DFLegacy__DataPath",
                                  "old": seed_record["source"], "new": str(run_state)}])
        # 必须等服务端真的开始监听再起 actor：launcher 在 StartServer=false 时只探一次端口
        # （src/DFLegacy.Launcher/Program.cs:336-347），而真服务端要约 5.4 秒才监听（2026-10-15 实测）。
        listen = r.wait_until_listening(rp, sc,
                                        server_json=server_json,
                                        timeout_s=server_start_timeout,
                                        on_tick=lambda waited: print(
                                            f"  仍在等待服务端开始监听… 已等 {waited:.0f}s"
                                            f"（冷启动时要索引 48343 个脚本，日志未落盘属正常）",
                                            file=sys.stderr))
        if listen.get("skipped"):
            print(f"警告：未等待服务端监听（{listen.get('reason')}）", file=sys.stderr)
        elif "processes" in listen:
            for proc in listen["processes"]:
                print(f"等待 {proc['name']} 服务端在 {proc['host']}:{proc['port']} 开始监听："
                      f"已就绪（{proc['waited_ms']} ms）")
        else:
            print(f"等待服务端在 {listen['host']}:{listen['port']} 开始监听："
                  f"已就绪（{listen['waited_ms']} ms）")
        seen_offsets: dict[str, int] = {}

        def before_start(actor) -> None:
            seen_offsets[actor.name] = (launcher_log.stat().st_size
                                        if launcher_log.exists() else 0)

        def on_started(actor, proc):
            pid, line, _ = capture_client_pid(launcher_log, seen_offsets[actor.name])
            (rp.actor_dir(actor.name) / "launcher.log").write_text(line + "\n",
                                                                  encoding="utf-8")
            return pid

        cmds = {a.name: launcher_command(launcher_exe, accounts[a.name])
                for a in sc.actors}  # 按场景声明顺序构造（集合顺序随哈希随机，不得使用）
        recs = r.start_actors(rp, sc, actor_commands=cmds,
                              before_start=before_start, on_started=on_started)
        client_pids = {rec["name"]: rec.get("client_pid") for rec in recs}
        print(f"run-dir: {rp.run_dir}")
        print("两个客户端已由 Launcher 无头启动；请按提示完成 UI 动作（选角/进城/靠近）。")
        entries: list[dict] = []
        total = len(sc.steps)
        for index, step in enumerate(sc.steps, start=1):
            _confirm(f"[{index}/{total}] 客户端 {step.actor}：{step.action}")
            entries.append({"actor": step.actor, "index": index, "action": step.action,
                            "at_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")})
        write_steps(rp, entries)
        _confirm("采集结束：即将关闭客户端与服务端")
    except KeyboardInterrupt:
        # Ctrl+C 是操作者中止：明确退出而不是栈崩（真机实测 2026-10-15 会吐 KeyboardInterrupt
        # traceback）。子进程由下面的 finally 回收。
        print("已中止（Ctrl+C）：正在回收已启动的进程", file=sys.stderr)
        return 130
    except (AmbiguousPIDError, LauncherFailedError, PIDTimeoutError,
            ServerStartupError, OSError, ValueError) as e:
        # 多条启动行（AmbiguousPIDError）与服务端没起来（ServerStartupError，消息带日志尾部）
        # 都走这条路：明确失败 + rc=1，绝不猜 PID（账本 L290）。
        print(f"编排失败：{e}", file=sys.stderr)
        return 1
    finally:
        r.stop_all()

    server_t0 = datetime.strptime(
        json.loads(rp.manifest.read_text(encoding="utf-8"))["created_utc"],
        "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    offsets: dict[str, float | None] = {}
    for a in sc.actors:
        pid = client_pids.get(a.name)
        source = client_dir / f"netdump-{pid}.log" if pid else None
        if source is not None and not source.exists():
            print(f"警告：未找到 actor {a.name} 的 netdump：{source}", file=sys.stderr)
            source = None
        collect_actor(rp, a.name, source)
        t0 = parse_netdump_t0(source) if source is not None else None
        if t0 is not None:
            t0 = t0.astimezone(timezone.utc)
        offsets[a.name] = clock_offset(server_t0, t0)
    record_offsets(rp, offsets)
    dirs = {a.name: rp.actor_dir(a.name) for a in sc.actors}
    v = judge(sc, dirs, rp.server_dir)
    write_verdict(rp, v)
    promote_digest(rp, HERE)
    print(f"run-dir: {rp.run_dir}")
    print(f"status : {v.status()}")
    print(f"counts : {json.dumps(v.counts(), ensure_ascii=False)}")
    return 0


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
        if not args.run_id or not args.client_dir or not args.actor_pid:
            print("--collect 需要 --run-id、--client-dir 与 --actor-pid（A=1234,B=5678）", file=sys.stderr)
            return 2
        sc = load_scenario(scen)
        rp = RunPaths.for_run(runs_dir, args.run_id)
        if not rp.manifest.exists():
            print(f"没有这个 run：{rp.manifest}", file=sys.stderr)
            return 1
        client_dir = Path(args.client_dir)
        try:
            pids = _parse_actor_pids(args.actor_pid, {a.name for a in sc.actors})
        except ValueError as e:
            print(str(e), file=sys.stderr)
            return 2
        server_log = Path(args.server_log) if args.server_log else None
        if server_log is not None and not server_log.is_file():
            print(f"--server-log 文件不存在或不是文件：{server_log}", file=sys.stderr)
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
        if server_log is not None:
            dest = rp.server_dir / server_log.name
            if server_log.resolve() != dest.resolve():
                rp.server_dir.mkdir(parents=True, exist_ok=True)
                shutil.copy2(server_log, dest)  # 显式单文件复制
        dirs = {a.name: rp.actor_dir(a.name) for a in sc.actors}
        v = judge(sc, dirs, rp.server_dir)
        write_verdict(rp, v)
        promote_digest(rp, HERE)
        print(f"run-dir: {rp.run_dir}")
        print(f"status : {v.status()}")
        print(f"counts : {json.dumps(v.counts(), ensure_ascii=False)}")
        return 0
    if args.actor_account:
        if not args.client_dir:
            print("一条命令需要 --client-dir（客户端目录，含 DNF.exe/Config.ini/netdump）",
                  file=sys.stderr)
            return 2
        return orchestrate(scen, runs_dir, sha, actor_accounts=args.actor_account,
                           paths=_orchestration_paths(args), expected_actors=args.actors,
                           server_start_timeout=args.server_start_timeout,
                           seed_state=Path(args.seed_state) if args.seed_state else None)
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
                       help="客户端目录（netdump-<pid>.log 所在；--collect 与一条命令共用）")
    p_run.add_argument("--seed-state", default=None,
                       help="显式选择种子存档（如上一轮关闭保存）；只复制到新run，不回写")
    p_run.add_argument("--actor-account", default=None,
                       help="一条命令用：A=alias1,B=alias2（账号别名；密码只留在 launcher.json）")
    p_run.add_argument("--launcher-exe", default=None,
                       help="默认 <client-dir>/town-test/DFLegacy.Launcher.exe")
    p_run.add_argument("--server-exe", default=None,
                       help="默认 <client-dir>/town-test/DFLegacy.Server.exe")
    p_run.add_argument("--channel-exe", default=None,
                       help="默认 <server-exe 同目录>/DFLegacy.Channel.exe（拆分模式用）")
    p_run.add_argument("--launcher-log", default=None,
                       help="默认 <launcher-exe 同目录>/launcher.log")
    p_run.add_argument("--actor-pid", default=None,
                       help="归集映射 A=1234,B=5678，名字须匹配场景 actor（--collect 用）")
    p_run.add_argument("--server-log", default=None,
                       help="单个服务端日志文件，显式复制进 run（可选）")
    p_run.add_argument("--runs-dir", default=str(Path.cwd() / "runs"))
    p_run.add_argument("--server-start-timeout", type=float, default=300.0,
                       help="等服务端开始监听的上限秒数（默认 300；冷缓存索引脚本可能很久）")
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