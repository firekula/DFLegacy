"""Offline test-control contracts: no process memory, input or running game."""
from copy import deepcopy
import importlib
import io
import json
from pathlib import Path
import os
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
                patch.object(self.api.state,'now_tick_ms',return_value=NOW),\
                patch.object(self.api.state,'load_snapshot',return_value=client()),patch.object(self.api.time,'sleep'):
            self.assertEqual(set(self.api.load_clients('.',uids=[1])),{1})

    def test_client_query_retries_second_read_access_failure(self):
        with patch.object(Path,'glob',return_value=[Path('snapshot.json')]),\
                patch.object(self.api,'read_json',return_value=dict(pid=PID)),\
                patch.object(self.api.state,'get_process_started_filetime',return_value=START),\
                patch.object(self.api.state,'now_tick_ms',return_value=NOW),\
                patch.object(self.api.state,'load_snapshot',side_effect=[self.api.state.SnapshotError('unreadable_json'),client()]),\
                patch.object(self.api.time,'sleep'):
            self.assertEqual(set(self.api.load_clients('.',uids=[1])),{1})

    def test_client_query_never_merges_disjoint_retry_observations(self):
        second=deepcopy(client());second['me']['uid']=2
        with patch.object(Path,'glob',return_value=[Path('snapshot.json')]),\
                patch.object(self.api,'read_json',return_value=dict(pid=PID)),\
                patch.object(self.api.state,'get_process_started_filetime',return_value=START),\
                patch.object(self.api.state,'now_tick_ms',return_value=NOW),\
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


class InventoryDropTests(unittest.TestCase):
    setUp=TestControlTests.setUp
    lease=TestControlTests.lease
    request=TestControlTests.request
    def test_native_exported_dead_observer_fixture_roundtrip(self):
        from tools.acceptance.native_fixtures import fixture_path
        value=json.loads(fixture_path('observer').read_bytes())
        sdk=client();sdk.update(pid=value['pid'],process_started_filetime=value['process_started_filetime'],
            sequence=value['snapshot_sequence'],generation=value['generation'])
        sdk['room'].update(map_token=value['map_token'],stage_token=value['observer']['stage_token'])
        sdk['me'].update(uid=value['uid'],actor_token=value['observer']['actor_token'],
            user_token=value['observer']['user_token'],dead=True,hp=0,position=None,map_token=None,same_map=False,detached=True)
        self.assertIsNone(value['observer']['actor_map_token'])
        self.assertIs(self.api.validate_ground_snapshot(value,sdk,now_tick_ms=value['captured_tick_ms']),value)
        self.assertIs(self.api.validate_inventory_snapshot(value,sdk,now_tick_ms=value['captured_tick_ms'],
            session_nonce=value['session_nonce'],run_id=value['run_id']),value)
    def test_dead_observer_binding_uses_current_stage_without_authorizing_input(self):
        sdk=client();sdk['me'].update(dead=True,hp=0,position=None,map_token=None,same_map=False,detached=True)
        value=self.native_inventory();value.update(valid=True,truncated=False,ground_items=[],
            map_token=sdk['room']['map_token'],snapshot_sequence=sdk['sequence'],observer=dict(
                actor_token=sdk['me']['actor_token'],user_token=sdk['me']['user_token'],
                actor_map_token=None,stage_token=sdk['room']['stage_token'],dead=True))
        self.assertIs(self.api.validate_ground_snapshot(value,sdk,now_tick_ms=NOW),value)
        self.assertIs(self.api.validate_inventory_snapshot(value,sdk,now_tick_ms=NOW,session_nonce=NONCE,run_id=RUN),value)
        retained=deepcopy(sdk);retained['me'].update(map_token='0x00020000',detached=False)
        retained_value=deepcopy(value);retained_value['observer']['actor_map_token']='0x00020000'
        self.assertEqual(self.api.validate_ground_snapshot(retained_value,retained,now_tick_ms=NOW)['map_token'],sdk['room']['map_token'])
        lease=self.api.parse_lease(self.lease(),uid=1,now_filetime=FT)
        with self.assertRaises(self.api.TestControlError):
            self.api.make_request(sdk,lease,operation=1,target_id=1,x=300,y=240,request_id=START,issued_tick_ms=NOW)
        for change in ('old_without_binding','actor','user','stage','map','missing_actor_map','death','sequence'):
            bad=deepcopy(value)
            if change=='old_without_binding':bad.pop('observer')
            if change in ('actor','user','stage'):bad['observer'][change+'_token']='0x00010000'
            if change=='map':bad['observer']['actor_map_token']='0x00010000'
            if change=='missing_actor_map':bad['observer'].pop('actor_map_token')
            if change=='death':bad['observer']['dead']=False
            if change=='sequence':bad['snapshot_sequence']=sdk['sequence']+6
            with self.subTest(change=change),self.assertRaises(self.api.TestControlError):
                self.api.validate_ground_snapshot(bad,sdk,now_tick_ms=NOW)
            with self.subTest(inventory_change=change),self.assertRaises(self.api.TestControlError):
                self.api.validate_inventory_snapshot(bad,sdk,now_tick_ms=NOW,session_nonce=NONCE,run_id=RUN)

    def native_inventory(self, sdk=None):
        sdk=sdk or client()
        return dict(schema='dfl-client-test-state/v1',pid=sdk['pid'],uid=sdk['me']['uid'],
            process_started_filetime=sdk['process_started_filetime'],generation=sdk['generation'],
            map_token=sdk['me']['map_token'],captured_tick_ms=NOW,session_nonce=NONCE,run_id=RUN,
            supported_operations=[1,2,3,4,5],valid=False,truncated=True,ground_items=[],
            inventory=dict(valid=True,reason='',container_token='0x18010000',array_token='0x18020000',
                capacity=120,pending=False,items=[dict(slot=9,item_token='0xF1234567',item_id=65535,count=100000)]))

    def party(self):
        before=server();room=before['instances'][0]
        room['members']=[dict(uid=i,character_id=f'{i:08d}-1111-4111-8111-111111111111',party_slot=i-1)
            for i in range(1,5)]
        source=dict(space='main',slot=9,item_id=65535,count=100000,
            instance_id='22222222-2222-4222-8222-222222222222',state=0,durability=None,
            seal_state=1,equipment_quality_seed=0xF1234567)
        room['inventories']=[dict(uid=1,character_id=room['members'][0]['character_id'],gold=5000,items=[source])]
        clients={}
        for uid in range(1,5):
            sdk=client();sdk['pid']+=uid;sdk['me']['uid']=uid;clients[uid]=sdk
        return before,clients

    def after_drop(self,before,**changes):
        after=deepcopy(before);room=after['instances'][0];source=room['inventories'][0]['items'][0]
        source['count']-=40000
        row={k:v for k,v in source.items() if k not in ('space','slot')}
        row.update(ground_id=7,count=40000,is_gold=False,x=300,y=240,pending=False,
            owner_uid=1,routing_source=0)
        row.update(changes);room['ground_items'].append(row)
        return after

    def run_drop(self,before,clients,after,observed=None,*,slot=9,item_id=65535,count=40000,during_confirmation=None,native_status='applied'):
        self.assertTrue(callable(getattr(self.api,'inventory_drop',None)),'inventory_drop is missing')
        receipt=dict(native=dict(status=native_status),run_id=RUN,room_generation=3,session_nonce=NONCE)
        native=self.native_inventory(clients[1])
        source=before['instances'][0]['inventories'][0]
        quantity=source['gold'] if slot==0 else source['items'][0]['count']
        native['inventory']['items'][0].update(slot=slot,item_id=item_id,count=quantity)
        if slot!=0:
            native['inventory']['items'].append(dict(slot=0,item_token='0x18030000',item_id=0,count=source['gold']))
        snapshots={uid:dict(schema='dfl-client-test-state/v1',pid=sdk['pid'],uid=uid,
            process_started_filetime=START,generation=1,map_token=sdk['me']['map_token'],
            captured_tick_ms=NOW,session_nonce=NONCE,run_id=RUN,valid=True,truncated=False,
            ground_items=[dict(ground_id=7,x=300,y=240,z=0,active=True,pending=False)])
            for uid,sdk in clients.items()}
        if observed is not None:snapshots=observed(snapshots)
        def read(path):
            pid=int(Path(path).stem.rsplit('-',1)[1])
            return native if pid==clients[1]['pid'] and not submitted[0] else snapshots.get(pid-PID,{})
        submitted=[False]
        def emit(*args,**kwargs):
            if submitted[0]:raise AssertionError('native request must not be resent')
            submitted[0]=True;return receipt
        observed_confirmation=[False]
        def load(*args,**kwargs):
            if submitted[0] and during_confirmation and not observed_confirmation[0]:
                observed_confirmation[0]=True;during_confirmation(directory)
            return after if submitted[0] else before
        with tempfile.TemporaryDirectory() as directory,patch.object(self.api,'load_server',side_effect=load), \
                patch.object(self.api,'load_clients',return_value=clients),patch.object(self.api,'read_json',side_effect=read), \
                patch.object(self.api,'execute_native',side_effect=emit), \
                patch.object(self.api.state,'now_tick_ms',return_value=NOW),patch.object(self.api.time,'sleep'):
            return self.api.inventory_drop(directory,uid=1,slot=slot,item_id=item_id,count=count,timeout=.001)

    def test_inventory_drop_wire_has_full_count_id_and_signed_object_token(self):
        try:raw=self.request(operation=5,target_id=511,x=2147483647,y=65535,item_token='0xF1234567')
        except (TypeError,self.api.TestControlError) as error:self.fail(f'op5 wire missing: {error}')
        value=self.api.parse_request(raw)
        self.assertEqual((len(raw),value['target_id'],value['x'],value['y'],value['reserved']),
            (112,511,2147483647,65535,-249346713))
        self.assertEqual(self.api.parse_request(self.request(operation=5,target_id=0,x=1000,y=0,
            item_token='0x10000000'))['operation'],5)
        for kwargs in [dict(target_id=1),dict(target_id=512),dict(x=0),dict(x=True),dict(y=65536),
                       dict(y=0),dict(item_token='0x00000000'),dict(item_token=None),dict(park=(0,0))]:
            args=dict(operation=5,target_id=9,x=40000,y=65535,item_token='0xF1234567');args.update(kwargs)
            with self.subTest(args=args),self.assertRaises(self.api.TestControlError):self.request(**args)
        with self.assertRaises(self.api.TestControlError):self.request(item_token='0x10000000')

    def test_inventory_validity_is_independent_of_ground_and_rejects_old_pending_or_duplicate_objects(self):
        self.assertTrue(callable(getattr(self.api,'validate_inventory_snapshot',None)),'inventory validation missing')
        good=self.native_inventory()
        self.assertEqual(self.api.validate_inventory_snapshot(good,client(),now_tick_ms=NOW,
            session_nonce=NONCE,run_id=RUN)['inventory']['items'][0]['count'],100000)
        for change in [lambda v:v.pop('supported_operations'),lambda v:v.update(captured_tick_ms=NOW-751),
            lambda v:v.update(session_nonce=NONCE-1),lambda v:v.update(run_id=RUN+1),
            lambda v:v.update(generation=2),lambda v:v['inventory'].update(pending=True),
            lambda v:v['inventory'].update(valid=False),lambda v:v['inventory'].update(capacity=513),
            lambda v:v['inventory']['items'][0].update(item_token='0x00000000'),
            lambda v:v['inventory']['items'].append(deepcopy(v['inventory']['items'][0])),
            lambda v:v['inventory']['items'].append(dict(slot=10,item_token='0xF1234567',item_id=3,count=1)),
            lambda v:v['inventory']['items'][0].update(slot=8)]:
            bad=deepcopy(good);change(bad)
            with self.subTest(bad=bad),self.assertRaises(self.api.TestControlError):
                self.api.validate_inventory_snapshot(bad,client(),now_tick_ms=NOW,session_nonce=NONCE,run_id=RUN)

    def test_inventory_native_receipt_checks_reserved_object_token(self):
        try:request=self.api.parse_request(self.request(operation=5,target_id=9,x=40000,y=65535,item_token='0xF1234567'))
        except (TypeError,self.api.TestControlError) as error:self.fail(f'op5 wire missing: {error}')
        value={k:request[k] for k in self.api.FIELDS if k not in ('issued_tick_ms','snapshot_tick_ms','snapshot_sequence')}
        value.update(schema='dfl-client-test-control/v1',status='applied',tick_ms=NOW,
            server_committed=False,persistence_verified=False,actor_token='0x1812E468',map_token='0x184AFED8',user_token='0x180A1AA8')
        self.api.native_result(value,request,NOW)
        for token in [None,0,-249346712,True]:
            with self.subTest(token=token),self.assertRaises(self.api.TestControlError):
                self.api.native_result(dict(value,reserved=token),request,NOW)

    def test_inventory_drop_confirms_source_active_route_metadata_and_all_four_native_replicas(self):
        before,clients=self.party();result=self.run_drop(before,clients,self.after_drop(before))
        self.assertEqual(result['status'],'synchronized')
        self.assertTrue(result['source_change']['matched'])
        self.assertEqual(result['ground_confirmation']['ground_id'],7)
        self.assertEqual(result['authoritative_source'],'server_inventory_and_active_ground_snapshot')
        self.assertTrue(result['synchronization']['complete'])

    def test_inventory_source_change_without_ground_or_monster_ground_is_not_confirmation(self):
        for changes in [dict(routing_source=1),dict(owner_uid=2),dict(instance_id=None),
                        dict(state=1),dict(seal_state=True),dict(durability=0),dict(count=39999),dict(pending=True),dict(ground_id=5)]:
            before,clients=self.party();result=self.run_drop(before,clients,self.after_drop(before,**changes))
            with self.subTest(changes=changes):
                self.assertEqual(result['status'],'partial_sync');self.assertTrue(result['source_change']['matched'])
                self.assertIsNone(result['ground_confirmation']);self.assertEqual(result['native']['status'],'applied')
        before,clients=self.party();after=self.after_drop(before);after['instances'][0]['ground_items'].pop()
        self.assertIsNone(self.run_drop(before,clients,after)['ground_confirmation'])

    def test_inventory_drop_rejects_unchanged_source_even_when_matching_new_ground_appears(self):
        before,clients=self.party();after=self.after_drop(before)
        after['instances'][0]['inventories']=deepcopy(before['instances'][0]['inventories'])
        result=self.run_drop(before,clients,after)
        self.assertEqual(result['status'],'not_confirmed');self.assertFalse(result['source_change']['matched'])

    def test_inventory_drop_missing_or_wrong_epoch_native_peer_retains_applied_and_server_confirmation(self):
        for change in [lambda values:{k:v for k,v in values.items() if k!=4},
                       lambda values:{**values,4:dict(values[4],generation=2)},
                       lambda values:{**values,4:dict(values[4],session_nonce=NONCE-1)}]:
            before,clients=self.party();result=self.run_drop(before,clients,self.after_drop(before),change)
            self.assertEqual(result['status'],'partial_sync');self.assertEqual(result['ground_confirmation']['ground_id'],7)
            self.assertFalse(result['synchronization']['complete']);self.assertEqual(result['native']['status'],'applied')

    def test_inventory_drop_room_change_after_submission_keeps_receipt(self):
        before,clients=self.party();after=self.after_drop(before);after['instances'][0]['room_generation']+=1
        result=self.run_drop(before,clients,after)
        self.assertEqual(result['status'],'not_confirmed');self.assertEqual(result['native']['status'],'applied')
        self.assertEqual(result['error'],'room_changed')

    def test_execute_drop_refuses_stale_object_token_before_publishing(self):
        before,clients=self.party();sdk=clients[1];native=self.native_inventory(sdk)
        native['inventory']['items'][0]['item_token']='0xF1234568'
        with tempfile.TemporaryDirectory() as directory:
            Path(directory,f'dfdebug-test-lease-1.bin').write_bytes(self.lease())
            with patch.object(self.api,'load_server',return_value=before), \
                    patch.object(self.api,'load_clients',return_value={1:sdk}), \
                    patch.object(self.api,'read_json',return_value=native), \
                    patch.object(self.api,'filetime',return_value=FT), \
                    patch.object(self.api.state,'now_tick_ms',return_value=NOW):
                with self.assertRaisesRegex(self.api.TestControlError,'inventory_item_token_changed'):
                    self.api.execute_native(directory,uid=1,operation=5,target_id=9,x=40000,y=65535,
                        item_token='0xF1234567',expected_server=before,expected_room=before['instances'][0],expected_client=sdk)
            self.assertEqual([p.name for p in Path(directory).iterdir()],['dfdebug-test-lease-1.bin'])

    def test_inventory_drop_gold_limit_and_empty_source_fail_before_request(self):
        self.assertTrue(callable(getattr(self.api,'inventory_drop',None)))
        before,clients=self.party();native=self.native_inventory(clients[1]);native['inventory']['items']=[]
        with patch.object(self.api,'load_server',return_value=before), \
                patch.object(self.api,'load_clients',return_value=clients), \
                patch.object(self.api,'read_json',return_value=native), \
                patch.object(self.api.state,'now_tick_ms',return_value=NOW):
            with self.assertRaisesRegex(self.api.TestControlError,'inventory_slot_empty'):
                self.api.inventory_drop('.',uid=1,slot=9,item_id=65535,count=40000)
            with self.assertRaisesRegex(self.api.TestControlError,'gold_drop_limit'):
                self.api.inventory_drop('.',uid=1,slot=0,item_id=0,count=1001)

    def test_inventory_cli_keeps_partial_timeout_exit_two_and_validation_exit_one(self):
        for status,want in [('synchronized',0),('partial_sync',2),('not_confirmed',2)]:
            with patch.object(self.api,'inventory_drop',return_value=dict(status=status)),patch('sys.stdout',new_callable=io.StringIO):
                self.assertEqual(self.api.main(['--directory','.','inventory','drop','--uid','1','--slot','9',
                    '--item','65535','--count','40000']),want)
        with patch.object(self.api,'inventory_drop',side_effect=self.api.TestControlError('inventory_pending_or_unknown')), \
                patch('sys.stdout',new_callable=io.StringIO):
            self.assertEqual(self.api.main(['--directory','.','inventory','drop','--uid','1','--slot','9',
                '--item','65535','--count','40000']),1)

    def test_stack_with_explicit_empty_guid_and_nullable_metadata_can_be_dropped(self):
        before,clients=self.party();source=before['instances'][0]['inventories'][0]['items'][0]
        source.update(instance_id='00000000-0000-0000-0000-000000000000',state=None,
            durability=None,seal_state=None,equipment_quality_seed=None)
        result=self.run_drop(before,clients,self.after_drop(before))
        self.assertEqual(result['status'],'synchronized')
        self.assertEqual(result['ground_confirmation']['instance_id'],'00000000-0000-0000-0000-000000000000')
        self.assertIsNone(result['ground_confirmation']['state'])

    def test_full_item_removal_and_gold_use_their_actual_server_sources(self):
        before,clients=self.party();before['instances'][0]['inventories'][0]['items'][0]['count']=1
        after=self.after_drop(before);after['instances'][0]['inventories'][0]['items']=[]
        after['instances'][0]['ground_items'][-1]['count']=1
        result=self.run_drop(before,clients,after,count=1)
        self.assertEqual(result['status'],'synchronized');self.assertIsNone(result['source_change']['after'])
        before,clients=self.party();after=self.after_drop(before)
        after['instances'][0]['inventories'][0]=deepcopy(before['instances'][0]['inventories'][0])
        after['instances'][0]['inventories'][0]['gold']=4000
        after['instances'][0]['ground_items'][-1].update(item_id=0,count=1000,is_gold=True,
            instance_id='00000000-0000-0000-0000-000000000000',state=None,durability=None,
            seal_state=None,equipment_quality_seed=None)
        result=self.run_drop(before,clients,after,slot=0,item_id=0,count=1000)
        self.assertEqual(result['status'],'synchronized');self.assertEqual(result['source_change']['after']['count'],4000)

    def test_zero_gold_native_row_does_not_prevent_inventory_observation(self):
        value=self.native_inventory()
        value['inventory']['items'].append(dict(slot=0,item_token='0x18030000',item_id=0,count=0))
        result=self.api.validate_inventory_snapshot(value,client(),now_tick_ms=NOW,session_nonce=NONCE,run_id=RUN)
        self.assertEqual(result['inventory']['items'][0]['item_id'],65535)
        before,clients=self.party();before['instances'][0]['inventories'][0]['gold']=0
        self.assertEqual(self.run_drop(before,clients,self.after_drop(before))['status'],'synchronized')
        value['inventory']['items'][0]['count']=0
        with self.assertRaises(self.api.TestControlError):
            self.api.validate_inventory_snapshot(value,client(),now_tick_ms=NOW,session_nonce=NONCE,run_id=RUN)

    def test_inventory_drop_supports_every_current_party_size(self):
        for size in (1,2,3):
            before,clients=self.party();before['instances'][0]['members']=before['instances'][0]['members'][:size]
            clients={uid:sdk for uid,sdk in clients.items() if uid<=size}
            with self.subTest(size=size):
                result=self.run_drop(before,clients,self.after_drop(before))
                self.assertEqual(result['status'],'synchronized')
                self.assertEqual(len(result['synchronization']['clients']),size)

    def test_native_drop_animation_coordinates_are_observations_not_identity_gate(self):
        before,clients=self.party()
        def airborne(values):
            for uid,value in values.items():
                value['ground_items'][0].update(x=300+uid*25,y=300+uid*5,z=uid*20)
            return values
        result=self.run_drop(before,clients,self.after_drop(before),airborne)
        self.assertEqual(result['status'],'synchronized')
        self.assertTrue(result['synchronization']['identity_complete'])
        self.assertFalse(result['synchronization']['positions']['consistent'])
        self.assertEqual(result['synchronization']['clients']['4']['position'],dict(x=400,y=320,z=80))

    def test_matching_ground_id_still_requires_active_nonpending_on_every_client(self):
        for change in [dict(active=False),dict(pending=True),dict(ground_id=8)]:
            before,clients=self.party()
            def changed(values):values[4]['ground_items'][0].update(change);return values
            result=self.run_drop(before,clients,self.after_drop(before),changed)
            self.assertEqual(result['status'],'partial_sync')
            self.assertFalse(result['synchronization']['complete'])

    def test_controller_lock_covers_server_confirmation_after_native_receipt(self):
        before,clients=self.party();outcomes=[]
        def concurrent(directory):
            try:self.api.inventory_drop(directory,uid=1,slot=9,item_id=65535,count=40000,timeout=.001)
            except self.api.TestControlError as error:outcomes.append(error.code)
            else:outcomes.append('duplicate_submission_allowed')
        result=self.run_drop(before,clients,self.after_drop(before),during_confirmation=concurrent)
        self.assertEqual(result['status'],'synchronized')
        self.assertEqual(outcomes,['command_busy'])

    def test_partial_drop_receipt_still_confirms_persisted_source_and_all_ground_observers(self):
        before,clients=self.party()
        result=self.run_drop(before,clients,self.after_drop(before),native_status='partial')
        self.assertEqual(result['status'],'synchronized')
        self.assertEqual(result['native']['status'],'partial')
        self.assertTrue(result['source_change']['matched'])
        self.assertEqual(result['ground_confirmation']['ground_id'],7)
        self.assertTrue(result['synchronization']['identity_complete'])

    def test_partial_drop_with_unchanged_source_is_not_a_definite_rejection(self):
        before,clients=self.party();result=self.run_drop(before,clients,deepcopy(before),native_status='partial')
        self.assertEqual(result['status'],'not_confirmed')
        self.assertEqual(result['native']['status'],'partial')
        self.assertFalse(result['source_change']['changed'])
        self.assertIsNone(result['ground_confirmation'])

    def test_partial_drop_room_change_keeps_the_uncertain_native_receipt(self):
        before,clients=self.party();after=self.after_drop(before);after['instances'][0]['room_generation']+=1
        result=self.run_drop(before,clients,after,native_status='partial')
        self.assertEqual(result['status'],'not_confirmed')
        self.assertEqual(result['native']['status'],'partial')
        self.assertEqual(result['error'],'room_changed')


class PartialActorOperationTests(unittest.TestCase):
    setUp=TestControlTests.setUp

    def operation(self,action,scenario):
        before=server();after=deepcopy(before);prior=client();observed=deepcopy(prior)
        if scenario=='confirmed':
            if action=='teleport':observed['players'][0]['position'].update(x=300,y=240)
            else:
                observed['enemies']=[];after['instances'][0]['monsters'][0]['alive']=False
        if scenario=='room_changed':after['instances'][0]['room_generation']+=1
        receipt=dict(native=dict(status='partial',reason='native_failed_after_touch'),
            run_id=RUN,room_generation=3,session_nonce=NONCE)
        submitted=[False]
        def emit(*args,**kwargs):
            if submitted[0]:raise AssertionError('uncertain native operation must not be resent')
            submitted[0]=True;return receipt
        with patch.object(self.api,'execute_native',side_effect=emit), \
                patch.object(self.api,'load_server',side_effect=lambda *a,**k:after if submitted[0] else before), \
                patch.object(self.api,'load_clients',side_effect=lambda *a,**k:{1:observed if submitted[0] else prior}), \
                patch.object(self.api.time,'sleep'):
            if action=='teleport':return self.api.teleport('.',uid=1,x=300,y=240,timeout=.001)
            return self.api.kill('.',uid=1,monster_id=2,timeout=.001)

    def test_touched_partial_teleport_uses_peer_position_confirmation_without_reissuing(self):
        for scenario,status in [('confirmed','synchronized'),('unchanged','partial_sync'),('room_changed','partial_sync')]:
            with self.subTest(scenario=scenario):
                result=self.operation('teleport',scenario)
                self.assertEqual(result['status'],status)
                self.assertEqual(result['native']['status'],'partial')
                self.assertEqual(result['native']['reason'],'native_failed_after_touch')
                if scenario=='room_changed':self.assertEqual(result['error'],'room_changed')
                else:self.assertEqual(result['synchronization']['complete'],scenario=='confirmed')

    def test_touched_partial_kill_uses_server_and_peer_death_confirmation_without_reissuing(self):
        for scenario,status in [('confirmed','synchronized'),('unchanged','partial_sync'),('room_changed','partial_sync')]:
            with self.subTest(scenario=scenario):
                result=self.operation('kill',scenario)
                self.assertEqual(result['status'],status)
                self.assertEqual(result['native']['status'],'partial')
                self.assertEqual(result['native']['reason'],'native_failed_after_touch')
                if scenario=='room_changed':self.assertEqual(result['error'],'room_changed')
                else:
                    self.assertEqual(result['server_monster_removed'],scenario=='confirmed')
                    self.assertEqual(result['clients_monster_dead'],scenario=='confirmed')


class PlayerKillTests(unittest.TestCase):
    setUp=TestControlTests.setUp
    lease=TestControlTests.lease
    request=TestControlTests.request

    def test_player_death_wire_is_self_only_and_has_no_free_hp_operands(self):
        raw=self.request(operation=6,target_id=1,x=0,y=0)
        self.assertEqual(self.api.parse_request(raw)['operation'],6)
        for values in [dict(target_id=2),dict(x=1),dict(y=-1),dict(item_token='0x10000')]:
            with self.subTest(values=values):
                args=dict(operation=6,target_id=1,x=0,y=0);args.update(values)
                with self.assertRaises(self.api.TestControlError):self.request(**args)

    def operation(self,scenario='confirmed',native_status='applied'):
        self.assertTrue(callable(getattr(self.api,'player_kill',None)),'player kill interface missing')
        before=server();before['instances'][0]['members'][0]['alive']=True
        before['instances'][0]['members'].append(dict(uid=3,character_id='33333333-3333-4333-8333-333333333333',party_slot=1,alive=True))
        local=client();peer=deepcopy(local);peer['pid']+=1;peer['me']=deepcopy(local['me']);peer['me'].update(uid=3,user_token='0x19000000',actor_token='0x19100000')
        local['players'].append(deepcopy(peer['me']));peer['players']=deepcopy(local['players'])
        prior={1:local,3:peer};after=deepcopy(before);observed=deepcopy(prior)
        if scenario!='server_alive':after['instances'][0]['members'][0]['alive']=False
        for s in observed.values():
            s['sequence']+=1;s['captured_tick_ms']+=10
            if scenario!='client_alive':
                for p in s['players']:
                    if p['uid']==1:p.update(hp=0,dead=True,map_token=None,same_map=False,detached=True,position=None)
        if scenario!='client_alive':observed[1]['me'].update(hp=0,dead=True,map_token=None,same_map=False,detached=True,position=None)
        if scenario=='missing_peer':observed[3]['players']=[p for p in observed[3]['players'] if p['uid']!=1]
        if scenario=='room_changed':after['instances'][0]['room_generation']+=1
        if scenario=='replaced_actor':observed[1]['me']['actor_token']='0x20000000'
        if scenario=='new_generation':observed[1]['generation']+=1
        if scenario=='stale_peer':observed[3]['sequence']=peer['sequence'];observed[3]['captured_tick_ms']=peer['captured_tick_ms']
        receipt=dict(native=dict(status=native_status,reason='native_player_death_requested'),run_id=RUN,room_generation=3,session_nonce=NONCE)
        calls=[]
        def emit(*args,**kwargs):
            self.assertFalse(calls,'player kill must never retransmit');calls.append(kwargs);return receipt
        with patch.object(self.api,'execute_native',side_effect=emit), \
                patch.object(self.api,'load_server',side_effect=lambda *a,**k:after if calls else before), \
                patch.object(self.api,'load_clients',side_effect=lambda *a,**k:observed if calls else prior), \
                patch.object(self.api.time,'sleep'):
            out=self.api.player_kill('.',uid=1,timeout=.002)
        self.assertEqual(len(calls),1);self.assertEqual(calls[0]['operation'],6);self.assertEqual(calls[0]['target_id'],1)
        return out

    def test_player_kill_requires_actual_server_self_and_peer_death(self):
        r=self.operation();self.assertEqual(r['status'],'synchronized');self.assertTrue(r['server_player_dead']);self.assertTrue(r['synchronization']['complete'])

    def test_incomplete_or_stale_observers_never_confirm_death(self):
        for scenario in ['server_alive','client_alive','missing_peer','stale_peer','replaced_actor','room_changed','new_generation']:
            with self.subTest(scenario=scenario):self.assertEqual(self.operation(scenario)['status'],'partial_sync')

    def test_partial_native_player_death_is_observed_once_without_retransmission(self):
        r=self.operation(native_status='partial');self.assertEqual(r['status'],'synchronized');self.assertEqual(r['native']['status'],'partial')

    def test_player_kill_cli_dispatches_separately_from_monster_kill(self):
        self.assertTrue(callable(getattr(self.api,'player_kill',None)),'player kill interface missing')
        with patch.object(self.api,'player_kill',return_value=dict(status='synchronized')),patch('sys.stdout',new=io.StringIO()):
            self.assertEqual(self.api.main(['--directory','.','player','kill','--uid','1']),0)

    def test_player_kill_rechecks_party_and_authoritative_life_before_publishing(self):
        for changed in ['party','dead']:
            with self.subTest(changed=changed),tempfile.TemporaryDirectory() as directory:
                before=server();before['instances'][0]['members'][0]['alive']=True;after=deepcopy(before);sdk=client()
                if changed=='party':after['instances'][0]['members'].append(dict(uid=3,character_id='33333333-3333-4333-8333-333333333333',party_slot=1,alive=True))
                else:after['instances'][0]['members'][0]['alive']=False
                Path(directory,'dfdebug-test-lease-1.bin').write_bytes(self.lease())
                with patch.object(self.api,'load_server',side_effect=[before,before,after]), \
                        patch.object(self.api,'load_clients',return_value={1:sdk}), \
                        patch.object(self.api,'filetime',return_value=FT), \
                        patch.object(self.api.state,'get_process_started_filetime',return_value=START), \
                        patch.object(self.api.state,'now_tick_ms',return_value=NOW):
                    with self.assertRaisesRegex(self.api.TestControlError,'party_changed|authoritative_living_player_required'):
                        self.api.execute_native(directory,uid=1,operation=6,target_id=1,timeout=.001)
                self.assertFalse(Path(directory,f'dfdebug-test-control-{sdk["pid"]}.bin').exists())


if __name__=='__main__':unittest.main()
