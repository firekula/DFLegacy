"""Offline NOTI561 candidate framing audit. No decryption claim; static branch/capture shape only."""
from pathlib import Path
import struct,hashlib
W=Path(__file__).resolve().parent
for p in sorted((W/'captures').rglob('30011-*.bin')):
 b=p.read_bytes();o=0;frames=[]
 while o+7<=len(b):
  n=struct.unpack_from('<I',b,o+3)[0]
  if n<13 or o+n>len(b):break
  f=b[o:o+n];frames.append((struct.unpack_from('<H',f,1)[0],n,f[:15].hex()));o+=n
 if not frames:continue
 print(str(p.relative_to(W/'captures')),'SHA256='+hashlib.sha256(b).hexdigest(),'FRAMES='+repr(frames))
 assert frames[0][0]==1
print('LIMIT: NOTI561 static sender path is anti-bot SendBufferClient; no plaintext interpretation or anti-cheat process implementation claimed.')
