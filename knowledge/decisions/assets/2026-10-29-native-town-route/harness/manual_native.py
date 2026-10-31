import h,json,time
from goaltest_v3 import route

def latest(role):
 end=time.monotonic()+3
 while time.monotonic()<end:
  raw=route(role)
  if raw:return json.loads(raw)
  time.sleep(.03)
 raise RuntimeError('No native route')

def wait(role,predicate):
 end=time.monotonic()+5
 while time.monotonic()<end:
  v=latest(role)
  if predicate(v):return v
  time.sleep(.035)
 raise RuntimeError('Native decision not observed')

def unchanged(role,label,steps):
 before=route(role);s0=h.snapshot(role);h.keys(role,label+'-gesture',steps);time.sleep(.35);after=route(role);s1=h.snapshot(role)
 h.save(label,dict(before_route=json.loads(before),after_route=json.loads(after),before=s0,after=s1,exact_route_bytes_unchanged=before==after))
 assert before==after

if __name__=='__main__':
 role='A'
 if not h.snapshot(role)['ui']['town_minimap']['visible']:h.keys(role,'A-manual-open-map',[dict(t=500,op='key',vk=78,dur=250)])
 assert h.snapshot(role)['ui']['town_minimap']['visible']
 prior=latest(role)['request_id'];h.keys(role,'A-manual-route-start',[dict(t=500,op='rclick',x=410,y=263,dur=180)])
 started=wait(role,lambda v:v['request_id']>prior and v['status']=='moving');assert started['source']=='minimap'
 # NPC right click must not replace the valid running route. Compare ID/goal;
 # movement progress is expected and must not be mistaken for a new decision.
 h.keys(role,'A-manual-NPC-right',[dict(t=500,op='rclick',x=280,y=182,dur=180)])
 npc_tick=h.snapshot(role)['captured_tick_ms'];after_npc=wait(role,lambda v:v['tick_ms']>npc_tick);assert after_npc['request_id']==started['request_id'] and after_npc['goal']==started['goal'] and after_npc['status']=='moving'
 h.keys(role,'A-manual-close-map',[dict(t=500,op='key',vk=78,dur=250)])
 close_tick=h.snapshot(role)['captured_tick_ms'];before_key=wait(role,lambda v:v['request_id']==started['request_id'] and v['status']=='moving' and v['tick_ms']>close_tick)
 assert not h.snapshot(role)['ui']['town_minimap']['visible']
 h.keys(role,'A-manual-left-key',[dict(t=500,op='key',vk=37,dur=220)])
 stopped=wait(role,lambda v:v['request_id']==started['request_id'] and v['status']=='interrupted')
 s0=h.snapshot(role);raw=route(role);time.sleep(2.25);s1=h.snapshot(role)
 assert route(role)==raw and s1['town']['direction_raw']==dict(x=4,y=4) and s1['town']['auto_move_active'] is False
 assert all(abs(s0['town']['position'][k]-s1['town']['position'][k])<=.01 for k in ['x','y'])
 h.save('A-native-manual-interruption',dict(started=started,after_npc=after_npc,before_key=before_key,terminal=stopped,after_key=s0,after_wait=s1,wait_ms=2250,route_bytes_unchanged=True))
 print('PASS NPC right retains active goal; closing map keeps route moving; left key interrupts without resume')
