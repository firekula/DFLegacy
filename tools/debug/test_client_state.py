"""Consumer contracts use hand-written snapshots, not a running game."""
from contextlib import redirect_stdout
from copy import deepcopy
import importlib
import io
import json
import os
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch


START = 134_060_000_000_000_123
PID = 4567
NOW = 0x1_0000_1000


def fixture(pid=PID, start=START):
    player = dict(
        uid=1, user_token="0x180A1AA8", actor_token="0x1812E468",
        map_token="0x184AFED8", hp=470, max_hp=470, mp=None, max_mp=None, dead=False,
        same_map=True, detached=False, position=dict(x=10.5, y=20, z=0),
    )
    doors = {
        direction: dict(enabled=False, rect=None)
        for direction in ("left", "right", "up", "down")
    }
    doors["right"] = dict(
        enabled=True, rect=dict(left=1100, top=100, width=60, height=100)
    )
    return dict(
        schema="dfl-client-state/v1", pid=pid,
        process_started_filetime=start, sequence=1,
        captured_filetime=START + 100, captured_tick_ms=NOW - 50,
        exported_filetime=START + 110, layout_supported=True, valid=True,
        scene=3, generation=1,
        room=dict(stage_token="0x17B69910", map_token="0x184AFED8",
                  boss_dead=False, map_clear_state=0, assembly_phase=None, direction_mask=2,
                  living_count=None, majority_required=None, doors=doors),
        me=player, players=[deepcopy(player)],
        enemies=[dict(uid=2, monster_id=1077, hp=937, dead=False, active=True,
                      position=dict(x=50, y=30, z=0), action=-1,
                      queued_action=0, authority_token=18000000)],
        skills=[dict(id=1, level=1, cooldown_ms=None, usable=None)],
        capabilities=dict(skill_levels=True, skill_cooldown=False,
                          skill_usable=False),
        server_context=None, truncated=False, errors=[], capture_duration_us=120,
    )


def town_fixture(pid=PID, start=START):
    value = fixture(pid, start)
    value.update(schema="dfl-client-state/v2", scene=1, room=None, enemies=[], skills=[])
    value["capabilities"]["skill_levels"] = False
    value["me"].update(hp=None, max_hp=None, dead=None)
    value["players"] = [deepcopy(value["me"])]
    value["town"] = dict(town_id=1, area_id=0, context_token="0x18001000",
                         map_token=value["me"]["map_token"], position=deepcopy(value["me"]["position"]),
                         destination=dict(x=100, y=200), direction_raw=dict(x=1, y=4))
    value["ui"] = dict(selection_mode_raw=0,
                       player_menu=dict(visible=False, target_uid=None, action_ids=None),
                       town_minimap=dict(visible=True, enabled=True),
                       dungeon_selector=dict(visible=None, active_raw=0, selected=None, hover=None),
                       countdown=dict(state_raw=0))
    return value


def selector_fixture():
    value=fixture();value.update(schema='dfl-client-state/v2',scene=2,town=None,room=None,me=None,players=[],enemies=[],skills=[],ui=town_fixture()['ui'])
    value['capabilities']={key:False for key in value['capabilities']}
    value['ui']['dungeon_selector'].update(active_raw=1,selected=3,hover=4)
    return value


class ClientStateTests(unittest.TestCase):
    def setUp(self):
        try:
            self.api = importlib.import_module("tools.debug.client_state")
        except ModuleNotFoundError:
            self.fail("client_state.load_snapshot is not implemented")
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / f"dfdebug-client-{PID}.json"
        self.value = fixture()
        self.write(self.value)

    def write(self, value, path=None):
        path = self.path if path is None else path
        temporary = path.with_suffix(".tmp")
        temporary.write_text(json.dumps(value), encoding="utf-8")
        temporary.replace(path)

    def load(self, **kwargs):
        return self.api.load_snapshot(
            self.path, pid=kwargs.pop("pid", PID),
            process_started_filetime=kwargs.pop("process_started_filetime", START),
            now_tick_ms=kwargs.pop("now_tick_ms", NOW), **kwargs,
        )

    def invoke(self, *extra):
        output = io.StringIO()
        arguments = [
            "--directory", self.directory.name, "--pid", str(PID),
            "--process-started-filetime", str(START), *extra,
        ]
        with redirect_stdout(output), patch.object(self.api, "now_tick_ms", return_value=NOW):
            status = self.api.main(arguments)
        lines = output.getvalue().splitlines()
        self.assertEqual(len(lines), 1, "polling must not print every frame")
        return status, json.loads(lines[0])

    def later(self, change):
        def update():
            time.sleep(0.025)
            change()
        worker = threading.Thread(target=update)
        worker.start()
        self.addCleanup(worker.join)

    def test_loads_fresh_complete_snapshot_without_identity_precision_loss(self):
        result = self.load()
        self.assertEqual(result["process_started_filetime"], 134_060_000_000_000_123)

        self.assertIs(type(result["process_started_filetime"]), int)
        self.assertEqual(self.api.get_known_field(result, "scene"), 3)
        self.assertEqual(self.api.get_known_field(result, "me.hp"), 470)
        self.assertEqual(self.api.get_known_field(result, "enemies.0.action"), -1)
        self.assertIs(type(self.api.get_known_field(result, "room.map_clear_state")), int)
        self.value["room"]["map_clear_state"] = 1
        self.write(self.value)
        self.assertTrue(self.api.match_condition(self.load(), "room.map_clear_state", 1))
        self.assertFalse(self.api.match_condition(self.load(), "room.map_clear_state", True))

    def test_optional_native_dialog_scope_and_unknown_inventory(self):
        value = town_fixture()
        value['ui']['dialogs'] = dict(valid=True, truncated=False, items=[dict(
            class_id=89, instance_token='0x09A00000', root_token='0x09B00000',
            shown_raw=True, input_enabled_raw=False)])
        value['ui']['input_blocked'] = None
        self.write(value)
        result = self.load()
        self.assertEqual(self.api.get_known_field(result, 'ui.dialogs.items.0.class_id'), 89)
        self.assertFalse(self.api.get_known_field(result, 'ui.dialogs.items.0.input_enabled_raw'))
        for valid, truncated in ((False, False), (True, True)):
            result['ui']['dialogs'].update(valid=valid, truncated=truncated)
            with self.assertRaises(self.api.UnknownFieldError):
                self.api.get_known_field(result, 'ui.dialogs.items.0.class_id')
        for change in ('unknown_class', 'duplicate', 'unbound', 'claim_global_block'):
            bad = deepcopy(value)
            if change == 'unknown_class': bad['ui']['dialogs']['items'][0]['class_id'] = 1
            if change == 'duplicate': bad['ui']['dialogs']['items'] *= 2
            if change == 'unbound': bad['ui']['dialogs']['items'][0]['root_token'] = None
            if change == 'claim_global_block': bad['ui']['input_blocked'] = False
            self.write(bad)
            with self.subTest(change=change), self.assertRaises(self.api.SnapshotError): self.load()

    def test_selector_projection_is_bound_to_scene2_instance_and_index(self):
        value = selector_fixture()
        value['ui']['dungeon_selector'].update(instance_token='0x01090000', transition_pending=False,
            phase_raw=1, stage_token=None, input_suppression_raw=0, record_count=4,
            selected_dungeon_id=31, difficulty_raw=2, maximum_difficulty_raw=3)
        self.write(value)
        self.assertEqual(self.api.get_known_field(self.load(), 'ui.dungeon_selector.selected_dungeon_id'), 31)
        for change in ('scene1', 'unbound', 'index', 'phase'):
            bad = deepcopy(value)
            if change == 'scene1':
                bad = town_fixture(); bad['ui']['dungeon_selector'] = deepcopy(value['ui']['dungeon_selector'])
            if change == 'unbound': bad['ui']['dungeon_selector']['instance_token'] = None
            if change == 'index': bad['ui']['dungeon_selector']['selected'] = -1
            if change == 'phase': bad['ui']['dungeon_selector']['phase_raw'] = 5
            self.write(bad)
            with self.subTest(change=change), self.assertRaises(self.api.SnapshotError): self.load()

    def test_v2_town_preserves_unknown_combat_and_exposes_bound_navigation(self):
        self.write(town_fixture())
        result = self.load()
        self.assertEqual(self.api.get_known_field(result, "town.town_id"), 1)
        self.assertEqual(self.api.get_known_field(result, "town.position.x"), 10.5)
        self.assertEqual(self.api.get_known_field(result, "town.destination.x"), 100)
        self.assertEqual(self.api.get_known_field(result, "town.direction_raw.y"), 4)
        self.assertEqual(self.api.get_known_field(result, "ui.countdown.state_raw"), 0)
        self.assertTrue(self.api.match_condition(result, "ui.town_minimap.visible", True))
        for path in ("me.hp", "me.dead", "me.mp", "ui.player_menu.target_uid"):
            with self.subTest(path=path), self.assertRaises(self.api.UnknownFieldError):
                self.api.get_known_field(result, path)

    def test_v2_town_rejects_stale_combat_and_mismatched_native_binding(self):
        for change in (
                lambda v: v["me"].update(hp=0),
                lambda v: v["players"][0].update(dead=False),
                lambda v: v["town"].update(map_token="0x00001000"),
                lambda v: v["me"].update(uid=None),
                lambda v: v["me"].update(same_map=False),
                lambda v: v["me"].update(detached=True),
                lambda v: v["town"]["position"].update(x=999),
                lambda v: v.update(room=fixture()["room"]),
                lambda v: v.update(enemies=fixture()["enemies"]),
                lambda v: v.update(skills=fixture()["skills"])):
            value = town_fixture()
            change(value)
            self.write(value)
            with self.subTest(change=change), self.assertRaises(self.api.SnapshotError):
                self.load()

    def test_v2_dungeon_keeps_dungeon_semantics_and_rejects_stale_town(self):
        value = fixture()
        value.update(schema="dfl-client-state/v2", town=None, ui=town_fixture()["ui"])
        self.write(value)
        self.assertEqual(self.api.get_known_field(self.load(), "me.hp"), 470)
        value["town"] = town_fixture()["town"]
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):
            self.load()

    def test_v2_selector_scene_is_observation_only_with_empty_world_payload(self):
        value=selector_fixture();self.write(value);result=self.load()
        self.assertEqual(self.api.get_known_field(result,'scene'),2)
        self.assertEqual(self.api.get_known_field(result,'ui.dungeon_selector.selected'),3)
        self.assertIsNone(self.api.summarize(result)['me'])
        for change in [dict(me=fixture()['me']),dict(room=fixture()['room']),dict(town=town_fixture()['town']),dict(players=fixture()['players']),dict(skills=fixture()['skills'])]:
            with self.subTest(change=list(change)):
                bad=deepcopy(value);bad.update(change);self.write(bad)
                with self.assertRaises(self.api.SnapshotError):self.load()
        value['capabilities']['skill_levels']=True;self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_v2_selector_scene_wait_succeeds_without_claiming_player_identity(self):
        self.write(selector_fixture());status,result=self.invoke('--wait','scene=2','--timeout-seconds','0')
        self.assertEqual(status,0);self.assertEqual(result['status'],'matched')

    def test_v2_ui_unknown_stays_unknown_and_selector_flag_is_not_visibility(self):
        value = town_fixture()
        value["ui"]["countdown"]["state_raw"] = None
        value["ui"]["dungeon_selector"].update(active_raw=1, selected=1)
        self.write(value)
        result = self.load()
        self.assertEqual(self.api.get_known_field(result, "ui.dungeon_selector.active_raw"), 1)
        self.assertFalse(self.api.match_condition(result, "ui.dungeon_selector.visible", True))
        self.assertFalse(self.api.match_condition(result, "ui.countdown.state_raw", 0))

    def test_v2_rejects_malformed_or_unbounded_ui_and_town_data(self):
        for change in (
                lambda v: v.pop("ui"),
                lambda v: v.update(ui=None),
                lambda v: v["town"]["destination"].update(x=True),
                lambda v: v["town"]["position"].update(y=None),
                lambda v: v["ui"]["player_menu"].update(target_uid=0),
                lambda v: v["ui"]["player_menu"].update(action_ids=list(range(257))),
                lambda v: v["ui"]["dungeon_selector"].update(visible=True)):
            value = town_fixture()
            change(value)
            self.write(value)
            with self.subTest(change=change), self.assertRaises(self.api.SnapshotError):
                self.load()

    def test_v1_town_contract_remains_empty_and_unsupported_schema_stays_rejected(self):
        value = town_fixture()
        value["schema"] = "dfl-client-state/v1"
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):
            self.load()
        value["schema"] = "dfl-client-state/v3"
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):
            self.load()

    def test_v2_town_summary_includes_compact_destination_and_ui(self):
        self.write(town_fixture())
        summary = self.api.summarize(self.load())
        self.assertEqual(summary["town"]["destination"], dict(x=100, y=200))
        self.assertEqual(summary["ui"]["town_minimap"]["visible"], True)
        self.assertIsNone(summary["me"]["hp"])
        self.assertLess(len(json.dumps(summary)), 1600)

    def test_v2_hidden_player_menu_does_not_make_target_or_actions_operable(self):
        value = town_fixture()
        value["ui"]["player_menu"].update(visible=False, target_uid=2, action_ids=[1])
        self.write(value)
        result = self.load()
        self.assertFalse(self.api.match_condition(result, "ui.player_menu.target_uid", 2))
        self.assertFalse(self.api.match_condition(result, "ui.player_menu.action_ids.0", 1))
        value["ui"]["player_menu"]["visible"] = True
        self.write(value)
        self.assertTrue(self.api.match_condition(self.load(), "ui.player_menu.target_uid", 2))

    def test_v2_town_null_position_is_unknown_and_ui_byte_limits_are_checked(self):
        value = town_fixture()
        value["town"]["position"] = None
        value["me"]["position"] = None
        value["players"][0]["position"] = None
        self.write(value)
        self.assertFalse(self.api.match_condition(self.load(), "town.position.x", 0))
        value["ui"]["dungeon_selector"]["active_raw"] = 256
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_v2_native_auto_move_flag_is_optional_boolean_and_never_an_integer(self):
        value = town_fixture()
        for flag in (True, False, None):
            value["town"]["auto_move_active"] = flag
            self.write(value)
            result = self.load()
            if flag is None:
                self.assertFalse(self.api.match_condition(result,"town.auto_move_active",False))
            else:
                self.assertIs(self.api.get_known_field(result,"town.auto_move_active"),flag)
        value["town"]["auto_move_active"] = 1
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()
        del value["town"]["auto_move_active"]
        self.write(value)
        with self.assertRaises(self.api.UnknownFieldError):
            self.api.get_known_field(self.load(),"town.auto_move_active")

    def test_town_with_null_me_and_room_is_valid_but_fields_are_unknown(self):
        self.value.update(scene=1, room=None, me=None, players=[], enemies=[], skills=[])
        self.write(self.value)
        result = self.load()
        self.assertEqual(self.api.summarize(result)["scene"], 1)
        with self.assertRaises(self.api.UnknownFieldError):
            self.api.get_known_field(result, "me.hp")
        self.assertFalse(self.api.match_condition(result, "me", None))

    def test_identity_layout_validity_and_schema_fail_closed(self):
        cases = [
            ("schema", "other/v1"), ("pid", PID + 1),
            ("process_started_filetime", START + 1),
            ("layout_supported", False), ("valid", False),
            ("scene", 2), ("scene", None), ("truncated", True),
        ]
        for key, bad in cases:
            with self.subTest(key=key, bad=bad):
                value = fixture()
                value[key] = bad
                self.write(value)
                with self.assertRaises(self.api.SnapshotError):
                    self.load()

    def test_boolean_and_string_header_identities_are_not_integers(self):
        for key in ("pid", "process_started_filetime", "sequence",
                    "captured_tick_ms", "generation"):
            for bad in (True, str(self.value[key])):
                with self.subTest(key=key, bad=bad):
                    value = fixture()
                    value[key] = bad
                    self.write(value)
                    with self.assertRaises(self.api.SnapshotError):
                        self.load()

    def test_age_boundary_and_future_capture_use_full_64bit_tick(self):
        self.value["captured_tick_ms"] = NOW - 750
        self.write(self.value)
        self.assertEqual(self.load()["captured_tick_ms"], 0x1_0000_1000 - 750)
        for tick in (NOW - 751, NOW + 1):
            self.value["captured_tick_ms"] = tick
            self.write(self.value)
            with self.assertRaises(self.api.SnapshotError):
                self.load()

    def test_rewriting_old_data_and_utc_jumps_cannot_refresh_capture(self):
        self.value["captured_tick_ms"] = NOW - 751
        for utc in (1, START + 900_000_000_000):
            self.value["captured_filetime"] = utc
            self.value["exported_filetime"] = utc + 1
            self.write(self.value)
            with self.assertRaises(self.api.SnapshotError):
                self.load()

    def test_fresh_capture_survives_utc_clock_reversal(self):
        self.value["exported_filetime"] = 1
        self.write(self.value)
        self.assertEqual(self.load()["me"]["hp"], 470)

    def test_atomic_partial_duplicate_keys_and_nonfinite_json_are_rejected(self):
        samples = [
            '{"schema":"dfl-client-state/v1",',
            json.dumps(self.value).replace('"pid": 4567', '"pid": 1, "pid": 4567'),
            json.dumps(self.value).replace('"x": 10.5', '"x": NaN'),
        ]
        for data in samples:
            with self.subTest(data=data[:30]):
                self.path.write_text(data, encoding="utf-8")
                with self.assertRaises(self.api.SnapshotError):
                    self.load()

    def test_missing_file_and_oversized_snapshot_are_data_errors(self):
        self.path.unlink()
        with self.assertRaises(self.api.SnapshotError):
            self.load()
        self.path.write_bytes(b" " * (1_048_576 + 1))
        with self.assertRaises(self.api.SnapshotError):
            self.load()

    def test_unknown_scalars_do_not_become_false_or_zero(self):
        self.value["me"]["hp"] = None
        self.value["room"]["map_clear_state"] = None
        self.value["room"]["doors"]["up"]["enabled"] = None
        self.write(self.value)
        result = self.load()
        for path in ("me.hp", "room.map_clear_state", "room.assembly_phase", "room.doors.up.enabled",
                     "skills.0.cooldown_ms", "skills.0.usable", "me.profession"):
            with self.subTest(path=path):
                with self.assertRaises(self.api.UnknownFieldError):
                    self.api.get_known_field(result, path)
                self.assertFalse(self.api.match_condition(result, path, None))

    def test_unproven_cooldown_or_usable_cannot_become_supported_in_v1(self):
        for key, bad in (("cooldown_ms", 0), ("usable", True)):
            value = fixture()
            value["skills"][0][key] = bad
            self.write(value)
            with self.assertRaises(self.api.SnapshotError):
                self.load()

    def test_skill_resources_are_optional_bound_scalars_and_cli_conditions(self):
        value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        value['capabilities'].update(skill_cooldown=True,skill_usable=True)
        value['me'].update(mp=10,max_mp=100);value['players']=[deepcopy(value['me'])]
        value['skills']=[dict(id=46,level=1,cooldown_ms=0,cooldown_blocked=False,usable=True,
            resources=dict(scope='native_manual_precast_resources',mp_cost=6,use_limit=2,use_count=1,items_sufficient=True))]
        self.write(value);result=self.load()
        for field,expected in [('mp_cost',6),('use_limit',2),('use_count',1),('items_sufficient',True)]:
            with self.subTest(field=field):
                path='skills.0.resources.'+field
                self.assertEqual(self.api.get_known_field(result,path),expected)
                self.assertTrue(self.api.match_condition(result,path,expected))
        value['skills'][0].pop('resources');self.write(value);result=self.load()
        with self.assertRaises(self.api.UnknownFieldError):self.api.get_known_field(result,'skills.0.resources.mp_cost')

    def test_skill_resources_reject_unproven_scope_domains_and_false_readiness(self):
        base=fixture();base.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        base['capabilities'].update(skill_cooldown=True,skill_usable=True)
        base['me'].update(mp=10,max_mp=100);base['players']=[deepcopy(base['me'])]
        base['skills']=[dict(id=46,level=1,cooldown_ms=0,cooldown_blocked=False,usable=True,
            resources=dict(scope='native_manual_precast_resources',mp_cost=6,use_limit=2,use_count=1,items_sufficient=True))]
        for field,bad in [('scope','global'),('scope',None),('mp_cost',-1),('mp_cost',True),('mp_cost',None),
                          ('use_limit',-1),('use_limit',None),('use_count',-1),('use_count',2),('use_count',None),
                          ('items_sufficient',False),('items_sufficient',None)]:
            with self.subTest(field=field,bad=bad):
                value=deepcopy(base);value['skills'][0]['resources'][field]=bad;self.write(value)
                with self.assertRaises(self.api.SnapshotError):self.load()
        value=deepcopy(base);value['me']['mp']=5;value['players']=[deepcopy(value['me'])];self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()
        value=deepcopy(base);value['capabilities']['skill_usable']=False;value['skills'][0]['usable']=None;self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_partial_skill_resources_remain_useful_without_fabricating_readiness(self):
        value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        value['capabilities'].update(skill_cooldown=True,skill_usable=True)
        value['skills']=[dict(id=46,level=1,cooldown_ms=0,cooldown_blocked=False,usable=None,
            resources=dict(scope='native_manual_precast_resources',mp_cost=None,use_limit=2,use_count=None,items_sufficient=False))]
        self.write(value);result=self.load()
        self.assertEqual(self.api.get_known_field(result,'skills.0.resources.use_limit'),2)
        self.assertIs(self.api.get_known_field(result,'skills.0.resources.items_sufficient'),False)
        for field in ['mp_cost','use_count']:
            with self.assertRaises(self.api.UnknownFieldError):self.api.get_known_field(result,'skills.0.resources.'+field)
        value['skills'][0]['resources'].update(use_limit=0,use_count=1);self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()
        direct=fixture();direct['skills'][0]['resources']=deepcopy(value['skills'][0]['resources'])
        with self.assertRaises(self.api.UnknownFieldError):self.api.get_known_field(direct,'skills.0.resources.use_count')

    def test_v2_skill_state_known_zero_false_and_unknown_are_distinct(self):
        value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        value['capabilities'].update(skill_cooldown=True,skill_usable=True)
        value['skills']=[dict(id=5,level=1,cooldown_ms=1250,cooldown_blocked=True,usable=False),dict(id=46,level=1,cooldown_ms=None,usable=None)]
        self.write(value);result=self.load()
        self.assertEqual(self.api.get_known_field(result,'skills.0.cooldown_ms'),1250)
        self.assertIs(self.api.get_known_field(result,'skills.0.cooldown_blocked'),True)
        self.assertIs(self.api.get_known_field(result,'skills.0.usable'),False)
        self.assertTrue(self.api.match_condition(result,'skills.0.usable',False))
        self.assertFalse(self.api.match_condition(result,'skills.0.usable',0))
        with self.assertRaises(self.api.UnknownFieldError):self.api.get_known_field(result,'skills.1.usable')
        self.assertFalse(self.api.match_condition(result,'skills.1.cooldown_ms',None))
        value['skills'][0].update(cooldown_ms=0,cooldown_blocked=False,usable=True);self.write(value);result=self.load()
        self.assertTrue(self.api.match_condition(result,'skills.0.cooldown_ms',0))
        self.assertIs(self.api.get_known_field(result,'skills.0.usable'),True)
        self.assertTrue(self.api.summarize(result)['skills']['cooldown'])
        self.assertTrue(self.api.summarize(result)['skills']['usable'])

    def test_skill_state_capabilities_are_independent_and_fields_require_support(self):
        value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        value['capabilities']['skill_cooldown']=True;value['skills'][0]['cooldown_ms']=0
        self.write(value);result=self.load();self.assertEqual(self.api.get_known_field(result,'skills.0.cooldown_ms'),0)
        with self.assertRaises(self.api.UnknownFieldError):self.api.get_known_field(result,'skills.0.usable')
        value['skills'][0]['usable']=False;self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_skill_cooldown_rejects_invalid_range_and_town_cannot_claim_support(self):
        for cooldown in [-1,True,2147483648,0.5]:
            with self.subTest(cooldown=cooldown):
                value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
                value['capabilities']['skill_cooldown']=True;value['skills'][0]['cooldown_ms']=cooldown
                self.write(value)
                with self.assertRaises(self.api.SnapshotError):self.load()
        value=town_fixture();value['capabilities']['skill_usable']=True;self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_direct_known_field_query_cannot_enable_v1_skills_by_flipping_capability(self):
        value=fixture();value['capabilities'].update(skill_cooldown=True,skill_usable=True)
        value['skills'][0].update(cooldown_ms=0,usable=True)
        for field in ['cooldown_ms','usable']:
            with self.subTest(field=field),self.assertRaises(self.api.UnknownFieldError):
                self.api.get_known_field(value,'skills.0.'+field)

    def test_skill_indefinite_blocking_is_known_and_inconsistent_states_are_rejected(self):
        value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        value['capabilities'].update(skill_cooldown=True,skill_usable=True)
        value['skills'][0].update(cooldown_ms=None,cooldown_blocked=True,usable=False)
        self.write(value);result=self.load();self.assertIs(self.api.get_known_field(result,'skills.0.cooldown_blocked'),True)
        for change in [dict(cooldown_blocked=1),dict(usable=True),dict(cooldown_ms=0),dict(cooldown_ms=1,cooldown_blocked=False)]:
            with self.subTest(change=change):
                bad=deepcopy(value);bad['skills'][0].update(change);self.write(bad)
                with self.assertRaises(self.api.SnapshotError):self.load()

    def test_skill_readiness_requires_local_identity_and_cannot_ignore_positive_cooldown(self):
        base=fixture();base.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
        base['capabilities'].update(skill_cooldown=True,skill_usable=True);base['skills'][0].update(cooldown_ms=0,usable=True)
        for change in ['positive_cd','no_me','no_actor','dead','detached']:
            with self.subTest(change=change):
                value=deepcopy(base)
                if change=='positive_cd':value['skills'][0]['cooldown_ms']=1250
                elif change=='no_me':value.update(me=None,players=[])
                elif change=='no_actor':value['me']['actor_token']=None
                elif change=='dead':value['me']['dead']=True
                else:value['me'].update(same_map=False,detached=True,map_token=None,position=None)
                self.write(value)
                with self.assertRaises(self.api.SnapshotError):self.load()
        base.update(me=None,players=[])
        with self.assertRaises(self.api.UnknownFieldError):self.api.get_known_field(base,'skills.0.usable')

    def test_v2_local_mp_supports_zero_partial_and_raw_over_max_without_accepting_remote_mp(self):
        for mp,maximum in [(123,150),(0,0),(None,500),(700,500)]:
            with self.subTest(mp=mp,maximum=maximum):
                value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui'])
                value['me'].update(mp=mp,max_mp=maximum);value['players'][0].update(mp=mp,max_mp=maximum)
                self.write(value);result=self.load();self.assertEqual(result['me']['mp'],mp);self.assertEqual(result['me']['max_mp'],maximum)
        value['players'][0].update(uid=2,actor_token='0x20000000',user_token='0x21000000')
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_v2_dialog_layer_placement_remains_separate_from_global_input(self):
        value=town_fixture();value['ui']['dialogs']=dict(valid=True,truncated=False,items=[dict(
            class_id=89,instance_token='0x18006000',root_token='0x18007000',shown_raw=True,input_enabled_raw=True,
            placement=dict(layer_token='0x18008000',attached=True,order=0,layer_shown_raw=True,layer_input_enabled_raw=False))])
        self.write(value);result=self.load()
        self.assertEqual(self.api.get_known_field(result,'ui.dialogs.items.0.placement.order'),0)
        self.assertFalse(self.api.match_condition(result,'ui.input_blocked',True))
        for change in ('detached_order','attached_missing','null_layer','order_bound'):
            bad=deepcopy(value);p=bad['ui']['dialogs']['items'][0]['placement']
            if change=='detached_order':p['attached']=False
            elif change=='attached_missing':p['order']=None
            elif change=='null_layer':p['layer_token']=None
            else:p['order']=128
            self.write(bad)
            with self.subTest(change=change),self.assertRaises(self.api.SnapshotError):self.load()
        value['ui']['dialogs']['items'][0]['placement'].update(attached=False,order=None);self.write(value)
        self.assertFalse(self.api.get_known_field(self.load(),'ui.dialogs.items.0.placement.attached'))

    def test_v2_native_keyboard_gate_is_scoped_and_validated(self):
        value=town_fixture();gate=dict(scope='native_command_checker_type0',actor_token='0x18004000',
            uid=value['me']['uid'],checker_token='0x18005000',keyboard_lock_level=3,keyboard_suppressed=True)
        value['ui']['command_input']=gate;self.write(value)
        result=self.load();self.assertTrue(self.api.get_known_field(result,'ui.command_input.keyboard_suppressed'))
        self.assertFalse(self.api.match_condition(result,'ui.input_blocked',True))
        for change in ('scope','uid','zero','null_token','type'):
            bad=deepcopy(value);g=bad['ui']['command_input']
            if change=='scope':g['scope']='all_input'
            elif change=='uid':g['uid']=2
            elif change=='zero':g['keyboard_lock_level']=0
            elif change=='null_token':g['checker_token']=None
            else:g['keyboard_lock_level']=True
            self.write(bad)
            with self.subTest(change=change),self.assertRaises(self.api.SnapshotError):self.load()
        gate.update(keyboard_lock_level=0,keyboard_suppressed=False);self.write(value)
        status,result=self.invoke('--wait','ui.command_input.keyboard_suppressed=false','--timeout-seconds','0')
        self.assertEqual(status,0);self.assertEqual(result['status'],'matched')

    def test_mouse_command_exclusion_has_its_own_scope_and_native_operands(self):
        value=town_fixture();value['ui']['command_input']=dict(scope='native_command_checker_type0',actor_token='0x18004000',
            uid=1,checker_token='0x18005000',keyboard_lock_level=0,keyboard_suppressed=False,
            mouse=dict(scope='native_command_checker_type1',lock_level=2,lock_suppressed=True,checker_flag90_raw=False,physical_excluded=True))
        self.write(value);s=self.load()
        self.assertTrue(self.api.get_known_field(s,'ui.command_input.mouse.physical_excluded'))
        self.assertFalse(self.api.get_known_field(s,'ui.command_input.keyboard_suppressed'))
        for change in ('scope','lock_boolean','flag_type','wrong_lock_predicate','wrong_exclusion'):
            bad=deepcopy(value);mouse=bad['ui']['command_input']['mouse']
            if change=='scope':mouse['scope']='world_input'
            elif change=='lock_boolean':mouse['lock_level']=True
            elif change=='flag_type':mouse['checker_flag90_raw']=1
            elif change=='wrong_lock_predicate':mouse['lock_suppressed']=False
            else:mouse['physical_excluded']=False
            self.write(bad)
            with self.subTest(change=change),self.assertRaises(self.api.SnapshotError):self.load()
        value['ui']['command_input']['mouse'].update(lock_level=0,lock_suppressed=False,checker_flag90_raw=True)
        self.write(value);self.assertTrue(self.api.get_known_field(self.load(),'ui.command_input.mouse.physical_excluded'))
        value['ui']['command_input']['mouse']=None;self.write(value)
        self.assertFalse(self.api.match_condition(self.load(),'ui.command_input.mouse.physical_excluded',False))
        value['ui']['command_input'].pop('mouse');self.write(value);self.assertFalse(self.api.match_condition(self.load(),'ui.command_input.mouse.physical_excluded',False))

    def test_mouse_button_state_deferral_never_claims_global_blocking(self):
        value=town_fixture();value['ui']['mouse_button_state']=dict(scope='engine_mouse_button_state',deferred=True)
        self.write(value);s=self.load();self.assertTrue(self.api.get_known_field(s,'ui.mouse_button_state.deferred'))
        self.assertFalse(self.api.match_condition(s,'ui.input_blocked',True))
        for change in ('scope','deferred_type','missing'):
            bad=deepcopy(value);m=bad['ui']['mouse_button_state']
            if change=='scope':m['scope']='global_input'
            elif change=='deferred_type':m['deferred']=1
            else:m.pop('deferred')
            self.write(bad)
            with self.subTest(change=change),self.assertRaises(self.api.SnapshotError):self.load()

    def test_eight_dialogs_require_coverage_and_keep_native_layers_separate(self):
        value=town_fixture();low=dict(class_id=7,instance_token='0x18006000',root_token='0x18007000',shown_raw=True,input_enabled_raw=True,
            placement=dict(layer_token='0x18008000',attached=True,order=0,layer_shown_raw=True,layer_input_enabled_raw=True))
        high=deepcopy(low);high.update(class_id=89,instance_token='0x18006100',root_token='0x18007100');high['placement']['layer_token']='0x18009000'
        value['ui']['dialogs']=dict(valid=True,truncated=False,items=[low,high],covered_class_ids=[7,9,35,37,48,86,89,106])
        self.write(value);s=self.load();self.assertEqual(self.api.get_known_field(s,'ui.dialogs.items.0.placement.order'),0)
        self.assertEqual(self.api.get_known_field(s,'ui.dialogs.covered_class_ids.0'),7)
        for change in ('missing_coverage','wrong_coverage','duplicate_coverage','unknown_class','same_layer_token'):
            bad=deepcopy(value);d=bad['ui']['dialogs']
            if change=='missing_coverage':d.pop('covered_class_ids')
            elif change=='wrong_coverage':d['covered_class_ids']=[35,89]
            elif change=='duplicate_coverage':d['covered_class_ids']=[7,7,89]
            elif change=='unknown_class':d['covered_class_ids']=[1,7,89]
            else:d['items'][1]['placement']['layer_token']=d['items'][0]['placement']['layer_token']
            self.write(bad)
            with self.subTest(change=change),self.assertRaises(self.api.SnapshotError):self.load()
        for cls in (7,9,0x56):
            single=deepcopy(value);single['ui']['dialogs']['items']=[deepcopy(low)];single['ui']['dialogs']['items'][0]['class_id']=cls;self.write(single)
            with self.subTest(cls=cls):self.assertEqual(self.api.get_known_field(self.load(),'ui.dialogs.items.0.class_id'),cls)

    def test_dialogs_cannot_claim_conflicting_layer_order_or_flags(self):
        value=town_fixture();one=dict(class_id=89,instance_token='0x18006000',root_token='0x18007000',
            shown_raw=True,input_enabled_raw=True,placement=dict(layer_token='0x18008000',attached=True,order=0,
            layer_shown_raw=True,layer_input_enabled_raw=True))
        two=deepcopy(one);two.update(instance_token='0x18006100',root_token='0x18007100')
        value['ui']['dialogs']=dict(valid=True,truncated=False,items=[one,two])
        for change in ('same_order','flags','different_layer'):
            bad=deepcopy(value);p=bad['ui']['dialogs']['items'][1]['placement']
            if change=='flags':p.update(order=1,layer_shown_raw=False)
            elif change=='different_layer':p.update(order=1,layer_token='0x18009000')
            self.write(bad)
            with self.subTest(change=change),self.assertRaises(self.api.SnapshotError):self.load()

    def test_cli_summary_exposes_mp_and_identified_party_sources(self):
        value=town_fixture();value['me'].update(mp=0,max_mp=200,mp_source=dict(actor_token='0x18005000',local=True))
        value['players']=[deepcopy(value['me'])];self.write(value)
        status,result=self.invoke();self.assertEqual(status,0)
        self.assertEqual(result['summary']['me']['mp'],0)
        self.assertEqual(result['summary']['players'][0]['mp_source'],value['me']['mp_source'])

    def test_v2_town_and_peer_mp_require_explicit_bound_source(self):
        for town in (False, True):
            value = town_fixture()
            if not town:
                value.update(scene=3, town=None, room=fixture()['room'], capabilities=fixture()['capabilities'])
                value['me'].update(hp=470, max_hp=470, dead=False)
            value['me'].update(mp=0,max_mp=200,mp_source=dict(actor_token='0x1800A000' if town else value['me']['actor_token'],local=True))
            peer=deepcopy(value['me']);peer.update(uid=2,user_token='0x18002000',actor_token='0x18003000',mp=81,
                mp_source=dict(actor_token='0x18004000' if town else '0x18003000',local=False))
            value['players']=[deepcopy(value['me']),peer];self.write(value)
            with self.subTest(town=town):
                result=self.load();self.assertEqual(self.api.get_known_field(result,'players.1.mp'),81)
                self.assertEqual(self.api.get_known_field(result,'me.mp'),0)
            for change in ('missing','wrong_scope','null_token','wrong_dungeon_actor'):
                if town and change=='wrong_dungeon_actor':continue
                bad=deepcopy(value);source=bad['players'][1]['mp_source']
                if change=='missing':bad['players'][1].pop('mp_source')
                elif change=='wrong_scope':source['local']=True
                elif change=='null_token':source['actor_token']=None
                else:source['actor_token']='0x18005000'
                self.write(bad)
                with self.subTest(town=town,change=change),self.assertRaises(self.api.SnapshotError):self.load()

    def test_mp_source_cannot_belong_to_two_uids(self):
        value=town_fixture();value['me'].update(mp=1,max_mp=200,mp_source=dict(actor_token='0x18005000',local=True))
        peer=deepcopy(value['me']);peer.update(uid=2,user_token='0x18002000',actor_token='0x18003000')
        peer['mp_source']['local']=False;value['players']=[deepcopy(value['me']),peer];self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_command_requires_dungeon_me_without_raising_type_error(self):
        value=town_fixture();value.update(scene=3,me=None,town=None,players=[],room=fixture()['room'])
        value['ui']['command_input']=dict(scope='native_command_checker_type0',actor_token='0x18004000',uid=1,
            checker_token='0x18005000',keyboard_lock_level=0,keyboard_suppressed=False)
        self.write(value)
        with self.assertRaises(self.api.SnapshotError):self.load()

    def test_v2_local_mp_still_rejects_negative_boolean_and_overflow(self):
        for mp in [-1,True,2147483648]:
            with self.subTest(mp=mp):
                value=fixture();value.update(schema='dfl-client-state/v2',town=None,ui=town_fixture()['ui']);value['me']['mp']=mp
                self.write(value)
                with self.assertRaises(self.api.SnapshotError):self.load()

    def test_mp_fields_are_preserved_unknown_and_nonnull_is_rejected(self):
        result = self.load()
        self.assertIsNone(result["me"]["mp"])
        with self.assertRaises(self.api.UnknownFieldError):
            self.api.get_known_field(result, "me.mp")
        self.value["me"]["mp"] = 1
        self.write(self.value)
        with self.assertRaises(self.api.SnapshotError):
            self.load()

    def test_null_door_records_and_partial_vectors_are_not_complete_snapshots(self):
        for change in (
                lambda value: value["room"].update(doors=None),
                lambda value: value["room"]["doors"].update(right=None),
                lambda value: value["me"]["position"].update(x=None),
                lambda value: value["room"]["doors"]["right"]["rect"].update(width=None)):
            value = fixture()
            change(value)
            self.write(value)
            with self.assertRaises(self.api.SnapshotError):
                self.load()

    def test_unknown_entity_or_room_identity_cannot_satisfy_physical_fields(self):
        self.value["me"].update(uid=None, dead=True)
        self.value["room"]["map_token"] = None
        self.write(self.value)
        result = self.load()
        self.assertFalse(self.api.match_condition(result, "me.dead", True))
        self.assertFalse(self.api.match_condition(result, "room.doors.right.enabled", True))

    def test_field_access_rejects_snapshot_marked_incomplete_after_loading(self):
        result = self.load()
        result["truncated"] = True
        with self.assertRaises(self.api.SnapshotError):
            self.api.get_known_field(result, "me.dead")

    def test_false_does_not_match_zero_and_true_does_not_match_one(self):
        result = self.load()
        self.assertTrue(self.api.match_condition(result, "me.dead", False))
        self.assertFalse(self.api.match_condition(result, "me.dead", 0))
        self.assertFalse(self.api.match_condition(result, "me.hp", False))
        self.assertFalse(self.api.match_condition(result, "room.doors.right.enabled", 1))
        self.assertTrue(self.api.match_condition(result, "me.position.x", 10.5))

    def test_conditions_only_traverse_known_data_without_eval(self):
        result = self.load()
        for path in ("me.__class__", "me.hp()", "__import__('os')", "players.-1.uid"):
            with self.subTest(path=path):
                with self.assertRaises(self.api.UnknownFieldError):
                    self.api.get_known_field(result, path)
        self.assertEqual(self.api.parse_condition("scene=3"), ("scene", 3))
        self.assertEqual(self.api.parse_condition("me.dead=false"), ("me.dead", False))
        with self.assertRaises(ValueError):
            self.api.parse_condition("me.hp>0")

    def test_unmatched_server_context_is_never_an_action_condition(self):
        self.value["server_context"] = dict(matched=False, room=[1, 2])
        self.write(self.value)
        result = self.load()
        with self.assertRaises(self.api.UnknownFieldError):
            self.api.get_known_field(result, "server_context.room.0")

    def test_detached_dead_identity_remains_known_but_position_does_not(self):
        self.value["me"].update(hp=0, dead=True, detached=True, same_map=False,
                                map_token=None, position=None)
        self.value["players"] = [deepcopy(self.value["me"])]
        self.write(self.value)
        result = self.load()
        self.assertTrue(self.api.match_condition(result, "me.dead", True))
        with self.assertRaises(self.api.UnknownFieldError):
            self.api.get_known_field(result, "me.position.x")

    def test_bad_shapes_duplicates_and_scan_limits_are_rejected(self):
        cases = []
        value = fixture()
        value["players"].append(deepcopy(value["players"][0]))
        cases.append(value)
        value = fixture()
        value["players"] *= 5
        cases.append(value)
        value = fixture()
        value["enemies"] *= 257
        cases.append(value)
        value = fixture()
        value["skills"] *= 217
        cases.append(value)
        value = fixture()
        value["room"]["doors"]["right"]["enabled"] = 1
        cases.append(value)
        value = fixture()
        value["room"]["map_clear_state"] = True
        cases.append(value)
        for value in cases:
            with self.subTest(kind=list(value)):
                self.write(value)
                with self.assertRaises(self.api.SnapshotError):
                    self.load()

    def test_four_clients_are_isolated_by_pid_and_start_identity(self):
        for number in range(4):
            pid = PID + number
            start = START + number
            path = Path(self.directory.name) / f"dfdebug-client-{pid}.json"
            self.write(fixture(pid, start), path)
            result = self.api.load_snapshot(path, pid=pid, process_started_filetime=start,
                                            now_tick_ms=NOW)
            self.assertEqual(result["pid"], pid)
            with self.assertRaises(self.api.SnapshotError):
                self.api.load_snapshot(path, pid=PID + 10, process_started_filetime=start,
                                       now_tick_ms=NOW)

    def test_summary_is_small_and_does_not_dump_debug_tables(self):
        summary = self.api.summarize(self.load())
        self.assertEqual(summary["scene"], 3)
        self.assertEqual(summary["me"]["hp"], 470)
        self.assertEqual(summary["enemies"], 1)
        self.assertIn("map_clear_state", summary["room"])
        self.assertEqual(summary["room"]["map_clear_state"], 0)
        self.assertEqual(summary["doors"].get("enabled_directions"), ["right"])
        self.assertNotIn("open", summary["doors"])
        self.assertFalse(summary["skills"]["cooldown"])
        text = json.dumps(summary)
        self.assertLess(len(text), 1200)
        self.assertNotIn("authority_token", text)
        self.assertNotIn("user_token", text)

    def test_cli_once_prints_one_compact_validated_summary(self):
        status, result = self.invoke()
        self.assertEqual(status, 0)
        self.assertTrue(result["ok"])
        self.assertEqual(result["summary"]["pid"], PID)

    def test_cli_expected_uid_rejects_another_character(self):
        status, result = self.invoke("--uid", "2")
        self.assertEqual(status, 2)
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"], "uid_mismatch")

    def test_cli_null_condition_times_out_instead_of_completing(self):
        status, result = self.invoke("--wait", "skills.0.cooldown_ms=null",
                                     "--timeout-seconds", "0")
        self.assertEqual(status, 1)
        self.assertEqual(result["status"], "timeout")

    def test_cli_numeric_scene_condition_is_valid(self):
        status, result = self.invoke("--wait", "scene=3", "--timeout-seconds", "0")
        self.assertEqual(status, 0)
        self.assertEqual(result["status"], "matched")

    def test_wait_accepts_new_generation_and_never_prints_polling_frames(self):
        value = deepcopy(self.value)
        value.update(sequence=2, generation=2, captured_tick_ms=NOW - 1)
        value["room"]["map_token"] = "0x18006640"
        self.later(lambda: self.write(value))
        status, result = self.invoke("--wait", "room.map_token=0x18006640",
                                     "--timeout-seconds", "0.3")
        self.assertEqual(status, 0)
        self.assertEqual(result["summary"]["generation"], 2)

    def test_wait_does_not_accept_same_sequence_with_rewritten_capture_time(self):
        value = deepcopy(self.value)
        value["captured_tick_ms"] = NOW - 1
        value["me"]["dead"] = True
        self.later(lambda: self.write(value))
        status, result = self.invoke("--wait", "me.dead=true", "--timeout-seconds", "0.12")
        self.assertEqual(status, 1)
        self.assertEqual(result["last_error"], "sequence_rewritten")

    def test_wait_stops_on_character_change_instead_of_matching_new_character(self):
        value = deepcopy(self.value)
        value.update(sequence=2, generation=2, captured_tick_ms=NOW - 1)
        value["me"].update(uid=2, dead=True)
        value["players"] = [deepcopy(value["me"])]
        self.later(lambda: self.write(value))
        status, result = self.invoke("--wait", "me.dead=true", "--timeout-seconds", "0.3")
        self.assertEqual(status, 2)
        self.assertEqual(result["error"], "uid_changed")

    def test_wait_retries_partial_atomic_read_but_not_as_known_data(self):
        self.path.write_text('{"schema":', encoding="utf-8")
        self.later(lambda: self.write(self.value))
        status, result = self.invoke("--wait", "scene=3", "--timeout-seconds", "0.3")
        self.assertEqual(status, 0)
        self.assertEqual(result["status"], "matched")

    def test_cli_can_query_start_identity_once_without_memory_reads(self):
        output = io.StringIO()
        arguments = ["--directory", self.directory.name, "--pid", str(PID),
                     "--wait", "skills.0.usable=true", "--timeout-seconds", "0.06"]
        with redirect_stdout(output), patch.object(self.api, "now_tick_ms", return_value=NOW), \
                patch.object(self.api, "get_process_started_filetime",
                             side_effect=[START, AssertionError("repeated identity query")]):
            status = self.api.main(arguments)
        self.assertEqual(status, 1)
        self.assertEqual(json.loads(output.getvalue())["status"], "timeout")

    @unittest.skipUnless(os.name == "nt", "Win32 metadata/time source")
    def test_win32_metadata_and_monotonic_clock_are_public_and_read_only(self):
        start = self.api.get_process_started_filetime(os.getpid())
        first = self.api.now_tick_ms()
        second = self.api.now_tick_ms()
        self.assertIs(type(start), int)
        self.assertGreater(start, 0)
        self.assertIs(type(first), int)
        self.assertGreaterEqual(second, first)


if __name__ == "__main__":
    unittest.main()
