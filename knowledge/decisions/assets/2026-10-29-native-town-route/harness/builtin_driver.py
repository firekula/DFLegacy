"""Drive one isolated DNF client solely through its IJL15 file interfaces.

No desktop automation, OS input/focus calls, packet injection, or memory writes.
The client itself handles the PID-scoped focus request and input timeline.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import time
from datetime import datetime


def stamp() -> str:
    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def atomic_text(path: Path, value: str) -> None:
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(value, encoding="utf-8")
    temporary.replace(path)


def wait_until(predicate, timeout_s: float) -> bool:
    deadline = time.monotonic() + timeout_s
    while True:
        if predicate():
            return True
        if time.monotonic() >= deadline:
            return False
        time.sleep(0.05)


def text_matches(path: Path, expected: str) -> bool:
    try:
        return path.read_text(encoding="utf-8").strip() == expected.strip()
    except (OSError, UnicodeError):
        return False


def image_snapshot(directory: Path) -> dict[str, tuple[int, int]]:
    snapshot = {}
    if directory.exists():
        for path in directory.iterdir():
            if path.is_file() and path.suffix.lower() in {".jpg", ".jpeg", ".png", ".bmp"}:
                info = path.stat()
                snapshot[path.name] = (info.st_mtime_ns, info.st_size)
    return snapshot


def load_steps(value: str) -> list[dict]:
    stripped = value.lstrip()
    if not stripped.startswith(("[", "{")):
        value = Path(value.removeprefix("@")).read_text(encoding="utf-8")
    parsed = json.loads(value)
    steps = parsed.get("steps") if isinstance(parsed, dict) else parsed
    if not isinstance(steps, list):
        raise ValueError("--steps-json must contain an array or an object with a steps array")
    result = []
    previous_t = -1
    for step in steps:
        if not isinstance(step, dict) or step.get("op") not in {"click", "rclick", "move", "key", "text", "drag"}:
            raise ValueError("Only built-in click, rclick, move, key, and text steps are accepted")
        if set(step) - {"t", "op", "vk", "x", "y", "dur", "chars", "x2", "y2"}:
            raise ValueError("Unknown built-in step fields")
        if not isinstance(step.get("t"), int) or step["t"] < 0 or step["t"] < previous_t:
            raise ValueError("Steps require nondecreasing nonnegative integer t values")
        if not isinstance(step.get("dur", 0), int) or step.get("dur", 0) < 0:
            raise ValueError("dur must be a nonnegative integer")
        if step["op"] == "key" and (not isinstance(step.get("vk"), int) or not 0 <= step["vk"] <= 255):
            raise ValueError("key requires vk in 0..255")
        if step["op"] in {"click", "rclick", "move", "drag"} and not all(isinstance(step.get(axis), int) for axis in ("x", "y")):
            raise ValueError("Mouse steps require integer client x/y coordinates")
        if step["op"] == "text" and not isinstance(step.get("chars"), str):
            raise ValueError("text requires chars")
        if step["op"] == "drag":
            if not (type(step.get("x2")) is int and 0 <= step["x2"] < 640 and type(step.get("y2")) is int and 0 <= step["y2"] < 480):
                raise ValueError("drag requires client destination x2/y2")
        elif "x2" in step or "y2" in step:
            raise ValueError("destination fields require drag")
        previous_t = step["t"]
        result.append(dict(step))
    return result


def end_ms(steps: list[dict]) -> int:
    return max((step["t"] + max(step.get("dur", 0), len(step.get("chars", "")) * 40)
                for step in steps), default=0)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client-dir", type=Path, required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--steps-json", default="[]", help="Inline JSON, JSON file path, or @file")
    parser.add_argument("--label", required=True)
    parser.add_argument("--out-dir", type=Path, required=True)
    parser.add_argument("--screenshot", action="store_true", help="Append built-in PrintScreen and copy its new image")
    parser.add_argument("--focus-timeout-s", type=float, default=15)
    parser.add_argument("--plan-timeout-s", type=float, default=None)
    args = parser.parse_args()
    if args.pid <= 0 or not 0 < args.focus_timeout_s <= 15:
        parser.error("pid must be positive and focus timeout must be in (0,15]")
    client = args.client_dir.resolve(strict=True)
    if not client.is_dir() or not (client / "DNF.exe").is_file():
        parser.error("--client-dir must be the directory containing DNF.exe")
    output = args.out_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    label = re.sub(r"[^\w.-]", "_", args.label).strip(".") or "step"
    record_path = output / f"{label}.json"
    diagnostic_path = output / f"{label}.diag.log"
    if record_path.exists() or diagnostic_path.exists() or any(output.glob(f"{label}.*")):
        parser.error("Preserve existing evidence; use a fresh label")
    diag = client / "auto-input-diag.log"
    try:
        diag_offset = diag.stat().st_size
    except FileNotFoundError:
        diag_offset = 0
    record = {"started": stamp(), "pid": args.pid, "client_dir": str(client),
              "label": args.label, "interface": "IJL15 file input only", "success": False}
    try:
        steps = load_steps(args.steps_json)
        if args.screenshot:
            steps.append({"t": end_ms(steps) + 500, "op": "key", "vk": 44, "dur": 200})
        if not steps:
            raise ValueError("Supply input steps or --screenshot")
        plan = client / "auto-input.json"
        completed = client / "auto-input.done.json"
        if plan.exists():
            raise RuntimeError("An input plan already exists; refusing to overwrite a potentially active plan")
        screenshot_dir = client / "ScreenShot"
        before_images = image_snapshot(screenshot_dir)
        focus = client / "auto-input.focus"
        focus_done = client / "auto-input.focus.done"
        focus_done.unlink(missing_ok=True)
        atomic_text(focus, str(args.pid))
        if not wait_until(lambda: not focus.exists() and text_matches(focus_done, str(args.pid)), args.focus_timeout_s):
            raise TimeoutError("Built-in PID-scoped focus was not acknowledged within the focus timeout")
        record["focus_acknowledged"] = stamp()
        completed.unlink(missing_ok=True)
        payload = json.dumps({"version": 1, "steps": steps}, ensure_ascii=False)
        record["steps"] = steps
        record["plan_sha256"] = hashlib.sha256(payload.encode("utf-8")).hexdigest()
        record["plan_written"] = stamp()
        atomic_text(plan, payload)
        timeout_s = args.plan_timeout_s or (end_ms(steps) / 1000 + 15)
        if timeout_s <= 0:
            raise ValueError("Plan timeout must be positive")
        last_focus_request = time.monotonic()
        record["focus_reassertions"] = 0

        def plan_finished() -> bool:
            nonlocal last_focus_request
            if not plan.exists() and text_matches(completed, payload):
                return True
            # Party scene changes can activate the other client. Reassert only
            # through the same built-in PID mailbox while this plan owns input.
            if time.monotonic() - last_focus_request >= 1 and not focus.exists():
                atomic_text(focus, str(args.pid))
                last_focus_request = time.monotonic()
                record["focus_reassertions"] += 1
            return False

        if not wait_until(plan_finished, timeout_s):
            raise TimeoutError("The exact fresh built-in plan did not complete within its timeout")
        record["plan_completed"] = stamp()
        if args.screenshot:
            candidates = []

            def find_new_image() -> bool:
                candidates.clear()
                current = image_snapshot(screenshot_dir)
                for name, info in current.items():
                    if info != before_images.get(name) and info[1] > 0:
                        candidates.append(screenshot_dir / name)
                return bool(candidates)

            if not wait_until(find_new_image, 10):
                raise TimeoutError("Built-in PrintScreen completed but no new ScreenShot image appeared")
            image = max(candidates, key=lambda path: path.stat().st_mtime_ns)
            # Wait briefly for the client's own image writer to release its file.
            screenshot_target = output / f"{label}{image.suffix.lower()}"
            screenshot_deadline = time.monotonic() + 5
            while True:
                try:
                    shutil.copy2(image, screenshot_target)
                    break
                except OSError:
                    if time.monotonic() >= screenshot_deadline:
                        raise
                    time.sleep(0.05)
            record["screenshot_source"] = str(image)
            record["screenshot"] = str(screenshot_target)
        record["success"] = True
    except Exception as error:
        record["error"] = f"{type(error).__name__}: {error}"
    finally:
        try:
            with diag.open("rb") as stream:
                stream.seek(diag_offset)
                fresh = stream.read().decode("utf-8", errors="replace")
            own_lines = [line for line in fresh.splitlines() if f"[{args.pid}]" in line]
            diagnostic_path.write_text("\n".join(own_lines) + "\n", encoding="utf-8")
            record["own_pid_diag_lines"] = len(own_lines)
            record["own_plan_armed"] = any("plan armed" in line for line in own_lines)
            if record.get("plan_completed") and not record["own_plan_armed"]:
                record["success"] = False
                record["error"] = "Completion marker lacks a fresh plan-armed diagnostic from the requested PID"
        except OSError as error:
            record["diag_error"] = str(error)
            if record.get("plan_completed"):
                record["success"] = False
        record["finished"] = stamp()
        record_path.write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(record, ensure_ascii=False, indent=2), flush=True)
    return 0 if record["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
