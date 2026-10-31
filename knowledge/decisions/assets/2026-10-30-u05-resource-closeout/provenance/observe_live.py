from pathlib import Path
import json,sys,time
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from accept import h,act,cli,four
from tools.debug import test_control as control

before=four();h.save('resource-before-safe-setup',dict(snapshots=before))
cleanup=[]
for enemy in before['A']['enemies']:
    if enemy['dead'] is True:continue
    try:result=control.kill(h.DIRECTORY,uid=before['A']['me']['uid'],monster_id=enemy['uid'],timeout=3)
    except Exception as error:result=dict(error=repr(error))
    cleanup.append(dict(monster=enemy['uid'],result=result))
h.save('resource-safe-room-setup',dict(results=cleanup,snapshots=four(),purpose='Preparation only, no loot/kill regression matrix or pickup'))
results={}
selected={'A':27,'B':51,'C':46,'D':4}
for role,skill_id in selected.items():
    s=h.snapshot(role);index=next(i for i,x in enumerate(s['skills']) if x['id']==skill_id)
    skill=s['skills'][index];r=skill['resources'];checks={}
    for field in ('mp_cost','use_limit','use_count','items_sufficient'):
        expected=r[field]
        if expected is None:continue
        expr=f'skills.{index}.resources.{field}='+json.dumps(expected)
        code,value=cli(role,expr);assert code==0 and value['status']=='matched',(role,expr,value)
        checks[field]=value
    results[role]=dict(skill_id=skill_id,index=index,snapshot=s,cli=checks)
    print(json.dumps(dict(role=role,dead=s['me']['dead'],skill_id=skill_id,resources=r,live_cli=len(checks))),flush=True)
h.save('resource-four-live-cli',dict(results=results))
s=act('B','resource-B-skill51-hover',[dict(t=0,op='move',x=120,y=446)],shot=True)
h.save('resource-B-skill51-hover-state',dict(snapshot=s))
print('B tooltip screenshot retained',flush=True)
