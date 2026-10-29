"""Root-only native file-input controller bound to this run's exact PID/path/start-time journal."""
import json, re, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
role,label,steps=sys.argv[1:4]
if role not in ['A','B'] or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}',label):raise ValueError('Invalid role/label')
journal=json.loads((ROOT/'deployment.json').read_text(encoding='utf-8-sig'))
if journal['Status']!='running':raise ValueError('Input requires this run to own active clients')
client=next(v for v in journal['Clients'] if v['Role']==role)
script=f"$p=Get-Process -Id {int(client['Id'])} -ErrorAction Stop; @{{Path=$p.Path;StartTimeUtc=$p.StartTime.ToUniversalTime().ToString('o')}}|ConvertTo-Json"
observed=json.loads(subprocess.run(['powershell','-NoProfile','-Command',script],capture_output=True,text=True,check=True).stdout)
if Path(observed['Path'])!=Path(client['Path']) or observed['StartTimeUtc']!=client['StartTimeUtc']:raise ValueError('PID identity changed')
output=ROOT/('evidence-'+journal['Phase']);output.mkdir(exist_ok=True)
command=[sys.executable,str(ROOT/'builtin_driver.py'),'--client-dir',str(Path(client['Path']).parent),
         '--pid',str(client['Id']),'--label',label,'--out-dir',str(output),'--steps-json',steps]
if '--no-shot' not in sys.argv:command.append('--screenshot')
result=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',errors='replace')
with (output/(label+'-driver.log')).open('x',encoding='utf-8') as f:f.write(result.stdout+result.stderr)
record=json.loads((output/(label+'.json')).read_text(encoding='utf-8'))
print(json.dumps({k:record.get(k) for k in ['label','success','error','screenshot','own_plan_armed']},ensure_ascii=False))
sys.exit(result.returncode)
