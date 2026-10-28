"""Verify the selected, sanitized final delivery evidence. Reads files only.
This validates the recorded evidence and hashes, not the current user's runtime.
"""
import json,hashlib,pathlib,argparse,sys
sys.dont_write_bytecode=True
APPROVED=dict(managed_source_commit='5078da27a4807695f002ceea61333c43e1932ce4',native_source_commit='ab1e6b685021f6cd6f8d125fc04dc0c5c50e931f',build_manifest_sha256='09EFFBE39AB99B377AAFB540933EA75919E9E04642FCE79F4D6AF0CB1CC05204',native_approval_sha256='832A30A725206FE2A789EFAD4EE7A3194E03B49CCF781E3D931D4C3C99BB8CD8',server_sha256='A0EE99650A49011B9527E06BB8E1C19E6A7E5D284CC51D8F9865597C9E133134',protocol_sha256='5A55089D384EFA9E4ACEC5A211EE5C551668CDDB5C93ED46A698D50250E04A4E',native_sha256='30AD9A4A3CD565DE845F74AB3D6C6D60EED1F3429EC7D53929D912F158246584')
SAVE='FB20572589A5CFB31D89E16EC6936DE96071630E432EDE787CCB0887930ABE52'
HELPERS=dict(source_commit='adc990941e860e7d199faa2e4acb7d1cdc48c868',ps1_sha256='8FCE652F0EF4ACD7FE2AFD95599944DF2E2854EEC0FBC2E4803FFE9F19D4914A',cmd_sha256='74220A29F6A5750848E48D2BE68F7142982B4D42F38B32DC71E7C5B98D0D3B4E',deployment_sha256='1D42D13953A0CC530D3D4DA422871C8C8ACD0D9001E5018F41B9DFAADE7AA517')
CONFIGS={'Config-0.ini':'03333C57357058DC73D67B4E2C8E56E4B03403B73E7E6B79AF370A596A108920','Config-1.ini':'3F04A210C46907E202D3C3FEDCC7AFF7E7D838F7A90249CB6381317493815861','launcher-A.json':'E1217E2FD1202E81FE34D1E6DE01B2A63E54820EEDD6AB90D4E3A7BDC9142E4E','launcher-B.json':'88FE0C76A3B3F614244282EF032BB8B28F227083C815FCD767A5CDC7DBB95609','server.json':'D604F6F0F9F351745B306EAEEEC8DAD028BF0330D4071090BCE80176CCB8F47C'}
GROUPS={'diagnostics','mentor_growth','pvp','avatar_gift','creature_gift','mentor_gold'}

RECEIPT='769CC11657058C0597BF2F32A946AB11F467EE5F747561D21413DA10A8226BC1'
REVIEW='D5A4891B6AA03022BB02B3C9F90D7C2B5EF78D9D91BD814749157990BB9FDD60'
def assess(e):
 checks=[]
 def c(name,v):
  checks.append(dict(name=name,passed=bool(v)))
  if not v:raise ValueError(name)
 try:
  c('closed scoped schema',e['schema']=='act1-final-delivery/v2' and e['status']=='PASS_AS_SCOPED')
  c('approved gameplay sources and binaries',e['approved']==APPROVED)
  p=e['publication'];c('49 source package;48 published excluding original config',p['package_count']==49 and p['published_count']==48 and p['excluded']==['server.json'] and p['existing_extras_preserved']==216 and p['helper_extras_count']==2)
  c('publish and restore receipt identities',p['publish_receipt_sha256']=='8E579F7F8B1E990D4586A150FA6B53F7658F10CDBCAFA5FBDBA99DEF63131451' and p['restore_receipt_sha256']=='98CC9EA40ED69A7191C602EF56AFA7F7CABA0F9E3C8C6AE6018120CB05C3E5C9')
  v=e['preservation'];c('original FB save and five config hashes',v['original_save_sha256']==SAVE and v['original_save_unchanged'] and v['all_five_configs_original'] and v['config_hashes']==CONFIGS)
  c('83 backups and EXE/PVF unchanged',v['backup_count']==83 and v['all_backups_unchanged'] and v['exe_pvf_unchanged'] and v['exe_pvf_count']==4)
  c('four approved native copies',v['four_native_targets_approved'] and v['native_target_count']==4)
  c('original and final absent processes',v['original_processes_absent'] and v['final_processes_absent'] and v['independent_process_observation']['count']==0)
  c('nine historical packages and nine artifacts retained',v['historical_package_count']==9 and v['historical_artifact_count']==9 and v['historical_hashes_unchanged'] and v['runtime_before_count']==266 and v['runtime_before_all_unchanged'] and v['fixed_history_count']==38)
  s=e['startup'];c('final startup helper identity separate from gameplay',s['helpers']==HELPERS)
  c('old helper retained as historical',s['historical_helpers']['source_commit']=='22f2f0a4f03c9d79e6fe5ee2df09568f3fc996f7')
  c('original settings retained',s['configured_server_entrance']==2311 and s['configured_launcher_entrance']==27001 and s['channel_port']==7001 and s['launcher_start_server'] is False and s['original_server_config_rewritten'] is False)
  f={v['id']:v for v in e['failures']};c('all three historical failures retained',set(f)=={'03','04','05'} and f['03']['status']=='FAILED_BIND' and f['04']['status']=='FAILED_PRECHECK_BEFORE_CLAIM' and f['04']['server_started'] is False and f['05']['status']=='FAILED_ACTIVE_LOG_SHARING')
  c('05 confirmed defect and failed entry remain',f['05']['server_started'] is True and f['05']['ready_observed'] is False and f['05']['independent_review']['confirmed_helper_defect'] is True and f['05']['environment_observation']['entrypoint_completed'] is False)
  c('historical failure hashes',f['03']['receipt_sha256']=='D08DFDD106C3831C85B7C7979D6EE7FD9344C24FA3F79C194A107204512C3612' and f['04']['receipt_sha256']=='1C89257EF490E865E5C6BCE66D8B80C48CFD53606D00F47EDC46BE531858659A' and f['05']['receipt_sha256']=='CECA330387E352970DC93687EA6B3E05494CB7FE16F190A0E7A46B3E107CEB20')
  r=e['readiness'];c('actual06 closed successful receipt',r['phase']=='06' and r['status']=='completed' and r['receipt_sha256']==RECEIPT)
  c('actual owned PID and start',r['pid']==50700 and r['start_time_utc']=='2026-10-28T01:08:31.9154318Z' and r['owned_process_identity_checked'])
  c('both ports same process',sorted(r['ports'])==[7001,27001] and r['both_owned_same_pid'])
  c('normal runtime hidden executable no CLI',r['normal_runtime_executable'] and r['hidden_child'] and r['server_arguments']==[])
  c('unique isolated state and original read-only guards',r['unique_isolated_datapath'] and r['isolated_initial_sha256']==SAVE and r['readonly_original_guards']==6 and r['guard_share_mode']=='Read')
  c('isolated copy may change while original stays intact',r['isolated_final_sha256']=='7E4407F4B183A975C4B3D2E59D50F52B77CE3FD07CCEFFC8F92FB88760EDD091' and r['isolated_final_sha256']!=SAVE)
  env=r['environment_observation'];c('same-host actual empty environment restored',env['matched'] is True and env['entrypoint_completed'] is True and env['execute'] is True and env['before_count']==env['after_count']==0 and env['before_sha256']==env['after_sha256']=='E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855' and env['values_exported'] is False and env['nonempty_environment_native_tested'] is False)
  c('child scoped environment',r['child_scoped_environment'] and r['environment_restored'])
  c('stable readiness and owned stop without retry',r['stable_seconds']>=2 and r['no_startup_failure'] and r['stopped'] and r['final_absent'] and r['automatic_retry'] is False and r['automatic_rollback'] is False)
  c('source review execution review and environment identities',r['execution_review_sha256']==REVIEW and r['recovery_review_sha256']=='9B6D1C9B03C4A3C18B5485FFE1D8D3367B5598566BA9811F681DA5D5CDFD38CC' and r['environment_observation']['sha256']=='A444A2A71A764D3FB2BA666FF1C9E7D994657E1225D6C95C762EADEFB3A171B5')
  sb=e['source_boundary'];c('401 whole nonnative bytes and git scope preserved',sb['count']==401 and sb['whole_physical_bytes_equal'] and sb['whole_git_tree_equal'] and sb['older_shared_files_unchanged'] is False)
  c('six distinct historical/native scopes',{x['id'] for x in e['components']}==GROUPS and len(e['components'])==6 and all(x['references'] and x['actual_builds'] and x['limitations'] for x in e['components']))
  c('no whole-game or blocked-value overclaim',all(e['limits'][k] is False for k in ['diagnostic125_naturally_triggered','fatigue_cof_restored','all_game_restored','historical_native_relabelled']))
  c('recorded audit bound',len(e['closed_audit_sha256'])==64 and len(e['closed_evidence_hashes'])>=20)
 except (KeyError,ValueError,TypeError):
  if not checks or checks[-1]['passed']:checks.append(dict(name='required semantic fields',passed=False))
 return dict(status='PASS_AS_SCOPED' if checks and all(x['passed'] for x in checks) else 'FAIL',passed=sum(x['passed'] for x in checks),total=len(checks),checks=checks)
def verify_files(e,directory):
 root=directory.resolve().parent
 for item in e['components']:
  for row in item['references']:
   p=(root/row['asset']).resolve()
   if not p.is_relative_to(root):raise ValueError('reference path escaped assets')
   b=p.read_bytes()
   if len(b)!=row['bytes'] or hashlib.sha256(b).hexdigest().upper()!=row['sha256']:raise ValueError('component hash mismatch: '+row['asset'])
 for row in e['public_evidence']:
  p=(directory/row['file']).resolve()
  if p.parent!=directory.resolve():raise ValueError('local evidence path escaped')
  if hashlib.sha256(p.read_bytes()).hexdigest().upper()!=row['sha256']:raise ValueError('local evidence hash mismatch')
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('proof',nargs='?',type=pathlib.Path,default=pathlib.Path(__file__).with_name('final-delivery.json'));a=p.parse_args();e=json.loads(a.proof.read_bytes());r=assess(e)
 try:verify_files(e,a.proof.parent);r['artifact_hashes']='PASS'
 except (ValueError,OSError,KeyError) as ex:r['status']='FAIL';r['artifact_hashes']='FAIL';r['error']=str(ex)
 manifest=a.proof.parent/'publication-manifest.json'
 if manifest.exists():
  try:
   for row in json.loads(manifest.read_bytes())['files']:
    q=(a.proof.parent/row['file']).resolve()
    if q.parent!=a.proof.parent.resolve():raise ValueError('publication path escaped')
    b=q.read_bytes()
    if len(b)!=row['bytes'] or hashlib.sha256(b).hexdigest().upper()!=row['sha256']:raise ValueError('publication inventory mismatch')
   r['publication_inventory']='PASS'
  except (ValueError,OSError,KeyError):r['status']='FAIL';r['publication_inventory']='FAIL'
 print(json.dumps(r,indent=2));return 0 if r['status']=='PASS_AS_SCOPED' else 1
if __name__=='__main__':raise SystemExit(main())
