from copy import deepcopy
import struct
import unittest
import zlib
import os
import json
from pathlib import Path
from tools.debug import gameplay_trace as api

def fixture():
    body=struct.pack('<HHH',529,3,2);raw=struct.pack('<BHI',0,6,zlib.crc32(body))+body
    event=dict(serial=1,tick_ms=5000,counter_us=9000,thread=7,context_token=0x100000,
        buffer_token=0x20000,provider_buffer_match=True,length=len(raw),captured_bytes=len(raw),truncated=False,hex=raw.hex())
    return dict(schema='dfl-native-gameplay-trace/v1',pid=42,process_started_filetime=123,
        serial=1,exported_tick_ms=5002,capacity=64,capacity_overwrites=0,lock_drops=0,invalid_records=0,
        events=[event],source='CRC passed; movement apply unknown')

class TraceTests(unittest.TestCase):
    def validate(self,v):return api.validate_trace(v,pid=42,process_started_filetime=123,now_tick_ms=5003)
    def test_valid_native_observation_and_empty_all_dropped_session(self):
        self.assertEqual(self.validate(fixture())['events'][0]['serial'],1)
        v=fixture();v.update(serial=0,events=[],lock_drops=1)
        self.assertEqual(self.validate(v)['lock_drops'],1)
    def test_actual_native_export_clock_boundary(self):
        from tools.acceptance.native_fixtures import fixture_path
        path=fixture_path('gameplay-trace')
        value=json.loads(Path(path).read_bytes())
        validated=api.validate_trace(value,pid=value['pid'],process_started_filetime=value['process_started_filetime'],
            now_tick_ms=value['exported_tick_ms']+1)
        self.assertEqual(validated['events'][0]['tick_ms'],10010)
        self.assertGreaterEqual(validated['exported_tick_ms'],10010)
    def test_movement_gate_is_not_counted_as_another_crc_passed_message(self):
        v=fixture();v['schema']='dfl-native-gameplay-trace/v2';e=v['events'][0]
        e.update(event_kind='gp4_movement_entered',frame_token=0x200000,buffer_token=0,length=0,captured_bytes=0,
            hex='',provider_buffer_match=False,native_gate=dict(actor_token=0x300000,controller_token=0x400000,
                movement_flags=16,object_kind=529,object_uid=0,player_kind=273,player_uid=3,room_x=0,room_y=1))
        summary=api.summarize(v)
        self.assertEqual(summary.get('movement_gate_events'),1)
        self.assertEqual(summary['known_protocols'],{})
        self.assertEqual(summary['crc_unknown'],0)
    def test_wrong_identity_stale_payload_bounds_and_forged_ring_rejected(self):
        for kind in ('pid','start','future','stale','event_clock','serial','hex','length','truncated','overwrite','bool_counter','opcode','extra_event'):
            v=fixture();e=v['events'][0]
            if kind=='pid':v['pid']=43
            if kind=='start':v['process_started_filetime']=124
            if kind=='future':v['exported_tick_ms']=6000
            if kind=='stale':v['exported_tick_ms']=3000
            if kind=='event_clock':e['tick_ms']=5003
            if kind=='serial':e['serial']=2
            if kind=='hex':e['hex']='zz'
            if kind=='length':e['captured_bytes']=3000
            if kind=='truncated':e['truncated']=True
            if kind=='overwrite':v['capacity_overwrites']=1
            if kind=='bool_counter':v['lock_drops']=False
            if kind=='opcode':e['hex']=e['hex'][:2]+'2a00'+e['hex'][6:]
            if kind=='extra_event':v['events'].append(deepcopy(e))
            with self.subTest(kind=kind),self.assertRaises(api.TraceError):self.validate(v)

if __name__=='__main__':unittest.main()
