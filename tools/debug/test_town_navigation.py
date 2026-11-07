"""Offline PVF/geometry tests. No clients, input, network or process control."""
from dataclasses import FrozenInstanceError
import hashlib
import importlib
import inspect
from pathlib import Path
import struct
import tempfile
import unittest
import zlib


MAGIC = b"DNF_SCRIPT_PACK"
ACTUAL_PVF = Path("G:/Game/DNF_ACT1/DF/Script.pvf")
ACTUAL_SHA = "288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f"


def encrypt(raw, key):
    raw += b"\0" * (-len(raw) % 4)
    return b"".join(struct.pack("<I", ((word << 6) | (word >> 26)) & 0xFFFFFFFF ^ key)
                    for (word,) in struct.iter_unpack("<I", raw))


def pvf_bytes(entries, tree_alignment=4, extra_padding=0, padding_byte=0):
    tree, body = bytearray(), bytearray()
    for number, (path, value) in enumerate(entries):
        path = path.encode("cp949")
        if isinstance(value, str):
            value = value.encode("cp949")
        key = zlib.crc32(value)
        tree.extend(struct.pack("<IIiIi", number, len(body), len(value), key, len(path)))
        tree.extend(path)
        body.extend(encrypt(value, key))
    key = 0x1234A5B6
    tree.extend(bytes([padding_byte]) * (-len(tree) % tree_alignment + extra_padding))
    encrypted_tree = encrypt(bytes(tree), key)
    return MAGIC + struct.pack("<iII", len(encrypted_tree), key, len(entries)) + encrypted_tree + body


def map_text(rectangles, portals=()):
    return ("[virtual movable area]\n" + "\n".join(" ".join(map(str, r)) for r in rectangles)
            + "\n[/virtual movable area]\n[town movable area]\n"
            + "\n".join(" ".join(map(str, p)) for p in portals) + "\n[/town movable area]\n")


def entries_for(areas):
    """areas maps (town,area) -> (rectangles, portal rows, kind)."""
    entries = [("town/town.lst", "\n".join(f'{t} `town{t}.twn`' for t in sorted({k[0] for k in areas})))]
    for town in sorted({k[0] for k in areas}):
        blocks = []
        for (t, a), (rectangles, portals, kind) in sorted(areas.items()):
            if t != town:
                continue
            path = f"test/{t}_{a}.map"
            blocks.append(f"[area]\n{a}\n`{path}`\n`[{kind}]`\n[/area]")
            entries.append(("map/" + path, map_text(rectangles, portals)))
        entries.append((f"town/town{town}.twn", "\n".join(blocks)))
    return entries


def native_steering_model(rectangles, start, goal):
    """Bounded integer-pixel model of the verified 186:122 axis steering.

    Evidence: 4795F0/4797D5/479807 choose separate axis speeds;
    4C8C00 checks each pixel/axis, 4C8F32 keeps the last valid position;
    776C60 uses right/bottom-exclusive rectangles. This model tests the
    planning assumption and is not a replacement for native acceptance.
    """
    x, y = start
    credit = 0
    for _ in range(5000):
        if (x, y) == goal:
            return x, y
        credit += 186
        x_steps, credit = divmod(credit, 122)
        for axis, count in ((0, x_steps), (1, 1)):
            for _ in range(count):
                position = [x, y]
                if position[axis] == goal[axis]:
                    break
                position[axis] += 1 if goal[axis] > position[axis] else -1
                if not any(a <= position[0] < a + w and b <= position[1] < b + h
                           for a, b, w, h in rectangles):
                    break
                x, y = position
    return x, y


def native_idle_model(rectangles, start, goal, blocked=()):
    """Fractional axis model: 433940 rounds +/-0.5 before the idle test.

    The SDK's 3px acceptance is separate from this native half-pixel band.
    No snapping to the integer target is performed.
    """
    position = list(start)
    rounded = lambda value: int(value + (0.5 if value >= 0 else -0.5))
    for _ in range(200_000):
        if tuple(map(rounded, position)) == goal:
            return tuple(position)
        moved = False
        for axis, speed in ((0, 186), (1, 122)):
            if rounded(position[axis]) == goal[axis]:
                continue
            candidate = position.copy()
            candidate[axis] += speed / 1000 * (1 if goal[axis] > rounded(position[axis]) else -1)
            x, y = candidate
            if (any(a <= x < a + w and b <= y < b + h for a, b, w, h in rectangles)
                    and not any(a <= x < a + w and b <= y < b + h for a, b, w, h in blocked)):
                position = candidate
                moved = True
        if not moved:
            return tuple(position)
    raise AssertionError("native idle model exceeded its bounded step count")


class TownNavigationTests(unittest.TestCase):
    def setUp(self):
        try:
            self.api = importlib.import_module("tools.debug.town_navigation")
        except ModuleNotFoundError:
            self.fail("read-only town navigation catalog is not implemented")
        scratch = Path(__file__).resolve().parents[2] / ".tmp" / "town-navigation-tests"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "fixture.pvf"

    def load_entries(self, entries):
        self.path.write_bytes(pvf_bytes(entries))
        return self.api.load_catalog(self.path)

    def load_areas(self, areas):
        return self.load_entries(entries_for(areas))

    def expect_code(self, code, action):
        with self.assertRaises(self.api.NavigationCatalogError) as caught:
            action()
        self.assertEqual(caught.exception.code, code)

    def test_encrypted_pvf_cp949_paths_normalization_and_immutable_catalog(self):
        entries = [("TOWN\\town.lst", "1 `마을.twn` // comment\r\n"),
                   ("town/마을.twn", "[area]\n0 `test/start.map` `[normal]`\n[/area]"),
                   ("map/test/start.map", map_text([(0, 0, 100, 80)]))]
        catalog = self.load_entries(entries)
        self.assertEqual(catalog.pvf_sha256, hashlib.sha256(self.path.read_bytes()).hexdigest())
        area = catalog.area((1, 0))
        self.assertEqual((area.town_id, area.area_id, area.map_path, area.kind),
                         (1, 0, "map/test/start.map", "normal"))
        self.assertIsInstance(area.walkable, tuple)
        self.assertTrue(area.walkable[0].contains(99.5, 40))
        with self.assertRaises(FrozenInstanceError):
            area.area_id = 9
        with self.assertRaises(FrozenInstanceError):
            area.walkable[0].x = 10

    def test_eight_byte_directory_alignment_preserves_catalog_and_rejects_junk(self):
        entries = entries_for({(1, 0): ([(0, 0, 30, 30)], [], "normal")})
        for suffix in range(8):
            varied = entries + [("unused/" + "x" * (suffix + 1), "")]
            with self.subTest(suffix=suffix):
                self.path.write_bytes(pvf_bytes(varied, tree_alignment=8))
                self.assertEqual(self.api.load_catalog(self.path).area((1, 0)).kind, "normal")
                self.path.write_bytes(pvf_bytes(varied, tree_alignment=8, extra_padding=8))
                self.expect_code("invalid_pvf", lambda: self.api.load_catalog(self.path))
        # This tree needs padding, so a nonzero byte must remain an error.
        varied = entries + [("unused/x", "")]
        self.path.write_bytes(pvf_bytes(varied, tree_alignment=8, padding_byte=1))
        self.expect_code("invalid_pvf", lambda: self.api.load_catalog(self.path))

    def test_pvf_header_tree_entry_bounds_duplicates_and_size_reject(self):
        valid = pvf_bytes(entries_for({(1, 0): ([(0, 0, 30, 30)], [], "normal")}))
        broken = [b"not pvf", valid[:26]]
        for offset, value in [(15, -4), (15, 3), (15, len(valid) + 4), (23, 0x7FFFFFFF)]:
            raw = bytearray(valid)
            struct.pack_into("<i", raw, offset, value)
            broken.append(raw)
        # Re-encrypt a corrupt tree containing an out-of-range body span.
        tree = struct.pack("<IIiIi", 1, 0xFFFFFFFC, 8, 1, 1) + b"a"
        encrypted = encrypt(tree, 5)
        broken.append(MAGIC + struct.pack("<iII", len(encrypted), 5, 1) + encrypted)
        duplicate = [("town/town.lst", ""), ("TOWN\\TOWN.LST", "")]
        broken.append(pvf_bytes(duplicate))
        broken.append(pvf_bytes([("../town/town.lst", "")]))
        for raw in broken:
            with self.subTest(size=len(raw)):
                self.path.write_bytes(raw)
                self.expect_code("invalid_pvf", lambda: self.api.load_catalog(self.path))

    def test_malformed_encrypted_tree_fields_are_bounded_before_script_read(self):
        for offset, length, path, path_length in [
                (0, -1, b"a", 1), (0, 8 * 1024 * 1024 + 1, b"a", 1),
                (0, 4, b"a", -1), (0, 4, b"a", 4097),
                (0, 4, b"\xff", 1), (0, 4, b"a\0b", 3), (1, 0, b"a", 1)]:
            with self.subTest(offset=offset, length=length, path=path):
                tree = struct.pack("<IIiIi", 1, offset, length, 5, path_length) + path
                encrypted = encrypt(tree, 7)
                self.path.write_bytes(MAGIC + struct.pack("<iII", len(encrypted), 7, 1) + encrypted + b"abcd")
                self.expect_code("invalid_pvf", lambda: self.api.load_catalog(self.path))
        self.expect_code("invalid_pvf", lambda: self.api.load_catalog(self.path.with_name("absent.pvf")))

    def test_numeric_bombs_and_unmatched_area_closers_report_catalog_error(self):
        good = entries_for({(1, 0): ([(0, 0, 30, 30)], [], "normal")})
        cases = [("town/town.lst", "9" * 5000 + " `town1.twn`"),
                 ("town/town1.twn", "[area]\n" + "9" * 5000 + " `test/1_0.map` `[normal]`\n[/area]"),
                 ("map/test/1_0.map", "[virtual movable area]\n" + "9" * 5000
                  + " 0 30 30\n[/virtual movable area]"),
                 ("town/town1.twn", good[-1][1] + "\n[/area]")]
        for name, replacement in cases:
            with self.subTest(name=name, prefix=replacement[:40]):
                self.expect_code("invalid_catalog", lambda: self.load_entries(
                    [(p, replacement if p == name else text) for p, text in good]))

    def test_invalid_catalog_rows_missing_map_and_duplicate_area_reject(self):
        good = entries_for({(1, 0): ([(0, 0, 30, 30)], [], "normal")})
        for replacement in ["[virtual movable area]\n0 0 -1 30\n[/virtual movable area]",
                            map_text([(0, 0, 30, 30)]) + "[town movable area]\n1 2 3\n[/town movable area]"]:
            bad = [(p, replacement if p.endswith(".map") else s) for p, s in good]
            self.expect_code("invalid_catalog", lambda: self.load_entries(bad))
        self.expect_code("invalid_catalog", lambda: self.load_entries(good[:-2] + good[-1:]))
        duplicate = [(p, s + s if p.endswith(".twn") else s) for p, s in good]
        self.expect_code("invalid_catalog", lambda: self.load_entries(duplicate))

    def test_route_finds_portals_without_using_unknown_or_dungeon_intermediates(self):
        areas = {
            (1, 0): ([(0, 0, 100, 100)], [(80, 20, 30, 30, 1, 1), (20, 20, 20, 20, 99, 99)], "normal"),
            (1, 1): ([(0, 0, 100, 100)], [(80, 20, 30, 30, 1, 2)], "gate"),
            (1, 2): ([(0, 0, 100, 100)], [(80, 20, 30, 30, 1, 3)], "dungeon gate"),
            (1, 3): ([(0, 0, 100, 100)], [], "normal")}
        catalog = self.load_areas(areas)
        route = catalog.route((1, 0), (1, 2))
        self.assertEqual(tuple(p.source for p in route), ((1, 0), (1, 1)))
        self.assertEqual(tuple(p.target for p in route), ((1, 1), (1, 2)))
        self.assertEqual(catalog.route((1, 0), (1, 0)), ())
        self.expect_code("no_route", lambda: catalog.route((1, 0), (1, 3)))
        self.expect_code("unknown_area", lambda: catalog.route((1, 0), (99, 99)))
        self.expect_code("unknown_area", lambda: catalog.area((9, 9)))
        for portal in route:
            self.assertTrue(portal.rect.contains(*portal.point))
            self.assertTrue(any(r.contains(*portal.point) for r in catalog.area(portal.source).walkable))
            self.assertGreater(portal.point[0], 80)
            self.assertLess(portal.point[0], 100)
        with self.assertRaises(FrozenInstanceError):
            route[0].point = (0, 0)

    def test_portal_outside_walkable_or_without_integer_interior_reject(self):
        for portal in [(200, 200, 20, 20, 1, 0), (99, 20, 20, 20, 1, 0)]:
            self.expect_code("invalid_catalog", lambda: self.load_areas({
                (1, 0): ([(0, 0, 100, 100)], [portal], "normal")}))

    def test_u_detour_first_moves_away_and_every_segment_stays_in_rectangle_union(self):
        rectangles = [(0, 0, 20, 100), (0, 80, 100, 20), (80, 0, 20, 100)]
        catalog = self.load_areas({(1, 0): (rectangles, [], "normal")})
        start, goal = (10.5, 10.5), (90, 10)
        points = catalog.waypoints((1, 0), start, goal)
        self.assertEqual(points[-1], goal)
        self.assertGreaterEqual(len(points), 3)
        self.assertGreater(points[0][1], 70)
        previous = start
        for point in points:
            self.assertTrue(all(type(v) is int for v in point))
            for step in range(1001):
                x = previous[0] + (point[0] - previous[0]) * step / 1000
                y = previous[1] + (point[1] - previous[1]) * step / 1000
                self.assertTrue(any(r.contains(x, y) for r in catalog.area((1, 0)).walkable))
            previous = point

    def test_visibility_simplifies_straight_union_and_handles_thin_overlap(self):
        catalog = self.load_areas({(1, 0): ([(0, 0, 50, 50), (49, 0, 51, 50)], [], "normal")})
        self.assertEqual(catalog.waypoints((1, 0), (1.5, 20.5), (99, 20)), ((99, 20),))
        self.assertEqual(catalog.waypoints((1, 0), (99, 20), (99, 20)), ((99, 20),))

    def test_disconnected_and_illegal_endpoints_fail_instead_of_direct_target(self):
        catalog = self.load_areas({(1, 0): ([(0, 0, 20, 20), (50, 0, 20, 20)], [], "normal")})
        self.expect_code("no_path", lambda: catalog.waypoints((1, 0), (10, 10), (60, 10)))
        self.expect_code("outside_walkable", lambda: catalog.waypoints((1, 0), (-1, 0), (10, 10)))
        self.expect_code("outside_walkable", lambda: catalog.waypoints((1, 0), (10, 10), (40, 10)))
        self.expect_code("outside_walkable", lambda: catalog.waypoints((1, 0), (float("nan"), 0), (10, 10)))
        self.expect_code("outside_walkable", lambda: catalog.waypoints((1, 0), (10 ** 1000, 0), (10, 10)))

    def test_native_half_open_right_bottom_boundaries_and_degenerate_box(self):
        catalog = self.load_areas({(1, 0): ([(0, 0, 20, 20)], [], "normal")})
        rect = catalog.area((1, 0)).walkable[0]
        self.assertTrue(rect.contains(0, 0))
        self.assertFalse(rect.contains(20, 10))
        self.assertFalse(rect.contains(10, 20))
        for start, goal in [((10, 10), (20, 10)), ((10, 10), (10, 20)),
                            ((20, 2), (20, 18)), ((2, 20), (18, 20))]:
            with self.subTest(start=start, goal=goal):
                self.expect_code("outside_walkable", lambda: catalog.waypoints((1, 0), start, goal))

    def test_corner_only_contact_has_no_native_axis_path_but_shared_edges_do(self):
        rectangles = [(0, 0, 20, 20), (20, 20, 20, 20)]
        self.assertEqual(native_steering_model(rectangles, (10, 10), (30, 30)), (19, 19))
        catalog = self.load_areas({(1, 0): (rectangles, [], "normal")})
        self.expect_code("no_path", lambda: catalog.waypoints((1, 0), (10, 10), (30, 30)))
        for rectangles, start, goal in [
                ([(0, 0, 20, 20), (20, 0, 20, 20)], (10, 10), (30, 10)),
                ([(0, 0, 20, 20), (0, 20, 20, 20)], (10, 10), (10, 30)),
                ([(0, 0, 20, 100), (20, 50, 20, 50)], (10, 10), (30, 90)),
                ([(0, 0, 20, 100), (20, 50, 20, 50)], (30, 90), (10, 10)),
                ([(0, 0, 100, 20), (50, 20, 50, 20)], (10, 10), (90, 30)),
                ([(0, 0, 100, 20), (50, 20, 50, 20)], (90, 30), (10, 10))]:
            catalog = self.load_areas({(1, 0): (rectangles, [], "normal")})
            position = start
            for point in catalog.waypoints((1, 0), position, goal):
                position = native_steering_model(rectangles, position, point)
                self.assertEqual(position, point)
            self.assertEqual(position, goal)

    def test_native_fixed_axis_ratio_requires_turns_despite_straight_visibility(self):
        rectangles = [(0, 0, 100, 100), (90, 20, 110, 20), (180, 20, 220, 80)]
        start, goal = (10, 10), (310, 60)
        self.assertEqual(native_steering_model(rectangles, start, goal), (99, 60))
        catalog = self.load_areas({(1, 0): (rectangles, [], "normal")})
        points = catalog.waypoints((1, 0), start, goal)
        self.assertGreater(len(points), 1, "a mathematically visible segment is not a native-safe leg")
        previous = start
        for point in points:
            # Exhaust the integer cells of each closed leg bounding box.
            # All script rectangle boundaries are integers and half-open.
            for x in range(min(previous[0], point[0]), max(previous[0], point[0]) + 1):
                for y in range(min(previous[1], point[1]), max(previous[1], point[1]) + 1):
                    self.assertTrue(any(a <= x < a + w and b <= y < b + h for a, b, w, h in rectangles))
            self.assertEqual(native_steering_model(rectangles, previous, point), point)
            previous = point
        self.assertEqual(previous, goal)

    def test_thin_overlap_bridge_tolerates_real_float_stop_in_both_directions(self):
        rectangles = [(9, 196, 1548, 142), (729, 157, 109, 40), (1048, 322, 89, 33)]
        catalog = self.load_areas({(1, 0): (rectangles, [], "normal")})
        observed = (782.58197, 195.941757)
        self.assertEqual(native_idle_model(rectangles, observed, (783, 196)), observed)
        for start, goal in [((785.0, 174.0), (16, 264)), (observed, (16, 264)),
                            ((16.0, 264.0), (785, 174))]:
            with self.subTest(start=start, goal=goal):
                position = start
                for _ in range(12):
                    points = catalog.waypoints((1, 0), position, goal)
                    self.assertEqual(points[-1], goal, "the final user goal is preserved")
                    waypoint = points[0]
                    stopped = native_idle_model(rectangles, position, waypoint)
                    self.assertTrue(all(abs(stopped[i] - waypoint[i]) < 0.5 for i in (0, 1)))
                    if waypoint == goal:
                        break
                    # A later SDK sample can be up to3px off; bridge targets
                    # must still leave all such positions inside walkable.
                    for dx in (-3, 3):
                        for dy in (-3, 3):
                            self.assertTrue(any(a <= waypoint[0] + dx < a + w
                                                and b <= waypoint[1] + dy < b + h
                                                for a, b, w, h in rectangles))
                    following = catalog.waypoints((1, 0), stopped, goal)
                    self.assertNotEqual(following[0], waypoint,
                                        "fresh planning must not repeat a rounded-idle bridge forever")
                    position = stopped
                else:
                    self.fail("thin-overlap route made no bounded tolerant progress")

    def test_blocked_foreign_portal_is_avoided_by_every_native_leg(self):
        self.assertIn("blocked_portals", inspect.signature(self.api.Catalog.waypoints).parameters,
                      "waypoints needs the backward-compatible blocked_portals keyword")
        rectangles = [(0, 0, 100, 100)]
        catalog = self.load_areas({(1, 0): (rectangles, [], "normal")})
        foreign = self.api.Rect(40, 40, 20, 20)
        portal = self.api.Portal((1, 0), (9, 9), foreign, (50, 50))
        original = catalog.area((1, 0))
        for blocked in [(foreign,), (portal,)]:
            position, goal = (10.0, 50.0), (90, 50)
            points = catalog.waypoints((1, 0), position, goal, blocked_portals=blocked)
            self.assertGreater(len(points), 1)
            self.assertEqual(points[-1], goal)
            for index, point in enumerate(points):
                end_margin = 3 if index < len(points) - 1 else 0
                for x in range(int(min(position[0], point[0] - end_margin)),
                               int(max(position[0], point[0] + end_margin)) + 1):
                    for y in range(int(min(position[1], point[1] - end_margin)),
                                   int(max(position[1], point[1] + end_margin)) + 1):
                        self.assertFalse(36 <= x < 64 and 36 <= y < 64,
                                         "full native leg and internal stopping region avoid the expanded door")
                position = native_idle_model(rectangles, position, point, [(40, 40, 20, 20)])
                self.assertTrue(all(abs(position[i] - point[i]) < 0.5 for i in (0, 1)))
            self.assertEqual(catalog.area((1, 0)), original, "blocking a route does not mutate catalog geometry")

    def test_birth_portal_escape_does_not_cut_through_foreign_portal(self):
        self.assertIn("blocked_portals", inspect.signature(self.api.Catalog.waypoints).parameters)
        catalog = self.load_areas({(1, 0): ([(0, 0, 100, 100)], [], "normal")})
        foreign = self.api.Rect(40, 80, 20, 5)
        self.assertEqual(self.api.PORTAL_BLOCK_MARGIN, 4)
        points = catalog.waypoints((1, 0), (50.0, 90.0), (25, 90), blocked_portals=(foreign,))
        self.assertEqual(points, ((25, 90),))
        # Selected birth portal30,80,40,20 is deliberately allowed by the
        # caller. The foreign door inside it still blocks a straight escape.
        self.expect_code("outside_walkable", lambda: catalog.waypoints(
            (1, 0), (50.0, 90.0), (50, 76), blocked_portals=(foreign,)))

    def test_blocked_portal_parameters_and_half_open_expansion_bounds(self):
        catalog = self.load_areas({(1, 0): ([(0, 0, 100, 100)], [], "normal")})
        foreign = self.api.Rect(40, 40, 20, 20)
        self.expect_code("outside_walkable", lambda: catalog.waypoints(
            (1, 0), (10, 50), (36, 50), blocked_portals=(foreign,)))
        self.assertEqual(catalog.waypoints((1, 0), (64, 50), (90, 50),
                                          blocked_portals=(foreign,)), ((90, 50),))
        for invalid in [None, [None], [self.api.Rect(0, 0, -1, 4)], [foreign] * 257,
                        [self.api.Portal((9, 0), (1, 0), foreign, (50, 50))]]:
            self.expect_code("invalid_catalog", lambda: catalog.waypoints(
                (1, 0), (10, 10), (90, 90), blocked_portals=invalid))

    def test_actual_pvf_tolerant_replanning_avoids_unselected_doors(self):
        from tools.acceptance.town_fixture import load_fixture_catalog
        fixture = load_fixture_catalog()
        catalog = fixture.catalog
        self.assertEqual(fixture.source_pvf_sha256, ACTUAL_SHA)
        self.assertEqual(catalog.pvf_sha256, fixture.catalog_pvf_sha256)
        area = catalog.area((1, 0))
        rectangles = [(r.x, r.y, r.width, r.height) for r in area.walkable]
        for start, target in [((785.0, 174.0), (2, 1)),
                              ((782.58197, 195.941757), (2, 1)),
                              ((200.0, 264.0), (1, 1))]:
            selected = next(p for p in area.portals if p.target == target)
            blocked = tuple(p for p in area.portals if p != selected)
            obstacles = [(p.rect.x, p.rect.y, p.rect.width, p.rect.height) for p in blocked]
            position = start
            for _ in range(12):
                points = catalog.waypoints((1, 0), position, selected.point, blocked_portals=blocked)
                self.assertEqual(points[-1], selected.point)
                point = points[0]
                stopped = native_idle_model(rectangles, position, point, obstacles)
                self.assertTrue(all(abs(stopped[i] - point[i]) < 0.5 for i in (0, 1)))
                if point == selected.point:
                    break
                self.assertNotEqual(catalog.waypoints((1, 0), stopped, selected.point,
                                                     blocked_portals=blocked)[0], point)
                position = stopped
            else:
                self.fail("actual PVF tolerant replanning failed to make bounded progress")

    def test_actual_pvf_sha_portal_points_and_multitown_forward_reverse_regression(self):
        from tools.acceptance.town_fixture import load_fixture_catalog
        fixture = load_fixture_catalog()
        catalog = fixture.catalog
        self.assertEqual(fixture.source_pvf_sha256, ACTUAL_SHA)
        self.assertEqual(catalog.pvf_sha256, fixture.catalog_pvf_sha256)
        self.assertEqual(catalog.area((1, 0)).map_path, "map/elvengard/elvengard.map")
        self.assertEqual(catalog.area((1, 2)).kind, "dungeon_gate")
        for start, goal in [((1, 1), (5, 1)), ((5, 1), (1, 1)), ((1, 0), (3, 1))]:
            route = catalog.route(start, goal)
            self.assertGreater(len(route), 1)
            current = start
            for portal in route:
                self.assertEqual(portal.source, current)
                area = catalog.area(current)
                self.assertNotEqual(area.kind, "dungeon_gate")
                self.assertTrue(portal.rect.contains(*portal.point))
                self.assertTrue(any(r.contains(*portal.point) for r in area.walkable))
                r = area.walkable[0]
                position = (r.x + r.width // 2, r.y + r.height // 2)
                points = catalog.waypoints(current, position, portal.point)
                self.assertEqual(points[-1], portal.point)
                rectangles = [(r.x, r.y, r.width, r.height) for r in area.walkable]
                for point in points:
                    # Independent Cartesian boundary partition checks every
                    # membership cell of the leg box, including right/bottom.
                    xs, ys = {position[0], point[0]}, {position[1], point[1]}
                    for a, b, w, h in rectangles:
                        xs.update(x for x in (a, a + w) if min(position[0], point[0]) <= x <= max(position[0], point[0]))
                        ys.update(y for y in (b, b + h) if min(position[1], point[1]) <= y <= max(position[1], point[1]))
                    for x in xs:
                        for y in ys:
                            self.assertTrue(any(a <= x < a + w and b <= y < b + h for a, b, w, h in rectangles))
                    self.assertEqual(native_steering_model(rectangles, position, point), point)
                    position = point
                current = portal.target
            self.assertEqual(current, goal)


if __name__ == "__main__":
    unittest.main()
