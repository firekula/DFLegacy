from pathlib import Path
from datetime import datetime,timedelta,timezone
import json,hashlib
root=Path(r'G:\Game\DNF_ACT1\DFLegacy\.tmp\town-position-followup-20261029');off=root/'offline';tz=timezone(timedelta(hours=8));data={c:json.loads((off/f'{c}-closed-final.json').read_text()) for c in 'AB'}
def at(row):return datetime.fromisoformat(row['socket_last_time']).replace(tzinfo=tz)
results=[]
for p in sorted((root/'live').glob('*.json')):
 d=json.loads(p.read_text(encoding='utf-8-sig'));role=d['role'];other='B' if role=='A' else 'A';start=datetime.fromisoformat(d['utc_start']);end=start+timedelta(seconds=d['samples'][-1]['t']);action=d['action'];tx=[r for r in data[role]['rows'] if r['protocol']=='CMD37' and start<=at(r)<=end];rx=[r for r in data[other]['rows'] if r['protocol']=='NOTI22' and r['uid']==str(d['uid']) and start<=at(r)<=end]
 matches=[]
 for r in tx:
  candidates=[v for v in rx if v['body9_hex'][4:]==r['body9_hex'][4:]]
  matches.append({'tx':r,'rx_candidates':candidates,'unique':len(candidates)==1,'socket_log_delta_ms':(at(candidates[0])-at(r)).total_seconds()*1000 if len(candidates)==1 else None})
 tail=[s for s in d['samples'] if s['t']>=8];last=d['samples'][-1]
 result={'trial':p.name,'role':role,'uid':d['uid'],'utc_start':start.isoformat(),'utc_end':end.isoformat(),'action_begin_t':action.get('begin_t'),'action_end_t':action.get('end_t'),'tx_count':len(tx),'peer_rx_count':len(rx),'matches':matches,'tail_8_to_end':{'samples':len(tail),'min_dx':min(s['dx'] for s in tail),'max_dx':max(s['dx'] for s in tail),'min_dy':min(s['dy'] for s in tail),'max_dy':max(s['dy'] for s in tail)},'last_native':last,'trial_sha256':hashlib.sha256(p.read_bytes()).hexdigest()};results.append(result)
 print(p.name,'TX/RX',len(tx),len(rx),'end own/peer',last['own']['position'],last['peer']['position'],'tail',result['tail_8_to_end'])
 for m in matches:print(' ',m['tx']['socket_last_time'],m['tx']['sequence'],m['tx']['x'],m['tx']['y'],m['tx']['direction_raw'],m['tx']['movement_value'],'matches',len(m['rx_candidates']),'delta_ms',m['socket_log_delta_ms'])
(off/'six-window-position-match-closed-final.json').write_text(json.dumps({'schema':'town-six-window-match/v1','socket_timezone':'+08:00','comparison':'CMD37 body[2:9] equals NOTI22 body[2:9], UID from native trial identity; do not compare sequence against UID','trials':results},indent=2),encoding='utf8')

