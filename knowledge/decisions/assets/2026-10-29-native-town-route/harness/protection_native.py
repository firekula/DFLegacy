import h,json,time
from manual_native import latest,wait,unchanged
from goaltest_v3 import route
from mapread import inspect
role='A'
prior=latest(role)['request_id']
h.keys(role,'A-takeover-old-goal',[dict(t=500,op='rclick',x=410,y=263,dur=180)])
s=h.snapshot(role);old=wait(role,lambda v:v['request_id']>prior and v['status']=='moving' and v['tick_ms']>s['captured_tick_ms'])
h.keys(role,'A-takeover-new-goal',[dict(t=500,op='rclick',x=300,y=201,dur=180)])
new=wait(role,lambda v:v['request_id']>old['request_id'])
terminal=wait(role,lambda v:v['request_id']==new['request_id'] and v['status']=='arrived')
time.sleep(.3);after=h.snapshot(role);raw=route(role);time.sleep(2.1)
assert route(role)==raw and latest(role)['goal']==dict(town_id=2,area_id=2,x=1734,y=263)
assert after['captured_tick_ms']>terminal['tick_ms'] and all(abs(after['town']['position'][k]-terminal['goal'][k])<=3 for k in ['x','y'])
h.save('A-valid-target-takeover',dict(old_active=old,new_target=new,terminal=terminal,after=after,after_wait=h.snapshot(role),route_bytes_unchanged=True,wait_ms=2100))
# Closed control and nonprojected canvas must never mint map goals.
unchanged(role,'A-control-right-no-goal',[dict(t=500,op='rclick',x=430,y=17,dur=180)])
unchanged(role,'A-outside-projection-no-goal',[dict(t=500,op='rclick',x=20,y=400,dur=180)])
for label,x,field in [('region-menu',430,'0x1c'),('npc-menu',596,'0x64')]:
 h.keys(role,'A-'+label+'-open',[dict(t=500,op='click',x=x,y=17,dur=180)])
 ui=inspect(role);h.save('A-'+label+'-open-state',ui);assert ui['menus'][field]['visible'],ui['menus']
 unchanged(role,'A-'+label+'-right-no-goal',[dict(t=500,op='rclick',x=300,y=201,dur=180)])
 h.keys(role,'A-'+label+'-close',[dict(t=500,op='click',x=x,y=17,dur=180)])
 assert not inspect(role)['menus'][field]['visible']
# A hidden cached plane must not create a map route. Original ground behavior
# remains authoritative, so this test asserts route identity rather than no move.
h.keys(role,'A-hidden-map-close',[dict(t=500,op='key',vk=78,dur=250)])
assert not h.snapshot(role)['ui']['town_minimap']['visible']
unchanged(role,'A-hidden-map-right-no-route',[dict(t=500,op='rclick',x=300,y=201,dur=180)])
h.keys(role,'A-hidden-map-manual-stop',[dict(t=500,op='key',vk=37,dur=220)])
raw=route(role);h.keys(role,'A-hidden-map-reopen',[dict(t=500,op='key',vk=78,dur=250)])
assert route(role)==raw and h.snapshot(role)['ui']['town_minimap']['visible']
h.save('A-reopened-map-no-replay',dict(route=latest(role),snapshot=h.snapshot(role),unchanged=True))
print('PASS valid target supersedes active route; control/outside/both menus/hidden map produce no new goal; reopen no replay')
