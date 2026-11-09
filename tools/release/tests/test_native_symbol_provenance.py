import importlib.util, pathlib, struct, tempfile, unittest, uuid
MODULE=pathlib.Path(__file__).parents[1]/"retain_native_symbols.py"
class Symbols(unittest.TestCase):
 def test_matched_private_pair_and_mismatch(self):
  self.assertTrue(MODULE.exists(),"private matched-symbol provenance step must exist")
  spec=importlib.util.spec_from_file_location("symbols",MODULE);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
  with tempfile.TemporaryDirectory() as temp:
   root=pathlib.Path(temp);guid=uuid.uuid4();age=3
   pe=bytearray(2048);pe[:2]=b'MZ';struct.pack_into('<I',pe,60,128);pe[128:132]=b'PE\0\0';struct.pack_into('<HH',pe,132,0x14c,1);struct.pack_into('<H',pe,148,224);struct.pack_into('<H',pe,152,0x10b);struct.pack_into('<II',pe,152+96+48,4096,28);struct.pack_into('<IIII',pe,376+8,1024,4096,1024,512)
   cv=b'RSDS'+guid.bytes_le+struct.pack('<I',age)+b'own.pdb\0';struct.pack_into('<IIHHIIII',pe,512,0,0,0,0,2,len(cv),4124,540);pe[540:540+len(cv)]=cv
   pdb=bytearray(512*5);pdb[:32]=b'Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0';struct.pack_into('<IIIIII',pdb,32,512,1,5,16,0,2);struct.pack_into('<I',pdb,1024,3);struct.pack_into('<IIII',pdb,1536,2,0xffffffff,28,4);struct.pack_into('<III',pdb,2048,20000404,1,age);pdb[2060:2076]=guid.bytes_le
   image=root/'own.dll';symbols=root/'own.pdb';image.write_bytes(pe);symbols.write_bytes(pdb)
   result=module.retain([(image,symbols)],root/'private','a'*40)
   self.assertEqual(result['modules'][0]['guid'],str(guid));self.assertEqual(result['modules'][0]['age'],age);self.assertEqual(result['sourceSha'],'a'*40)
   self.assertTrue((root/'private'/'own.pdb').exists());self.assertEqual(len(result['modules'][0]['imageSha256']),64)
   pdb[2060]^=1;symbols.write_bytes(pdb)
   with self.assertRaises(ValueError):module.retain([(image,symbols)],root/'wrong','a'*40)
   self.assertFalse((root/'wrong').exists())
   leaked=b'RSDS'+guid.bytes_le+struct.pack('<I',age)+b'C:\\private\\own.pdb\0'
   struct.pack_into('<I',pe,528,len(leaked));pe[540:540+len(leaked)]=leaked
   with self.assertRaises(ValueError):module.pe_identity(pe)

if __name__=='__main__':unittest.main()
