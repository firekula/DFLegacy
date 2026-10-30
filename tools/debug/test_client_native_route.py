import importlib
from contextlib import ExitStack, nullcontext
from copy import deepcopy
import unittest
from unittest.mock import patch
from tools.debug.test_client_route import snapshot
from tools.debug.test_client_state import NOW, PID, START


class NativeRouteWireTests(unittest.TestCase):
    def setUp(self):
        self.api=importlib.import_module('tools.debug.client_native_route')

    def request(self):
        raw=self.api.make_request(snapshot(),town_id=2,area_id=2,x=575,y=247,request_id=50,issued_tick_ms=NOW)
        return raw,self.api.parse_request(raw)

    def result(self,request):
        return dict(schema='dfl-client-route/v1',pid=request['pid'],process_started_filetime=request['process_started_filetime'],
                    request_id=50,uid=request['uid'],source='debug',status='arrived',reason='final_position_observed',tick_ms=NOW,
                    goal=dict(town_id=2,area_id=2,x=575,y=247),origin=dict(generation=request['generation'],
                    actor_token=f"0x{request['actor_token']:08X}",map_token=f"0x{request['map_token']:08X}",
                    user_token=f"0x{request['user_token']:08X}",context_token=f"0x{request['context_token']:08X}"),
                    observed=dict(town_id=2,area_id=2,position=dict(x=575,y=247),direction_raw=dict(x=4,y=4)))

    def test_new_wire_keeps_origin_and_final_area_separate(self):
        raw,request=self.request()
        self.assertEqual(len(raw),96);self.assertEqual(raw[:4],b'DLRT')
        self.assertEqual((request['town_id'],request['area_id']),(2,2))
        self.assertEqual(request['map_token'],int(snapshot()['town']['map_token'],16))

    def test_goal_result_checks_exact_original_binding_and_goal(self):
        _,request=self.request();value=self.result(request)
        self.assertEqual(self.api.validate_response(value,request,NOW)['status'],'arrived')
        for key in ('actor_token','map_token','user_token','context_token'):
            bad=self.result(request);bad['origin'][key]='0x19999990'
            with self.subTest(key=key),self.assertRaises(self.api.nav.NavigationError):
                self.api.validate_response(bad,request,NOW)
        value['goal']['area_id']=1
        with self.assertRaises(self.api.nav.NavigationError):self.api.validate_response(value,request,NOW)

    def test_queued_goal_cannot_be_reported_as_arrived(self):
        _,request=self.request();value=self.result(request);value['status']='requested'
        self.assertEqual(self.api.validate_response(value,request,NOW)['status'],'requested')
        value['status']='arrived';value['observed']['position']['y']=200
        with self.assertRaises(self.api.nav.NavigationError):self.api.validate_response(value,request,NOW)

    def test_future_old_or_wrong_source_result_cannot_be_used(self):
        _,request=self.request()
        for change in (dict(source='minimap'),dict(tick_ms=NOW+1),dict(tick_ms=NOW-1001)):
            value=self.result(request);value.update(change)
            with self.subTest(change=change),self.assertRaises(self.api.nav.NavigationError):
                self.api.validate_response(value,request,NOW)

    def test_bound_terminal_remains_usable_after_delayed_publication(self):
        _,request=self.request()
        for status in ('arrived','rejected','interrupted'):
            value=self.result(request);value['status']=status
            with self.subTest(status=status):
                self.assertEqual(self.api.validate_response(value,request,NOW+1240)['status'],status)

    def test_nonterminal_progress_still_expires_after_one_second(self):
        _,request=self.request()
        for status in ('requested','queued','awaiting_confirmation','moving','waiting_data',
                       'waiting_fresh_sample','waiting_transition'):
            value=self.result(request);value['status']=status
            with self.subTest(status=status),self.assertRaisesRegex(self.api.nav.NavigationError,'stale_response'):
                self.api.validate_response(value,request,NOW+1001)

    def test_delayed_terminal_keeps_time_binding_and_arrival_proofs(self):
        _,request=self.request()
        for field in ('before_issue','future','request_id','source','generation','actor_token','map_token',
                      'user_token','context_token','goal','position','direction'):
            value=self.result(request)
            if field=='before_issue':value['tick_ms']=NOW-1
            elif field=='future':value['tick_ms']=NOW+1241
            elif field=='request_id':value['request_id']+=1
            elif field=='source':value['source']='minimap'
            elif field=='generation':value['origin'][field]+=1
            elif field.endswith('_token'):value['origin'][field]='0x19999990'
            elif field=='goal':value['goal']['area_id']=1
            elif field=='position':value['observed']['position']['x']=500
            else:value['observed']['direction_raw']['x']=0
            with self.subTest(field=field),self.assertRaises(self.api.nav.NavigationError):
                self.api.validate_response(value,request,NOW+1240)


class NativeRouteExecuteTests(unittest.TestCase):
    def setUp(self):
        self.api=importlib.import_module('tools.debug.client_native_route')

    def execute_terminal(self,status,publication_ms=1240,sdk_ready_ms=0,*,wrong_position=False,
                         wrong_direction=False,sdk_capture=None,changed_identity=False,
                         changed_context=False,superseded=False,sdk_error='unreadable_json'):
        """Real execute loop with no process, filesystem, sleep or input access."""
        elapsed=[0.0];wire=[None];writes=[0];captures=[0]

        class FakePath:
            def __init__(self,value):self.value=str(value)
            def __truediv__(self,leaf):return FakePath(self.value+'/'+str(leaf))
            def is_dir(self):return True
            def exists(self):
                return wire[0] is not None if self.value.endswith('.bin') else round(elapsed[0]*1000)>=publication_ms
            def read_bytes(self):return b'other request' if superseded and wire[0] else wire[0]

        def capture(*args,**kwargs):
            captures[0]+=1
            if captures[0]>1 and round(elapsed[0]*1000)<sdk_ready_ms:
                raise self.api.state.SnapshotError(sdk_error)
            result=snapshot();result['captured_tick_ms']=NOW+round(elapsed[0]*1000)
            if captures[0]>1:
                result['town'].update(town_id=2,area_id=2,position=dict(x=500 if wrong_position else 575,y=247,z=0),
                                      direction_raw=dict(x=0 if wrong_direction else 4,y=4))
                result['me']['position']=deepcopy(result['town']['position'])
                if sdk_capture is not None:result['captured_tick_ms']=sdk_capture
                if changed_identity:result['me']['actor_token']='0x19999990'
                if changed_context:result['town']['context_token']='0x19999990'
                result['players']=[deepcopy(result['me'])]
            return result

        def result(path):
            request=self.api.parse_request(wire[0])
            return dict(schema='dfl-client-route/v1',pid=PID,process_started_filetime=START,
                uid=1,request_id=request['request_id'],source='debug',status=status,reason='retained_terminal',tick_ms=NOW+40,
                goal={k:request[k] for k in ('town_id','area_id','x','y')},
                origin=dict(generation=request['generation'],**{k:f"0x{request[k]:08X}" for k in
                    ('actor_token','map_token','user_token','context_token')}),
                observed=dict(town_id=2,area_id=2,position=dict(x=575,y=247),direction_raw=dict(x=4,y=4)))

        def write(path,raw):wire[0]=raw;writes[0]+=1
        with ExitStack() as stack:
            for target,name,value in (
                (self.api,'Path',FakePath),(self.api.time,'monotonic',lambda:elapsed[0]),
                (self.api.time,'time_ns',lambda:50),
                (self.api.time,'sleep',lambda seconds:elapsed.__setitem__(0,elapsed[0]+seconds)),
                (self.api.state,'now_tick_ms',lambda:NOW+round(elapsed[0]*1000)),
                (self.api.state,'get_process_started_filetime',lambda pid:START),
                (self.api.state,'load_snapshot',capture),(self.api.nav,'_controller_lock',lambda path:nullcontext()),
                (self.api.nav,'_atomic_write',write),(self.api.nav,'_read_json',result)):
                stack.enter_context(patch.object(target,name,value))
            value=self.api.execute('MOCK_ONLY',pid=PID,uid=1,started=START,town_id=2,area_id=2,
                                   x=575,y=247,timeout_seconds=2)
        return value,dict(writes=writes[0],captures=captures[0],elapsed_ms=round(elapsed[0]*1000))

    def test_three_late_published_terminals_preserve_outcome_without_resend(self):
        for status in ('arrived','rejected','interrupted'):
            with self.subTest(status=status):
                value,counters=self.execute_terminal(status)
                self.assertEqual(value['status'],status)
                self.assertEqual(value['native']['reason'],'retained_terminal')
                self.assertEqual(counters['writes'],1)
                self.assertEqual(value['ok'],status=='arrived')

    def test_terminal_does_not_expire_while_waiting_for_fresh_sdk(self):
        for code in ('unreadable_json','stale','invalid_capture'):
            with self.subTest(code=code):
                value,counters=self.execute_terminal('arrived',publication_ms=80,sdk_ready_ms=1240,sdk_error=code)
                self.assertEqual(value['status'],'arrived')
                self.assertGreater(counters['captures'],2);self.assertEqual(counters['writes'],1)

    def test_delayed_arrival_cannot_bypass_current_sdk_position_or_identity(self):
        for change,reason in ((dict(wrong_position=True),'arrival_unproven'),(dict(wrong_direction=True),'arrival_unproven'),
                              (dict(changed_identity=True),'identity_changed'),(dict(changed_context=True),'identity_changed')):
            with self.subTest(change=change),self.assertRaisesRegex(self.api.nav.NavigationError,reason):
                self.execute_terminal('arrived',**change)

    def test_equal_tick_sdk_cannot_prove_arrival_and_wait_stays_bounded(self):
        value,counters=self.execute_terminal('arrived',sdk_capture=NOW+40)
        self.assertEqual(value['status'],'timeout');self.assertEqual(counters['writes'],1)
        self.assertEqual(counters['elapsed_ms'],2000)

    def test_terminal_with_unavailable_sdk_times_out_without_resend(self):
        value,counters=self.execute_terminal('arrived',publication_ms=80,sdk_ready_ms=5000,sdk_error='stale')
        self.assertEqual(value['status'],'timeout');self.assertEqual(counters['writes'],1)
        self.assertEqual(counters['elapsed_ms'],2000)

    def test_superseded_command_still_fails_before_terminal_use(self):
        with self.assertRaisesRegex(self.api.nav.NavigationError,'superseded'):
            self.execute_terminal('arrived',superseded=True)
