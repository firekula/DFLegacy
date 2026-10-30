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


def make_request(snapshot,lease,*,operation,target_id,x=0,y=0,request_id,issued_tick_ms,park=None,item_token=None):
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
    require(integer(operation,1,6) and integer(target_id,0,65535),'invalid_target')
    require(operation==5 or point(x,y),'invalid_target')
    require(operation==5 or item_token is None,'unexpected_item_token')
    require(operation!=1 or target_id==me['uid'],'teleport_self_only')
    require(operation!=6 or (target_id==me['uid'] and x==0 and y==0),'player_death_self_only')
    require(operation not in (3,4) or target_id>0,'invalid_ground_id')
    reserved=0
    if operation==5:
        require(integer(x,1,0x7fffffff) and integer(y,0,65535),'invalid_inventory_drop')
        require(park is None,'unexpected_parking')
        token=object_token(item_token)
        reserved=token if token<0x80000000 else token-(1<<32)
    elif operation==4:
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
    require(1<=r['operation']<=6 and 0<=r['target_id']<=65535,'invalid_target')
    require(r['operation']!=6 or (r['target_id']==r['uid'] and r['x']==0 and r['y']==0 and r['reserved']==0),'player_death_self_only')
    if r['operation']==5:
        require((r['target_id']==0 or 9<=r['target_id']<=511) and integer(r['x'],1,0x7fffffff)
            and integer(r['y'],0,65535) and (r['target_id']==0)==(r['y']==0),'invalid_inventory_drop')
        require((r['reserved']&0xffffffff)>=0x10000,'invalid_item_token')
    else:require(point(r['x'],r['y']),'invalid_target')
    require(r['snapshot_tick_ms']<=r['issued_tick_ms'] and r['issued_tick_ms']-r['snapshot_tick_ms']<=750,'stale_snapshot')
    require(r['operation']!=1 or r['target_id']==r['uid'],'teleport_self_only')
    require(r['operation'] not in (3,4) or r['target_id']>0,'invalid_ground_id')
    require(r['operation'] in (4,5) or r['reserved']==0,'reserved_nonzero')
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


def observation_map(value,sdk):
    """Bind optional observer metadata; this never grants a mutation context."""
    me=sdk.get('me');require(isinstance(me,dict),'observer_identity_missing')
    observer=value.get('observer')
    if observer is None:
        require(me.get('dead') is False,'dead_observer_binding_required')
        return me.get('map_token')  # Compatibility with older living-only producers.
    room=sdk.get('room')
    require(isinstance(observer,dict) and isinstance(room,dict) and sdk.get('scene')==3
        and 'actor_map_token' in observer and type(me.get('same_map')) is bool and type(me.get('detached')) is bool
        and room.get('map_token') is not None and room.get('stage_token') is not None
        and type(me.get('dead')) is bool and type(observer.get('dead')) is bool
        and observer['dead']==me['dead']
        and all(observer.get(key)==me.get(source) and me.get(source) is not None
            for key,source in (('actor_token','actor_token'),('user_token','user_token')))
        and observer.get('actor_map_token')==me.get('map_token')
        and observer.get('stage_token')==room['stage_token'],'observer_identity_mismatch')
    require(me['dead'] or (me.get('same_map') is True and me.get('detached') is False
        and me.get('map_token')==room['map_token']),'observer_living_map_mismatch')
    require(integer(value.get('snapshot_sequence'),1) and integer(sdk.get('sequence'),1)
        and abs(value['snapshot_sequence']-sdk['sequence'])<=5,'observer_sequence_mismatch')
    return room['map_token']


def validate_ground_snapshot(value,sdk,*,now_tick_ms):
    require(value.get('schema')=='dfl-client-test-state/v1','bad_ground_snapshot_schema')
    require(value.get('valid') is True and value.get('truncated') is False,'incomplete_ground_snapshot')
    require(all(value.get(k)==sdk[k] for k in ('pid','process_started_filetime','generation'))
        and value.get('uid')==sdk['me']['uid'] and value.get('map_token')==observation_map(value,sdk),
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



def object_token(value):
    require(isinstance(value,str) and state._TOKEN.fullmatch(value) is not None
        and 0x10000<=int(value,16)<=0xffffffff,'invalid_item_token')
    return int(value,16)


def validate_inventory_snapshot(value,sdk,*,now_tick_ms,session_nonce,run_id):
    # Ground extraction can independently fail; inventory has its own validity.
    require(value.get('schema')=='dfl-client-test-state/v1','bad_inventory_snapshot_schema')
    require(all(type(value.get(k)) is int and value[k]==sdk[k] for k in
        ('pid','process_started_filetime','generation')) and type(value.get('uid')) is int
        and value['uid']==sdk['me']['uid'] and value.get('map_token')==observation_map(value,sdk),
        'inventory_snapshot_identity_mismatch')
    require(type(value.get('session_nonce')) is int and value['session_nonce']==session_nonce
        and type(value.get('run_id')) is int and value['run_id']==run_id,'inventory_snapshot_epoch_mismatch')
    require(fresh(value.get('captured_tick_ms'),now_tick_ms,750),'stale_inventory_snapshot')
    ops=value.get('supported_operations')
    require(isinstance(ops,list) and all(type(op) is int for op in ops) and 5 in ops,'inventory_drop_unsupported')
    inv=value.get('inventory');require(isinstance(inv,dict) and inv.get('valid') is True,'invalid_inventory_snapshot')
    require(isinstance(inv.get('reason'),str) and inv.get('pending') is False,'inventory_pending_or_unknown')
    object_token(inv.get('container_token'));object_token(inv.get('array_token'))
    require(integer(inv.get('capacity'),1,512),'inventory_capacity_invalid')
    rows=inv.get('items');require(isinstance(rows,list) and len(rows)<=inv['capacity'],'inventory_bound')
    slots=set();tokens=set()
    for row in rows:
        require(isinstance(row,dict) and integer(row.get('slot'),0,inv['capacity']-1)
            and (row['slot']==0 or row['slot']>=9) and row['slot'] not in slots,'inventory_duplicate_or_invalid_slot')
        token=object_token(row.get('item_token'))
        require(token not in tokens,'inventory_duplicate_object')
        require(integer(row.get('item_id'),0,65535) and integer(row.get('count'),0 if row['slot']==0 else 1,0xffffffff)
            and (row['slot']==0)==(row['item_id']==0),'invalid_inventory_item')
        slots.add(row['slot']);tokens.add(token)
    return value


DROP_METADATA=('instance_id','state','durability','seal_state','equipment_quality_seed')


def drop_source(room,*,uid,slot,item_id,allow_missing=False):
    members=[m for m in room['members'] if m['uid']==uid]
    inventories=room.get('inventories')
    require(len(members)==1 and isinstance(inventories,list),'inventory_source_missing')
    rows=[r for r in inventories if isinstance(r,dict) and type(r.get('uid')) is int and r['uid']==uid]
    require(len(rows)==1 and rows[0].get('character_id')==members[0]['character_id'],'inventory_source_identity_mismatch')
    inv=rows[0]
    if slot==0:
        require(item_id==0 and integer(inv.get('gold'),0,0xffffffff),'invalid_gold_source')
        return dict(item_id=0,count=inv['gold'])
    items=inv.get('items');require(isinstance(items,list),'inventory_source_missing')
    rows=[r for r in items if isinstance(r,dict) and r.get('space')=='main' and r.get('slot')==slot]
    if not rows and allow_missing:return None
    require(len(rows)==1 and rows[0].get('item_id')==item_id and integer(rows[0].get('count'),1,0xffffffff),
        'inventory_source_mismatch')
    row=rows[0]
    require(all(k in row for k in DROP_METADATA),'inventory_metadata_missing')
    require(row['instance_id'] is None or row['instance_id']=='00000000-0000-0000-0000-000000000000' or guid(row['instance_id']),'invalid_inventory_instance')
    require(all(row[k] is None or integer(row[k],0,0xffffffff) for k in DROP_METADATA[1:]),'invalid_inventory_metadata')
    return {k:row[k] for k in ('item_id','count')+DROP_METADATA}


def validate_drop_native(directory,sdk,server,room,*,slot,item_id,count,item_token):
    raw=read_json(Path(directory)/f"dfdebug-test-state-{sdk['pid']}.json")
    value=validate_inventory_snapshot(raw,sdk,now_tick_ms=state.now_tick_ms(),
        session_nonce=server['session_nonce'],run_id=room['run_id'])
    source=drop_source(room,uid=sdk['me']['uid'],slot=slot,item_id=item_id)
    rows=[r for r in value['inventory']['items'] if r['slot']==slot]
    require(len(rows)==1 and rows[0]['item_id']==item_id and rows[0]['count']==source['count']
        and count<=source['count'],'native_inventory_source_mismatch')
    require(object_token(rows[0]['item_token'])==object_token(item_token),'inventory_item_token_changed')
    return source


def active_drop_sync(directory,clients,*,members,ground,session_nonce,run_id,tolerance=3):
    observed={};positions=[]
    for uid in members:
        actual=None;failure=None
        try:
            sdk=clients.get(uid);require(sdk is not None,'client_unavailable')
            value=validate_ground_snapshot(read_json(Path(directory)/f"dfdebug-test-state-{sdk['pid']}.json"),
                sdk,now_tick_ms=state.now_tick_ms())
            require(type(value.get('session_nonce')) is int and value['session_nonce']==session_nonce
                and type(value.get('run_id')) is int and value['run_id']==run_id,'ground_snapshot_epoch_mismatch')
            rows=[r for r in value['ground_items'] if r['ground_id']==ground['ground_id']]
            if len(rows)==1:actual=rows[0]
        except TestControlError as error:failure=error.code
        matched=bool(actual and actual['active'] and not actual['pending'])
        position=None if actual is None else {axis:actual[axis] for axis in ('x','y','z')}
        observed[str(uid)]=dict(identity_matched=matched,matched=matched,position=position,
            ground_item=actual,error=failure)
        if matched:positions.append(position)
    complete=bool(members) and all(v['identity_matched'] for v in observed.values())
    spread={axis:max(p[axis] for p in positions)-min(p[axis] for p in positions)
        for axis in ('x','y','z')} if positions else None
    # Active-drop arcs are original-client animation. Report replica coordinates
    # independently; a server spawn coordinate is not a final landing target.
    return dict(complete=complete,identity_complete=complete,clients=observed,
        positions=dict(consistent=bool(complete and all(v<=tolerance for v in spread.values())),
            tolerance=tolerance,spread=spread,reference='native_replicas',
            server_spawn=dict(x=ground['x'],y=ground['y'])))


def inventory_drop(directory,*,uid,slot,item_id,count,timeout=5):
    require(integer(uid,1,65535) and integer(slot,0,511) and (slot==0 or slot>=9)
        and integer(item_id,0,65535) and (slot==0)==(item_id==0)
        and integer(count,1,0x7fffffff),'invalid_inventory_drop')
    require(slot!=0 or count<=1000,'gold_drop_limit')
    before=load_server(directory);room=select_instance(before,uid=uid);members=[m['uid'] for m in room['members']]
    require(1<=len(members)<=4,'invalid_party_size')
    prior=load_clients(directory,uids=members)
    require(set(prior)==set(members) and all(client_context(prior[m]) is not None for m in members),'all_dungeon_clients_required')
    # Keep the controller reserved after the native acknowledgement, through
    # the read-only server/peer confirmation window. A second controller fails closed.
    with command_lock(directory,f"dfdebug-inventory-drop-{prior[uid]['pid']}"):
        barrier=time.monotonic()+min(timeout,1)
        while before['captured_filetime']<max(s['captured_filetime'] for s in prior.values()):
            require(time.monotonic()<barrier,'server_snapshot_behind_client')
            latest=load_server(directory,expected_nonce=before['session_nonce']);other=select_instance(latest,uid=uid)
            check_binding(before,room,prior[uid],latest,other,prior[uid]);before,room=latest,other
            time.sleep(.025)
        raw=read_json(Path(directory)/f"dfdebug-test-state-{prior[uid]['pid']}.json")
        inv=validate_inventory_snapshot(raw,prior[uid],now_tick_ms=state.now_tick_ms(),
            session_nonce=before['session_nonce'],run_id=room['run_id'])['inventory']
        rows=[r for r in inv['items'] if r['slot']==slot];require(len(rows)==1,'inventory_slot_empty')
        token=rows[0]['item_token']
        source=validate_drop_native(directory,prior[uid],before,room,slot=slot,item_id=item_id,count=count,item_token=token)
        fresh_clients=load_clients(directory,uids=members)
        require(set(fresh_clients)==set(members) and all(client_context(fresh_clients[m])==client_context(prior[m])
            for m in members),'client_context_changed')
        result=execute_native(directory,uid=uid,operation=5,target_id=slot,x=count,y=item_id,item_token=token,timeout=timeout,
            expected_server=before,expected_room=room,expected_client=prior[uid])
        if result['native']['status']=='rejected':return dict(status='rejected',**result)
        # A partial sender receipt can follow a real send; confirm read-only, once.
        change=dict(before=source,after=None,matched=False,changed=False);new_items=[];ground=None;sync={};error=None
        deadline=time.monotonic()+timeout;before_ids={r['ground_id'] for r in room['ground_items']}
        try:
            while True:
                current=load_server(directory,expected_nonce=before['session_nonce']);other=select_instance(current,uid=uid,run_id=room['run_id'])
                check_binding(before,room,prior[uid],current,other,prior[uid])
                require(other['members']==room['members'],'party_changed')
                after=drop_source(other,uid=uid,slot=slot,item_id=item_id,allow_missing=True)
                remaining=source['count']-count
                matched=(after is None and remaining==0) or bool(after and after['count']==remaining
                    and all(after.get(k)==source.get(k) for k in DROP_METADATA))
                change=dict(before=source,after=after,matched=bool(matched),changed=after!=source)
                new_items=[r for r in other['ground_items'] if r['ground_id'] not in before_ids]
                candidates=[r for r in new_items if type(r.get('owner_uid')) is int and r['owner_uid']==uid
                    and type(r.get('routing_source')) is int and r['routing_source']==0 and r.get('pending') is False
                    and r.get('item_id')==item_id and r.get('count')==count and r.get('is_gold') is (slot==0)
                    and (slot==0 or all(k in r and type(r[k]) is type(source[k]) and r[k]==source[k] for k in DROP_METADATA))]
                ground=candidates[0] if len(candidates)==1 else None
                if ground:
                    clients=load_clients(directory,uids=members)
                    require(set(clients)==set(members) and all(client_context(clients[m])==client_context(prior[m])
                        for m in members),'client_context_changed')
                    sync=active_drop_sync(directory,clients,members=members,ground=ground,
                        session_nonce=before['session_nonce'],run_id=room['run_id'])
                if matched and ground and sync.get('complete'):break
                if time.monotonic()>=deadline:break
                time.sleep(.05)
        except (TestControlError,state.SnapshotError,OSError) as failure:error=str(failure)
        status='synchronized' if change['matched'] and ground and sync.get('complete') else (
            'partial_sync' if change['changed'] else 'not_confirmed')
        return dict(status=status,**result,source_change=change,new_ground_items=new_items,ground_confirmation=ground,
            synchronization=sync,authoritative_source='server_inventory_and_active_ground_snapshot',error=error)

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
    if request['operation']==5:
        require(type(value.get('reserved')) is int and value['reserved']==request['reserved'],'native_result_item_token_mismatch')
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


def execute_native(directory,*,uid,operation,target_id,x=0,y=0,park=None,item_token=None,timeout=5,
                   expected_server=None,expected_room=None,expected_client=None,expected_clients=None):
    directory=Path(directory);server=load_server(directory);room=select_instance(server,uid=uid)
    clients=load_clients(directory,uids=[uid]);require(uid in clients,'client_unavailable')
    snap=clients[uid];pid=snap['pid'];stem=f'dfdebug-test-control-{pid}'
    if expected_server is None:expected_server,expected_room,expected_client=server,room,snap
    check_binding(expected_server,expected_room,expected_client,server,room,snap)
    if operation==6:
        member_identity=lambda rows:[(m['uid'],m['character_id'],m['party_slot']) for m in rows]
        expected_members=[m['uid'] for m in expected_room['members']]
        if expected_clients is None:expected_clients=load_clients(directory,uids=expected_members)
        require(set(expected_clients)==set(expected_members) and all(client_context(expected_clients[m]) is not None for m in expected_members),'all_dungeon_clients_required')
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
        if operation==5:
            validate_drop_native(directory,snap,server,room,slot=target_id,item_id=y,count=x,item_token=item_token)
        request_id=filetime();raw=make_request(snap,lease,operation=operation,target_id=target_id,x=x,y=y,
            park=park,item_token=item_token,request_id=request_id,issued_tick_ms=state.now_tick_ms());request=parse_request(raw)
        latest=load_server(directory,expected_nonce=server['session_nonce']);latest_room=select_instance(latest,uid=uid)
        latest_clients=load_clients(directory,uids=[uid]);require(uid in latest_clients,'client_unavailable')
        check_binding(expected_server,expected_room,expected_client,latest,latest_room,latest_clients[uid])
        if operation==6:
            require(member_identity(latest_room['members'])==member_identity(expected_room['members']),'party_changed')
            require(next(m for m in latest_room['members'] if m['uid']==uid).get('alive') is True,'authoritative_living_player_required')
            peers=load_clients(directory,uids=expected_members)
            require(set(peers)==set(expected_members) and all(client_context(peers[m])==client_context(expected_clients[m]) for m in expected_members),'client_context_changed')
        if operation==5:
            old_source=drop_source(expected_room,uid=uid,slot=target_id,item_id=y)
            require(drop_source(latest_room,uid=uid,slot=target_id,item_id=y)==old_source,'inventory_source_changed')
            validate_drop_native(directory,latest_clients[uid],latest,latest_room,slot=target_id,item_id=y,
                count=x,item_token=item_token)
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
    if result['native']['status']=='rejected':return dict(status='rejected',**result)
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
    if result['native']['status']=='rejected':return dict(status='rejected',**result)
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


def player_death_sync(before,after,*,members,target_uid):
    observed={}
    for uid in members:
        old=before.get(uid);new=after.get(uid);matched=False;same=False;person=None
        if old and new and old.get('room') and new.get('room') and old.get('me') and new.get('me'):
            same=(new.get('scene')==3 and new.get('valid') is True and not new.get('truncated')
                and all(new.get(k)==old.get(k) for k in ('pid','process_started_filetime'))
                and all(new['me'].get(k)==old['me'].get(k) for k in ('uid','user_token','actor_token'))
                and all(new['room'].get(k)==old['room'].get(k) for k in ('map_token','stage_token'))
                and new['generation']==old['generation'] and new['sequence']>old['sequence']
                and new['captured_tick_ms']>old['captured_tick_ms'])
            previous=[p for p in old['players'] if p['uid']==target_uid]
            now=[p for p in new['players'] if p['uid']==target_uid]
            if same and len(previous)==len(now)==1:
                person=now[0]
                matched=(all(person.get(k)==previous[0].get(k) for k in ('uid','actor_token','user_token'))
                    and person.get('dead') is True and type(person.get('hp')) is int and person['hp']==0)
                if uid==target_uid:
                    matched=matched and new['me'].get('dead') is True and type(new['me'].get('hp')) is int and new['me']['hp']==0
        observed[str(uid)]=dict(context_matched=same,matched=bool(matched),player=person)
    return dict(complete=bool(members) and all(v['matched'] for v in observed.values()),clients=observed)


def player_kill(directory,*,uid,timeout=5):
    before=load_server(directory);room=select_instance(before,uid=uid);members=[m['uid'] for m in room['members']]
    require(2<=len(members)<=4,'player_death_party_required')
    target=next(m for m in room['members'] if m['uid']==uid)
    require(target.get('alive') is True,'authoritative_living_player_required')
    prior=load_clients(directory,uids=members)
    require(set(prior)==set(members) and all(client_context(prior.get(m)) is not None for m in members),'all_dungeon_clients_required')
    require(prior[uid]['me']['dead'] is False and type(prior[uid]['me']['hp']) is int and prior[uid]['me']['hp']>0,'live_player_required')
    result=execute_native(directory,uid=uid,operation=6,target_id=uid,timeout=timeout,
        expected_server=before,expected_room=room,expected_client=prior[uid],expected_clients=prior)
    if result['native']['status']=='rejected':return dict(status='rejected',**result)
    deadline=time.monotonic()+timeout;server_dead=False;sync={};error=None
    try:
        while time.monotonic()<deadline:
            current=load_server(directory,expected_nonce=before['session_nonce']);other=select_instance(current,uid=uid,run_id=room['run_id'])
            check_binding(before,room,prior[uid],current,other,prior[uid])
            identity=lambda rows:[(m['uid'],m['character_id'],m['party_slot']) for m in rows]
            require(identity(other['members'])==identity(room['members']),'party_changed')
            server_dead=next(m for m in other['members'] if m['uid']==uid).get('alive') is False
            sync=player_death_sync(prior,load_clients(directory,uids=members),members=members,target_uid=uid)
            if server_dead and sync['complete']:break
            time.sleep(.05)
    except (TestControlError,state.SnapshotError,OSError) as failure:error=str(failure)
    return dict(status='synchronized' if server_dead and sync.get('complete') else 'partial_sync',**result,
        server_player_dead=server_dead,synchronization=sync,error=error,
        authoritative_source='server_member_life_and_native_player_observers')


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
    p=q.add_parser('kill');p.add_argument('--uid',type=int,required=True)
    p=sub.add_parser('monster');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('kill')
    p.add_argument('--uid',type=int,required=True);p.add_argument('--monster',type=int,required=True)
    p=sub.add_parser('drop');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('isolate')
    for name in ('uid','ground','park-x','park-y'):p.add_argument('--'+name,type=int,required=True)
    p=sub.add_parser('inventory');q=p.add_subparsers(dest='action',required=True);p=q.add_parser('drop')
    for name in ('uid','slot','item','count'):p.add_argument('--'+name,type=int,required=True)
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
        elif args.command=='player':result=player_kill(args.directory,uid=args.uid,timeout=args.timeout) if args.action=='kill' else teleport(args.directory,uid=args.uid,x=args.x,y=args.y,timeout=args.timeout)
        elif args.command=='monster':result=kill(args.directory,uid=args.uid,monster_id=args.monster,timeout=args.timeout)
        elif args.command=='inventory':result=inventory_drop(args.directory,uid=args.uid,slot=args.slot,item_id=args.item,count=args.count,timeout=args.timeout)
        elif args.command=='drop':result=isolate(args.directory,uid=args.uid,ground_id=args.ground,park=(args.park_x,args.park_y),timeout=args.timeout)
        else:result=wait_loot(args.directory,uid=args.uid,ground_id=args.ground,run_id=args.run,after_event_id=args.after_event,timeout=args.timeout)
        print(json.dumps(result,ensure_ascii=True,allow_nan=False,indent=2))
        final=result.get('status',result.get('result',{}).get('status'))
        return 0 if final in ('observed','synchronized','committed') else 2
    except (TestControlError,state.SnapshotError,OSError) as error:
        print(json.dumps(dict(status='error',reason=str(error)),ensure_ascii=True));return 1


if __name__=='__main__':raise SystemExit(main())
