
local_checker_dispatch: 0x00433AC0, size 24
00433AC0 8b0d145ab700         mov     ecx, dword ptr [0xb75a14]
00433AC6 85c9                 test    ecx, ecx
00433AC8 7408                 je      0x433ad2
00433ACA 8b01                 mov     eax, dword ptr [ecx]
00433ACC ffa0d4050000         jmp     dword ptr [eax + 0x5d4]
00433AD2 b868deb500           mov     eax, 0xb5de68
00433AD7 c3                   ret     

local_checker_getter: 0x00493770, size 16
00493770 8b8184230000         mov     eax, dword ptr [ecx + 0x2384]
00493776 85c0                 test    eax, eax
00493778 7505                 jne     0x49377f
0049377A b868deb500           mov     eax, 0xb5de68
0049377F c3                   ret     

checker_vtable: 0x0099A88C, size 124
0099A88C 0043C560
0099A890 008BAA00
0099A894 0043BC00
0099A898 0043BC80
0099A89C 0043BF60
0099A8A0 008BA090
0099A8A4 0043BC10
0099A8A8 0043BE00
0099A8AC 0043BC20
0099A8B0 0044D2F0
0099A8B4 008BA0C0
0099A8B8 008BA130
0099A8BC 008BA7F0
0099A8C0 008BA940
0099A8C4 008BA900
0099A8C8 008BA1A0
0099A8CC 008BA030
0099A8D0 0043BC30
0099A8D4 008BAA60
0099A8D8 0043BC60
0099A8DC 008BACD0
0099A8E0 0043BD40
0099A8E4 0043BC70
0099A8E8 0043BE50
0099A8EC 0043BD60
0099A8F0 008BA990
0099A8F4 008BAAB0
0099A8F8 008BA020
0099A8FC 008BAAD0
0099A900 008BA220
0099A904 008BA610

checker_lock_setter: 0x008BA030, size 94
008BA030 55                   push    ebp
008BA031 8bec                 mov     ebp, esp
008BA033 8b4508               mov     eax, dword ptr [ebp + 8]
008BA036 85c0                 test    eax, eax
008BA038 7c4e                 jl      0x8ba088
008BA03A 83f804               cmp     eax, 4
008BA03D 7d49                 jge     0x8ba088
008BA03F 807d0c01             cmp     byte ptr [ebp + 0xc], 1
008BA043 8b548164             mov     edx, dword ptr [ecx + eax*4 + 0x64]
008BA047 56                   push    esi
008BA048 751f                 jne     0x8ba069
008BA04A 3b5510               cmp     edx, dword ptr [ebp + 0x10]
008BA04D 7727                 ja      0x8ba076
008BA04F c744816400000000     mov     dword ptr [ecx + eax*4 + 0x64], 0
008BA057 8b748164             mov     esi, dword ptr [ecx + eax*4 + 0x64]
008BA05B 33d2                 xor     edx, edx
008BA05D 85f6                 test    esi, esi
008BA05F 0f94c2               sete    dl
008BA062 8ac2                 mov     al, dl
008BA064 5e                   pop     esi
008BA065 5d                   pop     ebp
008BA066 c20c00               ret     0xc
008BA069 8b7510               mov     esi, dword ptr [ebp + 0x10]
008BA06C 3bd6                 cmp     edx, esi
008BA06E 7702                 ja      0x8ba072
008BA070 8bd6                 mov     edx, esi
008BA072 89548164             mov     dword ptr [ecx + eax*4 + 0x64], edx
008BA076 8b748164             mov     esi, dword ptr [ecx + eax*4 + 0x64]
008BA07A 33d2                 xor     edx, edx
008BA07C 85f6                 test    esi, esi
008BA07E 0f94c2               sete    dl
008BA081 8ac2                 mov     al, dl
008BA083 5e                   pop     esi
008BA084 5d                   pop     ebp
008BA085 c20c00               ret     0xc
008BA088 32c0                 xor     al, al
008BA08A 5d                   pop     ebp
008BA08B c20c00               ret     0xc

checker_lock_getter: 0x0043BC30, size 40
0043BC30 55                   push    ebp
0043BC31 8bec                 mov     ebp, esp
0043BC33 8b4508               mov     eax, dword ptr [ebp + 8]
0043BC36 85c0                 test    eax, eax
0043BC38 7c18                 jl      0x43bc52
0043BC3A 83f804               cmp     eax, 4
0043BC3D 7d13                 jge     0x43bc52
0043BC3F 56                   push    esi
0043BC40 8b748164             mov     esi, dword ptr [ecx + eax*4 + 0x64]
0043BC44 33d2                 xor     edx, edx
0043BC46 85f6                 test    esi, esi
0043BC48 0f94c2               sete    dl
0043BC4B 8ac2                 mov     al, dl
0043BC4D 5e                   pop     esi
0043BC4E 5d                   pop     ebp
0043BC4F c20400               ret     4
0043BC52 32c0                 xor     al, al
0043BC54 5d                   pop     ebp
0043BC55 c20400               ret     4

checker_physical_gate: 0x008BA7F0, size 265
008BA7F0 55                   push    ebp
008BA7F1 8bec                 mov     ebp, esp
008BA7F3 56                   push    esi
008BA7F4 8b7508               mov     esi, dword ptr [ebp + 8]
008BA7F7 8b06                 mov     eax, dword ptr [esi]
008BA7F9 83f804               cmp     eax, 4
008BA7FC 7d0c                 jge     0x8ba80a
008BA7FE 8b548164             mov     edx, dword ptr [ecx + eax*4 + 0x64]
008BA802 85d2                 test    edx, edx
008BA804 740b                 je      0x8ba811
008BA806 c6460801             mov     byte ptr [esi + 8], 1
008BA80A 32c0                 xor     al, al
008BA80C 5e                   pop     esi
008BA80D 5d                   pop     ebp
008BA80E c20400               ret     4
008BA811 53                   push    ebx
008BA812 32db                 xor     bl, bl
008BA814 85c0                 test    eax, eax
008BA816 0f858c000000         jne     0x8ba8a8
008BA81C 8a8190000000         mov     al, byte ptr [ecx + 0x90]
008BA822 84c0                 test    al, al
008BA824 7426                 je      0x8ba84c
008BA826 8b898c000000         mov     ecx, dword ptr [ecx + 0x8c]
008BA82C 85c9                 test    ecx, ecx
008BA82E 0f84b9000000         je      0x8ba8ed
008BA834 8b4604               mov     eax, dword ptr [esi + 4]
008BA837 50                   push    eax
008BA838 e8e384c2ff           call    0x4e2d20
008BA83D 84c0                 test    al, al
008BA83F 0f84a8000000         je      0x8ba8ed
008BA845 b301                 mov     bl, 1
008BA847 e990000000           jmp     0x8ba8dc
008BA84C 8b0d4c16bf00         mov     ecx, dword ptr [0xbf164c]
008BA852 8b4604               mov     eax, dword ptr [esi + 4]
008BA855 8b11                 mov     edx, dword ptr [ecx]
008BA857 50                   push    eax
008BA858 ff5218               call    dword ptr [edx + 0x18]
008BA85B 84c0                 test    al, al
008BA85D 7402                 je      0x8ba861
008BA85F b301                 mov     bl, 1
008BA861 8b0d5016bf00         mov     ecx, dword ptr [0xbf1650]
008BA867 85c9                 test    ecx, ecx
008BA869 746d                 je      0x8ba8d8
008BA86B 84db                 test    bl, bl
008BA86D 756d                 jne     0x8ba8dc
008BA86F 8b4604               mov     eax, dword ptr [esi + 4]
008BA872 8b11                 mov     edx, dword ptr [ecx]
008BA874 50                   push    eax
008BA875 ff5218               call    dword ptr [edx + 0x18]
008BA878 84c0                 test    al, al
008BA87A 7402                 je      0x8ba87e
008BA87C b301                 mov     bl, 1
008BA87E 8b4604               mov     eax, dword ptr [esi + 4]
008BA881 85c0                 test    eax, eax
008BA883 7453                 je      0x8ba8d8
008BA885 83f801               cmp     eax, 1
008BA888 744e                 je      0x8ba8d8
008BA88A 83f802               cmp     eax, 2
008BA88D 7449                 je      0x8ba8d8
008BA88F 83f803               cmp     eax, 3
008BA892 7444                 je      0x8ba8d8
008BA894 8b0d5016bf00         mov     ecx, dword ptr [0xbf1650]
008BA89A 8b11                 mov     edx, dword ptr [ecx]
008BA89C 50                   push    eax
008BA89D ff5220               call    dword ptr [edx + 0x20]
008BA8A0 84c0                 test    al, al
008BA8A2 7434                 je      0x8ba8d8
008BA8A4 b301                 mov     bl, 1
008BA8A6 eb34                 jmp     0x8ba8dc
008BA8A8 83f801               cmp     eax, 1
008BA8AB 7540                 jne     0x8ba8ed
008BA8AD 8a8190000000         mov     al, byte ptr [ecx + 0x90]
008BA8B3 84c0                 test    al, al
008BA8B5 7536                 jne     0x8ba8ed
008BA8B7 8b4604               mov     eax, dword ptr [esi + 4]
008BA8BA 85c0                 test    eax, eax
008BA8BC 750d                 jne     0x8ba8cb
008BA8BE a000d2cf00           mov     al, byte ptr [0xcfd200]
008BA8C3 84c0                 test    al, al
008BA8C5 7426                 je      0x8ba8ed
008BA8C7 b301                 mov     bl, 1
008BA8C9 eb11                 jmp     0x8ba8dc
008BA8CB a002d2cf00           mov     al, byte ptr [0xcfd202]
008BA8D0 84c0                 test    al, al
008BA8D2 7419                 je      0x8ba8ed
008BA8D4 b301                 mov     bl, 1
008BA8D6 eb04                 jmp     0x8ba8dc
008BA8D8 84db                 test    bl, bl
008BA8DA 7411                 je      0x8ba8ed
008BA8DC 8a4608               mov     al, byte ptr [esi + 8]
008BA8DF 84c0                 test    al, al
008BA8E1 740e                 je      0x8ba8f1
008BA8E3 32db                 xor     bl, bl
008BA8E5 8ac3                 mov     al, bl
008BA8E7 5b                   pop     ebx
008BA8E8 5e                   pop     esi
008BA8E9 5d                   pop     ebp
008BA8EA c20400               ret     4
008BA8ED c6460800             mov     byte ptr [esi + 8], 0
008BA8F1 8ac3                 mov     al, bl
008BA8F3 5b                   pop     ebx
008BA8F4 5e                   pop     esi
008BA8F5 5d                   pop     ebp
008BA8F6 c20400               ret     4

checker_direction_lookup: 0x008BA0C0, size 110
008BA0C0 55                   push    ebp
008BA0C1 8bec                 mov     ebp, esp
008BA0C3 51                   push    ecx
008BA0C4 8b5158               mov     edx, dword ptr [ecx + 0x58]
008BA0C7 53                   push    ebx
008BA0C8 56                   push    esi
008BA0C9 57                   push    edi
008BA0CA 8b7954               mov     edi, dword ptr [ecx + 0x54]
008BA0CD 2bd7                 sub     edx, edi
008BA0CF c1fa04               sar     edx, 4
008BA0D2 4a                   dec     edx
008BA0D3 783e                 js      0x8ba113
008BA0D5 8bf2                 mov     esi, edx
008BA0D7 c1e604               shl     esi, 4
008BA0DA 897dfc               mov     dword ptr [ebp - 4], edi
008BA0DD 8d5c3e0c             lea     ebx, [esi + edi + 0xc]
008BA0E1 803b00               cmp     byte ptr [ebx], 0
008BA0E4 7422                 je      0x8ba108
008BA0E6 8b4508               mov     eax, dword ptr [ebp + 8]
008BA0E9 8b4808               mov     ecx, dword ptr [eax + 8]
008BA0EC 8b4004               mov     eax, dword ptr [eax + 4]
008BA0EF 3bc1                 cmp     eax, ecx
008BA0F1 7415                 je      0x8ba108
008BA0F3 8b3c3e               mov     edi, dword ptr [esi + edi]
008BA0F6 3938                 cmp     dword ptr [eax], edi
008BA0F8 7407                 je      0x8ba101
008BA0FA 83c004               add     eax, 4
008BA0FD 3bc1                 cmp     eax, ecx
008BA0FF 75f5                 jne     0x8ba0f6
008BA101 3bc1                 cmp     eax, ecx
008BA103 8b7dfc               mov     edi, dword ptr [ebp - 4]
008BA106 7517                 jne     0x8ba11f
008BA108 4a                   dec     edx
008BA109 83ee10               sub     esi, 0x10
008BA10C 83eb10               sub     ebx, 0x10
008BA10F 85d2                 test    edx, edx
008BA111 7dce                 jge     0x8ba0e1
008BA113 5f                   pop     edi
008BA114 5e                   pop     esi
008BA115 83c8ff               or      eax, 0xffffffff
008BA118 5b                   pop     ebx
008BA119 8be5                 mov     esp, ebp
008BA11B 5d                   pop     ebp
008BA11C c20400               ret     4
008BA11F c1e204               shl     edx, 4
008BA122 8b043a               mov     eax, dword ptr [edx + edi]
008BA125 5f                   pop     edi
008BA126 5e                   pop     esi
008BA127 5b                   pop     ebx
008BA128 8be5                 mov     esp, ebp
008BA12A 5d                   pop     ebp
008BA12B c20400               ret     4

town_keyboard_direction: 0x0047B268, size 405
0047B268 682e020000           push    0x22e
0047B26D 6820d79900           push    0x99d720
0047B272 e84988fbff           call    0x433ac0
0047B277 83c408               add     esp, 8
0047B27A 85c0                 test    eax, eax
0047B27C 74ce                 je      0x47b24c
0047B27E 6830020000           push    0x230
0047B283 6820d79900           push    0x99d720
0047B288 e83388fbff           call    0x433ac0
0047B28D 8b10                 mov     edx, dword ptr [eax]
0047B28F 83c408               add     esp, 8
0047B292 6818f2b500           push    0xb5f218
0047B297 8bc8                 mov     ecx, eax
0047B299 ff5228               call    dword ptr [edx + 0x28]
0047B29C 6831020000           push    0x231
0047B2A1 6820d79900           push    0x99d720
0047B2A6 8bd8                 mov     ebx, eax
0047B2A8 e81388fbff           call    0x433ac0
0047B2AD 8b10                 mov     edx, dword ptr [eax]
0047B2AF 83c408               add     esp, 8
0047B2B2 6828f2b500           push    0xb5f228
0047B2B7 8bc8                 mov     ecx, eax
0047B2B9 ff5228               call    dword ptr [edx + 0x28]
0047B2BC 83fb02               cmp     ebx, 2
0047B2BF 7509                 jne     0x47b2ca
0047B2C1 c745f000000000       mov     dword ptr [ebp - 0x10], 0
0047B2C8 eb12                 jmp     0x47b2dc
0047B2CA 33c9                 xor     ecx, ecx
0047B2CC 83fb03               cmp     ebx, 3
0047B2CF 0f95c1               setne   cl
0047B2D2 49                   dec     ecx
0047B2D3 83e1fd               and     ecx, 0xfffffffd
0047B2D6 83c104               add     ecx, 4
0047B2D9 894df0               mov     dword ptr [ebp - 0x10], ecx
0047B2DC 85c0                 test    eax, eax
0047B2DE 7507                 jne     0x47b2e7
0047B2E0 bf02000000           mov     edi, 2
0047B2E5 eb0d                 jmp     0x47b2f4
0047B2E7 33d2                 xor     edx, edx
0047B2E9 83f801               cmp     eax, 1
0047B2EC 0f95c2               setne   dl
0047B2EF 83c203               add     edx, 3
0047B2F2 8bfa                 mov     edi, edx
0047B2F4 837df004             cmp     dword ptr [ebp - 0x10], 4
0047B2F8 7509                 jne     0x47b303
0047B2FA 83ff04               cmp     edi, 4
0047B2FD 0f8449ffffff         je      0x47b24c
0047B303 c605a0f0b50000       mov     byte ptr [0xb5f0a0], 0
0047B30A 8b4df0               mov     ecx, dword ptr [ebp - 0x10]
0047B30D 89be38020000         mov     dword ptr [esi + 0x238], edi
0047B313 83ff04               cmp     edi, 4
0047B316 8dbe44020000         lea     edi, [esi + 0x244]
0047B31C 898e34020000         mov     dword ptr [esi + 0x234], ecx
0047B322 8bcf                 mov     ecx, edi
0047B324 750d                 jne     0x47b333
0047B326 e835cff9ff           call    0x418260
0047B32B d80df8d69900         fmul    dword ptr [0x99d6f8]
0047B331 eb0b                 jmp     0x47b33e
0047B333 e828cff9ff           call    0x418260
0047B338 d80df4d69900         fmul    dword ptr [0x99d6f4]
0047B33E e8c5034a00           call    0x91b708
0047B343 8bd8                 mov     ebx, eax
0047B345 83be3402000004       cmp     dword ptr [esi + 0x234], 4
0047B34C 895df0               mov     dword ptr [ebp - 0x10], ebx
0047B34F 8bcf                 mov     ecx, edi
0047B351 750d                 jne     0x47b360
0047B353 e808cff9ff           call    0x418260
0047B358 d80df0d69900         fmul    dword ptr [0x99d6f0]
0047B35E eb0b                 jmp     0x47b36b
0047B360 e8fbcef9ff           call    0x418260
0047B365 d80decd69900         fmul    dword ptr [0x99d6ec]
0047B36B e898034a00           call    0x91b708
0047B370 8b8e34020000         mov     ecx, dword ptr [esi + 0x234]
0047B376 33ff                 xor     edi, edi
0047B378 83f904               cmp     ecx, 4
0047B37B 8945ec               mov     dword ptr [ebp - 0x14], eax
0047B37E 7505                 jne     0x47b385
0047B380 897df0               mov     dword ptr [ebp - 0x10], edi
0047B383 eb09                 jmp     0x47b38e
0047B385 3bcf                 cmp     ecx, edi
0047B387 7505                 jne     0x47b38e
0047B389 f7db                 neg     ebx
0047B38B 895df0               mov     dword ptr [ebp - 0x10], ebx
0047B38E 8b8e38020000         mov     ecx, dword ptr [esi + 0x238]
0047B394 83f904               cmp     ecx, 4
0047B397 7505                 jne     0x47b39e
0047B399 897dec               mov     dword ptr [ebp - 0x14], edi
0047B39C eb0a                 jmp     0x47b3a8
0047B39E 83f902               cmp     ecx, 2
0047B3A1 7505                 jne     0x47b3a8
0047B3A3 f7d8                 neg     eax
0047B3A5 8945ec               mov     dword ptr [ebp - 0x14], eax
0047B3A8 8b86c8030000         mov     eax, dword ptr [esi + 0x3c8]
0047B3AE 3bc7                 cmp     eax, edi
0047B3B0 7410                 je      0x47b3c2
0047B3B2 8a88a0010000         mov     cl, byte ptr [eax + 0x1a0]
0047B3B8 84c9                 test    cl, cl
0047B3BA 7406                 je      0x47b3c2
0047B3BC 897df0               mov     dword ptr [ebp - 0x10], edi
0047B3BF 897dec               mov     dword ptr [ebp - 0x14], edi
0047B3C2 8b86e0000000         mov     eax, dword ptr [esi + 0xe0]
0047B3C8 8b08                 mov     ecx, dword ptr [eax]
0047B3CA 3bcf                 cmp     ecx, edi
0047B3CC 7503                 jne     0x47b3d1
0047B3CE 8d4808               lea     ecx, [eax + 8]
0047B3D1 db45f0               fild    dword ptr [ebp - 0x10]
0047B3D4 8b11                 mov     edx, dword ptr [ecx]
0047B3D6 51                   push    ecx
0047B3D7 d91c24               fstp    dword ptr [esp]
0047B3DA 57                   push    edi
0047B3DB ff520c               call    dword ptr [edx + 0xc]
0047B3DE 8b86e0000000         mov     eax, dword ptr [esi + 0xe0]
0047B3E4 8b08                 mov     ecx, dword ptr [eax]
0047B3E6 3bcf                 cmp     ecx, edi
0047B3E8 7503                 jne     0x47b3ed
0047B3EA 8d4808               lea     ecx, [eax + 8]
0047B3ED db45ec               fild    dword ptr [ebp - 0x14]
0047B3F0 8b01                 mov     eax, dword ptr [ecx]
0047B3F2 51                   push    ecx
0047B3F3 d91c24               fstp    dword ptr [esp]
0047B3F6 6a01                 push    1
0047B3F8 ff500c               call    dword ptr [eax + 0xc]
0047B3FB eb08                 jmp     0x47b405

combat_direction_lookup: 0x00493790, size 126
00493790 55                   push    ebp
00493791 8bec                 mov     ebp, esp
00493793 8b4508               mov     eax, dword ptr [ebp + 8]
00493796 85c0                 test    eax, eax
00493798 56                   push    esi
00493799 57                   push    edi
0049379A 8bf9                 mov     edi, ecx
0049379C be18f2b500           mov     esi, 0xb5f218
004937A1 7405                 je      0x4937a8
004937A3 be28f2b500           mov     esi, 0xb5f228
004937A8 8b07                 mov     eax, dword ptr [edi]
004937AA 8bcf                 mov     ecx, edi
004937AC ff90d4050000         call    dword ptr [eax + 0x5d4]
004937B2 8b10                 mov     edx, dword ptr [eax]
004937B4 56                   push    esi
004937B5 8bc8                 mov     ecx, eax
004937B7 ff5228               call    dword ptr [edx + 0x28]
004937BA 83f802               cmp     eax, 2
004937BD be04000000           mov     esi, 4
004937C2 7504                 jne     0x4937c8
004937C4 33f6                 xor     esi, esi
004937C6 eb21                 jmp     0x4937e9
004937C8 83f803               cmp     eax, 3
004937CB 7507                 jne     0x4937d4
004937CD be01000000           mov     esi, 1
004937D2 eb15                 jmp     0x4937e9
004937D4 85c0                 test    eax, eax
004937D6 7507                 jne     0x4937df
004937D8 be02000000           mov     esi, 2
004937DD eb0a                 jmp     0x4937e9
004937DF 83f801               cmp     eax, 1
004937E2 7505                 jne     0x4937e9
004937E4 be03000000           mov     esi, 3
004937E9 8b07                 mov     eax, dword ptr [edi]
004937EB 6a0f                 push    0xf
004937ED 8bcf                 mov     ecx, edi
004937EF ff9058030000         call    dword ptr [eax + 0x358]
004937F5 84c0                 test    al, al
004937F7 740f                 je      0x493808
004937F9 56                   push    esi
004937FA e84100faff           call    0x433840
004937FF 83c404               add     esp, 4
00493802 5f                   pop     edi
00493803 5e                   pop     esi
00493804 5d                   pop     ebp
00493805 c20400               ret     4
00493808 5f                   pop     edi
00493809 8bc6                 mov     eax, esi
0049380B 5e                   pop     esi
0049380C 5d                   pop     ebp
0049380D c2                   .byte   0xc2

ui_waiting_set_release: 0x007993B0, size 149
007993B0 68850b0000           push    0xb85
007993B5 c781c40f000011000000 mov     dword ptr [ecx + 0xfc4], 0x11
007993BF 68c4cea700           push    0xa7cec4
007993C4 c60508d2cf0001       mov     byte ptr [0xcfd208], 1
007993CB e8f0a6c9ff           call    0x433ac0
007993D0 83c408               add     esp, 8
007993D3 85c0                 test    eax, eax
007993D5 741f                 je      0x7993f6
007993D7 68860b0000           push    0xb86
007993DC 68c4cea700           push    0xa7cec4
007993E1 e8daa6c9ff           call    0x433ac0
007993E6 8b10                 mov     edx, dword ptr [eax]
007993E8 83c408               add     esp, 8
007993EB 6a01                 push    1
007993ED 6a00                 push    0
007993EF 6a00                 push    0
007993F1 8bc8                 mov     ecx, eax
007993F3 ff5240               call    dword ptr [edx + 0x40]
007993F6 c3                   ret     
007993F7 cc                   int3    
007993F8 cc                   int3    
007993F9 cc                   int3    
007993FA cc                   int3    
007993FB cc                   int3    
007993FC cc                   int3    
007993FD cc                   int3    
007993FE cc                   int3    
007993FF cc                   int3    
00799400 c781c40f000000000000 mov     dword ptr [ecx + 0xfc4], 0
0079940A e871381600           call    0x8fcc80
0079940F 688e0b0000           push    0xb8e
00799414 68e8cea700           push    0xa7cee8
00799419 e8a2a6c9ff           call    0x433ac0
0079941E 83c408               add     esp, 8
00799421 85c0                 test    eax, eax
00799423 741f                 je      0x799444
00799425 688f0b0000           push    0xb8f
0079942A 68e8cea700           push    0xa7cee8
0079942F e88ca6c9ff           call    0x433ac0
00799434 8b10                 mov     edx, dword ptr [eax]
00799436 83c408               add     esp, 8
00799439 6a01                 push    1
0079943B 6a01                 push    1
0079943D 6a00                 push    0
0079943F 8bc8                 mov     ecx, eax
00799441 ff5240               call    dword ptr [edx + 0x40]
00799444 c3                   ret     

chat_edit_set_active: 0x00843D40, size 263
00843D40 55                   push    ebp
00843D41 8bec                 mov     ebp, esp
00843D43 53                   push    ebx
00843D44 8a5d08               mov     bl, byte ptr [ebp + 8]
00843D47 56                   push    esi
00843D48 8bf1                 mov     esi, ecx
00843D4A 389e6c010000         cmp     byte ptr [esi + 0x16c], bl
00843D50 0f84eb000000         je      0x843e41
00843D56 84db                 test    bl, bl
00843D58 7472                 je      0x843dcc
00843D5A 8b8e90000000         mov     ecx, dword ptr [esi + 0x90]
00843D60 8a4111               mov     al, byte ptr [ecx + 0x11]
00843D63 84c0                 test    al, al
00843D65 0f84d6000000         je      0x843e41
00843D6B 8b01                 mov     eax, dword ptr [ecx]
00843D6D ff5024               call    dword ptr [eax + 0x24]
00843D70 8b8e90000000         mov     ecx, dword ptr [esi + 0x90]
00843D76 8b410c               mov     eax, dword ptr [ecx + 0xc]
00843D79 a39c51b500           mov     dword ptr [0xb5519c], eax
00843D7E a3a451b500           mov     dword ptr [0xb551a4], eax
00843D83 83beb001000004       cmp     dword ptr [esi + 0x1b0], 4
00843D8A 750a                 jne     0x843d96
00843D8C 33c0                 xor     eax, eax
00843D8E 8986b0010000         mov     dword ptr [esi + 0x1b0], eax
00843D94 eb02                 jmp     0x843d98
00843D96 33c0                 xor     eax, eax
00843D98 83beb001000005       cmp     dword ptr [esi + 0x1b0], 5
00843D9F 7506                 jne     0x843da7
00843DA1 8986b0010000         mov     dword ptr [esi + 0x1b0], eax
00843DA7 83beb001000006       cmp     dword ptr [esi + 0x1b0], 6
00843DAE 7506                 jne     0x843db6
00843DB0 8986b0010000         mov     dword ptr [esi + 0x1b0], eax
00843DB6 8b9690000000         mov     edx, dword ptr [esi + 0x90]
00843DBC c74270db010000       mov     dword ptr [edx + 0x70], 0x1db
00843DC3 c6860408000001       mov     byte ptr [esi + 0x804], 1
00843DCA eb13                 jmp     0x843ddf
00843DCC 8b8684000000         mov     eax, dword ptr [esi + 0x84]
00843DD2 8b400c               mov     eax, dword ptr [eax + 0xc]
00843DD5 a39c51b500           mov     dword ptr [0xb5519c], eax
00843DDA a3a451b500           mov     dword ptr [0xb551a4], eax
00843DDF 8b8e88000000         mov     ecx, dword ptr [esi + 0x88]
00843DE5 57                   push    edi
00843DE6 885910               mov     byte ptr [ecx + 0x10], bl
00843DE9 8dbe70010000         lea     edi, [esi + 0x170]
00843DEF 8bcf                 mov     ecx, edi
00843DF1 e81a15bcff           call    0x405310
00843DF6 6a00                 push    0
00843DF8 6a64                 push    0x64
00843DFA 8bcf                 mov     ecx, edi
00843DFC e87f000b00           call    0x8f3e80
00843E01 84db                 test    bl, bl
00843E03 889e6c010000         mov     byte ptr [esi + 0x16c], bl
00843E09 5f                   pop     edi
00843E0A 7535                 jne     0x843e41
00843E0C 68b5020000           push    0x2b5
00843E11 68a02fa800           push    0xa82fa0
00843E16 e8a5fcbeff           call    0x433ac0
00843E1B 83c408               add     esp, 8
00843E1E 85c0                 test    eax, eax
00843E20 741f                 je      0x843e41
00843E22 68b6020000           push    0x2b6
00843E27 68a02fa800           push    0xa82fa0
00843E2C e88ffcbeff           call    0x433ac0
00843E31 8b10                 mov     edx, dword ptr [eax]
00843E33 83c408               add     esp, 8
00843E36 6a01                 push    1
00843E38 6a01                 push    1
00843E3A 6a00                 push    0
00843E3C 8bc8                 mov     ecx, eax
00843E3E ff5240               call    dword ptr [edx + 0x40]
00843E41 5e                   pop     esi
00843E42 5b                   pop     ebx
00843E43 5d                   pop     ebp
00843E44 c20400               ret     4

chat_edit_input_lock: 0x0084BE80, size 70
0084BE80 56                   push    esi
0084BE81 8bf1                 mov     esi, ecx
0084BE83 8a866c010000         mov     al, byte ptr [esi + 0x16c]
0084BE89 84c0                 test    al, al
0084BE8B 0f840d020000         je      0x84c09e
0084BE91 68c8030000           push    0x3c8
0084BE96 68f837a800           push    0xa837f8
0084BE9B e8207cbeff           call    0x433ac0
0084BEA0 83c408               add     esp, 8
0084BEA3 85c0                 test    eax, eax
0084BEA5 741f                 je      0x84bec6
0084BEA7 68c9030000           push    0x3c9
0084BEAC 68f837a800           push    0xa837f8
0084BEB1 e80a7cbeff           call    0x433ac0
0084BEB6 8b10                 mov     edx, dword ptr [eax]
0084BEB8 83c408               add     esp, 8
0084BEBB 6a01                 push    1
0084BEBD 6a00                 push    0
0084BEBF 6a00                 push    0
0084BEC1 8bc8                 mov     ecx, eax
0084BEC3 ff5240               call    dword ptr [edx + 0x40]

layer_constructor: 0x004EA7E0, size 130
004EA7E0 55                   push    ebp
004EA7E1 8bec                 mov     ebp, esp
004EA7E3 6aff                 push    -1
004EA7E5 6886359600           push    0x963586
004EA7EA 64a100000000         mov     eax, dword ptr fs:[0]
004EA7F0 50                   push    eax
004EA7F1 64892500000000       mov     dword ptr fs:[0], esp
004EA7F8 83ec08               sub     esp, 8
004EA7FB 53                   push    ebx
004EA7FC 56                   push    esi
004EA7FD 8bf1                 mov     esi, ecx
004EA7FF 33db                 xor     ebx, ebx
004EA801 57                   push    edi
004EA802 8975f0               mov     dword ptr [ebp - 0x10], esi
004EA805 885e04               mov     byte ptr [esi + 4], bl
004EA808 8d4e48               lea     ecx, [esi + 0x48]
004EA80B c70608169a00         mov     dword ptr [esi], 0x9a1608
004EA811 895dfc               mov     dword ptr [ebp - 4], ebx
004EA814 894dec               mov     dword ptr [ebp - 0x14], ecx
004EA817 885904               mov     byte ptr [ecx + 4], bl
004EA81A c645fc01             mov     byte ptr [ebp - 4], 1
004EA81E c701cc159a00         mov     dword ptr [ecx], 0x9a15cc
004EA824 e8d7444000           call    0x8eed00
004EA829 8d7e60               lea     edi, [esi + 0x60]
004EA82C 8bcf                 mov     ecx, edi
004EA82E c645fc02             mov     byte ptr [ebp - 4], 2
004EA832 e819261600           call    0x64ce50
004EA837 894704               mov     dword ptr [edi + 4], eax
004EA83A 895f08               mov     dword ptr [edi + 8], ebx
004EA83D 8bce                 mov     ecx, esi
004EA83F c645fc03             mov     byte ptr [ebp - 4], 3
004EA843 e838c04000           call    0x8f6880
004EA848 8bce                 mov     ecx, esi
004EA84A e851bf4000           call    0x8f67a0
004EA84F 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
004EA852 5f                   pop     edi
004EA853 8bc6                 mov     eax, esi
004EA855 5e                   pop     esi
004EA856 5b                   pop     ebx
004EA857 64890d00000000       mov     dword ptr fs:[0], ecx
004EA85E 8be5                 mov     esp, ebp
004EA860 5d                   pop     ebp
004EA861 c3                   ret     

layer_vtable: 0x009A1608, size 32
009A1608 004EA880
009A160C 008F67A0
009A1610 008F67B0
009A1614 004EA870
009A1618 008F6880
009A161C 008F6790
009A1620 00401020
009A1624 00401020

dialog_root_vtable: 0x00A7E09C, size 36
00A7E09C 007BF820
00A7E0A0 008F67A0
00A7E0A4 008F4CE0
00A7E0A8 004EA870
00A7E0AC 008F6880
00A7E0B0 008F6790
00A7E0B4 008F4D00
00A7E0B8 008F4D10
00A7E0BC 008F4C90

layer_low_attach: 0x0079BA10, size 53
0079BA10 6a6c                 push    0x6c
0079BA12 e863cb1700           call    0x91857a
0079BA17 83c404               add     esp, 4
0079BA1A 8945ec               mov     dword ptr [ebp - 0x14], eax
0079BA1D 85c0                 test    eax, eax
0079BA1F c745fc0b000000       mov     dword ptr [ebp - 4], 0xb
0079BA26 7409                 je      0x79ba31
0079BA28 8bc8                 mov     ecx, eax
0079BA2A e8b1edd4ff           call    0x4ea7e0
0079BA2F eb02                 jmp     0x79ba33
0079BA31 33c0                 xor     eax, eax
0079BA33 8b4e4c               mov     ecx, dword ptr [esi + 0x4c]
0079BA36 50                   push    eax
0079BA37 895dfc               mov     dword ptr [ebp - 4], ebx
0079BA3A 8986c8000000         mov     dword ptr [esi + 0xc8], eax
0079BA40 e80bb11500           call    0x8f6b50

layer_mid_attach: 0x0079C023, size 53
0079C023 6a6c                 push    0x6c
0079C025 e850c51700           call    0x91857a
0079C02A 83c404               add     esp, 4
0079C02D 8945ec               mov     dword ptr [ebp - 0x14], eax
0079C030 85c0                 test    eax, eax
0079C032 c745fc2d000000       mov     dword ptr [ebp - 4], 0x2d
0079C039 7409                 je      0x79c044
0079C03B 8bc8                 mov     ecx, eax
0079C03D e89ee7d4ff           call    0x4ea7e0
0079C042 eb02                 jmp     0x79c046
0079C044 33c0                 xor     eax, eax
0079C046 8b4e4c               mov     ecx, dword ptr [esi + 0x4c]
0079C049 50                   push    eax
0079C04A 895dfc               mov     dword ptr [ebp - 4], ebx
0079C04D 8986cc000000         mov     dword ptr [esi + 0xcc], eax
0079C053 e8f8aa1500           call    0x8f6b50

layer_high_attach: 0x0079C2D1, size 53
0079C2D1 6a6c                 push    0x6c
0079C2D3 e8a2c21700           call    0x91857a
0079C2D8 83c404               add     esp, 4
0079C2DB 8945ec               mov     dword ptr [ebp - 0x14], eax
0079C2DE 85c0                 test    eax, eax
0079C2E0 c745fc38000000       mov     dword ptr [ebp - 4], 0x38
0079C2E7 7409                 je      0x79c2f2
0079C2E9 8bc8                 mov     ecx, eax
0079C2EB e8f0e4d4ff           call    0x4ea7e0
0079C2F0 eb02                 jmp     0x79c2f4
0079C2F2 33c0                 xor     eax, eax
0079C2F4 8b4e50               mov     ecx, dword ptr [esi + 0x50]
0079C2F7 50                   push    eax
0079C2F8 895dfc               mov     dword ptr [ebp - 4], ebx
0079C2FB 8986d0000000         mov     dword ptr [esi + 0xd0], eax
0079C301 e84aa81500           call    0x8f6b50

dialog_attach_selected_layer: 0x0079A04A, size 54
0079A04A 4d                   dec     ebp
0079A04B f8                   clc     
0079A04C 8d45fc               lea     eax, [ebp - 4]
0079A04F 50                   push    eax
0079A050 e84b750000           call    0x7a15a0
0079A055 8b4508               mov     eax, dword ptr [ebp + 8]
0079A058 83f80f               cmp     eax, 0xf
0079A05B 7f04                 jg      0x79a061
0079A05D 33ff                 xor     edi, edi
0079A05F eb0b                 jmp     0x79a06c
0079A061 33c9                 xor     ecx, ecx
0079A063 83f817               cmp     eax, 0x17
0079A066 0f9fc1               setg    cl
0079A069 41                   inc     ecx
0079A06A 8bf9                 mov     edi, ecx
0079A06C 8b16                 mov     edx, dword ptr [esi]
0079A06E 8bce                 mov     ecx, esi
0079A070 ff522c               call    dword ptr [edx + 0x2c]
0079A073 8b8cbbc8000000       mov     ecx, dword ptr [ebx + edi*4 + 0xc8]
0079A07A 50                   push    eax
0079A07B e8d0ca1500           call    0x8f6b50

native_child_append: 0x008F6B50, size 75
008F6B50 55                   push    ebp
008F6B51 8bec                 mov     ebp, esp
008F6B53 8b4508               mov     eax, dword ptr [ebp + 8]
008F6B56 85c0                 test    eax, eax
008F6B58 7509                 jne     0x8f6b63
008F6B5A b800000080           mov     eax, 0x80000000
008F6B5F 5d                   pop     ebp
008F6B60 c20400               ret     4
008F6B63 53                   push    ebx
008F6B64 56                   push    esi
008F6B65 57                   push    edi
008F6B66 8b7964               mov     edi, dword ptr [ecx + 0x64]
008F6B69 8d7160               lea     esi, [ecx + 0x60]
008F6B6C 8b4f04               mov     ecx, dword ptr [edi + 4]
008F6B6F 8d4508               lea     eax, [ebp + 8]
008F6B72 50                   push    eax
008F6B73 51                   push    ecx
008F6B74 57                   push    edi
008F6B75 8bce                 mov     ecx, esi
008F6B77 e8a47cffff           call    0x8ee820
008F6B7C 6a01                 push    1
008F6B7E 8bce                 mov     ecx, esi
008F6B80 8bd8                 mov     ebx, eax
008F6B82 e849040000           call    0x8f6fd0
008F6B87 895f04               mov     dword ptr [edi + 4], ebx
008F6B8A 8b5304               mov     edx, dword ptr [ebx + 4]
008F6B8D 5f                   pop     edi
008F6B8E 5e                   pop     esi
008F6B8F 891a                 mov     dword ptr [edx], ebx
008F6B91 b801000000           mov     eax, 1
008F6B96 5b                   pop     ebx
008F6B97 5d                   pop     ebp
008F6B98 c20400               ret     4

native_child_render_order: 0x008F6A80, size 205
008F6A80 53                   push    ebx
008F6A81 56                   push    esi
008F6A82 8bf1                 mov     esi, ecx
008F6A84 8b06                 mov     eax, dword ptr [esi]
008F6A86 57                   push    edi
008F6A87 ff5018               call    dword ptr [eax + 0x18]
008F6A8A 8b4654               mov     eax, dword ptr [esi + 0x54]
008F6A8D 85c0                 test    eax, eax
008F6A8F 746f                 je      0x8f6b00
008F6A91 8b0d102ecf00         mov     ecx, dword ptr [0xcf2e10]
008F6A97 85c9                 test    ecx, ecx
008F6A99 7428                 je      0x8f6ac3
008F6A9B 8b5650               mov     edx, dword ptr [esi + 0x50]
008F6A9E 8b463c               mov     eax, dword ptr [esi + 0x3c]
008F6AA1 8b7e14               mov     edi, dword ptr [esi + 0x14]
008F6AA4 52                   push    edx
008F6AA5 8b5638               mov     edx, dword ptr [esi + 0x38]
008F6AA8 6a00                 push    0
008F6AAA 50                   push    eax
008F6AAB 035618               add     edx, dword ptr [esi + 0x18]
008F6AAE 8b4634               mov     eax, dword ptr [esi + 0x34]
008F6AB1 52                   push    edx
008F6AB2 8b15002ecf00         mov     edx, dword ptr [0xcf2e00]
008F6AB8 03c7                 add     eax, edi
008F6ABA 50                   push    eax
008F6ABB 52                   push    edx
008F6ABC e83f110000           call    0x8f7c00
008F6AC1 eb3d                 jmp     0x8f6b00
008F6AC3 8b0d002ecf00         mov     ecx, dword ptr [0xcf2e00]
008F6AC9 8b01                 mov     eax, dword ptr [ecx]
008F6ACB 6a00                 push    0
008F6ACD ff504c               call    dword ptr [eax + 0x4c]
008F6AD0 8b4650               mov     eax, dword ptr [esi + 0x50]
008F6AD3 8b5e18               mov     ebx, dword ptr [esi + 0x18]
008F6AD6 8b0d002ecf00         mov     ecx, dword ptr [0xcf2e00]
008F6ADC 8b11                 mov     edx, dword ptr [ecx]
008F6ADE 50                   push    eax
008F6ADF 8b463c               mov     eax, dword ptr [esi + 0x3c]
008F6AE2 50                   push    eax
008F6AE3 8b4638               mov     eax, dword ptr [esi + 0x38]
008F6AE6 03c3                 add     eax, ebx
008F6AE8 8b5e14               mov     ebx, dword ptr [esi + 0x14]
008F6AEB 50                   push    eax
008F6AEC 8b4634               mov     eax, dword ptr [esi + 0x34]
008F6AEF 03c3                 add     eax, ebx
008F6AF1 50                   push    eax
008F6AF2 ff5254               call    dword ptr [edx + 0x54]
008F6AF5 8b0d002ecf00         mov     ecx, dword ptr [0xcf2e00]
008F6AFB 8b11                 mov     edx, dword ptr [ecx]
008F6AFD ff5250               call    dword ptr [edx + 0x50]
008F6B00 8b4644               mov     eax, dword ptr [esi + 0x44]
008F6B03 85c0                 test    eax, eax
008F6B05 7407                 je      0x8f6b0e
008F6B07 8bc8                 mov     ecx, eax
008F6B09 8b01                 mov     eax, dword ptr [ecx]
008F6B0B 56                   push    esi
008F6B0C ff10                 call    dword ptr [eax]
008F6B0E 8b4664               mov     eax, dword ptr [esi + 0x64]
008F6B11 8b18                 mov     ebx, dword ptr [eax]
008F6B13 3bd8                 cmp     ebx, eax
008F6B15 742d                 je      0x8f6b44
008F6B17 8b7b08               mov     edi, dword ptr [ebx + 8]
008F6B1A 8a4710               mov     al, byte ptr [edi + 0x10]
008F6B1D 84c0                 test    al, al
008F6B1F 741c                 je      0x8f6b3d
008F6B21 8bcf                 mov     ecx, edi
008F6B23 e858ffffff           call    0x8f6a80
008F6B28 8a4711               mov     al, byte ptr [edi + 0x11]
008F6B2B 84c0                 test    al, al
008F6B2D 740e                 je      0x8f6b3d
008F6B2F 8a4712               mov     al, byte ptr [edi + 0x12]
008F6B32 84c0                 test    al, al
008F6B34 7407                 je      0x8f6b3d
008F6B36 8bcf                 mov     ecx, edi
008F6B38 e8c3feffff           call    0x8f6a00
008F6B3D 8b1b                 mov     ebx, dword ptr [ebx]
008F6B3F 3b5e64               cmp     ebx, dword ptr [esi + 0x64]
008F6B42 75d3                 jne     0x8f6b17
008F6B44 5f                   pop     edi
008F6B45 5e                   pop     esi
008F6B46 b801000000           mov     eax, 1
008F6B4B 5b                   pop     ebx
008F6B4C c3                   ret     

native_child_hit_order: 0x008F6C30, size 738
008F6C30 55                   push    ebp
008F6C31 8bec                 mov     ebp, esp
008F6C33 83ec08               sub     esp, 8
008F6C36 57                   push    edi
008F6C37 8bf9                 mov     edi, ecx
008F6C39 8a4710               mov     al, byte ptr [edi + 0x10]
008F6C3C 84c0                 test    al, al
008F6C3E 7407                 je      0x8f6c47
008F6C40 8a4711               mov     al, byte ptr [edi + 0x11]
008F6C43 84c0                 test    al, al
008F6C45 750a                 jne     0x8f6c51
008F6C47 b8ffffffff           mov     eax, 0xffffffff
008F6C4C 5f                   pop     edi
008F6C4D 8be5                 mov     esp, ebp
008F6C4F 5d                   pop     ebp
008F6C50 c3                   ret     
008F6C51 8b4768               mov     eax, dword ptr [edi + 0x68]
008F6C54 85c0                 test    eax, eax
008F6C56 7508                 jne     0x8f6c60
008F6C58 83c8ff               or      eax, 0xffffffff
008F6C5B 5f                   pop     edi
008F6C5C 8be5                 mov     esp, ebp
008F6C5E 5d                   pop     ebp
008F6C5F c3                   ret     
008F6C60 53                   push    ebx
008F6C61 8b5f64               mov     ebx, dword ptr [edi + 0x64]
008F6C64 8b03                 mov     eax, dword ptr [ebx]
008F6C66 3bd8                 cmp     ebx, eax
008F6C68 56                   push    esi
008F6C69 0f84cf000000         je      0x8f6d3e
008F6C6F 90                   nop     
008F6C70 8b4304               mov     eax, dword ptr [ebx + 4]
008F6C73 8b7008               mov     esi, dword ptr [eax + 8]
008F6C76 8a465c               mov     al, byte ptr [esi + 0x5c]
008F6C79 84c0                 test    al, al
008F6C7B 0f84af000000         je      0x8f6d30
008F6C81 8bce                 mov     ecx, esi
008F6C83 e8a8ffffff           call    0x8f6c30
008F6C88 83f8ff               cmp     eax, -1
008F6C8B 0f85b0000000         jne     0x8f6d41
008F6C91 8a4611               mov     al, byte ptr [esi + 0x11]
008F6C94 84c0                 test    al, al
008F6C96 0f8494000000         je      0x8f6d30
008F6C9C 8a4610               mov     al, byte ptr [esi + 0x10]
008F6C9F 84c0                 test    al, al
008F6CA1 0f8489000000         je      0x8f6d30
008F6CA7 8b4e14               mov     ecx, dword ptr [esi + 0x14]
008F6CAA 894dfc               mov     dword ptr [ebp - 4], ecx
008F6CAD e85e5a0000           call    0x8fc710
008F6CB2 3b45fc               cmp     eax, dword ptr [ebp - 4]
008F6CB5 7c4e                 jl      0x8f6d05
008F6CB7 8b5618               mov     edx, dword ptr [esi + 0x18]
008F6CBA 8955fc               mov     dword ptr [ebp - 4], edx
008F6CBD e85e5a0000           call    0x8fc720
008F6CC2 3b45fc               cmp     eax, dword ptr [ebp - 4]
008F6CC5 7c3e                 jl      0x8f6d05
008F6CC7 8b4614               mov     eax, dword ptr [esi + 0x14]
008F6CCA 8b4e1c               mov     ecx, dword ptr [esi + 0x1c]
008F6CCD 8945f8               mov     dword ptr [ebp - 8], eax
008F6CD0 894dfc               mov     dword ptr [ebp - 4], ecx
008F6CD3 e8385a0000           call    0x8fc710
008F6CD8 8b55fc               mov     edx, dword ptr [ebp - 4]
008F6CDB 8b4df8               mov     ecx, dword ptr [ebp - 8]
008F6CDE 8d540aff             lea     edx, [edx + ecx - 1]
008F6CE2 3bc2                 cmp     eax, edx
008F6CE4 7f1f                 jg      0x8f6d05
008F6CE6 8b4618               mov     eax, dword ptr [esi + 0x18]
008F6CE9 8b4e20               mov     ecx, dword ptr [esi + 0x20]
008F6CEC 8945fc               mov     dword ptr [ebp - 4], eax
008F6CEF 894df8               mov     dword ptr [ebp - 8], ecx
008F6CF2 e8295a0000           call    0x8fc720
008F6CF7 8b55f8               mov     edx, dword ptr [ebp - 8]
008F6CFA 8b4dfc               mov     ecx, dword ptr [ebp - 4]
008F6CFD 8d540aff             lea     edx, [edx + ecx - 1]
008F6D01 3bc2                 cmp     eax, edx
008F6D03 7e43                 jle     0x8f6d48
008F6D05 833df4d1cf0008       cmp     dword ptr [0xcfd1f4], 8
008F6D0C 750f                 jne     0x8f6d1d
008F6D0E 83c8ff               or      eax, 0xffffffff
008F6D11 a38451b500           mov     dword ptr [0xb55184], eax
008F6D16 a39051b500           mov     dword ptr [0xb55190], eax
008F6D1B eb13                 jmp     0x8f6d30
008F6D1D 833df8d1cf0010       cmp     dword ptr [0xcfd1f8], 0x10
008F6D24 750a                 jne     0x8f6d30
008F6D26 c7058851b500ffffffff mov     dword ptr [0xb55188], 0xffffffff
008F6D30 8b4764               mov     eax, dword ptr [edi + 0x64]
008F6D33 8b5b04               mov     ebx, dword ptr [ebx + 4]
008F6D36 3b18                 cmp     ebx, dword ptr [eax]
008F6D38 0f8532ffffff         jne     0x8f6c70
008F6D3E 83c8ff               or      eax, 0xffffffff
008F6D41 5e                   pop     esi
008F6D42 5b                   pop     ebx
008F6D43 5f                   pop     edi
008F6D44 8be5                 mov     esp, ebp
008F6D46 5d                   pop     ebp
008F6D47 c3                   ret     
008F6D48 a1f4d1cf00           mov     eax, dword ptr [0xcfd1f4]
008F6D4D 83f801               cmp     eax, 1
008F6D50 0f85b4000000         jne     0x8f6e0a
008F6D56 8b0d9c51b500         mov     ecx, dword ptr [0xb5519c]
008F6D5C 3b4e0c               cmp     ecx, dword ptr [esi + 0xc]
008F6D5F 0f847c010000         je      0x8f6ee1
008F6D65 a19051b500           mov     eax, dword ptr [0xb55190]
008F6D6A 83c9ff               or      ecx, 0xffffffff
008F6D6D 3bc1                 cmp     eax, ecx
008F6D6F 0f856c010000         jne     0x8f6ee1
008F6D75 8b570c               mov     edx, dword ptr [edi + 0xc]
008F6D78 8915a051b500         mov     dword ptr [0xb551a0], edx
008F6D7E 8b460c               mov     eax, dword ptr [esi + 0xc]
008F6D81 a39c51b500           mov     dword ptr [0xb5519c], eax
008F6D86 8b4608               mov     eax, dword ptr [esi + 8]
008F6D89 83f802               cmp     eax, 2
008F6D8C 7525                 jne     0x8f6db3
008F6D8E 56                   push    esi
008F6D8F 8bcf                 mov     ecx, edi
008F6D91 e88afbffff           call    0x8f6920
008F6D96 56                   push    esi
008F6D97 8bcf                 mov     ecx, edi
008F6D99 e8b2fdffff           call    0x8f6b50
008F6D9E 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6DA1 a19c51b500           mov     eax, dword ptr [0xb5519c]
008F6DA6 5e                   pop     esi
008F6DA7 5b                   pop     ebx
008F6DA8 89158c51b500         mov     dword ptr [0xb5518c], edx
008F6DAE 5f                   pop     edi
008F6DAF 8be5                 mov     esp, ebp
008F6DB1 5d                   pop     ebp
008F6DB2 c3                   ret     
008F6DB3 83f808               cmp     eax, 8
008F6DB6 7531                 jne     0x8f6de9
008F6DB8 8b4e0c               mov     ecx, dword ptr [esi + 0xc]
008F6DBB 890da451b500         mov     dword ptr [0xb551a4], ecx
008F6DC1 33c0                 xor     eax, eax
008F6DC3 b900010000           mov     ecx, 0x100
008F6DC8 bf2033cf00           mov     edi, 0xcf3320
008F6DCD f3ab                 rep stosd dword ptr es:[edi], eax
008F6DCF e8bc2b0000           call    0x8f9990
008F6DD4 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6DD7 a19c51b500           mov     eax, dword ptr [0xb5519c]
008F6DDC 5e                   pop     esi
008F6DDD 5b                   pop     ebx
008F6DDE 89158c51b500         mov     dword ptr [0xb5518c], edx
008F6DE4 5f                   pop     edi
008F6DE5 8be5                 mov     esp, ebp
008F6DE7 5d                   pop     ebp
008F6DE8 c3                   ret     
008F6DE9 837f080b             cmp     dword ptr [edi + 8], 0xb
008F6DED 7406                 je      0x8f6df5
008F6DEF 890db051b500         mov     dword ptr [0xb551b0], ecx
008F6DF5 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6DF8 a19c51b500           mov     eax, dword ptr [0xb5519c]
008F6DFD 5e                   pop     esi
008F6DFE 5b                   pop     ebx
008F6DFF 89158c51b500         mov     dword ptr [0xb5518c], edx
008F6E05 5f                   pop     edi
008F6E06 8be5                 mov     esp, ebp
008F6E08 5d                   pop     ebp
008F6E09 c3                   ret     
008F6E0A 83f808               cmp     eax, 8
008F6E0D 0f85ce000000         jne     0x8f6ee1
008F6E13 83caff               or      edx, 0xffffffff
008F6E16 8915ac51b500         mov     dword ptr [0xb551ac], edx
008F6E1C c705f4d1cf0000000000 mov     dword ptr [0xcfd1f4], 0
008F6E26 8b470c               mov     eax, dword ptr [edi + 0xc]
008F6E29 a3a051b500           mov     dword ptr [0xb551a0], eax
008F6E2E 8b4e0c               mov     ecx, dword ptr [esi + 0xc]
008F6E31 890d9c51b500         mov     dword ptr [0xb5519c], ecx
008F6E37 8b460c               mov     eax, dword ptr [esi + 0xc]
008F6E3A a38451b500           mov     dword ptr [0xb55184], eax
008F6E3F 8b4f08               mov     ecx, dword ptr [edi + 8]
008F6E42 83f907               cmp     ecx, 7
008F6E45 752e                 jne     0x8f6e75
008F6E47 8b4f0c               mov     ecx, dword ptr [edi + 0xc]
008F6E4A 890da851b500         mov     dword ptr [0xb551a8], ecx
008F6E50 837e0806             cmp     dword ptr [esi + 8], 6
008F6E54 7575                 jne     0x8f6ecb
008F6E56 8b4e0c               mov     ecx, dword ptr [esi + 0xc]
008F6E59 890db451b500         mov     dword ptr [0xb551b4], ecx
008F6E5F 89159051b500         mov     dword ptr [0xb55190], edx
008F6E65 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6E68 5e                   pop     esi
008F6E69 5b                   pop     ebx
008F6E6A 89159451b500         mov     dword ptr [0xb55194], edx
008F6E70 5f                   pop     edi
008F6E71 8be5                 mov     esp, ebp
008F6E73 5d                   pop     ebp
008F6E74 c3                   ret     
008F6E75 83f90c               cmp     ecx, 0xc
008F6E78 751f                 jne     0x8f6e99
008F6E7A 8b4f0c               mov     ecx, dword ptr [edi + 0xc]
008F6E7D 890db051b500         mov     dword ptr [0xb551b0], ecx
008F6E83 89159051b500         mov     dword ptr [0xb55190], edx
008F6E89 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6E8C 5e                   pop     esi
008F6E8D 5b                   pop     ebx
008F6E8E 89159451b500         mov     dword ptr [0xb55194], edx
008F6E94 5f                   pop     edi
008F6E95 8be5                 mov     esp, ebp
008F6E97 5d                   pop     ebp
008F6E98 c3                   ret     
008F6E99 83f902               cmp     ecx, 2
008F6E9C 751f                 jne     0x8f6ebd
008F6E9E 8b4f0c               mov     ecx, dword ptr [edi + 0xc]
008F6EA1 890dac51b500         mov     dword ptr [0xb551ac], ecx
008F6EA7 89159051b500         mov     dword ptr [0xb55190], edx
008F6EAD 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6EB0 5e                   pop     esi
008F6EB1 5b                   pop     ebx
008F6EB2 89159451b500         mov     dword ptr [0xb55194], edx
008F6EB8 5f                   pop     edi
008F6EB9 8be5                 mov     esp, ebp
008F6EBB 5d                   pop     ebp
008F6EBC c3                   ret     
008F6EBD 83f908               cmp     ecx, 8
008F6EC0 7509                 jne     0x8f6ecb
008F6EC2 8b4f0c               mov     ecx, dword ptr [edi + 0xc]
008F6EC5 890da451b500         mov     dword ptr [0xb551a4], ecx
008F6ECB 89159051b500         mov     dword ptr [0xb55190], edx
008F6ED1 8b560c               mov     edx, dword ptr [esi + 0xc]
008F6ED4 5e                   pop     esi
008F6ED5 5b                   pop     ebx
008F6ED6 89159451b500         mov     dword ptr [0xb55194], edx
008F6EDC 5f                   pop     edi
008F6EDD 8be5                 mov     esp, ebp
008F6EDF 5d                   pop     ebp
008F6EE0 c3                   ret     
008F6EE1 833df8d1cf0010       cmp     dword ptr [0xcfd1f8], 0x10
008F6EE8 7519                 jne     0x8f6f03
008F6EEA c705f8d1cf0000000000 mov     dword ptr [0xcfd1f8], 0
008F6EF4 8b460c               mov     eax, dword ptr [esi + 0xc]
008F6EF7 5e                   pop     esi
008F6EF8 5b                   pop     ebx
008F6EF9 a38851b500           mov     dword ptr [0xb55188], eax
008F6EFE 5f                   pop     edi
008F6EFF 8be5                 mov     esp, ebp
008F6F01 5d                   pop     ebp
008F6F02 c3                   ret     
008F6F03 8b460c               mov     eax, dword ptr [esi + 0xc]
008F6F06 5e                   pop     esi
008F6F07 5b                   pop     ebx
008F6F08 a38c51b500           mov     dword ptr [0xb5518c], eax
008F6F0D 5f                   pop     edi
008F6F0E 8be5                 mov     esp, ebp
008F6F10 5d                   pop     ebp
008F6F11 c3                   ret     

any_registered_ui_gate: 0x00799450, size 60
00799450 56                   push    esi
00799451 33f6                 xor     esi, esi
00799453 81c1d8000000         add     ecx, 0xd8
00799459 8da42400000000       lea     esp, [esp]
00799460 8b11                 mov     edx, dword ptr [ecx]
00799462 85d2                 test    edx, edx
00799464 740c                 je      0x799472
00799466 8b4104               mov     eax, dword ptr [ecx + 4]
00799469 2bc2                 sub     eax, edx
0079946B c1f802               sar     eax, 2
0079946E 85c0                 test    eax, eax
00799470 750d                 jne     0x79947f
00799472 46                   inc     esi
00799473 83c110               add     ecx, 0x10
00799476 83fe71               cmp     esi, 0x71
00799479 7ce5                 jl      0x799460
0079947B 32c0                 xor     al, al
0079947D 5e                   pop     esi
0079947E c3                   ret     
0079947F b001                 mov     al, 1
00799481 5e                   pop     esi
00799482 c3                   ret     
00799483 cc                   int3    
00799484 cc                   int3    
00799485 cc                   int3    
00799486 cc                   int3    
00799487 cc                   int3    
00799488 cc                   int3    
00799489 cc                   int3    
0079948A cc                   int3    
0079948B cc                   int3    

native_mp_user_bind: 0x007C86A0, size 88
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

native_mp_ui_getter_calls: 0x007C8E08, size 82
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
007C8E2D 8b0d4816bf00         mov     ecx, dword ptr [0xbf1648]
007C8E33 50                   push    eax
007C8E34 8b45fc               mov     eax, dword ptr [ebp - 4]
007C8E37 680cdea700           push    0xa7de0c
007C8E3C 50                   push    eax
007C8E3D 8b45f8               mov     eax, dword ptr [ebp - 8]
007C8E40 05d6000000           add     eax, 0xd6
007C8E45 50                   push    eax
007C8E46 8b45f0               mov     eax, dword ptr [ebp - 0x10]
007C8E49 05fe000000           add     eax, 0xfe
007C8E4E 50                   push    eax
007C8E4F 51                   push    ecx
007C8E50 ff5358               call    dword ptr [ebx + 0x58]
007C8E53 8b5df4               mov     ebx, dword ptr [ebp - 0xc]
007C8E56 83c41c               add     esp, 0x1c
007C8E59 8b                   .byte   0x8b

native_mp_getters: 0x00652A70, size 71
00652A70 81c170110000         add     ecx, 0x1170
00652A76 e9b5f5daff           jmp     0x402030
00652A7B cc                   int3    
00652A7C cc                   int3    
00652A7D cc                   int3    
00652A7E cc                   int3    
00652A7F cc                   int3    
00652A80 55                   push    ebp
00652A81 8bec                 mov     ebp, esp
00652A83 51                   push    ecx
00652A84 8b01                 mov     eax, dword ptr [ecx]
00652A86 56                   push    esi
00652A87 6a03                 push    3
00652A89 ff90e0020000         call    dword ptr [eax + 0x2e0]
00652A8F 8bf0                 mov     esi, eax
00652A91 8d4e18               lea     ecx, [esi + 0x18]
00652A94 e897f5daff           call    0x402030
00652A99 8945fc               mov     dword ptr [ebp - 4], eax
00652A9C db45fc               fild    dword ptr [ebp - 4]
00652A9F 8d4e20               lea     ecx, [esi + 0x20]
00652AA2 d95dfc               fstp    dword ptr [ebp - 4]
00652AA5 e8b657dcff           call    0x418260
00652AAA d84dfc               fmul    dword ptr [ebp - 4]
00652AAD e8568c2c00           call    0x91b708
00652AB2 5e                   pop     esi
00652AB3 8be5                 mov     esp, ebp
00652AB5 5d                   pop     ebp
00652AB6 c3                   ret     

native_stat_block_getter: 0x00652670, size 23
00652670 55                   push    ebp
00652671 8bec                 mov     ebp, esp
00652673 8b4508               mov     eax, dword ptr [ebp + 8]
00652676 69c038030000         imul    eax, eax, 0x338
0065267C 8d840888040000       lea     eax, [eax + ecx + 0x488]
00652683 5d                   pop     ebp
00652684 c20400               ret     4

party_create_class7_ctor: 0x007C0D90, size 116
007C0D90 55                   push    ebp
007C0D91 8bec                 mov     ebp, esp
007C0D93 6aff                 push    -1
007C0D95 68286a9800           push    0x986a28
007C0D9A 64a100000000         mov     eax, dword ptr fs:[0]
007C0DA0 50                   push    eax
007C0DA1 64892500000000       mov     dword ptr fs:[0], esp
007C0DA8 51                   push    ecx
007C0DA9 8b4508               mov     eax, dword ptr [ebp + 8]
007C0DAC 56                   push    esi
007C0DAD 8bf1                 mov     esi, ecx
007C0DAF 57                   push    edi
007C0DB0 33ff                 xor     edi, edi
007C0DB2 6a07                 push    7
007C0DB4 50                   push    eax
007C0DB5 8975f0               mov     dword ptr [ebp - 0x10], esi
007C0DB8 c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C0DBE c7460471000000       mov     dword ptr [esi + 4], 0x71
007C0DC5 897e08               mov     dword ptr [esi + 8], edi
007C0DC8 897e0c               mov     dword ptr [esi + 0xc], edi
007C0DCB 897e10               mov     dword ptr [esi + 0x10], edi
007C0DCE 897e14               mov     dword ptr [esi + 0x14], edi
007C0DD1 897e18               mov     dword ptr [esi + 0x18], edi
007C0DD4 897e1c               mov     dword ptr [esi + 0x1c], edi
007C0DD7 897e20               mov     dword ptr [esi + 0x20], edi
007C0DDA c74624ffffffff       mov     dword ptr [esi + 0x24], 0xffffffff
007C0DE1 e84acdffff           call    0x7bdb30
007C0DE6 8bce                 mov     ecx, esi
007C0DE8 897dfc               mov     dword ptr [ebp - 4], edi
007C0DEB c70614e6a700         mov     dword ptr [esi], 0xa7e614
007C0DF1 e8ba4d0100           call    0x7d5bb0
007C0DF6 8bce                 mov     ecx, esi
007C0DF8 e8a3580100           call    0x7d66a0
007C0DFD 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C0E00 5f                   pop     edi
007C0E01 8bc6                 mov     eax, esi
007C0E03 5e                   pop     esi

friend_addremove_class9_ctor: 0x007C0FF0, size 116
007C0FF0 55                   push    ebp
007C0FF1 8bec                 mov     ebp, esp
007C0FF3 6aff                 push    -1
007C0FF5 68286a9800           push    0x986a28
007C0FFA 64a100000000         mov     eax, dword ptr fs:[0]
007C1000 50                   push    eax
007C1001 64892500000000       mov     dword ptr fs:[0], esp
007C1008 51                   push    ecx
007C1009 8b4508               mov     eax, dword ptr [ebp + 8]
007C100C 56                   push    esi
007C100D 8bf1                 mov     esi, ecx
007C100F 57                   push    edi
007C1010 33ff                 xor     edi, edi
007C1012 6a09                 push    9
007C1014 50                   push    eax
007C1015 8975f0               mov     dword ptr [ebp - 0x10], esi
007C1018 c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C101E c7460471000000       mov     dword ptr [esi + 4], 0x71
007C1025 897e08               mov     dword ptr [esi + 8], edi
007C1028 897e0c               mov     dword ptr [esi + 0xc], edi
007C102B 897e10               mov     dword ptr [esi + 0x10], edi
007C102E 897e14               mov     dword ptr [esi + 0x14], edi
007C1031 897e18               mov     dword ptr [esi + 0x18], edi
007C1034 897e1c               mov     dword ptr [esi + 0x1c], edi
007C1037 897e20               mov     dword ptr [esi + 0x20], edi
007C103A c74624ffffffff       mov     dword ptr [esi + 0x24], 0xffffffff
007C1041 e8eacaffff           call    0x7bdb30
007C1046 8bce                 mov     ecx, esi
007C1048 897dfc               mov     dword ptr [ebp - 4], edi
007C104B c706b4e6a700         mov     dword ptr [esi], 0xa7e6b4
007C1051 e8aa870300           call    0x7f9800
007C1056 8bce                 mov     ecx, esi
007C1058 e8838b0300           call    0x7f9be0
007C105D 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C1060 5f                   pop     edi
007C1061 8bc6                 mov     eax, esi
007C1063 5e                   pop     esi

room_password_class56_ctor: 0x007C3300, size 116
007C3300 55                   push    ebp
007C3301 8bec                 mov     ebp, esp
007C3303 6aff                 push    -1
007C3305 68286a9800           push    0x986a28
007C330A 64a100000000         mov     eax, dword ptr fs:[0]
007C3310 50                   push    eax
007C3311 64892500000000       mov     dword ptr fs:[0], esp
007C3318 51                   push    ecx
007C3319 8b4508               mov     eax, dword ptr [ebp + 8]
007C331C 56                   push    esi
007C331D 8bf1                 mov     esi, ecx
007C331F 57                   push    edi
007C3320 33ff                 xor     edi, edi
007C3322 6a56                 push    0x56
007C3324 50                   push    eax
007C3325 8975f0               mov     dword ptr [ebp - 0x10], esi
007C3328 c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C332E c7460471000000       mov     dword ptr [esi + 4], 0x71
007C3335 897e08               mov     dword ptr [esi + 8], edi
007C3338 897e0c               mov     dword ptr [esi + 0xc], edi
007C333B 897e10               mov     dword ptr [esi + 0x10], edi
007C333E 897e14               mov     dword ptr [esi + 0x14], edi
007C3341 897e18               mov     dword ptr [esi + 0x18], edi
007C3344 897e1c               mov     dword ptr [esi + 0x1c], edi
007C3347 897e20               mov     dword ptr [esi + 0x20], edi
007C334A c74624ffffffff       mov     dword ptr [esi + 0x24], 0xffffffff
007C3351 e8daa7ffff           call    0x7bdb30
007C3356 8bce                 mov     ecx, esi
007C3358 897dfc               mov     dword ptr [ebp - 4], edi
007C335B c70654f1a700         mov     dword ptr [esi], 0xa7f154
007C3361 e82a4b0400           call    0x807e90
007C3366 8bce                 mov     ecx, esi
007C3368 e8d34e0400           call    0x808240
007C336D 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C3370 5f                   pop     edi
007C3371 8bc6                 mov     eax, esi
007C3373 5e                   pop     esi
