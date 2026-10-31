"""Read-only independent semantic checker. Run with Python 3, no game/build required.
Checks the published sanitized evidence, not a replacement for native UI review.
Private raw re-decode/freeze is recorded by hashes; raw/auth/state are not published.
"""
from pathlib import Path
import json,hashlib,collections
R=Path(__file__).resolve().parent
def read(n):return json.loads((R/n).read_text(encoding='utf-8-sig'))
def check(ok,label):
 if not ok:raise AssertionError(label)
def choose(events,direction,kind,op):return [e for e in events if e.get('direction')==direction and e.get('type')==kind and e.get('op')==op]
def one(events,direction,kind,op):
 rows=choose(events,direction,kind,op);check(len(rows)==1,f'exactly one {direction}/{kind}/{op}');return rows[0]
def main():
 manifest=read('sha256-manifest.json')
 for row in manifest['files']:
  p=R/row['name'];check(p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest().upper()==row['sha256'],'asset hash '+row['name'])
 r=read('native-packets.json');check(r['status']=='observed','capture status')
 cs={c['role']:c for c in r['captures']};check(set(cs)=={'A','B'},'two roles')
 cycles={};traffic={};summary=[]
 for role,expected in [('A',107),('B',242)]:
  c=cs[role];check(not c['errors'] and c['status']=='observed','capture errors')
  check(c['counts']['TX_frames']==c['counts']['TX_crc_valid']==expected,'all TX CRC')
  check(c['counts']['incomplete_tcp_bytes']==c['pending_log_line_bytes']==0,'complete capture')
  check(not c['counts'].get('omitted_selected_events',0),'untruncated events')
  ev=c['events'];logins=[e['channel_mode'] for e in ev if e.get('event')=='login_reply']
  check(logins==[0,4,0,4],'same PID normal/PvP/normal/PvP')
  exits=choose(ev,'RX',1,3);check(len(exits)==3 and all(e['success'] for e in exits),'three channel exits')
  starts=choose(ev,'RX',0,45);check(len(starts)==3,'three starts')
  cycles[role]=[]
  for i,s in enumerate(starts):
   check((s['selected_map'],s['mode'])==(3,0),'Hendon Pub individual')
   end=starts[i+1]['at'] if i<2 else '9999'
   part=[e for e in ev if s['at']<=e['at']<end]
   request=one(part,'RX',0,49);rank=one(part,'TX',1,61);record=one(part,'RX',0,48) if i<2 else choose(part,'RX',0,48)[0]
   result=one(part,'RX',0,47);ack=one(part,'TX',1,60)
   check(s['at']<request['at']<=rank['at']<=record['at']<=result['at']<ack['at'],'ordered lifecycle')
   check(result['winning_seat']==1 and not result['draw'] and result['character_exp']==0,'B win, no character EXP')
   check(result['victory_points']==(i+1)*(1 if role=='A' else 4),'recipient VP')
   check(result['experience_delta']==(0 if role=='A' else 1000),'recipient PvP EXP')
   own=[x for x in result['ranks'] if x['uid']==(1 if role=='A' else 2)]
   check(len(own)==1 and own[0]['total_score']==sum(rank['scores']),'submitted score preserved')
   check(record['wins']==(0 if role=='A' else i+1) and record['losses']==(i+1 if role=='A' else 0),'record W/L')
   cycles[role].append({'start':s,'rank_request':request,'rank':rank,'record':record,'result':result,'ack':ack,'events':part})
  t=collections.Counter()
  for flow in c['udp_flows']:
   for k,v in flow['headers'].items():t[flow['direction']+'_'+k]+=v
  traffic[role]=t
  summary.append({'role':role,'TX_crc_valid':expected,'login_modes':logins,'match_count':3,'udp_diagnostic_counts':dict(t)})
 check(one(cs['A']['events'],'TX',1,53)['at']<cycles['A'][0]['start']['at'],'create')
 check(one(cs['B']['events'],'RX',1,54)['success'],'join success')
 for i in range(3):
  check(cycles['A'][i]['result']['ranks']==cycles['B'][i]['result']['ranks'],'dual result ranks agree')
  all_events=cycles['A'][i]['events']+cycles['B'][i]['events']
  if i<2:check(len(choose(all_events,'TX',1,59))==1 and not choose(all_events,'TX',1,58),'timeout terminal')
  else:
   death=one(cycles['A'][i]['events'],'TX',1,58);check(death['killer_uid']==2,'actual A death')
   for role in ('A','B'):
    notice=one(cycles[role][i]['events'],'RX',0,46)
    check((notice['dead_seat'],notice['killer_seat'])==(0,1),'death seats')
    check(death['at']<=notice['at']<=cycles[role][i]['rank_request']['at'],'death before rank')
 for key in ('reliable_blocks','unreliable_blocks','ack_shape_blocks'):
  check(traffic['A']['TX_'+key]==traffic['B']['RX_'+key]>0,'A to B framing '+key)
  check(traffic['B']['TX_'+key]==traffic['A']['RX_'+key]>0,'B to A framing '+key)
 receipts=r['economy']['receipts'];check(len(receipts)==3,'three receipts')
 for i,receipt in enumerate(receipts):
  awards={x['role']:x for x in receipt['selected_awards']};check(set(awards)=={'A','B'},'atomic dual awards')
  for role in ('A','B'):
   a=awards[role];res=cycles[role][i]['result'];rec=cycles[role][i]['record']
   for k,v in {'VictoryPoints':res['victory_points'],'Points':rec['total_pvp_exp'],'Grade':res['grade'],'Wins':rec['wins'],'Losses':rec['losses'],'PlayCount':i+1,'Count':i+1,'ExperienceDelta':res['experience_delta']}.items():check(a[k]==v,'receipt '+role+'/'+k)
 delta=read('native-state-delta.json');check(delta['all_other_fields_equal'] and delta['three_unique_dual_receipts'],'canonical comparison')
 check(delta['after_sha256']==r['economy']['sha256'],'state snapshot binding')
 check(len(delta['changes'])==2,'two changed characters')
 fight=read('native-combat.json');samples=fight['samples'];check(fight['outcome']=='native_zero_hp_observed','native death observer')
 check(samples[0]['target_hp']==12742 and samples[-1]['target_hp']==0,'target HP transition')
 check(all(x['attacker_hp']==15042 for x in samples),'attacker HP unchanged')
 resp=read('native-responsiveness.json')['samples'];check(len(resp)==44 and all(x['responding'] for x in resp),'22 dual responsiveness samples')
 check(collections.Counter(x['role'] for x in resp)=={'A':22,'B':22},'responsiveness both roles')
 print(json.dumps({'checks':'PASS','historical_native_scope':'two seats / three matches / same-process channel round trip','final_delivery_binding':'PENDING','clients':summary,'canonical_receipts':3,'non_pvp_state_equal':True,'udp_application_crc':'NOT_VALIDATED: generic application parser does not cover these native block forms'},indent=2))
if __name__=='__main__':main()
