import struct
import unittest
from unittest.mock import Mock,patch
import ctypes as c
from tools.debug import motion_test_trace as m

def fixture():
    raw=bytearray(64+256*64)
    struct.pack_into('<6IQ2I3Q',raw,0,0x4D543031,1,64,64,256,42,12345,6,1,3,0,0)
    for i in range(3):struct.pack_into('<QQ4I2i6I',raw,64+i*64,i+1,1000+i,99,0x10000,0x20000,0x4B3C30,-1,252,0,529,0x30000,12,13,i%2)
    return bytes(raw)

class MotionTestTraceTests(unittest.TestCase):
    def decode(self,raw):return m.decode_block(raw,pid=42,started=12345,now=1100)
    def test_refusal_preserves_actual_signed_xy_axis_and_native_method(self):
        v=self.decode(fixture());self.assertEqual(v['serial'],3)
        self.assertEqual([e['serial'] for e in v['events']],[1,2,3]);self.assertEqual(v['events'][0]['x'],-1)
        self.assertEqual(v['events'][1]['axis'],'y');self.assertEqual(v['events'][0]['result'],False)
    def test_torn_wrong_identity_or_bad_ring_cannot_be_consumed(self):
        for offset,value in [(32,7),(20,43),(40,4),(64,2),(64+44,530),(64+60,2)]:
            with self.subTest(offset=offset):
                a=bytearray(fixture());struct.pack_into('<I',a,offset,value)
                with self.assertRaises(m.MotionTraceError):self.decode(a)
    def test_old_events_are_labelled_with_age_not_presented_as_new(self):
        v=self.decode(fixture());self.assertEqual(v['latest_age_ms'],98)
        self.assertEqual(m.new_events(v,3),[]);self.assertEqual(len(m.new_events(v,1)),2)
    def test_v2_native_branch_is_distinct_from_inferred_grid_predicate(self):
        a=bytearray(fixture());struct.pack_into('<I',a,4,2)
        for i,path in enumerate((1,2,4)):struct.pack_into('<II',a,64+i*64+52,path,7 if path==4 else 0)
        v=self.decode(a);self.assertEqual([e['native_path'] for e in v['events']],['independent_bounds','first_grid_query','state_gate'])
        self.assertEqual(v['events'][-1]['native_state_value'],7)
        struct.pack_into('<I',a,64+2*64+56,5)
        with self.assertRaises(m.MotionTraceError):self.decode(a)
    def test_wrap_is_ordered_by_serial_with_explicit_losses(self):
        a=bytearray(fixture());struct.pack_into('<Q',a,40,258)
        for serial in range(3,259):
            struct.pack_into('<QQ4I2i6I',a,64+(serial-1)%256*64,serial,1000,99,0x10000,0x20000,0x4B3C30,127,252,0,529,0x30000,12,13,0)
        v=self.decode(a);self.assertEqual(v['events'][0]['serial'],3);self.assertEqual(v['overwrites'],2)

    def test_standalone_module_is_selected_before_original_ijl(self):
        modules=[(10,'G:/game/ijl15.dll'),(20,'G:/game/DFLegacy.Client.dll')]
        self.assertEqual(m.select_patch_module(modules,'G:/game/DNF.exe'),20)
    def test_legacy_module_selection_rejects_foreign_and_ambiguous_modules(self):
        self.assertEqual(m.select_patch_module([(10,'G:/game/ijl15.dll')],'G:/game/DNF.exe'),10)
        for modules in [[(10,'G:/foreign/DFLegacy.Client.dll')],[(10,'G:/game/DFLegacy.Client.dll'),(20,'G:/game/DFLegacy.Client.dll')]]:
            with self.assertRaises(m.MotionTraceError):m.select_patch_module(modules,'G:/game/DNF.exe')

    def test_reader_requests_query_information_for_named_module_query(self):
        # GetModuleFileNameEx(non-null module) requires QUERY_INFORMATION,
        # whereas QueryFullProcessImageName alone also accepts LIMITED.
        kernel=Mock();granted=[]
        def open_process(access,inherit,pid):
            granted.append(access);return 77 if access&0x410==0x410 else 0
        def query(handle,flags,name,length):name.value='G:/game/DNF.exe';return 1
        def modules(handle,values,size,needed,flags):
            values[0]=0x100000;c.cast(needed,c.POINTER(c.c_ulong))[0]=c.sizeof(c.c_void_p);return 1
        def filename(handle,module,name,length):name.value='G:/game/ijl15.dll';return len(name.value)
        kernel.OpenProcess.side_effect=open_process;kernel.QueryFullProcessImageNameW.side_effect=query
        kernel.K32EnumProcessModulesEx.side_effect=modules;kernel.K32GetModuleFileNameExW.side_effect=filename
        with patch.object(m.c,'WinDLL',return_value=kernel,create=True),patch.object(m.state,'get_process_started_filetime',return_value=12345),patch.object(m.Reader,'_export',return_value=0x100100):
            reader=m.Reader(42,12345,'G:/game/DNF.exe');self.assertEqual(reader.address,0x100100);reader.close()
        self.assertEqual(granted,[0x410]);kernel.CloseHandle.assert_called_once_with(77)

if __name__=='__main__':unittest.main()
