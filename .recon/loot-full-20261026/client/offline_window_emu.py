from raw_probe import *
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE, UC_HOOK_MEM_INVALID
from unicorn.x86_const import *
from capstone.x86_const import X86_OP_IMM, X86_OP_MEM, X86_OP_REG

# Bounded, isolated CPU emulation: no process is launched, no imports execute,
# no host API, file, socket or native function is callable. All external calls
# are intercepted and logged. Results depend on the explicit synthetic fixture.
REGS={'eax':UC_X86_REG_EAX,'ebx':UC_X86_REG_EBX,'ecx':UC_X86_REG_ECX,'edx':UC_X86_REG_EDX,'esi':UC_X86_REG_ESI,'edi':UC_X86_REG_EDI,'ebp':UC_X86_REG_EBP,'esp':UC_X86_REG_ESP}

def run(click, random15=0x0997, suffix='', entry=0x7ea940, exploratory=False, pickup_action=4, requested_rarity=4):
    uc=Uc(UC_ARCH_X86,UC_MODE_32)
    uc.mem_map(0,0x10000)
    uc.mem_map(BASE,0xC00000)
    uc.mem_write(BASE,pe.get_memory_mapped_image())
    uc.mem_map(0x1000000,0x100000)
    uc.mem_map(0x2000000,0x500000)
    this=0x2000000; box=0x2100000; actor=0x2200000; vt=0x2300000; stub=0x2400000
    def w(addr,val):uc.mem_write(addr,struct.pack('<I',val&0xffffffff))
    def r(addr):return struct.unpack('<I',uc.mem_read(addr,4))[0]
    for obj in (this,box,actor,actor+0x1000,actor+0x2000,actor+0x3000):
        w(obj,vt)
    for off in range(0,0x800,4):w(vt+off,stub+off)
    for off,val in ((8,box),(0x10,box),(0x30,box+0x1000),(0x34,box+0x2000),(0x38,box+0x3000),(0x44,actor),(0x48,0x1234)):
        w(this+off,val)
    for idx in range(1,4):
        b=box+idx*0x1000;w(b,vt);w(b+0xc,idx);uc.mem_write(b+0x11,b'\x01')
    w(actor+0x7a0,actor+0x1000);w(actor+0x1000+0x60,requested_rarity);w(actor+0x1000+4,12345)
    w(actor+0x1000+0x68,requested_rarity)
    w(this+0x780,actor)
    for g in (0xB5DCE8,0xB5DCEC,0xBF1638,0xBF163C,0xBF1648,0xB5D6F0):w(g,box)
    w(0xBF0A9C,actor+0x2000);w(0xB75A14,actor+0x3000);w(0xB55184,click)
    w(box+0x10,vt);w(box+0x20,vt);w(box+0x24,vt)
    sp=0x10ff000;w(sp,0x1000000);uc.reg_write(UC_X86_REG_ESP,sp);uc.reg_write(UC_X86_REG_ECX,this)
    if exploratory:
        for idx,value in enumerate((pickup_action,actor,0,0),1):w(sp+4*idx,value)
    if entry in (0xd4fc74,0x8879e9,0xd40d01):
        uc.reg_write(UC_X86_REG_EBP,sp+0x400)
        uc.reg_write(UC_X86_REG_ESI,this)
        w(sp+0x408,actor);w(sp,46);w(sp+0x3f8,requested_rarity)
        if entry==0x8879e9:uc.reg_write(UC_X86_REG_EAX,actor+0x1008)
        if entry==0xd40d01:
            uc.reg_write(UC_X86_REG_ECX,actor)
            w(sp+0x3f8,0xCCCCCCCC)  # source must overwrite this from descriptor
    uc.reg_write(UC_X86_REG_EFLAGS,0x202)
    log=[];trace=[];count=0;begins=[];writes=[];sent=False;current_action=0;opens=[]
    allow={0x7ea940,0x7ea9cf,0x887780,0x7771e8}
    def operand_target(ins,op):
        if op.type==X86_OP_IMM:return op.imm&0xffffffff
        if op.type==X86_OP_REG:return uc.reg_read(REGS[ins.reg_name(op.reg)])
        if op.type==X86_OP_MEM:
            m=op.mem;addr=m.disp
            if m.base:addr+=uc.reg_read(REGS[ins.reg_name(m.base)])
            if m.index:addr+=uc.reg_read(REGS[ins.reg_name(m.index)])*m.scale
            return r(addr&0xffffffff)
        return None
    def hook(uc,at,size,_):
        nonlocal count,sent,current_action
        count+=1
        if count>15000 or at==0x1000000:uc.emu_stop();return
        if stub<=at<stub+0x800:
            esp=uc.reg_read(UC_X86_REG_ESP);off=at-stub
            log.append(f'VIRTUAL-ENTRY +{off:X} ecx={uc.reg_read(UC_X86_REG_ECX):08X} return={r(esp):08X}')
            if exploratory and off==0x38:uc.reg_write(UC_X86_REG_EAX,0x1234)
            elif exploratory and off==0x18:uc.reg_write(UC_X86_REG_EAX,uc.reg_read(UC_X86_REG_ECX)+8)
            elif off not in (0x24,0x28) and not (exploratory and off==0x518):uc.emu_stop();return
            uc.reg_write(UC_X86_REG_EIP,r(esp));uc.reg_write(UC_X86_REG_ESP,esp+4);return
        code=bytes(uc.mem_read(at,min(16,size+8)))
        ins=next(md.disasm(code,at,count=1))
        trace.append(f'{at:08X} esp={uc.reg_read(UC_X86_REG_ESP):08X} ecx={uc.reg_read(UC_X86_REG_ECX):08X} {ins.mnemonic} {ins.op_str}')
        if ins.mnemonic=='call':
            target=operand_target(ins,ins.operands[0])
            if target is None:return
            protected_piece=False
            if 0x401000<=target<0x998000:
                for first in list(md.disasm(bytes(uc.mem_read(target,24)),target))[:4]:
                    if first.mnemonic in ('jmp','call') and first.operands and first.operands[0].type==X86_OP_IMM and 0xd3c000<=first.operands[0].imm<0xd60000:
                        protected_piece=True
            if 0xD3C000<=target<0xD60000 or 0x8C16F8<=target<0x8C1E80 or target in allow or protected_piece:return
            esp=uc.reg_read(UC_X86_REG_ESP);ecx=uc.reg_read(UC_X86_REG_ECX)
            args=[r(esp+4*i) for i in range(6)]
            text=f'CALL {at:08X}->{target:08X} ecx={ecx:08X} args='+','.join(f'{x:08X}' for x in args)
            pop=0;result=1
            if target==0x40d900:result=actor+0x2000
            elif exploratory and target==0x417c60:result=r(ecx+0x7a0)
            elif exploratory and target==0x402030:result=current_action if ecx==this+0x1434 else 0;text+=f' FIXTURE_VALUE_{result}'
            elif exploratory and target==0x48a460:pop=4;current_action=args[0];result=0;text+=' FIXTURE_ACTION_STUB'
            elif target==0x8b2650:pop=4;text+=' BEGIN';begins.append(args[0])
            elif target in (0x40d8a0,0x40d8c0):
                pop=4;n=1 if target==0x40d8a0 else 2;text+=' WRITE'+str(n)
                writes.append(args[0].to_bytes(4,'little')[:n])
            elif target==0x8b2450:
                pop=8
                try:
                    value=bytes(uc.mem_read(args[0],min(args[1],32)))
                    text+=' BYTES='+value.hex();writes.append(value)
                except:text+=' BAD_BUFFER'
            elif target==0x8b24c0:text+=' SEND';sent=True
            elif exploratory and target==0x799ef0:
                opens.append(args[:4]);log.append(text+' OPEN_UI');uc.emu_stop();return
            elif target==0x8f6a00:pop=0
            elif target==0x46e720:result=1
            elif target==0x8eed90:result=0
            elif target==0x900c50:result=random15;text+=f' FIXTURE_RANDOM_{random15:04X}'
            elif exploratory and target==0x45d760:result=1;pop=8;text+=' FIXTURE_MAP_HAS_ACTOR_TRUE'
            elif target==0x776d50:
                word=struct.unpack('<H',uc.mem_read(args[0],2))[0]
                bit=(1<<(args[2]&31))&0xffff
                word=(word|bit) if args[1]&0xff else (word&~bit)
                uc.mem_write(args[0],struct.pack('<H',word))
                result=args[0];text+=f' BITSET={word:04X}'
            elif target==0x799e20:pop=4
            elif target==0x433890:result=0
            elif stub<=target<stub+0x800:
                off=target-stub
                result=ecx+8 if off==0x18 else 0
                if off in (0x24,0x28):pop=0
                elif off==0x38:result=0x1234
                else:log.append(text+' UNKNOWN_VIRTUAL');uc.emu_stop();return
            else:log.append(text+' UNKNOWN_EXTERNAL');uc.emu_stop();return
            log.append(text)
            uc.reg_write(UC_X86_REG_EAX,result);uc.reg_write(UC_X86_REG_ESP,esp+pop);uc.reg_write(UC_X86_REG_EIP,at+ins.size)
            if sent:uc.emu_stop()
    def invalid(uc,access,addr,size,value,_):
        log.append(f'INVALID access={access} address={addr:08X} size={size} eip={uc.reg_read(UC_X86_REG_EIP):08X}');return False
    uc.hook_add(UC_HOOK_CODE,hook);uc.hook_add(UC_HOOK_MEM_INVALID,invalid)
    try:uc.emu_start(entry,0x1000000,count=16000)
    except Exception as exc:log.append('ERROR '+str(exc))
    body=b''.join(writes)
    if exploratory:pass
    elif click in (1,3):
        expected=struct.pack('<HBBH',0x1234,int(click==3),4,(random15|0x12)&~0x20)
        assert begins==[46] and body==expected and sent,(click,begins,body.hex(),sent)
    else:assert not begins and not writes and not sent,(click,begins,body.hex(),sent)
    log.append(f'{"EXPLORATORY" if exploratory else "VERIFIED"} click={click} body={body.hex()} sends={int(sent)}')
    log.append(f'END count={count} eip={uc.reg_read(UC_X86_REG_EIP):08X}')
    (ROOT/f'offline_window_click{click}{suffix}.txt').write_text('\n'.join(log)+'\n\nLAST TRACE\n'+'\n'.join(trace[-120:]),encoding='utf-8')
    (ROOT/f'offline_window_click{click}{suffix}.trace.txt').write_text('\n'.join(trace),encoding='utf-8')
    return {'begin':begins,'body':body,'sent':sent,'opens':opens,'count':count,'log':log}

if __name__=='__main__':
    for click in (0,1,2,3):run(click)
    for val in (0,0x7fff):
        for click in (1,3):run(click,val,f'_rand{val:04X}')
    print('offline confirm-window packet flow: 8 fixtures passed (synthetic CPU context only)')
