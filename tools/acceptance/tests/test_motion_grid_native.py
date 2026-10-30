"""Execute original ACT1 candidate division, including the signed y+1 wrap."""
from pathlib import Path
import os,struct,unittest
from tools.debug.motion_test_trace import grid_cells

class MotionGridNativeTests(unittest.TestCase):
    def test_grid_coordinates_match_original_signed_machine_instructions(self):
        executable=os.environ.get('DFLEGACY_DNF_EXE')
        if not executable:
            executable='G:/Game/DNF_ACT1/Loot4_A/DNF.exe'
            if not Path(executable).is_file():self.skipTest('Original ACT1 executable required')
        import pefile
        from unicorn import Uc,UC_ARCH_X86,UC_MODE_32
        from unicorn.x86_const import UC_X86_REG_EBP,UC_X86_REG_ESI,UC_X86_REG_EBX,UC_X86_REG_EDI
        p=pefile.PE(executable)
        def signed(n):return n-0x100000000 if n&0x80000000 else n
        cases=[(-17,-13),(-16,-12),(-1,-1),(0,0),(15,5),(16,6),(127,11),(128,12),(656,167),(501,252),(439,338),(0,0x7fffffff),(-0x80000000,-0x80000000)]
        for x,y in cases:
            with self.subTest(x=x,y=y):
                u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(0x4B3000,0x2000);u.mem_map(0x30000000,0x10000)
                for start,end in [(0x4B3D31,0x4B3D59),(0x4B3DC9,0x4B3DDD)]:u.mem_write(start,p.get_data(start-0x400000,end-start))
                frame=0x30008000;u.mem_write(frame+8,struct.pack('<ii',x,y));u.reg_write(UC_X86_REG_EBP,frame);u.reg_write(UC_X86_REG_ESI,0x10000)
                u.emu_start(0x4B3D31,0x4B3D59,count=100);xx=signed(u.reg_read(UC_X86_REG_EBX));yy=signed(u.reg_read(UC_X86_REG_EDI))
                u.emu_start(0x4B3DC9,0x4B3DDD,count=100);next_y=signed(u.reg_read(UC_X86_REG_EBX))
                self.assertEqual(grid_cells(x,y),(xx,yy,next_y))
if __name__=='__main__':unittest.main()
