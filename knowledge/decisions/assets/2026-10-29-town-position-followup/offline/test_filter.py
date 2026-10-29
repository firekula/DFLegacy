from pathlib import Path
import struct,zlib,json,subprocess,hashlib
root=Path(r'G:\Game\DNF_ACT1\DFLegacy\.tmp\town-position-followup-20261029\offline');tests=root/'selftests';tests.mkdir(exist_ok=True)
seed=0x1234
def rnd():
 global seed
 vals=[]
 for i in range(3):seed=(seed*0x41c64e6d+0x3039)&0xffffffff;vals.append(seed)
 return ((vals[2]>>16)&0x3ff)^((((vals[1]>>16)&0x3ff)^(((vals[0]>>16)&0x7ff)<<10))<<10)
def rol(v,n):return ((v<<n)|(v>>(8-n)))&255 if n else v
def ror(v,n):return ((v>>n)|(v<<(8-n)))&255 if n else v
def cmd(id,body):
 random=rnd();key=random&255;rotation=(random>>8)&7;wire=body[:2]+bytes(ror(v^key,rotation) for v in body[2:])
 return struct.pack('<BBII',1,id,10+len(body),zlib.crc32(body))+wire
b1=struct.pack('<HhhBH',4,202,372,4,100);b2=struct.pack('<HhhBH',6,247,372,5,100)
frames=[cmd(37,b1),cmd(8,b'\x05\0PRIVATE-NONTARGET'),cmd(37,b2)];wire=b''.join(frames)
notify=struct.pack('<HhhBH',2,247,372,5,100);rx=struct.pack('<BBI',0,22,15)+bytes(ror(v^0xb5,6) for v in notify)
def line(data,ms=0,direction='TX',declared=None,truncated=False):
 return f'2026-10-29 19:00:00.{ms:03} T=77 {direction} TCP 127.0.0.1:1234 -> 127.0.0.1:7001 len={len(data) if declared is None else declared}{" (truncated)" if truncated else ""} hex={data.hex().upper()}\r\n'
cases={
 'merged':line(wire)+line(rx,1,'RX'),
 'split':line(wire[:3])+line(wire[3:14],1)+line(wire[14:],2)+line(rx[:7],3,'RX')+line(rx[7:],4,'RX'),
 'truncated':line(wire[:8],declared=len(wire),truncated=True)+line(wire,1),
 'invalid_header':line(b'\x01\x25\x01\0\0\0'+b'\0'*4+wire),
 'incomplete':line(wire[:-3]),
 'partial_final_line':line(wire)+line(rx,1,'RX').rstrip('\r\n'),
}
results=[]
for name,content in cases.items():
 inp=tests/(name+'.log');out=tests/(name+'.json');inp.write_text(content,encoding='ascii',newline='')
 p=subprocess.run(['dotnet','run','--no-build','--project',str(root/'PositionFilter.csproj'),'-c','Release','--','--input',str(inp),'--output',str(out),'--client','synthetic','--port','7001','--self-uid','1'],capture_output=True,text=True)
 assert p.returncode==0,(name,'tool failed');d=json.loads(out.read_text());s=out.read_text();assert 'PRIVATE-NONTARGET' not in s and '50524956415445' not in s
 if name in ['merged','split']:assert [(r['protocol'],r['x']) for r in d['rows']]==[('CMD37',202),('CMD37',247),('NOTI22',247)] and not d['errors']
 if name=='split':assert d['rows'][0]['first_line']==1 and d['rows'][0]['last_line']==3 and d['rows'][2]['first_line']==4 and d['rows'][2]['last_line']==5
 if name in ['truncated','invalid_header']:assert not d['rows'] and len(d['errors'])==1
 if name=='incomplete':assert len(d['rows'])==1 and d['streams'][0]['pending_bytes']==16
 if name=='partial_final_line':assert len(d['rows'])==2 and d['partial_final_line_bytes']>0
 results.append({'case':name,'passed':True,'position_rows':len(d['rows']),'errors':len(d['errors'])})
(root/'filter-selftest-results.json').write_text(json.dumps(results,indent=2));print(json.dumps(results,indent=2))
