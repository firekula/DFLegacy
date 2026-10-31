from pathlib import Path
import json,subprocess,sys,time
X=Path(__file__).resolve().parent
sys.path.insert(0,str(X));import h
T=X.parent/'party-sdk-acceptance-20261029'
for role in 'AB':
 c,start=h.identity(role);age=(time.time_ns()//100+116444736000000000-start)/10000000
 if age<12:time.sleep(12-age)
 steps=json.loads((T/'evidence-party-baseline'/f'p1-{role}-login.json').read_text())['steps']
 if role=='B':next(s for s in steps if s['t']==5200)['x']=120
 subprocess.run([sys.executable,str(X/'control.py'),role,role+'-login',json.dumps(steps),'--no-shot'],check=True)
 end=time.monotonic()+6
 while time.monotonic()<end:
  try:
   rows=h.snapshot(role)
   if rows['town'] and rows['town']['town_id']>0:break
  except h.state.SnapshotError:pass
  time.sleep(.1)
 else:raise RuntimeError('Native login did not reach ready Town')
print('PASS both native original login / persisted town')
