"""Independent closed one-match30AD checker; historical743 checker is separate."""
from pathlib import Path
import json,hashlib,datetime,sys
sys.dont_write_bytecode=True
R=Path(__file__).resolve().parent
APPROVED=dict(managed_source_commit='5078da27a4807695f002ceea61333c43e1932ce4',native_source_commit='ab1e6b685021f6cd6f8d125fc04dc0c5c50e931f',build_manifest_sha256='09EFFBE39AB99B377AAFB540933EA75919E9E04642FCE79F4D6AF0CB1CC05204',native_approval_sha256='832A30A725206FE2A789EFAD4EE7A3194E03B49CCF781E3D931D4C3C99BB8CD8',server_sha256='A0EE99650A49011B9527E06BB8E1C19E6A7E5D284CC51D8F9865597C9E133134',protocol_sha256='5A55089D384EFA9E4ACEC5A211EE5C551668CDDB5C93ED46A698D50250E04A4E',native_sha256='30AD9A4A3CD565DE845F74AB3D6C6D60EED1F3429EC7D53929D912F158246584')
def choose(events,direction,kind,op):return [v for v in events if (v.get('direction'),v.get('type'),v.get('op'))==(direction,kind,op)]
def dt(value):return datetime.datetime.fromisoformat(value.replace('Z','+00:00'))
def assess(p,delta,combat,responsive):
    checks=[];timeline=[]
    def check(name,ok):
        checks.append(dict(name=name,passed=bool(ok)))
        if not ok:raise ValueError(name)
    def one(ev,d,k,op):
        v=choose(ev,d,k,op);check('exactly one '+d+'/'+str(op),len(v)==1);return v[0]
    try:
        check('explicit final30AD phase',p['schema']=='act1-final-pvp-30ad-packets/v1' and p['phase']=='final-pvp-30AD')
        cs={c['role']:c for c in p['captures']};check('both frozen client identities',set(cs)=={'A','B'} and cs['A']['pid']==52320 and cs['B']['pid']==60896)
        check('original capture prefixes independently equal',len(p['prefix_comparisons'])==2 and all(v['equal'] for v in p['prefix_comparisons']))
        before=p['economy_before'];after=p['economy_after'];b={v['role']:v for v in before['characters']};a={v['role']:v for v in after['characters']}
        check('pinned before and stopped final state hashes',before['sha256']==delta['before_sha256']=='BDDD24C287F3FF1DF2D2C1C12155B967F727B0EA0833FC8FBE9639677DB6A7D3' and after['sha256']==delta['after_sha256']=='14D4526DC7BA4A847FC5F1C1128C79F21F14FCF40F985F86132A5F76D76298B4')
        check('three historical receipts retained and one new',len(before['receipts'])==3 and len(after['receipts'])==4 and after['receipts'][:3]==before['receipts'] and delta['historical_receipts_unchanged'] and delta['new_unique_dual_receipt'])
        check('all nonPvP full-state fields exactly equal',delta['all_other_full_state_fields_equal'] is True)
        awards={x['role']:x for x in after['receipts'][-1]['selected_awards']};check('atomic dual award',set(awards)=={'A','B'})
        cycles={};uids={}
        for role,tx in [('A',59),('B',71)]:
            c=cs[role];ev=c['events'];counts=c['counts']
            check(role+' complete error-free TX framing CRC',c['status']=='observed' and not c['errors'] and counts['TX_frames']==counts['TX_crc_valid']==tx and not counts.get('TX_crc_errors',0) and not counts.get('incomplete_tcp_bytes',0) and not c['pending_log_line_bytes'] and all(not counts.get(k,0) for k in ['selected_layout_errors','omitted_selected_events','omitted_errors']))
            logins=[v['channel_mode'] for v in ev if v.get('event')=='login_reply'];check(role+' normal PvP normal PvP same process',logins==[0,4,0,4])
            exits=choose(ev,'RX',1,3);check(role+' three successful channel exits',len(exits)==3 and all(v['success'] for v in exits))
            start=one(ev,'RX',0,45);check(role+' map3 individual mode0',start['selected_map']==3 and start['mode']==0)
            seats={}
            for v in choose(ev,'RX',0,43):
                if v['at']<start['at']:
                    for seat in v['seats']:
                        if 0<seat['uid']<65535:seats[seat['seat']]=seat['uid']
            check(role+' exactly two occupied seats',set(seats)=={0,1});uids[role]=seats
            part=[v for v in ev if v['at']>=start['at']]
            death=one(part,'RX',0,46);rank_request=one(part,'RX',0,49);rank=one(part,'TX',1,61);result=one(part,'RX',0,47);ack=one(part,'TX',1,60)
            records=[v for v in choose(part,'RX',0,48) if rank['at']<=v['at']<=result['at']];check(role+' one settlement record',len(records)==1);record=records[0]
            check(role+' ordered death result lifecycle',start['at']<death['at']<=rank_request['at']<=rank['at']<=record['at']<=result['at']<ack['at'])
            check(role+' B victory no draw ordinaryEXP0',(death['dead_seat'],death['killer_seat'],result['winning_seat'])==(0,1,1) and result['draw'] is False and result['character_exp']==0)
            own=next(v for v in result['ranks'] if v['uid']==seats[0 if role=='A' else 1]);check(role+' actual submitted score preserved',rank['representable_total'] and own['total_score']==sum(rank['scores'])==rank['total_score'])
            award=awards[role];expected=dict(VictoryPoints=result['victory_points'],Points=record['total_pvp_exp'],Grade=result['grade'],Wins=record['wins'],Losses=record['losses'],PlayCount=4,Count=4,ExperienceDelta=result['experience_delta'],GradeExtension=record['grade_extension'])
            check(role+' exact receipt versus wire',all(award[k]==v for k,v in expected.items()))
            mapping={'VictoryPoints':'VictoryPoints','PvpPoints':'Points','PvpGrade':'Grade','PvpWins':'Wins','PvpLosses':'Losses','PvpPlayCount':'PlayCount','PvpCount':'Count','PvpGradeExtension':'GradeExtension'}
            check(role+' canonical wallet versus atomic award',all(a[role][k]==award[v] for k,v in mapping.items()))
            check(role+' ordinary winner loser rule and one award',b[role]['PvpGrade']<=9 and a[role]['VictoryPoints']-b[role]['VictoryPoints']==(1 if role=='A' else 4) and a[role]['PvpPoints']-b[role]['PvpPoints']==(0 if role=='A' else 1000) and a[role]['PvpWins']-b[role]['PvpWins']==(0 if role=='A' else 1) and a[role]['PvpLosses']-b[role]['PvpLosses']==(1 if role=='A' else 0) and a[role]['PvpPlayCount']==b[role]['PvpPlayCount']+1 and a[role]['PvpCount']==b[role]['PvpCount']+1)
            check(role+' native threshold agrees grade',own['current_threshold']<=own['total_pvp_exp']<own['next_threshold'] and own['grade']==award['Grade'])
            later=[v for v in choose(part,'RX',0,48) if v['at']>ack['at']];check(role+' no second award on normal and PvP reentry',len(later)==2 and all(v['wins']==record['wins'] and v['losses']==record['losses'] and v['total_pvp_exp']==record['total_pvp_exp'] for v in later))
            cycles[role]=dict(start=start,rank_request=rank_request,result=result,ack=ack)
            timeline.append(dict(role=role,logins=[dict(at=v['at'],mode=v['channel_mode']) for v in ev if v.get('event')=='login_reply'],start=start['at'],result=result['at'],ack=ack['at']))
        check('dual membership and result agree',uids['A']==uids['B'] and cycles['A']['result']['ranks']==cycles['B']['result']['ranks'])
        death=one(cs['A']['events'],'TX',1,58);check('actual A death reports B runtime UID',death['killer_uid']==uids['A'][1] and cycles['A']['start']['at']<death['at']<=cycles['A']['rank_request']['at'])
        check('no timeout or B death substitute',not choose(cs['B']['events'],'TX',1,58) and all(not choose(c['events'],'TX',1,59) for c in cs.values()))
        check('native create and join',one(cs['A']['events'],'TX',1,53)['at']<cycles['A']['start']['at'] and one(cs['B']['events'],'RX',1,54)['success'])
        samples=combat['samples'];check('native VM_READ HP12742 to0 and B15042 unchanged',combat['outcome']=='native_zero_hp_observed' and samples[0]['A_hp']==12742 and samples[-1]['A_hp']==0 and all(x['B_hp']==15042 for x in samples))
        samples=responsive['samples'];check('390 response samples with no failure',responsive['failure'] is None and len(samples)==390 and all(v['responding'] for v in samples))
        for v in responsive['control_windows']:
            ss=[dt(x['at_utc']) for x in samples if x['role']==v['role']];low,high=min(ss),max(ss);begin,end=dt(v['started']),dt(v['finished']);overlap=max(0,(min(high,end)-max(low,begin)).total_seconds());coverage='complete' if low<=begin and end<=high else 'partial' if overlap>0 else 'none'
            check(v['role']+'/'+v['action']+' honest sampling coverage',v['coverage']==coverage and abs(v['overlap_seconds']-overlap)<0.001)
    except (ValueError,KeyError,IndexError,StopIteration,TypeError):
        if not checks or checks[-1]['passed']:checks.append(dict(name='required evidence structure',passed=False))
    return dict(schema='act1-final-pvp-30ad-check/v1',status='PASS_AS_SCOPED' if all(v['passed'] for v in checks) else 'FAIL',passed=sum(v['passed'] for v in checks),total=len(checks),checks=checks,timeline=timeline,limits=['One death match on final30AD; historical743 three matches remain separate.','Stopped disk snapshot and same-client reentry persist the fourth receipt; independent server restart of receipt4 was not observed.','Sampling does not cover B normal-world entry or either second PvP entry.','UDP application CRC/native peer behavior is not asserted by diagnostic counts.','Final normal runtime restoration/delivery remains separate.'])
def main():
    manifest=json.loads((R/'final30ad-sha256-manifest.json').read_text(encoding='utf-8'))
    for v in manifest['files']:
        raw=(R/v['name']).read_bytes()
        if len(raw)!=v['bytes'] or hashlib.sha256(raw).hexdigest().upper()!=v['sha256']:raise ValueError('Published asset hash mismatch')
    binding=json.loads((R/'final30ad-binding.json').read_text(encoding='utf-8'))
    if binding['approved']!=APPROVED or binding['server_pid']!=64868 or binding['clients']!={'A':52320,'B':60896}:raise ValueError('Final build identity mismatch')
    data=[json.loads((R/f'final30ad-{n}.json').read_text(encoding='utf-8')) for n in ['packets','state-delta','combat','responsiveness']]
    result=assess(*data);print(json.dumps(result,ensure_ascii=False,indent=2));return 0 if result['status']=='PASS_AS_SCOPED' else 1
if __name__=='__main__':raise SystemExit(main())
