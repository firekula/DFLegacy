from pathlib import Path
import sys,json,subprocess,time
sys.path.insert(0,str(Path(__file__).resolve().parent));import h
prefix=h.J['Phase'];before={r:h.snapshot(r) for r in 'AB'};assert all(s['scene']==1 for s in before.values())
steps=[dict(t=100,op='key',vk=39,dur=700),dict(t=1500,op='move',x=430,y=215),dict(t=2000,op='click',x=430,y=215,dur=500),dict(t=3000,op='click',x=430,y=215,dur=500)]
p=subprocess.run([sys.executable,str(h.ROOT/'control.py'),'A',prefix+'-choose-normal-Lorien',json.dumps(steps),'--no-shot'],capture_output=True,text=True);assert p.returncode==0,(p.stdout,p.stderr)
end=time.monotonic()+10
while time.monotonic()<end:
 try:
  after={r:h.snapshot(r) for r in 'AB'}
  if all(s['scene']==3 and sorted(p['uid'] for p in s['players'])==[1,2] for s in after.values()):break
 except h.state.SnapshotError:pass
 time.sleep(.06)
else:raise RuntimeError('not both dungeon')
h.save(prefix+'-same-room-entry',dict(before=before,after=after))
print(json.dumps({r:dict(room=s['room'],me=s['me']) for r,s in after.items()}))
