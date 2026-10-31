import h,json,time,concurrent.futures
from tools.debug import client_native_route as nav,client_party as party

def travel(role):
 c,start=h.identity(role);before=h.snapshot(role);result=nav.execute(h.DIRECTORY,pid=c['Id'],uid=before['me']['uid'],started=start,town_id=1,area_id=0,x=1500,y=247,timeout_seconds=80);after=h.snapshot(role)
 h.save(role+'-single-goal-to-forest-approach',dict(before=before,result=result,after=after));assert result['ok'];return role
with concurrent.futures.ThreadPoolExecutor(2) as executor:print('Native routes complete',list(executor.map(travel,'AB')))
def status(role,predicate):
 c,start=h.identity(role);end=time.monotonic()+5
 while time.monotonic()<end:
  try:
   v=party.read_status(h.DIRECTORY,pid=c['Id'],uid={'A':1,'B':2}[role],process_started_filetime=start)
   if predicate(v):return v
  except party.PartyError as e:
   if e.code not in ['stale_state','unreadable_response','invalid_state','unsupported_scene']:raise
  time.sleep(.04)
 raise RuntimeError('Party UI status unavailable')
status('A',lambda v:True);c,start=h.identity('A');request=party.execute(h.DIRECTORY,pid=c['Id'],uid=1,process_started_filetime=start,operation='request',target_uid=2);assert request['status']=='dispatched'
pending=status('B',lambda v:v['pending'] is not None and v['pending']['can_reply']);c,start=h.identity('B');reply=party.execute(h.DIRECTORY,pid=c['Id'],uid=2,process_started_filetime=start,operation='accept',target_uid=1);assert reply['status']=='dispatched'
time.sleep(.3);h.save('party-native-api-formed',dict(request=request,pending=pending,reply=reply,snapshots={r:h.snapshot(r) for r in 'AB'}))
# The dungeon-gate area is intentionally outside the general Town route catalog's
# supported targets. One original crossing and one native same-area destination
# move enter its existing selector flow.
for role in 'BA':h.keys(role,role+'-cross-forest-gate',[dict(t=500,op='key',vk=39,dur=900)])
for role in 'AB':
 s=h.snapshot(role);assert (s['town']['town_id'],s['town']['area_id'])==(1,2)
 result=h.nav.execute(h.DIRECTORY,pid=s['pid'],uid=s['me']['uid'],started=s['process_started_filetime'],x=560 if role=='B' else 600,y=247,wait='arrived',timeout_seconds=10)
 h.save(role+'-native-gate-interior-position',dict(result=result,after=h.snapshot(role)));assert result['ok']
print('PASS two original logins / single-goal Town travel / request+accept / both at dungeon gate')
