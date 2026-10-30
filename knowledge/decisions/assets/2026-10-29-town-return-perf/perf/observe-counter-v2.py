from pathlib import Path
import ctypes as c,importlib.util,json,struct,time,statistics,hashlib,sys,datetime
sys.dont_write_bytecode=True
BASE=Path(sys.argv[1]);Z=Path(__file__).resolve().parents[1];LABEL=sys.argv[2]
spec=importlib.util.spec_from_file_location('ro_process',Z.parent/'active-drop-native-20261029/review/ui/inventory_observe.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class IO(c.Structure):_fields_=[(n,c.c_ulonglong) for n in ['read_ops','write_ops','other_ops','read_bytes','write_bytes','other_bytes']]
def pct(x,p):return sorted(x)[int((len(x)-1)*p)] if x else None
j=json.loads((BASE/'deployment.json').read_text(encoding='utf-8-sig'));directory=BASE/('snapshots-'+j['Phase']);processes={};records={};paths={};file_changes=[];captures={r:[] for r in 'AB'};seen_sdk={}
u=c.WinDLL('user32');u.GetForegroundWindow.restype=c.c_void_p;u.GetWindowThreadProcessId.argtypes=[c.c_void_p,c.POINTER(c.c_ulong)]
for role in 'AB':
 v=next(v for v in j['Clients'] if v['Role']==role);whole,_,frac=v['StartTimeUtc'].removesuffix('Z').partition('.');d=datetime.datetime.fromisoformat(whole)-datetime.datetime(1601,1,1);started=(d.days*86400+d.seconds)*10000000+int((frac+'0000000')[:7]);r=m.Process(v['Id'],started,v['Path']);r.k.GetProcessIoCounters.argtypes=[c.c_void_p,c.POINTER(IO)];assert r.raw(0x433610,7)==bytes.fromhex('D9053CDDB500C3');processes[role]=r;io=IO();assert r.k.GetProcessIoCounters(r.handle,c.byref(io));records[role]=dict(identity=v,io_before={n:getattr(io,n) for n,_ in IO._fields_},updates=[],last_counter=None,last_poll=None,event_bounds=None,counter_resets=0)
 for tag in ['client','party','party-result']:
  p=directory/(f'dfdebug-party-{r.pid}.result.json' if tag=='party-result' else f'dfdebug-{tag}-{r.pid}.json')
  if p.exists():paths[(role,tag)]=[p,p.stat().st_mtime_ns,hashlib.sha256(p.read_bytes()).hexdigest()]
began=time.perf_counter();metadata_at=-1;focus=False;foreground=[]
try:
 while time.perf_counter()-began<20:
  now=time.perf_counter()-began
  if not focus and now>=2:
   r=processes['A'];target=r.path.parent/'auto-input.focus';assert not target.exists();t=target.with_name('auto-input.focus.perf2.tmp');t.write_bytes(str(r.pid).encode('ascii'));t.replace(target);focus=True
  fg=c.c_ulong();u.GetWindowThreadProcessId(u.GetForegroundWindow(),c.byref(fg))
  for role,r in processes.items():
   data=r.raw(0xB5DD20,32);counter=struct.unpack_from('<I',data,20)[0];v=records[role]
   if v['last_counter'] is not None and counter!=v['last_counter']:
    delta=(counter-v['last_counter'])&0xffffffff
    if delta<10000:
     current=(v['last_poll'],now);prior=v['event_bounds'];entry=dict(t=now,counter_delta=delta,foreground_pid=fg.value,native_fps=struct.unpack_from('<f',data,28)[0])
     if delta==1 and prior:
      entry.update(interval_lower_ms=max(0,(current[0]-prior[1])*1000),interval_upper_ms=(current[1]-prior[0])*1000)
     v['updates'].append(entry);v['event_bounds']=current
    else:v['counter_resets']+=1;v['event_bounds']=None
   v['last_counter']=counter;v['last_poll']=now
  if now-metadata_at>=.025:
   metadata_at=now;foreground.append(dict(t=now,pid=fg.value))
   for key,v in paths.items():
    try:stamp=v[0].stat().st_mtime_ns
    except OSError:continue
    if stamp!=v[1]:file_changes.append(dict(t=now,role=key[0],file=key[1],foreground_pid=fg.value));v[1]=stamp
   for role,r in processes.items():
    try:s=json.loads((directory/f'dfdebug-client-{r.pid}.json').read_bytes())
    except (OSError,ValueError):continue
    if s['sequence']!=seen_sdk.get(role):
     seen_sdk[role]=s['sequence'];captures[role].append(dict(t=now,scene=s['scene'],generation=s['generation'],sequence=s['sequence'],capture_us=s['capture_duration_us'],capture_age_ms=r.k.GetTickCount64()-s['captured_tick_ms'],foreground_pid=fg.value,town=s['town'],minimap=s['ui']['town_minimap']))
  time.sleep(.003)
finally:
 for role,r in processes.items():
  io=IO();r.k.GetProcessIoCounters(r.handle,c.byref(io));records[role]['io_after']={n:getattr(io,n) for n,_ in IO._fields_};r.identity();r.close()
summary={}
for role,v in records.items():
 f=[x for x in v['updates'] if 'interval_lower_ms'in x and x['foreground_pid']==v['identity']['Id'] and x['t']>=3];cost=[x['capture_us'] for x in captures[role]];age=[x['capture_age_ms'] for x in captures[role]]
 summary[role]=dict(foreground_single_update_intervals=len(f),observed_midpoint_ms=dict(median=statistics.median([(x['interval_lower_ms']+x['interval_upper_ms'])/2 for x in f]) if f else None,p99=pct([(x['interval_lower_ms']+x['interval_upper_ms'])/2 for x in f],.99)),proven_lower_bound_over50_ms=sum(x['interval_lower_ms']>50 for x in f),capture_us=dict(samples=len(cost),median=statistics.median(cost),max=max(cost)),capture_age_ms=dict(median=statistics.median(age),p95=pct(age,.95),max=max(age)),file_replacements={name:sum(x['role']==role and x['file']==name for x in file_changes) for name in ['client','party','party-result']},write_ops_delta=v['io_after']['write_ops']-v['io_before']['write_ops'])
 if (role,'party-result') in paths:summary[role]['result_bytes_unchanged']=hashlib.sha256(paths[(role,'party-result')][0].read_bytes()).hexdigest()==paths[(role,'party-result')][2]
out=dict(schema='native-update-counter-observation/v2',duration_seconds=20,timing_scope='B5DD34 increments at common tail of original timer; event time brackets inferred from external polls, not GPU Present. B5DD20 is FPS-window epoch and excluded.',summary=summary,records=records,foreground=foreground,file_changes=file_changes,sdk_captures=captures,observer_seconds=time.perf_counter()-began)
with (Z/'perf'/(LABEL+'.json')).open('x',encoding='utf-8') as f:json.dump(out,f,indent=2)
print(json.dumps(summary))
