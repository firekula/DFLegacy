from pathlib import Path
import ctypes as c,importlib.util,json,time,struct,threading,subprocess,sys,hashlib,statistics,datetime
import h
Q=Path(__file__).resolve().parent;LABEL=sys.argv[1];out=Q/'perf'/(LABEL+'.json');assert not out.exists();events=[];errors=[]
spec=importlib.util.spec_from_file_location('ro_process',Q.parent/'active-drop-native-20261029/review/ui/inventory_observe.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
v,start=h.identity('A');r=m.Process(v['Id'],start,v['Path']);assert r.raw(0x433610,7)==bytes.fromhex('D9053CDDB500C3')
u=c.WinDLL('user32');u.GetForegroundWindow.restype=c.c_void_p;u.GetWindowThreadProcessId.argtypes=[c.c_void_p,c.POINTER(c.c_ulong)];clock=time.perf_counter();utc=time.time_ns();records=[];polls=[];fgs=[];sdk=[];phases=[];last_counter=None;last_poll=None;bounds=None;lastmeta=0;seq=None

def t():return time.perf_counter()-clock

def event(kind,**v):events.append(dict(t=t(),kind=kind,**v))

def focus():
 p=r.path.parent/'auto-input.focus';done=r.path.parent/'auto-input.focus.done';assert not p.exists();done.unlink(missing_ok=True);tmp=p.with_name(p.name+'.measure.tmp');tmp.write_text(str(r.pid),encoding='ascii');tmp.replace(p);event('native_focus_requested');end=time.monotonic()+5
 while p.exists() or not done.exists():
  if time.monotonic()>end:raise RuntimeError('focus ack missing')
  time.sleep(.02)
 event('native_focus_ack')

def plan(label,steps):
 event('input_start',label=label,steps=steps)
 p=subprocess.run([sys.executable,str(Q/'control.py'),'A',LABEL+'-'+label,json.dumps(steps)],capture_output=True,text=True)
 if p.returncode:raise RuntimeError((p.stdout+p.stderr)[-1500:])
 record=json.loads((h.EVIDENCE/(LABEL+'-'+label+'.json')).read_text());event('input_complete',label=label,record=record)

def act(kind):
 try:
  if kind=='screenshots':plan('screen-control',sorted([dict(t=100+i*500,op='key',vk=37 if i%2==0 else 39,dur=200) for i in range(32)]+[dict(t=1000+i*3000,op='key',vk=44,dur=200) for i in range(6)],key=lambda x:x['t']))
  elif kind=='continuous':plan('continuous',[dict(t=100+i*500,op='key',vk=37 if i%2==0 else 39,dur=200) for i in range(32)])
  elif kind=='short':
   anchor=time.monotonic()
   for i in range(16):
    delay=anchor+i-time.monotonic()
    if delay>0:time.sleep(delay)
    plan('short-'+str(i),[dict(t=100,op='key',vk=37 if i%2==0 else 39,dur=200)])
  elif kind=='route':
   from tools.debug import client_native_route as nav
   s=h.snapshot('A');event('route_start',before=s);result=nav.execute(h.DIRECTORY,pid=r.pid,uid=s['me']['uid'],started=start,town_id=2,area_id=0,x=1431,y=299,timeout_seconds=75);event('route_complete',result=result,after=h.snapshot('A'));assert result['ok']
 except Exception as e:errors.append(repr(e));event('action_error',error=repr(e))

try:
 focus()
 image_before={p.name:dict(size=p.stat().st_size,mtime_ns=p.stat().st_mtime_ns) for p in (r.path.parent/'ScreenShot').glob('*.jpg')}
 for name,duration in [('continuous',20),('short',20)]:
  h.identity('A');r.close();r=m.Process(v['Id'],start,v['Path']);last_counter=last_poll=bounds=None;before=h.snapshot('A');phase=dict(name=name,start=t(),duration=duration,before=before);phases.append(phase);event('phase_start',phase=name)
  worker=threading.Thread(target=act,args=(name,),daemon=True);worker.start();until=time.perf_counter()+duration
  while time.perf_counter()<until:
   now=t();fg=c.c_ulong();u.GetWindowThreadProcessId(u.GetForegroundWindow(),c.byref(fg));data=r.raw(0xB5DD20,32);counter=struct.unpack_from('<I',data,20)[0];polls.append([now,t(),struct.unpack_from('<I',data,0)[0],counter,fg.value,name])
   if last_counter is not None and counter!=last_counter:
    delta=(counter-last_counter)&0xffffffff;current=(last_poll,now);entry=dict(t=now,phase=name,delta=delta,foreground_pid=fg.value,native_fps=struct.unpack_from('<f',data,28)[0])
    if delta==1 and bounds:entry.update(lower_ms=max(0,(current[0]-bounds[1])*1000),upper_ms=(current[1]-bounds[0])*1000)
    records.append(entry);bounds=current
   last_counter=counter;last_poll=now
   if now-lastmeta>=.025:
    lastmeta=now;fgs.append(dict(t=now,phase=name,pid=fg.value))
    try:s=json.loads((h.DIRECTORY/f'dfdebug-client-{r.pid}.json').read_bytes())
    except (OSError,ValueError):s=None
    if s and s['sequence']!=seq:
     seq=s['sequence'];sdk.append(dict(t=now,phase=name,scene=s['scene'],capture_us=s['capture_duration_us'],age_ms=r.k.GetTickCount64()-s['captured_tick_ms'],town=s['town']))
   time.sleep(.003)
  worker.join(timeout=50);assert not worker.is_alive();phase['end']=t();phase['after']=h.snapshot('A')
  xs=[x for x in records if x['phase']==name and 'lower_ms'in x];fgxs=[x for x in xs if x['foreground_pid']==r.pid];fs=[x for x in fgs if x['phase']==name];summary=dict(intervals=len(xs),foreground_intervals=len(fgxs),lower_over50=sum(x['lower_ms']>50 for x in xs),foreground_lower_over50=sum(x['lower_ms']>50 for x in fgxs),worst_lower_ms=max((x['lower_ms'] for x in xs),default=0),foreground_fraction=sum(x['pid']==r.pid for x in fs)/len(fs),worst_intervals=sorted(xs,key=lambda x:x['lower_ms'],reverse=True)[:10]);phase['summary']=summary;print(json.dumps(dict(phase=name,**summary)),flush=True)
  assert not errors,errors
finally:
 r.identity();r.close();out.write_text(json.dumps(dict(schema='operation-timing-observation/v1',identity=v,start_utc_ns=utc,clock_origin=clock,scope='B5DD34 external observer bounds, not GPU Present. Includes focus losses; all process reads QUERY/VM_READ only. No screenshot in measured phases.',phases=phases,events=events,records=records,polls=polls,foreground=fgs,sdk=sdk,errors=errors,image_before=image_before,image_after={p.name:dict(size=p.stat().st_size,mtime_ns=p.stat().st_mtime_ns) for p in (r.path.parent/'ScreenShot').glob('*.jpg')}),indent=2),encoding='utf-8')
