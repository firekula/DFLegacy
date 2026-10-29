"""Explicitly enabled local test control. No screenshots or external memory writes.

Server commits are authoritative for loot; native acknowledgments and peer
observations are separate evidence. All mutation remains inside gated consumers.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import math
import os
from pathlib import Path
import struct
import time
import uuid

try:
    from . import client_state as state
except ImportError:
    import client_state as state

MAX_JSON_BYTES = 4_194_304
WIRE = struct.Struct('<4sHHIIQQQQQQIIIIiiiiQQ')
LEASE = struct.Struct('<IIQQQII')
FIELDS = ('pid','uid','process_started_filetime','request_id','issued_tick_ms',
    'snapshot_tick_ms','generation','snapshot_sequence','actor_token','map_token',
    'user_token','operation','target_id','x','y','reserved','session_nonce','run_id')
EQUIPMENT_SAMPLES = {31901,17242,31046,27546}


class TestControlError(ValueError):
    __test__ = False
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def require(ok, code):
    if not ok: raise TestControlError(code)


def integer(v, minimum=0, maximum=(1 << 64)-1):
    return type(v) is int and minimum <= v <= maximum


def filetime():
    return time.time_ns()//100 + 116444736000000000


def guid(v):
    try:return isinstance(v,str) and uuid.UUID(v).int != 0
    except (ValueError,AttributeError):return False


def point(x,y):
    return integer(x,-32768,32767) and integer(y,-32768,32767)


def fresh(value,now,limit=15_000_000):
    return integer(value,1) and value <= now and now-value <= limit


def read_json(path):
    try:
        # Windows may briefly deny access while a producer replaces a snapshot.
        # Retry opening only; malformed/oversized content still fails closed.
        for attempt in range(6):
            try:
                with Path(path).open('rb') as stream: raw=stream.read(MAX_JSON_BYTES+1)
                break
            except OSError:
                if attempt==5:raise
                time.sleep(.01)
        require(len(raw)<=MAX_JSON_BYTES,'oversized_json')
        value=json.loads(raw.decode('utf-8-sig'),object_pairs_hook=state._pairs,parse_constant=state._constant)
        require(isinstance(value,dict),'object_required')
        return value
    except TestControlError:raise
    except (OSError,ValueError,UnicodeError,RecursionError) as error:
        raise TestControlError('unreadable_json') from error


def publish(path,value):
    path=Path(path)
    raw=value if isinstance(value,bytes) else json.dumps(value,ensure_ascii=True,allow_nan=False).encode()
    temporary=path.with_name(path.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with temporary.open('xb') as stream:stream.write(raw);stream.flush();os.fsync(stream.fileno())
        for attempt in range(6):
            try:os.replace(temporary,path);break
            except PermissionError:
                if attempt==5:raise
                # Keep the identical request and request_id on every attempt.
                time.sleep(.01)
    finally:
        if temporary.exists():temporary.unlink()


@contextmanager
def command_lock(directory,name):
    path=Path(directory)/(name+'.cli-lock')
    try:fd=os.open(path,os.O_CREAT|os.O_EXCL|os.O_WRONLY,0o600)
    except FileExistsError as error:raise TestControlError('command_busy') from error
    try:
        os.write(fd,str(os.getpid()).encode());yield
    finally:
        os.close(fd);path.unlink()


def validate_server(value,*,now_filetime,expected_nonce=None):
    require(value.get('schema')=='dfl-server-test-control/v1','bad_server_schema')
    require(value.get('enabled') is True,'test_control_disabled')
    require(integer(value.get('server_pid'),1,0xffffffff) and integer(value.get('server_started_filetime'),1),
            'bad_server_identity')
    require(integer(value.get('session_nonce'),1),'bad_nonce')
    require(expected_nonce is None or value['session_nonce']==expected_nonce,'server_session_changed')
    require(fresh(value.get('captured_filetime'),now_filetime),'stale_server')
    rooms=value.get('instances');require(isinstance(rooms,list) and len(rooms)<=256,'bad_instances')
    seen_runs=set()
    for room in rooms:
        require(isinstance(room,dict) and integer(room.get('run_id'),1) and room['run_id'] not in seen_runs,'bad_run')
        seen_runs.add(room['run_id'])
        require(integer(room.get('room_generation'),1) and integer(room.get('map_id'),0,65535),'bad_room')
        members=room.get('members');require(isinstance(members,list) and 1<=len(members)<=4,'bad_members')
        uids=set();slots=set();chars=set()
        for member in members:
            require(isinstance(member,dict) and integer(member.get('uid'),1,65535)
                and integer(member.get('party_slot'),0,3) and guid(member.get('character_id')),'bad_member')
            require(member['uid'] not in uids and member['party_slot'] not in slots and member['character_id'] not in chars,
                'duplicate_member')
            uids.add(member['uid']);slots.add(member['party_slot']);chars.add(member['character_id'])
        drops=room.get('ground_items');require(isinstance(drops,list) and len(drops)<=4096,'bad_ground_items')
        ids=set()
        for item in drops:
            require(isinstance(item,dict) and integer(item.get('ground_id'),1,65535)
                and integer(item.get('item_id'),0,65535) and integer(item.get('count'),0,0xffffffff)
                and type(item.get('pending')) is bool and point(item.get('x'),item.get('y')),'bad_ground')
            require(item['ground_id'] not in ids,'duplicate_ground');ids.add(item['ground_id'])
    require(isinstance(value.get('loot_results',[]),list),'bad_loot_results')
    return value


def select_instance(server,*,uid,run_id=None):
    require(integer(uid,1,65535),'bad_uid')
    rooms=[room for room in server['instances'] if (run_id is None or room['run_id']==run_id)
           and any(member['uid']==uid for member in room['members'])]
    require(len(rooms)==1,'unique_active_instance_required')
    return rooms[0]


def parse_lease(raw,*,uid,now_filetime):
    require(isinstance(raw,bytes) and len(raw)==LEASE.size,'bad_lease_size')
    magic,version,nonce,run,issued,user,flags=LEASE.unpack(raw)
    require(magic==0x4c545444 and version==1 and flags==1,'test_lease_disabled')
    require(nonce>0 and run>0 and user==uid,'lease_identity_mismatch')
    require(fresh(issued,now_filetime),'stale_lease')
    return dict(session_nonce=nonce,run_id=run,uid=user,issued_filetime=issued)


def make_request(snapshot,lease,*,operation,target_id,x=0,y=0,request_id,issued_tick_ms,park=None):
    try:state._validate(snapshot)
    except state.SnapshotError as error:raise TestControlError(error.code) from error
    require(snapshot['scene']==3 and snapshot['me'] and snapshot['room'],'dungeon_required')
    me=snapshot['me']
    require(not me['dead'] and me['same_map'] and not me['detached'],'live_player_required')
    require(all(isinstance(me.get(k),str) and state._TOKEN.fullmatch(me[k]) and int(me[k],16)>0
        for k in ('actor_token','map_token','user_token')),'native_identity_unknown')
    require(integer(issued_tick_ms) and integer(request_id,1),'bad_request_identity')
    require(snapshot['captured_tick_ms']<=issued_tick_ms and issued_tick_ms-snapshot['captured_tick_ms']<=750,
        'stale_snapshot')
    require(lease['uid']==me['uid'],'lease_identity_mismatch')
    require(integer(operation,1,4) and integer(target_id,0,65535) and point(x,y),'invalid_target')
    require(operation!=1 or target_id==me['uid'],'teleport_self_only')
    require(operation not in (3,4) or target_id>0,'invalid_ground_id')
    reserved=0
    if operation==4:
        require(isinstance(park,(tuple,list)) and len(park)==2 and point(*park),'parking_required')
        packed=(park[0]&0xffff)|((park[1]&0xffff)<<16)
        reserved=packed if packed<0x80000000 else packed-(1<<32)
    else:require(park is None,'unexpected_parking')
    values=(snapshot['pid'],me['uid'],snapshot['process_started_filetime'],request_id,issued_tick_ms,
        snapshot['captured_tick_ms'],snapshot['generation'],snapshot['sequence'],int(me['actor_token'],16),
        int(me['map_token'],16),int(me['user_token'],16),operation,target_id,x,y,reserved,
        lease['session_nonce'],lease['run_id'])
    raw=WIRE.pack(b'DLTC',1,WIRE.size,*values);parse_request(raw)
    return raw


def parse_request(raw):
    require(isinstance(raw,bytes) and len(raw)==WIRE.size,'bad_request_size')
    magic,version,size,*values=WIRE.unpack(raw)
    require(magic==b'DLTC' and version==1 and size==112,'bad_request_header')
    result=dict(zip(FIELDS,values));r=result
    require(r['pid']>0 and 0<r['uid']<=65535 and all(r[k]>0 for k in
        ('process_started_filetime','request_id','generation','snapshot_sequence','actor_token','map_token','user_token',
         'session_nonce','run_id')),'bad_request_identity')
    require(1<=r['operation']<=4 and 0<=r['target_id']<=65535 and point(r['x'],r['y']),'invalid_target')
    require(r['snapshot_tick_ms']<=r['issued_tick_ms'] and r['issued_tick_ms']-r['snapshot_tick_ms']<=750,'stale_snapshot')
    require(r['operation']!=1 or r['target_id']==r['uid'],'teleport_self_only')
    require(r['operation'] not in (3,4) or r['target_id']>0,'invalid_ground_id')
    require(r['operation']==4 or r['reserved']==0,'reserved_nonzero')
    if r['operation']==4:
        r['park']=struct.unpack('<hh',struct.pack('<i',r['reserved']))
    return result


def plan_isolation(instance,*,ground_id,at,park):
    require(isinstance(at,(list,tuple)) and len(at)==2 and point(*at) and
        isinstance(park,(list,tuple)) and len(park)==2 and point(*park),'invalid_target')
    require(math.hypot(at[0]-park[0],at[1]-park[1])>=100,'parking_too_close')
    drops=instance['ground_items'];require(0<len(drops)<=256,'layout_bound')
    ids=[d['ground_id'] for d in drops]
    require(len(ids)==len(set(ids)) and ground_id in ids,'ground_not_found')
    require(all(d['pending'] is False for d in drops),'pickup_in_progress')
    return [dict(ground_id=d['ground_id'],x=(at if d['ground_id']==ground_id else park)[0],
        y=(at if d['ground_id']==ground_id else park)[1]) for d in drops]


def validate_loot_result(value):
    require(integer(value.get('run_id'),1) and integer(value.get('ground_id'),1,65535),'bad_loot_identity')
    if 'scores' in value:
        scores=value['scores']
        require(isinstance(scores,list) and len(scores)==4 and all(integer(v,0,255) for v in scores),'bad_loot_scores')
    status=value.get('status');require(status in ('committed','rejected'),'not_final_loot_result')
    if status=='committed':
        require(value.get('persisted') is True,'persistence_not_confirmed')
        require(integer(value.get('winner_uid'),1,65535)
            and integer(value.get('item_id'),0,65535) and integer(value.get('count'),1,0xffffffff),'bad_loot_commit')
        require(value.get('is_gold') is True or integer(value.get('destination_slot'),0,65535),'missing_inventory_slot')
        require(value['item_id'] not in EQUIPMENT_SAMPLES or guid(value.get('instance_id')),'missing_item_instance')
    else:
        require(value.get('persisted') is False and isinstance(value.get('reason'),str) and bool(value['reason']),
            'bad_loot_rejection')
    return value


def find_loot_result(rows,*,run_id,ground_id,after_event_id=0):
    found=[v for v in rows if v.get('run_id')==run_id and v.get('ground_id')==ground_id
        and integer(v.get('event_id'),1) and v['event_id']>after_event_id]
    return validate_loot_result(max(found,key=lambda v:v['event_id'])) if found else None


def position_sync(snapshots,*,members,target_uid,x,y,tolerance=3):
    observed={}
    for uid in members:
        snapshot=snapshots.get(uid)
        people=[] if client_context(snapshot) is None else snapshot.get('players',[])
        actor=next((p for p in people if p.get('uid')==target_uid),None)
        pos=None if actor is None else actor.get('position')
        observed[str(uid)]=dict(position=pos,matched=bool(pos and all(type(pos.get(k)) in (int,float)
            and math.isfinite(pos[k]) for k in ('x','y')) and abs(pos['x']-x)<=tolerance and abs(pos['y']-y)<=tolerance))
    return dict(complete=bool(members) and all(v['matched'] for v in observed.values()),clients=observed)


def validate_ground_snapshot(value,sdk,*,now_tick_ms):
    require(value.get('schema')=='dfl-client-test-state/v1','bad_ground_snapshot_schema')
    require(value.get('valid') is True and value.get('truncated') is False,'incomplete_ground_snapshot')
    require(all(value.get(k)==sdk[k] for k in ('pid','process_started_filetime','generation'))
        and value.get('uid')==sdk['me']['uid'] and value.get('map_token')==sdk['me']['map_token'],
        'ground_snapshot_identity_mismatch')
    tick=value.get('captured_tick_ms')
    require(integer(tick) and tick<=now_tick_ms and now_tick_ms-tick<=750,'stale_ground_snapshot')
    rows=value.get('ground_items');require(isinstance(rows,list) and len(rows)<=256,'ground_snapshot_bound')
    ids=set()
    for row in rows:
        require(isinstance(row,dict) and integer(row.get('ground_id'),1,65535) and row['ground_id'] not in ids,
            'ground_snapshot_duplicate_or_invalid_id')
        ids.add(row['ground_id'])
        require(all(type(row.get(k)) in (int,float) and math.isfinite(row[k]) for k in ('x','y','z'))
            and type(row.get('active')) is bool and type(row.get('pending')) is bool,'bad_ground_snapshot_item')
    return value


def ground_sync(snapshots,*,members,placements,tolerance=3):
    expected={p['ground_id']:p for p in placements};observed={}
    for uid in members:
        snapshot=snapshots.get(uid)
        rows=[] if snapshot is None else [p for p in snapshot['ground_items'] if p['active']]
        by_id={p['ground_id']:p for p in rows}
        matched=bool(snapshot is not None and len(by_id)==len(rows) and set(by_id)==set(expected))
        positions={}
        for gid,goal in expected.items():
            actual=by_id.get(gid)
            okay=bool(actual and not actual['pending'] and abs(actual['x']-goal['x'])<=tolerance
                and abs(actual['y']-goal['y'])<=tolerance and abs(actual['z'])<=tolerance)
            positions[str(gid)]=dict(position=actual,matched=okay);matched=matched and okay
        observed[str(uid)]=dict(matched=matched,ground_items=positions)
    return dict(complete=bool(members) and all(v['matched'] for v in observed.values()),clients=observed)


def load_ground_clients(directory,clients):
    values={}
    for uid,sdk in clients.items():
        try:
            values[uid]=validate_ground_snapshot(read_json(Path(directory)/f"dfdebug-test-state-{sdk['pid']}.json"),
                sdk,now_tick_ms=state.now_tick_ms())
        except TestControlError:continue
    return values


def load_server(directory,expected_nonce=None):
    value=validate_server(read_json(Path(directory)/'dfdebug-test-server.json'),now_filetime=filetime(),expected_nonce=expected_nonce)
    require(state.get_process_started_filetime(value['server_pid'])==value['server_started_filetime'],'server_process_changed')
    return value


def load_clients(directory,*,uids=None):
    for attempt in range(6):
        # Re-enumerate after a replacement gap or failed second SDK read. Never
        # merge different attempts into an apparently complete party snapshot.
        values={}
        for path in Path(directory).glob('dfdebug-client-*.json'):
            try:
                raw=read_json(path);pid=raw.get('pid');start=state.get_process_started_filetime(pid)
                snap=state.load_snapshot(path,pid=pid,process_started_filetime=start,now_tick_ms=state.now_tick_ms())
                if snap['me'] is None or (uids is not None and snap['me']['uid'] not in uids):continue
                uid=snap['me']['uid'];require(uid not in values,'ambiguous_live_uid');values[uid]=snap
            except state.SnapshotError:continue
            except TestControlError as error:
                if error.code=='ambiguous_live_uid':raise
        if uids is None or set(uids).issubset(values) or attempt==5:break
        time.sleep(.01)
    return values


def native_result(value,request,now):
    require(value.get('schema')=='dfl-client-test-control/v1','bad_native_schema')
    for key in ('pid','uid','process_started_filetime','request_id','generation','session_nonce','run_id','x','y'):
        require(type(value.get(key)) is int and value[key]==request[key],'native_result_identity_mismatch')
    for key in ('actor_token','map_token','user_token'):
        require(value.get(key)==f"0x{request[key]:08X}",'native_result_identity_mismatch')
    require(value.get('server_committed') is False and value.get('persistence_verified') is False,'native_result_claimed_commit')
    require(value.get('status') in ('applied','partial','rejected'),'native_result_not_final')
    require(value.get('operation')==request['operation'] and value.get('target_id')==request['target_id'],
        'native_result_target_mismatch')
    require(integer(value.get('tick_ms')) and value['tick_ms']<=now and now-value['tick_ms']<=5000,'stale_native_result')
    return value


def client_context(snapshot):
    if not snapshot or snapshot.get('scene')!=3 or not snapshot.get('room') or not snapshot.get('me'):return None
    me=snapshot['me']
    return tuple(snapshot.get(k) for k in ('pid','process_started_filetime','generation'))+tuple(me.get(k)
        for k in ('uid','user_token','actor_token','map_token'))+(snapshot['room'].get('stage_token'),)


def check_binding(expected_server,expected_room,expected_client,server,room,client):
    require(all(server[k]==expected_server[k] for k in ('server_pid','server_started_filetime','session_nonce')),'server_session_changed')
    require(all(room[k]==expected_room[k] for k in ('run_id','room_generation','map_id')),'room_changed')
    require(client_context(expected_client) is not None and client_context(client)==client_context(expected_client),'client_context_changed')


def execute_native(directory,*,uid,operation,target_id,x=0,y=0,park=None,timeout=5,
                   expected_server=None,expected_room=None,expected_client=None):
    directory=Path(directory);server=load_server(directory);room=select_instance(server,uid=uid)
    clients=load_clients(directory,uids=[uid]);require(uid in clients,'client_unavailable')
    snap=clients[uid];pid=snap['pid'];stem=f'dfdebug-test-control-{pid}'
    if expected_server is None:expected_server,expected_room,expected_client=server,room,snap
    check_binding(expected_server,expected_room,expected_client,server,room,snap)
    with command_lock(directory,stem):
        # A newly entered native room must not be paired with the preceding
        # periodic server snapshot. Wait for a server observation after the
        # frozen client sample, then refuse any changed room or actor epoch.
        fresh_deadline=time.monotonic()+min(timeout,1.0)
        while True:
            server=load_server(directory,expected_nonce=expected_server['session_nonce']);room=select_instance(server,uid=uid)
            clients=load_clients(directory,uids=[uid]);require(uid in clients,'client_unavailable');snap=clients[uid]
            check_binding(expected_server,expected_room,expected_client,server,room,snap)
            if server['captured_filetime']>=expected_client['captured_filetime']:break
            require(time.monotonic()<fresh_deadline,'server_snapshot_behind_client')
            time.sleep(.025)
        lease=parse_lease((directory/f'dfdebug-test-lease-{uid}.bin').read_bytes(),uid=uid,now_filetime=filetime())
        require(lease['session_nonce']==server['session_nonce'] and lease['run_id']==room['run_id'],'lease_server_mismatch')
        request_id=filetime();raw=make_request(snap,lease,operation=operation,target_id=target_id,x=x,y=y,
            park=park,request_id=request_id,issued_tick_ms=state.now_tick_ms());request=parse_request(raw)
        latest=load_server(directory,expected_nonce=server['session_nonce']);latest_room=select_instance(latest,uid=uid)
        latest_clients=load_clients(directory,uids=[uid]);require(uid in latest_clients,'client_unavailable')
        check_binding(expected_server,expected_room,expected_client,latest,latest_room,latest_clients[uid])
        publish(directory/(stem+'.bin'),raw);deadline=time.monotonic()+timeout
        while time.monotonic()<deadline:
            require(state.get_process_started_filetime(pid)==snap['process_started_filetime'],'client_process_changed')
            try:
                value=read_json(directory/(stem+'.json'))
                if value.get('request_id')==request_id:
                    value=native_result(value,request,state.now_tick_ms())
                    return dict(native=value,run_id=room['run_id'],session_nonce=server['session_nonce'],
                        room_generation=room['room_generation'],request_id=request_id)
            except TestControlError as error:
                if error.code!='unreadable_json':raise
            time.sleep(.025)
        raise TestControlError('native_result_timeout')


def wait_loot(directory,*,uid,ground_id,run_id=None,after_event_id=0,timeout=5):
    first=load_server(directory)
    if run_id is None:run_id=select_instance(first,uid=uid)['run_id']
    require(integer(run_id,1) and integer(uid,1,65535),'bad_loot_query')
    deadline=time.monotonic()+timeout
    while True:
        value=load_server(directory,expected_nonce=first['session_nonce'])
        found=find_loot_result(value.get('loot_results',[]),run_id=run_id,ground_id=ground_id,
            after_event_id=after_event_id)
        if found:return dict(authoritative_source='server_commit_journal',result=found)
        if time.monotonic()>=deadline:raise TestControlError('loot_result_timeout')
        time.sleep(.05)


def teleport(directory,*,uid,x,y,timeout=5):
    before=load_server(directory);room=select_instance(before,uid=uid);members=[m['uid'] for m in room['members']]
    prior=load_clients(directory,uids=members)
    require(len(prior)==len(members) and all(client_context(prior.get(m)) is not None for m in members),'all_dungeon_clients_required')
    result=execute_native(directory,uid=uid,operation=1,target_id=uid,x=x,y=y,timeout=timeout,
        expected_server=before,expected_room=room,expected_client=prior[uid])
    if result['native']['status']!='applied':return dict(status='rejected',**result)
    deadline=time.monotonic()+timeout;sync={}
    try:
        while time.monotonic()<deadline:
            server=load_server(directory,expected_nonce=result['session_nonce']);room=select_instance(server,uid=uid,run_id=result['run_id'])
            require(room['room_generation']==result['room_generation'],'room_changed')
            require([m['uid'] for m in room['members']]==members,'party_changed')
            clients=load_clients(directory,uids=members)
            frozen={m:s for m,s in clients.items() if client_context(s)==client_context(prior[m])}
            sync=position_sync(frozen,members=members,target_uid=uid,x=x,y=y)
            if sync['complete']:break
            time.sleep(.05)
    except (TestControlError,state.SnapshotError,OSError) as error:
        return dict(status='partial_sync',**result,synchronization=sync,error=str(error))
    return dict(status='synchronized' if sync.get('complete') else 'partial_sync',**result,synchronization=sync)


def enemy_death_sync(before,after,*,members,monster_id):
    observed={}
    for uid in members:
        prior=before.get(uid);now=after.get(uid)
        same=bool(client_context(prior) is not None and client_context(now)==client_context(prior))
        enemy=None if not same else next((e for e in now['enemies'] if e['uid']==monster_id),None)
        observed[str(uid)]=dict(context_matched=same,matched=bool(same and (enemy is None or enemy['dead'] is True)))
    return dict(complete=bool(members) and all(v['matched'] for v in observed.values()),clients=observed)


def kill(directory,*,uid,monster_id,timeout=5):
    before=load_server(directory);room=select_instance(before,uid=uid)
    require(any(m['uid']==monster_id and m['alive'] for m in room['monsters']),'live_monster_required')
    members=[m['uid'] for m in room['members']];prior=load_clients(directory,uids=members)
    require(len(prior)==len(members) and all(client_context(prior.get(m)) is not None for m in members),'all_dungeon_clients_required')
    result=execute_native(directory,uid=uid,operation=2,target_id=monster_id,timeout=timeout,
        expected_server=before,expected_room=room,expected_client=prior[uid])
    if result['native']['status']!='applied':return dict(status='rejected',**result)
    deadline=time.monotonic()+timeout;server_dead=False;clients_dead=False;drops=[];sync={}
    try:
        while time.monotonic()<deadline:
            current=load_server(directory,expected_nonce=before['session_nonce']);other=select_instance(current,uid=uid,run_id=room['run_id'])
            require(other['room_generation']==room['room_generation'],'room_changed')
            server_dead=not any(m['uid']==monster_id and m['alive'] for m in other['monsters'])
            drops=[g for g in other['ground_items'] if g['ground_id'] not in {v['ground_id'] for v in room['ground_items']}]
            sync=enemy_death_sync(prior,load_clients(directory,uids=members),members=members,monster_id=monster_id)
            clients_dead=sync['complete']
            if server_dead and clients_dead:break
            time.sleep(.05)
    except (TestControlError,state.SnapshotError,OSError) as error:
        return dict(status='partial_sync',**result,server_monster_removed=server_dead,
            clients_monster_dead=clients_dead,new_ground_items=drops,synchronization=sync,error=str(error))
    return dict(status='synchronized' if server_dead and clients_dead else 'partial_sync',**result,
        server_monster_removed=server_dead,clients_monster_dead=clients_dead,new_ground_items=drops,synchronization=sync)


def server_layout(directory,server,room,uid,placements,timeout):
    directory=Path(directory)
    with command_lock(directory,'dfdebug-test-server-command'):
        request=dict(schema='dfl-server-test-command/v1',request_id=filetime(),session_nonce=server['session_nonce'],
            run_id=room['run_id'],room_generation=room['room_generation'],operation='relocate-ground',
            issued_filetime=filetime(),user_id=uid,placements=placements)
        publish(directory/'dfdebug-test-server-command.json',request);deadline=time.monotonic()+timeout
        while time.monotonic()<deadline:
            try:
                reply=read_json(directory/'dfdebug-test-server-command-result.json')
                if reply.get('request_id')==request['request_id']:
                    require(reply.get('schema')=='dfl-server-test-command-result/v1' and all(reply.get(k)==request[k]
                        for k in ('session_nonce','run_id','room_generation')),'layout_result_identity_mismatch')
                    require(reply.get('status') in ('applied','rejected'),'layout_result_not_final')
                    return reply
            except TestControlError as error:
                if error.code!='unreadable_json':raise
            time.sleep(.025)
        raise TestControlError('layout_result_timeout')


def isolate(directory,*,uid,ground_id,park,timeout=5):
    server=load_server(directory);room=select_instance(server,uid=uid);members=[m['uid'] for m in room['members']]
    clients=load_clients(directory,uids=members)
    require(len(clients)==len(members) and all(client_context(clients.get(m)) is not None for m in members),
        'all_dungeon_clients_required')
    pos=clients[uid]['me']['position'];require(pos is not None,'player_position_unknown')
    at=(round(pos['x']),round(pos['y']));placements=plan_isolation(room,ground_id=ground_id,at=at,park=park)
    barrier=time.monotonic()+min(timeout,1.0)
    while True:
        current=load_server(directory,expected_nonce=server['session_nonce']);other=select_instance(current,uid=uid,run_id=room['run_id'])
        check_binding(server,room,clients[uid],current,other,clients[uid])
        if current['captured_filetime']>=max(s['captured_filetime'] for s in clients.values()):break
        require(time.monotonic()<barrier,'server_snapshot_behind_client');time.sleep(.025)
    fresh_clients=load_clients(directory,uids=members)
    require(len(fresh_clients)==len(members) and all(client_context(fresh_clients[m])==client_context(clients[m])
        for m in members),'client_context_changed')
    layout=server_layout(directory,server,room,uid,placements,timeout)
    if layout['status']!='applied':return dict(status='rejected',server=layout,clients={})
    results={};sync={};complete=False
    try:
        for member in members:
            current=load_server(directory,expected_nonce=server['session_nonce']);other=select_instance(current,uid=uid,run_id=room['run_id'])
            require(other['room_generation']==room['room_generation'],'room_changed')
            try:results[str(member)]=execute_native(directory,uid=member,operation=4,target_id=ground_id,x=at[0],y=at[1],
                park=park,timeout=timeout,expected_server=server,expected_room=room,expected_client=clients[member])
            except (TestControlError,state.SnapshotError,OSError) as error:results[str(member)]=dict(error=str(error))
        complete=all(v.get('native',{}).get('status')=='applied' for v in results.values())
        deadline=time.monotonic()+timeout
        while complete and time.monotonic()<deadline:
            current=load_server(directory,expected_nonce=server['session_nonce']);other=select_instance(current,uid=uid,run_id=room['run_id'])
            require(other['room_generation']==room['room_generation'],'room_changed')
            fresh_clients=load_clients(directory,uids=members)
            frozen={m:s for m,s in fresh_clients.items() if client_context(s)==client_context(clients[m])}
            sync=ground_sync(load_ground_clients(directory,frozen),members=members,placements=placements)
            if sync['complete']:break
            time.sleep(.05)
    except (TestControlError,state.SnapshotError,OSError) as error:
        return dict(status='partial_sync',server=layout,placements=placements,clients=results,
            observed_sync=False,synchronization=sync,error=str(error))
    return dict(status='synchronized' if complete and sync.get('complete') else 'partial_sync',server=layout,
        placements=placements,clients=results,observed_sync=bool(sync.get('complete')),synchronization=sync)


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory',type=Path,required=True);parser.add_argument('--timeout',type=float,default=5)
    sub=parser.add_subparsers(dest='command',required=True)
    p=sub.add_parser('state');p.add_argument('--uid',type=int)
    p=sub.add_parser('player');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('teleport')
    for name in ('uid','x','y'):p.add_argument('--'+name,type=int,required=True)
    p=sub.add_parser('monster');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('kill')
    p.add_argument('--uid',type=int,required=True);p.add_argument('--monster',type=int,required=True)
    p=sub.add_parser('drop');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('isolate')
    for name in ('uid','ground','park-x','park-y'):p.add_argument('--'+name,type=int,required=True)
    p=sub.add_parser('loot');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('result')
    p.add_argument('--uid',type=int,required=True);p.add_argument('--ground',type=int,required=True)
    p.add_argument('--run',type=int);p.add_argument('--after-event',type=int,default=0)
    args=parser.parse_args(argv)
    try:
        require(math.isfinite(args.timeout) and 0<args.timeout<=60,'invalid_timeout')
        if args.command=='state':
            value=load_server(args.directory)
            if args.uid is not None:value={**value,'instances':[select_instance(value,uid=args.uid)]}
            clients=load_clients(args.directory,uids=None if args.uid is None else [args.uid])
            result=dict(status='observed',server=value,clients=clients,client_ground=load_ground_clients(args.directory,clients))
        elif args.command=='player':result=teleport(args.directory,uid=args.uid,x=args.x,y=args.y,timeout=args.timeout)
        elif args.command=='monster':result=kill(args.directory,uid=args.uid,monster_id=args.monster,timeout=args.timeout)
        elif args.command=='drop':result=isolate(args.directory,uid=args.uid,ground_id=args.ground,park=(args.park_x,args.park_y),timeout=args.timeout)
        else:result=wait_loot(args.directory,uid=args.uid,ground_id=args.ground,run_id=args.run,after_event_id=args.after_event,timeout=args.timeout)
        print(json.dumps(result,ensure_ascii=True,allow_nan=False,indent=2))
        final=result.get('status',result.get('result',{}).get('status'))
        return 0 if final in ('observed','synchronized','committed') else 2
    except (TestControlError,state.SnapshotError,OSError) as error:
        print(json.dumps(dict(status='error',reason=str(error)),ensure_ascii=True));return 1


if __name__=='__main__':raise SystemExit(main())
