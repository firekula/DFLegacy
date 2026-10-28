"""Separate closed normal-channel reentry and receipt4 restart supplement."""
from pathlib import Path
import json,datetime,hashlib,sys
sys.dont_write_bytecode=True
R=Path(__file__).resolve().parent
APPROVED=dict(managed_source_commit='5078da27a4807695f002ceea61333c43e1932ce4',native_source_commit='ab1e6b685021f6cd6f8d125fc04dc0c5c50e931f',build_manifest_sha256='09EFFBE39AB99B377AAFB540933EA75919E9E04642FCE79F4D6AF0CB1CC05204',native_approval_sha256='832A30A725206FE2A789EFAD4EE7A3194E03B49CCF781E3D931D4C3C99BB8CD8',server_sha256='A0EE99650A49011B9527E06BB8E1C19E6A7E5D284CC51D8F9865597C9E133134',protocol_sha256='5A55089D384EFA9E4ACEC5A211EE5C551668CDDB5C93ED46A698D50250E04A4E',native_sha256='30AD9A4A3CD565DE845F74AB3D6C6D60EED1F3429EC7D53929D912F158246584')
STATE='14D4526DC7BA4A847FC5F1C1128C79F21F14FCF40F985F86132A5F76D76298B4'
def dt(v):return datetime.datetime.fromisoformat(v.replace('Z','+00:00'))
def choose(ev,d,k,op):return [v for v in ev if (v.get('direction'),v.get('type'),v.get('op'))==(d,k,op)]
def assess(e):
    checks=[];summary=[]
    def check(name,v):
        checks.append(dict(name=name,passed=bool(v)))
        if not v:raise ValueError(name)
    try:
        check('explicit separate closed phase and new server',e['schema']=='act1-final-channel-reentry-proof/v1' and e['phase']=='final-channel-reentry-30AD' and e['server']['pid']==24096 and dt(e['server']['start_time_utc'])==dt('2026-10-28T00:26:24.5709138Z'))
        check('approved managed/native build binding',e['approved']==APPROVED)
        check('restart and normal reentry full-state bytes unchanged',e['full_state_bytes_equal'] and e['before_sha256']==e['after_sha256']==e['economy']['sha256']==STATE)
        check('four receipts persist without extra award',e['receipts_before']==e['receipts_after']==len(e['economy']['receipts'])==4)
        check('both immutable prefixes independently redecoded',len(e['prefix_comparisons'])==2 and all(v['equal'] for v in e['prefix_comparisons']))
        cs={v['role']:v for v in e['captures']};check('exactly both selected roles',set(cs)=={'A','B'})
        economy={v['role']:v for v in e['economy']['characters']};awards={v['role']:v for v in e['economy']['receipts'][-1]['selected_awards']};check('fourth receipt remains atomic dual award',set(awards)=={'A','B'})
        sampling=e['sampling'];samples=sampling['samples'];check('468 successful responses and no sampler failure',sampling['failure'] is None and len(samples)==468 and all(v['responding'] for v in samples))
        check('exactly four input windows and two full transitions',len(sampling['control_windows'])==4 and len(sampling['transitions'])==2)
        for role,pid in [('A',61536),('B',45620)]:
            c=cs[role];counts=c['counts'];ev=c['events'];check(role+' exact PID and20 valid TX',c['pid']==pid and c['status']=='observed' and not c['errors'] and counts['TX_frames']==counts['TX_crc_valid']==20 and not counts.get('TX_crc_errors',0))
            check(role+' complete layouts and capture tail',not c['pending_log_line_bytes'] and all(not counts.get(k,0) for k in ['incomplete_tcp_bytes','selected_layout_errors','omitted_selected_events','omitted_errors']))
            logins=[v for v in ev if v.get('event')=='login_reply'];check(role+' two native normal logins',[v['channel_mode'] for v in logins]==[0,0] and all(v['success'] for v in logins))
            exits=choose(ev,'RX',1,3);check(role+' one successful exit between normal logins',len(exits)==1 and exits[0]['success'] and logins[0]['at']<exits[0]['at']<logins[1]['at'])
            entries=choose(ev,'RX',1,4);check(role+' two successful native character entries',len(entries)==2 and all(v['success'] for v in entries))
            check(role+' no PvP start death ranking or result',not any(v.get('type')==0 and v.get('op') in (45,46,47,49) for v in ev) and not any(v.get('direction')=='TX' and v.get('type')==1 and v.get('op') in (53,54,58,59,61) for v in ev))
            expected=dict(VictoryPoints=4 if role=='A' else 16,Points=0 if role=='A' else 4000,Grade=0 if role=='A' else 2,Wins=0 if role=='A' else 4,Losses=4 if role=='A' else 0,PlayCount=4,Count=4,ExperienceDelta=0 if role=='A' else 1000,GradeExtension=0)
            check(role+' receipt4 exact prior award survives',all(awards[role][k]==v for k,v in expected.items()))
            record=choose(ev,'RX',0,48);check(role+' both restarted native record replies retain receipt4',len(record)==2 and all(v['wins']==expected['Wins'] and v['losses']==expected['Losses'] and v['total_pvp_exp']==expected['Points'] and v['grade']==expected['Grade'] for v in record))
            mapping={'VictoryPoints':'VictoryPoints','PvpPoints':'Points','PvpGrade':'Grade','PvpWins':'Wins','PvpLosses':'Losses','PvpPlayCount':'PlayCount','PvpCount':'Count','PvpGradeExtension':'GradeExtension'}
            check(role+' canonical counters equal persisted receipt',all(economy[role][k]==expected[v] for k,v in mapping.items()))
            times=[dt(v['at_utc']) for v in samples if v['role']==role];check(role+'234 monotonic samples at bounded cadence',len(times)==234 and times==sorted(set(times)) and max((b-a).total_seconds() for a,b in zip(times,times[1:]))<1)
            low,high=min(times),max(times);windows={v['action']:v for v in sampling['control_windows'] if v['role']==role};check(role+' chooser and world plans present',set(windows)=={'channel-chooser','normal-world-complete'})
            for name,v in windows.items():check(role+'/'+name+' full control interval sampled',v['entire_input_window_covered'] and low<=dt(v['started'])<=dt(v['plan_written'])<=dt(v['plan_completed'])<=dt(v['finished'])<=high)
            transitions=[v for v in sampling['transitions'] if v['role']==role];check(role+' one combined transition',len(transitions)==1);t=transitions[0];world=windows['normal-world-complete'];chooser=windows['channel-chooser']
            check(role+' entire chooser to world interval covered',t['entire_chooser_to_world_covered'] and dt(t['chooser_started'])==dt(chooser['started']) and dt(t['world_finished'])==dt(world['finished']) and low<=dt(t['chooser_started'])<dt(t['world_finished'])<=high)
            armed=dt(t['native_plan_armed']);check(role+' planned enter click offsets honest',dt(world['plan_written'])<=armed and dt(t['scheduled_channel_enter_click'])==armed+datetime.timedelta(milliseconds=1100) and dt(t['scheduled_world_enter_click'])==armed+datetime.timedelta(milliseconds=5900) and dt(t['scheduled_world_enter_click'])<dt(world['finished']))
            login=dt(logins[1]['at']).replace(tzinfo=datetime.timezone(datetime.timedelta(hours=8)));entry=dt(entries[1]['at']).replace(tzinfo=datetime.timezone(datetime.timedelta(hours=8)))
            check(role+' actual normal login and character reply inside sampled plan',dt(world['started'])<login<=entry<dt(world['finished']))
            summary.append(dict(role=role,tx_crc_valid=20,login_modes=[0,0],sample_count=len(times),sample_first=low.isoformat(),sample_last=high.isoformat(),max_sample_gap_seconds=max((b-a).total_seconds() for a,b in zip(times,times[1:])),chooser_started=t['chooser_started'],scheduled_channel_enter_click=t['scheduled_channel_enter_click'],scheduled_world_enter_click=t['scheduled_world_enter_click'],native_normal_login=login.isoformat(),native_character_reply=entry.isoformat(),world_finished=t['world_finished']))
    except (ValueError,KeyError,IndexError,TypeError):
        if not checks or checks[-1]['passed']:checks.append(dict(name='required evidence structure',passed=False))
    return dict(schema='act1-final-channel-reentry-check/v1',status='PASS_AS_SCOPED' if all(v['passed'] for v in checks) else 'FAIL',passed=sum(v['passed'] for v in checks),total=len(checks),checks=checks,roles=summary,limits=['468 Process.Responding samples at roughly half-second cadence; no claim that all possible sub-sample stalls are excluded.','Scheduled click timestamps derive from native plan-armed plus offset; native callback timestamps are not fabricated.','Independent normal-channel round trip supplements but does not rewrite prior390-sample scope.','Normal-runtime publication/configuration restoration remains separate.'])
def main():
    manifest=json.loads((R/'reentry30ad-sha256-manifest.json').read_text(encoding='utf-8'))
    for v in manifest['files']:
        b=(R/v['name']).read_bytes()
        if len(b)!=v['bytes'] or hashlib.sha256(b).hexdigest().upper()!=v['sha256']:raise ValueError('Asset hash mismatch')
    e=json.loads((R/'reentry30ad-proof.json').read_text(encoding='utf-8'));result=assess(e);print(json.dumps(result,indent=2));return 0 if result['status']=='PASS_AS_SCOPED' else 1
if __name__=='__main__':raise SystemExit(main())
