"""Offline LOGIN cursor replay. No credential/token bytes printed. Source LOGIN @081e8c78."""
from pathlib import Path
from Crypto.Cipher import AES
import struct,hashlib
W=Path(__file__).resolve().parent
for sp in sorted((W/'captures').rglob('*.bin')):
 parts=sp.stem.split('-')
 if len(parts)!=2 or parts[0] not in ('10013','30011'):continue
 cp=sp.with_name(parts[1]+'-'+parts[0]+'.bin')
 if not cp.exists():continue
 sr=sp.read_bytes();cr=cp.read_bytes()
 if sr[:3]!=b'\0\1\0' or cr[:3]!=b'\1\1\0':continue
 s=sr[:struct.unpack_from('<I',sr,3)[0]];c=cr[:struct.unpack_from('<I',cr,3)[0]]
 k=bytes((((v<<6)|(v>>2))&255)^181 for v in s[15:])[:334]
 d=AES.new(k[8:24],AES.MODE_ECB).decrypt(c[13:]);pos=0;fields=[]
 def take(n,label,hide=False):
  global pos
  assert 0<=n and pos+n<=len(d)
  x=d[pos:pos+n];fields.append((label,pos,n,hashlib.sha256(x).hexdigest() if hide else int.from_bytes(x,'little')));pos+=n;return x
 def u32(label):return int.from_bytes(take(4,label),'little')
 def string(n,cap,label):
  global pos
  assert 0<=n<cap and pos+n<=len(d)
  start=pos
  for _ in range(n):
   v=d[pos];pos+=1
   if not v:break
  fields.append((label,start,pos-start,'sha256='+hashlib.sha256(d[start:pos]).hexdigest()))
 u32('patch');n=u32('token_length');string(n,2049,'token')
 n=u32('string1_length');string(n,24,'string1')
 n=u32('string2_length');string(n,132,'string2')
 assert take(1,'flag')[0] in (0,1,2)
 assert u32('mouse_length')==8;take(8,'mouse_encrypted',True)
 u32('compare_DataManager_0x3c0');u32('u32_after_version');take(2,'server_group');take(4,'local_ip',True);take(1,'loginout_state')
 print(str(cp.relative_to(W/'captures')),'SHA256='+hashlib.sha256(cr).hexdigest())
 print('FIELDS',fields)
 print('CURSOR='+str(pos),'PLAIN_LEN='+str(len(d)),'UNCONSUMED='+str(len(d)-pos),'UNCONSUMED_ALL_ZERO='+str(not any(d[pos:])))
 assert pos<=len(d)
print('LIMIT: unconsumed bytes not named padding without producer proof; mouse plaintext and downstream login acceptance not verified.')
