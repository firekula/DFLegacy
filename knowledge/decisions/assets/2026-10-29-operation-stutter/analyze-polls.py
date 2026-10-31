from pathlib import Path
import json,sys
q=Path(__file__).resolve().parent;name=sys.argv[1];p=q/'perf'/(name+'.json');v=json.loads(p.read_text());polls=v['polls'];summ=[]
for ph in v['phases']:
 xs=[x for x in polls if x[5]==ph['name']];runs=[];gaps=[];first=prev=xs[0]
 for x in xs[1:]:
  gap=(x[0]-prev[1])*1000;read=(x[1]-x[0])*1000
  assert x[0]>=prev[1] and x[1]>=x[0]
  if gap>10 or read>5:gaps.append(dict(t=x[0],between_reads_ms=gap,read_ms=read))
  if x[2:4]!=prev[2:4] or gap>10 or read>5:
   runs.append(dict(start=first[1],end=prev[0],constant_lower_ms=max(0,(prev[0]-first[1])*1000),pair=first[2:4]));first=x
  prev=x
 runs.append(dict(start=first[1],end=prev[0],constant_lower_ms=max(0,(prev[0]-first[1])*1000),pair=first[2:4]))
 summ.append(dict(phase=ph['name'],polls=len(xs),foreground_fraction=sum(x[4]==v['identity']['Id'] for x in xs)/len(xs),max_read_ms=max((x[1]-x[0])*1000 for x in xs),max_contiguous_constant_ms=max(x['constant_lower_ms'] for x in runs),long_constant_runs=[x for x in runs if x['constant_lower_ms']>15],observer_gaps=gaps))
result=dict(rule='B5DD20/B5DD34 pair, constant lower bound with successive reads <=10ms apart and each read <=5ms. Observer scheduling gaps excluded, not GPU Present.',input=p.name,phases=summ)
if 'image_before' in v:result['native_screenshot_directory_unchanged']=v['image_before']==v['image_after']
with (q/'perf'/(name+'-analysis.json')).open('x',encoding='utf-8') as out:json.dump(result,out,indent=2)
print(json.dumps(result))
