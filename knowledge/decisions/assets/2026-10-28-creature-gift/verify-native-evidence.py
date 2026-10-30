"""Verify sanitized closed creature-gift evidence; never reads raw saves/logs.

Pass --self-test for synthetic data only. A PASS covers packet/canonical checks,
not screenshot review, loaded-binary attestation or whole-game acceptance.
"""
from pathlib import Path
import argparse, copy, datetime, hashlib, json, re, sys, uuid
sys.dont_write_bytecode = True
BASELINE = 'FB20572589A5CFB31D89E16EC6936DE96071630E432EDE787CCB0887930ABE52'
V3_RUNTIME = {'managed_source_commit': '5078da27a4807695f002ceea61333c43e1932ce4', 'native_source_commit': 'ab1e6b685021f6cd6f8d125fc04dc0c5c50e931f', 'managed_manifest_sha256': '09EFFBE39AB99B377AAFB540933EA75919E9E04642FCE79F4D6AF0CB1CC05204', 'native_approval_sha256': '832A30A725206FE2A789EFAD4EE7A3194E03B49CCF781E3D931D4C3C99BB8CD8', 'server_sha256': 'A0EE99650A49011B9527E06BB8E1C19E6A7E5D284CC51D8F9865597C9E133134', 'protocol_sha256': '5A55089D384EFA9E4ACEC5A211EE5C551668CDDB5C93ED46A698D50250E04A4E', 'native_sha256': '30AD9A4A3CD565DE845F74AB3D6C6D60EED1F3429EC7D53929D912F158246584', 'source_clone_sha256': '3E5A68F8F8322D1B726D1AF117CCB49433DE8B5F344DF307ADD3A33549B38665', 'manifest_sha256': '6DFF7938E91146D6A1B2D318ABC6ABF42368CB3DE808B2B199066E96A590035F', 'manifest_schema': 'act1-creature-row-final/v3', 'initial_clone_diff': 'none: exact bytes'}
STAGES = ('baseline', 'unclaimed', 'restart_unclaimed', 'claimed', 'hatched', 'relogin')
EGG, PET, PRICE = 63007, 63003, 6600

def require(value):
    if not value: raise ValueError('Sanitized evidence contract invalid')
def sha(raw): return hashlib.sha256(raw).hexdigest().upper()
def valid_hash(value): return isinstance(value,str) and re.fullmatch(r'[0-9A-Fa-f]{64}',value) is not None
def valid_uuid(value):
    try: return uuid.UUID(value).int != 0
    except (ValueError, TypeError, AttributeError): return False
def private_fields_absent(value):
    if isinstance(value,dict):
        forbidden={'accounts','characters','username','password','auth','token','raw_payload','raw_hex','path','local','remote'}
        return not any(str(k).lower() in forbidden for k in value) and all(private_fields_absent(v) for v in value.values())
    if isinstance(value,list):return all(private_fields_absent(v) for v in value)
    if isinstance(value,str):return re.search(r'\b(?:\d{1,3}\.){3}\d{1,3}\b',value) is None
    return True
def eggs(role):
    return [x for space in ('Inventory','AvatarInventory','CreatureInventory') for x in role.get(space,[]) if x.get('ItemId') in (EGG,PET)]
def gifts(role): return [m for m in role.get('Mails',[]) if (m.get('Attachment') or {}).get('ItemId') in (EGG,PET)]
def assess_entry_red(evidence):
    require(evidence.get('schema')=='act1-creature-gift-entry-red/v1' and evidence.get('case')=='creatureegg' and private_fields_absent(evidence))
    checks=[]
    def check(name,value):checks.append(dict(name=name,passed=bool(value)))
    check('closed INITIAL result is entry RED only',evidence.get('phase')=='initial' and evidence.get('native_result')=='ENTRY_RED')
    check('source clone distinguished from postboot and closed hashes',evidence.get('source_clone_sha256')==BASELINE and valid_hash(evidence.get('baseline_state_sha256')) and valid_hash(evidence.get('closed_state_sha256')) and evidence['baseline_state_sha256']!=BASELINE)
    check('explicit raw input hashes retained',bool(evidence['inputs']) and all(valid_hash(v['sha256']) and 0<=v['bytes']<=256*1024*1024 for v in evidence['inputs']))
    check('recorded server24040 and clients76984/59440',evidence['identities']['server_pid']==24040 and {v['role']:v['pid'] for v in evidence['identities']['clients']}==dict(A=76984,B=59440))
    check('approved managed and native fingerprints',evidence['runtime']==dict(source_commit='5078da27a4807695f002ceea61333c43e1932ce4',managed_manifest_sha256='3D92263FE0E44BA5F56E3255D61CE1080E31B77D55ECDC17551BB6435B64D266',server_sha256='A0EE99650A49011B9527E06BB8E1C19E6A7E5D284CC51D8F9865597C9E133134',protocol_sha256='5A55089D384EFA9E4ACEC5A211EE5C551668CDDB5C93ED46A698D50250E04A4E',native_sha256='7431347A19A5D8B0B963B474B9739288BA8755895B260DF04A51D80BFF65B3DC'))
    captures=evidence['captures'];check('both frozen clients independently decoded',len(captures)==2 and {v['role'] for v in captures}=={'A','B'})
    identities={v['role']:v for v in evidence['identities']['clients']};inputs={v['label']:v for v in evidence['inputs']}
    def utc(value):return datetime.datetime.fromisoformat(value.replace('Z','+00:00')).astimezone(datetime.timezone.utc)
    for c in captures:
        counts=c['counts'];role=c['role']
        check(role+' PID start and frozen raw hash bound',c['phase']=='initial' and c['pid']==identities[role]['pid'] and utc(c['start_time_utc'])==utc(identities[role]['start_time_utc']) and c['sha256']==inputs['client-'+role+'.log']['sha256'] and c['snapshot_bytes']==inputs['client-'+role+'.log']['bytes'])
        check(role+' complete raw capture and no layout errors',c['status']=='observed' and not c['errors'] and not c['pending_log_line_bytes'] and counts.get('incomplete_tcp_bytes',0)==0 and counts.get('selected_layout_errors',0)==0 and counts.get('disabled_flows',0)==0 and counts.get('omitted_selected_events',0)==0)
        check(role+' every TX CRC valid',counts['TX_frames']=={'A':19,'B':17}[role] and counts['TX_crc_valid']==counts['TX_frames'] and counts['TX_crc_errors']==0)
        check(role+' zero recipient lookup and gift orders',not any(v['type']==1 and v['op'] in (146,67) for v in c['events']))
    comparisons=evidence['previous_filtered_comparisons']
    check('earlier filtered report independently redecoded',len(comparisons)==2 and all(v['redecoded_equal'] is True and valid_hash(v['original_snapshot_sha256']) for v in comparisons))
    before,after=evidence['selected_before'],evidence['selected_after']
    check('selected cash quota items and mail unchanged',before==after)
    check('cash A26600 B997790 quota0 no egg or gift',after['A']['Cash']==26600 and after['B']['Cash']==997790 and all(after[r]['GiftReceiveCount']==0 and not eggs(after[r]) and not gifts(after[r]) for r in ('A','B')))
    rollover=evidence['source_to_postboot_changed_paths']
    check('post06 rollover contains only fatigue/default keys',bool(rollover) and all(p in ('/FatigueDayKey','/PvpLedger','/DungeonGoldLedger') or re.fullmatch(r'/Accounts/\d+/Characters/\d+/(?:UsedFatigue|MentorRelationshipExperience)',p) for p in rollover))
    check('RED-phase full-state changes only town position',all(re.fullmatch(r'/Accounts/\d+/Characters/\d+/(?:AreaId|X|Y)',p) for p in evidence['baseline_to_closed_changed_paths']))
    return dict(schema='act1-creature-gift-check/v1',stage='entry-red',checks=checks,passed=sum(v['passed'] for v in checks),total=len(checks),status='PASS' if all(v['passed'] for v in checks) else 'FAIL',native_result='ENTRY_RED',native_acceptance='GIFT_NOT_ACCEPTED',limitations=['PASS verifies the recorded failure to reach gift entry with zero orders, not successful native gifting.','Root-reviewed original screenshots establish the gray disabled row; protocol silence alone cannot prove UI state.'])
def assess(evidence, stage):
    require(evidence.get('schema') in ('act1-creature-gift-authoritative/v1','act1-creature-gift-authoritative/v3') and evidence.get('case') == 'creatureegg')
    require(private_fields_absent(evidence))
    require(stage in ('unclaimed','final'))
    checks = []
    def check(name, condition): checks.append(dict(name=name,passed=bool(condition)))
    required = STAGES[:2] if stage == 'unclaimed' else STAGES
    states = evidence['stages']; require(all(s in states for s in required))
    is_v3=evidence['schema']=='act1-creature-gift-authoritative/v3'
    if is_v3:
        check('v3 exact manifest dual source build native approval and 3E5 clone',evidence['runtime']==V3_RUNTIME)
        check('v3 original manifest included in frozen input hashes',sum(v.get('kind')=='case_manifest' and v.get('sha256')==V3_RUNTIME['manifest_sha256'] for v in evidence['inputs'])==1)
    else:
        check('source clone has separately proven FB baseline', evidence['runtime'].get('source_clone_sha256') == BASELINE)
    check('raw input hashes are bounded and explicit', bool(evidence['inputs']) and all(valid_hash(v.get('sha256')) and 0 <= v.get('bytes',-1) <= 256*1024*1024 for v in evidence['inputs']))
    check('raw observer recomputation matched every required report', all(states[s].get('observer_recomputed_equal') is True for s in required))
    comparisons = evidence.get('capture_report_comparisons',[])
    check('existing filtered capture independently redecoded', bool(comparisons) and all(c.get('equal') is True for c in comparisons))
    captures = evidence['captures']; check('nonempty frozen capture set', bool(captures))
    for c in captures:
        counts = c['counts']; label = c['phase']+'/'+c['role']+'/'+str(c['pid'])
        check(label+' exact frozen framing and complete tail', c['status']=='observed' and not c['errors'] and counts.get('disabled_flows',0)==0 and counts.get('incomplete_tcp_bytes',0)==0 and c.get('pending_log_line_bytes',0)==0)
        check(label+' every game TX CRC valid', counts.get('TX_frames',0)>0 and counts.get('TX_frames')==counts.get('TX_crc_valid') and counts.get('TX_crc_errors',0)==0)
        check(label+' selected export complete', counts.get('selected_layout_errors',0)==0 and counts.get('omitted_selected_events',0)==0 and counts.get('omitted_errors',0)==0)
    events = [(c,ev) for c in captures for ev in c['events']]
    def event(role, direction, op, predicate=lambda v:True, phase=None):
        return [(c,v) for c,v in events if c['role']==role and v['direction']==direction and v['type']==1 and v['op']==op and (phase is None or c['phase']==phase) and predicate(v)]
    baseline = states['baseline']['roles']; unclaimed = states['unclaimed']['roles']
    check('baseline cash and unused receiver quota', baseline['A']['Cash']==26600 and baseline['B']['Cash']==997790 and baseline['B']['GiftReceiveCount']==0)
    check('baseline has no selected egg or hatch output', all(not eggs(baseline[r]) and not gifts(baseline[r]) for r in ('A','B')))
    initial_phase = states['unclaimed']['phase']
    request = event('A','TX',67,lambda v:v.get('gift') is True,initial_phase)
    check('one actual native egg gift request', len(request)==1 and request[0][1].get('products')==[dict(ability_index=255,term=0,commodity=105009)])
    replies = event('A','RX',67,lambda v:v.get('success') is True and v.get('gift') is True and v.get('commodity')==105009,initial_phase)
    check('category3 gift success empty buyer rewards', len(replies)==1 and replies[0][1].get('category')==3 and replies[0][1].get('reward_count')==0 and replies[0][1].get('result_keys_match') is True)
    if len(request)==1 and len(replies)==1:
        check('request precedes success in same recorded client', request[0][0]['pid']==replies[0][0]['pid'] and request[0][1]['line']<replies[0][1]['line'])
    query = event('A','RX',146,lambda v:v.get('success') is True and v.get('level')==6 and v.get('received_count')==0,initial_phase)
    check('native recipient lookup reports level6 quota0', bool(query))
    check('sender 6600 debit receiver cash and quota', unclaimed['A']['Cash']==26600-PRICE and unclaimed['B']['Cash']==997790 and unclaimed['B']['GiftReceiveCount']==1)
    check('sender gains no egg or pet', not eggs(unclaimed['A']) and not gifts(unclaimed['A']))
    mails = gifts(unclaimed['B']); check('one unclaimed receiver egg mail', len(mails)==1)
    attachment = mails[0]['Attachment'] if len(mails)==1 else {}
    original_uuid = attachment.get('InstanceId'); mail_id = mails[0]['Id'] if len(mails)==1 else None
    check('fresh sealed count1 attachment and UUID', valid_uuid(original_uuid) and attachment.get('ItemId')==EGG and attachment.get('CountOrValue')==1 and attachment.get('State')==0 and attachment.get('Durability')==0 and attachment.get('SealState')==1)
    check('creature attachment has no avatar/quality fields', all(attachment.get(k) is None for k in ('AvatarExpireAt','AvatarAbilityIndex','AvatarExpiresAtUnixSeconds','QualitySeed')))
    check('mail has existing 30day lifetime', len(mails)==1 and mails[0]['ExpiresAt']-mails[0]['SentAt']==2592000)
    check('unclaimed gift has not entered recipient item spaces', not eggs(unclaimed['B']))
    wire_mails = [v for c,v in events if c['role']=='B' and v['direction']=='RX' and v['type']==0 and v['op']==97 and c['phase']==initial_phase]
    wire_rows = [p for v in wire_mails for p in v.get('target_packages',[]) if p.get('mail_id')==mail_id]
    check('native mailbox sealed egg tuple matches mail', any(p.get('item_id')==EGG and p.get('seal')==1 and p.get('durability')==0 and p.get('state')==0 and p.get('count_or_value')==1 for p in wire_rows))
    if stage == 'unclaimed':
        check('unclaimed freeze contains no receiver claim', not event('B','TX',98,lambda v:v.get('mail_id')==mail_id))
    else:
        restart = states['restart_unclaimed']['roles']['B']; restart_mails = gifts(restart)
        check('restart preserves original unclaimed mail and UUID', len(restart_mails)==1 and restart_mails[0]==mails[0] and not eggs(restart) and restart['Cash']==997790 and restart['GiftReceiveCount']==1)
        uid = None
        for name,item_id,seal in (('claimed',EGG,1),('hatched',PET,0),('relogin',PET,0)):
            roles = states[name]['roles']; selected = eggs(roles['B'])
            check(name+' only recipient creature space contains one gift', len(selected)==1 and len([v for v in roles['B'].get('CreatureInventory',[]) if v.get('ItemId') in (EGG,PET)])==1 and not any(v.get('ItemId') in (EGG,PET) for v in roles['B'].get('Inventory',[])+roles['B'].get('AvatarInventory',[])) and not gifts(roles['B']))
            item = selected[0] if len(selected)==1 else {}
            if name=='claimed': uid=item.get('CountOrValue')
            check(name+' same UUID and nonzero stable wire UID', item.get('InstanceId')==original_uuid and isinstance(uid,int) and 0<uid<=0xffffffff and item.get('CountOrValue')==uid)
            check(name+' correct egg/hatch and fresh defaults', item.get('ItemId')==item_id and item.get('SealState')==seal and item.get('Durability')==0 and item.get('CreatureStomach')==100 and item.get('CreatureStomachRemainderSeconds')==0 and item.get('CreatureExperience')==0 and item.get('CreatureLevel')==1 and item.get('CreatureNoCharge') is False and isinstance(item.get('CreatureName'),str) and bool(item['CreatureName']))
            if name=='claimed':check('claimed name is original decoded egg name',item.get('CreatureName')=='宠物蛋 (恰克斯)')
            check(name+' cash quota and no sender grant preserved', roles['A']['Cash']==20000 and roles['B']['Cash']==997790 and roles['B']['GiftReceiveCount']==1 and not eggs(roles['A']) and not gifts(roles['A']))
        claim_phase = states['claimed']['phase']; hatch_phase = states['hatched']['phase']; relog_phase=states['relogin']['phase']
        read_proven=bool(event('B','TX',144,lambda v:v.get('mail_id')==mail_id,claim_phase))
        if is_v3:
            # A read mail stays read across an exact frozen-state restart. The
            # native client need not retransmit CMD144 when claiming it later.
            # Require real state2 request/success, exact mail, same captured
            # connection, and chronology before the actual claim. Only the
            # initial phase or the claim phase may establish this evidence.
            read_proven=False
            exact_restart=(states['unclaimed']['state_sha256']==states['restart_unclaimed']['state_sha256'] and mails==restart_mails)
            claims=event('B','TX',98,lambda v:v.get('mail_id')==mail_id,claim_phase)
            for read_phase in {initial_phase,claim_phase}:
                if read_phase!=claim_phase and not exact_restart:continue
                requests=event('B','TX',144,lambda v:v.get('mail_id')==mail_id and v.get('state')==2,read_phase)
                replies=event('B','RX',144,lambda v:v.get('mail_id')==mail_id and v.get('state')==2 and v.get('success') is True,read_phase)
                for rc,rv in requests:
                    for ac,av in replies:
                        if rc is not ac or rv['line']>=av['line']:continue
                        if any(rv.get(k)!=av.get(k) for k in ('local_endpoint','remote_endpoint')):continue
                        for _,claim in claims:
                            try:ordered=datetime.datetime.fromisoformat(rv['at'])<=datetime.datetime.fromisoformat(av['at'])<datetime.datetime.fromisoformat(claim['at'])
                            except (KeyError,ValueError,TypeError):ordered=False
                            read_proven=read_proven or ordered
        check('native open/read/claim requests and success', bool(event('B','TX',99,phase=claim_phase)) and read_proven and bool(event('B','TX',98,lambda v:v.get('mail_id')==mail_id,claim_phase)) and bool(event('B','RX',98,lambda v:v.get('success') is True and v.get('mail_id')==mail_id,claim_phase)))
        check('native hatch request and success', bool(event('B','TX',105,lambda v:v.get('item_space')==7,hatch_phase)) and bool(event('B','RX',105,lambda v:v.get('success') is True,hatch_phase)))
        for name,item_id,phase in (('claimed',EGG,claim_phase),('hatched',PET,hatch_phase),('relogin',PET,relog_phase)):
            rows=[p for c,v in events if c['role']=='B' and c['phase']==phase and v['type']==0 and v['direction']=='RX' and v['op']==13 and v.get('list_type')==7 for p in v.get('target_items',[])]
            check(name+' native list7 contains stable UID', any(p.get('item_id')==item_id and p.get('count_or_value')==uid for p in rows))
    return dict(schema='act1-creature-gift-check/v1',stage=stage,checks=checks,passed=sum(v['passed'] for v in checks),total=len(checks),
                status='PASS' if all(v['passed'] for v in checks) else 'FAIL',native_acceptance='ROOT_UI_REVIEW_REQUIRED',
                limitations=['Only closed packet and selected canonical fields checked. Root must review original UI screenshots and deployment/restoration separately.',
                             ('V3 source clone 3E5 continues the frozen row-red state without business seeding; it is distinct from later snapshots.' if is_v3 else 'Source clone FB and postboot state hash are distinct. State hashes need not remain equal after normal daily rollover/serialization.'),
                             'Selected creature rows prove nonzero/stable target UID; unrelated creature UID collision census is outside this filtered schema.'])

def self_test():
    # Business assertions must fail closed without a real gift. No files read.
    empty_role=dict(Cash=26600,GiftReceiveCount=0,Inventory=[],AvatarInventory=[],CreatureInventory=[],Mails=[])
    other={**empty_role,'Cash':997790}
    e=dict(schema='act1-creature-gift-authoritative/v1',case='creatureegg',runtime=dict(source_clone_sha256=BASELINE),inputs=[dict(sha256='A'*64,bytes=0)],
           captures=[],capture_report_comparisons=[],stages={s:dict(phase='synthetic',roles=dict(A=copy.deepcopy(empty_role),B=copy.deepcopy(other)),observer_recomputed_equal=True) for s in STAGES})
    assert assess(e,'unclaimed')['status']=='FAIL'
    e['stages']['unclaimed']['roles']['A']['Cash']=20000
    assert assess(e,'unclaimed')['status']=='FAIL'
    assert assess(e,'final')['status']=='FAIL'
    identity='00000000-0000-4000-8000-000000000001'
    attachment=dict(ItemId=EGG,CountOrValue=1,State=0,Durability=0,SealState=1,InstanceId=identity)
    mail=dict(Id=7,SentAt=100,ExpiresAt=2592100,Attachment=attachment)
    for name in STAGES[1:]:
        roles=e['stages'][name]['roles'];roles['A']['Cash']=20000;roles['B']['GiftReceiveCount']=1
        if name in ('unclaimed','restart_unclaimed'):roles['B']['Mails']=[copy.deepcopy(mail)]
        else:
            roles['B']['CreatureInventory']=[dict(ItemId=EGG if name=='claimed' else PET,CountOrValue=2,State=0,Durability=0,SealState=1 if name=='claimed' else 0,InstanceId=identity,
                 CreatureStomach=100,CreatureStomachRemainderSeconds=0,CreatureExperience=0,CreatureLevel=1,CreatureName='宠物蛋 (恰克斯)' if name=='claimed' else 'synthetic output name',CreatureNoCharge=False)]
    e['capture_report_comparisons']=[dict(equal=True)]
    def cap(role,phase,events):return dict(role=role,phase=phase,pid=1,status='observed',errors=[],pending_log_line_bytes=0,
                                          counts=dict(TX_frames=1,TX_crc_valid=1),events=events)
    def ev(direction,kind,op,line=1,**fields):return dict(direction=direction,type=kind,op=op,line=line,**fields)
    e['captures']=[cap('A','synthetic',[
        ev('RX',1,146,success=True,level=6,received_count=0),
        ev('TX',1,67,2,gift=True,products=[dict(ability_index=255,term=0,commodity=105009)]),
        ev('RX',1,67,3,success=True,gift=True,category=3,commodity=105009,reward_count=0,result_keys_match=True)]),
        cap('B','synthetic',[ev('RX',0,97,target_packages=[dict(mail_id=7,item_id=EGG,seal=1,durability=0,state=0,count_or_value=1)])])]
    assert assess(e,'unclaimed')['status']=='PASS'
    v3=copy.deepcopy(e);v3['schema']='act1-creature-gift-authoritative/v3';v3['runtime']=dict(V3_RUNTIME)
    v3['inputs'].append(dict(kind='case_manifest',sha256=V3_RUNTIME['manifest_sha256'],bytes=1))
    assert assess(v3,'unclaimed')['status']=='PASS'
    for key in V3_RUNTIME:
        bad=copy.deepcopy(v3);bad['runtime'][key]='wrong'
        assert assess(bad,'unclaimed')['status']=='FAIL'
    bad=copy.deepcopy(v3);bad['inputs']=e['inputs']
    assert assess(bad,'unclaimed')['status']=='FAIL'
    for name in STAGES[2:]:e['stages'][name]['phase']='restart' if name!='relogin' else 'relogin'
    e['captures'] += [cap('B','restart',[
        ev('TX',1,99),ev('TX',1,144,mail_id=7,state=2),ev('TX',1,98,mail_id=7),ev('RX',1,98,success=True,mail_id=7),
        ev('RX',0,13,list_type=7,target_items=[dict(item_id=EGG,count_or_value=2)]),
        ev('TX',1,105,item_space=7,slot=0),ev('RX',1,105,success=True),
        ev('RX',0,13,list_type=7,target_items=[dict(item_id=PET,count_or_value=2)])]),
        cap('B','relogin',[ev('RX',0,13,list_type=7,target_items=[dict(item_id=PET,count_or_value=2)])])]
    assert assess(e,'final')['status']=='PASS'
    bad=copy.deepcopy(e);bad['stages']['hatched']['roles']['B']['CreatureInventory'][0]['InstanceId']='00000000-0000-4000-8000-000000000002'
    assert assess(bad,'final')['status']=='FAIL'
    bad=copy.deepcopy(e);bad['captures'][0]['counts']['TX_crc_valid']=0
    assert assess(bad,'final')['status']=='FAIL'
    bad=copy.deepcopy(e);bad['stages']['unclaimed']['observer_recomputed_equal']=False
    assert assess(bad,'final')['status']=='FAIL'
    print('creature evidence checker synthetic positive/fail-closed tests passed; no native result')

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--self-test',action='store_true');p.add_argument('--evidence',type=Path);p.add_argument('--sha256');p.add_argument('--stage',choices=('entry-red','unclaimed','final'));p.add_argument('--output',type=Path);a=p.parse_args()
    if a.self_test:self_test();return 0
    require(a.evidence and a.output and a.stage and valid_hash(a.sha256) and not a.output.exists())
    require(a.evidence.stat().st_size<=32*1024*1024);raw=a.evidence.read_bytes();require(sha(raw)==a.sha256.upper())
    e=json.loads(raw.decode('utf-8-sig'));require('Accounts' not in e and 'raw_payload' not in e)
    result=assess_entry_red(e) if a.stage=='entry-red' else assess(e,a.stage);result['evidence_sha256']=sha(raw);result['checker_sha256']=sha(Path(__file__).read_bytes())
    with a.output.open('x',encoding='utf-8') as f:json.dump(result,f,ensure_ascii=False,indent=2);f.write('\n')
    print(json.dumps(dict(status=result['status'],passed=result['passed'],total=result['total'],native_acceptance=result['native_acceptance'])))
    return 0 if result['status']=='PASS' else 1
if __name__=='__main__':
    try:raise SystemExit(main())
    except (OSError,ValueError,KeyError,TypeError) as error:
        print(json.dumps(dict(status='ERROR',error_type=type(error).__name__,native_acceptance='NOT_ASSESSED')));raise SystemExit(2)
