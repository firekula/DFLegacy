"""One final Town destination, executed through original-client navigation."""
from __future__ import annotations

from copy import deepcopy
import argparse
import ctypes
import hashlib
import json
import math
from pathlib import Path
import time

try:
    from . import client_navigation as nav
    from . import client_state as state
    from .town_navigation import PORTAL_BLOCK_MARGIN
except ImportError:
    import client_navigation as nav
    import client_state as state
    from town_navigation import PORTAL_BLOCK_MARGIN


def _require(condition, code):
    if not condition:
        raise nav.NavigationError(code)


class _ExpiredBeforeDispatch(nav.NavigationError):
    def __init__(self, request, source):
        super().__init__("expired_request")
        self.request, self.source = request, source


def _area(snapshot):
    town = snapshot["town"]
    return town["town_id"], town["area_id"]


def _binding(snapshot):
    me = snapshot["me"]
    return (snapshot["pid"], snapshot["process_started_filetime"], me["uid"],
            me["user_token"], me["actor_token"], snapshot["town"]["context_token"])


def _near_portal(portal, point):
    rect = getattr(portal, "rect", None)
    margin = PORTAL_BLOCK_MARGIN
    return (rect is not None and rect.x-margin <= point[0] < rect.right+margin
            and rect.y-margin <= point[1] < rect.bottom+margin)


def _monotonic_exit(portal, start, target):
    rect = portal.rect
    rounded = tuple(math.floor(v+.5) if v >= 0 else math.ceil(v-.5) for v in start)
    for origin in (start, rounded):
        if rect.contains(*origin):
            continue  # axis-monotone motion leaves this rectangle at most once
        left, right = sorted((origin[0], target[0]))
        top, bottom = sorted((origin[1], target[1]))
        if left < rect.right and right >= rect.x and top < rect.bottom and bottom >= rect.y:
            return False  # already outside: cannot pass through it to exit padding
    return True


def _portal_rearm_points(catalog, key, start, portal):
    """Leave an entry portal through walkable floor before approaching it again.

    Native area entry places the character inside the reverse portal. Moving
    within that rectangle does not rearm it. Targets retain a further pixel
    beyond the blocked margin, covering the native half-pixel idle rounding.
    """
    area = catalog.area(key)
    gap = PORTAL_BLOCK_MARGIN + 1
    candidates = set()
    for floor in area.walkable:
        xs = {max(floor.x, min(floor.right-1, round(value)))
              for value in (start[0], portal.point[0], floor.x+floor.width/2)}
        ys = {max(floor.y, min(floor.bottom-1, round(value)))
              for value in (start[1], portal.point[1], floor.y+floor.height/2)}
        candidates.update((x, y) for x in xs for y in (portal.rect.y-gap, portal.rect.bottom+gap))
        candidates.update((x, y) for y in ys for x in (portal.rect.x-gap, portal.rect.right+gap))
    def safe(point):
        x, y = point
        return (all(state._integer(v, -1_000_000, 1_000_000) for v in point)
                and any(floor.contains(x, y) for floor in area.walkable)
                and all(not _near_portal(p, point) for p in area.portals))
    ordered = sorted(filter(safe, candidates),
                     key=lambda p: ((p[0]-start[0])**2+(p[1]-start[1])**2, p))
    for point in ordered:
        try:
            points = catalog.waypoints(key, start, point,
                                       blocked_portals=tuple(p for p in area.portals if not _near_portal(p, start)))
            # One axis-monotone native leg can leave a rectangle only once.
            # Multiple turns while the birth gate is exempt could leave and
            # re-enter it before reaching an otherwise safe escape endpoint.
            if len(points) == 1 and _monotonic_exit(portal, start, point):
                return points
        except ValueError as error:
            if getattr(error, "code", None) not in ("no_path", "outside_walkable"): raise
    raise nav.NavigationError("portal_rearm_unavailable")


class _FileSession:
    def __init__(self, directory, *, pid, uid, started):
        self.directory = Path(directory)
        self.pid, self.uid, self.started = pid, uid, started
        _require(self.directory.is_dir(), "directory_missing")
        _require(state._integer(pid, 1, 0xFFFFFFFF) and state._integer(uid, 1, 65535)
                 and state._integer(started, 1), "bad_identity")
        self.command = self.directory / f"dfdebug-navigation-{pid}.bin"
        self.result = self.directory / f"dfdebug-navigation-{pid}.json"
        self.snapshot_path = self.directory / f"dfdebug-client-{pid}.json"
        self._raw, self._accepted = {}, {}

    def _identity(self):
        _require(state.get_process_started_filetime(self.pid) == self.started, "process_start_mismatch")

    def __enter__(self):
        self._identity()
        self._lock = nav._controller_lock(self.directory / f"dfdebug-navigation-{self.pid}.lock")
        self._lock.__enter__()
        return self

    def __exit__(self, *args): return self._lock.__exit__(*args)
    def now_tick_ms(self): return state.now_tick_ms()

    def capture(self):
        for attempt in range(6):
            self._identity()
            try:
                value = state.load_snapshot(self.snapshot_path, pid=self.pid,
                                            process_started_filetime=self.started,
                                            now_tick_ms=self.now_tick_ms())
            except state.SnapshotError as error:
                if error.code != "unreadable_json" or attempt == 5: raise
                time.sleep(.025)
                continue
            self._identity()
            nav._checked_snapshot(value)
            _require(value["me"]["uid"] == self.uid, "uid_mismatch")
            return value

    def _read_result(self, path):
        for attempt in range(6):
            self._identity()
            try: return nav._read_json(path)
            except nav.NavigationError as error:
                if error.code != "unreadable_response" or attempt == 5: raise
                time.sleep(.025)

    def send(self, current, x, y):
        self._identity()
        _require(current["pid"] == self.pid and current["process_started_filetime"] == self.started
                 and current["me"]["uid"] == self.uid, "identity_changed")
        highest = nav._existing_request_id(self.command, self.result, self.pid, self._read_result)
        raw = nav.make_request(current, x=x, y=y, request_id=max(time.time_ns(), highest+1),
                               issued_tick_ms=self.now_tick_ms())
        request = nav.parse_request(raw)
        self._identity()
        nav._atomic_write(self.command, raw)
        self._raw[request["request_id"]] = raw
        return request

    def check(self, request):
        self._identity()
        try:
            with self.command.open("rb") as stream: raw = stream.read(nav.WIRE.size+1)
        except OSError as error: raise nav.NavigationError("request_disappeared") from error
        _require(raw == self._raw.get(request["request_id"]), "superseded")

    def receipt(self, request):
        if not self.result.exists(): return None
        value = self._read_result(self.result)
        result_id = value.get("request_id")
        _require(state._integer(result_id), "bad_response")
        if value.get("source") != "debug":
            _require(not state._integer(value.get("tick_ms"))
                     or value["tick_ms"] < request["issued_tick_ms"], "superseded")
            return None
        _require(result_id <= request["request_id"], "superseded")
        if result_id != request["request_id"]: return None
        try: checked = nav.validate_response(value, request, self.now_tick_ms())
        except nav.NavigationError as error:
            if error.code != "stale_response": raise
            return self._accepted.get(request["request_id"])
        _require(checked["tick_ms"] >= request["issued_tick_ms"], "stale_response")
        self._accepted[request["request_id"]] = checked
        return checked


def _sha(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(65536), b""): digest.update(block)
    return digest.hexdigest().upper()


def _client_catalog_path(image_path, requested_pvf):
    image = Path(image_path)
    actual = image.parent / "Script.pvf"
    _require(image.name.lower() == "dnf.exe" and image.is_file() and actual.is_file(), "client_pvf_missing")
    if requested_pvf is not None:
        _require(Path(requested_pvf).is_file() and _sha(actual) == _sha(requested_pvf), "pvf_mismatch")
    return actual


def _process_image(pid):
    if not hasattr(ctypes, "WinDLL"): raise nav.NavigationError("process_metadata_unavailable")
    api = ctypes.WinDLL("kernel32", use_last_error=True)
    api.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
    api.OpenProcess.restype = ctypes.c_void_p
    api.QueryFullProcessImageNameW.argtypes = [ctypes.c_void_p, ctypes.c_ulong,
                                             ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_ulong)]
    api.CloseHandle.argtypes = [ctypes.c_void_p]
    handle = api.OpenProcess(0x1000, False, pid)
    _require(handle, "process_metadata_unavailable")
    try:
        text = ctypes.create_unicode_buffer(32768); size = ctypes.c_ulong(len(text))
        _require(api.QueryFullProcessImageNameW(handle, 0, text, ctypes.byref(size)), "process_metadata_unavailable")
        return Path(text.value)
    finally: api.CloseHandle(handle)


def run_route(catalog, transport, *, town_id, area_id, x, y, timeout_seconds=120,
              clock=time.monotonic, sleep=time.sleep):
    """Pure lifecycle; transport owns IO, exact receipt validation and PID lock.

    Each internal leg uses the native destination setter. A portal's expected
    context change ends that leg, while only the final physical goal ends this
    operation. No direction-key or packet fallback exists here.
    """
    goal = dict(town_id=town_id, area_id=area_id, x=x, y=y)
    current, history, requests, renewals = None, [], [], []
    deadline = clock() + timeout_seconds if state._finite(timeout_seconds) else clock()
    latest_sequence, latest_generation = 0, 0
    bound = None
    previous_request, previous_snapshot = None, None

    def capture():
        nonlocal latest_sequence, latest_generation
        value = transport.capture()
        nav._checked_snapshot(value)
        now = transport.now_tick_ms()
        _require(value["captured_tick_ms"] <= now and now-value["captured_tick_ms"] <= 750, "stale")
        _require(value["pid"] == transport.pid
                 and value["process_started_filetime"] == transport.started
                 and value["me"]["uid"] == transport.uid, "identity_changed")
        _require(value["sequence"] >= latest_sequence and value["generation"] >= latest_generation,
                 "snapshot_regressed")
        latest_sequence, latest_generation = value["sequence"], value["generation"]
        if bound is not None:
            _require(_binding(value) == bound, "identity_changed")
        _require(value["ui"]["selection_mode_raw"] == 0, "ui_busy")
        return value

    def leg(source, target, next_area=None, require_transition=False, renewing=None):
        nonlocal current, previous_request, previous_snapshot
        _require(clock() < deadline, "route_timeout")
        current = source
        _require(len(requests) < 1024, "route_step_limit")
        request = transport.send(current, *target)
        requests.append(request["request_id"])
        if renewing is not None:
            renewals.append(renewing)
        source = current
        accepted, accepted_tick = False, None
        native_status, context_interruption = None, False
        loading_began = None
        portal_wait_began = None
        while clock() < deadline:
            transport.check(request)
            receipt = transport.receipt(request)
            if receipt is not None:
                native_status = receipt["status"]
                if native_status == "rejected":
                    if receipt["reason"] == "expired_request" and not accepted:
                        raise _ExpiredBeforeDispatch(request, source)
                    raise nav.NavigationError(receipt["reason"] or "native_rejected")
                if native_status == "interrupted":
                    _require(next_area is not None and receipt["reason"] == "context_changed",
                             receipt["reason"] or "native_interrupted")
                    context_interruption = True
                    if portal_wait_began is None: portal_wait_began = clock()
                _require(native_status in ("requested", "arrived", "interrupted"), "bad_response")
                accepted = True
                if accepted_tick is None: accepted_tick = receipt["tick_ms"]
            try:
                current = capture()
                loading_began = None
            except (nav.NavigationError, state.SnapshotError) as error:
                if error.code not in ("invalid_capture", "stale", "unreadable_json"):
                    raise
                if loading_began is None: loading_began = clock()
                _require(clock()-loading_began <= 5, error.code)
                sleep(.04)
                continue
            changed_area = _area(current) != _area(source)
            if changed_area:
                _require(next_area is not None and _area(current) == next_area, "unexpected_area")
                _require(current["generation"] > source["generation"], "unexpected_context")
                _require(current["town"]["map_token"] != source["town"]["map_token"],
                         "map_transition_unproven")
                reached = accepted
            else:
                _require(current["generation"] == source["generation"]
                         and current["town"]["map_token"] == source["town"]["map_token"], "unexpected_context")
                reached = False
                if accepted and not context_interruption and current["captured_tick_ms"] > accepted_tick:
                    town = current["town"]
                    _require(town["destination"] == dict(zip(("x", "y"), target)), "destination_changed")
                    _require(town.get("auto_move_active") is not False, "native_auto_move_stopped")
                    _require(town.get("auto_move_active") is True, "native_auto_move_unknown")
                    at_point = (all(abs(town["position"][axis]-point) <= 3
                                    for axis, point in zip(("x", "y"), target))
                                and town["direction_raw"] == dict(x=4, y=4))
                    if not require_transition and at_point: reached = True
                    if require_transition and at_point:
                        if portal_wait_began is None: portal_wait_began = clock()
                        _require(clock()-portal_wait_began < 5, "portal_not_crossed")
            if reached:
                history.append(dict(request_id=request["request_id"], target=dict(zip(("x", "y"), target)),
                                    source_area=_area(source), observed_area=_area(current),
                                    source_generation=source["generation"], observed_generation=current["generation"],
                                    sequence=current["sequence"], native_status=native_status,
                                    portal=next_area is not None))
                previous_request, previous_snapshot = request, current
                return current
            if portal_wait_began is not None:
                _require(clock()-portal_wait_began < 5, "portal_not_crossed")
            sleep(.04)
        raise nav.NavigationError("route_timeout")

    def reach(source, target, next_area=None, selected_portal=None, leaving=()):
        """Replan the next native leg from its fresh actual start, never a prior waypoint."""
        nonlocal current, previous_request, previous_snapshot
        repeats, last_target, last_position = 0, None, None
        renewable_id, expiry_count = None, 0
        fresh_wait_ends = min(deadline, clock()+5)

        def validate_continuation(value):
            _require(_area(value) == _area(source) and value["generation"] == source["generation"]
                     and value["town"]["map_token"] == source["town"]["map_token"], "unexpected_context")
            if previous_snapshot is not None:
                prior, town = previous_snapshot["town"], value["town"]
                _require(town["destination"] == prior["destination"], "destination_changed")
                _require(town["direction_raw"] == dict(x=4, y=4), "manual_movement")
                _require(not (prior.get("auto_move_active") is True
                              and town.get("auto_move_active") is False), "native_auto_move_stopped")
                if renewable_id is not None:
                    _require(town.get("auto_move_active") == prior.get("auto_move_active"), "manual_movement")
                    _require(all(abs(town["position"][axis]-prior["position"][axis]) <= .5
                                 for axis in ("x", "y")), "manual_movement")

        while clock() < deadline:
            while True:
                if previous_request is not None:
                    transport.check(previous_request)
                    last_receipt = transport.receipt(previous_request)
                    if renewable_id is not None and last_receipt is not None:
                        _require(last_receipt["status"] == "rejected"
                                 and last_receipt["reason"] == "expired_request", "receipt_changed")
                    if last_receipt is not None and last_receipt["status"] == "rejected":
                        _require(previous_request["request_id"] == renewable_id
                                 and last_receipt["reason"] == "expired_request",
                                 last_receipt["reason"] or "native_rejected")
                    if last_receipt is not None and last_receipt["status"] == "interrupted":
                        _require(last_receipt["reason"] == "context_changed", last_receipt["reason"])
                try:
                    current = capture()
                    # All valid <=750ms observations can cancel a route. The
                    # stricter200ms budget only grants permission to send.
                    validate_continuation(current)
                    if transport.now_tick_ms()-current["captured_tick_ms"] > 200:
                        _require(clock() < fresh_wait_ends, "fresh_sample_unavailable")
                        sleep(.04)
                        continue
                    break
                except (nav.NavigationError, state.SnapshotError) as error:
                    # Wait only for a fresh sample; no missing/stale sample
                    # authorizes a send. Ownership and replies are rechecked
                    # every iteration, and fresh identity/context must match.
                    if (error.code not in ("invalid_capture", "stale", "unreadable_json")
                            or clock() >= fresh_wait_ends):
                        raise
                    sleep(.04)
            actual = tuple(current["town"]["position"][axis] for axis in ("x", "y"))
            # A gate is permitted only while approaching the selected exit,
            # or while monotonically leaving the birth gate that contains us.
            # Every fresh plan retains the same constraints; endpoint-only
            # filtering would still allow a native AABB to cross another gate.
            blocked = tuple(p for p in catalog.area(_area(current)).portals
                            if p != selected_portal and not (p in leaving and _near_portal(p, actual)))
            points = catalog.waypoints(_area(current), actual, target, blocked_portals=blocked)
            _require(points and points[-1] == target, "invalid_route")
            _require(not leaving or len(points) == 1, "portal_rearm_unavailable")
            _require(all(_monotonic_exit(p, actual, target) for p in leaving), "portal_rearm_unavailable")
            point = points[0]
            if point == last_target and all(abs(a-b) < .5 for a, b in zip(actual, last_position)):
                repeats += 1
                _require(repeats < 3, "native_no_progress")
            else: repeats = 0
            last_target, last_position = point, actual
            try:
                current = leg(current, point, next_area, require_transition=next_area is not None and point == target,
                              renewing=renewable_id)
            except _ExpiredBeforeDispatch as error:
                # Native ValidateRequest rejects this before SetAndActivate.
                # Never renew a request that was accepted or merely timed out.
                _require(expiry_count < 3 and error.source["town"]["direction_raw"] == dict(x=4,y=4),
                         "expired_request")
                expiry_count += 1
                previous_request, previous_snapshot = error.request, error.source
                renewable_id = error.request["request_id"]
                fresh_wait_ends = min(deadline, clock()+5)
                repeats, last_target, last_position = 0, None, None
                continue
            renewable_id = None
            fresh_wait_ends = min(deadline, clock()+5)
            if _area(current) != _area(source) or point == target: return current
            source = current
        raise nav.NavigationError("route_timeout")

    def leave_birth_portal(source):
        start = tuple(source["town"]["position"][axis] for axis in ("x", "y"))
        origins = tuple(p for p in catalog.area(_area(source)).portals if _near_portal(p, start))
        if not origins: return source
        # Overlapping birth gates cannot be distinguished safely from current
        # native state. Stop rather than accidentally re-enter either gate.
        _require(len(origins) == 1, "ambiguous_birth_portal")
        escape = _portal_rearm_points(catalog, _area(source), start, origins[0])
        escaped = reach(source, escape[-1], leaving=origins)
        observed = tuple(escaped["town"]["position"][axis] for axis in ("x", "y"))
        _require(not any(_near_portal(p, observed) for p in catalog.area(_area(escaped)).portals),
                 "portal_rearm_incomplete")
        return escaped

    try:
        _require(all(state._integer(v, 0, 255) for v in (town_id, area_id))
                 and all(state._integer(v, -1_000_000, 1_000_000) for v in (x, y)), "bad_target")
        _require(state._finite(timeout_seconds) and 0 < timeout_seconds <= 300, "bad_timeout")
        goal_area = catalog.area((town_id, area_id))
        _require(not any(_near_portal(p, (x, y)) for p in goal_area.portals), "goal_inside_portal")
        # Validate the final point before sending even the first portal leg.
        catalog.waypoints((town_id, area_id), (x, y), (x, y))
        current = capture()
        bound = _binding(current)
        portals = catalog.route(_area(current), (town_id, area_id))
        _require(len(portals) <= 256, "route_step_limit")
        for portal in portals:
            _require(portal.source == _area(current), "invalid_route")
            current = leave_birth_portal(current)
            current = reach(current, portal.point, portal.target, selected_portal=portal)
        _require(_area(current) == (town_id, area_id), "invalid_route")
        current = leave_birth_portal(current)
        current = reach(current, (x, y))
        return dict(ok=True, status="arrived", reason="final_position_observed", goal=goal,
                    observed=deepcopy(current["town"]), request_ids=requests, history=history,
                    expired_request_renewals=renewals)
    except (nav.NavigationError, state.SnapshotError, ValueError) as error:
        return dict(ok=False, status="interrupted" if requests else "rejected",
                    reason=getattr(error, "code", str(error)), goal=goal,
                    observed=deepcopy(current["town"]) if current is not None else None,
                    request_ids=requests, history=history, expired_request_renewals=renewals)


def execute_route(directory, *, pid, uid, started, town_id, area_id, x, y,
                  pvf_path=None, timeout_seconds=120):
    """One public operation; all route legs share the existing native PID lock."""
    goal = dict(town_id=town_id, area_id=area_id, x=x, y=y)
    try:
        _require(state._integer(pid, 1, 0xFFFFFFFF) and state._integer(uid, 1, 65535)
                 and state._integer(started, 1), "bad_identity")
        _require(all(state._integer(v, 0, 255) for v in (town_id, area_id))
                 and all(state._integer(v, -1_000_000, 1_000_000) for v in (x, y)), "bad_target")
        _require(state._finite(timeout_seconds) and 0 < timeout_seconds <= 300, "bad_timeout")
        _require(state.get_process_started_filetime(pid) == started, "process_start_mismatch")
        actual = _client_catalog_path(_process_image(pid), pvf_path)
        try:
            from .town_navigation import load_catalog
        except ImportError:
            from town_navigation import load_catalog
        catalog = load_catalog(actual)
        with _FileSession(directory, pid=pid, uid=uid, started=started) as transport:
            result = run_route(catalog, transport, town_id=town_id, area_id=area_id,
                               x=x, y=y, timeout_seconds=timeout_seconds)
        result.update(pid=pid, uid=uid, process_started_filetime=started,
                      pvf_sha256=catalog.pvf_sha256)
        return result
    except (nav.NavigationError, state.SnapshotError, ValueError, OSError) as error:
        return dict(ok=False, status="rejected", reason=getattr(error, "code", str(error)),
                    goal=goal, request_ids=[], history=[])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--uid", type=int, required=True)
    parser.add_argument("--process-started-filetime", type=int)
    parser.add_argument("--town", type=int, required=True)
    parser.add_argument("--area", type=int, required=True)
    parser.add_argument("--x", type=int, required=True)
    parser.add_argument("--y", type=int, required=True)
    parser.add_argument("--pvf", type=Path)
    parser.add_argument("--timeout-seconds", type=float, default=120)
    args = parser.parse_args(argv)
    try:
        started = args.process_started_filetime
        if started is None: started = state.get_process_started_filetime(args.pid)
        result = execute_route(args.directory, pid=args.pid, uid=args.uid, started=started,
                               town_id=args.town, area_id=args.area, x=args.x, y=args.y,
                               pvf_path=args.pvf, timeout_seconds=args.timeout_seconds)
    except (nav.NavigationError, state.SnapshotError) as error:
        result = dict(ok=False, status="rejected", reason=error.code)
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result["ok"] else 2


if __name__ == "__main__": raise SystemExit(main())
