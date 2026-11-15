"""File protocol tests only; no running game, input or external memory access."""
from contextlib import redirect_stdout
from copy import deepcopy
import importlib
import io
import json
from pathlib import Path
import struct
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from tools.debug.test_client_state import PID, START, NOW, fixture, town_fixture


WIRE = struct.Struct("<4sHHIIQQQQQQIIIIiiii")


def response(request, status="requested"):
    return dict(schema="dfl-client-navigation/v1", pid=request["pid"],
                process_started_filetime=request["process_started_filetime"], request_id=request["request_id"],
                uid=request["uid"], generation=request["generation"],
                actor_token=f"0x{request['actor_token']:08X}", map_token=f"0x{request['map_token']:08X}",
                town_id=request["town_id"], area_id=request["area_id"], source="debug", status=status,
                reason="" if status in ("requested", "arrived") else "blocked", tick_ms=NOW,
                target=dict(x=request["x"], y=request["y"]),
                position=dict(x=request["x"], y=request["y"]) if status == "arrived" else dict(x=10.5, y=20))


class ClientNavigationTests(unittest.TestCase):
    def setUp(self):
        try:
            self.api = importlib.import_module("tools.debug.client_navigation")
        except ModuleNotFoundError:
            self.fail("client_navigation file protocol is not implemented")
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.snapshot = town_fixture()
        self.snapshot_path = self.directory / f"dfdebug-client-{PID}.json"
        self.write_json(self.snapshot_path, self.snapshot)

    def write_json(self, path, value):
        temporary = path.with_suffix(".tmp")
        temporary.write_text(json.dumps(value), encoding="utf-8")
        # Windows readers can briefly hold a handle without FILE_SHARE_DELETE.
        # Match the native exporter's bounded retry instead of losing the writer thread.
        for attempt in range(6):
            try:
                temporary.replace(path)
                return
            except PermissionError:
                if attempt == 5: raise
                time.sleep(.002)

    def request(self, **kwargs):
        return self.api.make_request(self.snapshot, x=kwargs.pop("x", 100), y=kwargs.pop("y", 200),
                                     request_id=kwargs.pop("request_id", START),
                                     issued_tick_ms=kwargs.pop("issued_tick_ms", NOW), **kwargs)

    def test_request_uses_exact_96_byte_native_wire_and_precise_identity(self):
        raw = self.request()
        self.assertEqual(len(raw), 96)
        self.assertEqual(raw, WIRE.pack(b"DLNV", 1, 96, PID, 1, START, START, NOW, NOW-50, 1, 1,
                                       0x1812E468, 0x184AFED8, 0x180A1AA8, 0x18001000, 1, 0, 100, 200))
        parsed = self.api.parse_request(raw)
        self.assertEqual(parsed["process_started_filetime"], START)
        self.assertEqual(parsed["snapshot_sequence"], 1)

    def test_request_requires_valid_fresh_bound_town_state_and_known_position(self):
        for change in (lambda v:v.update(schema="dfl-client-state/v1"),
                       lambda v:v.update(scene=3), lambda v:v.update(valid=False),
                       lambda v:v.update(truncated=True),
                       lambda v:v.update(captured_tick_ms=NOW-751),
                       lambda v:v.update(captured_tick_ms=NOW+1),
                       lambda v:v["me"].update(actor_token=None),
                       lambda v:(v["me"].update(position=None),v["town"].update(position=None))):
            self.snapshot = town_fixture()
            change(self.snapshot)
            with self.subTest(change=change), self.assertRaises(self.api.NavigationError):
                self.request()

    def test_target_and_request_identifiers_reject_bool_overflow_or_zero(self):
        for kwargs in (dict(x=True),dict(y=1.5),dict(x=2**31),dict(y=-(2**31)-1),
                       dict(request_id=0),dict(request_id=True),dict(request_id=2**64),
                       dict(issued_tick_ms=-1)):
            with self.subTest(kwargs=kwargs), self.assertRaises(self.api.NavigationError):
                self.request(**kwargs)

    def test_parse_rejects_malformed_truncated_and_foreign_wire(self):
        valid=self.request()
        for raw in (valid[:-1],valid+b"x",b"FAIL"+valid[4:],valid[:4]+b"\x02\x00"+valid[6:]):
            with self.subTest(raw=raw[:8]),self.assertRaises(self.api.NavigationError):
                self.api.parse_request(raw)

    def test_response_validates_types_binding_status_and_freshness(self):
        request=self.api.parse_request(self.request())
        value=response(request)
        self.assertEqual(self.api.validate_response(value,request,NOW)["status"],"requested")
        for change in (lambda v:v.update(status="success"),lambda v:v.update(pid=PID+1),
                       lambda v:v.update(process_started_filetime=START+1),
                       lambda v:v.update(uid=2),lambda v:v.update(actor_token="0x00000000"),
                       lambda v:v.update(tick_ms=NOW+1),lambda v:v.update(tick_ms=NOW-1001),
                       lambda v:v["target"].update(x=101),lambda v:v.update(generation=2),
                       lambda v:v["position"].update(x=float("nan"))):
            value=response(request);change(value)
            with self.subTest(change=change),self.assertRaises(self.api.NavigationError):
                self.api.validate_response(value,request,NOW)

    def invoke(self,*extra):
        output=io.StringIO()
        args=["--directory",str(self.directory),"--pid",str(PID),"--uid","1",
              "--process-started-filetime",str(START),"--x","100","--y","200",
              "--timeout-seconds","0.35",*extra]
        with redirect_stdout(output),patch.object(self.api.state,"now_tick_ms",return_value=NOW), \
             patch.object(self.api.state,"get_process_started_filetime",return_value=START):
            code=self.api.main(args)
        lines=output.getvalue().splitlines()
        self.assertEqual(len(lines),1,"only the final result may be printed")
        return code,json.loads(lines[0])

    def wait_for_command(self):
        path = self.directory / f"dfdebug-navigation-{PID}.bin"
        deadline = time.monotonic() + 1
        while True:
            try:
                return self.api.parse_request(path.read_bytes())
            except (FileNotFoundError, PermissionError):
                if time.monotonic() >= deadline:
                    raise
                time.sleep(.005)

    def native_reply(self,status="requested",*,change_snapshot=None,change_response=None,delay=.025):
        def writer():
            request=self.wait_for_command();time.sleep(delay)
            if change_snapshot:
                value=deepcopy(self.snapshot);value.update(sequence=2,captured_tick_ms=NOW)
                change_snapshot(value);self.write_json(self.snapshot_path,value)
            value=response(request,status)
            if change_response:change_response(value)
            self.write_json(self.directory/f"dfdebug-navigation-{PID}.json",value)
        thread=threading.Thread(target=writer);thread.start();self.addCleanup(thread.join)

    def test_accepted_is_explicit_and_does_not_claim_arrival(self):
        self.native_reply()
        code,result=self.invoke("--wait","accepted")
        self.assertEqual(code,0);self.assertEqual(result["status"],"requested")
        self.assertTrue(result["accepted"]);self.assertFalse(result["arrived"])

    def test_default_wait_requires_arrived_response_and_observed_stationary_position(self):
        def arrived(v):
            v["me"]["position"].update(x=100,y=200)
            v["town"].update(position=deepcopy(v["me"]["position"]),destination=dict(x=100,y=200),direction_raw=dict(x=4,y=4))
        self.native_reply("arrived",change_snapshot=arrived)
        code,result=self.invoke()
        self.assertEqual(code,0);self.assertEqual(result["status"],"arrived")
        self.assertTrue(result["arrived"])

    def test_acceptance_alone_times_out_when_waiting_for_arrival(self):
        self.native_reply()
        code,result=self.invoke("--timeout-seconds","0.08")
        self.assertEqual(code,1);self.assertEqual(result["status"],"timeout")
        self.assertTrue(result["accepted"]);self.assertFalse(result["arrived"])

    def test_arrival_receipt_without_actual_destination_does_not_pass(self):
        self.native_reply("arrived")
        code,result=self.invoke("--timeout-seconds","0.08")
        self.assertEqual(code,1);self.assertFalse(result["arrived"])

    def test_changed_character_or_area_interrupts_instead_of_accepting_receipt(self):
        self.native_reply(change_snapshot=lambda v:v["town"].update(area_id=1))
        code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"context_changed")

    def test_matching_rejection_and_interruption_never_report_success(self):
        for status in ("rejected","interrupted"):
            with self.subTest(status=status):
                self.native_reply(status)
                code,result=self.invoke("--wait","accepted")
                self.assertEqual(code,2);self.assertFalse(result["ok"])
                self.assertEqual(result["status"],status)
                (self.directory/f"dfdebug-navigation-{PID}.bin").unlink()

    def test_newer_response_identifies_supersession(self):
        self.native_reply(change_response=lambda v:v.update(request_id=v["request_id"]+1))
        code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"superseded")

    def test_existing_controller_lock_is_preserved_and_no_command_is_written(self):
        lock=self.directory/f"dfdebug-navigation-{PID}.lock";lock.write_bytes(b"other-controller")
        code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"controller_busy")
        self.assertEqual(lock.read_bytes(),b"other-controller")
        self.assertFalse((self.directory/f"dfdebug-navigation-{PID}.bin").exists())

    def test_wrong_uid_stale_snapshot_or_process_identity_never_writes_command(self):
        with patch.object(self.api.state,"get_process_started_filetime",return_value=START+1):
            with self.assertRaises(self.api.NavigationError):
                self.api.execute(self.directory,pid=PID,uid=1,started=START,x=1,y=1,timeout_seconds=0)
        code,result=self.invoke("--uid","2")
        self.assertEqual(code,2);self.assertEqual(result["error"],"uid_mismatch")
        self.assertFalse((self.directory/f"dfdebug-navigation-{PID}.bin").exists())

    def test_explicit_old_request_id_cannot_overwrite_newer_command(self):
        path=self.directory/f"dfdebug-navigation-{PID}.bin";raw=self.request(request_id=START)
        path.write_bytes(raw)
        code,result=self.invoke("--request-id",str(START))
        self.assertEqual(code,2);self.assertEqual(result["error"],"request_id_not_increasing")
        self.assertEqual(path.read_bytes(),raw)

    def test_atomic_publish_leaves_only_complete_request_and_removes_owned_lock(self):
        self.native_reply()
        code,_=self.invoke("--wait","accepted")
        self.assertEqual(code,0)
        self.assertEqual(len((self.directory/f"dfdebug-navigation-{PID}.bin").read_bytes()),96)
        self.assertFalse(list(self.directory.glob("*.lock")))
        self.assertFalse(list(self.directory.glob("*.bin.*.tmp")))

    def test_response_reader_rejects_duplicate_keys_or_oversized_files(self):
        path=self.directory/"invalid-response.json"
        for raw in (b'{"pid":1,"pid":2}',b" "*(16_384+1),b'{"position":{"x":NaN}}'):
            path.write_bytes(raw)
            with self.assertRaises(self.api.NavigationError):self.api._read_json(path)

    def test_lock_replaced_by_another_owner_is_never_removed_by_cleanup(self):
        path=self.directory/"owner.lock"
        with self.api._controller_lock(path):
            path.unlink();path.write_bytes(b"replacement-owner")
        self.assertEqual(path.read_bytes(),b"replacement-owner")

    def accepted_then(self, *, destination_changed=False, minimap_result=False, ack_tick=NOW-10):
        def writer():
            request=self.wait_for_command()
            result=self.directory/f"dfdebug-navigation-{PID}.json"
            value=response(request)
            value["tick_ms"]=ack_tick
            self.write_json(result,value)
            time.sleep(.075) # Allow the consumer to observe the setter acknowledgement.
            snapshot=deepcopy(self.snapshot);snapshot.update(sequence=2,captured_tick_ms=NOW)
            if destination_changed:
                snapshot["town"]["destination"]=dict(x=150,y=200)
            else:
                snapshot["me"]["position"].update(x=100,y=200)
                snapshot["town"].update(position=deepcopy(snapshot["me"]["position"]),
                                         destination=dict(x=100,y=200),direction_raw=dict(x=4,y=4))
            self.write_json(self.snapshot_path,snapshot)
            if minimap_result:
                value=response(request);value.update(source="minimap",request_id=1)
                value["target"]=dict(x=150,y=200)
                self.write_json(result,value)
            elif not destination_changed:
                self.write_json(result,response(request,"arrived"))
        thread=threading.Thread(target=writer);thread.start();self.addCleanup(thread.join)

    def test_post_ack_destination_change_interrupts_without_sending_cancel(self):
        self.accepted_then(destination_changed=True)
        code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["status"],"interrupted")
        self.assertEqual(result["reason"],"destination_changed")
        self.assertTrue(result["accepted"]);self.assertFalse(result["arrived"])
        request=self.api.parse_request((self.directory/f"dfdebug-navigation-{PID}.bin").read_bytes())
        self.assertEqual((request["x"],request["y"]),(100,200))

    def test_before_ack_snapshot_with_old_destination_cannot_falsely_interrupt(self):
        self.snapshot["town"]["destination"]=dict(x=999,y=999)
        self.write_json(self.snapshot_path,self.snapshot)
        self.accepted_then(ack_tick=self.snapshot["captured_tick_ms"])
        code,result=self.invoke()
        self.assertEqual(code,0);self.assertEqual(result["status"],"arrived")

    def test_minimap_result_overwrite_and_post_ack_target_change_interrupts(self):
        self.accepted_then(destination_changed=True,minimap_result=True)
        code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["status"],"interrupted")
        self.assertEqual(result["reason"],"destination_changed")

    def assert_command_read_denial_recovers(self, scenario):
        original = Path.read_bytes
        command = self.directory / f"dfdebug-navigation-{PID}.bin"
        denied = []
        def read(path):
            if path == command and not denied:
                denied.append(path)
                raise PermissionError("transient Windows command sharing violation")
            return original(path)
        with patch.object(Path, "read_bytes", read):
            scenario()
        self.assertEqual(denied, [command])

    def test_native_reply_recovers_transient_command_open_denial(self):
        self.assert_command_read_denial_recovers(
            self.test_accepted_is_explicit_and_does_not_claim_arrival)

    def test_post_ack_reply_recovers_transient_command_open_denial(self):
        self.assert_command_read_denial_recovers(
            self.test_minimap_result_overwrite_and_post_ack_target_change_interrupts)

    def test_transient_snapshot_read_failure_retries_with_fresh_identity(self):
        original=self.api.state.load_snapshot
        attempts=[0]
        def read(*args,**kwargs):
            attempts[0]+=1
            if attempts[0]<=2:raise self.api.state.SnapshotError("unreadable_json")
            return original(*args,**kwargs)
        self.native_reply()
        with patch.object(self.api.state,"load_snapshot",side_effect=read):
            code,result=self.invoke("--wait","accepted")
        self.assertEqual(code,0);self.assertTrue(result["accepted"])
        self.assertGreaterEqual(attempts[0],3)

    def test_persistent_snapshot_read_failure_is_bounded_and_does_not_publish(self):
        with patch.object(self.api.state,"load_snapshot",side_effect=self.api.state.SnapshotError("unreadable_json")) as read:
            code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"unreadable_json")
        self.assertGreater(read.call_count,1);self.assertLessEqual(read.call_count,6)
        self.assertFalse((self.directory/f"dfdebug-navigation-{PID}.bin").exists())

    def test_read_replace_race_after_command_publish_recovers_without_reissuing(self):
        original=self.api.state.load_snapshot
        attempts=[0]
        def read(*args,**kwargs):
            attempts[0]+=1
            if attempts[0] in (3,4):raise self.api.state.SnapshotError("unreadable_json")
            return original(*args,**kwargs)
        self.native_reply()
        with patch.object(self.api.state,"load_snapshot",side_effect=read):
            code,result=self.invoke("--wait","accepted")
        self.assertEqual(code,0);self.assertTrue(result["accepted"])
        request=self.api.parse_request((self.directory/f"dfdebug-navigation-{PID}.bin").read_bytes())
        self.assertEqual(request["request_id"],result["request_id"])

    def test_non_read_snapshot_failure_is_never_retried(self):
        with patch.object(self.api.state,"load_snapshot",side_effect=self.api.state.SnapshotError("stale")) as read:
            code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"stale")
        self.assertEqual(read.call_count,1)

    def test_identity_change_during_read_retry_stops_before_a_second_read(self):
        with patch.object(self.api.state,"now_tick_ms",return_value=NOW), \
             patch.object(self.api.state,"get_process_started_filetime",side_effect=[START,START+1]), \
             patch.object(self.api.state,"load_snapshot",side_effect=self.api.state.SnapshotError("unreadable_json")) as read:
            with self.assertRaises(self.api.NavigationError) as error:
                self.api.execute(self.directory,pid=PID,uid=1,started=START,x=100,y=200,timeout_seconds=0)
        self.assertEqual(error.exception.code,"process_start_mismatch")
        self.assertEqual(read.call_count,1)

    def test_transient_result_replace_failure_retries_without_reissuing_command(self):
        original=self.api._read_json;attempts=[0]
        def read(path):
            attempts[0]+=1
            if attempts[0]<=2:raise self.api.NavigationError("unreadable_response")
            return original(path)
        self.native_reply()
        with patch.object(self.api,"_read_json",side_effect=read):
            code,result=self.invoke("--wait","accepted")
        self.assertEqual(code,0);self.assertTrue(result["accepted"])
        self.assertGreaterEqual(attempts[0],3)

    def test_persistent_result_read_failure_is_bounded_and_does_not_reissue(self):
        self.native_reply()
        with patch.object(self.api,"_read_json",side_effect=self.api.NavigationError("unreadable_response")) as read:
            code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"unreadable_response")
        self.assertGreater(read.call_count,1);self.assertLessEqual(read.call_count,6)
        self.assertEqual(len((self.directory/f"dfdebug-navigation-{PID}.bin").read_bytes()),96)

    def test_non_read_result_failure_is_not_retried(self):
        self.native_reply()
        with patch.object(self.api,"_read_json",side_effect=self.api.NavigationError("bad_response")) as read:
            code,result=self.invoke()
        self.assertEqual(code,2);self.assertEqual(result["error"],"bad_response")
        self.assertEqual(read.call_count,1)


if __name__=="__main__":unittest.main()
