from pathlib import Path
import json,sys,time
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from accept import h,act,cli,four
from native_sampler import Sampler,K

def witness(role,ids):
    s=h.snapshot(role);reader=Sampler(role);actor=int(s['me']['actor_token'],16);u=reader.u;secure=reader.secure
    assert u(0xB75A14)==actor and secure(actor+0xD4)==s['me']['uid']
    result=dict(role=role,pid=s['pid'],process_started_filetime=s['process_started_filetime'],sdk_sequence=s['sequence'],
        actor_token=s['me']['actor_token'],uid=s['me']['uid'],read_tick_ms=h.state.now_tick_ms(),skills=[])
    try:
        for skill_id in ids:
            pointer=u(actor+0x1AEC+skill_id*4)
            assert pointer and u(pointer)==0x99EE78 and u(pointer+4)==actor and secure(pointer+8)==skill_id
            level=secure(pointer+0x2E0);maximum=secure(pointer+0x20);begin=u(pointer+0x1BC);end=u(pointer+0x1C0)
            assert 0<=end-begin<=32*0x18 and (end-begin)%0x18==0
            costs=[]
            for row in range(begin,end,0x18):
                item_id,first,last=secure(row),secure(row+8),secure(row+0x10)
                assert maximum>1 and first is not None and last is not None
                numerator=(last-first)*(level-1);quotient=abs(numerator)//(maximum-1)*(1 if numerator>=0 else -1)
                costs.append(dict(item_id=item_id,first=first,last=last,required=first+quotient,matches=[]))
            container=u(0xB5DCEC);assert container==u(actor+0x242C)
            capacity=secure(container+0x10);array=u(container+0x18);assert capacity<=512
            for slot in range(capacity):
                item=u(array+slot*4)
                if not item:continue
                table=u(item);assert u(table+0x18)==0x4619A0
                item_id=u(item+0xC)
                for cost in costs:
                    if item_id==cost['item_id']:
                        assert u(table+0x34)==0x461AB0
                        cost['matches'].append(dict(slot=slot,item_token=hex(item),quantity=secure(item+0xA0)))
            count_head=u(actor+0x1E54);count=u(actor+0x1E58);assert count<=64
            counters=[];node=u(count_head)
            for _ in range(count):
                assert node!=count_head
                counters.append(dict(skill_id=u(node+8),used=secure(node+0xC)));node=u(node)
            assert node==count_head
            row=dict(skill_id=skill_id,skill_token=hex(pointer),level=level,maximum_level=maximum,
                context_enable_raw=secure(pointer+0x2EC),item_costs=costs,inventory_token=hex(container),
                inventory_capacity=capacity,counter_nodes=counters,
                sdk_skill=next(x for x in s['skills'] if x['id']==skill_id))
            assert u(actor+0x1AEC+skill_id*4)==pointer and secure(pointer+0x2E0)==level
            result['skills'].append(row)
        assert u(0xB75A14)==actor and secure(actor+0xD4)==s['me']['uid']
        result['read_calls']=reader.calls
    finally:K.CloseHandle(reader.handle)
    return result

witnesses={role:witness(role,ids) for role,ids in {'A':[27,55],'B':[51],'C':[46],'D':[4]}.items()}
h.save('resource-raw-witnesses',dict(scope='Bounded read-only same-process research, not SDK history or execution receipt',results=witnesses))
rows=[];last={r:0 for r in 'ABCD'};began=time.monotonic()
while time.monotonic()-began<12:
    for role,s in four().items():
        if s['sequence']==last[role]:continue
        last[role]=s['sequence'];rows.append(dict(role=role,pid=s['pid'],sequence=s['sequence'],captured_tick_ms=s['captured_tick_ms'],
            generation=s['generation'],uid=s['me']['uid'],actor_token=s['me']['actor_token'],dead=s['me']['dead'],
            capture_duration_us=s['capture_duration_us'],skills=s['skills']))
    time.sleep(.04)
negative={}
for role in 'ABCD':
    code,value=cli(role,'ui.input_blocked=false',timeout=0);assert code==1 and value['status']=='timeout'
    negative[role]=value
h.save('resource-twelve-second-window',dict(rows=rows,before=four(),global_unknown_checks=negative))
for role in 'ACD':
    s=act(role,'resource-'+role+'-final-key-image',[dict(t=0,op='move',x=590,y=380)],shot=True)
    h.save('resource-'+role+'-final-key-state',dict(snapshot=s))
h.save('resource-four-final-state',dict(snapshots=four()))
print(json.dumps(dict(unique_sdk_rows=len(rows),raw_witness_roles=list(witnesses),global_unknown_timeouts=4,
    item_requirements_A=[dict(skill_id=x['skill_id'],costs=x['item_costs']) for x in witnesses['A']['skills']]),ensure_ascii=False),flush=True)
