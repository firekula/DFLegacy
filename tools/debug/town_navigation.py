"""Read-only ACT1 town catalog and bounded static navigation.

PVF word decoding follows src/DFLegacy.Protocol/PvfArchiveReader.cs. This
module never extracts files, controls a client, or changes navigation state.
Rectangles use the script's x/y/width/height convention and the original
776C60 right/bottom-exclusive bounds. A native leg requires its entire
closed axis-aligned bounding box to be covered, because 4795F0 steers x/y
independently at 186:122 rather than following an arbitrary straight line.
"""
from collections import deque
from dataclasses import dataclass
import hashlib
import math
from pathlib import Path
import re
import struct
from types import MappingProxyType
from typing import Mapping


_MAGIC = b"DNF_SCRIPT_PACK"
_MAX_ARCHIVE = 256 * 1024 * 1024
_MAX_TREE = 16 * 1024 * 1024
_MAX_FILES = 200_000
_MAX_ENTRY = 8 * 1024 * 1024
_MAX_AREAS = 4096
_MAX_RECTS = 256
_MAX_PORTALS = 256
_MAX_PLAN_RECTS = 4096
_COORD_LIMIT = 1_000_000
# The native idle getter rounds to the nearest integer (raw +/-0.5).
# These larger margins are a conservative planning policy covering the SDK's
# separate3px observation tolerance; they are not native steering constants.
_STOP_MARGIN = 3
_WAYPOINT_MARGIN = 4
# Public so the controller can choose birth-portal exit candidates against
# exactly the same half-open expansion. Left/top boundary itself is blocked.
PORTAL_BLOCK_MARGIN = 4
AreaKey = tuple[int, int]
Point = tuple[int, int]


class NavigationCatalogError(ValueError):
    """Stable codes: invalid_pvf, invalid_catalog, unknown_area, no_route,
    outside_walkable, no_path. Details are diagnostic, never a route result.
    """

    def __init__(self, code: str, detail: str):
        self.code = code
        super().__init__(f"{code}: {detail}")


@dataclass(frozen=True, slots=True)
class Rect:
    x: int
    y: int
    width: int
    height: int

    @property
    def right(self) -> int:
        return self.x + self.width

    @property
    def bottom(self) -> int:
        return self.y + self.height

    def contains(self, x: float, y: float) -> bool:
        return self.x <= x < self.right and self.y <= y < self.bottom


@dataclass(frozen=True, slots=True)
class Portal:
    source: AreaKey
    target: AreaKey
    rect: Rect
    point: Point


@dataclass(frozen=True, slots=True)
class Area:
    town_id: int
    area_id: int
    map_path: str
    walkable: tuple[Rect, ...]
    portals: tuple[Portal, ...]
    kind: str


def _intersection(a: Rect, b: Rect) -> Rect | None:
    x, y = max(a.x, b.x), max(a.y, b.y)
    right, bottom = min(a.right, b.right), min(a.bottom, b.bottom)
    # A shared edge can be crossed through its middle. A corner alone is
    # deliberately not a corridor with room to walk through.
    if right < x or bottom < y or (right == x and bottom == y):
        return None
    return Rect(x, y, right - x, bottom - y)


def _middle(rect: Rect) -> Point:
    return rect.x + rect.width // 2, rect.y + rect.height // 2


def _bridge(a: Rect, b: Rect) -> tuple[Point, ...] | None:
    shared = _intersection(a, b)
    if shared is None:
        return None
    margin = _WAYPOINT_MARGIN
    ax, ay, ar, ab = a.x + margin, a.y + margin, a.right - margin, a.bottom - margin
    bx, by, br, bb = b.x + margin, b.y + margin, b.right - margin, b.bottom - margin
    if ax > ar or ay > ab or bx > br or by > bb:
        return None  # no supported stopping clearance in a thin fragment
    x, y = _middle(shared)
    clamp = lambda value, low, high: max(low, min(value, high))
    candidates = []
    if max(ax, bx) <= min(ar, br):
        x_mid = clamp(x, max(ax, bx), min(ar, br))
        candidates.append(((x_mid, clamp(y, ay, ab)), (x_mid, clamp(y, by, bb))))
    if max(ay, by) <= min(ab, bb):
        y_mid = clamp(y, max(ay, by), min(ab, bb))
        candidates.append(((clamp(x, ax, ar), y_mid), (clamp(x, bx, br), y_mid)))
    for first, second in candidates:
        # Target well inside the next rectangle, not at the one-pixel shared
        # strip where a rounded-idle raw position can remain on the old side.
        if _leg_covered((a, b), first, second, _STOP_MARGIN, _STOP_MARGIN):
            return (first,) if first == second else (first, second)
    return None


def _box_covered(rectangles: tuple[Rect, ...], start, end) -> bool:
    """Prove coverage of the closed leg AABB by the half-open union.

    Sweep all rectangle x boundaries and intervening open slabs. Active
    rectangles are constant in each slab; their half-open y intervals must
    cover the whole closed [bottom,top] range, including a degenerate line.
    Boundary columns are checked separately so excluded right/bottom edges
    cannot pass through an equality or an epsilon comparison.
    """
    left, right = sorted((start[0], end[0]))
    bottom, top = sorted((start[1], end[1]))
    cuts = {left, right}
    for rect in rectangles:
        cuts.update(x for x in (rect.x, rect.right) if left <= x <= right)
    columns = sorted(cuts)
    columns += [(a + b) / 2 for a, b in zip(columns, columns[1:])]
    for x in columns:
        covered = bottom
        for low, high in sorted((r.y, r.bottom) for r in rectangles if r.x <= x < r.right):
            if high <= covered:
                continue
            if low > covered:
                return False
            covered = high
            if covered > top:  # upper bounds are excluded, including at top
                break
        else:
            return False
    return True


def _leg_covered(rectangles, start, end, start_margin=0, end_margin=0) -> bool:
    low = (min(start[0] - start_margin, end[0] - end_margin),
           min(start[1] - start_margin, end[1] - end_margin))
    high = (max(start[0] + start_margin, end[0] + end_margin),
            max(start[1] + start_margin, end[1] + end_margin))
    return _box_covered(rectangles, low, high)


def _without_portals(rectangles: tuple[Rect, ...], key: AreaKey, blocked) -> tuple[Rect, ...]:
    if not isinstance(blocked, (tuple, list)) or len(blocked) > _MAX_PORTALS:
        raise NavigationCatalogError("invalid_catalog", "blocked_portals must be a bounded sequence")
    for item in blocked:
        if isinstance(item, Portal):
            if item.source != key:
                raise NavigationCatalogError("invalid_catalog", "blocked portal belongs to another area")
            item = item.rect
        if (not isinstance(item, Rect) or any(type(v) is not int for v in
                (item.x, item.y, item.width, item.height))):
            raise NavigationCatalogError("invalid_catalog", "blocked_portals requires Portal or Rect values")
        _rect((item.x, item.y, item.width, item.height))
        margin = PORTAL_BLOCK_MARGIN
        obstacle = Rect(item.x - margin, item.y - margin,
                        item.width + margin * 2, item.height + margin * 2)
        remaining = []
        for ground in rectangles:
            shared = _intersection(ground, obstacle)
            if shared is None or shared.width == 0 or shared.height == 0:
                remaining.append(ground)
                continue
            # Four disjoint half-open pieces exactly subtract the expanded
            # door, preserving the geometry outside it without rasterization.
            pieces = ((ground.x, ground.y, ground.width, shared.y - ground.y),
                      (ground.x, shared.bottom, ground.width, ground.bottom - shared.bottom),
                      (ground.x, shared.y, shared.x - ground.x, shared.height),
                      (shared.right, shared.y, ground.right - shared.right, shared.height))
            remaining.extend(Rect(*values) for values in pieces if values[2] > 0 and values[3] > 0)
            if len(remaining) > _MAX_PLAN_RECTS:
                raise NavigationCatalogError("no_path", "blocked portal geometry exceeds planning bound")
        rectangles = tuple(dict.fromkeys(remaining))
    return rectangles


@dataclass(frozen=True, slots=True)
class Catalog:
    pvf_sha256: str
    _areas: Mapping[AreaKey, Area]

    def area(self, key: AreaKey) -> Area:
        if (not isinstance(key, tuple) or len(key) != 2
                or any(type(v) is not int for v in key) or key not in self._areas):
            raise NavigationCatalogError("unknown_area", f"unknown town/area {key!r}")
        return self._areas[key]

    def route(self, start_key: AreaKey, goal_key: AreaKey) -> tuple[Portal, ...]:
        self.area(start_key)
        self.area(goal_key)
        queue = deque([start_key])
        previous: dict[AreaKey, Portal | None] = {start_key: None}
        while queue:
            current = queue.popleft()
            if current == goal_key:
                result = []
                while previous[current] is not None:
                    portal = previous[current]
                    result.append(portal)
                    current = portal.source
                return tuple(reversed(result))
            for portal in self._areas[current].portals:
                target = self._areas.get(portal.target)
                if (target is None or portal.target in previous
                        or (target.kind == "dungeon_gate" and portal.target != goal_key)):
                    continue
                previous[portal.target] = portal
                queue.append(portal.target)
        raise NavigationCatalogError("no_route", f"no ordinary town route {start_key} -> {goal_key}")

    def waypoints(self, key: AreaKey, start_xy: tuple[float, float],
                  target_xy: Point, *, blocked_portals=()) -> tuple[Point, ...]:
        """Plan native-safe legs; the last point is the unchanged user goal.

        blocked_portals accepts up to256 Portal/Rect values. Each half-open
        door is expanded4px on every side and subtracted from walkable before
        planning. Internal bridge points have4px inward stopping clearance;
        complete leg AABBs also include the possible3px internal stop region.
        The caller alone decides which birth/selected portals to exempt.
        """
        rectangles = _without_portals(self.area(key).walkable, key, blocked_portals)
        if (not isinstance(start_xy, tuple) or len(start_xy) != 2
                or any(type(v) not in (int, float) or abs(v) > _COORD_LIMIT
                       or not math.isfinite(v) for v in start_xy)
                or not isinstance(target_xy, tuple) or len(target_xy) != 2
                or any(type(v) is not int or abs(v) > _COORD_LIMIT for v in target_xy)):
            raise NavigationCatalogError("outside_walkable", "invalid or non-finite endpoint")
        starts = [i for i, rect in enumerate(rectangles) if rect.contains(*start_xy)]
        goals = {i for i, rect in enumerate(rectangles) if rect.contains(*target_xy)}
        if not starts or not goals:
            raise NavigationCatalogError("outside_walkable", f"endpoint outside {key} walkable rectangles")
        if _box_covered(rectangles, start_xy, target_xy):
            return (target_xy,)
        # A sequence of rectangle intersections supplies a path even where
        # reaching the goal requires initially moving farther away from it.
        queue = deque(starts)
        previous = {i: None for i in starts}
        final = None
        while queue:
            current = queue.popleft()
            if current in goals:
                final = current
                break
            for next_index, rect in enumerate(rectangles):
                if next_index in previous:
                    continue
                bridge = _bridge(rectangles[current], rect)
                if bridge is not None:
                    previous[next_index] = (current, bridge)
                    queue.append(next_index)
        if final is None:
            raise NavigationCatalogError("no_path", f"disconnected walkable rectangles in {key}")
        turns = [target_xy]
        while previous[final] is not None:
            final, bridge = previous[final]
            turns.extend(reversed(bridge))
        path = [start_xy] + list(reversed(turns))
        # Simplify only where the entire native leg AABB stays walkable.
        result, index = [], 0
        while index < len(path) - 1:
            for next_index in range(len(path) - 1, index, -1):
                if _leg_covered(rectangles, path[index], path[next_index],
                                _STOP_MARGIN if index > 0 else 0,
                                _STOP_MARGIN if next_index < len(path) - 1 else 0):
                    result.append(path[next_index])
                    index = next_index
                    break
            else:
                raise NavigationCatalogError("no_path", "rectangle path has no covered next leg")
        return tuple(result)


def _decode(data: bytes, key: int) -> bytes:
    return b"".join(struct.pack("<I", ((word ^ key) >> 6) | (((word ^ key) << 26) & 0xFFFFFFFF))
                    for (word,) in struct.iter_unpack("<I", data))


def _path(value: str) -> str:
    value = value.replace("\\", "/").lower()
    if (not value or value.startswith("/") or "\0" in value or ":" in value
            or any(part in ("", ".", "..") for part in value.split("/"))):
        raise NavigationCatalogError("invalid_pvf", "unsafe or empty virtual path")
    return value


class _Pvf:
    def __init__(self, path):
        try:
            if not 27 <= Path(path).stat().st_size <= _MAX_ARCHIVE:
                raise NavigationCatalogError("invalid_pvf", "archive size outside bounds")
            with Path(path).open("rb") as stream:
                raw = stream.read(_MAX_ARCHIVE + 1)
        except OSError as error:
            raise NavigationCatalogError("invalid_pvf", f"cannot read archive: {error}") from error
        if not 27 <= len(raw) <= _MAX_ARCHIVE or raw[:15] != _MAGIC:
            raise NavigationCatalogError("invalid_pvf", "invalid DNF_SCRIPT_PACK header")
        length, key, count = struct.unpack_from("<iII", raw, 15)
        if (length <= 0 or length % 4 or length > min(_MAX_TREE, len(raw) - 27)
                or count > min(_MAX_FILES, length // 21)):
            raise NavigationCatalogError("invalid_pvf", "invalid tree size or count")
        self.raw, self.body, self.entries = raw, 27 + length, {}
        self.sha256 = hashlib.sha256(raw).hexdigest()
        tree, cursor, identifiers = _decode(raw[27:self.body], key), 0, set()
        for _ in range(count):
            if cursor > len(tree) - 20:
                raise NavigationCatalogError("invalid_pvf", "truncated tree entry")
            identifier, offset, size, key, path_length = struct.unpack_from("<IIiIi", tree, cursor)
            cursor += 20
            if (size < 0 or size > _MAX_ENTRY or not 0 < path_length <= 4096
                    or path_length > len(tree) - cursor
                    or offset % 4 or offset > len(raw) - self.body - ((size + 3) & ~3)):
                raise NavigationCatalogError("invalid_pvf", "entry size/path/body outside bounds")
            try:
                virtual_path = _path(tree[cursor:cursor + path_length].decode("cp949").rstrip("\0"))
            except UnicodeDecodeError as error:
                raise NavigationCatalogError("invalid_pvf", "invalid CP949 directory path") from error
            cursor += path_length
            if virtual_path in self.entries or identifier in identifiers:
                raise NavigationCatalogError("invalid_pvf", "duplicate directory path or identifier")
            identifiers.add(identifier)
            self.entries[virtual_path] = offset, size, key
        if len(tree) - cursor > 3 or any(tree[cursor:]):
            raise NavigationCatalogError("invalid_pvf", "unexpected tree trailing bytes")

    def text(self, virtual_path: str) -> str:
        path = _path(virtual_path)
        if path not in self.entries:
            raise NavigationCatalogError("invalid_catalog", f"missing script {path}")
        offset, size, key = self.entries[path]
        start = self.body + offset
        plain = _decode(self.raw[start:start + ((size + 3) & ~3)], key)[:size]
        # Like Encoding.GetEncoding(949) in the source reader. Localized
        # comments may contain unmappable bytes; structural tags are ASCII.
        text = plain.decode("cp949", errors="replace")
        return re.sub(r"//[^\r\n]*", "", text)


def _block(text: str, tag: str, required=False) -> str:
    opening, closing = f"[{tag}]", f"[/{tag}]"
    if text.lower().count(opening) != text.lower().count(closing):
        raise NavigationCatalogError("invalid_catalog", f"unclosed {tag}")
    matches = re.findall(re.escape(opening) + r"(.*?)" + re.escape(closing), text, re.I | re.S)
    if len(matches) > 1 or (required and len(matches) != 1):
        raise NavigationCatalogError("invalid_catalog", f"missing/duplicate {tag}")
    return matches[0] if matches else ""


def _rows(text: str, width: int, limit: int) -> list[tuple[int, ...]]:
    rows = []
    for line in text.splitlines():
        if not line.strip():
            continue
        parts = line.split()
        if len(parts) != width or any(not re.fullmatch(r"-?\d{1,7}", part, re.ASCII) for part in parts):
            raise NavigationCatalogError("invalid_catalog", f"expected {width} integer columns")
        values = tuple(int(part) for part in parts)
        if any(abs(v) > _COORD_LIMIT for v in values) or len(rows) >= limit:
            raise NavigationCatalogError("invalid_catalog", "row value/count outside bounds")
        rows.append(values)
    return rows


def _rect(values) -> Rect:
    x, y, width, height = values
    if width <= 0 or height <= 0 or abs(x + width) > _COORD_LIMIT or abs(y + height) > _COORD_LIMIT:
        raise NavigationCatalogError("invalid_catalog", "invalid rectangle size or bounds")
    return Rect(x, y, width, height)


def _portal_point(rect: Rect, walkable: tuple[Rect, ...]) -> Point:
    shared = [_intersection(rect, ground) for ground in walkable]
    shared = [r for r in shared if r is not None and r.width >= 2 and r.height >= 2]
    if not shared:
        raise NavigationCatalogError("invalid_catalog", "portal has no safe integer walkable interior")
    return _middle(max(shared, key=lambda r: (min(r.width, r.height), r.width * r.height)))


def load_catalog(pvf_path) -> Catalog:
    archive = _Pvf(pvf_path)
    areas, towns = {}, set()
    for line in archive.text("town/town.lst").splitlines():
        if not line.strip():
            continue
        row = re.fullmatch(r"\s*(\d{1,3})\s+`([^`]+)`\s*", line, re.ASCII)
        if row is None or not 0 <= int(row[1]) <= 255 or int(row[1]) in towns:
            raise NavigationCatalogError("invalid_catalog", "invalid/duplicate town list row")
        town = int(row[1])
        towns.add(town)
        town_text = archive.text("town/" + row[2])
        blocks = re.findall(r"\[area\](.*?)\[/area\]", town_text, re.S | re.I)
        if (town_text.lower().count("[area]") != len(blocks)
                or town_text.lower().count("[/area]") != len(blocks) or not blocks):
            raise NavigationCatalogError("invalid_catalog", "invalid town area blocks")
        for block in blocks:
            head = re.match(r"\s*(\d{1,3})\s+`([^`]+)`", block, re.ASCII)
            kinds = re.findall(r"`\[(normal|gate|dungeon gate|pub|fishing place)\]`", block, re.I)
            if head is None or not 0 <= int(head[1]) <= 255 or len(kinds) != 1:
                raise NavigationCatalogError("invalid_catalog", "invalid area header/kind")
            area_id, map_path = int(head[1]), _path("map/" + head[2])
            key = town, area_id
            if key in areas or len(areas) >= _MAX_AREAS:
                raise NavigationCatalogError("invalid_catalog", "duplicate/too many areas")
            text = archive.text(map_path)
            walkable = tuple(_rect(row) for row in _rows(
                _block(text, "virtual movable area", True), 4, _MAX_RECTS))
            if not walkable:
                raise NavigationCatalogError("invalid_catalog", "area has no walkable rectangles")
            portals = []
            for row in _rows(_block(text, "town movable area"), 6, _MAX_PORTALS):
                rect, target = _rect(row[:4]), row[4:]
                if target != (-1, -1) and any(not 0 <= v <= 255 for v in target):
                    raise NavigationCatalogError("invalid_catalog", "invalid portal target id")
                portals.append(Portal(key, target, rect, _portal_point(rect, walkable)))
            areas[key] = Area(town, area_id, map_path, walkable, tuple(portals),
                              kinds[0].lower().replace(" ", "_"))
    if not areas:
        raise NavigationCatalogError("invalid_catalog", "empty town catalog")
    return Catalog(archive.sha256, MappingProxyType(areas))
