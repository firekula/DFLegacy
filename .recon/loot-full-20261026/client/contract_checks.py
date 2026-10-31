from offline_window_emu import run, ROOT
import struct

lines=[]
for click in (0,1,2,3):
    r=run(click)
    lines.append(f'window click={click} body={r["body"].hex()} sends={r["sent"]}')
for random15 in (0,0x7fff):
    for click in (1,3):
        r=run(click,random15,f'_rand{random15:04X}')
        lines.append(f'window click={click} random={random15:04X} body={r["body"].hex()} sends={r["sent"]}')
for rarity in range(6):
    r=run(0,suffix=f'_groundPath_rarity{rarity}',entry=0xd40d01,
          exploratory=True,requested_rarity=rarity)
    if rarity==4:
        assert r['opens']==[[98,0x2200000,0,0]] and not r['begin'] and not r['sent'],r
    else:
        assert r['begin']==[46] and r['body']==struct.pack('<HBBH',0x1234,0,rarity,0x0997) and r['sent'] and not r['opens'],r
    lines.append(f'ground descriptor rarity={rarity} body={r["body"].hex()} sends={r["sent"]} opens={r["opens"]}')
lines.append('14 fixtures passed; isolated synthetic CPU contexts, not runtime client/UI acceptance.')
(ROOT/'contract_checks.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('\n'.join(lines))
