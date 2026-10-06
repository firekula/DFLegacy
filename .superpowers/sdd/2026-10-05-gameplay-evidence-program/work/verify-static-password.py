"""Offline capture cross-check for statically derived offsets/mode. No network; no token dump."""
from pathlib import Path
import base64, hashlib, struct
from Crypto.Cipher import AES
from Crypto.PublicKey import RSA
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
W=Path(__file__).resolve().parent
pubpath=W/'vm-files/vm-game-publickey.pem'; pub=RSA.import_key(pubpath.read_bytes())
def crc_ne(data):
 c=0xffffffff
 for x in data:
  c ^= x
  for _ in range(8): c=(c>>1)^(0x4db89129 if c&1 else 0)
 c ^= 0xffffffff
 q=struct.pack('<I',c)
 return (c&0xffffff00)|(q[0]^q[1]^q[2]^q[3]^0x18)
lines=['PUBLIC_KEY_SHA256='+hashlib.sha256(pubpath.read_bytes()).hexdigest()]
for folder,sp,cp in [('streams-live','10013-2343.bin','2343-10013.bin'),('streams-live2','10013-14805.bin','14805-10013.bin')]:
 sraw=(W/'captures'/folder/sp).read_bytes(); craw=(W/'captures'/folder/cp).read_bytes()
 s=sraw[:struct.unpack_from('<I',sraw,3)[0]]; c=craw[:struct.unpack_from('<I',craw,3)[0]]
 key=bytes((((x<<6)|(x>>2))&255)^0xb5 for x in s[15:])[:334]
 assert crc_ne(s[15:])==struct.unpack_from('<I',s,7)[0]
 plain=AES.new(key[8:24],AES.MODE_ECB).decrypt(c[13:])
 independent=Cipher(algorithms.AES(key[8:24]),modes.ECB()).decryptor()
 assert independent.update(c[13:])+independent.finalize()==plain
 patch,n=struct.unpack_from('<II',plain)
 assert patch<=3600 and 0<n<2049 and 8+n<=len(plain)
 token=plain[8:8+n]; sig=base64.b64decode(token,validate=True)
 assert base64.b64encode(sig)==token
 block=pow(int.from_bytes(sig,'big'),pub.e,pub.n).to_bytes(pub.size_in_bytes(),'big')
 assert block[:2]==b'\x00\x01'
 end=block.index(b'\0',2);assert end>=10 and block[2:end]==b'\xff'*(end-2)
 msg=block[end+1:]; assert 0<len(msg)<=104
 wrong=AES.new(key[9:25],AES.MODE_ECB).decrypt(c[13:]); assert wrong!=plain
 lines += [f'{folder} S_SHA256={hashlib.sha256(sraw).hexdigest()} C_SHA256={hashlib.sha256(craw).hexdigest()}',f'{folder} NOTI1_ENCRYPTED_CRC=PASS CMD1_MODE=ECB KEY_SLICE=[8:24] TWO_AES_IMPLEMENTATIONS=PASS PLAIN_LEN={len(plain)} PATCH={patch} TOKEN_LEN={n} RSA_BLOCK={len(sig)} RSA_TYPE1=PASS RECOVERED_LEN={len(msg)} WRONG_KEY_NEGATIVE=PASS']
lines += ['LIMIT: no singleton memory snapshot; RSA result not yet independently verified by OpenSSL; accounts not printed or attributed.']
print('\n'.join(lines))
