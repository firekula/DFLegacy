from copy import deepcopy
import json
import os
from pathlib import Path
import unittest
import tempfile
import io
from contextlib import redirect_stdout
from unittest.mock import patch

from tools.debug import frame_profile as api

def fixture():
    stats=dict(frames=1,foreground_frames=1,unknown_foreground_frames=0,background_frames=0,
        foreground_transition_frames=0,foreground_intervals=0,clock_errors=0,max_hook_us=20000,
        max_foreground_hook_us=20000,max_foreground_interval_us=None,foreground_hook_over15ms=1,
        foreground_hook_over50ms=0,foreground_intervals_over50ms=0,maximum_parts_us=[100,200,100,300,19300])
    return dict(schema='dfl-client-frame-profile/v1',pid=42,process_started_filetime=123,serial=1,
        captured_tick_ms=5000,exported_tick_ms=5002,skipped_publications=0,missing_buckets=0,unattributed_clock_errors=0,
        parts=['state_capture','navigation','party','test_control','overlay_and_content_publish'],
        total=stats,current_window=dict(start_tick_ms=5000,last_tick_ms=5000,statistics=deepcopy(stats)),
        completed_windows=[],slow_events=[dict(serial=1,tick_ms=5000,scene=1,hook_us=20000,
            foreground_interval_us=None,interval_known=False,foreground_known=True,
            foreground_begin=True,foreground_end=True,parts_us=[100,200,100,300,19300])],scope='Debug callback only')

class ProfileTests(unittest.TestCase):
    def validate(self,v):return api.validate_profile(v,pid=42,process_started_filetime=123,now_tick_ms=5003)

    def test_outer_pipeline_summary_preserves_measured_section_names(self):
        v=fixture();v['schema']='dfl-client-frame-pipeline/v1'
        v['parts']=['input_and_original_overlay','text_setup','debug_module','fps_and_memory_format','text_draw']
        summary=api.summarize(v)
        self.assertEqual(summary['maximum_parts_us'],dict(input_and_original_overlay=100,
            text_setup=200,debug_module=100,fps_and_memory_format=300,text_draw=19300))
        self.assertIn('IJL',summary['scope'])

    def test_known_hook_cost_and_unknown_interval_are_distinct(self):
        v=self.validate(fixture());s=api.summarize(v)
        self.assertTrue(s['foreground_sampled'])
        self.assertEqual(v['total']['max_foreground_hook_us'],20000)
        self.assertIsNone(v['slow_events'][0]['foreground_interval_us'])

    def test_identity_freshness_and_measurement_contradictions_are_rejected(self):
        for change in ('pid','start','future','stale','serial','bool_frames','coverage','unknown_interval_zero',
            'part','event_interval_zero','event_foreground_unknown','bad_counter','window_bound','duplicate_event'):
            v=fixture()
            if change=='pid':v['pid']=True
            if change=='start':v['process_started_filetime']=124
            if change=='future':v['captured_tick_ms']=6000
            if change=='stale':v['captured_tick_ms']=3000
            if change=='serial':v['serial']=0
            if change=='bool_frames':v['total']['frames']=True
            if change=='coverage':v['total']['background_frames']=1
            if change=='unknown_interval_zero':v['total']['max_foreground_interval_us']=0
            if change=='part':v['total']['maximum_parts_us'][0]=float('inf')
            if change=='event_interval_zero':v['slow_events'][0]['foreground_interval_us']=0
            if change=='event_foreground_unknown':v['slow_events'][0]['foreground_known']=False
            if change=='bad_counter':v['total']['foreground_hook_over50ms']=2
            if change=='window_bound':v['completed_windows']=[deepcopy(v['current_window'])]*9
            if change=='duplicate_event':v['slow_events']*=2
            with self.subTest(change=change),self.assertRaises(api.ProfileError):self.validate(v)

    def test_unknown_foreground_does_not_claim_observed_absence(self):
        v=fixture()
        for stats in (v['total'],v['current_window']['statistics']):
            stats.update(foreground_frames=0,unknown_foreground_frames=1,max_foreground_hook_us=None,
                foreground_hook_over15ms=0)
        v['slow_events']=[]
        self.assertFalse(api.summarize(self.validate(v))['foreground_sampled'])

    def test_retained_evidence_cannot_exceed_lifetime_summary(self):
        for change in ('lifetime','serial','event','component'):
            v=fixture()
            if change=='lifetime':v['total'].update(max_hook_us=1000,max_foreground_hook_us=1000,
                foreground_hook_over15ms=0,maximum_parts_us=[0,0,0,0,1000])
            if change=='serial':v['serial']=2
            if change=='event':v['slow_events'][0].update(hook_us=60000,parts_us=[100,200,100,300,59300])
            if change=='component':v['current_window']['statistics']['maximum_parts_us'][3]=500
            with self.subTest(change=change),self.assertRaises(api.ProfileError):self.validate(v)

    def test_real_native_export_roundtrip(self):
        from tools.acceptance.native_fixtures import fixture_path
        path=fixture_path('frame-profile')
        v=json.loads(Path(path).read_bytes())
        api.validate_profile(v,pid=v['pid'],process_started_filetime=v['process_started_filetime'],
            now_tick_ms=v['exported_tick_ms'])
        self.assertEqual(v['total']['unknown_foreground_frames'],1)
        self.assertIsNone(v['slow_events'][0]['foreground_interval_us'])

    def test_file_bound_and_duplicate_keys_are_rejected_before_use(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'dfdebug-frame-profile-42.json'
            path.write_text(json.dumps(fixture()),encoding='utf-8')
            self.assertEqual(api.load_profile(directory,pid=42,process_started_filetime=123,now_tick_ms=5003)['serial'],1)
            for content in (b'{"pid":42,"pid":43}',b' '* (api.MAX_BYTES+1),b'{"x":NaN}'):
                path.write_bytes(content)
                with self.subTest(content=content[:32]),self.assertRaises(api.ProfileError):
                    api.load_profile(directory,pid=42,process_started_filetime=123,now_tick_ms=5003)

    def test_cli_reports_observation_scope_and_refuses_invalid_pid_without_os_query(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'dfdebug-frame-profile-42.json';path.write_text(json.dumps(fixture()),encoding='utf-8')
            output=io.StringIO()
            with redirect_stdout(output),patch.object(api.state,'now_tick_ms',return_value=5003):
                result=api.main(['--directory',directory,'--pid','42','--process-started-filetime','123'])
            self.assertEqual(result,0);summary=json.loads(output.getvalue());self.assertEqual(summary['status'],'observed')
            self.assertIn('not GPU',summary['scope'])
            with redirect_stdout(io.StringIO()),patch.object(api.state,'get_process_started_filetime') as query:
                self.assertEqual(api.main(['--directory',directory,'--pid','0']),1);query.assert_not_called()

if __name__=='__main__':unittest.main()
