"""Route lifecycle proofs use native receipts and snapshots, never game input."""
from copy import deepcopy
from types import SimpleNamespace
import importlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tools.debug.test_client_state import PID, START, NOW, town_fixture


def snapshot(area=0, generation=1, x=10, y=20, destination=None, moving=False, auto=True):
    value = town_fixture()
    token = f"0x{0x184AFED8 + area * 0x100:08X}"
    value.update(generation=generation)
    value["me"].update(map_token=token, position=dict(x=x, y=y, z=0))
    value["players"] = [deepcopy(value["me"])]
    value["town"].update(area_id=area, map_token=token,
                         position=dict(x=x, y=y, z=0),
                         destination=dict(zip(("x", "y"), destination or (x, y))),
                         direction_raw=dict(x=1 if moving else 4, y=4),
                         auto_move_active=auto)
    return value


class Catalog:
    pvf_sha256 = "A" * 64

    def area(self, key):
        if key not in ((1, 0), (1, 1)):
            raise ValueError("unknown_area")
        return SimpleNamespace(kind="normal", portals=())

    def route(self, start, goal):
        self.area(start)
        self.area(goal)
        return () if start == goal else (SimpleNamespace(source=start, target=goal, point=(90, 20)),)

    def waypoints(self, key, start, target, **kwargs):
        return (target,)


class Clock:
    def __init__(self): self.value = 0
    def __call__(self): return self.value
    def sleep(self, seconds): self.value += seconds


class Transport:
    pid, uid, started = PID, 1, START

    def __init__(self, first, stages, receipts=None):
        self.current = deepcopy(first)
        self.stages = deepcopy(stages)
        self.receipts = receipts or []
        self.queue, self.sent = [], []
        self.counter, self.polls = 0, 0
        self.overwritten = False

    def now_tick_ms(self): return NOW + self.counter

    def capture(self):
        self.counter += 1
        if self.queue:
            value = self.queue.pop(0)
            if isinstance(value, Exception): raise value
            self.current = value
        value = deepcopy(self.current)
        value.update(sequence=self.counter, captured_tick_ms=NOW + self.counter)
        return value

    def send(self, current, x, y):
        self.sent.append((current["town"]["area_id"], x, y))
        self.queue = list(self.stages[len(self.sent) - 1])
        self.polls = 0
        return dict(request_id=len(self.sent), x=x, y=y, issued_tick_ms=NOW + self.counter)

    def receipt(self, request):
        self.polls += 1
        value = self.receipts[len(self.sent) - 1] if len(self.receipts) >= len(self.sent) else "requested"
        if callable(value): value = value(self.polls)
        if value is None: return None
        if isinstance(value, dict): return value
        return dict(status=value, reason="", tick_ms=NOW)

    def check(self, request):
        if self.overwritten: raise ValueError("superseded")


class ClientRouteTests(unittest.TestCase):
    def setUp(self):
        self.api = importlib.import_module("tools.debug.client_route")
        self.clock = Clock()

    def run_route(self, transport, **kwargs):
        return self.api.run_route(Catalog(), transport, town_id=1, area_id=1,
                                  x=120, y=35, clock=self.clock, sleep=self.clock.sleep,
                                  timeout_seconds=kwargs.pop("timeout_seconds", 2), **kwargs)

    def successful(self, receipts=None):
        return Transport(snapshot(), [[snapshot(1, 2, 15, 20)], [snapshot(1, 2, 120, 35)]], receipts)

    def test_one_goal_crosses_expected_area_and_reaches_final_coordinate(self):
        transport = self.successful()
        result = self.run_route(transport)
        self.assertEqual(result["status"], "arrived")
        self.assertTrue(result["ok"])
        self.assertEqual(transport.sent, [(0, 90, 20), (1, 120, 35)])
        self.assertEqual(result["observed"]["area_id"], 1)
        self.assertEqual(result["observed"]["position"]["x"], 120)

    def test_portal_context_interruption_is_expected_only_with_new_area_and_generation(self):
        transport = self.successful([dict(status="interrupted", reason="context_changed", tick_ms=NOW)])
        self.assertEqual(self.run_route(transport)["status"], "arrived")
        for bad in (snapshot(0, 2, 15, 20), snapshot(1, 1, 15, 20), snapshot(2, 2, 15, 20)):
            transport = Transport(snapshot(), [[bad]])
            result = self.run_route(transport)
            self.assertFalse(result["ok"])
            self.assertEqual(len(transport.sent), 1)

    def test_transition_without_native_acceptance_cannot_advance_or_report_arrived(self):
        transport = self.successful([lambda polls: None])
        result = self.run_route(transport, timeout_seconds=.2)
        self.assertFalse(result["ok"])
        self.assertEqual(len(transport.sent), 1)

    def test_native_rejection_never_becomes_success_from_coincident_new_area(self):
        transport = self.successful([dict(status="rejected", reason="live_context_unavailable", tick_ms=NOW)])
        result = self.run_route(transport)
        self.assertFalse(result["ok"])
        self.assertEqual(result["reason"], "live_context_unavailable")
        self.assertEqual(len(transport.sent), 1)

    def test_explicit_pre_dispatch_expiry_renews_from_unchanged_fresh_state(self):
        expired = dict(status="rejected", reason="expired_request", tick_ms=NOW)
        transport = Transport(snapshot(), [[snapshot()], [snapshot(x=120, y=35)]], [expired, "requested"])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertEqual(transport.sent, [(0, 120, 35), (0, 120, 35)])
        self.assertEqual(result["expired_request_renewals"], [1])

    def test_expiry_cannot_renew_after_native_acceptance(self):
        expired = dict(status="rejected", reason="expired_request", tick_ms=NOW)
        response = lambda polls: "requested" if polls == 1 else expired
        transport = Transport(snapshot(), [[snapshot(x=30, destination=(120,35), moving=True)]], [response])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "expired_request")
        self.assertEqual(len(transport.sent), 1)

    def test_expiry_renewal_stops_if_the_old_receipt_later_reports_acceptance(self):
        expired = dict(status="rejected", reason="expired_request", tick_ms=NOW)
        response = lambda polls: expired if polls == 1 else "requested"
        transport = Transport(snapshot(), [[snapshot()], [snapshot(x=120,y=35)]], [response])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertFalse(result["ok"], result)
        self.assertEqual(result["reason"], "receipt_changed")
        self.assertEqual(len(transport.sent), 1)
        self.assertEqual(result["expired_request_renewals"], [])

    def test_expiry_renewal_cannot_override_a_changed_manual_destination(self):
        expired = dict(status="rejected", reason="expired_request", tick_ms=NOW)
        transport = Transport(snapshot(), [[snapshot(destination=(88,88))]], [expired])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "destination_changed")
        self.assertEqual(len(transport.sent), 1)

    def test_expiry_renewal_budget_is_bounded(self):
        expired = dict(status="rejected", reason="expired_request", tick_ms=NOW)
        transport = Transport(snapshot(), [[snapshot()]] * 4, [expired] * 4)
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "expired_request")
        self.assertEqual(len(transport.sent), 4)

    def test_send_waits_for_a_fresher_sample_to_leave_native_queueing_budget(self):
        class SlowExport(Transport):
            def capture(inner):
                value = super().capture()
                if not inner.sent and inner.counter < 3:
                    value["captured_tick_ms"] -= 300
                return value
            def send(inner, current, x, y):
                self.assertLessEqual(inner.now_tick_ms()-current["captured_tick_ms"], 200)
                return super().send(current, x, y)
        transport = SlowExport(snapshot(), [[snapshot(x=120, y=35)]])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertGreaterEqual(self.clock(), .04)
        self.assertEqual(len(transport.sent), 1)

    def test_valid_sample_above_send_budget_still_cancels_expiry_renewal(self):
        class ManualWhileWaiting(Transport):
            def capture(inner):
                manual = len(inner.sent) == 1 and not getattr(inner, "seen_manual", False)
                if manual: inner.counter += 400
                value = super().capture()
                if manual:
                    value["captured_tick_ms"] -= 300
                    inner.seen_manual = True
                    self.assertGreater(value["captured_tick_ms"], NOW+2)
                return value
        expired = dict(status="rejected", reason="expired_request", tick_ms=NOW)
        manual = snapshot(destination=(88,88), moving=True)
        transport = ManualWhileWaiting(snapshot(), [[manual, snapshot()], [snapshot(x=120,y=35)]],
                                       [expired, "requested"])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "destination_changed")
        self.assertEqual(len(transport.sent), 1)
        self.assertEqual(result["expired_request_renewals"], [])

    def test_loading_snapshot_waits_without_sending_a_second_leg(self):
        from tools.debug.client_navigation import NavigationError
        transport = Transport(snapshot(), [[NavigationError("invalid_capture"), snapshot(1, 2, 15, 20)],
                                           [snapshot(1, 2, 120, 35)]])
        self.assertEqual(self.run_route(transport)["status"], "arrived")
        self.assertEqual(len(transport.sent), 2)

    def test_proven_transition_can_pause_before_the_first_continuation_capture(self):
        from tools.debug.client_navigation import NavigationError
        transport = Transport(snapshot(), [[snapshot(1, 2, 15, 20), NavigationError("stale"),
                                            snapshot(1, 2, 15, 20)], [snapshot(1, 2, 120, 35)]])
        result = self.run_route(transport)
        self.assertTrue(result["ok"], result)
        self.assertEqual(len(transport.sent), 2)
        self.assertGreaterEqual(self.clock(), .04)

    def test_post_transition_capture_wait_is_bounded_without_sending_again(self):
        from tools.debug.client_navigation import NavigationError
        transport = Transport(snapshot(), [[snapshot(1, 2, 15, 20)] + [NavigationError("stale")] * 200])
        result = self.run_route(transport, timeout_seconds=10)
        self.assertEqual(result["reason"], "stale")
        self.assertEqual(len(transport.sent), 1)
        self.assertGreaterEqual(self.clock(), 5)
        self.assertLess(self.clock(), 6)

    def test_owned_same_area_leg_waits_for_transient_stale_without_resending(self):
        from tools.debug.client_navigation import NavigationError
        transport = Transport(snapshot(), [[NavigationError("stale"), snapshot(x=120, y=35)]])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertEqual(transport.sent, [(0, 120, 35)])
        self.assertGreaterEqual(self.clock(), .04)

    def test_owned_same_area_leg_snapshot_gap_still_expires(self):
        from tools.debug.client_navigation import NavigationError
        transport = Transport(snapshot(), [[NavigationError("stale")] * 200])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=10)
        self.assertEqual(result["reason"], "stale")
        self.assertEqual(transport.sent, [(0, 120, 35)])
        self.assertGreaterEqual(self.clock(), 5)
        self.assertLess(self.clock(), 6)

    def test_character_replacement_after_a_short_gap_cannot_continue(self):
        from tools.debug.client_navigation import NavigationError
        changed = snapshot(x=120, y=35)
        changed["me"]["actor_token"] = "0x19999990"
        changed["players"] = [deepcopy(changed["me"])]
        transport = Transport(snapshot(), [[NavigationError("stale"), changed]])
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "identity_changed")
        self.assertEqual(transport.sent, [(0, 120, 35)])

    def test_same_uid_with_replaced_character_binding_cannot_inherit_route(self):
        for field in ("user_token", "actor_token"):
            bad = snapshot(1, 2, 15, 20)
            bad["me"][field] = "0x19999990"
            bad["players"] = [deepcopy(bad["me"])]
            transport = Transport(snapshot(), [[bad]])
            self.assertEqual(self.run_route(transport)["reason"], "identity_changed")
            self.assertEqual(len(transport.sent), 1)

    def test_manual_destination_override_stops_all_later_legs(self):
        changed = snapshot(x=30, destination=(55, 55), moving=True)
        transport = Transport(snapshot(), [[changed]])
        result = self.run_route(transport)
        self.assertEqual(result["reason"], "destination_changed")
        self.assertEqual(len(transport.sent), 1)

    def test_manual_native_stop_does_not_restart_even_with_unchanged_destination(self):
        stopped = snapshot(x=30, destination=(90, 20), auto=False)
        transport = Transport(snapshot(), [[stopped]])
        self.assertEqual(self.run_route(transport)["reason"], "native_auto_move_stopped")
        self.assertEqual(len(transport.sent), 1)

    def test_request_supersession_does_not_get_overwritten_by_next_leg(self):
        transport = self.successful()
        transport.overwritten = True
        self.assertEqual(self.run_route(transport)["reason"], "superseded")
        self.assertLessEqual(len(transport.sent), 1)

    def test_invalid_goal_fails_before_any_native_request(self):
        transport = self.successful()
        result = self.api.run_route(Catalog(), transport, town_id=1, area_id=8, x=0, y=0,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=1)
        self.assertFalse(result["ok"])
        self.assertEqual(transport.sent, [])

    def test_invalid_coordinate_in_a_known_destination_area_does_not_travel_first(self):
        class OutsideGoal(Catalog):
            def waypoints(self, key, start, target, **kwargs):
                if key == (1, 1) and start == target: raise ValueError("outside_walkable")
                return super().waypoints(key, start, target)
        transport = self.successful()
        result = self.api.run_route(OutsideGoal(), transport, town_id=1, area_id=1, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=1)
        self.assertEqual(result["reason"], "outside_walkable")
        self.assertEqual(transport.sent, [])

    def test_blocked_or_expired_route_is_never_arrived(self):
        transport = Transport(snapshot(), [[snapshot(x=30, destination=(90, 20), moving=True)]])
        self.assertFalse(self.run_route(transport, timeout_seconds=.2)["ok"])
        self.assertEqual(len(transport.sent), 1)

    def test_next_leg_does_not_overwrite_a_manual_destination_between_legs(self):
        class Turns(Catalog):
            def waypoints(self, key, start, target, **kwargs):
                return ((50, 20), target) if start[0] < 40 and target == (120, 35) else (target,)
        transport = Transport(snapshot(), [[snapshot(x=50, destination=(50, 20)),
                                             snapshot(x=55, destination=(75, 99), moving=True)],
                                            [snapshot(x=120, y=35)]])
        result = self.api.run_route(Turns(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "destination_changed")
        self.assertEqual(transport.sent, [(0, 50, 20)])

    def test_new_area_without_new_map_is_not_enough_to_continue(self):
        old_map = snapshot()["me"]["map_token"]
        changed = snapshot(1, 2, 15, 20)
        final = snapshot(1, 2, 120, 35)
        for value in (changed, final):
            value["me"]["map_token"] = old_map
            value["players"] = [deepcopy(value["me"])]
            value["town"]["map_token"] = old_map
        transport = Transport(snapshot(), [[changed], [final]])
        result = self.run_route(transport)
        self.assertEqual(result["reason"], "map_transition_unproven")
        self.assertEqual(len(transport.sent), 1)

    def test_reaching_a_portal_coordinate_without_crossing_has_a_bounded_failure(self):
        transport = Transport(snapshot(), [[snapshot(x=90, y=20)]], ["arrived"])
        result = self.run_route(transport, timeout_seconds=10)
        self.assertEqual(result["reason"], "portal_not_crossed")
        self.assertEqual(len(transport.sent), 1)
        self.assertLess(self.clock(), 7)

    def test_manual_stop_inside_arrival_tolerance_never_starts_the_next_leg(self):
        class Turns(Catalog):
            def waypoints(self, key, start, target, **kwargs):
                return ((50, 20), target) if start[0] < 40 and target == (120, 35) else (target,)
        transport = Transport(snapshot(), [[snapshot(x=48, destination=(50, 20), auto=False)],
                                            [snapshot(x=120, y=35)]])
        result = self.api.run_route(Turns(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "native_auto_move_stopped")
        self.assertEqual(len(transport.sent), 1)

    def test_late_rejection_of_the_previous_request_is_not_ignored(self):
        class Turns(Catalog):
            def waypoints(self, key, start, target, **kwargs):
                return ((50, 20), target) if start[0] < 40 and target == (120, 35) else (target,)
        reply = lambda polls: (dict(status="requested", reason="", tick_ms=NOW) if polls == 1
                               else dict(status="rejected", reason="live_context_unavailable", tick_ms=NOW))
        transport = Transport(snapshot(), [[snapshot(x=50, destination=(50, 20))],
                                            [snapshot(x=120, y=35)]], [reply])
        result = self.api.run_route(Turns(), transport, town_id=1, area_id=0, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "live_context_unavailable")
        self.assertEqual(len(transport.sent), 1)

    def test_context_changed_receipt_without_a_physical_transition_is_also_bounded(self):
        transport = Transport(snapshot(), [[snapshot(x=90, y=20)]],
                              [dict(status="interrupted", reason="context_changed", tick_ms=NOW)])
        result = self.run_route(transport, timeout_seconds=10)
        self.assertEqual(result["reason"], "portal_not_crossed")
        self.assertLess(self.clock(), 7)

    def test_starting_inside_a_portal_automatically_leaves_and_reenters_it(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        portal = Portal((1, 0), (1, 1), Rect(30, 80, 40, 20), (50, 89))
        catalog = RealCatalog("a"*64, {
            (1, 0): Area(1, 0, "start", (Rect(0, 0, 100, 100),), (portal,), "normal"),
            (1, 1): Area(1, 1, "end", (Rect(0, 0, 100, 100),), (), "normal")})
        transport = Transport(snapshot(x=50, y=90), [[snapshot(x=50, y=75)],
                              [snapshot(1, 2, 10, 20, auto=False)], [snapshot(1, 2, 40, 40)]])
        result = self.api.run_route(catalog, transport, town_id=1, area_id=1, x=40, y=40,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertEqual(transport.sent, [(0, 50, 75), (0, 50, 89), (1, 40, 40)])

    def test_portal_rearm_does_not_escape_through_another_portal(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        portal = Portal((1, 0), (1, 1), Rect(30, 80, 40, 20), (50, 89))
        other = Portal((1, 0), (1, 8), Rect(0, 70, 100, 10), (50, 75))
        catalog = RealCatalog("a"*64, {(1, 0): Area(1, 0, "start", (Rect(0, 0, 100, 100),),
                                                  (portal, other), "normal")})
        points = self.api._portal_rearm_points(catalog, (1, 0), (50, 90), portal)
        self.assertEqual(points[-1], (25, 90))
        self.assertTrue(all(not other.rect.contains(*p) for p in points))

    def test_rearm_entire_path_avoids_a_foreign_portal(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        portal = Portal((1, 0), (1, 1), Rect(30, 80, 40, 20), (50, 89))
        foreign = Portal((1, 0), (1, 8), Rect(40, 80, 20, 5), (50, 82))
        catalog = RealCatalog("a"*64, {(1, 0): Area(1, 0, "start", (Rect(0, 0, 100, 100),),
                                                  (portal, foreign), "normal")})
        previous = (50, 90)
        for point in self.api._portal_rearm_points(catalog, (1, 0), previous, portal):
            left, right = sorted((previous[0], point[0]))
            top, bottom = sorted((previous[1], point[1]))
            self.assertFalse(left < foreign.rect.right and right >= foreign.rect.x
                             and top < foreign.rect.bottom and bottom >= foreign.rect.y,
                             (previous, point))
            previous = point

    def test_birth_portal_exit_cannot_leave_then_reenter_on_multiple_legs(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        portal = Portal((1, 0), (1, 8), Rect(30, 30, 40, 40), (50, 50))
        floors = tuple(Rect(*r) for r in [(46,46,29,8), (67,46,8,28),
                                         (46,66,29,8), (0,62,54,8)])
        catalog = RealCatalog("a"*64, {(1, 0): Area(1, 0, "winding", floors, (portal,), "normal")})
        with self.assertRaises(self.api.nav.NavigationError) as caught:
            self.api._portal_rearm_points(catalog, (1, 0), (50, 50), portal)
        self.assertEqual(caught.exception.code, "portal_rearm_unavailable")

    def test_start_near_but_outside_a_portal_cannot_escape_through_its_interior(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        portal = Portal((1, 0), (1, 8), Rect(30, 30, 40, 40), (50, 50))
        catalog = RealCatalog("a"*64, {(1, 0): Area(1, 0, "narrow", (Rect(29,40,70,20),),
                                                  (portal,), "normal")})
        with self.assertRaises(self.api.nav.NavigationError) as caught:
            self.api._portal_rearm_points(catalog, (1, 0), (29.2, 50), portal)
        self.assertEqual(caught.exception.code, "portal_rearm_unavailable")

    def test_final_goal_inside_a_portal_is_rejected_before_travel(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        portal = Portal((1, 1), (1, 0), Rect(30, 30, 20, 20), (40, 40))
        catalog = RealCatalog("a"*64, {
            (1, 0): Area(1, 0, "start", (Rect(0, 0, 100, 100),), (), "normal"),
            (1, 1): Area(1, 1, "end", (Rect(0, 0, 100, 100),), (portal,), "normal")})
        transport = Transport(snapshot(), [])
        result = self.api.run_route(catalog, transport, town_id=1, area_id=1, x=40, y=40,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertEqual(result["reason"], "goal_inside_portal")
        self.assertEqual(transport.sent, [])

    def test_final_movement_keeps_avoiding_foreign_portals_after_each_replan(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        foreign = Portal((1, 0), (1, 8), Rect(40, 40, 20, 20), (50, 50))
        catalog = RealCatalog("a"*64, {(1, 0): Area(1, 0, "start", (Rect(0, 0, 100, 100),),
                                                  (foreign,), "normal")})
        class Walking(Transport):
            def send(inner, current, x, y):
                start = current["town"]["position"]
                left, right = sorted((start["x"], x)); top, bottom = sorted((start["y"], y))
                self.assertFalse(left < 60 and right >= 40 and top < 60 and bottom >= 40,
                                 (start, (x, y)))
                inner.stages.append([snapshot(x=x, y=y)])
                return super().send(current, x, y)
        transport = Walking(snapshot(x=10, y=50), [])
        result = self.api.run_route(catalog, transport, town_id=1, area_id=0, x=90, y=50,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertGreater(len(transport.sent), 1)

    def test_birth_reverse_portal_is_left_even_when_next_portal_is_different(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Portal, Rect
        origin = Portal((1, 0), (1, 8), Rect(30, 80, 40, 20), (50, 89))
        selected = Portal((1, 0), (1, 1), Rect(80, 30, 20, 40), (89, 50))
        catalog = RealCatalog("a"*64, {
            (1, 0): Area(1, 0, "start", (Rect(0, 0, 100, 100),), (origin, selected), "normal"),
            (1, 1): Area(1, 1, "end", (Rect(0, 0, 100, 100),), (), "normal")})
        class Walking(Transport):
            def send(inner, current, x, y):
                if len(inner.sent) == 0:
                    self.assertFalse(origin.rect.contains(x, y))
                    self.assertFalse(selected.rect.contains(x, y), "must rearm birth portal first")
                    value = snapshot(x=x, y=y)
                elif selected.rect.contains(x, y): value = snapshot(1, 2, 10, 20, auto=False)
                else: value = snapshot(current["town"]["area_id"], current["generation"], x, y)
                inner.stages.append([value])
                return super().send(current, x, y)
        transport = Walking(snapshot(x=50, y=90), [])
        result = self.api.run_route(catalog, transport, town_id=1, area_id=1, x=40, y=40,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)

    def test_expected_portal_may_cross_on_an_intermediate_native_leg(self):
        class Turns(Catalog):
            def waypoints(self, key, start, target, **kwargs):
                return ((50, 20), target) if key == (1, 0) else (target,)
        transport = self.successful()
        result = self.api.run_route(Turns(), transport, town_id=1, area_id=1, x=120, y=35,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertEqual(transport.sent, [(0, 50, 20), (1, 120, 35)])

    def test_fresh_actual_start_is_replanned_before_sending(self):
        from tools.debug.town_navigation import Catalog as RealCatalog, Area, Rect
        catalog = RealCatalog("a"*64, {(1, 0): Area(1, 0, "fixture", (
            Rect(0, 0, 100, 100), Rect(90, 20, 110, 20), Rect(180, 20, 220, 80)), (), "normal")})
        class Drift(Transport):
            def capture(self):
                if self.counter == 1 and not self.sent: self.current = snapshot(x=99, y=60, moving=True)
                return super().capture()
            def send(self, current, x, y):
                self.stages.append([snapshot(x=x, y=y)])
                return super().send(current, x, y)
        transport = Drift(snapshot(x=190, y=60), [])
        result = self.api.run_route(catalog, transport, town_id=1, area_id=0, x=310, y=60,
                                    clock=self.clock, sleep=self.clock.sleep, timeout_seconds=2)
        self.assertTrue(result["ok"], result)
        self.assertEqual(transport.sent, [(0, 95, 30), (0, 190, 30), (0, 310, 60)])


class FileSessionTests(unittest.TestCase):
    def setUp(self):
        self.api = importlib.import_module("tools.debug.client_route")
        self.nav = importlib.import_module("tools.debug.client_navigation")
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.value = snapshot()
        (self.directory / f"dfdebug-client-{PID}.json").write_text(json.dumps(self.value), encoding="utf-8")
        self.patch_start = patch.object(self.api.state, "get_process_started_filetime", return_value=START)
        self.patch_tick = patch.object(self.api.state, "now_tick_ms", return_value=NOW)
        self.patch_start.start(); self.patch_tick.start()
        self.addCleanup(self.patch_start.stop); self.addCleanup(self.patch_tick.stop)

    def session(self):
        return self.api._FileSession(self.directory, pid=PID, uid=1, started=START)

    def test_route_holds_existing_same_area_lock_until_exit(self):
        lock = self.directory / f"dfdebug-navigation-{PID}.lock"
        with self.session():
            self.assertTrue(lock.exists())
            with self.assertRaises(self.nav.NavigationError) as caught:
                with self.nav._controller_lock(lock): pass
            self.assertEqual(caught.exception.code, "controller_busy")
        self.assertFalse(lock.exists())

    def test_file_session_uses_the_exact_existing_native_wire(self):
        with self.session() as session:
            current = session.capture()
            request = session.send(current, 90, 20)
            raw = (self.directory / f"dfdebug-navigation-{PID}.bin").read_bytes()
            parsed = self.nav.parse_request(raw)
            self.assertEqual(parsed, request)
            self.assertEqual((parsed["pid"], parsed["uid"], parsed["x"], parsed["y"]), (PID, 1, 90, 20))
            session.check(request)

    def test_native_result_is_bound_and_command_supersession_is_detected(self):
        from tools.debug.test_client_navigation import response
        with self.session() as session:
            request = session.send(session.capture(), 90, 20)
            result_path = self.directory / f"dfdebug-navigation-{PID}.json"
            result_path.write_text(json.dumps(response(request)), encoding="utf-8")
            self.assertEqual(session.receipt(request)["status"], "requested")
            bad = response(request); bad["uid"] = 2
            result_path.write_text(json.dumps(bad), encoding="utf-8")
            with self.assertRaises(self.nav.NavigationError): session.receipt(request)
            (self.directory / f"dfdebug-navigation-{PID}.bin").write_bytes(b"foreign")
            with self.assertRaises(self.nav.NavigationError) as caught: session.check(request)
            self.assertEqual(caught.exception.code, "superseded")

    def test_process_reuse_is_rejected_before_writing_a_command(self):
        with self.session() as session:
            current = session.capture()
            with patch.object(self.api.state, "get_process_started_filetime", return_value=START+1):
                with self.assertRaises(self.nav.NavigationError): session.send(current, 90, 20)
            self.assertFalse((self.directory / f"dfdebug-navigation-{PID}.bin").exists())

    def test_catalog_must_match_the_actual_client_pvf(self):
        image = self.directory / "DNF.exe"; image.write_bytes(b"fixture")
        actual = self.directory / "Script.pvf"; actual.write_bytes(b"actual")
        wrong = self.directory / "other.pvf"; wrong.write_bytes(b"different")
        with self.assertRaises(self.nav.NavigationError) as caught:
            self.api._client_catalog_path(image, wrong)
        self.assertEqual(caught.exception.code, "pvf_mismatch")
        same = self.directory / "same.pvf"; same.write_bytes(b"actual")
        self.assertEqual(self.api._client_catalog_path(image, same), actual)


if __name__ == "__main__": unittest.main()
