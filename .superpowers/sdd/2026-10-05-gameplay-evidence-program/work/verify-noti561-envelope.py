"""Offline framing hypothesis test derived from EncodeInt + DPInterDataHead serializers."""
from pathlib import Path
import struct,hashlib,collections
W=Path(__file__).resolve().parent
for rel in ['streams-live/10013-2343.bin','streams-live2/10013-14805.bin','streams-vm/10013-13681.bin']:
 b=(W/'captures'/rel).read_bytes();p=0;count=0;deltas=collections.Counter();types=collections.Counter()
 while p<len(b):
  n=struct.unpack_from('<I',b,p+3)[0];assert n>=15 and p+n<=len(b);f=b[p:p+n];p+=n
  if f[:3]!=b'\0\x31\x02':continue
  body=f[15:];assert len(body)>=9
  outer=struct.unpack_from('>I',body)[0];headlen,kind,flag=struct.unpack_from('>HHB',body,4)
  deltas[len(body)-outer]+=1;types[kind]+=1
  assert headlen+4==outer,(rel,count,headlen,outer)
  assert outer<=len(body)
  print(rel,'frame='+str(count),'outer='+str(outer),'inter_len='+str(headlen),'kind='+str(kind),'flag='+str(flag),'residual='+str(len(body)-outer))
  count+=1
 print('SUMMARY',rel,'sha256='+hashlib.sha256(b).hexdigest(),'count='+str(count),'residual_hist='+str(dict(deltas)),'type_hist='+str(dict(types)))
print('LIMIT: structural header checks only; residual bytes and payload contents not assigned semantics.')
