"""Read default-off native ACT1 loop segments; never send game input."""
import argparse
import json
from pathlib import Path
from . import client_state as state

PARTS=('critical_sections_and_pre_update','network_dispatch','ui_78C9E0','scene_79CE00','post_scene','pacing_433660','tail_792A70')
MAX_BYTES=1024*1024

class LoopError(ValueError):
    def __init__(self,code):self.code=code;super().__init__(code)

def validate_profile(value,*,pid,process_started_filetime,now_tick_ms):
    def require(ok,code='bad_profile'):
        if not ok:raise LoopError(code)
    def integer(n,low=0,high=0xffffffffffffffff):return state._integer(n,low,high)
    def array(v,known=True):return isinstance(v,list) and len(v)==7 and all(integer(n) if known else n is None for n in v)
    def stats(s):
        counts=('frames','intervals','cpu_frames','cpu_intervals','foreground_frames','background_frames','unknown_foreground_frames','foreground_transition_frames','loops_over15ms','outside_over50ms')
        require(isinstance(s,dict) and all(integer(s.get(k)) for k in counts),'bad_statistics')
        require(s['foreground_frames']+s['background_frames']+s['unknown_foreground_frames']+s['foreground_transition_frames']==s['frames'],'bad_statistics')
        require(s['intervals']<=s['frames'] and s['cpu_frames']<=s['frames'] and s['cpu_intervals']<=s['intervals']
            and s['cpu_intervals']<=s['cpu_frames'] and s['loops_over15ms']<=s['frames'] and s['outside_over50ms']<=s['intervals'],'bad_statistics')
        for key,count in [('max_loop_us','frames'),('max_interval_us','intervals'),('max_outside_us','intervals'),('max_cpu_loop_us','cpu_frames'),('max_cpu_outside_us','cpu_intervals')]:
            require(key in s and (integer(s[key]) if s[count] else s[key] is None),'bad_statistics')
        require(array(s.get('maximum_sections_us'),bool(s['frames'])) and array(s.get('maximum_cpu_sections_us'),bool(s['cpu_frames'])),'bad_sections')
    def event(e,event_id):
        require(isinstance(e,dict) and integer(e.get('event_id')) and e['event_id']==event_id,'bad_event_id')
        require(all(integer(e.get(k),1) for k in ('sequence','thread','begin_tick_ms','tick_ms'))
            and e['begin_tick_ms']<=e['tick_ms']<=value['exported_tick_ms']
            and all(integer(e.get(k),0,0xffffffff) for k in ('scene_begin','scene_end')),'bad_event_clock')
        require(all(type(e.get(k)) is bool for k in ('foreground_known','foreground_begin','foreground_end','cpu_known','interval_known','cpu_outside_known')),'bad_event_flags')
        require(integer(e.get('loop_us')) and array(e.get('sections_us')) and sum(e['sections_us'])==e['loop_us'],'bad_sections')
        if e['cpu_known']:
            require(integer(e.get('cpu_loop_us')) and array(e.get('cpu_sections_us')) and sum(e['cpu_sections_us'])==e['cpu_loop_us'],'bad_cpu')
        else:require(e.get('cpu_loop_us') is None and e.get('cpu_sections_us') is None,'bad_cpu')
        if e['interval_known']:
            require(all(integer(e.get(k)) for k in ('interval_us','outside_us','previous_loop_us','previous_sequence'))
                and e['previous_sequence']+1==e['sequence'] and e['interval_us']==e['previous_loop_us']+e['outside_us']
                and array(e.get('previous_sections_us')) and sum(e['previous_sections_us'])==e['previous_loop_us'],'bad_interval')
            require(e.get('previous_cpu_loop_us') is None or integer(e['previous_cpu_loop_us']),'bad_cpu')
        else:require(all(e.get(k) is None for k in ('interval_us','outside_us','previous_loop_us','previous_sequence','previous_sections_us','previous_cpu_loop_us')),'bad_interval')
        if e['cpu_outside_known']:
            require(e['interval_known'] and e['cpu_known'] and integer(e.get('cpu_outside_us')) and integer(e.get('previous_cpu_loop_us')),'bad_cpu')
        else:require(e.get('cpu_outside_us') is None,'bad_cpu')
    require(isinstance(value,dict) and value.get('schema')=='dfl-native-loop-profile/v1','bad_schema')
    require(integer(pid,1,0xffffffff) and integer(process_started_filetime,1) and integer(value.get('pid'),1,0xffffffff)
        and value['pid']==pid and integer(value.get('process_started_filetime'),1) and value['process_started_filetime']==process_started_filetime,'identity_mismatch')
    require(integer(now_tick_ms) and integer(value.get('exported_tick_ms'),1) and value['exported_tick_ms']<=now_tick_ms
        and now_tick_ms-value['exported_tick_ms']<=1500,'stale_worker')
    require(isinstance(value.get('parts'),list) and tuple(value['parts'])==PARTS and type(value.get('capacity')) is int and value['capacity']==32,'bad_parts')
    require(all(integer(value.get(k)) for k in ('captured_tick_ms','received','invalid_records','nested_records','sequence_discontinuities','lock_drops','slow_events_total')),'bad_counters')
    stats(value.get('total'));total=value['total']
    require(value['received']==total['frames']+value['invalid_records'] and value['nested_records']<=value['invalid_records']
        and value['slow_events_total']<=total['frames'] and value['sequence_discontinuities']<=total['frames']
        and value['captured_tick_ms']<=value['exported_tick_ms'],'bad_counters')
    windows=value.get('completed_windows');require(isinstance(windows,list) and len(windows)<=8,'bad_windows')
    current=value.get('current_window');require(isinstance(current,dict),'bad_windows')
    previous_start=-1;count=0
    for w in windows+[current]:
        require(isinstance(w,dict) and integer(w.get('start_tick_ms')) and integer(w.get('last_tick_ms')),'bad_windows')
        stats(w.get('statistics'));count+=w['statistics']['frames']
        if w['statistics']['frames']:
            require(w['start_tick_ms']%1000==0 and previous_start<w['start_tick_ms']<=w['last_tick_ms']<=value['captured_tick_ms'],'bad_windows');previous_start=w['start_tick_ms']
    require(count<=total['frames'],'bad_windows')
    last=value.get('last_sample')
    if total['frames']:
        event(last,0);require(last['tick_ms']==value['captured_tick_ms']==current['last_tick_ms'],'bad_capture_clock')
    else:require(last is None and value['captured_tick_ms']==0,'bad_capture_clock')
    events=value.get('slow_events');require(isinstance(events,list) and len(events)==min(32,value['slow_events_total']),'bad_ring')
    previous_sequence=previous_tick=0
    for i,e in enumerate(events,value['slow_events_total']-len(events)+1):
        event(e,i);require(previous_sequence<e['sequence'] and previous_tick<=e['tick_ms']<=value['captured_tick_ms'],'bad_event_clock')
        previous_sequence=e['sequence'];previous_tick=e['tick_ms']
    require(isinstance(value.get('scope'),str) and 0<len(value['scope'])<=1024,'bad_scope')
    return value

def load_profile(directory,*,pid,process_started_filetime,now_tick_ms=None):
    if not state._integer(pid,1,0xffffffff):raise LoopError('bad_pid')
    try:
        with (Path(directory)/f'dfdebug-native-loop-{pid}.json').open('rb') as f:raw=f.read(MAX_BYTES+1)
        if len(raw)>MAX_BYTES:raise LoopError('oversize_profile')
        v=json.loads(raw,object_pairs_hook=state._pairs,parse_constant=state._constant)
    except (OSError,ValueError,RecursionError) as e:
        if isinstance(e,LoopError):raise
        raise LoopError('unreadable_profile') from e
    return validate_profile(v,pid=pid,process_started_filetime=process_started_filetime,now_tick_ms=state.now_tick_ms() if now_tick_ms is None else now_tick_ms)

def summarize(value,now_tick_ms):
    last=value['last_sample'];t=value['total']
    return dict(ok=True,status='observed' if last else 'waiting_for_loop',pid=value['pid'],frames=t['frames'],
        last_sample_age_ms=now_tick_ms-value['captured_tick_ms'] if last else None,
        max_loop_us=t['max_loop_us'],max_outside_us=t['max_outside_us'],max_cpu_loop_us=t['max_cpu_loop_us'],
        maximum_sections_us=dict(zip(PARTS,t['maximum_sections_us'])),slow_events_total=value['slow_events_total'],
        invalid_records=value['invalid_records'],lock_drops=value['lock_drops'],sequence_discontinuities=value['sequence_discontinuities'],
        scope=value['scope'])

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',required=True);p.add_argument('--pid',required=True,type=int);a=p.parse_args(argv)
    try:
        started=state.get_process_started_filetime(a.pid)
        value=load_profile(a.directory,pid=a.pid,process_started_filetime=started)
        print(json.dumps(summarize(value,state.now_tick_ms())));return 0
    except (LoopError,state.SnapshotError) as e:print(json.dumps(dict(ok=False,error=e.code)));return 1

if __name__=='__main__':raise SystemExit(main())
