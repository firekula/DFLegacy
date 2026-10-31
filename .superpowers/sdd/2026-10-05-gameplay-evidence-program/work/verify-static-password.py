"""Offline capture cross-check for statically derived offsets/mode. No network; no token dump."""
from pathlib import Path
import base64, hashlib, struct, subprocess, tempfile, os
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
pairs=[]
for spath in sorted((W/'captures').rglob('*.bin')):
 parts=spath.stem.split('-')
 if len(parts)!=2 or parts[0] not in ('10013','30011'): continue
 cpath=spath.with_name(parts[1]+'-'+parts[0]+'.bin')
 if not cpath.exists() or spath.read_bytes()[:3]!=b'\x00\x01\x00' or cpath.read_bytes()[:3]!=b'\x01\x01\x00':
  lines.append('SKIP_NO_PAIRED_LOGIN='+str(spath.relative_to(W/'captures')));continue
 pairs.append((str(spath.parent.relative_to(W/'captures')),spath.name,cpath.name))
seen=set()
for folder,sp,cp in pairs:
 pubpath=(W/'vm-files/vm-game-publickey.pem') if sp.startswith('10013-') else (W.parents[1]/'2026-10-05-official-login-channel/work/container-game-publickey.pem')
 pub=RSA.import_key(pubpath.read_bytes())
 sraw=(W/'captures'/folder/sp).read_bytes(); craw=(W/'captures'/folder/cp).read_bytes()
 identity=(hashlib.sha256(sraw).hexdigest(),hashlib.sha256(craw).hexdigest())
 lines.append(f'PAIR={folder}/{sp} <-> {cp} PUB_SHA256={hashlib.sha256(pubpath.read_bytes()).hexdigest()} DUPLICATE={identity in seen}')
 seen.add(identity)
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
 with tempfile.TemporaryDirectory() as td:
  sigp=Path(td)/'sig.bin'; outp=Path(td)/'out.bin'
  sigp.write_bytes(sig)
  subprocess.run(['D:/Program Files/Git/usr/bin/openssl.exe','pkeyutl','-verifyrecover','-pubin','-inkey',str(pubpath),'-in',str(sigp),'-pkeyopt','rsa_padding_mode:none','-out',str(outp)],check=True)
  assert outp.read_bytes()==block
  subprocess.run(['D:/Program Files/Git/usr/bin/openssl.exe','pkeyutl','-verifyrecover','-pubin','-inkey',str(pubpath),'-in',str(sigp),'-pkeyopt','rsa_padding_mode:pkcs1','-out',str(outp)],check=True)
  assert outp.read_bytes()==msg
 assert AES.new(key[8:24],AES.MODE_ECB).encrypt(plain)==c[13:]
 auth=bytearray(104);auth[:len(msg)-1]=msg[:-1];struct.pack_into('<I',auth,100,patch)
 lines += [f'{folder} OPENSSL_RAW_AND_PKCS1=PASS CIPHER_ROUNDTRIP=PASS AUTH_COPY_LEN={len(msg)-1} UID_BE={int.from_bytes(auth[:4],"big")} UNAME_32_SHA256={hashlib.sha256(auth[4:36]).hexdigest()} TIMESTAMP_BE_PLUS_PATCH={(int.from_bytes(auth[36:40],"big")+patch)&0xffffffff} GCA_BE={int.from_bytes(auth[40:42],"big")} MOBILE_IS_ONE={auth[44]==1}']
 wrong=AES.new(key[9:25],AES.MODE_ECB).decrypt(c[13:]); assert wrong!=plain
 lines += [f'{folder} S_SHA256={hashlib.sha256(sraw).hexdigest()} C_SHA256={hashlib.sha256(craw).hexdigest()}',f'{folder} NOTI1_ENCRYPTED_CRC=PASS CMD1_MODE=ECB KEY_SLICE=[8:24] TWO_AES_IMPLEMENTATIONS=PASS PLAIN_LEN={len(plain)} PATCH={patch} TOKEN_LEN={n} RSA_BLOCK={len(sig)} RSA_TYPE1=PASS RECOVERED_LEN={len(msg)} WRONG_KEY_NEGATIVE=PASS']
lines += [f'PAIRED_FILES={len(pairs)} UNIQUE_CONTENT_PAIRS={len(seen)}']
lines += ['LIMIT: no singleton memory snapshot; UID is token accessor value only, not a verified account-name attribution. OpenSSL command executable path is pinned to installed Git.']
print('\n'.join(lines))
