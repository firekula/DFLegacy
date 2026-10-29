
RANGE 0041944B-0041947B
0041944B 8b4508 mov eax, dword ptr [ebp + 8]
0041944E 8a4801 mov cl, byte ptr [eax + 1]
00419451 884df3 mov byte ptr [ebp - 0xd], cl
00419454 0fb655f3 movzx edx, byte ptr [ebp - 0xd]
00419458 8995ec9effff mov dword ptr [ebp - 0x6114], edx
0041945E 81bdec9effff97000000 cmp dword ptr [ebp - 0x6114], 0x97
00419468 0f87fb0b0100 ja 0x42a069
0041946E 8b85ec9effff mov eax, dword ptr [ebp - 0x6114]
00419474 ff248581a04200 jmp dword ptr [eax*4 + 0x42a081]

RANGE 0041E878-0041E9C7
0041E878 c6853bedffff00 mov byte ptr [ebp - 0x12c5], 0
0041E87F 66c78530edffff0000 mov word ptr [ebp - 0x12d0], 0
0041E888 66c7853cedffff0000 mov word ptr [ebp - 0x12c4], 0
0041E891 66c78534edffff0000 mov word ptr [ebp - 0x12cc], 0
0041E89A 66c7852cedffff6400 mov word ptr [ebp - 0x12d4], 0x64
0041E8A3 8d8530edffff lea eax, [ebp - 0x12d0]
0041E8A9 50 push eax
0041E8AA e891684d00 call 0x8f5140
0041E8AF 83c404 add esp, 4
0041E8B2 8d8d3cedffff lea ecx, [ebp - 0x12c4]
0041E8B8 51 push ecx
0041E8B9 e882684d00 call 0x8f5140
0041E8BE 83c404 add esp, 4
0041E8C1 8d9534edffff lea edx, [ebp - 0x12cc]
0041E8C7 52 push edx
0041E8C8 e873684d00 call 0x8f5140
0041E8CD 83c404 add esp, 4
0041E8D0 8d853bedffff lea eax, [ebp - 0x12c5]
0041E8D6 50 push eax
0041E8D7 e844684d00 call 0x8f5120
0041E8DC 83c404 add esp, 4
0041E8DF 8d8d2cedffff lea ecx, [ebp - 0x12d4]
0041E8E5 51 push ecx
0041E8E6 e855684d00 call 0x8f5140
0041E8EB 83c404 add esp, 4
0041E8EE 0fb7b530edffff movzx esi, word ptr [ebp - 0x12d0]
0041E8F5 b9405ab700 mov ecx, 0xb75a40
0041E8FA e86161feff call 0x404a60
0041E8FF 3bf0 cmp esi, eax
0041E901 0f84b1000000 je 0x41e9b8
0041E907 668b9530edffff mov dx, word ptr [ebp - 0x12d0]
0041E90E 52 push edx
0041E90F 8b0db8dcb500 mov ecx, dword ptr [0xb5dcb8]
0041E915 e896f40d00 call 0x4fddb0
0041E91A 85c0 test eax, eax
0041E91C 0f8496000000 je 0x41e9b8
0041E922 668b8530edffff mov ax, word ptr [ebp - 0x12d0]
0041E929 50 push eax
0041E92A 8b0db8dcb500 mov ecx, dword ptr [0xb5dcb8]
0041E930 e87bf40d00 call 0x4fddb0
0041E935 8bc8 mov ecx, eax
0041E937 e86491ffff call 0x417aa0
0041E93C 898528edffff mov dword ptr [ebp - 0x12d8], eax
0041E942 8b8df09effff mov ecx, dword ptr [ebp - 0x6110]
0041E948 e8a394ffff call 0x417df0
0041E94D 8bc8 mov ecx, eax
0041E94F e86cc2feff call 0x40abc0
0041E954 8bc8 mov ecx, eax
0041E956 e8255afeff call 0x404380
0041E95B 898524edffff mov dword ptr [ebp - 0x12dc], eax
0041E961 83bd24edffff00 cmp dword ptr [ebp - 0x12dc], 0
0041E968 744c je 0x41e9b6
0041E96A 8b8d28edffff mov ecx, dword ptr [ebp - 0x12d8]
0041E970 51 push ecx
0041E971 8b8d24edffff mov ecx, dword ptr [ebp - 0x12dc]
0041E977 e894690a00 call 0x4c5310
0041E97C 0fb6d0 movzx edx, al
0041E97F 85d2 test edx, edx
0041E981 7433 je 0x41e9b6
0041E983 668b852cedffff mov ax, word ptr [ebp - 0x12d4]
0041E98A 50 push eax
0041E98B 8a8d3bedffff mov cl, byte ptr [ebp - 0x12c5]
0041E991 51 push ecx
0041E992 0fbf9534edffff movsx edx, word ptr [ebp - 0x12cc]
0041E999 52 push edx
0041E99A 0fbf853cedffff movsx eax, word ptr [ebp - 0x12c4]
0041E9A1 50 push eax
0041E9A2 8b8d28edffff mov ecx, dword ptr [ebp - 0x12d8]
0041E9A8 8b11 mov edx, dword ptr [ecx]
0041E9AA 8b8d28edffff mov ecx, dword ptr [ebp - 0x12d8]
0041E9B0 ff9270010000 call dword ptr [edx + 0x170]
0041E9B6 eb0a jmp 0x41e9c2
0041E9B8 b9405ab700 mov ecx, 0xb75a40
0041E9BD e89e60feff call 0x404a60
0041E9C2 e9a2b60000 jmp 0x42a069

RANGE 00478F00-00478F6E
00478F00 55 push ebp
00478F01 8bec mov ebp, esp
00478F03 56 push esi
00478F04 8bf1 mov esi, ecx
00478F06 8b86c8030000 mov eax, dword ptr [esi + 0x3c8]
00478F0C 85c0 test eax, eax
00478F0E 740a je 0x478f1a
00478F10 8a88a0010000 mov cl, byte ptr [eax + 0x1a0]
00478F16 84c9 test cl, cl
00478F18 754f jne 0x478f69
00478F1A 0fb64510 movzx eax, byte ptr [ebp + 0x10]
00478F1E 48 dec eax
00478F1F 99 cdq 
00478F20 b903000000 mov ecx, 3
00478F25 f7f9 idiv ecx
00478F27 85d2 test edx, edx
00478F29 740b je 0x478f36
00478F2B 4a dec edx
00478F2C f7da neg edx
00478F2E 1bd2 sbb edx, edx
00478F30 83e2fd and edx, 0xfffffffd
00478F33 83c204 add edx, 4
00478F36 85c0 test eax, eax
00478F38 7507 jne 0x478f41
00478F3A b803000000 mov eax, 3
00478F3F eb0e jmp 0x478f4f
00478F41 33c9 xor ecx, ecx
00478F43 83f801 cmp eax, 1
00478F46 0f94c1 sete cl
00478F49 8d4c0902 lea ecx, [ecx + ecx + 2]
00478F4D 8bc1 mov eax, ecx
00478F4F 8b4d14 mov ecx, dword ptr [ebp + 0x14]
00478F52 57 push edi
00478F53 8b3e mov edi, dword ptr [esi]
00478F55 51 push ecx
00478F56 50 push eax
00478F57 8b4508 mov eax, dword ptr [ebp + 8]
00478F5A 52 push edx
00478F5B 8b550c mov edx, dword ptr [ebp + 0xc]
00478F5E 52 push edx
00478F5F 50 push eax
00478F60 8bce mov ecx, esi
00478F62 ff9774010000 call dword ptr [edi + 0x174]
00478F68 5f pop edi
00478F69 5e pop esi
00478F6A 5d pop ebp
00478F6B c21000 ret 0x10

RANGE 004798A0-00479A40
004798A0 55 push ebp
004798A1 8bec mov ebp, esp
004798A3 668b4518 mov ax, word ptr [ebp + 0x18]
004798A7 6685c0 test ax, ax
004798AA 53 push ebx
004798AB 56 push esi
004798AC 57 push edi
004798AD 8bf9 mov edi, ecx
004798AF 7455 je 0x479906
004798B1 8b1558d4cf00 mov edx, dword ptr [0xcfd458]
004798B7 8b8f48020000 mov ecx, dword ptr [edi + 0x248]
004798BD 8b1d5cd4cf00 mov ebx, dword ptr [0xcfd45c]
004798C3 0fbfc0 movsx eax, ax
004798C6 894518 mov dword ptr [ebp + 0x18], eax
004798C9 8b4244 mov eax, dword ptr [edx + 0x44]
004798CC 8d0488 lea eax, [eax + ecx*4]
004798CF db4518 fild dword ptr [ebp + 0x18]
004798D2 8bd0 mov edx, eax
004798D4 33d3 xor edx, ebx
004798D6 d80d70a79900 fmul dword ptr [0x99a770]
004798DC d95d18 fstp dword ptr [ebp + 0x18]
004798DF 8b4d18 mov ecx, dword ptr [ebp + 0x18]
004798E2 33d1 xor edx, ecx
004798E4 8910 mov dword ptr [eax], edx
004798E6 8b1558d4cf00 mov edx, dword ptr [0xcfd458]
004798EC 8b5248 mov edx, dword ptr [edx + 0x48]
004798EF 8b8748020000 mov eax, dword ptr [edi + 0x248]
004798F5 8b1d5cd4cf00 mov ebx, dword ptr [0xcfd45c]
004798FB 8d0482 lea eax, [edx + eax*4]
004798FE 8bd0 mov edx, eax
00479900 33d3 xor edx, ebx
00479902 33d1 xor edx, ecx
00479904 8910 mov dword ptr [eax], edx
00479906 837d1404 cmp dword ptr [ebp + 0x14], 4
0047990A 8d9f44020000 lea ebx, [edi + 0x244]
00479910 8bcb mov ecx, ebx
00479912 750d jne 0x479921
00479914 e847e9f9ff call 0x418260
00479919 d80df8d69900 fmul dword ptr [0x99d6f8]
0047991F eb0b jmp 0x47992c
00479921 e83ae9f9ff call 0x418260
00479926 d80df4d69900 fmul dword ptr [0x99d6f4]
0047992C e8d71d4a00 call 0x91b708
00479931 8bf0 mov esi, eax
00479933 837d1004 cmp dword ptr [ebp + 0x10], 4
00479937 8bcb mov ecx, ebx
00479939 750d jne 0x479948
0047993B e820e9f9ff call 0x418260
00479940 d80df0d69900 fmul dword ptr [0x99d6f0]
00479946 eb0b jmp 0x479953
00479948 e813e9f9ff call 0x418260
0047994D d80decd69900 fmul dword ptr [0x99d6ec]
00479953 e8b01d4a00 call 0x91b708
00479958 8b5d08 mov ebx, dword ptr [ebp + 8]
0047995B 8bc8 mov ecx, eax
0047995D 8b4510 mov eax, dword ptr [ebp + 0x10]
00479960 85c0 test eax, eax
00479962 7508 jne 0x47996c
00479964 69f60cfeffff imul esi, esi, 0xfffffe0c
0047996A eb0b jmp 0x479977
0047996C 83f801 cmp eax, 1
0047996F 7519 jne 0x47998a
00479971 69f6f4010000 imul esi, esi, 0x1f4
00479977 b8d34d6210 mov eax, 0x10624dd3
0047997C f7ee imul esi
0047997E c1fa06 sar edx, 6
00479981 8bc2 mov eax, edx
00479983 c1e81f shr eax, 0x1f
00479986 03c2 add eax, edx
00479988 03d8 add ebx, eax
0047998A 8b4514 mov eax, dword ptr [ebp + 0x14]
0047998D 83f802 cmp eax, 2
00479990 8b750c mov esi, dword ptr [ebp + 0xc]
00479993 751b jne 0x4799b0
00479995 69c90cfeffff imul ecx, ecx, 0xfffffe0c
0047999B b8d34d6210 mov eax, 0x10624dd3
004799A0 f7e9 imul ecx
004799A2 c1fa06 sar edx, 6
004799A5 8bca mov ecx, edx
004799A7 c1e91f shr ecx, 0x1f
004799AA 03ca add ecx, edx
004799AC 03f1 add esi, ecx
004799AE eb1e jmp 0x4799ce
004799B0 83f803 cmp eax, 3
004799B3 7519 jne 0x4799ce
004799B5 69c9f4010000 imul ecx, ecx, 0x1f4
004799BB b8d34d6210 mov eax, 0x10624dd3
004799C0 f7e9 imul ecx
004799C2 c1fa06 sar edx, 6
004799C5 8bc2 mov eax, edx
004799C7 c1e81f shr eax, 0x1f
004799CA 03c2 add eax, edx
004799CC 03f0 add esi, eax
004799CE 8b4d10 mov ecx, dword ptr [ebp + 0x10]
004799D1 b804000000 mov eax, 4
004799D6 3bc8 cmp ecx, eax
004799D8 7551 jne 0x479a2b
004799DA 394514 cmp dword ptr [ebp + 0x14], eax
004799DD 754c jne 0x479a2b
004799DF 8b17 mov edx, dword ptr [edi]
004799E1 8bcf mov ecx, edi
004799E3 ff9284000000 call dword ptr [edx + 0x84]
004799E9 8bcb mov ecx, ebx
004799EB 2bc8 sub ecx, eax
004799ED 8bc1 mov eax, ecx
004799EF 99 cdq 
004799F0 33c2 xor eax, edx
004799F2 2bc2 sub eax, edx
004799F4 83f832 cmp eax, 0x32
004799F7 7d0c jge 0x479a05
004799F9 8b17 mov edx, dword ptr [edi]
004799FB 8bcf mov ecx, edi
004799FD ff9284000000 call dword ptr [edx + 0x84]
00479A03 8bd8 mov ebx, eax
00479A05 8b07 mov eax, dword ptr [edi]
00479A07 8bcf mov ecx, edi
00479A09 ff9088000000 call dword ptr [eax + 0x88]
00479A0F 8bce mov ecx, esi
00479A11 2bc8 sub ecx, eax
00479A13 8bc1 mov eax, ecx
00479A15 99 cdq 
00479A16 33c2 xor eax, edx
00479A18 2bc2 sub eax, edx
00479A1A 83f832 cmp eax, 0x32
00479A1D 7d0c jge 0x479a2b
00479A1F 8b17 mov edx, dword ptr [edi]
00479A21 8bcf mov ecx, edi
00479A23 ff9288000000 call dword ptr [edx + 0x88]
00479A29 8bf0 mov esi, eax
00479A2B 8b07 mov eax, dword ptr [edi]
00479A2D 56 push esi
00479A2E 53 push ebx
00479A2F 8bcf mov ecx, edi
00479A31 ff9078010000 call dword ptr [eax + 0x178]
00479A37 5f pop edi
00479A38 5e pop esi
00479A39 5b pop ebx
00479A3A 5d pop ebp
00479A3B c21400 ret 0x14
00479A3E cc int3 
00479A3F cc int3 

RANGE 00407460-00407480
00407460 8b01 mov eax, dword ptr [ecx]
00407462 6a00 push 0
00407464 ff9080000000 call dword ptr [eax + 0x80]
0040746A c3 ret 
0040746B cc int3 
0040746C cc int3 
0040746D cc int3 
0040746E cc int3 
0040746F cc int3 
00407470 8b01 mov eax, dword ptr [ecx]
00407472 6a01 push 1
00407474 ff9080000000 call dword ptr [eax + 0x80]
0040747A c3 ret 
0040747B cc int3 
0040747C cc int3 
0040747D cc int3 
0040747E cc int3 
0040747F cc int3 