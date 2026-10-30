"""Read bounded, process-bound debug callback measurements; never read game memory."""
from __future__ import annotations
import argparse
import json
from pathlib import Path

from . import client_state as state

PARTS=['state_capture','navigation','party','test_control','overlay_and_content_publish']
PIPELINE_PARTS=['input_and_original_overlay','text_setup','debug_module','fps_and_memory_format','text_draw']
COUNTERS=('frames','foreground_frames','unknown_foreground_frames','background_frames',
    'foreground_transition_frames','foreground_intervals','clock_errors','foreground_hook_over15ms',
    'foreground_hook_over50ms','foreground_intervals_over50ms')
MAX_BYTES=262144

class ProfileError(ValueError):
    def __init__(self,code):self.code=code;super().__init__(code)

def require(condition,code):
    if not condition:raise ProfileError(code)

def stats(s):
    require(isinstance(s,dict) and all(state._integer(s.get(k)) for k in COUNTERS),'bad_statistics')
    require(s['frames']==sum(s[k] for k in ('foreground_frames','unknown_foreground_frames',
        'background_frames','foreground_transition_frames')),'bad_foreground_coverage')
    require(s['foreground_intervals']<=s['foreground_frames'] and s['foreground_hook_over50ms']
        <=s['foreground_hook_over15ms']<=s['foreground_frames'] and s['foreground_intervals_over50ms']
        <=s['foreground_intervals'],'bad_threshold_counts')
    for key,count in (('max_hook_us','frames'),('max_foreground_hook_us','foreground_frames'),
        ('max_foreground_interval_us','foreground_intervals')):
        require(key in s and (state._integer(s.get(key)) if s[count] else s.get(key) is None),'unknown_measurement')
    parts=s.get('maximum_parts_us')
    require(isinstance(parts,list) and len(parts)==5 and all(
        state._integer(p) and p<=s['max_hook_us'] if s['frames'] else p is None for p in parts),'bad_component_times')
    require(not s['foreground_frames'] or s['max_foreground_hook_us']<=s['max_hook_us'],'bad_foreground_maximum')
    require(bool(s['foreground_hook_over15ms'])==bool(s['foreground_frames'] and s['max_foreground_hook_us']>15000)
        and bool(s['foreground_hook_over50ms'])==bool(s['foreground_frames'] and s['max_foreground_hook_us']>50000)
        and bool(s['foreground_intervals_over50ms'])==bool(s['foreground_intervals'] and s['max_foreground_interval_us']>50000),
        'inconsistent_thresholds')
    return s

def validate_profile(value,*,pid,process_started_filetime,now_tick_ms,pipeline=False):
    require(isinstance(value,dict) and value.get('schema')==('dfl-client-frame-pipeline/v1' if pipeline else 'dfl-client-frame-profile/v1'),'unsupported_schema')
    require(state._integer(pid,1,0xffffffff) and state._integer(process_started_filetime,1)
        and state._integer(value.get('pid'),1,0xffffffff) and value['pid']==pid
        and state._integer(value.get('process_started_filetime'),1)
        and value['process_started_filetime']==process_started_filetime,'identity_mismatch')
    require(all(state._integer(value.get(k),1) for k in ('serial','captured_tick_ms','exported_tick_ms'))
        and state._integer(now_tick_ms) and value['captured_tick_ms']<=value['exported_tick_ms']<=now_tick_ms
        and now_tick_ms-value['captured_tick_ms']<=1500,'stale_profile')
    require(state._integer(value.get('skipped_publications')) and state._integer(value.get('missing_buckets'))
        and state._integer(value.get('unattributed_clock_errors'))
        and value.get('parts')==(PIPELINE_PARTS if pipeline else PARTS),'bad_header')
    total=stats(value.get('total'));require(total['frames']>0,'no_valid_frames')
    require(value['serial']==total['frames']+total['clock_errors']
        and value['unattributed_clock_errors']<=total['clock_errors'],'inconsistent_serial')
    completed=value.get('completed_windows');require(isinstance(completed,list) and len(completed)<=8,'window_bound')
    current=value.get('current_window');require(isinstance(current,dict),'bad_current_window')
    previous=-1000;retained={key:0 for key in COUNTERS}
    for w in completed+[current]:
        require(isinstance(w,dict) and state._integer(w.get('start_tick_ms')) and state._integer(w.get('last_tick_ms'))
            and w['start_tick_ms']%1000==0 and previous+1000<=w['start_tick_ms']
            and w['start_tick_ms']<=w['last_tick_ms']<w['start_tick_ms']+1000
            and w['last_tick_ms']<=value['captured_tick_ms'],'bad_window_order')
        previous=w['start_tick_ms'];s=stats(w.get('statistics'))
        require(s['frames']+s['clock_errors']>0,'empty_retained_window')
        for key in COUNTERS:retained[key]+=s[key]
        for key in ('max_hook_us','max_foreground_hook_us','max_foreground_interval_us'):
            require(s[key] is None or total[key] is not None and s[key]<=total[key],'window_exceeds_total')
        require(all(p is None or p<=maximum for p,maximum in zip(s['maximum_parts_us'],total['maximum_parts_us'])),
            'window_component_exceeds_total')
    require(all(retained[k]<=total[k] for k in COUNTERS)
        and retained['clock_errors']+value['unattributed_clock_errors']<=total['clock_errors']
        and current['last_tick_ms']==value['captured_tick_ms'],'bad_frame_coverage')
    events=value.get('slow_events');require(isinstance(events,list) and len(events)<=32,'event_bound')
    previous_serial=0;previous_tick=0;hook15=hook50=gap50=0
    for e in events:
        require(isinstance(e,dict) and state._integer(e.get('serial'),previous_serial+1,value['serial'])
            and state._integer(e.get('tick_ms'),previous_tick,value['captured_tick_ms'])
            and state._integer(e.get('scene'),0,0xffffffff) and state._integer(e.get('hook_us'))
            and all(type(e.get(k)) is bool for k in ('interval_known','foreground_known','foreground_begin','foreground_end')),
            'bad_event')
        previous_serial=e['serial'];previous_tick=e['tick_ms'];gap=e.get('foreground_interval_us')
        require('foreground_interval_us' in e and (state._integer(gap) if e['interval_known'] else gap is None),'unknown_event_interval')
        fg=e['foreground_known'] and e['foreground_begin'] and e['foreground_end']
        require(not e['interval_known'] or fg,'unproven_interval_foreground')
        require((fg and e['hook_us']>15000) or (e['interval_known'] and gap>50000),'unproven_slow_event')
        parts=e.get('parts_us');require(isinstance(parts,list) and len(parts)==5
            and all(state._integer(p) for p in parts) and sum(parts)==e['hook_us'],'bad_event_components')
        require(e['hook_us']<=total['max_hook_us'] and (not fg or total['max_foreground_hook_us'] is not None
            and e['hook_us']<=total['max_foreground_hook_us']) and (not e['interval_known']
            or total['max_foreground_interval_us'] is not None and gap<=total['max_foreground_interval_us'])
            and all(p<=maximum for p,maximum in zip(parts,total['maximum_parts_us'])),'event_exceeds_total')
        hook15+=fg and e['hook_us']>15000;hook50+=fg and e['hook_us']>50000;gap50+=e['interval_known'] and gap>50000
    require(hook15<=total['foreground_hook_over15ms'] and hook50<=total['foreground_hook_over50ms']
        and gap50<=total['foreground_intervals_over50ms'],'event_count_exceeds_total')
    return value

def summarize(value):
    s=value['total']
    return dict(ok=True,status='observed',pid=value['pid'],serial=value['serial'],frames=s['frames'],
        foreground_sampled=s['foreground_frames']>0,foreground_frames=s['foreground_frames'],
        unknown_foreground_frames=s['unknown_foreground_frames'],clock_errors=s['clock_errors'],
        max_foreground_hook_us=s['max_foreground_hook_us'],max_foreground_interval_us=s['max_foreground_interval_us'],
        maximum_parts_us=dict(zip(value['parts'],s['maximum_parts_us'])),
        foreground_hook_over50ms=s['foreground_hook_over50ms'],foreground_intervals_over50ms=s['foreground_intervals_over50ms'],
        unattributed_clock_errors=value['unattributed_clock_errors'],
        missing_buckets=value['missing_buckets'],retained_completed_windows=len(value['completed_windows']),
        scope=('IJL performance-overlay call including DebugRender; native work outside this call and profiler overhead excluded. Invocation intervals are not GPU Present or continuous foreground proof.'
            if value['schema']=='dfl-client-frame-pipeline/v1' else
            'Existing DebugRender workload; profiler bookkeeping/export excluded. Invocation intervals are not GPU Present or continuous foreground proof.'))

def load_profile(directory,*,pid,process_started_filetime,now_tick_ms=None,pipeline=False):
    require(state._integer(pid,1,0xffffffff),'bad_pid')
    path=Path(directory)/f'dfdebug-frame-{"pipeline" if pipeline else "profile"}-{pid}.json'
    try:
        with path.open('rb') as f:raw=f.read(MAX_BYTES+1)
        require(len(raw)<=MAX_BYTES,'oversize_profile')
        value=json.loads(raw,object_pairs_hook=state._pairs,parse_constant=state._constant)
    except (OSError,ValueError,RecursionError) as error:
        if isinstance(error,ProfileError):raise
        raise ProfileError('unreadable_profile') from error
    return validate_profile(value,pid=pid,process_started_filetime=process_started_filetime,
        now_tick_ms=state.now_tick_ms() if now_tick_ms is None else now_tick_ms,pipeline=pipeline)

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',type=Path,required=True)
    p.add_argument('--pid',type=int,required=True);p.add_argument('--process-started-filetime',type=int)
    p.add_argument('--pipeline',action='store_true',help='Read optional IJL outer-call timings')
    args=p.parse_args(argv)
    try:
        require(state._integer(args.pid,1,0xffffffff),'bad_pid')
        if args.process_started_filetime is None:started=state.get_process_started_filetime(args.pid)
        else:
            require(state._integer(args.process_started_filetime,1),'bad_process_start')
            started=args.process_started_filetime
        value=load_profile(args.directory,pid=args.pid,process_started_filetime=started,pipeline=args.pipeline)
        print(json.dumps(summarize(value)));return 0
    except (ProfileError,state.SnapshotError) as error:
        print(json.dumps(dict(ok=False,status='invalid',error=error.code)));return 1

if __name__=='__main__':raise SystemExit(main())
