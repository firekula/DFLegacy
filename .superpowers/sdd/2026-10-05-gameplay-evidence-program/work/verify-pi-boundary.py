"""Replay outgoing PI hash/bit layout, no live traffic. Sources: pi-*-static.txt."""
from pathlib import Path
import struct, hashlib
W=Path(__file__).resolve().parent
M=0xffffffff
def sf(d):
 if not d:return 0
 h=len(d);n=len(d)//4;p=0
 for _ in range(n):
  h=(h+int.from_bytes(d[p:p+2],'little'))&M
  t=((int.from_bytes(d[p+2:p+4],'little')<<11)^h)&M
  h=((h<<16)^t)&M;p+=4;h=(h+(h>>11))&M
 r=len(d)%4
 if r==3:
  h=(h+int.from_bytes(d[p:p+2],'little'))&M;h^=(h<<16)&M;h^=d[p+2]<<18;h=(h+(h>>11))&M
 elif r==2:
  h=(h+int.from_bytes(d[p:p+2],'little'))&M;h^=(h<<11)&M;h=(h+(h>>17))&M
 elif r==1:
  h=(h+d[p])&M;h^=(h<<10)&M;h=(h+(h>>1))&M
 h^=(h<<3)&M;h=(h+(h>>5))&M;h^=(h<<4)&M;h=(h+(h>>17))&M;h^=(h<<25)&M;return (h+(h>>6))&M
def pack(h,r):
 return ((h<<6)&255)|((h&12)<<12)|((h&48)>>2)|((h&192)<<4)|((r&3)<<12)|((r&12)<<2)|((r&48)<<4)|(r>>6)
def unpack(v):
 h=((v>>6)&3)|((v>>12)&12)|((v<<2)&48)|((v>>4)&192)
 r=((v>>12)&3)|((v>>2)&12)|((v>>4)&48)|((v&3)<<6)
 return h,r
for h in range(256):
 for r in range(256):assert unpack(pack(h,r))==(h,r)
print('BIT_LAYOUT_ALL_65536=PASS')
for rel in ['streams-live/10013-2343.bin','streams-live2/10013-14805.bin']:
 raw=(W/'captures'/rel).read_bytes();p=0;prev=0x27;count=0;bad=[]
 while p<len(raw):
  assert p+15<=len(raw);n=struct.unpack_from('<I',raw,p+3)[0];assert n>=15 and p+n<=len(raw)
  f=raw[p:p+n];opcode=struct.unpack_from('<H',f,1)[0]
  if f[0]==0 and opcode==1:prev=0x27
  h,r=unpack(int.from_bytes(f[13:15],'little'))
  v=sf(f[15:]);q=struct.pack('<I',v);expect=q[0]^q[1]^q[2]^q[3]^prev
  if not f[15:]:
   print(rel,'EMPTY_BODY_RANDOM_HASH_NOT_PREDICTED frame='+str(count)+' opcode='+str(opcode))
  elif h!=expect:bad.append((count,opcode))
  prev=r;p+=n;count+=1
 print(rel,'SHA256='+hashlib.sha256(raw).hexdigest(), 'frames='+str(count),'hash_mismatch='+str(bad))
 assert not bad, 'nonempty payload hash mismatch'
print('LIMIT: random bytes decoded from captures, not predicted; no anti-cheat process implementation.')
