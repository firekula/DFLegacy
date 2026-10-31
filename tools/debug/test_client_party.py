import copy
import json
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from tools.debug import client_party as party


def status():
    return dict(schema=party.STATE_SCHEMA, pid=7, uid=2, process_started_filetime=999,
                tick_ms=1100, snapshot_tick_ms=1090, snapshot_sequence=20, generation=3,
                actor_token="0x00100000", map_token="0x00200000", user_token="0x00300000",
                context_token="0x00400000", town_id=1, area_id=0, available=True, reason="ready",
                pending=None)


def invitation():
    value = status()
    value["pending"] = dict(window_token="0x00500000", serial=4, requester_uid=1,
                            peer_type=0, remaining_ms=5000, can_reply=True, reason="ready")
    return value


class PartyTests(unittest.TestCase):
    def test_request_and_reply_wire_roundtrip(self):
        for operation, value in (("request", status()), ("accept", invitation()), ("reject", invitation())):
            raw = party.make_request(value, operation=operation, target_uid=1, request_id=40, issued_tick_ms=1110)
            self.assertEqual(len(raw), 108)
            decoded = party.parse_request(raw)
            self.assertEqual(decoded["operation"], party.OPERATIONS[operation])
            self.assertEqual(decoded["target_uid"], 1)
            self.assertEqual(decoded["invitation_serial"], 0 if operation == "request" else 4)

    def test_no_self_invite_and_no_blind_reply(self):
        for value, operation, target, code in ((status(), "request", 2, "invalid_target"),
                                               (status(), "accept", 1, "no_pending_invitation"),
                                               (invitation(), "reject", 3, "invitation_mismatch")):
            with self.assertRaisesRegex(party.PartyError, code):
                party.make_request(value, operation=operation, target_uid=target, request_id=40, issued_tick_ms=1110)

    def test_stale_unknown_and_non_party_prompts_never_encode(self):
        cases = []
        old = status(); old["snapshot_tick_ms"] = 100
        cases.append((old, "request", "stale_state"))
        unknown = status(); unknown["available"] = False; unknown["reason"] = "layout_unknown"
        cases.append((unknown, "request", "party_unavailable"))
        for key, replacement in (("peer_type", 1), ("remaining_ms", 0), ("can_reply", False)):
            value = invitation(); value["pending"][key] = replacement
            cases.append((value, "accept", "invitation_unavailable"))
        for value, operation, code in cases:
            with self.subTest(code=code, operation=operation):
                with self.assertRaisesRegex(party.PartyError, code):
                    party.make_request(value, operation=operation, target_uid=1, request_id=40, issued_tick_ms=1110)

    def test_exact_size_header_and_identity_are_required(self):
        raw = party.make_request(status(), operation="request", target_uid=1, request_id=40, issued_tick_ms=1110)
        for invalid in (raw[:-1], raw + b"x", b"XXXX" + raw[4:]):
            with self.assertRaises(party.PartyError): party.parse_request(invalid)
        for key, replacement in (("pid", True), ("actor_token", "0x00000000"), ("uid", 0),
                                 ("generation", 0), ("available", "yes")):
            value = status(); value[key] = replacement
            with self.subTest(key=key):
                with self.assertRaises(party.PartyError): party.validate_state(value, now_tick_ms=1110)

    def test_reply_pins_prompt_and_response_pins_all_context(self):
        request = party.parse_request(party.make_request(invitation(), operation="reject", target_uid=1,
                                                         request_id=40, issued_tick_ms=1110))
        result = dict(schema=party.RESULT_SCHEMA, status="dispatched", reason="native_reject_sent", tick_ms=1120,
                      **{key: value for key, value in request.items() if key not in ("issued_tick_ms", "snapshot_tick_ms")})
        for key in party.TOKENS: result[key] = f"0x{result[key]:08X}"
        self.assertEqual(party.validate_result(result, request, now_tick_ms=1130)["status"], "dispatched")
        for key, replacement in (("request_id", 41), ("invitation_serial", 5), ("operation", 2),
                                 ("user_token", "0x00300004"), ("target_uid", 3), ("area_id", 1)):
            invalid = copy.deepcopy(result); invalid[key] = replacement
            with self.subTest(key=key):
                with self.assertRaisesRegex(party.PartyError, "result_binding_mismatch"):
                    party.validate_result(invalid, request, now_tick_ms=1130)

    def test_bounded_json_rejects_duplicates_and_wrong_process(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "dfdebug-party-7.json"
            path.write_text(json.dumps(status()), encoding="utf-8")
            self.assertEqual(party.read_status(directory, pid=7, uid=2, process_started_filetime=999,
                                               now_tick_ms=1110)["uid"], 2)
            with self.assertRaisesRegex(party.PartyError, "process_mismatch"):
                party.read_status(directory, pid=7, uid=2, process_started_filetime=1000, now_tick_ms=1110)
            path.write_text('{"schema":"x","schema":"y"}', encoding="utf-8")
            with self.assertRaises(party.PartyError): party.read_status(directory, pid=7, now_tick_ms=1110)

    def test_execute_uses_file_api_with_identity_and_releases_controller(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            current = invitation(); current["consumed_request_id"] = 50
            (root / "dfdebug-party-7.json").write_text(json.dumps(current), encoding="utf-8")
            written = []
            def native_file_reply(path, raw):
                written.append(path.name)
                path.write_bytes(raw)
                request = party.parse_request(raw)
                result = {key: request[key] for key in party.MATCH}
                for key in party.TOKENS: result[key] = f"0x{result[key]:08X}"
                result.update(schema=party.RESULT_SCHEMA, status="dispatched", reason="native_reject_sent", tick_ms=1110)
                (root / "dfdebug-party-7.result.json").write_text(json.dumps(result), encoding="utf-8")
            with patch.object(party.state, "now_tick_ms", return_value=1110), patch.object(party.nav, "_atomic_write", native_file_reply):
                result = party.execute(directory, pid=7, operation="reject", uid=2, process_started_filetime=999)
            self.assertEqual(result["target_uid"], 1)
            self.assertEqual(result["request_id"], 51)
            self.assertEqual(written, ["dfdebug-party-7.bin"])
            self.assertFalse((root / "dfdebug-party-7.control.lock").exists())

    def test_busy_or_stale_controller_does_not_write_a_command(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "dfdebug-party-7.json").write_text(json.dumps(status()), encoding="utf-8")
            lock = root / "dfdebug-party-7.control.lock"; lock.write_bytes(b"other controller")
            with self.assertRaisesRegex(party.PartyError, "controller_busy"):
                party.execute(directory, pid=7, operation="request", target_uid=1)
            self.assertEqual(lock.read_bytes(), b"other controller")
            lock.unlink()
            with patch.object(party.state, "now_tick_ms", return_value=2000):
                with self.assertRaisesRegex(party.PartyError, "stale_state"):
                    party.execute(directory, pid=7, operation="request", target_uid=1)
            self.assertFalse((root / "dfdebug-party-7.bin").exists())
            self.assertFalse(lock.exists())

    def test_result_sharing_conflict_retries_read_without_resending(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "dfdebug-party-7.json").write_text(json.dumps(status()), encoding="utf-8")
            attempts, writes = [], []
            original_read = party.nav._read_json
            def read(path):
                if path.name.endswith(".result.json"):
                    attempts.append(path.name)
                    if len(attempts) == 1: raise party.nav.NavigationError("unreadable_response")
                return original_read(path)
            def native_reply(path, raw):
                writes.append(path.name); path.write_bytes(raw)
                request = party.parse_request(raw)
                result = {key: request[key] for key in party.MATCH}
                for key in party.TOKENS: result[key] = f"0x{result[key]:08X}"
                result.update(schema=party.RESULT_SCHEMA, status="dispatched", reason="native_request_sent", tick_ms=1110)
                (root / "dfdebug-party-7.result.json").write_text(json.dumps(result), encoding="utf-8")
            with patch.object(party.state, "now_tick_ms", return_value=1110), patch.object(party.nav, "_read_json", read), patch.object(party.nav, "_atomic_write", native_reply):
                value = party.execute(directory, pid=7, operation="request", target_uid=1)
            self.assertEqual(value["status"], "dispatched")
            self.assertEqual(len(attempts), 2)
            self.assertEqual(len(writes), 1)


if __name__ == "__main__": unittest.main()
