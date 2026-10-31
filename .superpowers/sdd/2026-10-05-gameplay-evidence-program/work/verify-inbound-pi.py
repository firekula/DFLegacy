"""Offline inbound PI replay. Exact sources: inbound-*-static.txt, CUser::Parsing."""
from pathlib import Path
import struct,hashlib,ast
from Crypto.Cipher import AES
W=Path(__file__).resolve().parent
# Reuse only definitions from the already reviewed outbound probe, without executing its tests.
tree=ast.parse((W/'verify-pi-boundary.py').read_text(encoding='utf-8'))
ns={'M':0xffffffff};exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef)],type_ignores=[]),'<shared-pi-functions>','exec'),ns)
sf,unpack=ns['sf'],ns['unpack']
def crc(data):
 c=0xffffffff
 for x in data:
  c^=x
  for _ in range(8):c=(c>>1)^(0x4db89129 if c&1 else 0)
 q=struct.pack('<I',c^0xffffffff);return q[0]^q[1]^q[2]^q[3]^24
for rel in ['streams-live/2343-10013.bin','streams-live2/14805-10013.bin','streams-vm/13681-10013.bin']:
 p=W/'captures'/rel;b=p.read_bytes();o=0;n=0;prev=0x27;bad=[];seq=[]
 while o<len(b):
  assert o+13<=len(b);z=struct.unpack_from('<I',b,o+3)[0];assert 13<=z<=1400 and o+z<=len(b)
  f=b[o:o+z];op=struct.unpack_from('<H',f,1)[0];body=f[13:]
  if f[0]==1 and op==1:prev=0x27
  h,new=unpack(int.from_bytes(f[9:11],'little'))
  q=struct.pack('<I',sf(body));expect=q[0]^q[1]^q[2]^q[3]^prev
  if body and h!=expect:bad.append((n,op))
  prev=new;seq.append(struct.unpack_from('<H',f,11)[0]);o+=z;n+=1
 assert not bad
 assert seq==list(range(len(seq)))
 # CMD1 plaintext CRC includes the two unencrypted sequence bytes.
 peer=p.with_name('10013-'+p.stem.split('-')[0]+'.bin');s=peer.read_bytes();size=struct.unpack_from('<I',s,3)[0]
 key=bytes((((x<<6)|(x>>2))&255)^181 for x in s[15:size])[:334]
 f=b[:struct.unpack_from('<I',b,3)[0]];plain=AES.new(key[8:24],AES.MODE_ECB).decrypt(f[13:]);covered=f[11:13]+plain
 assert crc(covered)==f[7]
 assert crc(bytes([covered[0]^1])+covered[1:])!=f[7]
 assert crc(covered[:-1]+bytes([covered[-1]^1]))!=f[7]
 print(rel,'SHA256='+hashlib.sha256(b).hexdigest(),'frames='+str(n),'PI_CIPHER_CHAIN=PASS SEQUENCE_OBSERVED_0_TO_20=PASS CMD1_CRC_SEQUENCE_PLUS_PLAIN=PASS TWO_MUTATION_NEGATIVES=PASS')
print('LIMIT: observed sequence increment is not proof server checks it; UpdatePacket has no sequence comparison. No other opcode plaintext decrypted here.')
