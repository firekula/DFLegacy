import importlib
import unittest
from tools.debug.test_client_route import snapshot
from tools.debug.test_client_state import NOW


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
