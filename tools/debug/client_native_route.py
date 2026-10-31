"""Set one final destination in the client DLL's native Town route controller."""
from pathlib import Path
import argparse
import struct
import time
try:
    from . import client_navigation as nav
    from . import client_state as state
except ImportError:
    import client_navigation as nav
    import client_state as state


def require(value, code):
    if not value: raise nav.NavigationError(code)


def make_request(snapshot, *, town_id, area_id, x, y, request_id, issued_tick_ms):
    require(all(state._integer(v,0,255) for v in (town_id,area_id)), 'invalid_target')
    raw=bytearray(nav.make_request(snapshot,x=x,y=y,request_id=request_id,issued_tick_ms=issued_tick_ms))
    raw[:4]=b'DLRT'
    struct.pack_into('<ii',raw,80,town_id,area_id)
    return bytes(raw)


def parse_request(raw):
    require(isinstance(raw,bytes) and len(raw)==96 and raw[:4]==b'DLRT','invalid_request')
    legacy=bytearray(raw);legacy[:4]=b'DLNV'
    return nav.parse_request(bytes(legacy))


def validate_response(value, request, now_tick_ms):
    require(isinstance(value,dict) and value.get('schema')=='dfl-client-route/v1','bad_response')
    for key in ('pid','process_started_filetime','request_id','uid'):
        require(state._integer(value.get(key),1) and value[key]==request[key],'response_binding_mismatch')
    require(value.get('source')=='debug','superseded')
    require(state._integer(value.get('tick_ms')) and value['tick_ms']<=now_tick_ms,'future_response')
    require(value.get('status') in ('requested','queued','awaiting_confirmation','moving','waiting_data',
                                  'waiting_fresh_sample','waiting_transition','arrived','rejected','interrupted'),'bad_response')
    # Terminal event ticks remain fixed while native IO publication retries.
    # Their exact per-request binding is durable during execute's bounded wait;
    # arrival still needs the independent fresh SDK proof below.
    require(value['tick_ms']>=request['issued_tick_ms'] and
            (value['status'] in ('arrived','rejected','interrupted') or now_tick_ms-value['tick_ms']<=1000),'stale_response')
    require(isinstance(value.get('reason'),str),'bad_response')
    origin=value.get('origin');goal=value.get('goal')
    require(isinstance(origin,dict) and origin.get('generation')==request['generation'],'response_binding_mismatch')
    for key in ('actor_token','map_token','user_token','context_token'):
        require(nav._token(origin.get(key)) and int(origin[key],16)==request[key],'response_binding_mismatch')
    require(isinstance(goal,dict) and all(type(goal.get(k)) is int and goal[k]==request[k]
                                        for k in ('town_id','area_id','x','y')),'response_binding_mismatch')
    if value['status']=='arrived':
        observed=value.get('observed')
        require(isinstance(observed,dict) and all(observed.get(k)==request[k] for k in ('town_id','area_id')),'arrival_unproven')
        point=observed.get('position')
        require(isinstance(point,dict) and all(state._finite(point.get(k)) and abs(point[k]-request[k])<=3 for k in ('x','y'))
                and observed.get('direction_raw')==dict(x=4,y=4),'arrival_unproven')
    return value


def execute(directory, *, pid, uid, started, town_id, area_id, x, y, timeout_seconds=150):
    """One DLRT command, no Python waypoint loop or accepted-request resend."""
    directory=Path(directory)
    require(directory.is_dir(),'directory_missing')
    require(state._finite(timeout_seconds) and 0<timeout_seconds<=300,'bad_timeout')
    def identity():
        require(state.get_process_started_filetime(pid)==started,'process_start_mismatch')
    def snapshot():
        identity()
        sample=state.load_snapshot(directory/f'dfdebug-client-{pid}.json',pid=pid,process_started_filetime=started,
                                   now_tick_ms=state.now_tick_ms())
        nav._checked_snapshot(sample)
        require(sample['me']['uid']==uid,'uid_mismatch')
        return sample
    command=directory/f'dfdebug-route-{pid}.bin'
    deadline=time.monotonic()+timeout_seconds
    with nav._controller_lock(directory/f'dfdebug-navigation-{pid}.lock'):
        current=snapshot()
        highest=0
        if command.exists():highest=parse_request(command.read_bytes())['request_id']
        raw=make_request(current,town_id=town_id,area_id=area_id,x=x,y=y,request_id=max(time.time_ns(),highest+1),
                         issued_tick_ms=state.now_tick_ms())
        request=parse_request(raw)
        result_path=directory/f"dfdebug-route-{pid}-{request['request_id']}.result.json"
        identity();nav._atomic_write(command,raw)
        while time.monotonic()<deadline:
            identity()
            require(command.read_bytes()==raw,'superseded')
            if not result_path.exists():time.sleep(.04);continue
            try:value=validate_response(nav._read_json(result_path),request,state.now_tick_ms())
            except nav.NavigationError as error:
                if error.code not in ('stale_response','unreadable_response'):raise
                time.sleep(.04);continue
            status=value['status']
            if status in ('rejected','interrupted'):
                return dict(ok=False,status=status,reason=value['reason'],request_id=request['request_id'],native=value)
            if status=='arrived':
                try:final=snapshot()
                except (nav.NavigationError,state.SnapshotError) as error:
                    if error.code not in ('stale','invalid_capture','unreadable_json'):raise
                    time.sleep(.04);continue
                require(all(final['me'][k]==current['me'][k] for k in ('user_token','actor_token','uid'))
                        and final['town']['context_token']==current['town']['context_token'],'identity_changed')
                if final['captured_tick_ms']<=value['tick_ms']:
                    time.sleep(.04);continue
                town=final['town']
                require(town['town_id']==town_id and town['area_id']==area_id
                        and all(abs(town['position'][k]-request[k])<=3 for k in ('x','y'))
                        and town['direction_raw']==dict(x=4,y=4),'arrival_unproven')
                return dict(ok=True,status='arrived',reason='native_and_sdk_final_position',request_id=request['request_id'],native=value,observed=town)
            time.sleep(.04)
        return dict(ok=False,status='timeout',reason='native_route_timeout',request_id=request['request_id'])


def main(argv=None):
    import json
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory',type=Path,required=True);parser.add_argument('--pid',type=int,required=True)
    parser.add_argument('--uid',type=int,required=True);parser.add_argument('--process-started-filetime',type=int)
    for name in ('town','area','x','y'):parser.add_argument('--'+name,type=int,required=True)
    parser.add_argument('--timeout-seconds',type=float,default=150)
    args=parser.parse_args(argv)
    try:
        started=args.process_started_filetime or state.get_process_started_filetime(args.pid)
        result=execute(args.directory,pid=args.pid,uid=args.uid,started=started,town_id=args.town,area_id=args.area,
                       x=args.x,y=args.y,timeout_seconds=args.timeout_seconds)
    except (nav.NavigationError,state.SnapshotError,OSError,ValueError) as error:
        result=dict(ok=False,status='rejected',reason=getattr(error,'code',str(error)))
    print(json.dumps(result,ensure_ascii=False));return 0 if result['ok'] else 2


if __name__=='__main__':raise SystemExit(main())
