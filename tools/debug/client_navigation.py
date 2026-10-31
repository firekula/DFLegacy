"""Set a current-area native Town destination through debug.dll's file API."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import struct
import time
import uuid

try:
    from . import client_state as state
except ImportError:
    import client_state as state


WIRE = struct.Struct("<4sHHIIQQQQQQIIIIiiii")
MAX_RESPONSE_BYTES = 16_384
RESPONSE_SCHEMA = "dfl-client-navigation/v1"
_REQUEST_FIELDS = ("pid", "uid", "process_started_filetime", "request_id", "issued_tick_ms",
                   "snapshot_tick_ms", "generation", "snapshot_sequence", "actor_token", "map_token",
                   "user_token", "context_token", "town_id", "area_id", "x", "y")


class NavigationError(ValueError):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def _require(condition, code):
    if not condition:
        raise NavigationError(code)


def _token(value):
    return isinstance(value, str) and state._TOKEN.fullmatch(value) is not None and int(value, 16) != 0


def _checked_snapshot(snapshot):
    try:
        state._validate(snapshot)
    except state.SnapshotError as error:
        raise NavigationError(error.code) from error
    _require(snapshot["schema"] == state.SCHEMA_V2 and snapshot["scene"] == 1, "town_required")
    _require(snapshot["town"]["position"] is not None and snapshot["me"]["position"] is not None,
             "position_unknown")
    return snapshot


def make_request(snapshot, *, x, y, request_id, issued_tick_ms):
    """Encode one validated request. This function performs no IO or native call."""
    _checked_snapshot(snapshot)
    _require(state._integer(x, -(1 << 31), (1 << 31) - 1)
             and state._integer(y, -(1 << 31), (1 << 31) - 1), "bad_target")
    _require(state._integer(request_id, 1) and state._integer(issued_tick_ms), "bad_request_identity")
    captured = snapshot["captured_tick_ms"]
    _require(captured <= issued_tick_ms and issued_tick_ms - captured <= 750, "stale_snapshot")
    me, town = snapshot["me"], snapshot["town"]
    values = (snapshot["pid"], me["uid"], snapshot["process_started_filetime"], request_id,
              issued_tick_ms, captured, snapshot["generation"], snapshot["sequence"],
              int(me["actor_token"], 16), int(me["map_token"], 16), int(me["user_token"], 16),
              int(town["context_token"], 16), town["town_id"], town["area_id"], x, y)
    raw = WIRE.pack(b"DLNV", 1, WIRE.size, *values)
    parse_request(raw)
    return raw


def parse_request(raw):
    """Decode the exact native LE wire without accepting partial or foreign files."""
    _require(isinstance(raw, bytes) and len(raw) == WIRE.size, "bad_request_size")
    magic, version, size, *values = WIRE.unpack(raw)
    _require(magic == b"DLNV" and version == 1 and size == 96, "bad_request_header")
    request = dict(zip(_REQUEST_FIELDS, values))
    _require(0 < request["pid"] <= 0xFFFFFFFF and 0 < request["uid"] <= 65535
             and all(request[key] > 0 for key in ("process_started_filetime", "request_id",
                                                  "generation", "snapshot_sequence"))
             and all(request[key] != 0 for key in ("actor_token", "map_token", "user_token", "context_token"))
             and request["town_id"] >= 0 and request["area_id"] >= 0, "bad_request_identity")
    _require(request["snapshot_tick_ms"] <= request["issued_tick_ms"]
             and request["issued_tick_ms"] - request["snapshot_tick_ms"] <= 750, "stale_snapshot")
    return request


def _read_json(path):
    try:
        with Path(path).open("rb") as stream:
            raw = stream.read(MAX_RESPONSE_BYTES + 1)
        _require(len(raw) <= MAX_RESPONSE_BYTES, "oversized_response")
        value = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=state._pairs,
                           parse_constant=state._constant)
        _require(isinstance(value, dict), "bad_response")
        return value
    except NavigationError:
        raise
    except (OSError, UnicodeError, ValueError, RecursionError) as error:
        raise NavigationError("unreadable_response") from error


def validate_response(value, request, now_tick_ms):
    """Validate an exact matching, recent native result; requested != arrived."""
    _require(isinstance(value, dict) and value.get("schema") == RESPONSE_SCHEMA, "bad_response")
    for key in ("pid", "process_started_filetime", "request_id", "uid", "generation", "tick_ms"):
        _require(state._integer(value.get(key)), "bad_response")
    _require(value.get("source") == "debug"
             and value.get("status") in ("requested", "arrived", "rejected", "interrupted")
             and isinstance(value.get("reason"), str) and len(value["reason"]) <= 128, "bad_response")
    for key in ("pid", "process_started_filetime", "request_id", "uid", "generation", "town_id", "area_id"):
        _require(type(value.get(key)) is int and value[key] == request[key], "response_binding_mismatch")
    for key in ("actor_token", "map_token"):
        _require(_token(value.get(key)) and int(value[key], 16) == request[key], "response_binding_mismatch")
    _require(state._integer(now_tick_ms) and value["tick_ms"] <= now_tick_ms, "future_response")
    _require(now_tick_ms - value["tick_ms"] <= 1000, "stale_response")
    target = value.get("target")
    _require(isinstance(target, dict) and all(type(target.get(k)) is int and target[k] == request[k]
                                           for k in ("x", "y")), "response_binding_mismatch")
    position = value.get("position")
    _require(position is None or isinstance(position, dict)
             and all(state._finite(position.get(k)) for k in ("x", "y")), "bad_response_position")
    return value


def _context(snapshot):
    me, town = snapshot["me"], snapshot["town"]
    return (snapshot["generation"], me["uid"], me["user_token"], me["actor_token"], me["map_token"],
            town["context_token"], town["town_id"], town["area_id"])


def _arrived(snapshot, request):
    town = snapshot["town"]
    return (town["destination"] == dict(x=request["x"], y=request["y"])
            and town["direction_raw"] == dict(x=4, y=4)
            and all(abs(town["position"][k] - request[k]) <= 3 for k in ("x", "y")))


@contextmanager
def _controller_lock(path):
    owner = f"{os.getpid()}:{uuid.uuid4().hex}".encode("ascii")
    try:
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError as error:
        raise NavigationError("controller_busy") from error
    except OSError as error:
        raise NavigationError("lock_unavailable") from error
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(owner)
        yield
    finally:
        try:
            with path.open("rb") as stream:
                still_owned = stream.read(len(owner) + 1) == owner
            if still_owned:
                path.unlink()
        except OSError:
            pass


def _atomic_write(path, data):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    except OSError as error:
        raise NavigationError("request_write_failed") from error
    finally:
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


def _existing_request_id(command, result, pid, read_result=None):
    highest = 0
    if command.exists():
        try:
            with command.open("rb") as stream:
                request = parse_request(stream.read(WIRE.size + 1))
        except OSError as error:
            raise NavigationError("existing_request_unreadable") from error
        _require(request["pid"] == pid, "existing_request_pid_mismatch")
        highest = request["request_id"]
    if result.exists():
        value = (read_result or _read_json)(result)
        _require(value.get("schema") == RESPONSE_SCHEMA and type(value.get("pid")) is int
                 and value["pid"] == pid and state._integer(value.get("request_id")), "bad_existing_response")
        highest = max(highest, value["request_id"])
    return highest


def execute(directory, *, pid, uid, started, x, y, wait="arrived", timeout_seconds=35, request_id=None):
    """Publish through local files, then observe native result and fresh state."""
    directory = Path(directory)
    _require(directory.is_dir(), "directory_missing")
    _require(state._integer(pid, 1, 0xFFFFFFFF) and state._integer(uid, 1, 65535)
             and state._integer(started, 1), "bad_identity")
    _require(wait in ("accepted", "arrived") and state._finite(timeout_seconds)
             and 0 <= timeout_seconds <= 60, "bad_timeout")
    command = directory / f"dfdebug-navigation-{pid}.bin"
    result = directory / f"dfdebug-navigation-{pid}.json"
    snapshot_path = directory / f"dfdebug-client-{pid}.json"

    def identity():
        try:
            actual = state.get_process_started_filetime(pid)
        except state.SnapshotError as error:
            raise NavigationError(error.code) from error
        _require(actual == started, "process_start_mismatch")

    def capture():
        read_deadline = time.monotonic() + .25
        for attempt in range(6):
            identity()
            try:
                snapshot = state.load_snapshot(snapshot_path, pid=pid, process_started_filetime=started,
                                               now_tick_ms=state.now_tick_ms())
            except state.SnapshotError as error:
                remaining = read_deadline - time.monotonic()
                if error.code != "unreadable_json" or attempt == 5 or remaining <= 0:
                    raise NavigationError(error.code) from error
                # Windows may briefly deny reads during the producer's atomic replacement.
                time.sleep(min(.04, remaining))
                continue
            _checked_snapshot(snapshot)
            _require(snapshot["me"]["uid"] == uid, "uid_mismatch")
            return snapshot
        raise NavigationError("unreadable_json")

    def read_result(path):
        read_deadline = time.monotonic() + .25
        for attempt in range(6):
            identity()
            try:
                return _read_json(path)
            except NavigationError as error:
                remaining = read_deadline - time.monotonic()
                if error.code != "unreadable_response" or attempt == 5 or remaining <= 0:
                    raise
                time.sleep(min(.04, remaining))
        raise NavigationError("unreadable_response")

    with _controller_lock(directory / f"dfdebug-navigation-{pid}.lock"):
        first = capture()
        bound = _context(first)
        highest = _existing_request_id(command, result, pid, read_result)
        if request_id is None:
            request_id = max(time.time_ns(), highest + 1)
        _require(state._integer(request_id, 1) and request_id > highest, "request_id_not_increasing")
        current = capture()
        _require(_context(current) == bound, "context_changed")
        raw = make_request(current, x=x, y=y, request_id=request_id, issued_tick_ms=state.now_tick_ms())
        request = parse_request(raw)
        identity()
        _atomic_write(command, raw)
        observer = state._Observer(uid)
        observer.observe(current)
        deadline = time.monotonic() + timeout_seconds
        accepted = False
        accepted_tick = None

        def destination_changed(snapshot):
            destination = snapshot["town"]["destination"]
            return (accepted_tick is not None and snapshot["captured_tick_ms"] > accepted_tick
                    and destination is not None and destination != dict(x=request["x"], y=request["y"]))

        def interrupted():
            return dict(ok=False, status="interrupted", reason="destination_changed", request_id=request_id,
                        accepted=True, arrived=False)

        while True:
            current = capture()
            _require(_context(current) == bound, "context_changed")
            try:
                observer.observe(current)
            except state.SnapshotError as error:
                raise NavigationError(error.code) from error
            if destination_changed(current):
                return interrupted()
            try:
                with command.open("rb") as stream:
                    _require(stream.read(WIRE.size + 1) == raw, "superseded")
            except OSError as error:
                raise NavigationError("request_disappeared") from error
            if result.exists():
                value = read_result(result)
                result_id = value.get("request_id")
                _require(state._integer(result_id), "bad_response")
                if value.get("source") == "debug" and result_id > request_id:
                    raise NavigationError("superseded")
                if result_id == request_id and value.get("source") == "debug":
                    try:
                        now = state.now_tick_ms()
                        value = validate_response(value, request, now)
                        _require(current["captured_tick_ms"] <= now
                                 and now - current["captured_tick_ms"] <= 750, "stale")
                    except NavigationError as error:
                        if error.code != "stale_response":
                            raise
                    else:
                        status = value["status"]
                        if status in ("rejected", "interrupted"):
                            return dict(ok=False, status=status, reason=value["reason"], request_id=request_id,
                                        accepted=accepted, arrived=False)
                        accepted = True
                        if accepted_tick is None:
                            accepted_tick = value["tick_ms"]
                        if destination_changed(current):
                            return interrupted()
                        arrived = status == "arrived" and _arrived(current, request)
                        if wait == "accepted" or arrived:
                            return dict(ok=True, status="arrived" if arrived else "requested",
                                        request_id=request_id, accepted=True, arrived=arrived,
                                        target=value["target"], position=current["town"]["position"])
            if time.monotonic() >= deadline:
                return dict(ok=False, status="timeout", request_id=request_id, accepted=accepted, arrived=False)
            time.sleep(min(.04, max(0, deadline - time.monotonic())))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--uid", type=int, required=True)
    parser.add_argument("--process-started-filetime", type=int)
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
    parser.add_argument("--request-id", type=int)
    parser.add_argument("--wait", choices=("accepted", "arrived"), default="arrived")
    parser.add_argument("--timeout-seconds", type=float, default=35)
    args = parser.parse_args(argv)
    try:
        started = args.process_started_filetime
        if started is None:
            started = state.get_process_started_filetime(args.pid)
        result = execute(args.directory, pid=args.pid, uid=args.uid, started=started,
                         x=args.x, y=args.y, wait=args.wait, timeout_seconds=args.timeout_seconds,
                         request_id=args.request_id)
    except (NavigationError, state.SnapshotError) as error:
        result = dict(ok=False, status="invalid", error=error.code, arrived=False)
    print(json.dumps(result, separators=(",", ":")))
    return 0 if result["ok"] else 1 if result["status"] == "timeout" else 2


if __name__ == "__main__":
    raise SystemExit(main())
