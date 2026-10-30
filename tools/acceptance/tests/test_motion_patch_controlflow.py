"""Audit real installation sites and execute same-row and cross-row tails."""
import os,struct,unittest,re
from tools.acceptance.motion_replay_fixture import load_original_image
class MotionPatchControlFlowTests(unittest.TestCase):
 def test_no_original_jump_enters_a_patch_interior_and_both_grid_paths_return(self):
  path=os.environ.get('DFLEGACY_NATIVE_LOOP_DLL')
  if not path:self.skipTest('Explicit actual IJL required')
  executable=os.environ.get('DFLEGACY_DNF_EXE')
  import pefile,capstone
  from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
  from unicorn.x86_const import UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EFLAGS
  p=pefile.PE(path);raw=p.get_memory_mapped_image();base=p.OPTIONAL_HEADER.ImageBase
  exports=[e for e in p.DIRECTORY_ENTRY_EXPORT.symbols if e.name==b'DflMotionProbeSites'];self.assertEqual(len(exports),1,'inspect the actual installer table')
  at=exports[0].address;magic,version,count,size=struct.unpack_from('<4I',raw,at);self.assertEqual((magic,version,count,size),(0x4D505331,1,5,32))
  sites=[struct.unpack_from('<4I8s2I',raw,at+16+i*32) for i in range(count)]
  original=load_original_image(executable=executable)
  md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_32);md.detail=True
  for address,target,length,call,expected,_,_ in sites:self.assertEqual(original.get_data(address-0x400000,length),expected[:length])
  for ins in md.disasm(original.get_data(0x4B3C30-0x400000,0x1EB),0x4B3C30):
   if ins.group(capstone.CS_GRP_JUMP) and ins.operands[0].type==capstone.x86.X86_OP_IMM:
    target=ins.operands[0].imm
    self.assertFalse(any(address<target<address+length for address,_,length,*_ in sites),f'{ins.address:X} jumps into patched instruction at {target:X}')
  probes={}
  for match in re.finditer(rb'\x9c\x60\x81\xec\x20\x02\x00\x00',raw):
   code=list(md.disasm(raw[match.start():match.start()+100],base+match.start()));i=next(i for i,x in enumerate(code) if x.mnemonic=='call');point=int(code[i-1].op_str,0)
   if point in (112,115):
    entry=match.start()-2 if point==112 and raw[match.start()-2:match.start()]==b'\x6a\x00' else match.start()
    probes[point]=(base+entry,int(code[i].op_str,0))
  self.assertEqual(set(probes),{112,115})
  for y,grid_result in [(251,0),(251,1),(252,0),(252,1),(-1,1),(359,0)]:
   with self.subTest(y=y,grid_result=grid_result):
    outcomes=[]
    for patched in (False,True):
     u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(base,(len(raw)+4095)//4096*4096);u.mem_write(base,raw)
     for address,size in [(0x4B3000,0x2000),(0x42C000,0x1000),(0x8A8000,0x2000),(0xAFD000,0x12000),(0x10000,0x2000),(0x20000,0x3000),(0x30000000,0x10000),(0x40000000,0x3000)]:u.mem_map(address,size)
     u.mem_write(0x4B3C30,original.get_data(0x4B3C30-0x400000,0x1EB))
     if patched:
      for address,target,length,call,*_ in sites:
       if 0x4B3000<=address<0x4B5000:u.mem_write(address,bytes([0xE8 if call else 0xE9])+struct.pack('<I',(target-address-5)&0xffffffff)+b'\x90'*(length-5))
     # Execute the original no-argument v18C getter and actual ret16 grid
     # function. The flag pushed before v18C belongs to the later grid call.
     u.mem_write(0x8A8FE0,original.get_data(0x8A8FE0-0x400000,0x76));u.mem_write(0x42C990,original.get_data(0x42C990-0x400000,0x10))
     u.mem_write(0x10000,struct.pack('<I',0x11000));u.mem_write(0x10424,struct.pack('<I',100));u.mem_write(0x1118C,struct.pack('<I',0x42C990))
     u.mem_write(0xB0DE98,struct.pack('<3I',100,100,0x20000));u.mem_write(0x20000,bytes([0 if grid_result else 2])*10000)
     frame=0x30008000;out=0x30006000;u.mem_write(out,struct.pack('<I',777));u.mem_write(frame,struct.pack('<5I',0x30009000,0x40000000,1,y&0xffffffff,out))
     u.mem_write(frame-16,struct.pack('<4I',0x1122,0x3344,0x5566,31));u.reg_write(UC_X86_REG_ESP,frame-16);u.reg_write(UC_X86_REG_EBP,frame);u.reg_write(UC_X86_REG_ESI,0x10000);u.reg_write(UC_X86_REG_EDI,int(y/12))
     # FirstGridProbe already replays the native mov ECX,[EBP+C] and resumes
     # at DCC. Starting at DC9 would itself enter that legitimate patch.
     u.reg_write(UC_X86_REG_ECX,y&0xffffffff)
     calls=[];returns=[]
     def hook(cpu,address,size,unused):
      if patched and address in {v[1] for v in probes.values()}:
       sp=cpu.reg_read(UC_X86_REG_ESP);ret,point,slot,saved=struct.unpack('<4I',bytes(cpu.mem_read(sp,16)));calls.append(point)
       if point==115:
        self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(slot,4)))[0],0x4B3DFF)
        self.assertEqual(struct.unpack('<4I',bytes(cpu.mem_read(slot+4,16))),(31,int((y+1)/12)&0xffffffff,100,1))
        returns.append(0x4B3DFF);cpu.mem_write(slot,struct.pack('<I',probes[112][0]))
       else:
        self.assertEqual(point,112);self.assertEqual(slot,frame-20)
        self.assertEqual(struct.unpack('<I',bytes(cpu.mem_read(saved+28,4)))[0],grid_result)
        cpu.mem_write(slot,struct.pack('<I',returns.pop()))
       for reg in [UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]:cpu.reg_write(reg,0)
       cpu.reg_write(UC_X86_REG_EFLAGS,0x202)
       cpu.reg_write(UC_X86_REG_ESP,sp+16);cpu.reg_write(UC_X86_REG_EIP,ret)
      elif address==0x8A8FE0:
       sp=cpu.reg_read(UC_X86_REG_ESP)
       self.assertEqual(struct.unpack('<4I',bytes(cpu.mem_read(sp+4,16))),(31,int((y+1)/12)&0xffffffff,100,1))
       self.assertEqual(cpu.reg_read(UC_X86_REG_ECX),0xAFDE10)
      elif address==0x40000000:cpu.emu_stop()
     u.hook_add(UC_HOOK_CODE,hook)
     try:u.emu_start(0x4B3DCC,0x40000001,count=500)
     except Exception as error:self.fail(f'patched={patched} EIP={u.reg_read(UC_X86_REG_EIP):X} EAX={u.reg_read(UC_X86_REG_EAX):X} ESP={u.reg_read(UC_X86_REG_ESP):X}: {error}')
     self.assertEqual(u.reg_read(UC_X86_REG_EIP),0x40000000);self.assertEqual(u.reg_read(UC_X86_REG_ESP),frame+20)
     crosses=int(y/12)!=int((y+1)/12);self.assertEqual(calls,[115,112] if patched and crosses else [])
     outcomes.append(([u.reg_read(r) for r in [UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP,UC_X86_REG_ESP,UC_X86_REG_EFLAGS]],bytes(u.mem_read(out,4))))
    self.assertEqual(outcomes[0],outcomes[1])
if __name__=='__main__':unittest.main()
