import h,json,sys,time,subprocess

def route(role):
 c,_=h.identity(role);p=h.DIRECTORY/f"dfdebug-route-{c['Id']}.json"
 for attempt in range(6):
  try:return p.read_bytes() if p.exists() else None
  except OSError:
   if attempt==5:return None
   time.sleep(.015)

def run(role,label,steps,want='arrived'):
 before=h.snapshot(role);prior=route(role);began=h.state.now_tick_ms();history=[];states=[];last=prior;last_area=None
 p=subprocess.Popen([sys.executable,str(h.ROOT/'control.py'),role,label+'-input',json.dumps(steps),'--no-shot'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 deadline=time.monotonic()+125;terminal=None
 while time.monotonic()<deadline:
  h.identity(role);raw=route(role)
  if raw and raw!=last:
   v=json.loads(raw);history.append(v);last=raw
   with (h.EVIDENCE/(label+'-route-updates.jsonl')).open('ab') as f:f.write(raw+b'\n')
   print(json.dumps(dict(elapsed=h.state.now_tick_ms()-began,status=v['status'],reason=v['reason'],request_id=v['request_id'],steps=v['steps'],area=None if v['observed'] is None else [v['observed']['town_id'],v['observed']['area_id']])),flush=True)
   if v['status'] in ('arrived','interrupted','rejected'):
    terminal=v;break
  try:
   s=h.snapshot(role);area=(s['town']['town_id'],s['town']['area_id'])
   if area!=last_area:states.append(s);last_area=area
  except h.state.SnapshotError:pass
  if not history and h.state.now_tick_ms()-began>8000:break
  time.sleep(.035)
 out=p.communicate(timeout=12)[0].decode('utf-8',errors='replace');print(out,flush=True);assert p.returncode==0,out
 time.sleep(.3);after=h.snapshot(role)
 proof=dict(role=role,before=before,after=after,history=history,area_states=states,elapsed_ms=h.state.now_tick_ms()-began,input_steps=steps,native_route_only=True,expected_status=want,terminal=terminal)
 h.save(label,proof)
 assert terminal and terminal['status']==want,proof
 assert terminal['source']=='minimap'
 if want=='arrived':
  t=after['town'];g=terminal['goal']
  assert t['town_id']==g['town_id'] and t['area_id']==g['area_id']
  assert all(abs(t['position'][k]-g[k])<=3 for k in ['x','y']) and t['direction_raw']==dict(x=4,y=4)
  assert after['captured_tick_ms']>terminal['tick_ms']
 print(json.dumps(dict(PASS=label,elapsed_ms=proof['elapsed_ms'],steps=terminal['steps'],goal=terminal['goal'],areas=[(s['town']['town_id'],s['town']['area_id']) for s in states])),flush=True)
 return proof
if __name__=='__main__':run(sys.argv[1],sys.argv[2],json.loads(sys.argv[3]),sys.argv[4] if len(sys.argv)>4 else 'arrived')
