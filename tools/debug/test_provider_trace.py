import struct,unittest,zlib
from tools.debug import provider_trace as p

def fixture(result=1,inner=True):
    raw=bytearray(p.SIZE);struct.pack_into('<6IQ2I3Q',raw,0,0x50543031,1,64,4168,32,42,12345,2,1,1,0,0)
    outer_body=b'outer-bytes';inside_body=b'inner-bytes'
    outer=struct.pack('<BHI',0,42,zlib.crc32(outer_body))+outer_body
    inside=struct.pack('<BHI',0,4,zlib.crc32(inside_body))+inside_body if inner else b''
    struct.pack_into('<3Q12I',raw,64,1,1000,2000000,99,0x10000,0x30008000,result,0x20000,len(outer),len(outer),0x30000 if inner else 0,len(inside),len(inside),3 if inner else 1,0)
    raw[136:136+len(outer)]=outer;raw[136+2048:136+2048+len(inside)]=inside
    return bytes(raw)
class ProviderTraceTests(unittest.TestCase):
    def decode(self,raw):return p.decode_block(raw,pid=42,started=12345,now=1100)
    def test_actual_provider_result_is_distinct_from_inner_crc_and_dispatch(self):
        v=self.decode(fixture());e=v['events'][0];self.assertTrue(e['provider_succeeded']);self.assertEqual(e['source_hex'][2:6],'2a00')
        self.assertEqual(e['inner_length'],18);self.assertEqual(e['frame_token'],0x30008000);self.assertEqual(e['counter_us'],2000000)
    def test_failure_with_no_output_remains_an_observed_failure(self):
        e=self.decode(fixture(0,False))['events'][0];self.assertFalse(e['provider_succeeded']);self.assertIsNone(e['inner_hex']);self.assertFalse(e['inner_known'])
    def test_unknown_capture_and_truncation_are_explicit(self):
        raw=bytearray(fixture());struct.pack_into('<I',raw,64+44,3000);struct.pack_into('<I',raw,64+48,2048)
        self.assertTrue(self.decode(raw)['events'][0]['source_truncated'])
        struct.pack_into('<I',raw,64+64,2);struct.pack_into('<I',raw,64+48,0)
        self.assertIsNone(self.decode(raw)['events'][0]['source_hex'])
    def test_wrong_identity_torn_ring_bad_lengths_and_non42_source_are_rejected(self):
        for offset,value in [(20,43),(32,3),(64,2),(64+48,2049),(64+64,4),(136+1,7)]:
            with self.subTest(offset=offset):
                raw=bytearray(fixture());struct.pack_into('<I',raw,offset,value)
                with self.assertRaises(p.MotionTraceError):self.decode(raw)
if __name__=='__main__':unittest.main()
