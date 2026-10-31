"""Independent oracle: execute original ACT1 bytes with synthetic data only.

No process access. Secure getter 402030 is intercepted to return fixture values;
hash lookup and signed comparison run without an intercepted dependency.
"""
from pathlib import Path
import hashlib, json, struct
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *

PE_PATH=Path('G:/Game/DNF_ACT1/Loot4_A/DNF.exe')
pe=pefile.PE(str(PE_PATH)); BASE=pe.OPTIONAL_HEADER.ImageBase
STOP=0x20000000; DATA=0x10000000; STACK=0x30000000
def put(u,a,*v): u.mem_write(a,struct.pack('<'+'I'*len(v),*[x&0xffffffff for x in v]))
def word(u,a): return struct.unpack('<I',u.mem_read(a,4))[0]
def signed(v): return v if v<0x80000000 else v-0x100000000
def machine():
    u=Uc(UC_ARCH_X86,UC_MODE_32)
    u.mem_map(BASE,pe.OPTIONAL_HEADER.SizeOfImage)
    u.mem_write(BASE,pe.get_memory_mapped_image())
    for a,n in [(DATA,0x10000),(STOP,0x1000),(STACK,0x10000)]:u.mem_map(a,n)
    return u
def call(u,va,this,args=(),secure=None,return_hooks=None):
    sp=STACK+0x8000;put(u,sp,STOP,*args)
    u.reg_write(UC_X86_REG_ESP,sp);u.reg_write(UC_X86_REG_ECX,this)
    def hook(u,a,n,extra):
        if return_hooks and a in return_hooks:
            result,cleanup=return_hooks[a]
            u.reg_write(UC_X86_REG_EAX,result&0xffffffff)
            sp=u.reg_read(UC_X86_REG_ESP)
            u.reg_write(UC_X86_REG_EIP,word(u,sp));u.reg_write(UC_X86_REG_ESP,sp+4+cleanup)
        elif a==0x402030:
            assert secure is not None
            u.reg_write(UC_X86_REG_EAX,secure[u.reg_read(UC_X86_REG_ECX)]&0xffffffff)
            sp=u.reg_read(UC_X86_REG_ESP)
            u.reg_write(UC_X86_REG_EIP,word(u,sp));u.reg_write(UC_X86_REG_ESP,sp+4)
    h=u.hook_add(UC_HOOK_CODE,hook)
    u.emu_start(va,STOP,count=10000);u.hook_del(h)
    return u.reg_read(UC_X86_REG_EAX)

result={'pe_sha256':hashlib.sha256(PE_PATH.read_bytes()).hexdigest(),'interpolation':[],'lookup':[],'signed_limit':[],'first_slot':[],'icon_items':[]}
for first,last,level,maximum in [(2,4,1,10),(4,1,2,3),(2,4,10,10),(9,0,2,4),(0,8,3,5)]:
    u=machine();row=DATA
    native=signed(call(u,0x4A3CF0,row,(level,maximum),{row+8:first,row+0x10:last}))
    delta=(last-first)*(level-1);q=abs(delta)//(maximum-1)*(1 if delta>=0 else -1)
    expected=first+q;assert native==expected
    result['interpolation'].append(dict(first=first,last=last,level=level,maximum=maximum,native=native))

for requested,keys in [(20,[20]),(20,[1,20,30]),(20,[21]),(20,[1,19]),(20,[])]:
    u=machine();m=DATA;head=DATA+0x100;bucket=DATA+0x200;answer=DATA+0x300;keyptr=DATA+0x304
    put(u,m+8,head);put(u,m+0x14,bucket);put(u,m+0x20,0,1)
    nodes=[DATA+0x400+i*0x20 for i in range(len(keys))]
    put(u,bucket,nodes[0] if nodes else head,head);put(u,keyptr,requested)
    for i,(n,key) in enumerate(zip(nodes,keys)):put(u,n,nodes[i+1] if i+1<len(nodes) else head,head,key)
    call(u,0x4D5C80,m,(answer,keyptr))
    actual=word(u,answer);expected=next((n for n,k in zip(nodes,keys) if k==requested),head)
    assert actual==expected
    result['lookup'].append(dict(requested=requested,keys=keys,matched=actual!=head))

for count,limit in [(-1,2),(0,2),(1,2),(2,2),(3,2),(0,0x7fffffff)]:
    u=machine();u.reg_write(UC_X86_REG_EAX,count&0xffffffff);u.reg_write(UC_X86_REG_EDI,limit)
    u.emu_start(0x49C6B5,0x49C6B9,count=2)
    below=u.reg_read(UC_X86_REG_EIP)==0x49C700
    assert below==(count<limit)
    result['signed_limit'].append(dict(count=count,limit=limit,below=below))

for ids,expected in [([1001,1001],0),([2002,1001],1),([2002,3003],-1)]:
    u=machine();inventory=DATA;slots=DATA+0x100;manager=DATA+0x200;mirrorA=DATA+0x300;mirrorB=DATA+0x400;key=0x12345678
    put(u,0xCFD458,manager,key);put(u,manager+0x44,mirrorA,mirrorB)
    put(u,inventory+0x10,(inventory+0x10)^key,0,slots)
    put(u,mirrorA,len(ids)^mirrorA^key);put(u,mirrorB,len(ids)^mirrorB^key)
    for i,itemid in enumerate(ids):
        item=DATA+0x1000+i*0x100;vtable=DATA+0x2000
        put(u,slots+i*4,item);put(u,item,vtable);put(u,item+0xc,itemid);put(u,vtable+0x18,0x4619A0)
    actual=signed(call(u,0x45D9A0,inventory,(1001,)))
    assert actual==expected
    result['first_slot'].append(dict(ids=ids,native_slot=actual))

for first,last,level,maximum,quantities,expected in [(2,4,1,10,[1,100],False),(2,4,1,10,[2,0],True),(4,1,2,3,[2,100],False),(4,1,2,3,[3,0],True),(0,0,1,10,[0,0],True)]:
    u=machine();inventory=DATA;slots=DATA+0x100;manager=DATA+0x200;mirrorA=DATA+0x300;mirrorB=DATA+0x400;key=0x12345678
    actor=DATA+0x3000;skill=DATA+0x5000;row=DATA+0x6000;vtable=DATA+0x2000
    put(u,0xCFD458,manager,key);put(u,manager+0x44,mirrorA,mirrorB)
    put(u,inventory+0x10,(inventory+0x10)^key,0,slots)
    put(u,mirrorA,len(quantities)^mirrorA^key);put(u,mirrorB,len(quantities)^mirrorB^key)
    secure={skill+0x2ec:1,skill+0x2e0:level,skill+0x20:maximum,row:1001,row+8:first,row+0x10:last,actor+0x1170:100,inventory+0x10:len(quantities)}
    for i,quantity in enumerate(quantities):
        item=DATA+0x1000+i*0x100
        put(u,slots+i*4,item);put(u,item,vtable);put(u,item+0xc,1001);put(u,vtable+0x18,0x4619A0);put(u,vtable+0x34,0x461AB0)
        secure[item+0xa0]=quantity
    put(u,actor,vtable+0x500);put(u,vtable+0x500+0x340,0x652A70)
    put(u,0xB75A14,actor);put(u,0xB5DCEC,inventory)
    put(u,skill+0x1bc,row,row+0x18)
    actual=bool(call(u,0x4F4F50,skill,(actor,),secure,{0x4F4D30:(0,8)})&0xff)
    assert actual==expected
    result['icon_items'].append(dict(first=first,last=last,level=level,maximum=maximum,quantities=quantities,native=actual))

print(json.dumps(result,indent=2))
