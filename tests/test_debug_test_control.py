"""Offline test-control contracts: no process memory, input or running game."""
from copy import deepcopy
import importlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

from tools.debug.test_client_state import fixture, PID, START, NOW

FT = START + 100
NONCE = 0xFEDCBA9876543210
RUN = 17


def client():
    value = fixture()
    value.update(schema="dfl-client-state/v2", town=None)
    value['ui'] = dict(selection_mode_raw=0,
        player_menu=dict(visible=False,target_uid=None,action_ids=None),
        town_minimap=dict(visible=False,enabled=True),
        dungeon_selector=dict(visible=None,active_raw=0,selected=None,hover=None),
        countdown=dict(state_raw=0))
    return value


def server():
    return dict(schema='dfl-server-test-control/v1', enabled=True,
        server_pid=123,server_started_filetime=START-100,session_nonce=NONCE,captured_filetime=FT,
        instances=[dict(run_id=RUN,room_generation=3,dungeon_id=1,map_id=13,room_x=2,room_y=0,
            members=[dict(uid=1,character_id='11111111-1111-4111-8111-111111111111',party_slot=0)],
            monsters=[dict(uid=2,monster_id=1077,alive=True)],
            ground_items=[dict(ground_id=5,item_id=27546,count=1,is_gold=False,x=100,y=200,pending=False,
                instance_id='22222222-2222-4222-8222-222222222222'),
                dict(ground_id=6,item_id=3030,count=1,is_gold=False,x=110,y=200,pending=False,instance_id=None)],
            inventories=[])],loot_results=[])


class TestControlTests(unittest.TestCase):
    def setUp(self):
        try:self.api=importlib.import_module('tools.debug.test_control')
        except ModuleNotFoundError:self.fail('test-only CLI consumer is not implemented')

    def lease(self,**kwargs):
        values=dict(nonce=NONCE,run=RUN,issued=FT,uid=1,flags=1);values.update(kwargs)
        return struct.pack('<IIQQQII',0x4c545444,1,values['nonce'],values['run'],values['issued'],values['uid'],values['flags'])

    def request(self,**kwargs):
        values=dict(operation=1,target_id=1,x=300,y=240,request_id=START,issued_tick_ms=NOW)
        values.update(kwargs)
        lease=self.api.parse_lease(self.lease(),uid=1,now_filetime=FT)
        return self.api.make_request(client(),lease,**values)

    def test_exact_native_wire_and_full_width_nonce(self):
        raw=self.request()
        self.assertEqual(raw,struct.pack('<4sHHIIQQQQQQIIIIiiiiQQ',b'DLTC',1,112,PID,1,START,START,NOW,NOW-50,
            1,1,0x1812E468,0x184AFED8,0x180A1AA8,1,1,300,240,0,NONCE,RUN))
        self.assertEqual(self.api.parse_request(raw)['session_nonce'],NONCE)

    def test_lease_rejects_disabled_stale_foreign_and_malformed(self):
        for raw in [self.lease(flags=0),self.lease(flags=3),self.lease(uid=2),self.lease(issued=FT-15_000_001),
                    self.lease(issued=FT+1),self.lease(nonce=0),self.lease(run=0),self.lease()[:-1],self.lease()+b'x']:
            with self.subTest(raw=raw),self.assertRaises(self.api.TestControlError):
                self.api.parse_lease(raw,uid=1,now_filetime=FT)

    def test_server_requires_enabled_fresh_exact_schema_and_nonce(self):
        self.assertEqual(self.api.validate_server(server(),now_filetime=FT)['server_pid'],123)
        for change in [lambda s:s.update(enabled=False),lambda s:s.update(enabled=1),
                       lambda s:s.update(schema='other'),lambda s:s.update(captured_filetime=FT-15_000_001),
                       lambda s:s.update(captured_filetime=FT+1),lambda s:s.update(session_nonce=0),
                       lambda s:s.update(server_pid=True),lambda s:s.update(instances='bad')]:
            value=server();change(value)
            with self.subTest(value=value),self.assertRaises(self.api.TestControlError):
                self.api.validate_server(value,now_filetime=FT)
        with self.assertRaises(self.api.TestControlError):
            self.api.validate_server(server(),now_filetime=FT,expected_nonce=NONCE-1)

    def test_unique_member_run_and_party_slots(self):
        self.assertEqual(self.api.select_instance(server(),uid=1)['run_id'],RUN)
        value=server();value['instances'].append(deepcopy(value['instances'][0]))
        with self.assertRaises(self.api.TestControlError):self.api.select_instance(value,uid=1)
        value=server();value['instances'][0]['members'].append(dict(uid=2,character_id='33333333-3333-4333-8333-333333333333',party_slot=0))
        with self.assertRaises(self.api.TestControlError):self.api.validate_server(value,now_filetime=FT)

    def test_request_rejects_invalid_identity_or_expired_snapshot(self):
        for change in [lambda s:s.update(valid=False),lambda s:s.update(truncated=True),
                       lambda s:s.update(captured_tick_ms=NOW-751),lambda s:s.update(scene=1),
                       lambda s:s['me'].update(dead=True),lambda s:s['me'].update(actor_token=None)]:
            value=client();change(value)
            with self.subTest(value=value),self.assertRaises(self.api.TestControlError):
                self.api.make_request(value,self.api.parse_lease(self.lease(),uid=1,now_filetime=FT),
                    operation=1,target_id=1,x=300,y=240,request_id=START,issued_tick_ms=NOW)

    def test_request_rejects_unsafe_targets_and_bool_numbers(self):
        for kwargs in [dict(x=True),dict(y=1.5),dict(x=32768),dict(y=-32769),dict(operation=5),
                       dict(target_id=65536),dict(operation=1,target_id=2),dict(request_id=0),dict(request_id=True)]:
            with self.subTest(kwargs=kwargs),self.assertRaises(self.api.TestControlError):self.request(**kwargs)

    def test_isolation_wire_packs_signed_park_without_changing_target_coordinates(self):
        value=self.api.parse_request(self.request(operation=4,target_id=5,x=10,y=20,park=(-120,250)))
        self.assertEqual((value['x'],value['y']),(10,20))
        self.assertEqual(value['park'],(-120,250))
        with self.assertRaises(self.api.TestControlError):self.request(operation=1,park=(1,2))

    def test_parse_rejects_trailing_bytes_reserved_and_foreign_magic(self):
        good=self.request();bad=bytearray(good);struct.pack_into('<i',bad,92,1)
        for raw in [good[:-1],good+b'0',b'XXXX'+good[4:],bytes(bad)]:
            with self.subTest(raw=raw),self.assertRaises(self.api.TestControlError):self.api.parse_request(raw)

    def test_monster_zero_is_valid_but_ground_zero_is_not(self):
        self.assertEqual(self.api.parse_request(self.request(operation=2,target_id=0))['target_id'],0)
        for op in (3,4):
            with self.assertRaises(self.api.TestControlError):
                self.request(operation=op,target_id=0,**({'park':(50,180)} if op==4 else {}))

    def test_native_partial_is_a_result_and_never_server_commit(self):
        request=self.api.parse_request(self.request())
        value={k:request[k] for k in ('pid','uid','process_started_filetime','request_id','generation','operation','target_id')}
        value.update(schema='dfl-client-test-control/v1',status='partial',tick_ms=NOW,
            server_committed=False,persistence_verified=False,session_nonce=NONCE,run_id=RUN,
            x=300,y=240,actor_token='0x1812E468',map_token='0x184AFED8',user_token='0x180A1AA8')
        self.assertEqual(self.api.native_result(value,request,NOW)['status'],'partial')
        value['status']='committed'
        with self.assertRaises(self.api.TestControlError):self.api.native_result(value,request,NOW)

    def test_native_reply_requires_same_nonce_map_actor_and_coordinates(self):
        request=self.api.parse_request(self.request())
        value={k:request[k] for k in ('pid','uid','process_started_filetime','request_id','generation','operation','target_id',
            'session_nonce','run_id','x','y')}
        value.update(schema='dfl-client-test-control/v1',status='applied',tick_ms=NOW,
            server_committed=False,persistence_verified=False,actor_token='0x1812E468',map_token='0x184AFED8',user_token='0x180A1AA8')
        self.api.native_result(value,request,NOW)
        for key,bad in [('session_nonce',NONCE-1),('run_id',RUN+1),('map_token','0x99999999'),
                        ('actor_token','0x99999999'),('user_token','0x99999999'),('x',301),('uid',True),
                        ('server_committed',True),('persistence_verified',True)]:
            changed={**value,key:bad}
            with self.subTest(key=key),self.assertRaises(self.api.TestControlError):self.api.native_result(changed,request,NOW)

    def test_disabled_server_stops_native_submission_before_any_process_or_file_action(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'dfdebug-test-server.json';data=server();data['enabled']=False;path.write_text(json.dumps(data))
            with patch.object(self.api,'filetime',return_value=FT),patch.object(self.api,'load_clients') as load:
                with self.assertRaisesRegex(self.api.TestControlError,'test_control_disabled'):
                    self.api.execute_native(directory,uid=1,operation=1,target_id=1,x=300,y=240)
                load.assert_not_called()
            self.assertEqual([p.name for p in Path(directory).iterdir()],['dfdebug-test-server.json'])

    def test_gold_ledger_commit_is_valid_without_inventory_slot(self):
        value=dict(run_id=RUN,ground_id=7,event_id=20,status='committed',persisted=True,is_gold=True,
            winner_uid=1,destination_slot=None,item_id=0,count=100,instance_id=None)
        self.assertEqual(self.api.validate_loot_result(value)['count'],100)

    def test_final_commit_can_be_queried_after_room_destroyed_using_explicit_run(self):
        value=server();value['instances']=[]
        value['loot_results']=[dict(run_id=RUN,ground_id=5,event_id=10,status='committed',persisted=True,
            winner_uid=1,destination_slot=20,item_id=27546,count=1,instance_id='22222222-2222-4222-8222-222222222222')]
        with patch.object(self.api,'load_server',return_value=value):
            result=self.api.wait_loot('.',uid=1,ground_id=5,run_id=RUN,timeout=.1)
        self.assertEqual(result['result']['event_id'],10)

    def test_isolation_plan_keeps_every_ground_identity_and_rejects_pending(self):
        room=server()['instances'][0]
        plan=self.api.plan_isolation(room,ground_id=5,at=(300,240),park=(50,180))
        self.assertEqual(plan,[dict(ground_id=5,x=300,y=240),dict(ground_id=6,x=50,y=180)])
        self.assertEqual(room['ground_items'][0]['x'],100)
        room['ground_items'][1]['pending']=True
        with self.assertRaises(self.api.TestControlError):self.api.plan_isolation(room,ground_id=5,at=(300,240),park=(50,180))
        room['ground_items'][1]['pending']=False
        with self.assertRaises(self.api.TestControlError):self.api.plan_isolation(room,ground_id=99,at=(300,240),park=(50,180))
        with self.assertRaises(self.api.TestControlError):self.api.plan_isolation(room,ground_id=5,at=(300,240),park=(310,240))

    def test_loot_result_never_accepts_route_or_native_ack_as_commit(self):
        base=dict(run_id=RUN,ground_id=5,pickup_id='44444444-4444-4444-8444-444444444444',
            item_id=27546,count=1,status='committed',winner_uid=1,destination_slot=12,
            instance_id='22222222-2222-4222-8222-222222222222',persisted=True)
        self.assertTrue(self.api.validate_loot_result(base)['persisted'])
        for change in [lambda v:v.update(status='routed'),lambda v:v.update(status='applied'),
                       lambda v:v.update(persisted=False),lambda v:v.update(winner_uid=None),
                       lambda v:v.update(destination_slot=None),lambda v:v.update(instance_id=None)]:
            value=deepcopy(base);change(value)
            with self.subTest(value=value),self.assertRaises(self.api.TestControlError):self.api.validate_loot_result(value)
        rejected=dict(run_id=RUN,ground_id=5,status='rejected',reason='overweight',persisted=False)
        self.assertEqual(self.api.validate_loot_result(rejected)['status'],'rejected')

    def test_wait_loot_ignores_historical_events_and_preserves_rejection(self):
        rows=[dict(run_id=RUN,ground_id=5,status='rejected',reason='overweight',persisted=False,event_id=1),
              dict(run_id=RUN+1,ground_id=5,status='rejected',reason='overweight',persisted=False,event_id=2)]
        self.assertIsNone(self.api.find_loot_result(rows,run_id=RUN,ground_id=5,after_event_id=1))
        self.assertEqual(self.api.find_loot_result(rows,run_id=RUN,ground_id=5,after_event_id=0)['reason'],'overweight')

    def test_loot_scores_when_present_are_four_native_byte_values(self):
        base=dict(run_id=RUN,ground_id=5,status='rejected',reason='overweight',persisted=False)
        for scores in ['AAAAAA==',[1,2,3],[1,2,3,4,5],[1,2,3,True],[1,2,3,-1],[1,2,3,256]]:
            with self.subTest(scores=scores),self.assertRaises(self.api.TestControlError):
                self.api.validate_loot_result(dict(base,scores=scores))
        self.assertEqual(self.api.validate_loot_result(dict(base,scores=[0,1,100,255]))['scores'],[0,1,100,255])

    def test_peer_sync_requires_all_members_not_just_local_actor(self):
        value=client();value['me']['position']=dict(x=300,y=240,z=0)
        value['players']=[deepcopy(value['me'])]
        self.assertFalse(self.api.position_sync({1:value},members=[1,2],target_uid=1,x=300,y=240)['complete'])
        other=deepcopy(value);other['me']['uid']=2
        other['players'][0]['position']['x']=100
        self.assertFalse(self.api.position_sync({1:value,2:other},members=[1,2],target_uid=1,x=300,y=240)['complete'])
        other['players'][0]['position']['x']=300
        self.assertTrue(self.api.position_sync({1:value,2:other},members=[1,2],target_uid=1,x=300,y=240)['complete'])
        other['scene']=1
        self.assertFalse(self.api.position_sync({1:value,2:other},members=[1,2],target_uid=1,x=300,y=240)['complete'])

    def test_json_reader_rejects_duplicates_nonfinite_and_oversize(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'data.json'
            for payload in [b'{"a":1,"a":2}',b'{"a":NaN}',b'x'*(self.api.MAX_JSON_BYTES+1)]:
                path.write_bytes(payload)
                with self.assertRaises(self.api.TestControlError):self.api.read_json(path)

    def test_json_reader_retries_transient_windows_file_access(self):
        with patch.object(Path,'open',side_effect=[PermissionError(13,'sharing'),io.BytesIO(b'{"ready":true}')]),\
                patch.object(self.api.time,'sleep'):
            self.assertEqual(self.api.read_json('snapshot.json'),dict(ready=True))

    def test_client_query_retries_snapshot_replacement_gap(self):
        path=Path('dfdebug-client-123.json')
        with patch.object(Path,'glob',side_effect=[[],[path]]),\
                patch.object(self.api,'read_json',return_value=dict(pid=PID)),\
                patch.object(self.api.state,'get_process_started_filetime',return_value=START),\
                patch.object(self.api.state,'load_snapshot',return_value=client()),patch.object(self.api.time,'sleep'):
            self.assertEqual(set(self.api.load_clients('.',uids=[1])),{1})

    def test_client_query_retries_second_read_access_failure(self):
        with patch.object(Path,'glob',return_value=[Path('snapshot.json')]),\
                patch.object(self.api,'read_json',return_value=dict(pid=PID)),\
                patch.object(self.api.state,'get_process_started_filetime',return_value=START),\
                patch.object(self.api.state,'load_snapshot',side_effect=[self.api.state.SnapshotError('unreadable_json'),client()]),\
                patch.object(self.api.time,'sleep'):
            self.assertEqual(set(self.api.load_clients('.',uids=[1])),{1})

    def test_client_query_never_merges_disjoint_retry_observations(self):
        second=deepcopy(client());second['me']['uid']=2
        with patch.object(Path,'glob',return_value=[Path('snapshot.json')]),\
                patch.object(self.api,'read_json',return_value=dict(pid=PID)),\
                patch.object(self.api.state,'get_process_started_filetime',return_value=START),\
                patch.object(self.api.state,'load_snapshot',side_effect=[client(),second]*3),\
                patch.object(self.api.time,'sleep'):
            result=self.api.load_clients('.',uids=[1,2])
            self.assertEqual(set(result),{2})

    def test_atomic_publish_retries_same_payload_without_reissuing_command(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'command.bin';path.write_bytes(b'previous')
            original=self.api.os.replace;attempts=[]
            def replace(source,destination):
                attempts.append(Path(source).read_bytes())
                if len(attempts)==1:raise PermissionError(13,'sharing')
                return original(source,destination)
            with patch.object(self.api.os,'replace',side_effect=replace),patch.object(self.api.time,'sleep'):
                self.api.publish(path,b'one request')
            self.assertEqual(attempts,[b'one request',b'one request'])
            self.assertEqual(path.read_bytes(),b'one request')
            self.assertEqual(list(Path(directory).iterdir()),[path])

    def test_atomic_publish_permission_failure_is_bounded_and_cleans_temporary(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'command.bin';path.write_bytes(b'previous')
            with patch.object(self.api.os,'replace',side_effect=PermissionError(13,'sharing')) as replace,\
                    patch.object(self.api.time,'sleep'),self.assertRaises(PermissionError):
                self.api.publish(path,b'new')
            self.assertGreater(replace.call_count,1)
            self.assertLessEqual(replace.call_count,6)
            self.assertEqual(path.read_bytes(),b'previous')
            self.assertEqual(list(Path(directory).iterdir()),[path])

    def test_ground_sync_requires_positions_on_every_replica_and_exact_identity_set(self):
        placements=[dict(ground_id=5,x=300,y=240),dict(ground_id=6,x=50,y=180)]
        ground=[dict(**p,z=0,active=True,pending=False) for p in placements]
        snaps={1:dict(ground_items=deepcopy(ground)),2:dict(ground_items=deepcopy(ground))}
        self.assertTrue(self.api.ground_sync(snaps,members=[1,2],placements=placements)['complete'])
        snaps[2]['ground_items'][0]['x']=100
        self.assertFalse(self.api.ground_sync(snaps,members=[1,2],placements=placements)['complete'])
        snaps[2]['ground_items']=deepcopy(ground)+[dict(ground_id=7,x=300,y=240,z=0,active=True,pending=False)]
        self.assertFalse(self.api.ground_sync(snaps,members=[1,2],placements=placements)['complete'])
        self.assertFalse(self.api.ground_sync({1:snaps[1]},members=[1,2],placements=placements)['complete'])

    def test_native_ground_snapshot_has_process_map_and_freshness_binding(self):
        sdk=client()
        value=dict(schema='dfl-client-test-state/v1',pid=PID,uid=1,process_started_filetime=START,
            generation=1,map_token=sdk['me']['map_token'],captured_tick_ms=NOW,valid=True,truncated=False,
            ground_items=[dict(ground_id=5,x=10,y=20,z=0,active=True,pending=False)])
        self.api.validate_ground_snapshot(value,sdk,now_tick_ms=NOW)
        for change in [lambda v:v.update(valid=False),lambda v:v.update(truncated=True),
                       lambda v:v.update(pid=PID+1),lambda v:v.update(generation=2),
                       lambda v:v.update(map_token='0x00010000'),lambda v:v.update(captured_tick_ms=NOW-751),
                       lambda v:v['ground_items'].append(deepcopy(v['ground_items'][0]))]:
            bad=deepcopy(value);change(bad)
            with self.subTest(bad=bad),self.assertRaises(self.api.TestControlError):
                self.api.validate_ground_snapshot(bad,sdk,now_tick_ms=NOW)

    def test_execute_native_refuses_rebind_to_another_room_before_publishing(self):
        before=server();old=client();after=deepcopy(before);after['instances'][0]['room_generation']+=1
        newer=deepcopy(old);newer['generation']+=1;newer['me']['map_token']='0x19000000'
        with tempfile.TemporaryDirectory() as directory,patch.object(self.api,'load_server',return_value=after),\
                patch.object(self.api,'load_clients',return_value={1:newer}):
            with self.assertRaises(self.api.TestControlError):
                self.api.execute_native(directory,uid=1,operation=2,target_id=2,expected_server=before,
                    expected_room=before['instances'][0],expected_client=old)
            self.assertEqual(list(Path(directory).iterdir()),[])

    def test_empty_enemy_list_after_town_or_epoch_change_is_not_observed_death(self):
        before={1:client()};after={1:deepcopy(before[1])};after[1]['enemies']=[]
        self.assertTrue(self.api.enemy_death_sync(before,after,members=[1],monster_id=2)['complete'])
        for change in [lambda s:s.update(scene=1),lambda s:s.update(generation=2),
                       lambda s:s['me'].update(map_token='0x19000000'),lambda s:s.update(pid=PID+1)]:
            bad={1:deepcopy(after[1])};change(bad[1])
            self.assertFalse(self.api.enemy_death_sync(before,bad,members=[1],monster_id=2)['complete'])

    def test_teleport_after_mutation_retains_receipt_when_context_disappears(self):
        receipt=dict(native=dict(status='applied'),run_id=RUN,room_generation=3,session_nonce=NONCE)
        with patch.object(self.api,'execute_native',return_value=receipt),\
                patch.object(self.api,'load_clients',return_value={1:client()}),\
                patch.object(self.api,'load_server',side_effect=[server(),self.api.TestControlError('room_changed')]):
            result=self.api.teleport('.',uid=1,x=300,y=240,timeout=.1)
        self.assertEqual(result['status'],'partial_sync')
        self.assertEqual(result['native']['status'],'applied')
        self.assertEqual(result['error'],'room_changed')

    def test_isolation_after_applied_server_layout_retains_partial_evidence(self):
        before=server();after=deepcopy(before);after['instances'][0]['room_generation']+=1
        with patch.object(self.api,'load_server',side_effect=[before,before,after]),\
                patch.object(self.api,'load_clients',return_value={1:client()}),\
                patch.object(self.api,'server_layout',return_value=dict(status='applied',request_id=100)),\
                patch.object(self.api,'execute_native') as native:
            result=self.api.isolate('.',uid=1,ground_id=5,park=(500,200),timeout=.1)
        self.assertEqual(result['status'],'partial_sync')
        self.assertEqual(result['server']['status'],'applied')
        self.assertEqual(result['error'],'room_changed')
        native.assert_not_called()

    def test_isolation_rejects_town_replica_before_changing_server_layout(self):
        town=client();town['scene']=1
        with patch.object(self.api,'load_server',return_value=server()),\
                patch.object(self.api,'load_clients',return_value={1:town}),\
                patch.object(self.api,'server_layout') as layout:
            with self.assertRaises(self.api.TestControlError):
                self.api.isolate('.',uid=1,ground_id=5,park=(500,200),timeout=.1)
            layout.assert_not_called()


if __name__=='__main__':unittest.main()
