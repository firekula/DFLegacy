import json
from pathlib import Path
from unittest.mock import patch
import pytest
from tools.acceptance import native_control as control


def target():
 return dict(Role='A',Id=123,Path='G:/Game/DF/DNF.exe',StartTimeUtc='2026-10-29T07:30:25.6428300Z')


def test_default_command_does_not_request_native_screenshot(tmp_path):
 c=control.build_command(tmp_path/'builtin_driver.py',target(),'sample',tmp_path,[dict(t=100,op='key',vk=39,dur=200)])
 assert '--screenshot' not in c
 assert json.loads(c[c.index('--steps-json')+1])==[dict(t=100,op='key',vk=39,dur=200)]


def test_cli_capture_is_explicit_and_legacy_no_shot_remains_compatible():
 p=control.parser()
 assert not p.parse_args(['run','A','sample','[]']).screenshot
 assert p.parse_args(['run','A','sample','[]','--screenshot']).screenshot
 assert p.parse_args(['run','A','sample','[]','--no-shot']).no_shot
 with pytest.raises(SystemExit):p.parse_args(['run','A','sample','[]','--no-shot','--screenshot'])


def test_explicit_screenshot_survives_command_construction(tmp_path):
 c=control.build_command(tmp_path/'builtin_driver.py',target(),'sample',tmp_path,[],screenshot=True)
 assert c.count('--screenshot')==1


def test_start_time_keeps_seventh_fractional_digit():
 assert control.started_filetime(target()['StartTimeUtc'])==134377326256428300


def test_pid_reuse_or_foreign_image_is_rejected():
 t=target();start=control.started_filetime(t['StartTimeUtc'])
 with patch.object(control,'process_started',return_value=start+1),patch.object(control,'process_image',return_value=t['Path']):
  with pytest.raises(ValueError,match='identity'):control.verify_target(t)
 with patch.object(control,'process_started',return_value=start),patch.object(control,'process_image',return_value='G:/Other/DNF.exe'):
  with pytest.raises(ValueError,match='identity'):control.verify_target(t)


def test_existing_evidence_never_runs_input(tmp_path):
 run=tmp_path/'run';run.mkdir();(run/'builtin_driver.py').write_text('unused')
 (run/'deployment.json').write_text(json.dumps(dict(Status='running',Phase='one',Clients=[target()])))
 out=run/'evidence-one';out.mkdir();p=out/'sample-driver.log';p.write_bytes(b'prior evidence')
 with patch.object(control,'verify_target'),patch.object(control.subprocess,'run') as invoke:
  with pytest.raises(FileExistsError):control.run_control(run,'A','sample','[]')
  invoke.assert_not_called()
 assert p.read_bytes()==b'prior evidence'


def test_exact_journal_and_both_id_checks_wrap_driver(tmp_path):
 run=tmp_path/'run';run.mkdir();(run/'builtin_driver.py').write_text('unused')
 journal=dict(Status='running',Phase='one',Clients=[target()]);(run/'deployment.json').write_text(json.dumps(journal));out=run/'evidence-one'
 def driver(command,**kwargs):
  assert '--screenshot' not in command
  (out/'sample.json').write_text(json.dumps(dict(label='sample',success=True,own_plan_armed=True,screenshot=None)))
  class Result:returncode=0;stdout='done';stderr=''
  return Result()
 with patch.object(control,'verify_target') as verify,patch.object(control.subprocess,'run',side_effect=driver):
  assert control.run_control(run,'A','sample','[]')['success']
  assert verify.call_count==2


def test_stopped_or_ambiguous_run_is_rejected_before_input(tmp_path):
 for clients,status in [([target()],'stopped'),([target(),target()],'running')]:
  (tmp_path/'deployment.json').write_text(json.dumps(dict(Status=status,Phase='one',Clients=clients)))
  with patch.object(control.subprocess,'run') as invoke:
   with pytest.raises(ValueError):control.run_control(tmp_path,'A','sample','[]')
   invoke.assert_not_called()
