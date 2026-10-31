"""Data-driven ACT1 party actions through debug.dll; no external socket or UI input."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import struct
import time

try:
    from . import client_state as state, client_navigation as nav
except ImportError:
    import client_state as state
    import client_navigation as nav

STATE_SCHEMA = "dfl-client-party/v1"
RESULT_SCHEMA = "dfl-client-party-result/v1"
OPERATIONS = dict(request=1, accept=2, reject=3)
WIRE = struct.Struct("<4sHHIIQQQQQQIIIIiiIIIQ")
FIELDS = ("pid", "uid", "process_started_filetime", "request_id", "issued_tick_ms",
          "snapshot_tick_ms", "generation", "snapshot_sequence", "actor_token", "map_token",
          "user_token", "context_token", "town_id", "area_id", "operation", "target_uid",
          "window_token", "invitation_serial")
TOKENS = ("actor_token", "map_token", "user_token", "context_token", "window_token")
MATCH = tuple(key for key in FIELDS if key not in ("issued_tick_ms", "snapshot_tick_ms"))


class PartyError(ValueError):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def _require(condition, code):
    if not condition: raise PartyError(code)


def _token(value, zero=False):
    return (isinstance(value, str) and state._TOKEN.fullmatch(value) is not None
            and (zero or int(value, 16) != 0))


def validate_state(value, *, now_tick_ms):
    _require(isinstance(value, dict) and value.get("schema") == STATE_SCHEMA, "bad_state")
    for key in ("pid", "uid", "process_started_filetime", "generation", "snapshot_sequence"):
        _require(state._integer(value.get(key), 1), "bad_state_identity")
    _require(value["pid"] <= 0xFFFFFFFF and value["uid"] <= 65535, "bad_state_identity")
    _require(all(_token(value.get(key)) for key in TOKENS[:-1]), "bad_state_identity")
    _require(all(state._integer(value.get(key), 0, 255) for key in ("town_id", "area_id")), "bad_state_identity")
    _require(type(value.get("available")) is bool and isinstance(value.get("reason"), str)
             and len(value["reason"]) <= 128, "bad_state")
    _require(state._integer(value.get("consumed_request_id", 0)), "bad_state")
    _require(state._integer(now_tick_ms) and state._integer(value.get("tick_ms"))
             and state._integer(value.get("snapshot_tick_ms"))
             and value["snapshot_tick_ms"] <= value["tick_ms"] <= now_tick_ms, "future_state")
    _require(now_tick_ms - value["snapshot_tick_ms"] <= 750, "stale_state")
    pending = value.get("pending")
    _require("pending" in value and (pending is None or isinstance(pending, dict)), "bad_pending")
    if pending is not None:
        _require(_token(pending.get("window_token")) and state._integer(pending.get("serial"), 1)
                 and state._integer(pending.get("requester_uid"), 1, 65535)
                 and state._integer(pending.get("peer_type"), 0, 3)
                 and state._integer(pending.get("remaining_ms"), 0, 10000)
                 and type(pending.get("can_reply")) is bool and isinstance(pending.get("reason"), str)
                 and len(pending["reason"]) <= 128, "bad_pending")
    return value


def make_request(value, *, operation, target_uid, request_id, issued_tick_ms):
    validate_state(value, now_tick_ms=issued_tick_ms)
    _require(value["available"], "party_unavailable")
    _require(operation in OPERATIONS, "invalid_operation")
    _require(state._integer(target_uid, 1, 65535) and target_uid != value["uid"], "invalid_target")
    _require(state._integer(request_id, 1), "bad_request_identity")
    pending = value["pending"]
    window, serial = 0, 0
    if operation == "request":
        _require(pending is None, "pending_invitation")
    else:
        _require(pending is not None, "no_pending_invitation")
        _require(pending["requester_uid"] == target_uid, "invitation_mismatch")
        age = issued_tick_ms - value["tick_ms"]
        _require(pending["peer_type"] == 0 and pending["can_reply"]
                 and pending["remaining_ms"] > age, "invitation_unavailable")
        window, serial = int(pending["window_token"], 16), pending["serial"]
    values = (value["pid"], value["uid"], value["process_started_filetime"], request_id,
              issued_tick_ms, value["snapshot_tick_ms"], value["generation"], value["snapshot_sequence"],
              *(int(value[key], 16) for key in TOKENS[:-1]), value["town_id"], value["area_id"],
              OPERATIONS[operation], target_uid, window, serial)
    raw = WIRE.pack(b"DLPT", 1, WIRE.size, *values)
    parse_request(raw)
    return raw


def parse_request(raw):
    _require(isinstance(raw, bytes) and len(raw) == WIRE.size, "bad_request_size")
    magic, version, size, *values = WIRE.unpack(raw)
    _require(magic == b"DLPT" and version == 1 and size == 108, "bad_request_header")
    value = dict(zip(FIELDS, values))
    _require(all(value[key] > 0 for key in ("pid", "uid", "process_started_filetime", "request_id",
                                          "generation", "snapshot_sequence", *TOKENS[:-1]))
             and value["uid"] <= 65535 and 0 < value["target_uid"] <= 65535
             and value["target_uid"] != value["uid"] and 0 <= value["town_id"] <= 255
             and 0 <= value["area_id"] <= 255, "bad_request_identity")
    _require(value["operation"] in OPERATIONS.values(), "invalid_operation")
    _require((value["operation"] == 1 and value["window_token"] == value["invitation_serial"] == 0)
             or (value["operation"] != 1 and value["window_token"] != 0 and value["invitation_serial"] > 0),
             "bad_request_invitation")
    _require(value["snapshot_tick_ms"] <= value["issued_tick_ms"]
             and value["issued_tick_ms"] - value["snapshot_tick_ms"] <= 750, "stale_state")
    return value


def _read(path):
    for attempt in range(6):
        try: return nav._read_json(path)
        except nav.NavigationError as error:
            if error.code != "unreadable_response" or attempt == 5:
                raise PartyError(error.code) from error
            # Native replacement can briefly deny a Windows read. Retry observation
            # only: the command may already have been sent and must not be replayed.
            time.sleep(.01)


def read_status(directory, *, pid, uid=None, process_started_filetime=None, now_tick_ms=None):
    value = _read(Path(directory) / f"dfdebug-party-{pid}.json")
    validate_state(value, now_tick_ms=state.now_tick_ms() if now_tick_ms is None else now_tick_ms)
    _require(value["pid"] == pid and (uid is None or value["uid"] == uid)
             and (process_started_filetime is None or value["process_started_filetime"] == process_started_filetime),
             "process_mismatch")
    return value


def validate_result(value, request, *, now_tick_ms):
    _require(isinstance(value, dict) and value.get("schema") == RESULT_SCHEMA, "bad_result")
    _require(value.get("status") in ("dispatched", "rejected") and isinstance(value.get("reason"), str)
             and len(value["reason"]) <= 128, "bad_result")
    for key in MATCH:
        actual = value.get(key)
        if key in TOKENS:
            _require(_token(actual, zero=True), "bad_result")
            actual = int(actual, 16)
        else:
            _require(type(actual) is int, "bad_result")
        _require(actual == request[key], "result_binding_mismatch")
    _require(state._integer(value.get("tick_ms")) and value["tick_ms"] <= now_tick_ms
             and now_tick_ms - value["tick_ms"] <= 1000, "stale_result")
    return value


def execute(directory, *, pid, operation, target_uid=None, uid=None, process_started_filetime=None, timeout_seconds=3):
    directory = Path(directory)
    _require(operation in OPERATIONS and 0 < timeout_seconds <= 10, "invalid_operation")
    command = directory / f"dfdebug-party-{pid}.bin"
    response = directory / f"dfdebug-party-{pid}.result.json"
    try:
        with nav._controller_lock(directory / f"dfdebug-party-{pid}.control.lock"):
            value = read_status(directory, pid=pid, uid=uid, process_started_filetime=process_started_filetime)
            if target_uid is None and value["pending"] is not None: target_uid = value["pending"]["requester_uid"]
            highest = value.get("consumed_request_id", 0)
            if command.exists():
                with command.open("rb") as stream: highest = max(highest, parse_request(stream.read(WIRE.size + 1))["request_id"])
            if response.exists():
                previous = _read(response)
                _require(previous.get("schema") == RESULT_SCHEMA and state._integer(previous.get("request_id")),
                         "invalid_previous_result")
                highest = max(highest, previous["request_id"])
            raw = make_request(value, operation=operation, target_uid=target_uid, request_id=highest + 1,
                               issued_tick_ms=state.now_tick_ms())
            request = parse_request(raw)
            nav._atomic_write(command, raw)
            deadline = time.monotonic() + timeout_seconds
            while time.monotonic() < deadline:
                if response.exists():
                    result = _read(response)
                    if result.get("request_id") == request["request_id"]:
                        return validate_result(result, request, now_tick_ms=state.now_tick_ms())
                time.sleep(0.025)
            raise PartyError("response_timeout")
    except nav.NavigationError as error: raise PartyError(error.code) from error


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("state", *OPERATIONS))
    parser.add_argument("--directory", required=True)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--uid", type=int)
    parser.add_argument("--process-started-filetime", type=int)
    parser.add_argument("--target-uid", type=int)
    args = parser.parse_args()
    try:
        identity = dict(pid=args.pid, uid=args.uid, process_started_filetime=args.process_started_filetime)
        value = (read_status(args.directory, **identity) if args.operation == "state" else
                 execute(args.directory, operation=args.operation, target_uid=args.target_uid, **identity))
        print(json.dumps(value, ensure_ascii=False))
        return 0 if args.operation == "state" or value["status"] == "dispatched" else 1
    except PartyError as error:
        print(json.dumps(dict(error=error.code)))
        return 2


if __name__ == "__main__": raise SystemExit(main())
