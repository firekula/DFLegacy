
base_actor_ctor_complete: 0x00407290, size 281
00407290 55                   push    ebp
00407291 8bec                 mov     ebp, esp
00407293 6aff                 push    -1
00407295 6860a59500           push    0x95a560
0040729A 64a100000000         mov     eax, dword ptr fs:[0]
004072A0 50                   push    eax
004072A1 64892500000000       mov     dword ptr fs:[0], esp
004072A8 51                   push    ecx
004072A9 53                   push    ebx
004072AA 56                   push    esi
004072AB 8bf1                 mov     esi, ecx
004072AD 57                   push    edi
004072AE 8975f0               mov     dword ptr [ebp - 0x10], esi
004072B1 c70650879900         mov     dword ptr [esi], 0x998750
004072B7 e8d4514b00           call    0x8bc490
004072BC 33ff                 xor     edi, edi
004072BE 8d4e08               lea     ecx, [esi + 8]
004072C1 897dfc               mov     dword ptr [ebp - 4], edi
004072C4 c7067c879900         mov     dword ptr [esi], 0x99877c
004072CA e8c1e4ffff           call    0x405790
004072CF 8d4e48               lea     ecx, [esi + 0x48]
004072D2 c645fc01             mov     byte ptr [ebp - 4], 1
004072D6 e8b5e4ffff           call    0x405790
004072DB 8d8e88000000         lea     ecx, [esi + 0x88]
004072E1 c645fc02             mov     byte ptr [ebp - 4], 2
004072E5 e8a6e4ffff           call    0x405790
004072EA 8d8ed0000000         lea     ecx, [esi + 0xd0]
004072F0 c645fc03             mov     byte ptr [ebp - 4], 3
004072F4 e897e1ffff           call    0x405490
004072F9 8d9eec000000         lea     ebx, [esi + 0xec]
004072FF 8bcb                 mov     ecx, ebx
00407301 c645fc04             mov     byte ptr [ebp - 4], 4
00407305 e856d9ffff           call    0x404c60
0040730A 894304               mov     dword ptr [ebx + 4], eax
0040730D 897b08               mov     dword ptr [ebx + 8], edi
00407310 89be00010000         mov     dword ptr [esi + 0x100], edi
00407316 89be04010000         mov     dword ptr [esi + 0x104], edi
0040731C 89be08010000         mov     dword ptr [esi + 0x108], edi
00407322 89be10010000         mov     dword ptr [esi + 0x110], edi
00407328 89be14010000         mov     dword ptr [esi + 0x114], edi
0040732E 89be18010000         mov     dword ptr [esi + 0x118], edi
00407334 8d9e1c010000         lea     ebx, [esi + 0x11c]
0040733A 8bcb                 mov     ecx, ebx
0040733C c645fc07             mov     byte ptr [ebp - 4], 7
00407340 e80b2e4500           call    0x85a150
00407345 894304               mov     dword ptr [ebx + 4], eax
00407348 c6401101             mov     byte ptr [eax + 0x11], 1
0040734C 8b4304               mov     eax, dword ptr [ebx + 4]
0040734F 894004               mov     dword ptr [eax + 4], eax
00407352 8b4304               mov     eax, dword ptr [ebx + 4]
00407355 8900                 mov     dword ptr [eax], eax
00407357 8b4304               mov     eax, dword ptr [ebx + 4]
0040735A 894008               mov     dword ptr [eax + 8], eax
0040735D 897b08               mov     dword ptr [ebx + 8], edi
00407360 89be3c010000         mov     dword ptr [esi + 0x13c], edi
00407366 89be40010000         mov     dword ptr [esi + 0x140], edi
0040736C 89be44010000         mov     dword ptr [esi + 0x144], edi
00407372 89be48010000         mov     dword ptr [esi + 0x148], edi
00407378 89be4c010000         mov     dword ptr [esi + 0x14c], edi
0040737E 89be50010000         mov     dword ptr [esi + 0x150], edi
00407384 8bce                 mov     ecx, esi
00407386 c645fc09             mov     byte ptr [ebp - 4], 9
0040738A e8b1a00600           call    0x471440
0040738F 8bce                 mov     ecx, esi
00407391 e84ab90600           call    0x472ce0
00407396 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
00407399 5f                   pop     edi
0040739A 8bc6                 mov     eax, esi
0040739C 5e                   pop     esi
0040739D 5b                   pop     ebx
0040739E 64890d00000000       mov     dword ptr fs:[0], ecx
004073A5 8be5                 mov     esp, ebp
004073A7 5d                   pop     ebp
004073A8 c3                   ret     

registration_id_ctor_complete: 0x00405490, size 85
00405490 56                   push    esi
00405491 8bf1                 mov     esi, ecx
00405493 57                   push    edi
00405494 8d7e04               lea     edi, [esi + 4]
00405497 8bcf                 mov     ecx, edi
00405499 e8e2f5ffff           call    0x404a80
0040549E c70600000000         mov     dword ptr [esi], 0
004054A4 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
004054AA 8b5144               mov     edx, dword ptr [ecx + 0x44]
004054AD 8b4704               mov     eax, dword ptr [edi + 4]
004054B0 8d0482               lea     eax, [edx + eax*4]
004054B3 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
004054B9 8bc8                 mov     ecx, eax
004054BB 33ca                 xor     ecx, edx
004054BD 8908                 mov     dword ptr [eax], ecx
004054BF a158d4cf00           mov     eax, dword ptr [0xcfd458]
004054C4 8b5704               mov     edx, dword ptr [edi + 4]
004054C7 8b4848               mov     ecx, dword ptr [eax + 0x48]
004054CA 8d0491               lea     eax, [ecx + edx*4]
004054CD 8b0d5cd4cf00         mov     ecx, dword ptr [0xcfd45c]
004054D3 8bd0                 mov     edx, eax
004054D5 33d1                 xor     edx, ecx
004054D7 8910                 mov     dword ptr [eax], edx
004054D9 5f                   pop     edi
004054DA c7460c01000000       mov     dword ptr [esi + 0xc], 1
004054E1 8bc6                 mov     eax, esi
004054E3 5e                   pop     esi
004054E4 c3                   ret     

object_id_and_category_getters: 0x004073B0, size 39
004073B0 8d81d0000000         lea     eax, [ecx + 0xd0]
004073B6 c3                   ret     
004073B7 cc                   int3    
004073B8 cc                   int3    
004073B9 cc                   int3    
004073BA cc                   int3    
004073BB cc                   int3    
004073BC cc                   int3    
004073BD cc                   int3    
004073BE cc                   int3    
004073BF cc                   int3    
004073C0 8b81cc000000         mov     eax, dword ptr [ecx + 0xcc]
004073C6 c3                   ret     
004073C7 cc                   int3    
004073C8 cc                   int3    
004073C9 cc                   int3    
004073CA cc                   int3    
004073CB cc                   int3    
004073CC cc                   int3    
004073CD cc                   int3    
004073CE cc                   int3    
004073CF cc                   int3    
004073D0 8b81d0000000         mov     eax, dword ptr [ecx + 0xd0]
004073D6 c3                   ret     

category_bits_predicate_complete: 0x00471550, size 23
00471550 55                   push    ebp
00471551 8bec                 mov     ebp, esp
00471553 8b4508               mov     eax, dword ptr [ebp + 8]
00471556 8b89cc000000         mov     ecx, dword ptr [ecx + 0xcc]
0047155C 23c8                 and     ecx, eax
0047155E 3bc8                 cmp     ecx, eax
00471560 0f94c0               sete    al
00471563 5d                   pop     ebp
00471564 c20400               ret     4

base_living_actor_ctor_prefix: 0x0042C830, size 56
0042C830 55                   push    ebp
0042C831 8bec                 mov     ebp, esp
0042C833 6aff                 push    -1
0042C835 68f4b19500           push    0x95b1f4
0042C83A 64a100000000         mov     eax, dword ptr fs:[0]
0042C840 50                   push    eax
0042C841 64892500000000       mov     dword ptr fs:[0], esp
0042C848 83ec08               sub     esp, 8
0042C84B 53                   push    ebx
0042C84C 56                   push    esi
0042C84D 8bf1                 mov     esi, ecx
0042C84F 8975ec               mov     dword ptr [ebp - 0x14], esi
0042C852 e839aafdff           call    0x407290
0042C857 33db                 xor     ebx, ebx
0042C859 8d8ea0010000         lea     ecx, [esi + 0x1a0]
0042C85F 895dfc               mov     dword ptr [ebp - 4], ebx
0042C862 c7069c8f9900         mov     dword ptr [esi], 0x998f9c

stat_actor_ctor_prefix: 0x00652170, size 75
00652170 55                   push    ebp
00652171 8bec                 mov     ebp, esp
00652173 6aff                 push    -1
00652175 68425e9700           push    0x975e42
0065217A 64a100000000         mov     eax, dword ptr fs:[0]
00652180 50                   push    eax
00652181 64892500000000       mov     dword ptr fs:[0], esp
00652188 83ec0c               sub     esp, 0xc
0065218B 53                   push    ebx
0065218C 56                   push    esi
0065218D 8bf1                 mov     esi, ecx
0065218F 57                   push    edi
00652190 8975ec               mov     dword ptr [ebp - 0x14], esi
00652193 e898a6ddff           call    0x42c830
00652198 68c0e96400           push    0x64e9c0
0065219D 68700a6500           push    0x650a70
006521A2 6a04                 push    4
006521A4 6838030000           push    0x338
006521A9 8d8688040000         lea     eax, [esi + 0x488]
006521AF 33db                 xor     ebx, ebx
006521B1 50                   push    eax
006521B2 895dfc               mov     dword ptr [ebp - 4], ebx
006521B5 c706342ea000         mov     dword ptr [esi], 0xa02e34

IRDCharacter_ctor_complete: 0x00653290, size 677
00653290 55                   push    ebp
00653291 8bec                 mov     ebp, esp
00653293 6aff                 push    -1
00653295 6838649700           push    0x976438
0065329A 64a100000000         mov     eax, dword ptr fs:[0]
006532A0 50                   push    eax
006532A1 64892500000000       mov     dword ptr fs:[0], esp
006532A8 51                   push    ecx
006532A9 53                   push    ebx
006532AA 56                   push    esi
006532AB 8bf1                 mov     esi, ecx
006532AD 57                   push    edi
006532AE 8975f0               mov     dword ptr [ebp - 0x10], esi
006532B1 e8baeeffff           call    0x652170
006532B6 33db                 xor     ebx, ebx
006532B8 8d8e98180000         lea     ecx, [esi + 0x1898]
006532BE 895dfc               mov     dword ptr [ebp - 4], ebx
006532C1 c706bc32a000         mov     dword ptr [esi], 0xa032bc
006532C7 e864e9ffff           call    0x651c30
006532CC 899e0c190000         mov     dword ptr [esi + 0x190c], ebx
006532D2 8d8620190000         lea     eax, [esi + 0x1920]
006532D8 b904000000           mov     ecx, 4
006532DD 8d4900               lea     ecx, [ecx]
006532E0 8958f0               mov     dword ptr [eax - 0x10], ebx
006532E3 8918                 mov     dword ptr [eax], ebx
006532E5 83c004               add     eax, 4
006532E8 49                   dec     ecx
006532E9 75f5                 jne     0x6532e0
006532EB 899e30190000         mov     dword ptr [esi + 0x1930], ebx
006532F1 899e34190000         mov     dword ptr [esi + 0x1934], ebx
006532F7 899e38190000         mov     dword ptr [esi + 0x1938], ebx
006532FD 899e3c190000         mov     dword ptr [esi + 0x193c], ebx
00653303 8d8e40190000         lea     ecx, [esi + 0x1940]
00653309 c645fc02             mov     byte ptr [ebp - 4], 2
0065330D e89e8eddff           call    0x42c1b0
00653312 8d450b               lea     eax, [ebp + 0xb]
00653315 50                   push    eax
00653316 8d550b               lea     edx, [ebp + 0xb]
00653319 8d8e4c1e0000         lea     ecx, [esi + 0x1e4c]
0065331F 52                   push    edx
00653320 c645fc03             mov     byte ptr [ebp - 4], 3
00653324 e85779e1ff           call    0x46ac80
00653329 8d450b               lea     eax, [ebp + 0xb]
0065332C 50                   push    eax
0065332D 8d550b               lea     edx, [ebp + 0xb]
00653330 8d8e741e0000         lea     ecx, [esi + 0x1e74]
00653336 52                   push    edx
00653337 c645fc04             mov     byte ptr [ebp - 4], 4
0065333B e8b0edffff           call    0x6520f0
00653340 8d8ea01e0000         lea     ecx, [esi + 0x1ea0]
00653346 c645fc05             mov     byte ptr [ebp - 4], 5
0065334A e8a1e2ffff           call    0x6515f0
0065334F 899e30200000         mov     dword ptr [esi + 0x2030], ebx
00653355 899e34200000         mov     dword ptr [esi + 0x2034], ebx
0065335B 899e38200000         mov     dword ptr [esi + 0x2038], ebx
00653361 899e40200000         mov     dword ptr [esi + 0x2040], ebx
00653367 899e44200000         mov     dword ptr [esi + 0x2044], ebx
0065336D 899e48200000         mov     dword ptr [esi + 0x2048], ebx
00653373 899e50200000         mov     dword ptr [esi + 0x2050], ebx
00653379 899e54200000         mov     dword ptr [esi + 0x2054], ebx
0065337F 899e58200000         mov     dword ptr [esi + 0x2058], ebx
00653385 8dbe5c200000         lea     edi, [esi + 0x205c]
0065338B 8bcf                 mov     ecx, edi
0065338D c645fc09             mov     byte ptr [ebp - 4], 9
00653391 e89a7f2500           call    0x8ab330
00653396 8bcf                 mov     ecx, edi
00653398 e893802500           call    0x8ab430
0065339D bf0f000000           mov     edi, 0xf
006533A2 89be98200000         mov     dword ptr [esi + 0x2098], edi
006533A8 899e94200000         mov     dword ptr [esi + 0x2094], ebx
006533AE 889e84200000         mov     byte ptr [esi + 0x2084], bl
006533B4 89bebc200000         mov     dword ptr [esi + 0x20bc], edi
006533BA 899eb8200000         mov     dword ptr [esi + 0x20b8], ebx
006533C0 889ea8200000         mov     byte ptr [esi + 0x20a8], bl
006533C6 89bed8200000         mov     dword ptr [esi + 0x20d8], edi
006533CC 899ed4200000         mov     dword ptr [esi + 0x20d4], ebx
006533D2 889ec4200000         mov     byte ptr [esi + 0x20c4], bl
006533D8 8d8e14210000         lea     ecx, [esi + 0x2114]
006533DE c645fc0d             mov     byte ptr [ebp - 4], 0xd
006533E2 e89916dbff           call    0x404a80
006533E7 8d8e20210000         lea     ecx, [esi + 0x2120]
006533ED c645fc0e             mov     byte ptr [ebp - 4], 0xe
006533F1 e88a16dbff           call    0x404a80
006533F6 8d8e28210000         lea     ecx, [esi + 0x2128]
006533FC c645fc0f             mov     byte ptr [ebp - 4], 0xf
00653400 e87b16dbff           call    0x404a80
00653405 8d8e30210000         lea     ecx, [esi + 0x2130]
0065340B c645fc10             mov     byte ptr [ebp - 4], 0x10
0065340F e86c16dbff           call    0x404a80
00653414 8d8e44210000         lea     ecx, [esi + 0x2144]
0065341A c645fc11             mov     byte ptr [ebp - 4], 0x11
0065341E e85d16dbff           call    0x404a80
00653423 89be88210000         mov     dword ptr [esi + 0x2188], edi
00653429 899e84210000         mov     dword ptr [esi + 0x2184], ebx
0065342F 889e74210000         mov     byte ptr [esi + 0x2174], bl
00653435 899e98210000         mov     dword ptr [esi + 0x2198], ebx
0065343B 899e9c210000         mov     dword ptr [esi + 0x219c], ebx
00653441 899ea0210000         mov     dword ptr [esi + 0x21a0], ebx
00653447 89bed4210000         mov     dword ptr [esi + 0x21d4], edi
0065344D 899ed0210000         mov     dword ptr [esi + 0x21d0], ebx
00653453 889ec0210000         mov     byte ptr [esi + 0x21c0], bl
00653459 8d8efc210000         lea     ecx, [esi + 0x21fc]
0065345F c645fc15             mov     byte ptr [ebp - 4], 0x15
00653463 e82823dbff           call    0x405790
00653468 8d8e44220000         lea     ecx, [esi + 0x2244]
0065346E c645fc16             mov     byte ptr [ebp - 4], 0x16
00653472 e869e8ffff           call    0x651ce0
00653477 8d8e98220000         lea     ecx, [esi + 0x2298]
0065347D c645fc17             mov     byte ptr [ebp - 4], 0x17
00653481 e80a23dbff           call    0x405790
00653486 899edc220000         mov     dword ptr [esi + 0x22dc], ebx
0065348C 899ee0220000         mov     dword ptr [esi + 0x22e0], ebx
00653492 899ee4220000         mov     dword ptr [esi + 0x22e4], ebx
00653498 899e3c230000         mov     dword ptr [esi + 0x233c], ebx
0065349E 899e40230000         mov     dword ptr [esi + 0x2340], ebx
006534A4 899e44230000         mov     dword ptr [esi + 0x2344], ebx
006534AA 899e48230000         mov     dword ptr [esi + 0x2348], ebx
006534B0 8d8e54230000         lea     ecx, [esi + 0x2354]
006534B6 c645fc1a             mov     byte ptr [ebp - 4], 0x1a
006534BA e8c115dbff           call    0x404a80
006534BF 8d8e5c230000         lea     ecx, [esi + 0x235c]
006534C5 c645fc1b             mov     byte ptr [ebp - 4], 0x1b
006534C9 e8b215dbff           call    0x404a80
006534CE 8d8e74230000         lea     ecx, [esi + 0x2374]
006534D4 c645fc1c             mov     byte ptr [ebp - 4], 0x1c
006534D8 e8a315dbff           call    0x404a80
006534DD 8d8e8c230000         lea     ecx, [esi + 0x238c]
006534E3 c645fc1d             mov     byte ptr [ebp - 4], 0x1d
006534E7 e8a422dbff           call    0x405790
006534EC 8d8edc230000         lea     ecx, [esi + 0x23dc]
006534F2 c645fc1e             mov     byte ptr [ebp - 4], 0x1e
006534F6 e89522dbff           call    0x405790
006534FB 8d8e20240000         lea     ecx, [esi + 0x2420]
00653501 c645fc1f             mov     byte ptr [ebp - 4], 0x1f
00653505 e87615dbff           call    0x404a80
0065350A 8bce                 mov     ecx, esi
0065350C c645fc20             mov     byte ptr [ebp - 4], 0x20
00653510 e84b44e4ff           call    0x497960
00653515 8b4508               mov     eax, dword ptr [ebp + 8]
00653518 50                   push    eax
00653519 8bce                 mov     ecx, esi
0065351B e8a0ffe4ff           call    0x4a34c0
00653520 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
00653523 5f                   pop     edi
00653524 8bc6                 mov     eax, esi
00653526 5e                   pop     esi
00653527 5b                   pop     ebx
00653528 64890d00000000       mov     dword ptr fs:[0], ecx
0065352F 8be5                 mov     esp, ebp
00653531 5d                   pop     ebp
00653532 c20400               ret     4

player_init_complete: 0x00497960, size 1433
00497960 53                   push    ebx
00497961 56                   push    esi
00497962 8bf1                 mov     esi, ecx
00497964 8b06                 mov     eax, dword ptr [esi]
00497966 57                   push    edi
00497967 ff5040               call    dword ptr [eax + 0x40]
0049796A 8bc8                 mov     ecx, eax
0049796C e86f2d0300           call    0x4ca6e0
00497971 c786cc00000011010000 mov     dword ptr [esi + 0xcc], 0x111
0049797B 33c9                 xor     ecx, ecx
0049797D 33db                 xor     ebx, ebx
0049797F 899e24040000         mov     dword ptr [esi + 0x424], ebx
00497985 83c8ff               or      eax, 0xffffffff
00497988 8986c81f0000         mov     dword ptr [esi + 0x1fc8], eax
0049798E 898ecc1f0000         mov     dword ptr [esi + 0x1fcc], ecx
00497994 898ed01f0000         mov     dword ptr [esi + 0x1fd0], ecx
0049799A 898ed41f0000         mov     dword ptr [esi + 0x1fd4], ecx
004979A0 898ed81f0000         mov     dword ptr [esi + 0x1fd8], ecx
004979A6 898edc1f0000         mov     dword ptr [esi + 0x1fdc], ecx
004979AC 898ee01f0000         mov     dword ptr [esi + 0x1fe0], ecx
004979B2 898ee41f0000         mov     dword ptr [esi + 0x1fe4], ecx
004979B8 898ee81f0000         mov     dword ptr [esi + 0x1fe8], ecx
004979BE 898eec1f0000         mov     dword ptr [esi + 0x1fec], ecx
004979C4 898ef01f0000         mov     dword ptr [esi + 0x1ff0], ecx
004979CA 898ef41f0000         mov     dword ptr [esi + 0x1ff4], ecx
004979D0 898ef81f0000         mov     dword ptr [esi + 0x1ff8], ecx
004979D6 898efc1f0000         mov     dword ptr [esi + 0x1ffc], ecx
004979DC 898e00200000         mov     dword ptr [esi + 0x2000], ecx
004979E2 898e04200000         mov     dword ptr [esi + 0x2004], ecx
004979E8 898e08200000         mov     dword ptr [esi + 0x2008], ecx
004979EE 898e0c200000         mov     dword ptr [esi + 0x200c], ecx
004979F4 898e10200000         mov     dword ptr [esi + 0x2010], ecx
004979FA 898e14200000         mov     dword ptr [esi + 0x2014], ecx
00497A00 898e18200000         mov     dword ptr [esi + 0x2018], ecx
00497A06 898e1c200000         mov     dword ptr [esi + 0x201c], ecx
00497A0C 898e20200000         mov     dword ptr [esi + 0x2020], ecx
00497A12 898e24200000         mov     dword ptr [esi + 0x2024], ecx
00497A18 899e28200000         mov     dword ptr [esi + 0x2028], ebx
00497A1E 899e70200000         mov     dword ptr [esi + 0x2070], ebx
00497A24 899e74200000         mov     dword ptr [esi + 0x2074], ebx
00497A2A 899e78200000         mov     dword ptr [esi + 0x2078], ebx
00497A30 899e7c200000         mov     dword ptr [esi + 0x207c], ebx
00497A36 889ea0200000         mov     byte ptr [esi + 0x20a0], bl
00497A3C 889ea2200000         mov     byte ptr [esi + 0x20a2], bl
00497A42 c786dc20000004000000 mov     dword ptr [esi + 0x20dc], 4
00497A4C 889ee0200000         mov     byte ptr [esi + 0x20e0], bl
00497A52 889ef0200000         mov     byte ptr [esi + 0x20f0], bl
00497A58 b101                 mov     cl, 1
00497A5A 888ea3200000         mov     byte ptr [esi + 0x20a3], cl
00497A60 899ee4200000         mov     dword ptr [esi + 0x20e4], ebx
00497A66 899ee8200000         mov     dword ptr [esi + 0x20e8], ebx
00497A6C 899eec200000         mov     dword ptr [esi + 0x20ec], ebx
00497A72 899ef4200000         mov     dword ptr [esi + 0x20f4], ebx
00497A78 899ef8200000         mov     dword ptr [esi + 0x20f8], ebx
00497A7E 89860c210000         mov     dword ptr [esi + 0x210c], eax
00497A84 889efc200000         mov     byte ptr [esi + 0x20fc], bl
00497A8A 899e00210000         mov     dword ptr [esi + 0x2100], ebx
00497A90 899e04210000         mov     dword ptr [esi + 0x2104], ebx
00497A96 899e08210000         mov     dword ptr [esi + 0x2108], ebx
00497A9C 889e10210000         mov     byte ptr [esi + 0x2110], bl
00497AA2 a158d4cf00           mov     eax, dword ptr [0xcfd458]
00497AA7 8b4044               mov     eax, dword ptr [eax + 0x44]
00497AAA 8b9618210000         mov     edx, dword ptr [esi + 0x2118]
00497AB0 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497AB6 8d0490               lea     eax, [eax + edx*4]
00497AB9 8bd0                 mov     edx, eax
00497ABB 33d7                 xor     edx, edi
00497ABD 8910                 mov     dword ptr [eax], edx
00497ABF 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497AC5 8b5248               mov     edx, dword ptr [edx + 0x48]
00497AC8 8b8618210000         mov     eax, dword ptr [esi + 0x2118]
00497ACE 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497AD4 8d0482               lea     eax, [edx + eax*4]
00497AD7 8bd0                 mov     edx, eax
00497AD9 33d7                 xor     edx, edi
00497ADB 8910                 mov     dword ptr [eax], edx
00497ADD 899e1c210000         mov     dword ptr [esi + 0x211c], ebx
00497AE3 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497AE9 8b8624210000         mov     eax, dword ptr [esi + 0x2124]
00497AEF 8b5244               mov     edx, dword ptr [edx + 0x44]
00497AF2 8d0482               lea     eax, [edx + eax*4]
00497AF5 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497AFB 8bd0                 mov     edx, eax
00497AFD 33d7                 xor     edx, edi
00497AFF 8910                 mov     dword ptr [eax], edx
00497B01 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497B07 8b5248               mov     edx, dword ptr [edx + 0x48]
00497B0A 8b8624210000         mov     eax, dword ptr [esi + 0x2124]
00497B10 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497B16 8d0482               lea     eax, [edx + eax*4]
00497B19 8bd0                 mov     edx, eax
00497B1B 33d7                 xor     edx, edi
00497B1D 8910                 mov     dword ptr [eax], edx
00497B1F 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497B25 8b5244               mov     edx, dword ptr [edx + 0x44]
00497B28 8b862c210000         mov     eax, dword ptr [esi + 0x212c]
00497B2E 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497B34 8d0482               lea     eax, [edx + eax*4]
00497B37 8bd0                 mov     edx, eax
00497B39 33d7                 xor     edx, edi
00497B3B 8910                 mov     dword ptr [eax], edx
00497B3D 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497B43 8b5248               mov     edx, dword ptr [edx + 0x48]
00497B46 8b862c210000         mov     eax, dword ptr [esi + 0x212c]
00497B4C 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497B52 8d0482               lea     eax, [edx + eax*4]
00497B55 8bd0                 mov     edx, eax
00497B57 33d7                 xor     edx, edi
00497B59 8910                 mov     dword ptr [eax], edx
00497B5B 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497B61 8b5244               mov     edx, dword ptr [edx + 0x44]
00497B64 8b8634210000         mov     eax, dword ptr [esi + 0x2134]
00497B6A 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497B70 8d0482               lea     eax, [edx + eax*4]
00497B73 8bd0                 mov     edx, eax
00497B75 33d7                 xor     edx, edi
00497B77 8910                 mov     dword ptr [eax], edx
00497B79 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497B7F 8b5248               mov     edx, dword ptr [edx + 0x48]
00497B82 8b8634210000         mov     eax, dword ptr [esi + 0x2134]
00497B88 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497B8E 8d0482               lea     eax, [edx + eax*4]
00497B91 8bd0                 mov     edx, eax
00497B93 33d7                 xor     edx, edi
00497B95 8910                 mov     dword ptr [eax], edx
00497B97 899e38210000         mov     dword ptr [esi + 0x2138], ebx
00497B9D 899e3c210000         mov     dword ptr [esi + 0x213c], ebx
00497BA3 c78640210000e8030000 mov     dword ptr [esi + 0x2140], 0x3e8
00497BAD 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497BB3 8b5244               mov     edx, dword ptr [edx + 0x44]
00497BB6 8b8648210000         mov     eax, dword ptr [esi + 0x2148]
00497BBC 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497BC2 8d0482               lea     eax, [edx + eax*4]
00497BC5 8bd0                 mov     edx, eax
00497BC7 f7d2                 not     edx
00497BC9 33d7                 xor     edx, edi
00497BCB 8910                 mov     dword ptr [eax], edx
00497BCD 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497BD3 8b5248               mov     edx, dword ptr [edx + 0x48]
00497BD6 8b8648210000         mov     eax, dword ptr [esi + 0x2148]
00497BDC 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497BE2 8d0482               lea     eax, [edx + eax*4]
00497BE5 8bd0                 mov     edx, eax
00497BE7 f7d2                 not     edx
00497BE9 33d7                 xor     edx, edi
00497BEB 8910                 mov     dword ptr [eax], edx
00497BED 899e4c210000         mov     dword ptr [esi + 0x214c], ebx
00497BF3 899e50210000         mov     dword ptr [esi + 0x2150], ebx
00497BF9 899e54210000         mov     dword ptr [esi + 0x2154], ebx
00497BFF 899e58210000         mov     dword ptr [esi + 0x2158], ebx
00497C05 899e5c210000         mov     dword ptr [esi + 0x215c], ebx
00497C0B 899e90210000         mov     dword ptr [esi + 0x2190], ebx
00497C11 899ea4210000         mov     dword ptr [esi + 0x21a4], ebx
00497C17 899ea8210000         mov     dword ptr [esi + 0x21a8], ebx
00497C1D 899eac210000         mov     dword ptr [esi + 0x21ac], ebx
00497C23 899eb0210000         mov     dword ptr [esi + 0x21b0], ebx
00497C29 888eb4210000         mov     byte ptr [esi + 0x21b4], cl
00497C2F 889e60210000         mov     byte ptr [esi + 0x2160], bl
00497C35 899e64210000         mov     dword ptr [esi + 0x2164], ebx
00497C3B 899e68210000         mov     dword ptr [esi + 0x2168], ebx
00497C41 899e6c210000         mov     dword ptr [esi + 0x216c], ebx
00497C47 899e8c210000         mov     dword ptr [esi + 0x218c], ebx
00497C4D 899eb8210000         mov     dword ptr [esi + 0x21b8], ebx
00497C53 889ed8210000         mov     byte ptr [esi + 0x21d8], bl
00497C59 899edc210000         mov     dword ptr [esi + 0x21dc], ebx
00497C5F 899ee0210000         mov     dword ptr [esi + 0x21e0], ebx
00497C65 899ee4210000         mov     dword ptr [esi + 0x21e4], ebx
00497C6B 899ee8210000         mov     dword ptr [esi + 0x21e8], ebx
00497C71 899eec210000         mov     dword ptr [esi + 0x21ec], ebx
00497C77 899ef0210000         mov     dword ptr [esi + 0x21f0], ebx
00497C7D 899ef4210000         mov     dword ptr [esi + 0x21f4], ebx
00497C83 889ef8210000         mov     byte ptr [esi + 0x21f8], bl
00497C89 899e3c220000         mov     dword ptr [esi + 0x223c], ebx
00497C8F 899e40220000         mov     dword ptr [esi + 0x2240], ebx
00497C95 899e9c200000         mov     dword ptr [esi + 0x209c], ebx
00497C9B 889ea1200000         mov     byte ptr [esi + 0x20a1], bl
00497CA1 889e70220000         mov     byte ptr [esi + 0x2270], bl
00497CA7 899e80220000         mov     dword ptr [esi + 0x2280], ebx
00497CAD 899e84220000         mov     dword ptr [esi + 0x2284], ebx
00497CB3 899e8c220000         mov     dword ptr [esi + 0x228c], ebx
00497CB9 899e88220000         mov     dword ptr [esi + 0x2288], ebx
00497CBF 899e90220000         mov     dword ptr [esi + 0x2290], ebx
00497CC5 899e94220000         mov     dword ptr [esi + 0x2294], ebx
00497CCB 8d8610230000         lea     eax, [esi + 0x2310]
00497CD1 ba05000000           mov     edx, 5
00497CD6 b902000000           mov     ecx, 2
00497CDB eb03                 jmp     0x497ce0
00497CDD 8d4900               lea     ecx, [ecx]
00497CE0 8958d8               mov     dword ptr [eax - 0x28], ebx
00497CE3 8918                 mov     dword ptr [eax], ebx
00497CE5 83c004               add     eax, 4
00497CE8 49                   dec     ecx
00497CE9 75f5                 jne     0x497ce0
00497CEB 4a                   dec     edx
00497CEC 75e8                 jne     0x497cd6
00497CEE 899e4c230000         mov     dword ptr [esi + 0x234c], ebx
00497CF4 899e9c1e0000         mov     dword ptr [esi + 0x1e9c], ebx
00497CFA 899e50230000         mov     dword ptr [esi + 0x2350], ebx
00497D00 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497D06 8b5244               mov     edx, dword ptr [edx + 0x44]
00497D09 8b8e58230000         mov     ecx, dword ptr [esi + 0x2358]
00497D0F 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497D15 8d0c8a               lea     ecx, [edx + ecx*4]
00497D18 8bd1                 mov     edx, ecx
00497D1A 33d7                 xor     edx, edi
00497D1C 8911                 mov     dword ptr [ecx], edx
00497D1E 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497D24 8b5248               mov     edx, dword ptr [edx + 0x48]
00497D27 8b8e58230000         mov     ecx, dword ptr [esi + 0x2358]
00497D2D 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497D33 8d0c8a               lea     ecx, [edx + ecx*4]
00497D36 8d8654230000         lea     eax, [esi + 0x2354]
00497D3C 8bd1                 mov     edx, ecx
00497D3E 33d7                 xor     edx, edi
00497D40 8911                 mov     dword ptr [ecx], edx
00497D42 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497D48 8b5244               mov     edx, dword ptr [edx + 0x44]
00497D4B 8b8e60230000         mov     ecx, dword ptr [esi + 0x2360]
00497D51 8d0c8a               lea     ecx, [edx + ecx*4]
00497D54 8bd1                 mov     edx, ecx
00497D56 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
00497D5C 8dbe5c230000         lea     edi, [esi + 0x235c]
00497D62 8911                 mov     dword ptr [ecx], edx
00497D64 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497D6A 8b5248               mov     edx, dword ptr [edx + 0x48]
00497D6D 8b4f04               mov     ecx, dword ptr [edi + 4]
00497D70 8d0c8a               lea     ecx, [edx + ecx*4]
00497D73 8bd1                 mov     edx, ecx
00497D75 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
00497D7B 688e0d0000           push    0xd8e
00497D80 8911                 mov     dword ptr [ecx], edx
00497D82 33c9                 xor     ecx, ecx
00497D84 8d9664230000         lea     edx, [esi + 0x2364]
00497D8A 890a                 mov     dword ptr [edx], ecx
00497D8C 894a04               mov     dword ptr [edx + 4], ecx
00497D8F 894a08               mov     dword ptr [edx + 8], ecx
00497D92 894a0c               mov     dword ptr [edx + 0xc], ecx
00497D95 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497D9B 8b5244               mov     edx, dword ptr [edx + 0x44]
00497D9E 8b8e78230000         mov     ecx, dword ptr [esi + 0x2378]
00497DA4 8d0c8a               lea     ecx, [edx + ecx*4]
00497DA7 8bd1                 mov     edx, ecx
00497DA9 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
00497DAF 50                   push    eax
00497DB0 8911                 mov     dword ptr [ecx], edx
00497DB2 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
00497DB8 8b5248               mov     edx, dword ptr [edx + 0x48]
00497DBB 8b8e78230000         mov     ecx, dword ptr [esi + 0x2378]
00497DC1 8d0c8a               lea     ecx, [edx + ecx*4]
00497DC4 8bd1                 mov     edx, ecx
00497DC6 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
00497DCC 8911                 mov     dword ptr [ecx], edx
00497DCE 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
00497DD4 e8e7954600           call    0x9013c0
00497DD9 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
00497DDF 688f0d0000           push    0xd8f
00497DE4 57                   push    edi
00497DE5 e8d6954600           call    0x9013c0
00497DEA 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
00497DF0 68900d0000           push    0xd90
00497DF5 8d8674230000         lea     eax, [esi + 0x2374]
00497DFB 50                   push    eax
00497DFC e8bf954600           call    0x9013c0
00497E01 899e84230000         mov     dword ptr [esi + 0x2384], ebx
00497E07 899e88230000         mov     dword ptr [esi + 0x2388], ebx
00497E0D 899ecc230000         mov     dword ptr [esi + 0x23cc], ebx
00497E13 899ed0230000         mov     dword ptr [esi + 0x23d0], ebx
00497E19 899ed4230000         mov     dword ptr [esi + 0x23d4], ebx
00497E1F 33c0                 xor     eax, eax
00497E21 b9d8000000           mov     ecx, 0xd8
00497E26 8dbeec1a0000         lea     edi, [esi + 0x1aec]
00497E2C f3ab                 rep stosd dword ptr es:[edi], eax
00497E2E 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
00497E34 8b86f8230000         mov     eax, dword ptr [esi + 0x23f8]
00497E3A 8b5144               mov     edx, dword ptr [ecx + 0x44]
00497E3D 8d0482               lea     eax, [edx + eax*4]
00497E40 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
00497E46 8bc8                 mov     ecx, eax
00497E48 33ca                 xor     ecx, edx
00497E4A 8908                 mov     dword ptr [eax], ecx
00497E4C a158d4cf00           mov     eax, dword ptr [0xcfd458]
00497E51 8b4848               mov     ecx, dword ptr [eax + 0x48]
00497E54 8b96f8230000         mov     edx, dword ptr [esi + 0x23f8]
00497E5A 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497E60 8d0491               lea     eax, [ecx + edx*4]
00497E63 8bd0                 mov     edx, eax
00497E65 33d7                 xor     edx, edi
00497E67 8910                 mov     dword ptr [eax], edx
00497E69 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
00497E6F 8b5144               mov     edx, dword ptr [ecx + 0x44]
00497E72 8b8600240000         mov     eax, dword ptr [esi + 0x2400]
00497E78 8d0482               lea     eax, [edx + eax*4]
00497E7B 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
00497E81 8bc8                 mov     ecx, eax
00497E83 33ca                 xor     ecx, edx
00497E85 8908                 mov     dword ptr [eax], ecx
00497E87 a158d4cf00           mov     eax, dword ptr [0xcfd458]
00497E8C 8b4848               mov     ecx, dword ptr [eax + 0x48]
00497E8F 8b9600240000         mov     edx, dword ptr [esi + 0x2400]
00497E95 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
00497E9B 8d0491               lea     eax, [ecx + edx*4]
00497E9E 8bd0                 mov     edx, eax
00497EA0 33d7                 xor     edx, edi
00497EA2 8910                 mov     dword ptr [eax], edx
00497EA4 889e1c240000         mov     byte ptr [esi + 0x241c], bl
00497EAA 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
00497EB0 8b5144               mov     edx, dword ptr [ecx + 0x44]
00497EB3 8b8624240000         mov     eax, dword ptr [esi + 0x2424]
00497EB9 8d0482               lea     eax, [edx + eax*4]
00497EBC 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
00497EC2 8bc8                 mov     ecx, eax
00497EC4 33ca                 xor     ecx, edx
00497EC6 8908                 mov     dword ptr [eax], ecx
00497EC8 a158d4cf00           mov     eax, dword ptr [0xcfd458]
00497ECD 8b9624240000         mov     edx, dword ptr [esi + 0x2424]
00497ED3 8b4848               mov     ecx, dword ptr [eax + 0x48]
00497ED6 8d0491               lea     eax, [ecx + edx*4]
00497ED9 8b0d5cd4cf00         mov     ecx, dword ptr [0xcfd45c]
00497EDF 8bd0                 mov     edx, eax
00497EE1 33d1                 xor     edx, ecx
00497EE3 8910                 mov     dword ptr [eax], edx
00497EE5 5f                   pop     edi
00497EE6 899e2c240000         mov     dword ptr [esi + 0x242c], ebx
00497EEC c7862824000005000000 mov     dword ptr [esi + 0x2428], 5
00497EF6 5e                   pop     esi
00497EF7 5b                   pop     ebx
00497EF8 c3                   ret     

swordman_ctor_complete: 0x00653C50, size 615
00653C50 55                   push    ebp
00653C51 8bec                 mov     ebp, esp
00653C53 6aff                 push    -1
00653C55 68e0659700           push    0x9765e0
00653C5A 64a100000000         mov     eax, dword ptr fs:[0]
00653C60 50                   push    eax
00653C61 64892500000000       mov     dword ptr fs:[0], esp
00653C68 83ec08               sub     esp, 8
00653C6B 53                   push    ebx
00653C6C 56                   push    esi
00653C6D 57                   push    edi
00653C6E 8bf1                 mov     esi, ecx
00653C70 33ff                 xor     edi, edi
00653C72 57                   push    edi
00653C73 8975ec               mov     dword ptr [ebp - 0x14], esi
00653C76 e815f6ffff           call    0x653290
00653C7B 8d8e30240000         lea     ecx, [esi + 0x2430]
00653C81 897dfc               mov     dword ptr [ebp - 4], edi
00653C84 c706dc38a000         mov     dword ptr [esi], 0xa038dc
00653C8A e8f10ddbff           call    0x404a80
00653C8F 89be4c240000         mov     dword ptr [esi + 0x244c], edi
00653C95 89be50240000         mov     dword ptr [esi + 0x2450], edi
00653C9B 89be54240000         mov     dword ptr [esi + 0x2454], edi
00653CA1 8d8ec0240000         lea     ecx, [esi + 0x24c0]
00653CA7 c645fc02             mov     byte ptr [ebp - 4], 2
00653CAB e8d00ddbff           call    0x404a80
00653CB0 8d45f3               lea     eax, [ebp - 0xd]
00653CB3 50                   push    eax
00653CB4 8d55f3               lea     edx, [ebp - 0xd]
00653CB7 8d8e00250000         lea     ecx, [esi + 0x2500]
00653CBD 52                   push    edx
00653CBE c645fc03             mov     byte ptr [ebp - 4], 3
00653CC2 e8191cf0ff           call    0x5558e0
00653CC7 8d8e44250000         lea     ecx, [esi + 0x2544]
00653CCD c645fc04             mov     byte ptr [ebp - 4], 4
00653CD1 e8aa0ddbff           call    0x404a80
00653CD6 8d45f3               lea     eax, [ebp - 0xd]
00653CD9 50                   push    eax
00653CDA 8d55f3               lea     edx, [ebp - 0xd]
00653CDD 8d8e58250000         lea     ecx, [esi + 0x2558]
00653CE3 52                   push    edx
00653CE4 c645fc05             mov     byte ptr [ebp - 4], 5
00653CE8 e8a3a2efff           call    0x54df90
00653CED 8d8e98250000         lea     ecx, [esi + 0x2598]
00653CF3 c645fc06             mov     byte ptr [ebp - 4], 6
00653CF7 e8840ddbff           call    0x404a80
00653CFC 8d9ea0250000         lea     ebx, [esi + 0x25a0]
00653D02 8bcb                 mov     ecx, ebx
00653D04 c645fc07             mov     byte ptr [ebp - 4], 7
00653D08 e823762500           call    0x8ab330
00653D0D 8bcb                 mov     ecx, ebx
00653D0F e81c772500           call    0x8ab430
00653D14 8d9e60260000         lea     ebx, [esi + 0x2660]
00653D1A 8bcb                 mov     ecx, ebx
00653D1C c645fc08             mov     byte ptr [ebp - 4], 8
00653D20 e80b762500           call    0x8ab330
00653D25 8bcb                 mov     ecx, ebx
00653D27 e804772500           call    0x8ab430
00653D2C 8d8e78260000         lea     ecx, [esi + 0x2678]
00653D32 c645fc09             mov     byte ptr [ebp - 4], 9
00653D36 e8450ddbff           call    0x404a80
00653D3B 8d8e80260000         lea     ecx, [esi + 0x2680]
00653D41 c645fc0a             mov     byte ptr [ebp - 4], 0xa
00653D45 e8360ddbff           call    0x404a80
00653D4A 8d8e88260000         lea     ecx, [esi + 0x2688]
00653D50 c645fc0b             mov     byte ptr [ebp - 4], 0xb
00653D54 e8270ddbff           call    0x404a80
00653D59 8d8e30270000         lea     ecx, [esi + 0x2730]
00653D5F c645fc0c             mov     byte ptr [ebp - 4], 0xc
00653D63 e8a809dbff           call    0x404710
00653D68 8d9e40270000         lea     ebx, [esi + 0x2740]
00653D6E 8bcb                 mov     ecx, ebx
00653D70 c645fc0d             mov     byte ptr [ebp - 4], 0xd
00653D74 e8b7752500           call    0x8ab330
00653D79 8bcb                 mov     ecx, ebx
00653D7B e8b0762500           call    0x8ab430
00653D80 8d9e54270000         lea     ebx, [esi + 0x2754]
00653D86 8bcb                 mov     ecx, ebx
00653D88 c645fc0e             mov     byte ptr [ebp - 4], 0xe
00653D8C e89f752500           call    0x8ab330
00653D91 8bcb                 mov     ecx, ebx
00653D93 e898762500           call    0x8ab430
00653D98 8d45f3               lea     eax, [ebp - 0xd]
00653D9B 50                   push    eax
00653D9C 8d55f3               lea     edx, [ebp - 0xd]
00653D9F 8d8e6c270000         lea     ecx, [esi + 0x276c]
00653DA5 52                   push    edx
00653DA6 c645fc0f             mov     byte ptr [ebp - 4], 0xf
00653DAA e8311bf0ff           call    0x5558e0
00653DAF 8d9e64290000         lea     ebx, [esi + 0x2964]
00653DB5 8bcb                 mov     ecx, ebx
00653DB7 c645fc10             mov     byte ptr [ebp - 4], 0x10
00653DBB e870752500           call    0x8ab330
00653DC0 8bcb                 mov     ecx, ebx
00653DC2 e869762500           call    0x8ab430
00653DC7 8d45f3               lea     eax, [ebp - 0xd]
00653DCA 50                   push    eax
00653DCB 8d55f3               lea     edx, [ebp - 0xd]
00653DCE 8d8e84290000         lea     ecx, [esi + 0x2984]
00653DD4 52                   push    edx
00653DD5 c645fc11             mov     byte ptr [ebp - 4], 0x11
00653DD9 e8b2a1efff           call    0x54df90
00653DDE 8d8eb0290000         lea     ecx, [esi + 0x29b0]
00653DE4 c645fc12             mov     byte ptr [ebp - 4], 0x12
00653DE8 e8930cdbff           call    0x404a80
00653DED 8d8eb8290000         lea     ecx, [esi + 0x29b8]
00653DF3 c645fc13             mov     byte ptr [ebp - 4], 0x13
00653DF7 e8840cdbff           call    0x404a80
00653DFC 8d8ec0290000         lea     ecx, [esi + 0x29c0]
00653E02 c645fc14             mov     byte ptr [ebp - 4], 0x14
00653E06 e8750cdbff           call    0x404a80
00653E0B 8d9ec8290000         lea     ebx, [esi + 0x29c8]
00653E11 8bcb                 mov     ecx, ebx
00653E13 c645fc15             mov     byte ptr [ebp - 4], 0x15
00653E17 e814752500           call    0x8ab330
00653E1C 8bcb                 mov     ecx, ebx
00653E1E e80d762500           call    0x8ab430
00653E23 8d8edc290000         lea     ecx, [esi + 0x29dc]
00653E29 c645fc16             mov     byte ptr [ebp - 4], 0x16
00653E2D e84e0cdbff           call    0x404a80
00653E32 8d8ee8290000         lea     ecx, [esi + 0x29e8]
00653E38 c645fc17             mov     byte ptr [ebp - 4], 0x17
00653E3C e83f0cdbff           call    0x404a80
00653E41 8d8ef0290000         lea     ecx, [esi + 0x29f0]
00653E47 c645fc18             mov     byte ptr [ebp - 4], 0x18
00653E4B e8300cdbff           call    0x404a80
00653E50 89be242a0000         mov     dword ptr [esi + 0x2a24], edi
00653E56 89be282a0000         mov     dword ptr [esi + 0x2a28], edi
00653E5C 89be2c2a0000         mov     dword ptr [esi + 0x2a2c], edi
00653E62 8dbee42a0000         lea     edi, [esi + 0x2ae4]
00653E68 8bcf                 mov     ecx, edi
00653E6A c645fc1a             mov     byte ptr [ebp - 4], 0x1a
00653E6E e8bd742500           call    0x8ab330
00653E73 8bcf                 mov     ecx, edi
00653E75 e8b6752500           call    0x8ab430
00653E7A 8dbe482b0000         lea     edi, [esi + 0x2b48]
00653E80 8bcf                 mov     ecx, edi
00653E82 c645fc1b             mov     byte ptr [ebp - 4], 0x1b
00653E86 e8a5742500           call    0x8ab330
00653E8B 8bcf                 mov     ecx, edi
00653E8D e89e752500           call    0x8ab430
00653E92 8bce                 mov     ecx, esi
00653E94 c645fc1c             mov     byte ptr [ebp - 4], 0x1c
00653E98 e863dbfdff           call    0x631a00
00653E9D 8bce                 mov     ecx, esi
00653E9F e8cc0dfeff           call    0x634c70
00653EA4 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
00653EA7 5f                   pop     edi
00653EA8 8bc6                 mov     eax, esi
00653EAA 5e                   pop     esi
00653EAB 5b                   pop     ebx
00653EAC 64890d00000000       mov     dword ptr fs:[0], ecx
00653EB3 8be5                 mov     esp, ebp
00653EB5 5d                   pop     ebp
00653EB6 c3                   ret     

fighter_ctor_complete: 0x00654200, size 199
00654200 55                   push    ebp
00654201 8bec                 mov     ebp, esp
00654203 6aff                 push    -1
00654205 68ec679700           push    0x9767ec
0065420A 64a100000000         mov     eax, dword ptr fs:[0]
00654210 50                   push    eax
00654211 64892500000000       mov     dword ptr fs:[0], esp
00654218 51                   push    ecx
00654219 53                   push    ebx
0065421A 56                   push    esi
0065421B 57                   push    edi
0065421C 8bf1                 mov     esi, ecx
0065421E 6a01                 push    1
00654220 8975f0               mov     dword ptr [ebp - 0x10], esi
00654223 e868f0ffff           call    0x653290
00654228 33ff                 xor     edi, edi
0065422A c706fc3ea000         mov     dword ptr [esi], 0xa03efc
00654230 897dfc               mov     dword ptr [ebp - 4], edi
00654233 89befc240000         mov     dword ptr [esi + 0x24fc], edi
00654239 89be00250000         mov     dword ptr [esi + 0x2500], edi
0065423F 89be04250000         mov     dword ptr [esi + 0x2504], edi
00654245 8d9e64250000         lea     ebx, [esi + 0x2564]
0065424B 8bcb                 mov     ecx, ebx
0065424D c645fc01             mov     byte ptr [ebp - 4], 1
00654251 e8da702500           call    0x8ab330
00654256 8bcb                 mov     ecx, ebx
00654258 e8d3712500           call    0x8ab430
0065425D 89be38260000         mov     dword ptr [esi + 0x2638], edi
00654263 89be3c260000         mov     dword ptr [esi + 0x263c], edi
00654269 89be40260000         mov     dword ptr [esi + 0x2640], edi
0065426F 89becc260000         mov     dword ptr [esi + 0x26cc], edi
00654275 89bed0260000         mov     dword ptr [esi + 0x26d0], edi
0065427B 89bed4260000         mov     dword ptr [esi + 0x26d4], edi
00654281 8d8e3c270000         lea     ecx, [esi + 0x273c]
00654287 c645fc04             mov     byte ptr [ebp - 4], 4
0065428B e80015dbff           call    0x405790
00654290 89be80270000         mov     dword ptr [esi + 0x2780], edi
00654296 89be84270000         mov     dword ptr [esi + 0x2784], edi
0065429C 89be88270000         mov     dword ptr [esi + 0x2788], edi
006542A2 8bce                 mov     ecx, esi
006542A4 c645fc06             mov     byte ptr [ebp - 4], 6
006542A8 e8a393f8ff           call    0x5dd650
006542AD 8bce                 mov     ecx, esi
006542AF e86ca7f8ff           call    0x5dea20
006542B4 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
006542B7 5f                   pop     edi
006542B8 8bc6                 mov     eax, esi
006542BA 5e                   pop     esi
006542BB 5b                   pop     ebx
006542BC 64890d00000000       mov     dword ptr fs:[0], ecx
006542C3 8be5                 mov     esp, ebp
006542C5 5d                   pop     ebp
006542C6 c3                   ret     

gunner_ctor_complete: 0x00654420, size 534
00654420 55                   push    ebp
00654421 8bec                 mov     ebp, esp
00654423 6aff                 push    -1
00654425 687e699700           push    0x97697e
0065442A 64a100000000         mov     eax, dword ptr fs:[0]
00654430 50                   push    eax
00654431 64892500000000       mov     dword ptr fs:[0], esp
00654438 51                   push    ecx
00654439 53                   push    ebx
0065443A 56                   push    esi
0065443B 57                   push    edi
0065443C 8bf1                 mov     esi, ecx
0065443E 6a02                 push    2
00654440 8975f0               mov     dword ptr [ebp - 0x10], esi
00654443 e848eeffff           call    0x653290
00654448 33ff                 xor     edi, edi
0065444A 8d8e34240000         lea     ecx, [esi + 0x2434]
00654450 897dfc               mov     dword ptr [ebp - 4], edi
00654453 c7061c45a000         mov     dword ptr [esi], 0xa0451c
00654459 e812d9ffff           call    0x651d70
0065445E 8d8e08250000         lea     ecx, [esi + 0x2508]
00654464 c645fc01             mov     byte ptr [ebp - 4], 1
00654468 e81306dbff           call    0x404a80
0065446D 8d8e18250000         lea     ecx, [esi + 0x2518]
00654473 c645fc02             mov     byte ptr [ebp - 4], 2
00654477 e80406dbff           call    0x404a80
0065447C 8d8e20250000         lea     ecx, [esi + 0x2520]
00654482 c645fc03             mov     byte ptr [ebp - 4], 3
00654486 e8f505dbff           call    0x404a80
0065448B 8d8e28250000         lea     ecx, [esi + 0x2528]
00654491 c645fc04             mov     byte ptr [ebp - 4], 4
00654495 e8e605dbff           call    0x404a80
0065449A 8d8e3c250000         lea     ecx, [esi + 0x253c]
006544A0 c645fc05             mov     byte ptr [ebp - 4], 5
006544A4 e8d705dbff           call    0x404a80
006544A9 8d8e44250000         lea     ecx, [esi + 0x2544]
006544AF c645fc06             mov     byte ptr [ebp - 4], 6
006544B3 e8c805dbff           call    0x404a80
006544B8 8d8e6c250000         lea     ecx, [esi + 0x256c]
006544BE c645fc07             mov     byte ptr [ebp - 4], 7
006544C2 e8b905dbff           call    0x404a80
006544C7 8d8e98250000         lea     ecx, [esi + 0x2598]
006544CD c645fc08             mov     byte ptr [ebp - 4], 8
006544D1 e83a02dbff           call    0x404710
006544D6 8d8ea8250000         lea     ecx, [esi + 0x25a8]
006544DC c645fc09             mov     byte ptr [ebp - 4], 9
006544E0 e82b02dbff           call    0x404710
006544E5 8d8eb0250000         lea     ecx, [esi + 0x25b0]
006544EB c645fc0a             mov     byte ptr [ebp - 4], 0xa
006544EF e81c02dbff           call    0x404710
006544F4 8d9e08260000         lea     ebx, [esi + 0x2608]
006544FA 8bcb                 mov     ecx, ebx
006544FC c645fc0b             mov     byte ptr [ebp - 4], 0xb
00654500 e82b6e2500           call    0x8ab330
00654505 8bcb                 mov     ecx, ebx
00654507 e8246f2500           call    0x8ab430
0065450C 8d8ef4260000         lea     ecx, [esi + 0x26f4]
00654512 c645fc0c             mov     byte ptr [ebp - 4], 0xc
00654516 e86505dbff           call    0x404a80
0065451B 8d9e50280000         lea     ebx, [esi + 0x2850]
00654521 8bcb                 mov     ecx, ebx
00654523 c645fc0d             mov     byte ptr [ebp - 4], 0xd
00654527 e8046e2500           call    0x8ab330
0065452C 8bcb                 mov     ecx, ebx
0065452E e8fd6e2500           call    0x8ab430
00654533 8d8e6c280000         lea     ecx, [esi + 0x286c]
00654539 c645fc0e             mov     byte ptr [ebp - 4], 0xe
0065453D e83e05dbff           call    0x404a80
00654542 8d8e74280000         lea     ecx, [esi + 0x2874]
00654548 c645fc0f             mov     byte ptr [ebp - 4], 0xf
0065454C e82f05dbff           call    0x404a80
00654551 c645fc10             mov     byte ptr [ebp - 4], 0x10
00654555 8d8eec280000         lea     ecx, [esi + 0x28ec]
0065455B e82005dbff           call    0x404a80
00654560 8d9e00290000         lea     ebx, [esi + 0x2900]
00654566 8bcb                 mov     ecx, ebx
00654568 c645fc11             mov     byte ptr [ebp - 4], 0x11
0065456C e8bf6d2500           call    0x8ab330
00654571 8bcb                 mov     ecx, ebx
00654573 e8b86e2500           call    0x8ab430
00654578 89be18290000         mov     dword ptr [esi + 0x2918], edi
0065457E 89be1c290000         mov     dword ptr [esi + 0x291c], edi
00654584 89be20290000         mov     dword ptr [esi + 0x2920], edi
0065458A 89be34290000         mov     dword ptr [esi + 0x2934], edi
00654590 89be38290000         mov     dword ptr [esi + 0x2938], edi
00654596 89be3c290000         mov     dword ptr [esi + 0x293c], edi
0065459C 8d8e70290000         lea     ecx, [esi + 0x2970]
006545A2 c645fc14             mov     byte ptr [ebp - 4], 0x14
006545A6 e8d504dbff           call    0x404a80
006545AB 8d8e78290000         lea     ecx, [esi + 0x2978]
006545B1 c645fc15             mov     byte ptr [ebp - 4], 0x15
006545B5 e8c604dbff           call    0x404a80
006545BA 68e01f4000           push    0x401fe0
006545BF 68804a4000           push    0x404a80
006545C4 6a03                 push    3
006545C6 6a08                 push    8
006545C8 8d8698290000         lea     eax, [esi + 0x2998]
006545CE 50                   push    eax
006545CF c645fc16             mov     byte ptr [ebp - 4], 0x16
006545D3 e893602c00           call    0x91a66b
006545D8 8d8e1c2a0000         lea     ecx, [esi + 0x2a1c]
006545DE c645fc17             mov     byte ptr [ebp - 4], 0x17
006545E2 e89904dbff           call    0x404a80
006545E7 8d9e242a0000         lea     ebx, [esi + 0x2a24]
006545ED 8bcb                 mov     ecx, ebx
006545EF c645fc18             mov     byte ptr [ebp - 4], 0x18
006545F3 e8386d2500           call    0x8ab330
006545F8 8bcb                 mov     ecx, ebx
006545FA e8316e2500           call    0x8ab430
006545FF 89be3c2a0000         mov     dword ptr [esi + 0x2a3c], edi
00654605 89be402a0000         mov     dword ptr [esi + 0x2a40], edi
0065460B 89be442a0000         mov     dword ptr [esi + 0x2a44], edi
00654611 8bce                 mov     ecx, esi
00654613 c645fc1a             mov     byte ptr [ebp - 4], 0x1a
00654617 e804fcf9ff           call    0x5f4220
0065461C 8bce                 mov     ecx, esi
0065461E e85d27faff           call    0x5f6d80
00654623 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
00654626 5f                   pop     edi
00654627 8bc6                 mov     eax, esi
00654629 5e                   pop     esi
0065462A 5b                   pop     ebx
0065462B 64890d00000000       mov     dword ptr fs:[0], ecx
00654632 8be5                 mov     esp, ebp
00654634 5d                   pop     ebp
00654635 c3                   ret     

mage_ctor_complete: 0x006548B0, size 346
006548B0 55                   push    ebp
006548B1 8bec                 mov     ebp, esp
006548B3 6aff                 push    -1
006548B5 68666a9700           push    0x976a66
006548BA 64a100000000         mov     eax, dword ptr fs:[0]
006548C0 50                   push    eax
006548C1 64892500000000       mov     dword ptr fs:[0], esp
006548C8 51                   push    ecx
006548C9 53                   push    ebx
006548CA 56                   push    esi
006548CB 57                   push    edi
006548CC 8bf1                 mov     esi, ecx
006548CE 6a03                 push    3
006548D0 8975f0               mov     dword ptr [ebp - 0x10], esi
006548D3 e8b8e9ffff           call    0x653290
006548D8 8dbe4c240000         lea     edi, [esi + 0x244c]
006548DE 33db                 xor     ebx, ebx
006548E0 8bcf                 mov     ecx, edi
006548E2 895dfc               mov     dword ptr [ebp - 4], ebx
006548E5 c7063c4ba000         mov     dword ptr [esi], 0xa04b3c
006548EB e8406a2500           call    0x8ab330
006548F0 8bcf                 mov     ecx, edi
006548F2 e8396b2500           call    0x8ab430
006548F7 899e5c250000         mov     dword ptr [esi + 0x255c], ebx
006548FD 899e60250000         mov     dword ptr [esi + 0x2560], ebx
00654903 899e64250000         mov     dword ptr [esi + 0x2564], ebx
00654909 6830bb7400           push    0x74bb30
0065490E 6830bf4200           push    0x42bf30
00654913 6a02                 push    2
00654915 6a10                 push    0x10
00654917 8d869c250000         lea     eax, [esi + 0x259c]
0065491D 50                   push    eax
0065491E c645fc02             mov     byte ptr [ebp - 4], 2
00654922 e8445d2c00           call    0x91a66b
00654927 8dbe04260000         lea     edi, [esi + 0x2604]
0065492D 8bcf                 mov     ecx, edi
0065492F c645fc03             mov     byte ptr [ebp - 4], 3
00654933 e8f8692500           call    0x8ab330
00654938 8bcf                 mov     ecx, edi
0065493A e8f16a2500           call    0x8ab430
0065493F 8dbe30260000         lea     edi, [esi + 0x2630]
00654945 8bcf                 mov     ecx, edi
00654947 c645fc04             mov     byte ptr [ebp - 4], 4
0065494B e8e0692500           call    0x8ab330
00654950 8bcf                 mov     ecx, edi
00654952 e8d96a2500           call    0x8ab430
00654957 8d8e7c260000         lea     ecx, [esi + 0x267c]
0065495D c645fc05             mov     byte ptr [ebp - 4], 5
00654961 e81a01dbff           call    0x404a80
00654966 8d8e84260000         lea     ecx, [esi + 0x2684]
0065496C c645fc06             mov     byte ptr [ebp - 4], 6
00654970 e80b01dbff           call    0x404a80
00654975 8d8ea0260000         lea     ecx, [esi + 0x26a0]
0065497B c645fc07             mov     byte ptr [ebp - 4], 7
0065497F e8fc00dbff           call    0x404a80
00654984 8d8ea8260000         lea     ecx, [esi + 0x26a8]
0065498A c645fc08             mov     byte ptr [ebp - 4], 8
0065498E e8ed00dbff           call    0x404a80
00654993 8d8e48270000         lea     ecx, [esi + 0x2748]
00654999 c645fc09             mov     byte ptr [ebp - 4], 9
0065499D e8de00dbff           call    0x404a80
006549A2 8d8e50270000         lea     ecx, [esi + 0x2750]
006549A8 c645fc0a             mov     byte ptr [ebp - 4], 0xa
006549AC e8cf00dbff           call    0x404a80
006549B1 8d8e58270000         lea     ecx, [esi + 0x2758]
006549B7 c645fc0b             mov     byte ptr [ebp - 4], 0xb
006549BB e8c000dbff           call    0x404a80
006549C0 8d8e60270000         lea     ecx, [esi + 0x2760]
006549C6 c645fc0c             mov     byte ptr [ebp - 4], 0xc
006549CA e8b100dbff           call    0x404a80
006549CF c786902700000f000000 mov     dword ptr [esi + 0x2790], 0xf
006549D9 899e8c270000         mov     dword ptr [esi + 0x278c], ebx
006549DF 889e7c270000         mov     byte ptr [esi + 0x277c], bl
006549E5 c645fc0e             mov     byte ptr [ebp - 4], 0xe
006549E9 8bce                 mov     ecx, esi
006549EB e88064fbff           call    0x60ae70
006549F0 8bce                 mov     ecx, esi
006549F2 e87990fbff           call    0x60da70
006549F7 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
006549FA 5f                   pop     edi
006549FB 8bc6                 mov     eax, esi
006549FD 5e                   pop     esi
006549FE 5b                   pop     ebx
006549FF 64890d00000000       mov     dword ptr fs:[0], ecx
00654A06 8be5                 mov     esp, ebp
00654A08 5d                   pop     ebp
00654A09 c3                   ret     

priest_ctor_complete: 0x00654BB0, size 337
00654BB0 55                   push    ebp
00654BB1 8bec                 mov     ebp, esp
00654BB3 6aff                 push    -1
00654BB5 68206b9700           push    0x976b20
00654BBA 64a100000000         mov     eax, dword ptr fs:[0]
00654BC0 50                   push    eax
00654BC1 64892500000000       mov     dword ptr fs:[0], esp
00654BC8 83ec08               sub     esp, 8
00654BCB 53                   push    ebx
00654BCC 56                   push    esi
00654BCD 57                   push    edi
00654BCE 8bf1                 mov     esi, ecx
00654BD0 6a04                 push    4
00654BD2 8975ec               mov     dword ptr [ebp - 0x14], esi
00654BD5 e8b6e6ffff           call    0x653290
00654BDA 33db                 xor     ebx, ebx
00654BDC 8d8e34240000         lea     ecx, [esi + 0x2434]
00654BE2 895dfc               mov     dword ptr [ebp - 4], ebx
00654BE5 c7065c51a000         mov     dword ptr [esi], 0xa0515c
00654BEB e890fedaff           call    0x404a80
00654BF0 8dbe7c240000         lea     edi, [esi + 0x247c]
00654BF6 8bcf                 mov     ecx, edi
00654BF8 c645fc01             mov     byte ptr [ebp - 4], 1
00654BFC e82f672500           call    0x8ab330
00654C01 8bcf                 mov     ecx, edi
00654C03 e828682500           call    0x8ab430
00654C08 8d8e9c240000         lea     ecx, [esi + 0x249c]
00654C0E c645fc02             mov     byte ptr [ebp - 4], 2
00654C12 e869fedaff           call    0x404a80
00654C17 8d8eb4240000         lea     ecx, [esi + 0x24b4]
00654C1D c645fc03             mov     byte ptr [ebp - 4], 3
00654C21 e85afedaff           call    0x404a80
00654C26 8dbed8240000         lea     edi, [esi + 0x24d8]
00654C2C 8bcf                 mov     ecx, edi
00654C2E c645fc04             mov     byte ptr [ebp - 4], 4
00654C32 e8f9662500           call    0x8ab330
00654C37 8bcf                 mov     ecx, edi
00654C39 e8f2672500           call    0x8ab430
00654C3E 8d45f3               lea     eax, [ebp - 0xd]
00654C41 50                   push    eax
00654C42 8d55f3               lea     edx, [ebp - 0xd]
00654C45 8d8e20250000         lea     ecx, [esi + 0x2520]
00654C4B 52                   push    edx
00654C4C c645fc05             mov     byte ptr [ebp - 4], 5
00654C50 e88b0cf0ff           call    0x5558e0
00654C55 8dbe6c250000         lea     edi, [esi + 0x256c]
00654C5B 8bcf                 mov     ecx, edi
00654C5D c645fc06             mov     byte ptr [ebp - 4], 6
00654C61 e8ca662500           call    0x8ab330
00654C66 8bcf                 mov     ecx, edi
00654C68 e8c3672500           call    0x8ab430
00654C6D 8d8eb0250000         lea     ecx, [esi + 0x25b0]
00654C73 c645fc07             mov     byte ptr [ebp - 4], 7
00654C77 e804fedaff           call    0x404a80
00654C7C 8d8eb8250000         lea     ecx, [esi + 0x25b8]
00654C82 c645fc08             mov     byte ptr [ebp - 4], 8
00654C86 e8f5fddaff           call    0x404a80
00654C8B 899eb0260000         mov     dword ptr [esi + 0x26b0], ebx
00654C91 899eb4260000         mov     dword ptr [esi + 0x26b4], ebx
00654C97 899eb8260000         mov     dword ptr [esi + 0x26b8], ebx
00654C9D 8d8ebc260000         lea     ecx, [esi + 0x26bc]
00654CA3 c645fc0a             mov     byte ptr [ebp - 4], 0xa
00654CA7 e8d4fddaff           call    0x404a80
00654CAC 8d8ec4260000         lea     ecx, [esi + 0x26c4]
00654CB2 c645fc0b             mov     byte ptr [ebp - 4], 0xb
00654CB6 e855fadaff           call    0x404710
00654CBB 8bce                 mov     ecx, esi
00654CBD c645fc0c             mov     byte ptr [ebp - 4], 0xc
00654CC1 e86a85fcff           call    0x61d230
00654CC6 8bce                 mov     ecx, esi
00654CC8 e82393fcff           call    0x61dff0
00654CCD 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
00654CD0 5f                   pop     edi
00654CD1 8bc6                 mov     eax, esi
00654CD3 5e                   pop     esi
00654CD4 5b                   pop     ebx
00654CD5 64890d00000000       mov     dword ptr fs:[0], ecx
00654CDC 8be5                 mov     esp, ebp
00654CDE 5d                   pop     ebp
00654CDF c3                   ret     
00654CE0 55                   push    ebp
00654CE1 8bec                 mov     ebp, esp
00654CE3 56                   push    esi
00654CE4 8bf1                 mov     esi, ecx
00654CE6 e825000000           call    0x654d10
00654CEB f6450801             test    byte ptr [ebp + 8], 1
00654CEF 7409                 je      0x654cfa
00654CF1 56                   push    esi
00654CF2 e886502c00           call    0x919d7d
00654CF7 83c404               add     esp, 4
00654CFA 8bc6                 mov     eax, esi
00654CFC 5e                   pop     esi
00654CFD 5d                   pop     ebp
00654CFE c20400               ret     4

map_registration_complete: 0x004C7AE0, size 1012
004C7AE0 55                   push    ebp
004C7AE1 8bec                 mov     ebp, esp
004C7AE3 51                   push    ecx
004C7AE4 56                   push    esi
004C7AE5 8b7510               mov     esi, dword ptr [ebp + 0x10]
004C7AE8 85f6                 test    esi, esi
004C7AEA 57                   push    edi
004C7AEB 8bf9                 mov     edi, ecx
004C7AED 0f84d9030000         je      0x4c7ecc
004C7AF3 56                   push    esi
004C7AF4 e817d8ffff           call    0x4c5310
004C7AF9 84c0                 test    al, al
004C7AFB 0f85cb030000         jne     0x4c7ecc
004C7B01 8b06                 mov     eax, dword ptr [esi]
004C7B03 8bce                 mov     ecx, esi
004C7B05 ff5028               call    dword ptr [eax + 0x28]
004C7B08 8b4d08               mov     ecx, dword ptr [ebp + 8]
004C7B0B 8b550c               mov     edx, dword ptr [ebp + 0xc]
004C7B0E 8908                 mov     dword ptr [eax], ecx
004C7B10 8d45fc               lea     eax, [ebp - 4]
004C7B13 8955fc               mov     dword ptr [ebp - 4], edx
004C7B16 8b16                 mov     edx, dword ptr [esi]
004C7B18 50                   push    eax
004C7B19 8bce                 mov     ecx, esi
004C7B1B ff5228               call    dword ptr [edx + 0x28]
004C7B1E 8bc8                 mov     ecx, eax
004C7B20 83c104               add     ecx, 4
004C7B23 e8d8cff3ff           call    0x404b00
004C7B28 8b06                 mov     eax, dword ptr [esi]
004C7B2A 8bce                 mov     ecx, esi
004C7B2C ff5054               call    dword ptr [eax + 0x54]
004C7B2F 85c0                 test    eax, eax
004C7B31 740f                 je      0x4c7b42
004C7B33 8b16                 mov     edx, dword ptr [esi]
004C7B35 56                   push    esi
004C7B36 8bce                 mov     ecx, esi
004C7B38 ff5254               call    dword ptr [edx + 0x54]
004C7B3B 8bc8                 mov     ecx, eax
004C7B3D e8beebffff           call    0x4c6700
004C7B42 8b06                 mov     eax, dword ptr [esi]
004C7B44 53                   push    ebx
004C7B45 57                   push    edi
004C7B46 8bce                 mov     ecx, esi
004C7B48 ff5050               call    dword ptr [eax + 0x50]
004C7B4B 8d5510               lea     edx, [ebp + 0x10]
004C7B4E 8d4f0c               lea     ecx, [edi + 0xc]
004C7B51 52                   push    edx
004C7B52 e8b929faff           call    0x46a510
004C7B57 8b06                 mov     eax, dword ptr [esi]
004C7B59 8bce                 mov     ecx, esi
004C7B5B ff5028               call    dword ptr [eax + 0x28]
004C7B5E 83780c06             cmp     dword ptr [eax + 0xc], 6
004C7B62 7479                 je      0x4c7bdd
004C7B64 8b16                 mov     edx, dword ptr [esi]
004C7B66 8bce                 mov     ecx, esi
004C7B68 ff5228               call    dword ptr [edx + 0x28]
004C7B6B 8b400c               mov     eax, dword ptr [eax + 0xc]
004C7B6E c1e004               shl     eax, 4
004C7B71 8d5c381c             lea     ebx, [eax + edi + 0x1c]
004C7B75 8b4514               mov     eax, dword ptr [ebp + 0x14]
004C7B78 85c0                 test    eax, eax
004C7B7A 7456                 je      0x4c7bd2
004C7B7C 8b4308               mov     eax, dword ptr [ebx + 8]
004C7B7F 8b4b04               mov     ecx, dword ptr [ebx + 4]
004C7B82 8d5514               lea     edx, [ebp + 0x14]
004C7B85 52                   push    edx
004C7B86 50                   push    eax
004C7B87 51                   push    ecx
004C7B88 8d45fc               lea     eax, [ebp - 4]
004C7B8B 50                   push    eax
004C7B8C e89f6dfbff           call    0x47e930
004C7B91 8b45fc               mov     eax, dword ptr [ebp - 4]
004C7B94 8b4b08               mov     ecx, dword ptr [ebx + 8]
004C7B97 83c410               add     esp, 0x10
004C7B9A 3bc1                 cmp     eax, ecx
004C7B9C 7506                 jne     0x4c7ba4
004C7B9E 8d4d10               lea     ecx, [ebp + 0x10]
004C7BA1 51                   push    ecx
004C7BA2 eb32                 jmp     0x4c7bd6
004C7BA4 8a4d18               mov     cl, byte ptr [ebp + 0x18]
004C7BA7 84c9                 test    cl, cl
004C7BA9 7412                 je      0x4c7bbd
004C7BAB 8d5510               lea     edx, [ebp + 0x10]
004C7BAE 52                   push    edx
004C7BAF 50                   push    eax
004C7BB0 8d4518               lea     eax, [ebp + 0x18]
004C7BB3 50                   push    eax
004C7BB4 8bcb                 mov     ecx, ebx
004C7BB6 e80521faff           call    0x469cc0
004C7BBB eb20                 jmp     0x4c7bdd
004C7BBD 8d4d10               lea     ecx, [ebp + 0x10]
004C7BC0 51                   push    ecx
004C7BC1 83c004               add     eax, 4
004C7BC4 50                   push    eax
004C7BC5 8d5518               lea     edx, [ebp + 0x18]
004C7BC8 52                   push    edx
004C7BC9 8bcb                 mov     ecx, ebx
004C7BCB e8f020faff           call    0x469cc0
004C7BD0 eb0b                 jmp     0x4c7bdd
004C7BD2 8d4510               lea     eax, [ebp + 0x10]
004C7BD5 50                   push    eax
004C7BD6 8bcb                 mov     ecx, ebx
004C7BD8 e83329faff           call    0x46a510
004C7BDD 8b16                 mov     edx, dword ptr [esi]
004C7BDF 6a01                 push    1
004C7BE1 8bce                 mov     ecx, esi
004C7BE3 ff522c               call    dword ptr [edx + 0x2c]
004C7BE6 84c0                 test    al, al
004C7BE8 7417                 je      0x4c7c01
004C7BEA 8d4518               lea     eax, [ebp + 0x18]
004C7BED 50                   push    eax
004C7BEE 8d4f7c               lea     ecx, [edi + 0x7c]
004C7BF1 897518               mov     dword ptr [ebp + 0x18], esi
004C7BF4 e877650200           call    0x4ee170
004C7BF9 56                   push    esi
004C7BFA 8bcf                 mov     ecx, edi
004C7BFC e88ffcffff           call    0x4c7890
004C7C01 8b0d145ab700         mov     ecx, dword ptr [0xb75a14]
004C7C07 33db                 xor     ebx, ebx
004C7C09 3bcb                 cmp     ecx, ebx
004C7C0B 7418                 je      0x4c7c25
004C7C0D 8b11                 mov     edx, dword ptr [ecx]
004C7C0F ff927c050000         call    dword ptr [edx + 0x57c]
004C7C15 394508               cmp     dword ptr [ebp + 8], eax
004C7C18 750b                 jne     0x4c7c25
004C7C1A 8b450c               mov     eax, dword ptr [ebp + 0xc]
004C7C1D 50                   push    eax
004C7C1E 8bcf                 mov     ecx, edi
004C7C20 e88be9ffff           call    0x4c65b0
004C7C25 8b4f10               mov     ecx, dword ptr [edi + 0x10]
004C7C28 3bcb                 cmp     ecx, ebx
004C7C2A 7505                 jne     0x4c7c31
004C7C2C 895d18               mov     dword ptr [ebp + 0x18], ebx
004C7C2F eb0b                 jmp     0x4c7c3c
004C7C31 8b4714               mov     eax, dword ptr [edi + 0x14]
004C7C34 2bc1                 sub     eax, ecx
004C7C36 c1f802               sar     eax, 2
004C7C39 894518               mov     dword ptr [ebp + 0x18], eax
004C7C3C 8b4518               mov     eax, dword ptr [ebp + 0x18]
004C7C3F 85c0                 test    eax, eax
004C7C41 7e19                 jle     0x4c7c5c
004C7C43 8b4f10               mov     ecx, dword ptr [edi + 0x10]
004C7C46 8d0499               lea     eax, [ecx + ebx*4]
004C7C49 8b08                 mov     ecx, dword ptr [eax]
004C7C4B 8b11                 mov     edx, dword ptr [ecx]
004C7C4D 56                   push    esi
004C7C4E ff9230010000         call    dword ptr [edx + 0x130]
004C7C54 8b4518               mov     eax, dword ptr [ebp + 0x18]
004C7C57 43                   inc     ebx
004C7C58 3bd8                 cmp     ebx, eax
004C7C5A 7ce7                 jl      0x4c7c43
004C7C5C 8d8f0c010000         lea     ecx, [edi + 0x10c]
004C7C62 e8c9a3f3ff           call    0x402030
004C7C67 83f801               cmp     eax, 1
004C7C6A 745f                 je      0x4c7ccb
004C7C6C a1145ab700           mov     eax, dword ptr [0xb75a14]
004C7C71 85c0                 test    eax, eax
004C7C73 7456                 je      0x4c7ccb
004C7C75 8b06                 mov     eax, dword ptr [esi]
004C7C77 6a11                 push    0x11
004C7C79 8bce                 mov     ecx, esi
004C7C7B c6451800             mov     byte ptr [ebp + 0x18], 0
004C7C7F ff502c               call    dword ptr [eax + 0x2c]
004C7C82 84c0                 test    al, al
004C7C84 7420                 je      0x4c7ca6
004C7C86 8b0d145ab700         mov     ecx, dword ptr [0xb75a14]
004C7C8C 8b11                 mov     edx, dword ptr [ecx]
004C7C8E ff928c010000         call    dword ptr [edx + 0x18c]
004C7C94 8bd8                 mov     ebx, eax
004C7C96 8b06                 mov     eax, dword ptr [esi]
004C7C98 8bce                 mov     ecx, esi
004C7C9A ff908c010000         call    dword ptr [eax + 0x18c]
004C7CA0 3bd8                 cmp     ebx, eax
004C7CA2 741c                 je      0x4c7cc0
004C7CA4 eb16                 jmp     0x4c7cbc
004C7CA6 8b16                 mov     edx, dword ptr [esi]
004C7CA8 6a21                 push    0x21
004C7CAA 8bce                 mov     ecx, esi
004C7CAC ff522c               call    dword ptr [edx + 0x2c]
004C7CAF 84c0                 test    al, al
004C7CB1 740d                 je      0x4c7cc0
004C7CB3 83be8804000001       cmp     dword ptr [esi + 0x488], 1
004C7CBA 7504                 jne     0x4c7cc0
004C7CBC c6451801             mov     byte ptr [ebp + 0x18], 1
004C7CC0 8b4518               mov     eax, dword ptr [ebp + 0x18]
004C7CC3 50                   push    eax
004C7CC4 8bcf                 mov     ecx, edi
004C7CC6 e835cff3ff           call    0x404c00
004C7CCB 8b16                 mov     edx, dword ptr [esi]
004C7CCD 8bce                 mov     ecx, esi
004C7CCF ff5210               call    dword ptr [edx + 0x10]
004C7CD2 8b06                 mov     eax, dword ptr [esi]
004C7CD4 6a21                 push    0x21
004C7CD6 8bce                 mov     ecx, esi
004C7CD8 ff502c               call    dword ptr [eax + 0x2c]
004C7CDB 84c0                 test    al, al
004C7CDD 742a                 je      0x4c7d09
004C7CDF 8b16                 mov     edx, dword ptr [esi]
004C7CE1 8bce                 mov     ecx, esi
004C7CE3 ff92d8020000         call    dword ptr [edx + 0x2d8]
004C7CE9 8b4808               mov     ecx, dword ptr [eax + 8]
004C7CEC 85c9                 test    ecx, ecx
004C7CEE 7419                 je      0x4c7d09
004C7CF0 8b87f4000000         mov     eax, dword ptr [edi + 0xf4]
004C7CF6 85c0                 test    eax, eax
004C7CF8 740f                 je      0x4c7d09
004C7CFA 8b4850               mov     ecx, dword ptr [eax + 0x50]
004C7CFD 85c9                 test    ecx, ecx
004C7CFF 7408                 je      0x4c7d09
004C7D01 6a01                 push    1
004C7D03 56                   push    esi
004C7D04 e8a7c4f9ff           call    0x4641b0
004C7D09 8b06                 mov     eax, dword ptr [esi]
004C7D0B 8bce                 mov     ecx, esi
004C7D0D ff5030               call    dword ptr [eax + 0x30]
004C7D10 83f802               cmp     eax, 2
004C7D13 0f84b2010000         je      0x4c7ecb
004C7D19 8b16                 mov     edx, dword ptr [esi]
004C7D1B 8bce                 mov     ecx, esi
004C7D1D ff5230               call    dword ptr [edx + 0x30]
004C7D20 3d21010000           cmp     eax, 0x121
004C7D25 0f84a0010000         je      0x4c7ecb
004C7D2B 8b15d8c4b500         mov     edx, dword ptr [0xb5c4d8]
004C7D31 83fa10               cmp     edx, 0x10
004C7D34 a1c4c4b500           mov     eax, dword ptr [0xb5c4c4]
004C7D39 8bc8                 mov     ecx, eax
004C7D3B 7305                 jae     0x4c7d42
004C7D3D b9c4c4b500           mov     ecx, 0xb5c4c4
004C7D42 8b35d4c4b500         mov     esi, dword ptr [0xb5c4d4]
004C7D48 03ce                 add     ecx, esi
004C7D4A 83fa10               cmp     edx, 0x10
004C7D4D 7305                 jae     0x4c7d54
004C7D4F b8c4c4b500           mov     eax, 0xb5c4c4
004C7D54 51                   push    ecx
004C7D55 50                   push    eax
004C7D56 8d4518               lea     eax, [ebp + 0x18]
004C7D59 50                   push    eax
004C7D5A b9c0c4b500           mov     ecx, 0xb5c4c0
004C7D5F e8dca6f3ff           call    0x402440
004C7D64 8b4f10               mov     ecx, dword ptr [edi + 0x10]
004C7D67 85c9                 test    ecx, ecx
004C7D69 7504                 jne     0x4c7d6f
004C7D6B 33c0                 xor     eax, eax
004C7D6D eb08                 jmp     0x4c7d77
004C7D6F 8b4714               mov     eax, dword ptr [edi + 0x14]
004C7D72 2bc1                 sub     eax, ecx
004C7D74 c1f802               sar     eax, 2
004C7D77 50                   push    eax
004C7D78 68b8fd9900           push    0x99fdb8
004C7D7D 6a00                 push    0
004C7D7F e82c3d3e00           call    0x8abab0
004C7D84 8bc8                 mov     ecx, eax
004C7D86 83c40c               add     esp, 0xc
004C7D89 8d7101               lea     esi, [ecx + 1]
004C7D8C 8d642400             lea     esp, [esp]
004C7D90 8a11                 mov     dl, byte ptr [ecx]
004C7D92 41                   inc     ecx
004C7D93 84d2                 test    dl, dl
004C7D95 75f9                 jne     0x4c7d90
004C7D97 2bce                 sub     ecx, esi
004C7D99 51                   push    ecx
004C7D9A 50                   push    eax
004C7D9B b9c0c4b500           mov     ecx, 0xb5c4c0
004C7DA0 e8bb4ff4ff           call    0x40cd60
004C7DA5 33db                 xor     ebx, ebx
004C7DA7 8b4f10               mov     ecx, dword ptr [edi + 0x10]
004C7DAA 85c9                 test    ecx, ecx
004C7DAC 895d18               mov     dword ptr [ebp + 0x18], ebx
004C7DAF 0f84f9000000         je      0x4c7eae
004C7DB5 8b4714               mov     eax, dword ptr [edi + 0x14]
004C7DB8 2bc1                 sub     eax, ecx
004C7DBA c1f802               sar     eax, 2
004C7DBD 3bd8                 cmp     ebx, eax
004C7DBF 0f83e9000000         jae     0x4c7eae
004C7DC5 8bc1                 mov     eax, ecx
004C7DC7 8d349d00000000       lea     esi, [ebx*4]
004C7DCE 03c6                 add     eax, esi
004C7DD0 8b00                 mov     eax, dword ptr [eax]
004C7DD2 8b10                 mov     edx, dword ptr [eax]
004C7DD4 8bc8                 mov     ecx, eax
004C7DD6 ff5230               call    dword ptr [edx + 0x30]
004C7DD9 83f802               cmp     eax, 2
004C7DDC 0f84c6000000         je      0x4c7ea8
004C7DE2 8b4710               mov     eax, dword ptr [edi + 0x10]
004C7DE5 03c6                 add     eax, esi
004C7DE7 8b00                 mov     eax, dword ptr [eax]
004C7DE9 8b10                 mov     edx, dword ptr [eax]
004C7DEB 8bc8                 mov     ecx, eax
004C7DED ff5230               call    dword ptr [edx + 0x30]
004C7DF0 3d21010000           cmp     eax, 0x121
004C7DF5 0f84ad000000         je      0x4c7ea8
004C7DFB 813dd4c4b50090010000 cmp     dword ptr [0xb5c4d4], 0x190
004C7E05 0f839d000000         jae     0x4c7ea8
004C7E0B 8b4710               mov     eax, dword ptr [edi + 0x10]
004C7E0E 03c6                 add     eax, esi
004C7E10 8b00                 mov     eax, dword ptr [eax]
004C7E12 8b10                 mov     edx, dword ptr [eax]
004C7E14 6a01                 push    1
004C7E16 8bc8                 mov     ecx, eax
004C7E18 ff522c               call    dword ptr [edx + 0x2c]
004C7E1B 84c0                 test    al, al
004C7E1D 7416                 je      0x4c7e35
004C7E1F 8b4710               mov     eax, dword ptr [edi + 0x10]
004C7E22 03c6                 add     eax, esi
004C7E24 8b00                 mov     eax, dword ptr [eax]
004C7E26 8d88a4010000         lea     ecx, [eax + 0x1a4]
004C7E2C e8bf423e00           call    0x8ac0f0
004C7E31 8bd8                 mov     ebx, eax
004C7E33 eb05                 jmp     0x4c7e3a
004C7E35 bb48879900           mov     ebx, 0x998748
004C7E3A 8b4710               mov     eax, dword ptr [edi + 0x10]
004C7E3D 03c6                 add     eax, esi
004C7E3F 8b00                 mov     eax, dword ptr [eax]
004C7E41 8b10                 mov     edx, dword ptr [eax]
004C7E43 6a01                 push    1
004C7E45 8bc8                 mov     ecx, eax
004C7E47 ff522c               call    dword ptr [edx + 0x2c]
004C7E4A 84c0                 test    al, al
004C7E4C 740f                 je      0x4c7e5d
004C7E4E 8b4710               mov     eax, dword ptr [edi + 0x10]
004C7E51 03c6                 add     eax, esi
004C7E53 8b00                 mov     eax, dword ptr [eax]
004C7E55 8b88a0010000         mov     ecx, dword ptr [eax + 0x1a0]
004C7E5B eb03                 jmp     0x4c7e60
004C7E5D 83c9ff               or      ecx, 0xffffffff
004C7E60 8b4710               mov     eax, dword ptr [edi + 0x10]
004C7E63 03c6                 add     eax, esi
004C7E65 8b00                 mov     eax, dword ptr [eax]
004C7E67 8b10                 mov     edx, dword ptr [eax]
004C7E69 53                   push    ebx
004C7E6A 51                   push    ecx
004C7E6B 8bc8                 mov     ecx, eax
004C7E6D ff5230               call    dword ptr [edx + 0x30]
004C7E70 50                   push    eax
004C7E71 8b4518               mov     eax, dword ptr [ebp + 0x18]
004C7E74 50                   push    eax
004C7E75 68a4fd9900           push    0x99fda4
004C7E7A 6a00                 push    0
004C7E7C e82f3c3e00           call    0x8abab0
004C7E81 8bc8                 mov     ecx, eax
004C7E83 83c418               add     esp, 0x18
004C7E86 8d7101               lea     esi, [ecx + 1]
004C7E89 8da42400000000       lea     esp, [esp]
004C7E90 8a11                 mov     dl, byte ptr [ecx]
004C7E92 41                   inc     ecx
004C7E93 84d2                 test    dl, dl
004C7E95 75f9                 jne     0x4c7e90
004C7E97 2bce                 sub     ecx, esi
004C7E99 51                   push    ecx
004C7E9A 50                   push    eax
004C7E9B b9c0c4b500           mov     ecx, 0xb5c4c0
004C7EA0 e8bb4ef4ff           call    0x40cd60
004C7EA5 8b5d18               mov     ebx, dword ptr [ebp + 0x18]
004C7EA8 43                   inc     ebx
004C7EA9 e9f9feffff           jmp     0x4c7da7
004C7EAE 813dd4c4b5009a010000 cmp     dword ptr [0xb5c4d4], 0x19a
004C7EB8 7311                 jae     0x4c7ecb
004C7EBA 6a08                 push    8
004C7EBC 6898fd9900           push    0x99fd98
004C7EC1 b9c0c4b500           mov     ecx, 0xb5c4c0
004C7EC6 e8954ef4ff           call    0x40cd60
004C7ECB 5b                   pop     ebx
004C7ECC 5f                   pop     edi
004C7ECD 5e                   pop     esi
004C7ECE 8be5                 mov     esp, ebp
004C7ED0 5d                   pop     ebp
004C7ED1 c21400               ret     0x14

register_party_user_combat_objects_complete: 0x008A4F00, size 84
008A4F00 56                   push    esi
008A4F01 57                   push    edi
008A4F02 8bf9                 mov     edi, ecx
008A4F04 33f6                 xor     esi, esi
008A4F06 eb08                 jmp     0x8a4f10
008A4F08 8da42400000000       lea     esp, [esp]
008A4F0F 90                   nop     
008A4F10 8b0da85ab700         mov     ecx, dword ptr [0xb75aa8]
008A4F16 56                   push    esi
008A4F17 e88424c6ff           call    0x5073a0
008A4F1C 85c0                 test    eax, eax
008A4F1E 742b                 je      0x8a4f4b
008A4F20 8b8810010000         mov     ecx, dword ptr [eax + 0x110]
008A4F26 85c9                 test    ecx, ecx
008A4F28 7421                 je      0x8a4f4b
008A4F2A 8a9170220000         mov     dl, byte ptr [ecx + 0x2270]
008A4F30 84d2                 test    dl, dl
008A4F32 7517                 jne     0x8a4f4b
008A4F34 0fb74004             movzx   eax, word ptr [eax + 4]
008A4F38 6a00                 push    0
008A4F3A 6a00                 push    0
008A4F3C 51                   push    ecx
008A4F3D 8b4f1c               mov     ecx, dword ptr [edi + 0x1c]
008A4F40 50                   push    eax
008A4F41 6811010000           push    0x111
008A4F46 e8952bc2ff           call    0x4c7ae0
008A4F4B 46                   inc     esi
008A4F4C 83fe08               cmp     esi, 8
008A4F4F 7cbf                 jl      0x8a4f10
008A4F51 5f                   pop     edi
008A4F52 5e                   pop     esi
008A4F53 c3                   ret     

register_user_combat_object_call_window: 0x008A65E8, size 37
008A65E8 8bb710010000         mov     esi, dword ptr [edi + 0x110]
008A65EE 85f6                 test    esi, esi
008A65F0 0f84ab000000         je      0x8a66a1
008A65F6 0fb74704             movzx   eax, word ptr [edi + 4]
008A65FA 8b4b1c               mov     ecx, dword ptr [ebx + 0x1c]
008A65FD 6a00                 push    0
008A65FF 6a00                 push    0
008A6601 56                   push    esi
008A6602 50                   push    eax
008A6603 6811010000           push    0x111
008A6608 e8d314c2ff           call    0x4c7ae0

native_ui_user_to_mp_calls_window: 0x007C86A0, size 1933
007C86A0 55                   push    ebp
007C86A1 8bec                 mov     ebp, esp
007C86A3 83ec68               sub     esp, 0x68
007C86A6 53                   push    ebx
007C86A7 56                   push    esi
007C86A8 8bf1                 mov     esi, ecx
007C86AA 8b0db8dcb500         mov     ecx, dword ptr [0xb5dcb8]
007C86B0 33c0                 xor     eax, eax
007C86B2 668b4648             mov     ax, word ptr [esi + 0x48]
007C86B6 57                   push    edi
007C86B7 8b3d145ab700         mov     edi, dword ptr [0xb75a14]
007C86BD 50                   push    eax
007C86BE e8ed56d3ff           call    0x4fddb0
007C86C3 85c0                 test    eax, eax
007C86C5 894638               mov     dword ptr [esi + 0x38], eax
007C86C8 0f846c0f0000         je      0x7c963a
007C86CE 8b8810010000         mov     ecx, dword ptr [eax + 0x110]
007C86D4 85c9                 test    ecx, ecx
007C86D6 894e34               mov     dword ptr [esi + 0x34], ecx
007C86D9 0f845b0f0000         je      0x7c963a
007C86DF 8b880c010000         mov     ecx, dword ptr [eax + 0x10c]
007C86E5 85c9                 test    ecx, ecx
007C86E7 7405                 je      0x7c86ee
007C86E9 894e6c               mov     dword ptr [esi + 0x6c], ecx
007C86EC eb0a                 jmp     0x7c86f8
007C86EE 8bc8                 mov     ecx, eax
007C86F0 e87b53d3ff           call    0x4fda70
007C86F5 89466c               mov     dword ptr [esi + 0x6c], eax
007C86F8 8b466c               mov     eax, dword ptr [esi + 0x6c]
007C86FB 85c0                 test    eax, eax
007C86FD 751a                 jne     0x7c8719
007C86FF 833d3435b50013       cmp     dword ptr [0xb53534], 0x13
007C8706 750a                 jne     0x7c8712
007C8708 8b4e10               mov     ecx, dword ptr [esi + 0x10]
007C870B 6a22                 push    0x22
007C870D e80e17fdff           call    0x799e20
007C8712 8b06                 mov     eax, dword ptr [esi]
007C8714 8bce                 mov     ecx, esi
007C8716 ff5028               call    dword ptr [eax + 0x28]
007C8719 8b4508               mov     eax, dword ptr [ebp + 8]
007C871C 3b4608               cmp     eax, dword ptr [esi + 8]
007C871F 0f852f0f0000         jne     0x7c9654
007C8725 8b4818               mov     ecx, dword ptr [eax + 0x18]
007C8728 8b5814               mov     ebx, dword ptr [eax + 0x14]
007C872B 894df8               mov     dword ptr [ebp - 8], ecx
007C872E 8b4e68               mov     ecx, dword ptr [esi + 0x68]
007C8731 6a04                 push    4
007C8733 e818ba1100           call    0x8e4150
007C8738 85c0                 test    eax, eax
007C873A 7434                 je      0x7c8770
007C873C 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8742 8b10                 mov     edx, dword ptr [eax]
007C8744 680000804f           push    0x4f800000
007C8749 680000804f           push    0x4f800000
007C874E 6aff                 push    -1
007C8750 6a00                 push    0
007C8752 680000803f           push    0x3f800000
007C8757 680000803f           push    0x3f800000
007C875C 51                   push    ecx
007C875D 8b4df8               mov     ecx, dword ptr [ebp - 8]
007C8760 83c132               add     ecx, 0x32
007C8763 51                   push    ecx
007C8764 8d4b15               lea     ecx, [ebx + 0x15]
007C8767 51                   push    ecx
007C8768 8bc8                 mov     ecx, eax
007C876A ff9298000000         call    dword ptr [edx + 0x98]
007C8770 8b466c               mov     eax, dword ptr [esi + 0x6c]
007C8773 85c0                 test    eax, eax
007C8775 0f84a4000000         je      0x7c881f
007C877B 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C877E 85c9                 test    ecx, ecx
007C8780 0f8499000000         je      0x7c881f
007C8786 8b11                 mov     edx, dword ptr [ecx]
007C8788 ff928c040000         call    dword ptr [edx + 0x48c]
007C878E 85c0                 test    eax, eax
007C8790 0f8489000000         je      0x7c881f
007C8796 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C8799 8b01                 mov     eax, dword ptr [ecx]
007C879B ff908c040000         call    dword ptr [eax + 0x48c]
007C87A1 833800               cmp     dword ptr [eax], 0
007C87A4 7515                 jne     0x7c87bb
007C87A6 8b45f8               mov     eax, dword ptr [ebp - 8]
007C87A9 6a00                 push    0
007C87AB 68c8000000           push    0xc8
007C87B0 83c0e2               add     eax, -0x1e
007C87B3 6a64                 push    0x64
007C87B5 50                   push    eax
007C87B6 8d430d               lea     eax, [ebx + 0xd]
007C87B9 eb58                 jmp     0x7c8813
007C87BB 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C87BE 8b11                 mov     edx, dword ptr [ecx]
007C87C0 ff928c040000         call    dword ptr [edx + 0x48c]
007C87C6 833801               cmp     dword ptr [eax], 1
007C87C9 7521                 jne     0x7c87ec
007C87CB 8b55f8               mov     edx, dword ptr [ebp - 8]
007C87CE 8b4e6c               mov     ecx, dword ptr [esi + 0x6c]
007C87D1 8b01                 mov     eax, dword ptr [ecx]
007C87D3 6a00                 push    0
007C87D5 68c8000000           push    0xc8
007C87DA 6a64                 push    0x64
007C87DC 83c2dd               add     edx, -0x23
007C87DF 52                   push    edx
007C87E0 8d530a               lea     edx, [ebx + 0xa]
007C87E3 52                   push    edx
007C87E4 ff9084010000         call    dword ptr [eax + 0x184]
007C87EA eb33                 jmp     0x7c881f
007C87EC 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C87EF 8b01                 mov     eax, dword ptr [ecx]
007C87F1 ff908c040000         call    dword ptr [eax + 0x48c]
007C87F7 833802               cmp     dword ptr [eax], 2
007C87FA 0f8536010000         jne     0x7c8936
007C8800 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8803 83c0dd               add     eax, -0x23
007C8806 6a00                 push    0
007C8808 68c8000000           push    0xc8
007C880D 6a64                 push    0x64
007C880F 50                   push    eax
007C8810 8d430f               lea     eax, [ebx + 0xf]
007C8813 8b4e6c               mov     ecx, dword ptr [esi + 0x6c]
007C8816 8b11                 mov     edx, dword ptr [ecx]
007C8818 50                   push    eax
007C8819 ff9284010000         call    dword ptr [edx + 0x184]
007C881F 8b4e68               mov     ecx, dword ptr [esi + 0x68]
007C8822 6a06                 push    6
007C8824 e827b91100           call    0x8e4150
007C8829 85c0                 test    eax, eax
007C882B 742e                 je      0x7c885b
007C882D 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8833 8b10                 mov     edx, dword ptr [eax]
007C8835 680000804f           push    0x4f800000
007C883A 680000804f           push    0x4f800000
007C883F 6aff                 push    -1
007C8841 6a00                 push    0
007C8843 680000803f           push    0x3f800000
007C8848 680000803f           push    0x3f800000
007C884D 51                   push    ecx
007C884E 8b4df8               mov     ecx, dword ptr [ebp - 8]
007C8851 51                   push    ecx
007C8852 53                   push    ebx
007C8853 8bc8                 mov     ecx, eax
007C8855 ff9298000000         call    dword ptr [edx + 0x98]
007C885B 8b4e68               mov     ecx, dword ptr [esi + 0x68]
007C885E 6a02                 push    2
007C8860 e8ebb81100           call    0x8e4150
007C8865 85c0                 test    eax, eax
007C8867 7434                 je      0x7c889d
007C8869 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C886F 8b10                 mov     edx, dword ptr [eax]
007C8871 680000804f           push    0x4f800000
007C8876 680000804f           push    0x4f800000
007C887B 6aff                 push    -1
007C887D 6a00                 push    0
007C887F 680000803f           push    0x3f800000
007C8884 680000803f           push    0x3f800000
007C8889 51                   push    ecx
007C888A 8b4df8               mov     ecx, dword ptr [ebp - 8]
007C888D 83c10e               add     ecx, 0xe
007C8890 51                   push    ecx
007C8891 8d4b0e               lea     ecx, [ebx + 0xe]
007C8894 51                   push    ecx
007C8895 8bc8                 mov     ecx, eax
007C8897 ff9298000000         call    dword ptr [edx + 0x98]
007C889D 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C88A0 8b11                 mov     edx, dword ptr [ecx]
007C88A2 6a01                 push    1
007C88A4 ff92e0020000         call    dword ptr [edx + 0x2e0]
007C88AA 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C88AD 8945e4               mov     dword ptr [ebp - 0x1c], eax
007C88B0 8b01                 mov     eax, dword ptr [ecx]
007C88B2 6a03                 push    3
007C88B4 ff90e0020000         call    dword ptr [eax + 0x2e0]
007C88BA 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C88C0 8b11                 mov     edx, dword ptr [ecx]
007C88C2 6844d5af00           push    0xafd544
007C88C7 8945f4               mov     dword ptr [ebp - 0xc], eax
007C88CA ff524c               call    dword ptr [edx + 0x4c]
007C88CD 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C88D3 8b01                 mov     eax, dword ptr [ecx]
007C88D5 8d563c               lea     edx, [esi + 0x3c]
007C88D8 52                   push    edx
007C88D9 ff5048               call    dword ptr [eax + 0x48]
007C88DC 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C88E2 8b09                 mov     ecx, dword ptr [ecx]
007C88E4 8b4634               mov     eax, dword ptr [esi + 0x34]
007C88E7 83eb78               sub     ebx, 0x78
007C88EA 895df0               mov     dword ptr [ebp - 0x10], ebx
007C88ED 894dd8               mov     dword ptr [ebp - 0x28], ecx
007C88F0 83c37e               add     ebx, 0x7e
007C88F3 8d88a4010000         lea     ecx, [eax + 0x1a4]
007C88F9 895dec               mov     dword ptr [ebp - 0x14], ebx
007C88FC e8ef370e00           call    0x8ac0f0
007C8901 8b5df8               mov     ebx, dword ptr [ebp - 8]
007C8904 8b55ec               mov     edx, dword ptr [ebp - 0x14]
007C8907 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C890D 50                   push    eax
007C890E 8b45d8               mov     eax, dword ptr [ebp - 0x28]
007C8911 6aff                 push    -1
007C8913 83c37e               add     ebx, 0x7e
007C8916 53                   push    ebx
007C8917 52                   push    edx
007C8918 ff5054               call    dword ptr [eax + 0x54]
007C891B 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C891E 8b01                 mov     eax, dword ptr [ecx]
007C8920 ff908c040000         call    dword ptr [eax + 0x48c]
007C8926 8b4828               mov     ecx, dword ptr [eax + 0x28]
007C8929 83c010               add     eax, 0x10
007C892C 83f910               cmp     ecx, 0x10
007C892F 7267                 jb      0x7c8998
007C8931 8b4004               mov     eax, dword ptr [eax + 4]
007C8934 eb65                 jmp     0x7c899b
007C8936 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C8939 8b11                 mov     edx, dword ptr [ecx]
007C893B ff928c040000         call    dword ptr [edx + 0x48c]
007C8941 833803               cmp     dword ptr [eax], 3
007C8944 7524                 jne     0x7c896a
007C8946 8b55f8               mov     edx, dword ptr [ebp - 8]
007C8949 8b4e6c               mov     ecx, dword ptr [esi + 0x6c]
007C894C 8b01                 mov     eax, dword ptr [ecx]
007C894E 6a00                 push    0
007C8950 68c8000000           push    0xc8
007C8955 6a64                 push    0x64
007C8957 83c2e5               add     edx, -0x1b
007C895A 52                   push    edx
007C895B 8d5311               lea     edx, [ebx + 0x11]
007C895E 52                   push    edx
007C895F ff9084010000         call    dword ptr [eax + 0x184]
007C8965 e9b5feffff           jmp     0x7c881f
007C896A 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C896D 8b01                 mov     eax, dword ptr [ecx]
007C896F ff908c040000         call    dword ptr [eax + 0x48c]
007C8975 8b08                 mov     ecx, dword ptr [eax]
007C8977 8b45f8               mov     eax, dword ptr [ebp - 8]
007C897A 83c0e5               add     eax, -0x1b
007C897D 83f904               cmp     ecx, 4
007C8980 0f8580feffff         jne     0x7c8806
007C8986 6a00                 push    0
007C8988 68c8000000           push    0xc8
007C898D 6a64                 push    0x64
007C898F 50                   push    eax
007C8990 8d4311               lea     eax, [ebx + 0x11]
007C8993 e97bfeffff           jmp     0x7c8813
007C8998 83c004               add     eax, 4
007C899B 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C89A1 8b09                 mov     ecx, dword ptr [ecx]
007C89A3 8b55f0               mov     edx, dword ptr [ebp - 0x10]
007C89A6 50                   push    eax
007C89A7 6aff                 push    -1
007C89A9 81c2fe000000         add     edx, 0xfe
007C89AF 53                   push    ebx
007C89B0 8bc1                 mov     eax, ecx
007C89B2 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C89B8 52                   push    edx
007C89B9 ff5054               call    dword ptr [eax + 0x54]
007C89BC 8b5634               mov     edx, dword ptr [esi + 0x34]
007C89BF a14816bf00           mov     eax, dword ptr [0xbf1648]
007C89C4 8b18                 mov     ebx, dword ptr [eax]
007C89C6 8bca                 mov     ecx, edx
007C89C8 8955e8               mov     dword ptr [ebp - 0x18], edx
007C89CB 8b11                 mov     edx, dword ptr [ecx]
007C89CD 894de8               mov     dword ptr [ebp - 0x18], ecx
007C89D0 ff928c040000         call    dword ptr [edx + 0x48c]
007C89D6 8bc8                 mov     ecx, eax
007C89D8 83c134               add     ecx, 0x34
007C89DB e85096c3ff           call    0x402030
007C89E0 8b4de8               mov     ecx, dword ptr [ebp - 0x18]
007C89E3 50                   push    eax
007C89E4 8b01                 mov     eax, dword ptr [ecx]
007C89E6 ff908c040000         call    dword ptr [eax + 0x48c]
007C89EC 8bc8                 mov     ecx, eax
007C89EE 83c12c               add     ecx, 0x2c
007C89F1 e83a96c3ff           call    0x402030
007C89F6 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C89FC 50                   push    eax
007C89FD 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8A00 68b400a800           push    0xa800b4
007C8A05 0593000000           add     eax, 0x93
007C8A0A 6aff                 push    -1
007C8A0C 50                   push    eax
007C8A0D 8b45f0               mov     eax, dword ptr [ebp - 0x10]
007C8A10 05fe000000           add     eax, 0xfe
007C8A15 50                   push    eax
007C8A16 51                   push    ecx
007C8A17 ff5358               call    dword ptr [ebx + 0x58]
007C8A1A 8b5e34               mov     ebx, dword ptr [esi + 0x34]
007C8A1D 8b13                 mov     edx, dword ptr [ebx]
007C8A1F 83c41c               add     esp, 0x1c
007C8A22 8bcb                 mov     ecx, ebx
007C8A24 ff928c040000         call    dword ptr [edx + 0x48c]
007C8A2A 8bc8                 mov     ecx, eax
007C8A2C 83c134               add     ecx, 0x34
007C8A2F e8fc95c3ff           call    0x402030
007C8A34 50                   push    eax
007C8A35 8b03                 mov     eax, dword ptr [ebx]
007C8A37 8bcb                 mov     ecx, ebx
007C8A39 ff908c040000         call    dword ptr [eax + 0x48c]
007C8A3F 8bc8                 mov     ecx, eax
007C8A41 83c12c               add     ecx, 0x2c
007C8A44 e8e795c3ff           call    0x402030
007C8A49 50                   push    eax
007C8A4A 68a800a800           push    0xa800a8
007C8A4F 8d4d98               lea     ecx, [ebp - 0x68]
007C8A52 6a40                 push    0x40
007C8A54 51                   push    ecx
007C8A55 e856b30e00           call    0x8b3db0
007C8A5A 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8A60 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C8A63 8b02                 mov     eax, dword ptr [edx]
007C8A65 8b11                 mov     edx, dword ptr [ecx]
007C8A67 83c414               add     esp, 0x14
007C8A6A 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8A6D ff9298010000         call    dword ptr [edx + 0x198]
007C8A73 8b0d84b3b500         mov     ecx, dword ptr [0xb5b384]
007C8A79 50                   push    eax
007C8A7A 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8A7D 6814dea700           push    0xa7de14
007C8A82 6aff                 push    -1
007C8A84 0593000000           add     eax, 0x93
007C8A89 50                   push    eax
007C8A8A 8b01                 mov     eax, dword ptr [ecx]
007C8A8C 6aff                 push    -1
007C8A8E 8d5598               lea     edx, [ebp - 0x68]
007C8A91 52                   push    edx
007C8A92 ff5020               call    dword ptr [eax + 0x20]
007C8A95 8b5df0               mov     ebx, dword ptr [ebp - 0x10]
007C8A98 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8A9E 8bcb                 mov     ecx, ebx
007C8AA0 2bc8                 sub     ecx, eax
007C8AA2 8b45d8               mov     eax, dword ptr [ebp - 0x28]
007C8AA5 81c1fd000000         add     ecx, 0xfd
007C8AAB 51                   push    ecx
007C8AAC 52                   push    edx
007C8AAD ff5058               call    dword ptr [eax + 0x58]
007C8AB0 8b4638               mov     eax, dword ptr [esi + 0x38]
007C8AB3 83c418               add     esp, 0x18
007C8AB6 85c0                 test    eax, eax
007C8AB8 0f8466010000         je      0x7c8c24
007C8ABE 8d4810               lea     ecx, [eax + 0x10]
007C8AC1 e89a49d3ff           call    0x4fd460
007C8AC6 83ec08               sub     esp, 8
007C8AC9 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8ACC 8d45d8               lea     eax, [ebp - 0x28]
007C8ACF 8bcc                 mov     ecx, esp
007C8AD1 8965e8               mov     dword ptr [ebp - 0x18], esp
007C8AD4 50                   push    eax
007C8AD5 e8163fc4ff           call    0x40c9f0
007C8ADA 8bce                 mov     ecx, esi
007C8ADC e86ff9ffff           call    0x7c8450
007C8AE1 8b4e38               mov     ecx, dword ptr [esi + 0x38]
007C8AE4 83c110               add     ecx, 0x10
007C8AE7 894660               mov     dword ptr [esi + 0x60], eax
007C8AEA e87149d3ff           call    0x4fd460
007C8AEF 83ec08               sub     esp, 8
007C8AF2 8d55d8               lea     edx, [ebp - 0x28]
007C8AF5 8bcc                 mov     ecx, esp
007C8AF7 8965e8               mov     dword ptr [ebp - 0x18], esp
007C8AFA 52                   push    edx
007C8AFB 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8AFE e8ed3ec4ff           call    0x40c9f0
007C8B03 8bce                 mov     ecx, esi
007C8B05 e8c6f9ffff           call    0x7c84d0
007C8B0A 894664               mov     dword ptr [esi + 0x64], eax
007C8B0D 8b4660               mov     eax, dword ptr [esi + 0x60]
007C8B10 85c0                 test    eax, eax
007C8B12 741c                 je      0x7c8b30
007C8B14 50                   push    eax
007C8B15 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8B18 05a5000000           add     eax, 0xa5
007C8B1D 50                   push    eax
007C8B1E 8d8bc8000000         lea     ecx, [ebx + 0xc8]
007C8B24 51                   push    ecx
007C8B25 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8B2B e8e08ac3ff           call    0x401610
007C8B30 8b4664               mov     eax, dword ptr [esi + 0x64]
007C8B33 85c0                 test    eax, eax
007C8B35 741d                 je      0x7c8b54
007C8B37 8b55f8               mov     edx, dword ptr [ebp - 8]
007C8B3A 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8B40 50                   push    eax
007C8B41 81c2a5000000         add     edx, 0xa5
007C8B47 52                   push    edx
007C8B48 8d83c8000000         lea     eax, [ebx + 0xc8]
007C8B4E 50                   push    eax
007C8B4F e8bc8ac3ff           call    0x401610
007C8B54 8b4e54               mov     ecx, dword ptr [esi + 0x54]
007C8B57 8b4650               mov     eax, dword ptr [esi + 0x50]
007C8B5A 894dfc               mov     dword ptr [ebp - 4], ecx
007C8B5D 03c8                 add     ecx, eax
007C8B5F 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8B62 0f84bc000000         je      0x7c8c24
007C8B68 db45d8               fild    dword ptr [ebp - 0x28]
007C8B6B 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8B6E db45fc               fild    dword ptr [ebp - 4]
007C8B71 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8B77 8b11                 mov     edx, dword ptr [ecx]
007C8B79 83ec08               sub     esp, 8
007C8B7C d8c1                 fadd    st(1)
007C8B7E 05a8000000           add     eax, 0xa8
007C8B83 81c3ff000000         add     ebx, 0xff
007C8B89 d8f9                 fdivr   st(1)
007C8B8B d80db4989900         fmul    dword ptr [0x9998b4]
007C8B91 d95de8               fstp    dword ptr [ebp - 0x18]
007C8B94 ddd8                 fstp    st(0)
007C8B96 d945e8               fld     dword ptr [ebp - 0x18]
007C8B99 dd1c24               fstp    qword ptr [esp]
007C8B9C 6a02                 push    2
007C8B9E 689c00a800           push    0xa8009c
007C8BA3 6aff                 push    -1
007C8BA5 50                   push    eax
007C8BA6 53                   push    ebx
007C8BA7 51                   push    ecx
007C8BA8 ff5258               call    dword ptr [edx + 0x58]
007C8BAB d945e8               fld     dword ptr [ebp - 0x18]
007C8BAE 83c418               add     esp, 0x18
007C8BB1 dd1c24               fstp    qword ptr [esp]
007C8BB4 6a02                 push    2
007C8BB6 689c00a800           push    0xa8009c
007C8BBB 8d4598               lea     eax, [ebp - 0x68]
007C8BBE 6a40                 push    0x40
007C8BC0 50                   push    eax
007C8BC1 e8eab10e00           call    0x8b3db0
007C8BC6 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8BCC 8b01                 mov     eax, dword ptr [ecx]
007C8BCE 8b55fc               mov     edx, dword ptr [ebp - 4]
007C8BD1 8b4dd8               mov     ecx, dword ptr [ebp - 0x28]
007C8BD4 83c418               add     esp, 0x18
007C8BD7 52                   push    edx
007C8BD8 51                   push    ecx
007C8BD9 68f3010000           push    0x1f3
007C8BDE 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8BE1 e8ba190e00           call    0x8aa5a0
007C8BE6 8b0d84b3b500         mov     ecx, dword ptr [0xb5b384]
007C8BEC 8b11                 mov     edx, dword ptr [ecx]
007C8BEE 83c404               add     esp, 4
007C8BF1 50                   push    eax
007C8BF2 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8BF5 6aff                 push    -1
007C8BF7 05a8000000           add     eax, 0xa8
007C8BFC 50                   push    eax
007C8BFD 6aff                 push    -1
007C8BFF 8d4598               lea     eax, [ebp - 0x68]
007C8C02 50                   push    eax
007C8C03 ff5220               call    dword ptr [edx + 0x20]
007C8C06 8b5df0               mov     ebx, dword ptr [ebp - 0x10]
007C8C09 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8C0F 8bcb                 mov     ecx, ebx
007C8C11 2bc8                 sub     ecx, eax
007C8C13 8b45d8               mov     eax, dword ptr [ebp - 0x28]
007C8C16 81c1fd000000         add     ecx, 0xfd
007C8C1C 51                   push    ecx
007C8C1D 52                   push    edx
007C8C1E ff5058               call    dword ptr [eax + 0x58]
007C8C21 83c41c               add     esp, 0x1c
007C8C24 8b4638               mov     eax, dword ptr [esi + 0x38]
007C8C27 d905842ca900         fld     dword ptr [0xa92c84]
007C8C2D 8b88dc000000         mov     ecx, dword ptr [eax + 0xdc]
007C8C33 85c9                 test    ecx, ecx
007C8C35 c745fc00000000       mov     dword ptr [ebp - 4], 0
007C8C3C 742d                 je      0x7c8c6b
007C8C3E 8b80e0000000         mov     eax, dword ptr [eax + 0xe0]
007C8C44 ddd8                 fstp    st(0)
007C8C46 6bc064               imul    eax, eax, 0x64
007C8C49 85c0                 test    eax, eax
007C8C4B 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8C4E db45d8               fild    dword ptr [ebp - 0x28]
007C8C51 7d06                 jge     0x7c8c59
007C8C53 d805b4a69900         fadd    dword ptr [0x99a6b4]
007C8C59 85c9                 test    ecx, ecx
007C8C5B 894dd8               mov     dword ptr [ebp - 0x28], ecx
007C8C5E db45d8               fild    dword ptr [ebp - 0x28]
007C8C61 7d06                 jge     0x7c8c69
007C8C63 d805b4a69900         fadd    dword ptr [0x99a6b4]
007C8C69 def9                 fdivp   st(1)
007C8C6B 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8C6E 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8C74 8b11                 mov     edx, dword ptr [ecx]
007C8C76 83ec08               sub     esp, 8
007C8C79 dd1c24               fstp    qword ptr [esp]
007C8C7C 6a02                 push    2
007C8C7E 689400a800           push    0xa80094
007C8C83 6aff                 push    -1
007C8C85 05bd000000           add     eax, 0xbd
007C8C8A 50                   push    eax
007C8C8B 8d83fe000000         lea     eax, [ebx + 0xfe]
007C8C91 50                   push    eax
007C8C92 51                   push    ecx
007C8C93 ff5258               call    dword ptr [edx + 0x58]
007C8C96 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C8C99 83c420               add     esp, 0x20
007C8C9C e8efafcdff           call    0x4a3c90
007C8CA1 8b17                 mov     edx, dword ptr [edi]
007C8CA3 50                   push    eax
007C8CA4 8bcf                 mov     ecx, edi
007C8CA6 ff9234030000         call    dword ptr [edx + 0x334]
007C8CAC 50                   push    eax
007C8CAD 8d45fc               lea     eax, [ebp - 4]
007C8CB0 50                   push    eax
007C8CB1 e8baf5ffff           call    0x7c8270
007C8CB6 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C8CB9 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8CBF 8b01                 mov     eax, dword ptr [ecx]
007C8CC1 8b1a                 mov     ebx, dword ptr [edx]
007C8CC3 894de8               mov     dword ptr [ebp - 0x18], ecx
007C8CC6 83c40c               add     esp, 0xc
007C8CC9 894de8               mov     dword ptr [ebp - 0x18], ecx
007C8CCC ff9034030000         call    dword ptr [eax + 0x334]
007C8CD2 8b4de8               mov     ecx, dword ptr [ebp - 0x18]
007C8CD5 8b11                 mov     edx, dword ptr [ecx]
007C8CD7 50                   push    eax
007C8CD8 ff9230030000         call    dword ptr [edx + 0x330]
007C8CDE 8b4dec               mov     ecx, dword ptr [ebp - 0x14]
007C8CE1 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8CE7 50                   push    eax
007C8CE8 8b45fc               mov     eax, dword ptr [ebp - 4]
007C8CEB 680cdea700           push    0xa7de0c
007C8CF0 50                   push    eax
007C8CF1 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8CF4 05d6000000           add     eax, 0xd6
007C8CF9 50                   push    eax
007C8CFA 51                   push    ecx
007C8CFB 52                   push    edx
007C8CFC ff5358               call    dword ptr [ebx + 0x58]
007C8CFF 8b5df4               mov     ebx, dword ptr [ebp - 0xc]
007C8D02 83c41c               add     esp, 0x1c
007C8D05 8bcb                 mov     ecx, ebx
007C8D07 e814cacbff           call    0x485720
007C8D0C 50                   push    eax
007C8D0D 8b07                 mov     eax, dword ptr [edi]
007C8D0F 8bcf                 mov     ecx, edi
007C8D11 ff909c010000         call    dword ptr [eax + 0x19c]
007C8D17 50                   push    eax
007C8D18 8d4dfc               lea     ecx, [ebp - 4]
007C8D1B 51                   push    ecx
007C8D1C e84ff5ffff           call    0x7c8270
007C8D21 83c40c               add     esp, 0xc
007C8D24 8bcb                 mov     ecx, ebx
007C8D26 e8f5c9cbff           call    0x485720
007C8D2B 8b5634               mov     edx, dword ptr [esi + 0x34]
007C8D2E 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8D31 db45d8               fild    dword ptr [ebp - 0x28]
007C8D34 6a03                 push    3
007C8D36 52                   push    edx
007C8D37 d95dd8               fstp    dword ptr [ebp - 0x28]
007C8D3A e851b4c6ff           call    0x434190
007C8D3F d84dd8               fmul    dword ptr [ebp - 0x28]
007C8D42 a14816bf00           mov     eax, dword ptr [0xbf1648]
007C8D47 8b18                 mov     ebx, dword ptr [eax]
007C8D49 e8ba291500           call    0x91b708
007C8D4E 8b4dfc               mov     ecx, dword ptr [ebp - 4]
007C8D51 50                   push    eax
007C8D52 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8D55 6814d29900           push    0x99d214
007C8D5A 51                   push    ecx
007C8D5B 05ed000000           add     eax, 0xed
007C8D60 8b55ec               mov     edx, dword ptr [ebp - 0x14]
007C8D63 50                   push    eax
007C8D64 a14816bf00           mov     eax, dword ptr [0xbf1648]
007C8D69 52                   push    edx
007C8D6A 50                   push    eax
007C8D6B ff5358               call    dword ptr [ebx + 0x58]
007C8D6E 8b5df4               mov     ebx, dword ptr [ebp - 0xc]
007C8D71 83c420               add     esp, 0x20
007C8D74 8bcb                 mov     ecx, ebx
007C8D76 e8657dccff           call    0x490ae0
007C8D7B 8b17                 mov     edx, dword ptr [edi]
007C8D7D 50                   push    eax
007C8D7E 6a03                 push    3
007C8D80 8bcf                 mov     ecx, edi
007C8D82 ff92e0020000         call    dword ptr [edx + 0x2e0]
007C8D88 8bc8                 mov     ecx, eax
007C8D8A e8517dccff           call    0x490ae0
007C8D8F 50                   push    eax
007C8D90 8d45fc               lea     eax, [ebp - 4]
007C8D93 50                   push    eax
007C8D94 e8d7f4ffff           call    0x7c8270
007C8D99 83c40c               add     esp, 0xc
007C8D9C 8bcb                 mov     ecx, ebx
007C8D9E e83d7dccff           call    0x490ae0
007C8DA3 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C8DA6 8945d8               mov     dword ptr [ebp - 0x28], eax
007C8DA9 db45d8               fild    dword ptr [ebp - 0x28]
007C8DAC 6a04                 push    4
007C8DAE 51                   push    ecx
007C8DAF d95dd8               fstp    dword ptr [ebp - 0x28]
007C8DB2 e8d9b3c6ff           call    0x434190
007C8DB7 d84dd8               fmul    dword ptr [ebp - 0x28]
007C8DBA 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8DC0 8b1a                 mov     ebx, dword ptr [edx]
007C8DC2 e841291500           call    0x91b708
007C8DC7 8b4dec               mov     ecx, dword ptr [ebp - 0x14]
007C8DCA 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8DD0 50                   push    eax
007C8DD1 8b45fc               mov     eax, dword ptr [ebp - 4]
007C8DD4 6814d29900           push    0x99d214
007C8DD9 50                   push    eax
007C8DDA 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8DDD 0504010000           add     eax, 0x104
007C8DE2 50                   push    eax
007C8DE3 51                   push    ecx
007C8DE4 52                   push    edx
007C8DE5 ff5358               call    dword ptr [ebx + 0x58]
007C8DE8 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C8DEB 83c420               add     esp, 0x20
007C8DEE e8cdaecdff           call    0x4a3cc0
007C8DF3 50                   push    eax
007C8DF4 8b07                 mov     eax, dword ptr [edi]
007C8DF6 8bcf                 mov     ecx, edi
007C8DF8 ff9044030000         call    dword ptr [eax + 0x344]
007C8DFE 50                   push    eax
007C8DFF 8d4dfc               lea     ecx, [ebp - 4]
007C8E02 51                   push    ecx
007C8E03 e868f4ffff           call    0x7c8270
007C8E08 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007C8E0B 8b154816bf00         mov     edx, dword ptr [0xbf1648]
007C8E11 8b01                 mov     eax, dword ptr [ecx]
007C8E13 8b1a                 mov     ebx, dword ptr [edx]
007C8E15 83c40c               add     esp, 0xc
007C8E18 894de8               mov     dword ptr [ebp - 0x18], ecx
007C8E1B ff9044030000         call    dword ptr [eax + 0x344]
007C8E21 8b4de8               mov     ecx, dword ptr [ebp - 0x18]
007C8E24 8b11                 mov     edx, dword ptr [ecx]
007C8E26 50                   push    eax
007C8E27 ff9240030000         call    dword ptr [edx + 0x340]

native_local_command_dispatch_complete: 0x00433AC0, size 24
00433AC0 8b0d145ab700         mov     ecx, dword ptr [0xb75a14]
00433AC6 85c9                 test    ecx, ecx
00433AC8 7408                 je      0x433ad2
00433ACA 8b01                 mov     eax, dword ptr [ecx]
00433ACC ffa0d4050000         jmp     dword ptr [eax + 0x5d4]
00433AD2 b868deb500           mov     eax, 0xb5de68
00433AD7 c3                   ret     

native_local_command_getter_complete: 0x00493770, size 16
00493770 8b8184230000         mov     eax, dword ptr [ecx + 0x2384]
00493776 85c0                 test    eax, eax
00493778 7505                 jne     0x49377f
0049377A b868deb500           mov     eax, 0xb5de68
0049377F c3                   ret     
