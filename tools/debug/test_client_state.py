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
