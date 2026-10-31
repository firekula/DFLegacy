"""Try static key reconstruction for NOTI561 using observed envelope fields.
Only reports candidates that pass exact Crc32 on encrypted body; no plaintext claim."""
from pathlib import Path
import struct,hashlib
W=Path(__file__).resolve().parent
def crc(data):
 c=0xffffffff
 for x in data:
  c^=x
  for _ in range(8):c=(c>>1)^(0xedb88320 if c&1 else 0)
 return (~c)&0xffffffff
def gen(choice,seed):
 # GenKey calls TenMd5 on 8-byte words choice+index and produces 32B.
 out=bytearray()
 for i in range(2):out+=hashlib.md5(struct.pack('<II',choice+i,seed)).digest()
 return bytes(out)
for rel in ['streams-live/10013-2343.bin','streams-live2/10013-14805.bin','streams-vm/10013-13681.bin']:
 b=(W/'captures'/rel).read_bytes();p=0;rows=[]
 while p<len(b):
  n=struct.unpack_from('<I',b,p+3)[0];f=b[p:p+n];p+=n
  if struct.unpack_from('<H',f,1)[0]!=561:continue
  body=f[15:];outer=struct.unpack_from('>I',body)[0];typ=struct.unpack_from('>H',body,4)[0]
  # fixed encrypt stores choice in protocol data; derive candidates from observed type and small seed space is not evidence.
  rows.append((n-15,outer,typ,hashlib.sha256(body).hexdigest()))
 print(rel,'rows='+str(len(rows)),'length_type_samples='+str(rows[:4]))
print('LIMIT: GenKey call input uses runtime choice/seed; this script intentionally does not brute-force unknown random state or claim decryption.')
