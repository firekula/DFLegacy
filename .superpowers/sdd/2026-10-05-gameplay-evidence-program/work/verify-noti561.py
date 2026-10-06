"""Actual NOTI561 inventory; fail on malformed frames. No wrapper decryption claimed."""
from pathlib import Path
import struct,hashlib
W=Path(__file__).resolve().parent
for rel in ['streams-live/10013-2343.bin','streams-live2/10013-14805.bin','streams-vm/10013-13681.bin','streams/30011-46386.bin']:
 p=W/'captures'/rel;b=p.read_bytes();o=0;index=0;matches=[]
 while o<len(b):
  assert o+15<=len(b)
  n=struct.unpack_from('<I',b,o+3)[0];assert n>=15 and o+n<=len(b)
  f=b[o:o+n];cat=f[0];op=struct.unpack_from('<H',f,1)[0]
  if cat==0 and op==561:matches.append((index,o,n,n-15,hashlib.sha256(f[15:]).hexdigest()))
  o+=n;index+=1
 print(rel,'SHA256='+hashlib.sha256(b).hexdigest(),'frames='+str(index),'NOTI561_COUNT='+str(len(matches)))
 for m in matches:print('index,offset,frame_len,body_len,body_sha256=',m)
 if rel.startswith('streams/'):assert not matches
 else:assert matches
print('LIMIT: opcode classification and payload hashes only; wrapper plaintext not recovered.')
