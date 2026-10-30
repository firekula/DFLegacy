"""PID-bound control of an owned IJL input run; captures are explicit.

The per-run builtin_driver.py implements the existing IJL file protocol.
This wrapper verifies PID/path/start and preserves fresh evidence labels.
"""
from __future__ import annotations
import argparse
import ctypes
import datetime
import json
from pathlib import Path
import re
import subprocess
import sys


def started_filetime(value):
 main,_,fraction=value.removesuffix('Z').partition('.')
 delta=datetime.datetime.fromisoformat(main)-datetime.datetime(1601,1,1)
 return (delta.days*86400+delta.seconds)*10_000_000+int((fraction+'0000000')[:7])


def process_started(pid):
 if __package__ in (None,''):
  sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
 from tools.debug.client_state import get_process_started_filetime
 return get_process_started_filetime(pid)


def process_image(pid):
 k=ctypes.WinDLL('kernel32',use_last_error=True)
 k.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_int,ctypes.c_ulong];k.OpenProcess.restype=ctypes.c_void_p
 k.QueryFullProcessImageNameW.argtypes=[ctypes.c_void_p,ctypes.c_ulong,ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_ulong)]
 k.CloseHandle.argtypes=[ctypes.c_void_p]
 handle=k.OpenProcess(0x1000,False,pid)
 if not handle:raise OSError('Cannot query owned process')
 try:
  name=ctypes.create_unicode_buffer(32768);size=ctypes.c_ulong(len(name))
  if not k.QueryFullProcessImageNameW(handle,0,name,ctypes.byref(size)):raise OSError('Cannot query process image')
  return name.value
 finally:k.CloseHandle(handle)


def verify_target(target):
 expected=started_filetime(target['StartTimeUtc'])
 actual=process_started(target['Id'])
 image=process_image(target['Id'])
 if actual!=expected or str(Path(image).resolve()).casefold()!=str(Path(target['Path']).resolve()).casefold() or process_started(target['Id'])!=expected:
  raise ValueError('Owned process identity changed')


def build_command(driver,target,label,output,steps,*,screenshot=False):
 payload=steps if isinstance(steps,str) else json.dumps(steps,ensure_ascii=False)
 command=[sys.executable,str(driver),'--client-dir',str(Path(target['Path']).parent),
          '--pid',str(target['Id']),'--label',label,'--out-dir',str(output),'--steps-json',payload]
 if screenshot:command.append('--screenshot')
 return command


def run_control(run_dir,role,label,steps,*,screenshot=False):
 run=Path(run_dir).resolve(strict=True)
 if role not in ('A','B','C','D') or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}',label):raise ValueError('Invalid role or label')
 journal_path=run/'deployment.json';raw=journal_path.read_bytes();journal=json.loads(raw.decode('utf-8-sig'))
 clients=[x for x in journal['Clients'] if x['Role']==role]
 if journal['Status']!='running' or len(clients)!=1:raise ValueError('Expected one owned client in a running phase')
 if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}',journal['Phase']):raise ValueError('Invalid phase')
 target=clients[0];output=run/('evidence-'+journal['Phase']);output.mkdir(exist_ok=True)
 if any(output.glob(label+'.*')) or (output/(label+'-driver.log')).exists():raise FileExistsError('Preserve existing evidence; use a fresh label')
 driver=run/'builtin_driver.py'
 if not driver.is_file():raise FileNotFoundError('Run has no builtin IJL file driver')
 verify_target(target)
 reservations=output/'controller-reservations';reservations.mkdir(exist_ok=True)
 with (reservations/(label+'.lock')).open('x',encoding='utf-8') as lock:lock.write(str(target['Id']))
 result=subprocess.run(build_command(driver,target,label,output,steps,screenshot=screenshot),capture_output=True,text=True,encoding='utf-8',errors='replace')
 with (output/(label+'-driver.log')).open('x',encoding='utf-8') as log:log.write(result.stdout+result.stderr)
 if journal_path.read_bytes()!=raw:raise ValueError('Owned run journal changed during input')
 verify_target(target)
 record=json.loads((output/(label+'.json')).read_text(encoding='utf-8'))
 if result.returncode or not record.get('own_plan_armed'):record['success']=False
 return record


def parser():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('run_dir',type=Path);p.add_argument('role');p.add_argument('label');p.add_argument('steps')
 group=p.add_mutually_exclusive_group()
 group.add_argument('--screenshot',action='store_true',help='Explicit key acceptance capture using native PrintScreen')
 group.add_argument('--no-shot',action='store_true',help='Compatibility alias for the no-capture default')
 return p


def main(argv=None):
 a=parser().parse_args(argv)
 try:
  record=run_control(a.run_dir,a.role,a.label,a.steps,screenshot=a.screenshot and not a.no_shot)
  print(json.dumps({key:record.get(key) for key in ('label','success','error','screenshot','own_plan_armed')},ensure_ascii=False))
  return 0 if record.get('success') else 1
 except (OSError,ValueError,KeyError) as e:
  print(json.dumps(dict(label=a.label,success=False,error=str(e)),ensure_ascii=False));return 1


if __name__=='__main__':sys.exit(main())
