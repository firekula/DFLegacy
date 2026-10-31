import h,json,time,subprocess,sys,importlib.util,math,struct,datetime,threading,hashlib
from tools.debug import client_native_route as nav
N=h.ROOT.parent/'town-position-followup-20261029';OUT=N/'live';OUT.mkdir(exist_ok=True)
spec=importlib.util.spec_from_file_location('town_ro',h.ROOT.parent/'active-drop-native-20261029/review/ui/inventory_observe.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

def actor_read(r,p,mp):
 actor=int(p['actor_token'],16);user=int(p['user_token'],16);uid=p['uid'];before=time.perf_counter_ns()
 assert r.u(actor)==0x9988DC and r.u(user+0x10C)==actor and r.u(actor+0xE4)==mp and m.secure(r,actor+0xD4)==uid
 pos=m.actor_position(r,actor);raw=struct.unpack('<4i',r.raw(actor+0x234,16));assert pos
 assert r.u(user+0x10C)==actor and r.u(actor+0xE4)==mp and m.secure(r,actor+0xD4)==uid
 return dict(position=pos,direction=dict(x=raw[0],y=raw[1]),destination=dict(x=raw[2],y=raw[3]),start_qpc_ns=before,end_qpc_ns=time.perf_counter_ns())

for role in 'AB':
 for kind in ['ground','direction','destination']:
  label=role+'-'+kind;other='B' if role=='A' else 'A';before={r:h.snapshot(r) for r in [role,other]};own=before[role]['me'];uid=own['uid'];peer=next(p for p in before[other]['players'] if p['uid']==uid)
  assert all(s['scene']==1 and s['town']['town_id']==2 and s['town']['area_id']==0 for s in before.values())
  proc={};ident={}
  for rr in [role,other]:
   c,start=h.identity(rr);proc[rr]=m.Process(c['Id'],start,c['Path']);ident[rr]=dict(record=c,started=start)
  clock=time.perf_counter();utc=datetime.datetime.now(datetime.timezone.utc).isoformat();samples=[];action={};errors=[]
  def act():
   try:
    action['begin_t']=time.perf_counter()-clock
    if kind=='destination':
     s=h.snapshot(role);goal=dict(town_id=2,area_id=0,x=round(s['town']['position']['x'])+110,y=round(s['town']['position']['y']));c,start=h.identity(role)
     action['goal']=goal;action['result']=nav.execute(h.DIRECTORY,pid=c['Id'],uid=uid,started=start,**goal,timeout_seconds=8)
     assert action['result']['ok'],action['result']
    else:
     steps=([dict(t=100,op='move',x=400,y=330),dict(t=250,op='click',x=400,y=330,dur=100)] if kind=='ground' else [dict(t=100,op='key',vk=37 if role=='A' else 39,dur=300)])
     action['steps']=steps
     p=subprocess.run([sys.executable,str(h.ROOT/'control.py'),role,'U07-'+label+'-input',json.dumps(steps)],capture_output=True,text=True,encoding='utf-8');action['returncode']=p.returncode;action['output']=p.stdout;assert p.returncode==0,p.stderr
    action['end_t']=time.perf_counter()-clock
   except Exception as e:errors.append(repr(e))
  worker=None
  try:
   until=clock+11
   while time.perf_counter()<until:
    t=time.perf_counter()-clock
    if t>=2 and worker is None:worker=threading.Thread(target=act);worker.start()
    a=actor_read(proc[role],own,int(before[role]['town']['map_token'],16));b=actor_read(proc[other],peer,int(before[other]['town']['map_token'],16));samples.append(dict(t=t,own=a,peer=b,dx=a['position']['x']-b['position']['x'],dy=a['position']['y']-b['position']['y']))
    time.sleep(.08)
   worker.join(timeout=12);assert not worker.is_alive();assert not errors,errors
   after={r:h.snapshot(r) for r in [role,other]};end=samples[-1];tail=[x for x in samples if x['t']>=8]
   result=dict(role=role,kind=kind,uid=uid,before=before,after=after,identity=ident,utc_start=utc,qpc_origin=clock,action=action,samples=samples,errors=errors,journal_sha256=hashlib.sha256(h.JOURNAL).hexdigest().upper(),scope='QUERY_INFORMATION/VM_READ paired validated native town actors. Sequential bounded reads, not atomic. Original native inputs/single DLRT goal only; no screenshots inside measured window.')
   with (OUT/(label+'.json')).open('x',encoding='utf-8') as f:json.dump(result,f,indent=2)
   print(json.dumps(dict(trial=label,samples=len(samples),action=action,tail_dx=[min(x['dx'] for x in tail),max(x['dx'] for x in tail)],tail_dy=[min(x['dy'] for x in tail),max(x['dy'] for x in tail)],read_calls={r:proc[r].calls for r in proc})),flush=True)
  finally:
   for r in proc.values():r.identity();r.close()
