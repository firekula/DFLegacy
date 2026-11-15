"""Read passive native CRC-passed application observations; never send input."""
from __future__ import annotations
import json
import argparse
import re
from collections import Counter
from pathlib import Path
from . import client_state as state
from tools.acceptance.act1_gameplay_evidence import decode_application

MAX_BYTES=1024*1024

class TraceError(ValueError):
    def __init__(self,code):self.code=code;super().__init__(code)

def validate_trace(value,*,pid,process_started_filetime,now_tick_ms):
    def require(b,code):
        if not b:raise TraceError(code)
    require(isinstance(value,dict) and value.get('schema') in ('dfl-native-gameplay-trace/v1','dfl-native-gameplay-trace/v2'),'bad_schema')
    version2=value['schema']=='dfl-native-gameplay-trace/v2'
    require(state._integer(pid,1,0xffffffff) and state._integer(process_started_filetime,1)
        and state._integer(value.get('pid'),1,0xffffffff) and value['pid']==pid
        and state._integer(value.get('process_started_filetime'),1)
        and value['process_started_filetime']==process_started_filetime,'identity_mismatch')
    require(state._integer(now_tick_ms) and state._integer(value.get('exported_tick_ms'),1)
        and value['exported_tick_ms']<=now_tick_ms and now_tick_ms-value['exported_tick_ms']<=1500,'stale_trace')
    require(all(state._integer(value.get(k)) for k in ('serial','capacity_overwrites','lock_drops','invalid_records'))
        and value.get('capacity')==64 and value['capacity_overwrites']==max(0,value['serial']-64),'bad_counters')
    rows=value.get('events');require(isinstance(rows,list) and len(rows)==min(64,value['serial']),'bad_ring')
    require(isinstance(value.get('source'),str) and 0<len(value['source'])<=512,'bad_source')
    previous_tick=previous_counter=0
    for serial,e in enumerate(rows,value['serial']-len(rows)+1):
        require(isinstance(e,dict) and state._integer(e.get('serial'),1) and e['serial']==serial,'bad_event_serial')
        require(state._integer(e.get('tick_ms'),1) and previous_tick<=e['tick_ms']<=value['exported_tick_ms']
            and state._integer(e.get('counter_us'),1) and previous_counter<=e['counter_us'],'bad_event_clock')
        previous_tick=e['tick_ms'];previous_counter=e['counter_us']
        require(all(state._integer(e.get(k),1,0xffffffff) for k in ('thread','context_token')),'bad_native_context')
        kind=e.get('event_kind','accepted');require(kind in ('accepted','gp4_movement_entered'),'bad_event_kind')
        if version2:
            require('event_kind' in e and state._integer(e.get('frame_token'),0,0xffffffff),'bad_native_frame')
            if kind=='accepted':require(e.get('native_gate') is None,'unexpected_gate')
        else:require(kind=='accepted','bad_event_kind')
        if kind=='gp4_movement_entered':
            gate=e.get('native_gate');require(version2 and isinstance(gate,dict) and e['frame_token']>=0x10000,'bad_native_gate')
            require(all(state._integer(gate.get(k),0x10000,0xffffffff) for k in ('actor_token','controller_token'))
                and all(state._integer(gate.get(k),0,65535) for k in ('object_kind','object_uid','player_kind','player_uid'))
                and all(state._integer(gate.get(k),0,255) for k in ('room_x','room_y'))
                and state._integer(gate.get('movement_flags'),0,0xffffffff),'bad_native_gate')
            require(e.get('length')==0 and e.get('captured_bytes')==0 and e.get('hex')==''
                and e.get('buffer_token')==0 and e.get('truncated') is False
                and e.get('provider_buffer_match') is False,'unexpected_gate_payload')
            continue
        require(state._integer(e.get('buffer_token'),1,0xffffffff),'bad_native_context')
        require(state._integer(e.get('length'),7,0x100000) and state._integer(e.get('captured_bytes'),7,2048)
            and e['captured_bytes']==min(e['length'],2048) and type(e.get('truncated')) is bool
            and e['truncated']==(e['length']>e['captured_bytes']) and type(e.get('provider_buffer_match')) is bool,'bad_capture_bound')
        raw=e.get('hex');require(isinstance(raw,str) and len(raw)==e['captured_bytes']*2
            and re.fullmatch('[0-9a-f]+',raw) is not None,'bad_hex')
        data=bytes.fromhex(raw);require(int.from_bytes(data[1:3],'little')<=41,'bad_opcode')
    return value

def load_trace(directory,*,pid,process_started_filetime,now_tick_ms=None):
    if not state._integer(pid,1,0xffffffff):raise TraceError('bad_pid')
    try:
        with (Path(directory)/f'dfdebug-gameplay-trace-{pid}.json').open('rb') as f:raw=f.read(MAX_BYTES+1)
        if len(raw)>MAX_BYTES:raise TraceError('oversize_trace')
        value=json.loads(raw,object_pairs_hook=state._pairs,parse_constant=state._constant)
    except (OSError,ValueError,RecursionError) as error:
        if isinstance(error,TraceError):raise
        raise TraceError('unreadable_trace') from error
    return validate_trace(value,pid=pid,process_started_filetime=process_started_filetime,
        now_tick_ms=state.now_tick_ms() if now_tick_ms is None else now_tick_ms)

def summarize(value):
    known=Counter();unknown=truncated=buffer_match=gates=0
    for e in value['events']:
        if e.get('event_kind')=='gp4_movement_entered':gates+=1;continue
        buffer_match+=e['provider_buffer_match']
        if e['truncated']:truncated+=1;continue
        try:app=decode_application(bytes.fromhex(e['hex']))
        except ValueError:unknown+=1;continue
        if app.crc_valid:known[app.protocol]+=1
        else:unknown+=1
    return dict(ok=True,status='observed',pid=value['pid'],serial=value['serial'],retained_events=len(value['events']),
        known_protocols=dict(known),crc_unknown=unknown,truncated_events=truncated,provider_buffer_matches=buffer_match,
        lock_drops=value['lock_drops'],invalid_records=value['invalid_records'],capacity_overwrites=value['capacity_overwrites'],
        movement_gate_events=gates,movement_application='completion_not_observed',provider_branch='not_observed')

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--directory',type=Path,required=True)
    p.add_argument('--pid',type=int,required=True);p.add_argument('--process-started-filetime',type=int);a=p.parse_args(argv)
    try:
        if not state._integer(a.pid,1,0xffffffff):raise TraceError('bad_pid')
        started=a.process_started_filetime if a.process_started_filetime is not None else state.get_process_started_filetime(a.pid)
        print(json.dumps(summarize(load_trace(a.directory,pid=a.pid,process_started_filetime=started))));return 0
    except (TraceError,state.SnapshotError) as e:
        print(json.dumps(dict(ok=False,status='invalid',error=e.code)));return 1

if __name__=='__main__':raise SystemExit(main())
