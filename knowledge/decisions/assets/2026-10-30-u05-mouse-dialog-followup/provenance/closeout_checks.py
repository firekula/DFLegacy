from pathlib import Path
import collections,contextlib,hashlib,io,json,re,struct,sys
from unittest.mock import patch
R=Path(__file__).resolve().parents[2];sys.path.insert(0,str(R))
from tools.debug import client_state as state
T=Path('G:/Game/DNF_ACT1/DFLegacy/.tmp/u05-ui-followup-20261030')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def save(name,value):
 with (T/name).open('x',encoding='utf-8') as f:json.dump(value,f,indent=2)
J=read(T/'deployment.json');assert J['Status']=='stopped'
directory=Path(J['SnapshotDirectory']);replays={}
for client in J['Clients']:
 path=directory/f"dfdebug-client-{client['Id']}.json";raw=read(path);replays[client['Role']]={}
 assert raw['valid'] and raw['scene']==1
 for name,expression in [('mouse_lock','ui.command_input.mouse.lock_level=0'),('physical','ui.command_input.mouse.physical_excluded=false'),
     ('button_state','ui.mouse_button_state.deferred=false'),('coverage','ui.dialogs.covered_class_ids.5=86')]:
  out=io.StringIO()
  with contextlib.redirect_stdout(out),patch.object(state,'now_tick_ms',return_value=raw['captured_tick_ms']):
   code=state.main(['--directory',str(directory),'--pid',str(raw['pid']),'--process-started-filetime',str(raw['process_started_filetime']),
      '--uid',str(raw['me']['uid']),'--wait',expression,'--timeout-seconds','0'])
  result=json.loads(out.getvalue());assert code==0 and result['status']=='matched'
  replays[client['Role']][name]=result
 out=io.StringIO()
 with contextlib.redirect_stdout(out),patch.object(state,'now_tick_ms',return_value=raw['captured_tick_ms']):
  code=state.main(['--directory',str(directory),'--pid',str(raw['pid']),'--process-started-filetime',str(raw['process_started_filetime']),
     '--wait','ui.input_blocked=false','--timeout-seconds','0'])
 assert code==1 and json.loads(out.getvalue())['status']=='timeout'
save('closed-sdk-cli-replay.json',dict(scope='16 conditions on preserved actual SDK samples with historical captured clock, not live/fresh process checks',
 final_live_cli_attempt='not executed: helper import failure before shutdown; not counted as a live pass',results=replays,global_unknown_timed_out=True))

# Only plaintext transport headers are needed to rule out outgoing actions.
# Cipher payloads/credentials are neither decrypted nor exported.
line=re.compile(r'^.*? T=\d+ TX TCP (\S+) -> (\S+) len=(\d+)(.*?) hex=([A-Fa-f0-9]*)$')
audit={};forbidden=(12,63,64,97,98)
for client in J['Clients']:
 path=T/('closed-'+J['Phase'])/(client['Role']+'-netdump.log')
 streams=collections.defaultdict(bytearray);counts=collections.Counter();frames=0;segments=0
 for text in path.read_text(encoding='utf-8').splitlines():
  m=line.match(text)
  if not m:continue
  local,remote,length,extra,hexdata=m.groups()
  if remote.rsplit(':',1)[-1]!='47001':continue
  data=bytes.fromhex(hexdata);assert len(data)==int(length) and not extra.strip(),(client['Role'],'truncated segment')
  pending=streams[(local,remote)];pending.extend(data);segments+=1
  while len(pending)>=10:
   kind,protocol=struct.unpack_from('<BB',pending);size=struct.unpack_from('<I',pending,2)[0]
   assert kind==1 and 10<=size<=4*1024*1024,(client['Role'],'invalid stream header')
   if len(pending)<size:break
   counts[protocol]+=1;frames+=1;del pending[:size]
 assert segments and all(not b for b in streams.values()),(client['Role'],'incomplete stream')
 audit[client['Role']]=dict(segments=segments,frames=frames,mailbox_open_99=counts[99],
     forbidden_counts={str(p):counts[p] for p in forbidden},netdump_sha256=hashlib.sha256(path.read_bytes()).hexdigest())
 assert all(counts[p]==0 for p in forbidden),audit[client['Role']]
save('outgoing-header-audit.json',dict(scope='Strict from-byte-zero TX TCP frame headers to private channel47001. Bodies remain encrypted; no business-commit inference.',
    forbidden=dict(create_party=12,add_friend=63,remove_friend=64,send_mail=97,extract_attachment=98),results=audit))

old=read(Path(read(T/'setup-v1.json')['SourceClosedState']));new=read(T/('closed-'+J['Phase'])/'state.json')
keys=('Gold','Cash','Inventory','Equipment','Warehouse','AvatarInventory','CreatureInventory')
old_accounts={a['Id']:a for a in old['Accounts']};checked=0
for account in new['Accounts']:
 before=old_accounts[account['Id']]
 for key in ('Friends','Blacklist','Cash'):assert before[key]==account[key],key
 prior={c['Id']:c for c in before['Characters']}
 for char in account['Characters']:
  for key in keys:assert prior[char['Id']][key]==char[key],(char['Id'],key)
  checked+=1
save('private-state-no-transfers.json',dict(accounts_checked=len(new['Accounts']),characters_checked=checked,friend_blacklist_cash_unchanged=True,
    character_fields_unchanged=list(keys),scope='Private test clone only; mailbox observation/weakness/last-seen metadata may change. Normal save checked separately by hash.'))
print(json.dumps(dict(closed_cli_replay_conditions=16,outgoing_audit=audit,private_characters_unchanged=checked)))
