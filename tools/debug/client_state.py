"""Read validated debug.dll state; never read game memory or send input."""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
import math
import os
from pathlib import Path
import re
import time


SCHEMA = "dfl-client-state/v1"
SCHEMA_V2 = "dfl-client-state/v2"
MAX_BYTES = 1_048_576
UINT64_MAX = (1 << 64) - 1
_TOKEN = re.compile(r"0x[0-9a-fA-F]{8}\Z")
_PATH = re.compile(r"[A-Za-z][A-Za-z0-9_]*(?:\.(?:[A-Za-z][A-Za-z0-9_]*|[0-9]+))*\Z")
_POSITION = {"x": "number", "y": "number", "z": "number"}
_MP_SOURCE = {"actor_token": "token", "local": "bool"}
_PLAYER = {
    "uid": "uid", "user_token": "token", "actor_token": "token",
    "map_token": "token", "hp": "hp", "max_hp": "hp", "mp": "hp",
    "max_mp": "hp", "dead": "bool", "mp_source": _MP_SOURCE,
    "same_map": "bool", "detached": "bool", "position": _POSITION,
}
_ENEMY = {
    "uid": "u32", "monster_id": "u32", "hp": "hp", "dead": "bool",
    "active": "bool", "position": _POSITION, "action": "i32",
    "queued_action": "u32", "authority_token": "u32",
}
_SKILL_RESOURCES = {'scope':'resource_scope','mp_cost':'hp','use_limit':'hp','use_count':'hp','items_sufficient':'bool'}
_SKILL = {"id": "u32", "level": "u32", "cooldown_ms": "duration", "cooldown_blocked": "bool", "usable": "bool",
          'resources':_SKILL_RESOURCES}
_RECT = {
    "left": "i32", "top": "i32", "width": "u32", "height": "u32",
}
_DOOR = {"enabled": "bool", "rect": _RECT}
_POINT = {"x": "i32", "y": "i32"}
_TOWN = {
    "town_id": "i32", "area_id": "i32", "context_token": "token",
    "map_token": "token", "position": _POSITION,
    "destination": _POINT, "direction_raw": _POINT, "auto_move_active": "bool",
}
_SELECTOR_EXTRA = {
    'instance_token': 'token', 'transition_pending': 'bool', 'phase_raw': 'u32',
    'stage_token': 'token', 'input_suppression_raw': 'i32', 'record_count': 'u32',
    'selected_dungeon_id': 'i32', 'difficulty_raw': 'i32', 'maximum_difficulty_raw': 'i32',
}
_SELECTOR = {"visible": "bool", "active_raw": "u8", "selected": "i32", "hover": "i32", **_SELECTOR_EXTRA}
_PLACEMENT = {'layer_token': 'token', 'attached': 'bool', 'order': 'u32',
              'layer_shown_raw': 'bool', 'layer_input_enabled_raw': 'bool'}
_DIALOG_BASE = {'class_id': 'u32', 'instance_token': 'token', 'root_token': 'token',
           'shown_raw': 'bool', 'input_enabled_raw': 'bool'}
_DIALOG = {**_DIALOG_BASE, 'placement': _PLACEMENT}
_DIALOG_LEGACY_CLASSES = (0x23,0x25,0x30,0x59,0x6A)
_DIALOG_CLASSES = (7,9,0x23,0x25,0x30,0x56,0x59,0x6A)
_DIALOGS = {'valid': 'bool', 'truncated': 'bool', 'items': [_DIALOG, 64], 'covered_class_ids': ['u32',8]}
_MOUSE_COMMAND = {'scope': 'mouse_command_scope', 'lock_level': 'i32', 'lock_suppressed': 'bool',
                  'checker_flag90_raw': 'bool', 'physical_excluded': 'bool'}
_MOUSE_BUTTONS = {'scope': 'mouse_button_scope', 'deferred': 'bool'}
_COMMAND_BASE = {'scope': 'command_scope', 'actor_token': 'token', 'uid': 'uid',
                  'checker_token': 'token', 'keyboard_lock_level': 'i32', 'keyboard_suppressed': 'bool'}
_COMMAND_INPUT = {**_COMMAND_BASE,'mouse':_MOUSE_COMMAND}
_UI = {
    "selection_mode_raw": "i32",
    "player_menu": {"visible": "bool", "target_uid": "uid", "action_ids": ["u32", 32]},
    "town_minimap": {"visible": "bool", "enabled": "bool"},
    "dungeon_selector": _SELECTOR,
    "countdown": {"state_raw": "i32"},
    'dialogs': _DIALOGS, 'input_blocked': 'bool', 'command_input': _COMMAND_INPUT, 'mouse_button_state':_MOUSE_BUTTONS,
}
_ROOM = {
    "stage_token": "token", "map_token": "token", "boss_dead": "bool",
    "map_clear_state": "i32",
    "assembly_phase": "i32", "direction_mask": "u32", "living_count": "u32",
    "majority_required": "u32",
    "doors": {direction: _DOOR for direction in ("left", "right", "up", "down")},
}
_CAPABILITIES = {
    "skill_levels": "bool", "skill_cooldown": "bool", "skill_usable": "bool",
}
_BODY = {
    "room": _ROOM, "me": _PLAYER, "players": [_PLAYER, 4],
    "enemies": [_ENEMY, 256], "skills": [_SKILL, 216],
    "capabilities": _CAPABILITIES,
}
_KNOWN = {
    **_BODY, "town": _TOWN, "ui": _UI, "pid": "u32", "process_started_filetime": "uint",
    "sequence": "uint", "captured_filetime": "uint", "captured_tick_ms": "uint",
    "exported_filetime": "uint", "scene": "u32", "generation": "uint",
    "layout_supported": "bool", "valid": "bool", "truncated": "bool",
    "capture_duration_us": "uint",
}


class SnapshotError(ValueError):
    """The file cannot be used as current, complete client state."""

    def __init__(self, code: str):
        self.code = code
        super().__init__(code)


class UnknownFieldError(LookupError):
    """The snapshot is usable, but this particular field is not known."""

    def __init__(self, path: str, reason: str = "unknown"):
        self.path = path
        self.reason = reason
        super().__init__(f"{path}: {reason}")


def _require(condition, code):
    if not condition:
        raise SnapshotError(code)


def _integer(value, minimum=0, maximum=UINT64_MAX):
    return type(value) is int and minimum <= value <= maximum


def _finite(value):
    if type(value) not in (int, float):
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


def _shape(value, shape, *, nullable=True):
    if value is None:
        _require(nullable, "missing_data")
        return
    if isinstance(shape, dict):
        _require(isinstance(value, dict), "bad_object")
        for key, child in shape.items():
            if (shape is _DIALOGS and key=='covered_class_ids' or shape is _COMMAND_INPUT and key=='mouse') and key not in value:
                continue
            if shape is _DIALOG and key=='placement' and key not in value:
                continue
            if shape is _PLAYER and key == 'mp_source' and key not in value:
                continue  # Additive provenance; legacy local-dungeon MP remains compatible.
            if key not in value and ((shape is _UI and key in ('dialogs', 'input_blocked', 'command_input','mouse_button_state'))
                                    or (shape is _SELECTOR and key in _SELECTOR_EXTRA)):
                continue  # Additive observation fields remain unknown on older v2 producers.
            if shape is _SKILL and key in ("cooldown_blocked",'resources') and key not in value:
                continue  # Additive v2 diagnostic; old snapshots remain unknown.
            if shape is _TOWN and key == "auto_move_active" and key not in value:
                continue  # Additive v2 field; absence remains unknown to get_known_field.
            _require(key in value, "missing_field")
            _shape(value[key], child, nullable=shape not in (_POSITION, _RECT, _POINT))
        return
    if isinstance(shape, list):
        _require(isinstance(value, list) and len(value) <= shape[1], "bad_array")
        for child in value:
            _shape(child, shape[0], nullable=False)
        return
    checks = {
        "bool": lambda: type(value) is bool,
        "command_scope": lambda: value == 'native_command_checker_type0',
        "mouse_command_scope": lambda: value == 'native_command_checker_type1',
        "mouse_button_scope": lambda: value == 'engine_mouse_button_state',
        'resource_scope':lambda: value=='native_manual_precast_resources',
        "uint": lambda: _integer(value),
        "u32": lambda: _integer(value, 0, (1 << 32) - 1),
        "u8": lambda: _integer(value, 0, 255),
        "i32": lambda: _integer(value, -(1 << 31), (1 << 31) - 1),
        "uid": lambda: _integer(value, 1, 65535),
        "hp": lambda: _integer(value, 0, (1 << 31) - 1),
        "duration": lambda: _integer(value, 0, (1 << 31) - 1),
        "number": lambda: _finite(value),
        "token": lambda: isinstance(value, str) and _TOKEN.fullmatch(value) is not None
        and int(value, 16) != 0,
    }
    _require(checks[shape](), "bad_field_type")


def _unique(rows, key):
    known = [row[key] for row in rows if row[key] is not None]
    _require(len(known) == len(set(known)), "duplicate_identity")


def _skill_identity(snapshot):
    me, room = snapshot.get("me"), snapshot.get("room")
    return (isinstance(me, dict) and isinstance(room, dict) and _integer(me.get("uid"), 1, 65535)
            and all(isinstance(me.get(key), str) and _TOKEN.fullmatch(me[key]) and int(me[key], 16) != 0
                    for key in ("actor_token", "user_token"))
            and all(isinstance(room.get(key), str) and _TOKEN.fullmatch(room[key]) and int(room[key], 16) != 0
                    for key in ("stage_token", "map_token")))


def _validate(snapshot):
    _require(isinstance(snapshot, dict), "bad_root")
    _require(snapshot.get("schema") in (SCHEMA, SCHEMA_V2), "unsupported_schema")
    version2 = snapshot["schema"] == SCHEMA_V2
    for key in ("pid", "process_started_filetime", "sequence", "captured_filetime",
                "captured_tick_ms", "exported_filetime", "generation"):
        _require(key in snapshot and _integer(snapshot[key]), "bad_header")
    _require(0 < snapshot["pid"] <= (1 << 32) - 1
             and snapshot["process_started_filetime"] > 0
             and snapshot["sequence"] > 0
             and snapshot["captured_filetime"] > 0
             and snapshot["exported_filetime"] > 0, "bad_header")
    _require(snapshot.get("layout_supported") is True, "unsupported_layout")
    _require(snapshot.get("valid") is True, "invalid_capture")
    _require(type(snapshot.get("truncated")) is bool, "bad_truncated")
    _require(snapshot["truncated"] is False, "truncated")
    _require(type(snapshot.get("scene")) is int and (snapshot["scene"] in (1, 3) or version2 and snapshot["scene"] == 2),
             "unknown_scene")
    for key, shape in _BODY.items():
        _require(key in snapshot, "missing_field")
        _shape(snapshot[key], shape, nullable=key in ("room", "me"))
    mp_owners={}
    for player in snapshot["players"] + ([snapshot["me"]] if snapshot["me"] is not None else []):
        me = snapshot["me"]
        local_mp = (version2 and snapshot["scene"] == 3 and me is not None
                    and all(player[key] == me[key] for key in ("uid", "actor_token", "user_token")))
        source = player.get('mp_source')
        bound_mp = False
        if source is not None:
            is_local = me is not None and all(player[key] == me[key] for key in ('uid','actor_token','user_token'))
            _require(version2 and snapshot['scene'] in (1,3)
                     and all(player[key] is not None for key in ('uid','actor_token','user_token'))
                     and source['actor_token'] is not None and type(source['local']) is bool
                     and source['local'] == is_local
                     and (snapshot['scene'] != 3 or source['actor_token'] == player['actor_token']), 'mp_source_unbound')
            bound_mp = True
            actor=source['actor_token'].lower()
            _require(actor not in mp_owners or mp_owners[actor]==player['uid'], 'duplicate_mp_source')
            mp_owners[actor]=player['uid']
        _require(bound_mp or (local_mp and 'mp_source' not in player)
                 or (player["mp"] is None and player["max_mp"] is None), "unproven_mp")
    room = snapshot["room"]
    if room is not None:
        _require(isinstance(room["doors"], dict)
                 and all(isinstance(room["doors"].get(direction), dict)
                         for direction in ("left", "right", "up", "down")), "bad_doors")
    caps = snapshot["capabilities"]
    _require(all(type(caps[key]) is bool for key in _CAPABILITIES), "bad_capabilities")
    skill_state = version2 and snapshot["scene"] == 3 and caps["skill_levels"] is True
    _require(skill_state or (caps["skill_cooldown"] is False and caps["skill_usable"] is False),
             "unsupported_capability")
    _require(not (caps["skill_cooldown"] or caps["skill_usable"]) or _skill_identity(snapshot), "skill_actor_unbound")
    for skill in snapshot["skills"]:
        _require(_integer(skill["id"], 0, (1 << 32) - 1)
                 and _integer(skill["level"], 0, (1 << 32) - 1), "bad_skill")
        _require((caps["skill_cooldown"] or (skill["cooldown_ms"] is None and skill.get("cooldown_blocked") is None))
                 and (caps["skill_usable"] or skill["usable"] is None), "unproven_skill_state")
        blocked = skill.get("cooldown_blocked")
        _require(blocked is None or skill["cooldown_ms"] is None or blocked == (skill["cooldown_ms"] > 0), "inconsistent_skill_state")
        _require(blocked is not True or skill["usable"] is not True, "inconsistent_skill_state")
        _require(skill["cooldown_ms"] is None or skill["cooldown_ms"] == 0 or skill["usable"] is not True, "inconsistent_skill_state")
        _require(skill["usable"] is not True or (snapshot["me"]["dead"] is False
                 and snapshot["me"]["same_map"] is True and snapshot["me"]["detached"] is False), "skill_actor_unavailable")
        resources=skill.get('resources')
        if resources is not None:
            _require(skill_state and caps['skill_usable'] and _skill_identity(snapshot) and skill['level']>0,
                     'unproven_skill_resources')
            _require(resources.get('scope')=='native_manual_precast_resources','bad_resource_scope')
            limit,count=resources['use_limit'],resources['use_count']
            _require(count is None or limit is not None and limit>0,'unbound_skill_use_count')
            if skill['usable'] is True:
                cost=resources['mp_cost'];mp=snapshot['me']['mp']
                _require(cost is not None and mp is not None and mp>=cost and limit is not None
                         and (limit==0 or count is not None and count<limit)
                         and resources['items_sufficient'] is True,'inconsistent_skill_resources')
    _unique(snapshot["players"], "uid")
    _unique(snapshot["players"], "actor_token")
    _unique(snapshot["enemies"], "uid")
    _unique(snapshot["skills"], "id")
    if version2:
        _require("town" in snapshot and "ui" in snapshot, "missing_field")
        _shape(snapshot["town"], _TOWN)
        _shape(snapshot["ui"], _UI, nullable=False)
        for key in ("player_menu", "town_minimap", "dungeon_selector", "countdown"):
            _require(isinstance(snapshot["ui"][key], dict), "bad_ui")
        _require(snapshot["ui"]["dungeon_selector"]["visible"] is None, "unproven_ui_visibility")
        _require(snapshot['ui'].get('input_blocked') is None, 'unproven_global_input_block')
        command=snapshot['ui'].get('command_input')
        if command is not None:
            me=snapshot['me']
            _require(all(command[key] is not None for key in _COMMAND_BASE)
                     and command['keyboard_suppressed'] == (command['keyboard_lock_level'] != 0)
                     and (me is None or command['uid'] == me['uid'])
                     and (snapshot['scene'] != 3 or (me is not None and command['actor_token'] == me['actor_token']))
                     and (me is None or me.get('mp_source') is None
                          or command['actor_token'] == me['mp_source']['actor_token']), 'command_input_unbound')
            mouse=command.get('mouse')
            if mouse is not None:
                _require(all(mouse[key] is not None for key in _MOUSE_COMMAND)
                         and mouse['lock_suppressed']==(mouse['lock_level']!=0)
                         and mouse['physical_excluded']==(mouse['lock_suppressed'] or mouse['checker_flag90_raw']), 'mouse_command_unbound')
        buttons=snapshot['ui'].get('mouse_button_state')
        if buttons is not None:
            _require(all(buttons[key] is not None for key in _MOUSE_BUTTONS),'mouse_button_state_unbound')
        selector = snapshot['ui']['dungeon_selector']
        if any(selector.get(key) is not None for key in _SELECTOR_EXTRA):
            _require(snapshot['scene'] == 2 and selector.get('instance_token') is not None
                     and type(selector.get('transition_pending')) is bool
                     and _integer(selector.get('phase_raw'), 0, 4)
                     and _integer(selector.get('record_count'), 0, 64), 'selector_unbound')
            projected = [selector.get(key) for key in ('selected_dungeon_id', 'difficulty_raw', 'maximum_difficulty_raw')]
            _require(all(item is None for item in projected) or (all(item is not None for item in projected)
                     and _integer(selector.get('selected'), 0, selector['record_count'] - 1)), 'selector_projection_unbound')
        dialogs = snapshot['ui'].get('dialogs')
        if dialogs is not None:
            _require(type(dialogs['valid']) is bool and type(dialogs['truncated']) is bool
                     and isinstance(dialogs['items'], list), 'bad_dialog_inventory')
            coverage=dialogs.get('covered_class_ids')
            if coverage is not None:
                _require(isinstance(coverage,list) and bool(coverage) and len(coverage)==len(set(coverage))
                         and all(c in _DIALOG_CLASSES for c in coverage),'bad_dialog_coverage')
            else:coverage=_DIALOG_LEGACY_CLASSES
            layer_signatures={};layer_groups={};layer_orders=set()
            for dialog in dialogs['items']:
                _require(dialog['class_id'] in coverage
                         and all(dialog[key] is not None for key in _DIALOG_BASE), 'dialog_unbound')
                placement=dialog.get('placement')
                if placement is not None:
                    _require(all(placement[key] is not None for key in _PLACEMENT if key!='order')
                             and (placement['order'] is None if placement['attached'] is False
                                  else _integer(placement['order'],0,127)), 'dialog_placement_unbound')
                    signature=(placement['layer_token'].lower(),placement['layer_shown_raw'],placement['layer_input_enabled_raw'])
                    group=0 if dialog['class_id']<=0xF else 1 if dialog['class_id']<=0x17 else 2
                    _require(group not in layer_signatures or layer_signatures[group]==signature,'inconsistent_dialog_layer')
                    _require(signature[0] not in layer_groups or layer_groups[signature[0]]==group,'aliased_dialog_layer')
                    layer_signatures[group]=signature;layer_groups[signature[0]]=group
                    if placement['attached']:
                        order=(signature[0],placement['order'])
                        _require(order not in layer_orders,'duplicate_dialog_order')
                        layer_orders.add(order)
            _unique(dialogs['items'], 'instance_token')
            _unique(dialogs['items'], 'root_token')
        if snapshot["scene"] == 3:
            _require(snapshot["town"] is None, "stale_dungeon_town")
        elif snapshot["scene"] == 1:
            town, me = snapshot["town"], snapshot["me"]
            _require(town is not None and me is not None, "town_unbound")
            _require(_integer(town["town_id"], 0, (1 << 31) - 1)
                     and _integer(town["area_id"], 0, (1 << 31) - 1)
                     and town["context_token"] is not None and town["map_token"] is not None,
                     "town_unbound")
            _require(all(me[key] is not None for key in ("uid", "user_token", "actor_token", "map_token"))
                     and me["map_token"] == town["map_token"]
                     and me["same_map"] is True and me["detached"] is False
                     and me["position"] == town["position"], "town_binding_mismatch")
            _require(snapshot["room"] is None and not snapshot["enemies"] and not snapshot["skills"]
                     and caps["skill_levels"] is False, "stale_town_payload")
            for player in snapshot["players"] + [me]:
                _require(all(player[key] is None for key in ("hp", "max_hp", "dead")),
                         "unproven_town_combat")
        else:
            _require(snapshot["town"] is None and snapshot["room"] is None and snapshot["me"] is None
                     and not snapshot["players"] and not snapshot["enemies"] and not snapshot["skills"]
                     and not any(caps.values()), "stale_selector_world_payload")
    elif snapshot["scene"] == 1:
        _require(snapshot["room"] is None and snapshot["me"] is None
                 and not snapshot["players"] and not snapshot["enemies"]
                 and not snapshot["skills"], "stale_town_payload")
    errors = snapshot.get("errors")
    _require(isinstance(errors, list) and len(errors) <= 1024
             and all(isinstance(error, str) and len(error) <= 512 for error in errors),
             "bad_errors")
    _require(_integer(snapshot.get("capture_duration_us")), "bad_duration")
    _require("server_context" in snapshot, "missing_field")
    context = snapshot["server_context"]
    _require(context is None or isinstance(context, dict) and context.get("matched") is False,
             "unsafe_server_context")


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        _require(key not in result, "duplicate_json_key")
        result[key] = value
    return result


def _constant(_):
    raise SnapshotError("nonfinite_json")


def load_snapshot(path, *, pid, process_started_filetime, now_tick_ms, max_age_ms=750):
    """Read once; preserve integer identities and reject stale/incomplete data."""
    _require(_integer(pid, 1, (1 << 32) - 1)
             and _integer(process_started_filetime, 1)
             and _integer(now_tick_ms) and _integer(max_age_ms), "bad_expectation")
    try:
        with Path(path).open("rb") as stream:
            data = stream.read(MAX_BYTES + 1)
        _require(len(data) <= MAX_BYTES, "oversized")
        snapshot = json.loads(data.decode("utf-8-sig"), object_pairs_hook=_pairs,
                              parse_constant=_constant)
    except SnapshotError:
        raise
    except (OSError, UnicodeError, ValueError, RecursionError) as error:
        raise SnapshotError("unreadable_json") from error
    _validate(snapshot)
    _require(snapshot["pid"] == pid, "pid_mismatch")
    _require(snapshot["process_started_filetime"] == process_started_filetime,
             "process_start_mismatch")
    captured = snapshot["captured_tick_ms"]
    _require(captured <= now_tick_ms, "future_capture")
    _require(now_tick_ms - captured <= max_age_ms, "stale")
    return snapshot


def get_known_field(snapshot, dotted_path):
    """Return a known scalar, or raise UnknownFieldError; never eval a path."""
    _require(isinstance(snapshot, dict), "bad_root")
    _require(snapshot.get("layout_supported") is True and snapshot.get("valid") is True
             and snapshot.get("truncated") is False
             and type(snapshot.get("scene")) is int and (snapshot["scene"] in (1, 3)
                  or snapshot.get("schema") == SCHEMA_V2 and snapshot["scene"] == 2),
             "incomplete_snapshot")
    if not isinstance(dotted_path, str) or _PATH.fullmatch(dotted_path) is None \
            or any(part.startswith("__") for part in dotted_path.split(".")):
        raise UnknownFieldError(str(dotted_path), "bad_path")
    parts = dotted_path.split(".")
    if parts[0] == "skills":
        caps = snapshot.get("capabilities", {})
        dynamic = any(part in ("cooldown_ms", "cooldown_blocked", "usable",'resources') for part in parts)
        if not caps.get("skill_levels") or (any(part in ("cooldown_ms", "cooldown_blocked") for part in parts) and not caps.get("skill_cooldown")) \
                or (any(part in ('usable','resources') for part in parts) and not caps.get("skill_usable")) \
                or (dynamic and (snapshot.get("schema") != SCHEMA_V2 or snapshot.get("scene") != 3)):
            raise UnknownFieldError(dotted_path, "unsupported")
        if dynamic and not _skill_identity(snapshot):
            raise UnknownFieldError(dotted_path, "skill_actor_unbound")
    if parts[0] in ("town", "ui") and snapshot.get("schema") != SCHEMA_V2:
        raise UnknownFieldError(dotted_path, "unsupported")
    if parts[:3] == ['ui', 'dialogs', 'items']:
        inventory = snapshot.get('ui', {}).get('dialogs') or {}
        if inventory.get('valid') is not True or inventory.get('truncated') is not False:
            raise UnknownFieldError(dotted_path, 'incomplete_dialog_inventory')
    if parts[:2] == ['ui', 'dungeon_selector'] and len(parts) > 2 and parts[2] in _SELECTOR_EXTRA:
        selector = snapshot.get('ui', {}).get('dungeon_selector') or {}
        if snapshot.get('scene') != 2 or selector.get('instance_token') is None:
            raise UnknownFieldError(dotted_path, 'selector_unbound')
    if parts[:2] == ["ui", "player_menu"] and len(parts) > 2 \
            and parts[2] in ("target_uid", "action_ids") \
            and snapshot.get("ui", {}).get("player_menu", {}).get("visible") is not True:
        raise UnknownFieldError(dotted_path, "menu_hidden")
    value, shape = snapshot, _KNOWN
    try:
        for part in parts:
            if isinstance(shape, dict):
                if part not in shape or not isinstance(value, dict):
                    raise KeyError(part)
                shape, value = shape[part], value[part]
            elif isinstance(shape, list):
                if not part.isdecimal() or not isinstance(value, list):
                    raise KeyError(part)
                shape, value = shape[0], value[int(part)]
            else:
                raise KeyError(part)
            if value is None:
                raise UnknownFieldError(dotted_path)
    except (KeyError, IndexError, TypeError) as error:
        raise UnknownFieldError(dotted_path) from error
    if isinstance(value, (dict, list)) or not isinstance(shape, str):
        raise UnknownFieldError(dotted_path, "not_scalar")
    if parts[0] == "room" and parts[1] not in ("stage_token", "map_token"):
        room = snapshot["room"]
        if room["stage_token"] is None or room["map_token"] is None:
            raise UnknownFieldError(dotted_path, "room_unbound")
    if parts[0] in ("me", "players"):
        entity = snapshot["me"] if parts[0] == "me" else snapshot["players"][int(parts[1])]
        member = parts[1] if parts[0] == "me" else parts[2]
        if member not in ("uid", "user_token", "actor_token") \
                and any(entity[key] is None for key in ("uid", "user_token", "actor_token")):
            raise UnknownFieldError(dotted_path, "entity_unbound")
    if parts[0] == "enemies" and snapshot["enemies"][int(parts[1])]["uid"] is None:
        raise UnknownFieldError(dotted_path, "entity_unbound")
    if parts[0] == "town":
        town, me = snapshot.get("town"), snapshot.get("me")
        if snapshot["scene"] != 1 or town is None or me is None \
                or any(me.get(key) is None for key in ("uid", "user_token", "actor_token", "map_token")) \
                or town.get("context_token") is None or town.get("map_token") != me["map_token"] \
                or me.get("same_map") is not True or me.get("detached") is not False:
            raise UnknownFieldError(dotted_path, "town_unbound")
    if parts[0] in ("me", "players") and "position" in parts:
        entity = snapshot["me"] if parts[0] == "me" else snapshot["players"][int(parts[1])]
        if entity["same_map"] is not True or entity["detached"] is not False:
            raise UnknownFieldError(dotted_path, "position_unbound")
    return value


def match_condition(snapshot, dotted_path, expected):
    """Unknown/null never completes a condition, including '=null'."""
    if expected is None or isinstance(expected, (dict, list)):
        return False
    try:
        actual = get_known_field(snapshot, dotted_path)
    except UnknownFieldError:
        return False
    if type(actual) is bool or type(expected) is bool:
        return type(actual) is type(expected) and actual == expected
    if type(actual) in (int, float) and type(expected) in (int, float):
        return _finite(actual) and _finite(expected) and actual == expected
    return type(actual) is type(expected) and actual == expected


def parse_condition(expression):
    path, separator, raw = expression.partition("=")
    if not separator or _PATH.fullmatch(path) is None or not raw:
        raise ValueError("condition must be dotted.path=value")
    try:
        expected = json.loads(raw, parse_constant=_constant)
    except (ValueError, SnapshotError):
        if re.fullmatch(r"[A-Za-z0-9_:/.-]+", raw) is None or raw in ("NaN", "Infinity", "-Infinity"):
            raise ValueError("condition value must be a scalar") from None
        expected = raw
    if isinstance(expected, (dict, list)) or type(expected) is float and not _finite(expected):
        raise ValueError("condition value must be a finite scalar")
    return path, expected


def summarize(snapshot):
    """Small display only; unknown is retained rather than coerced to zero."""
    me = snapshot["me"]
    room = snapshot["room"]
    doors = room["doors"] if room is not None else {}
    result = {
        "pid": snapshot["pid"], "sequence": snapshot["sequence"],
        "generation": snapshot["generation"], "scene": snapshot["scene"],
        "room": None if room is None else {
            "stage_token": room["stage_token"], "map_token": room["map_token"],
            "map_clear_state": room["map_clear_state"]},
        "me": None if me is None else {
            key: me[key] for key in ("uid", "hp", "max_hp", "dead", "detached")},
        "enemies": len(snapshot["enemies"]),
        "doors": {
            "enabled_directions": [key for key, door in doors.items()
                                   if door["enabled"] is True],
            "unknown": [key for key, door in doors.items()
                        if door["enabled"] is None or door["enabled"] is True and door["rect"] is None],
        },
        "skills": {"levels": snapshot["capabilities"]["skill_levels"],
                   "cooldown": snapshot["capabilities"]["skill_cooldown"],
                   "usable": snapshot["capabilities"]["skill_usable"]},
    }
    if snapshot.get("schema") == SCHEMA_V2:
        if result['me'] is not None:
            result['me'].update({key:me.get(key) for key in ('mp','max_mp','mp_source')})
        result['players']=[{key:player.get(key) for key in ('uid','mp','max_mp','mp_source')}
                           for player in snapshot['players']]
        town = snapshot["town"]
        result["town"] = None if town is None else {
            key: town[key] for key in ("town_id", "area_id", "position", "destination", "direction_raw")}
        if town is not None:
            result["town"]["auto_move_active"] = town.get("auto_move_active")
        result["ui"] = snapshot["ui"]
    return result


def _kernel32():
    if os.name != "nt":
        raise SnapshotError("win32_clock_or_identity_unavailable")
    return ctypes.WinDLL("kernel32", use_last_error=True)


def now_tick_ms():
    """GetTickCount64, not Python uptime assumptions or a truncated DWORD."""
    function = _kernel32().GetTickCount64
    function.argtypes = []
    function.restype = ctypes.c_ulonglong
    return int(function())


def get_process_started_filetime(pid):
    """Query creation metadata once with QUERY_LIMITED_INFORMATION, not VM_READ."""
    _require(_integer(pid, 1, (1 << 32) - 1), "bad_expectation")
    kernel = _kernel32()
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    kernel.GetProcessTimes.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.restype = wintypes.BOOL
    handle = kernel.OpenProcess(0x1000, False, pid)
    _require(bool(handle), "process_identity_unavailable")
    try:
        created, exited, system, user = (wintypes.FILETIME() for _ in range(4))
        _require(bool(kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited),
                                            ctypes.byref(system), ctypes.byref(user))),
                 "process_identity_unavailable")
        return (int(created.dwHighDateTime) << 32) | int(created.dwLowDateTime)
    finally:
        kernel.CloseHandle(handle)


class _Observer:
    def __init__(self, uid=None):
        self.uid = uid
        self.previous = None

    def observe(self, snapshot):
        current_uid = snapshot["me"]["uid"] if snapshot["me"] is not None else None
        if self.uid is not None and current_uid is not None:
            _require(current_uid == self.uid, "uid_changed")
        if self.uid is None and current_uid is not None:
            self.uid = current_uid
        comparable = {key: value for key, value in snapshot.items()
                      if key not in ("exported_filetime", "server_context")}
        if self.previous is not None:
            old = self.previous
            _require(snapshot["sequence"] >= old["sequence"], "sequence_regressed")
            if snapshot["sequence"] == old["sequence"]:
                _require(comparable == old, "sequence_rewritten")
            _require(snapshot["generation"] >= old["generation"], "generation_regressed")
            _require(snapshot["captured_tick_ms"] >= old["captured_tick_ms"], "capture_time_regressed")
        self.previous = comparable


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--process-started-filetime", type=int)
    parser.add_argument("--uid", type=int)
    parser.add_argument("--wait", metavar="DOTTED.PATH=VALUE")
    parser.add_argument("--timeout-seconds", type=float, default=10)
    args = parser.parse_args(argv)
    if not _integer(args.pid, 1, (1 << 32) - 1) \
            or args.process_started_filetime is not None and not _integer(args.process_started_filetime, 1) \
            or args.uid is not None and not _integer(args.uid, 1, 65535) \
            or not math.isfinite(args.timeout_seconds) or not 0 <= args.timeout_seconds <= 300:
        parser.error("invalid identity or timeout (0..300 seconds)")
    try:
        condition = parse_condition(args.wait) if args.wait else None
    except ValueError as error:
        parser.error(str(error))
    try:
        started = args.process_started_filetime
        if started is None:
            started = get_process_started_filetime(args.pid)
    except SnapshotError as error:
        print(json.dumps({"ok": False, "status": "invalid", "error": error.code}))
        return 2
    path = args.directory / f"dfdebug-client-{args.pid}.json"
    deadline = time.monotonic() + args.timeout_seconds
    observer = _Observer(args.uid)
    last_error, last_summary = None, None
    while True:
        try:
            snapshot = load_snapshot(path, pid=args.pid, process_started_filetime=started,
                                     now_tick_ms=now_tick_ms())
            if args.uid is not None:
                current_uid = snapshot["me"]["uid"] if snapshot["me"] is not None else None
                _require(current_uid is not None, "uid_unknown")
                _require(current_uid == args.uid, "uid_mismatch")
            observer.observe(snapshot)
            last_summary = summarize(snapshot)
            last_error = None
            if condition is None or match_condition(snapshot, *condition):
                print(json.dumps({"ok": True, "status": "once" if condition is None else "matched",
                                  "summary": last_summary}, separators=(",", ":")))
                return 0
        except SnapshotError as error:
            last_error = error.code
            if condition is None or error.code in (
                    "pid_mismatch", "process_start_mismatch", "uid_mismatch", "uid_changed"):
                print(json.dumps({"ok": False, "status": "invalid", "error": error.code},
                                 separators=(",", ":")))
                return 2
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            result = {"ok": False, "status": "timeout"}
            if last_error is not None:
                result["last_error"] = last_error
            if last_summary is not None:
                result["summary"] = last_summary
            print(json.dumps(result, separators=(",", ":")))
            return 1
        time.sleep(min(0.05, remaining))


if __name__ == "__main__":
    raise SystemExit(main())
