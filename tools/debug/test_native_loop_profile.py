import json
import os
from pathlib import Path
from copy import deepcopy
import unittest
import tempfile
import contextlib
import io
from unittest.mock import patch
from tools.debug import native_loop_profile as api

class NativeLoopProfileTests(unittest.TestCase):
    def fixture(self):
        from tools.acceptance.native_fixtures import fixture_path
        path=fixture_path('native-loop')
        return json.loads(Path(path).read_bytes())
    def validate(self,v,now=None):
        return api.validate_profile(v,pid=v['pid'],process_started_filetime=v['process_started_filetime'],now_tick_ms=v['exported_tick_ms']+1 if now is None else now)
    def test_actual_native_outside_cpu_and_sequence_gap(self):
        v=self.validate(self.fixture());e=v['slow_events'][0]
        self.assertEqual(e['outside_us'],2493000);self.assertEqual(e['cpu_outside_us'],300)
        self.assertEqual(sum(e['sections_us']),7000)
        self.assertFalse(v['last_sample']['cpu_known']);self.assertIsNone(v['last_sample']['cpu_loop_us'])
        self.assertFalse(v['last_sample']['interval_known']);self.assertEqual(v['sequence_discontinuities'],1)
    def test_stale_worker_rejected_but_old_main_loop_remains_observable(self):
        v=self.fixture();old=v['captured_tick_ms'];v['exported_tick_ms']=old+10000
        self.validate(v,old+10001)
        summary=api.summarize(v,old+10001)
        self.assertEqual(summary.get('last_sample_age_ms'),10001)
        with self.assertRaises(api.LoopError):self.validate(v,old+12000)
    def test_identity_counter_type_section_and_interval_corruption(self):
        for kind in ['identity','capacity','bool_counter','parts','event_id','section_sum','cpu_unknown','outside_sum','sequence','nested_count','clock']:
            v=deepcopy(self.fixture());e=v['slow_events'][0]
            if kind=='identity':v['pid']+=1
            if kind=='capacity':v['capacity']=32.0
            if kind=='bool_counter':v['lock_drops']=False
            if kind=='parts':v['parts'][0]='invented'
            if kind=='event_id':e['event_id']=99
            if kind=='section_sum':e['sections_us'][0]+=1
            if kind=='cpu_unknown':v['last_sample']['cpu_loop_us']=0
            if kind=='outside_sum':e['outside_us']+=1
            if kind=='sequence':e['previous_sequence']=e['sequence']
            if kind=='nested_count':v['nested_records']=v['invalid_records']+1
            if kind=='clock':v['exported_tick_ms']=v['captured_tick_ms']-1
            base=self.fixture()
            with self.subTest(kind=kind),self.assertRaises(api.LoopError):
                api.validate_profile(v,pid=base['pid'],process_started_filetime=base['process_started_filetime'],now_tick_ms=base['exported_tick_ms']+1)
    def test_cli_dates_a_new_atomic_publication_after_reading(self):
        v=self.fixture();clock={'now':v['exported_tick_ms']-1};original=Path.open
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/f"dfdebug-native-loop-{v['pid']}.json";path.write_text(json.dumps(v),encoding='utf-8')
            def open_fresh(p,*args,**kwargs):
                if p==path:clock['now']=v['exported_tick_ms']+1
                return original(p,*args,**kwargs)
            with patch.object(api.state,'now_tick_ms',side_effect=lambda:clock['now']),patch.object(api.state,'get_process_started_filetime',return_value=v['process_started_filetime']),patch.object(Path,'open',open_fresh),contextlib.redirect_stdout(io.StringIO()) as output:
                code=api.main(['--directory',directory,'--pid',str(v['pid'])])
            self.assertEqual(code,0,output.getvalue());self.assertTrue(json.loads(output.getvalue())['ok'])

if __name__=='__main__':unittest.main()
