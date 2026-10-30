
command_physical_gate_complete: [0x008BA7F0,0x008BA8F9) size=265
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

command_priority_setter_complete: [0x008BA030,0x008BA08E) size=94
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

command_lock_getter_complete: [0x0043BC30,0x0043BC58) size=40
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

command_init_complete: [0x008BA990,0x008BA9F0) size=96
008BA990 33c0                 xor     eax, eax
008BA992 894164               mov     dword ptr [ecx + 0x64], eax
008BA995 894168               mov     dword ptr [ecx + 0x68], eax
008BA998 89416c               mov     dword ptr [ecx + 0x6c], eax
008BA99B 894170               mov     dword ptr [ecx + 0x70], eax
008BA99E 89818c000000         mov     dword ptr [ecx + 0x8c], eax
008BA9A4 888190000000         mov     byte ptr [ecx + 0x90], al
008BA9AA c7416010270000       mov     dword ptr [ecx + 0x60], 0x2710
008BA9B1 8b8198000000         mov     eax, dword ptr [ecx + 0x98]
008BA9B7 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
008BA9BD 8b5244               mov     edx, dword ptr [edx + 0x44]
008BA9C0 8d0482               lea     eax, [edx + eax*4]
008BA9C3 56                   push    esi
008BA9C4 8b355cd4cf00         mov     esi, dword ptr [0xcfd45c]
008BA9CA 8bd0                 mov     edx, eax
008BA9CC 33d6                 xor     edx, esi
008BA9CE 8910                 mov     dword ptr [eax], edx
008BA9D0 8b8198000000         mov     eax, dword ptr [ecx + 0x98]
008BA9D6 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
008BA9DC 8b5148               mov     edx, dword ptr [ecx + 0x48]
008BA9DF 8d0482               lea     eax, [edx + eax*4]
008BA9E2 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
008BA9E8 8bc8                 mov     ecx, eax
008BA9EA 33ca                 xor     ecx, edx
008BA9EC 8908                 mov     dword ptr [eax], ecx
008BA9EE 5e                   pop     esi
008BA9EF c3                   ret     

alternate_checker_install_complete: [0x0048F790,0x0048FADA) size=842
0048F790 55                   push    ebp
0048F791 8bec                 mov     ebp, esp
0048F793 6aff                 push    -1
0048F795 680c039600           push    0x96030c
0048F79A 64a100000000         mov     eax, dword ptr fs:[0]
0048F7A0 50                   push    eax
0048F7A1 64892500000000       mov     dword ptr fs:[0], esp
0048F7A8 51                   push    ecx
0048F7A9 53                   push    ebx
0048F7AA 56                   push    esi
0048F7AB 8bf1                 mov     esi, ecx
0048F7AD 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048F7B3 57                   push    edi
0048F7B4 33ff                 xor     edi, edi
0048F7B6 3bcf                 cmp     ecx, edi
0048F7B8 7406                 je      0x48f7c0
0048F7BA 8b01                 mov     eax, dword ptr [ecx]
0048F7BC 6a01                 push    1
0048F7BE ff10                 call    dword ptr [eax]
0048F7C0 6808090000           push    0x908
0048F7C5 89be30240000         mov     dword ptr [esi + 0x2430], edi
0048F7CB e8aa8d4800           call    0x91857a
0048F7D0 83c404               add     esp, 4
0048F7D3 8945f0               mov     dword ptr [ebp - 0x10], eax
0048F7D6 3bc7                 cmp     eax, edi
0048F7D8 897dfc               mov     dword ptr [ebp - 4], edi
0048F7DB 7409                 je      0x48f7e6
0048F7DD 8bc8                 mov     ecx, eax
0048F7DF e83c280000           call    0x492020
0048F7E4 eb02                 jmp     0x48f7e8
0048F7E6 33c0                 xor     eax, eax
0048F7E8 83cbff               or      ebx, 0xffffffff
0048F7EB 689c000000           push    0x9c
0048F7F0 895dfc               mov     dword ptr [ebp - 4], ebx
0048F7F3 898630240000         mov     dword ptr [esi + 0x2430], eax
0048F7F9 e87c8d4800           call    0x91857a
0048F7FE 83c404               add     esp, 4
0048F801 8945f0               mov     dword ptr [ebp - 0x10], eax
0048F804 3bc7                 cmp     eax, edi
0048F806 c745fc01000000       mov     dword ptr [ebp - 4], 1
0048F80D 7409                 je      0x48f818
0048F80F 8bc8                 mov     ecx, eax
0048F811 e81ac3faff           call    0x43bb30
0048F816 8bf8                 mov     edi, eax
0048F818 6a01                 push    1
0048F81A 57                   push    edi
0048F81B 895dfc               mov     dword ptr [ebp - 4], ebx
0048F81E c6879000000001       mov     byte ptr [edi + 0x90], 1
0048F825 e866b80000           call    0x49b090
0048F82A 8b16                 mov     edx, dword ptr [esi]
0048F82C 83c408               add     esp, 8
0048F82F 57                   push    edi
0048F830 8bce                 mov     ecx, esi
0048F832 ff92e4050000         call    dword ptr [edx + 0x5e4]
0048F838 8b06                 mov     eax, dword ptr [esi]
0048F83A 8bce                 mov     ecx, esi
0048F83C ff90d4050000         call    dword ptr [eax + 0x5d4]
0048F842 6a6c                 push    0x6c
0048F844 c6809000000001       mov     byte ptr [eax + 0x90], 1
0048F84B e82a8d4800           call    0x91857a
0048F850 83c404               add     esp, 4
0048F853 8945f0               mov     dword ptr [ebp - 0x10], eax
0048F856 85c0                 test    eax, eax
0048F858 c745fc02000000       mov     dword ptr [ebp - 4], 2
0048F85F 740e                 je      0x48f86f
0048F861 68d8000000           push    0xd8
0048F866 8bc8                 mov     ecx, eax
0048F868 e8133cfaff           call    0x433480
0048F86D eb02                 jmp     0x48f871
0048F86F 33c0                 xor     eax, eax
0048F871 8b16                 mov     edx, dword ptr [esi]
0048F873 50                   push    eax
0048F874 8bce                 mov     ecx, esi
0048F876 895dfc               mov     dword ptr [ebp - 4], ebx
0048F879 ff92e8050000         call    dword ptr [edx + 0x5e8]
0048F87F 8b06                 mov     eax, dword ptr [esi]
0048F881 8bce                 mov     ecx, esi
0048F883 ff90d8050000         call    dword ptr [eax + 0x5d8]
0048F889 89700c               mov     dword ptr [eax + 0xc], esi
0048F88C 8b16                 mov     edx, dword ptr [esi]
0048F88E 8bce                 mov     ecx, esi
0048F890 ff92d4050000         call    dword ptr [edx + 0x5d4]
0048F896 50                   push    eax
0048F897 8b06                 mov     eax, dword ptr [esi]
0048F899 8bce                 mov     ecx, esi
0048F89B ff90d8050000         call    dword ptr [eax + 0x5d8]
0048F8A1 8bc8                 mov     ecx, eax
0048F8A3 e868c40600           call    0x4fbd10
0048F8A8 8b16                 mov     edx, dword ptr [esi]
0048F8AA 8bce                 mov     ecx, esi
0048F8AC ff9218060000         call    dword ptr [edx + 0x618]
0048F8B2 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048F8B8 81c17c070000         add     ecx, 0x77c
0048F8BE e84d5af7ff           call    0x405310
0048F8C3 8b8630240000         mov     eax, dword ptr [esi + 0x2430]
0048F8C9 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0048F8CF 8b5144               mov     edx, dword ptr [ecx + 0x44]
0048F8D2 8b8890070000         mov     ecx, dword ptr [eax + 0x790]
0048F8D8 057c070000           add     eax, 0x77c
0048F8DD c1e102               shl     ecx, 2
0048F8E0 8d3c11               lea     edi, [ecx + edx]
0048F8E3 333d5cd4cf00         xor     edi, dword ptr [0xcfd45c]
0048F8E9 6a00                 push    0
0048F8EB 83f701               xor     edi, 1
0048F8EE 893c11               mov     dword ptr [ecx + edx], edi
0048F8F1 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
0048F8F7 8b4a48               mov     ecx, dword ptr [edx + 0x48]
0048F8FA 8b4014               mov     eax, dword ptr [eax + 0x14]
0048F8FD 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
0048F903 c1e002               shl     eax, 2
0048F906 8d1408               lea     edx, [eax + ecx]
0048F909 33d7                 xor     edx, edi
0048F90B 83f201               xor     edx, 1
0048F90E 891408               mov     dword ptr [eax + ecx], edx
0048F911 8b8630240000         mov     eax, dword ptr [esi + 0x2430]
0048F917 8b8850030000         mov     ecx, dword ptr [eax + 0x350]
0048F91D 51                   push    ecx
0048F91E 8d887c070000         lea     ecx, [eax + 0x77c]
0048F924 e857454600           call    0x8f3e80
0048F929 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048F92F 81c1bc070000         add     ecx, 0x7bc
0048F935 e8d659f7ff           call    0x405310
0048F93A 8b8630240000         mov     eax, dword ptr [esi + 0x2430]
0048F940 8b88d0070000         mov     ecx, dword ptr [eax + 0x7d0]
0048F946 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
0048F94C 8b5244               mov     edx, dword ptr [edx + 0x44]
0048F94F 05bc070000           add     eax, 0x7bc
0048F954 c1e102               shl     ecx, 2
0048F957 8d3c11               lea     edi, [ecx + edx]
0048F95A 333d5cd4cf00         xor     edi, dword ptr [0xcfd45c]
0048F960 6a00                 push    0
0048F962 83f701               xor     edi, 1
0048F965 893c11               mov     dword ptr [ecx + edx], edi
0048F968 8b4014               mov     eax, dword ptr [eax + 0x14]
0048F96B 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0048F971 8b4948               mov     ecx, dword ptr [ecx + 0x48]
0048F974 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
0048F97A c1e002               shl     eax, 2
0048F97D 8d1408               lea     edx, [eax + ecx]
0048F980 33d7                 xor     edx, edi
0048F982 83f201               xor     edx, 1
0048F985 891408               mov     dword ptr [eax + ecx], edx
0048F988 8b8630240000         mov     eax, dword ptr [esi + 0x2430]
0048F98E 8b8854030000         mov     ecx, dword ptr [eax + 0x354]
0048F994 51                   push    ecx
0048F995 8d88bc070000         lea     ecx, [eax + 0x7bc]
0048F99B e8e0444600           call    0x8f3e80
0048F9A0 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048F9A6 81c128080000         add     ecx, 0x828
0048F9AC e85f59f7ff           call    0x405310
0048F9B1 8b8630240000         mov     eax, dword ptr [esi + 0x2430]
0048F9B7 8b883c080000         mov     ecx, dword ptr [eax + 0x83c]
0048F9BD 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
0048F9C3 8b5244               mov     edx, dword ptr [edx + 0x44]
0048F9C6 0528080000           add     eax, 0x828
0048F9CB c1e102               shl     ecx, 2
0048F9CE 8d3c11               lea     edi, [ecx + edx]
0048F9D1 333d5cd4cf00         xor     edi, dword ptr [0xcfd45c]
0048F9D7 6a00                 push    0
0048F9D9 83f701               xor     edi, 1
0048F9DC 893c11               mov     dword ptr [ecx + edx], edi
0048F9DF 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0048F9E5 8b4948               mov     ecx, dword ptr [ecx + 0x48]
0048F9E8 8b4014               mov     eax, dword ptr [eax + 0x14]
0048F9EB 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
0048F9F1 c1e002               shl     eax, 2
0048F9F4 8d1408               lea     edx, [eax + ecx]
0048F9F7 33d7                 xor     edx, edi
0048F9F9 83f201               xor     edx, 1
0048F9FC 891408               mov     dword ptr [eax + ecx], edx
0048F9FF 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048FA05 68e8030000           push    0x3e8
0048FA0A 81c128080000         add     ecx, 0x828
0048FA10 e86b444600           call    0x8f3e80
0048FA15 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048FA1B 81c174080000         add     ecx, 0x874
0048FA21 e8ea58f7ff           call    0x405310
0048FA26 8b8630240000         mov     eax, dword ptr [esi + 0x2430]
0048FA2C 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0048FA32 8b5144               mov     edx, dword ptr [ecx + 0x44]
0048FA35 8b8888080000         mov     ecx, dword ptr [eax + 0x888]
0048FA3B 0574080000           add     eax, 0x874
0048FA40 c1e102               shl     ecx, 2
0048FA43 8d3c11               lea     edi, [ecx + edx]
0048FA46 333d5cd4cf00         xor     edi, dword ptr [0xcfd45c]
0048FA4C 6814010000           push    0x114
0048FA51 83f701               xor     edi, 1
0048FA54 893c11               mov     dword ptr [ecx + edx], edi
0048FA57 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
0048FA5D 8b4a48               mov     ecx, dword ptr [edx + 0x48]
0048FA60 8b4014               mov     eax, dword ptr [eax + 0x14]
0048FA63 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
0048FA69 c1e002               shl     eax, 2
0048FA6C 8d1408               lea     edx, [eax + ecx]
0048FA6F 33d7                 xor     edx, edi
0048FA71 83f201               xor     edx, 1
0048FA74 891408               mov     dword ptr [eax + ecx], edx
0048FA77 e8fe8a4800           call    0x91857a
0048FA7C 8bf8                 mov     edi, eax
0048FA7E 83c404               add     esp, 4
0048FA81 897df0               mov     dword ptr [ebp - 0x10], edi
0048FA84 85ff                 test    edi, edi
0048FA86 c745fc03000000       mov     dword ptr [ebp - 4], 3
0048FA8D 7411                 je      0x48faa0
0048FA8F 8b06                 mov     eax, dword ptr [esi]
0048FA91 8bce                 mov     ecx, esi
0048FA93 ff5038               call    dword ptr [eax + 0x38]
0048FA96 50                   push    eax
0048FA97 8bcf                 mov     ecx, edi
0048FA99 e862120000           call    0x490d00
0048FA9E eb02                 jmp     0x48faa2
0048FAA0 33c0                 xor     eax, eax
0048FAA2 8b8e30240000         mov     ecx, dword ptr [esi + 0x2430]
0048FAA8 898104090000         mov     dword ptr [ecx + 0x904], eax
0048FAAE 8bb630240000         mov     esi, dword ptr [esi + 0x2430]
0048FAB4 8b8e04090000         mov     ecx, dword ptr [esi + 0x904]
0048FABA 8d5620               lea     edx, [esi + 0x20]
0048FABD 52                   push    edx
0048FABE 83c110               add     ecx, 0x10
0048FAC1 895dfc               mov     dword ptr [ebp - 4], ebx
0048FAC4 e8878bf8ff           call    0x418650
0048FAC9 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
0048FACC 5f                   pop     edi
0048FACD 5e                   pop     esi
0048FACE 5b                   pop     ebx
0048FACF 64890d00000000       mov     dword ptr fs:[0], ecx
0048FAD6 8be5                 mov     esp, ebp
0048FAD8 5d                   pop     ebp
0048FAD9 c3                   ret     

actor_checker_pointer_setter_complete: [0x00493B50,0x00493B60) size=16
00493B50 55                   push    ebp
00493B51 8bec                 mov     ebp, esp
00493B53 8b4508               mov     eax, dword ptr [ebp + 8]
00493B56 898184230000         mov     dword ptr [ecx + 0x2384], eax
00493B5C 5d                   pop     ebp
00493B5D c20400               ret     4

engine_mouse_update_complete: [0x008FCAA0,0x008FCC5E) size=446
008FCAA0 55                   push    ebp
008FCAA1 8bec                 mov     ebp, esp
008FCAA3 a00ad2cf00           mov     al, byte ptr [0xcfd20a]
008FCAA8 53                   push    ebx
008FCAA9 56                   push    esi
008FCAAA 8b7508               mov     esi, dword ptr [ebp + 8]
008FCAAD b901000000           mov     ecx, 1
008FCAB2 3ac1                 cmp     al, cl
008FCAB4 57                   push    edi
008FCAB5 8b7d10               mov     edi, dword ptr [ebp + 0x10]
008FCAB8 0f85a0000000         jne     0x8fcb5e
008FCABE 8b0d0cd2cf00         mov     ecx, dword ptr [0xcfd20c]
008FCAC4 e8f7fcffff           call    0x8fc7c0
008FCAC9 8b0d0cd2cf00         mov     ecx, dword ptr [0xcfd20c]
008FCACF 8b4120               mov     eax, dword ptr [ecx + 0x20]
008FCAD2 8b1510d2cf00         mov     edx, dword ptr [0xcfd210]
008FCAD8 8b1d14d2cf00         mov     ebx, dword ptr [0xcfd214]
008FCADE 03c2                 add     eax, edx
008FCAE0 a3ecd1cf00           mov     dword ptr [0xcfd1ec], eax
008FCAE5 8b5124               mov     edx, dword ptr [ecx + 0x24]
008FCAE8 a008d2cf00           mov     al, byte ptr [0xcfd208]
008FCAED 03d3                 add     edx, ebx
008FCAEF 84c0                 test    al, al
008FCAF1 8915f0d1cf00         mov     dword ptr [0xcfd1f0], edx
008FCAF7 0f8548010000         jne     0x8fcc45
008FCAFD 8a4118               mov     al, byte ptr [ecx + 0x18]
008FCB00 8a1501d2cf00         mov     dl, byte ptr [0xcfd201]
008FCB06 c0e807               shr     al, 7
008FCB09 3ad0                 cmp     dl, al
008FCB0B 741c                 je      0x8fcb29
008FCB0D 8ad0                 mov     dl, al
008FCB0F f6da                 neg     dl
008FCB11 a200d2cf00           mov     byte ptr [0xcfd200], al
008FCB16 a201d2cf00           mov     byte ptr [0xcfd201], al
008FCB1B 1bd2                 sbb     edx, edx
008FCB1D 83e2f9               and     edx, 0xfffffff9
008FCB20 83c208               add     edx, 8
008FCB23 8915f4d1cf00         mov     dword ptr [0xcfd1f4], edx
008FCB29 8a4119               mov     al, byte ptr [ecx + 0x19]
008FCB2C 8a0d03d2cf00         mov     cl, byte ptr [0xcfd203]
008FCB32 c0e807               shr     al, 7
008FCB35 3ac8                 cmp     cl, al
008FCB37 0f8408010000         je      0x8fcc45
008FCB3D 8ac8                 mov     cl, al
008FCB3F f6d9                 neg     cl
008FCB41 a202d2cf00           mov     byte ptr [0xcfd202], al
008FCB46 a203d2cf00           mov     byte ptr [0xcfd203], al
008FCB4B 1bc9                 sbb     ecx, ecx
008FCB4D 83e1f2               and     ecx, 0xfffffff2
008FCB50 83c110               add     ecx, 0x10
008FCB53 890df8d1cf00         mov     dword ptr [0xcfd1f8], ecx
008FCB59 e9e7000000           jmp     0x8fcc45
008FCB5E 8b1d10d2cf00         mov     ebx, dword ptr [0xcfd210]
008FCB64 0fbfd7               movsx   edx, di
008FCB67 03d3                 add     edx, ebx
008FCB69 8b1d14d2cf00         mov     ebx, dword ptr [0xcfd214]
008FCB6F 8bc7                 mov     eax, edi
008FCB71 c1e810               shr     eax, 0x10
008FCB74 8915ecd1cf00         mov     dword ptr [0xcfd1ec], edx
008FCB7A 0fbfd0               movsx   edx, ax
008FCB7D a008d2cf00           mov     al, byte ptr [0xcfd208]
008FCB82 03d3                 add     edx, ebx
008FCB84 8915f0d1cf00         mov     dword ptr [0xcfd1f0], edx
008FCB8A 32d2                 xor     dl, dl
008FCB8C 3ac2                 cmp     al, dl
008FCB8E 755d                 jne     0x8fcbed
008FCB90 8d86fffdffff         lea     eax, [esi - 0x201]
008FCB96 83f804               cmp     eax, 4
008FCB99 0f87a6000000         ja      0x8fcc45
008FCB9F ff248560cc8f00       jmp     dword ptr [eax*4 + 0x8fcc60]
008FCBA6 890df4d1cf00         mov     dword ptr [0xcfd1f4], ecx
008FCBAC 880d00d2cf00         mov     byte ptr [0xcfd200], cl
008FCBB2 e98e000000           jmp     0x8fcc45
008FCBB7 c705f8d1cf0002000000 mov     dword ptr [0xcfd1f8], 2
008FCBC1 880d02d2cf00         mov     byte ptr [0xcfd202], cl
008FCBC7 eb7c                 jmp     0x8fcc45
008FCBC9 c705f4d1cf0008000000 mov     dword ptr [0xcfd1f4], 8
008FCBD3 881500d2cf00         mov     byte ptr [0xcfd200], dl
008FCBD9 eb6a                 jmp     0x8fcc45
008FCBDB c705f8d1cf0010000000 mov     dword ptr [0xcfd1f8], 0x10
008FCBE5 881502d2cf00         mov     byte ptr [0xcfd202], dl
008FCBEB eb58                 jmp     0x8fcc45
008FCBED 8b0d2cd2cf00         mov     ecx, dword ptr [0xcfd22c]
008FCBF3 a130d2cf00           mov     eax, dword ptr [0xcfd230]
008FCBF8 03c1                 add     eax, ecx
008FCBFA 8b0d24d2cf00         mov     ecx, dword ptr [0xcfd224]
008FCC00 8bd9                 mov     ebx, ecx
008FCC02 8d5001               lea     edx, [eax + 1]
008FCC05 c1e308               shl     ebx, 8
008FCC08 3bd3                 cmp     edx, ebx
008FCC0A 721d                 jb      0x8fcc29
008FCC0C 41                   inc     ecx
008FCC0D 51                   push    ecx
008FCC0E b920d2cf00           mov     ecx, 0xcfd220
008FCC13 e8a81db8ff           call    0x47e9c0
008FCC18 84c0                 test    al, al
008FCC1A 7429                 je      0x8fcc45
008FCC1C 8b1530d2cf00         mov     edx, dword ptr [0xcfd230]
008FCC22 a12cd2cf00           mov     eax, dword ptr [0xcfd22c]
008FCC27 03c2                 add     eax, edx
008FCC29 8b1528d2cf00         mov     edx, dword ptr [0xcfd228]
008FCC2F 8bc8                 mov     ecx, eax
008FCC31 c1e908               shr     ecx, 8
008FCC34 8b0c8a               mov     ecx, dword ptr [edx + ecx*4]
008FCC37 25ff000000           and     eax, 0xff
008FCC3C 893481               mov     dword ptr [ecx + eax*4], esi
008FCC3F ff052cd2cf00         inc     dword ptr [0xcfd22c]
008FCC45 8b0d04d2cf00         mov     ecx, dword ptr [0xcfd204]
008FCC4B 85c9                 test    ecx, ecx
008FCC4D 740a                 je      0x8fcc59
008FCC4F 8b450c               mov     eax, dword ptr [ebp + 0xc]
008FCC52 8b11                 mov     edx, dword ptr [ecx]
008FCC54 57                   push    edi
008FCC55 50                   push    eax
008FCC56 56                   push    esi
008FCC57 ff12                 call    dword ptr [edx]
008FCC59 5f                   pop     edi
008FCC5A 5e                   pop     esi
008FCC5B 5b                   pop     ebx
008FCC5C 5d                   pop     ebp
008FCC5D c3                   ret     

engine_mouse_message_jump_table: [0x008FCC60,0x008FCC74) size=20
008FCC60 008FCBA6
008FCC64 008FCBC9
008FCC68 008FCC45
008FCC6C 008FCBB7
008FCC70 008FCBDB

engine_mouse_release_complete: [0x008FCC80,0x008FCD60) size=224
008FCC80 55                   push    ebp
008FCC81 8bec                 mov     ebp, esp
008FCC83 83ec0c               sub     esp, 0xc
008FCC86 a1f0d1cf00           mov     eax, dword ptr [0xcfd1f0]
008FCC8B 8945fc               mov     dword ptr [ebp - 4], eax
008FCC8E a12cd2cf00           mov     eax, dword ptr [0xcfd22c]
008FCC93 85c0                 test    eax, eax
008FCC95 56                   push    esi
008FCC96 57                   push    edi
008FCC97 8b3decd1cf00         mov     edi, dword ptr [0xcfd1ec]
008FCC9D c60508d2cf0000       mov     byte ptr [0xcfd208], 0
008FCCA4 0f847c000000         je      0x8fcd26
008FCCAA a130d2cf00           mov     eax, dword ptr [0xcfd230]
008FCCAF 53                   push    ebx
008FCCB0 8b1528d2cf00         mov     edx, dword ptr [0xcfd228]
008FCCB6 8bc8                 mov     ecx, eax
008FCCB8 c1e908               shr     ecx, 8
008FCCBB 8b0c8a               mov     ecx, dword ptr [edx + ecx*4]
008FCCBE 6a00                 push    0
008FCCC0 25ff000000           and     eax, 0xff
008FCCC5 8b1481               mov     edx, dword ptr [ecx + eax*4]
008FCCC8 6a00                 push    0
008FCCCA 52                   push    edx
008FCCCB e8d0fdffff           call    0x8fcaa0
008FCCD0 a12cd2cf00           mov     eax, dword ptr [0xcfd22c]
008FCCD5 83c40c               add     esp, 0xc
008FCCD8 85c0                 test    eax, eax
008FCCDA 7449                 je      0x8fcd25
008FCCDC a130d2cf00           mov     eax, dword ptr [0xcfd230]
008FCCE1 40                   inc     eax
008FCCE2 3d00010000           cmp     eax, 0x100
008FCCE7 a330d2cf00           mov     dword ptr [0xcfd230], eax
008FCCEC 752f                 jne     0x8fcd1d
008FCCEE 8b1524d2cf00         mov     edx, dword ptr [0xcfd224]
008FCCF4 8b0d28d2cf00         mov     ecx, dword ptr [0xcfd228]
008FCCFA 8b31                 mov     esi, dword ptr [ecx]
008FCCFC 33c0                 xor     eax, eax
008FCCFE 4a                   dec     edx
008FCCFF 7412                 je      0x8fcd13
008FCD01 8b5c8104             mov     ebx, dword ptr [ecx + eax*4 + 4]
008FCD05 891c81               mov     dword ptr [ecx + eax*4], ebx
008FCD08 8b0d28d2cf00         mov     ecx, dword ptr [0xcfd228]
008FCD0E 40                   inc     eax
008FCD0F 3bc2                 cmp     eax, edx
008FCD11 72ee                 jb      0x8fcd01
008FCD13 893481               mov     dword ptr [ecx + eax*4], esi
008FCD16 33c0                 xor     eax, eax
008FCD18 a330d2cf00           mov     dword ptr [0xcfd230], eax
008FCD1D ff0d2cd2cf00         dec     dword ptr [0xcfd22c]
008FCD23 758b                 jne     0x8fccb0
008FCD25 5b                   pop     ebx
008FCD26 8b0dfcd1cf00         mov     ecx, dword ptr [0xcfd1fc]
008FCD2C 8b75fc               mov     esi, dword ptr [ebp - 4]
008FCD2F 8d45f4               lea     eax, [ebp - 0xc]
008FCD32 50                   push    eax
008FCD33 51                   push    ecx
008FCD34 897df4               mov     dword ptr [ebp - 0xc], edi
008FCD37 8975f8               mov     dword ptr [ebp - 8], esi
008FCD3A ff1588849900         call    dword ptr [0x998488]
008FCD40 8b55f8               mov     edx, dword ptr [ebp - 8]
008FCD43 8b45f4               mov     eax, dword ptr [ebp - 0xc]
008FCD46 52                   push    edx
008FCD47 50                   push    eax
008FCD48 ff1584849900         call    dword ptr [0x998484]
008FCD4E 893decd1cf00         mov     dword ptr [0xcfd1ec], edi
008FCD54 5f                   pop     edi
008FCD55 8935f0d1cf00         mov     dword ptr [0xcfd1f0], esi
008FCD5B 5e                   pop     esi
008FCD5C 8be5                 mov     esp, ebp
008FCD5E 5d                   pop     ebp
008FCD5F c3                   ret     

ui_set_waiting_complete: [0x007993B0,0x007993F7) size=71
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

ui_release_waiting_complete: [0x00799400,0x00799445) size=69
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

notify_dispatch_prefix: [0x00419420,0x0041947B) size=91
00419420 55                   push    ebp
00419421 8bec                 mov     ebp, esp
00419423 6aff                 push    -1
00419425 68eab49500           push    0x95b4ea
0041942A 64a100000000         mov     eax, dword ptr fs:[0]
00419430 50                   push    eax
00419431 64892500000000       mov     dword ptr fs:[0], esp
00419438 b840620000           mov     eax, 0x6240
0041943D e8fe1b5000           call    0x91b040
00419442 53                   push    ebx
00419443 56                   push    esi
00419444 57                   push    edi
00419445 898df09effff         mov     dword ptr [ebp - 0x6110], ecx
0041944B 8b4508               mov     eax, dword ptr [ebp + 8]
0041944E 8a4801               mov     cl, byte ptr [eax + 1]
00419451 884df3               mov     byte ptr [ebp - 0xd], cl
00419454 0fb655f3             movzx   edx, byte ptr [ebp - 0xd]
00419458 8995ec9effff         mov     dword ptr [ebp - 0x6114], edx
0041945E 81bdec9effff97000000 cmp     dword ptr [ebp - 0x6114], 0x97
00419468 0f87fb0b0100         ja      0x42a069
0041946E 8b85ec9effff         mov     eax, dword ptr [ebp - 0x6114]
00419474 ff248581a04200       jmp     dword ptr [eax*4 + 0x42a081]

notify_dispatch_table: [0x0042A081,0x0042A2E1) size=608
0042A081 0041947B
0042A085 004194C8
0042A089 00419A57
0042A08D 0041B0D3
0042A091 0041B1CE
0042A095 0041B255
0042A099 0041B3C8
0042A09D 0041B4C8
0042A0A1 0041B5F1
0042A0A5 0041B7A1
0042A0A9 0041C41A
0042A0AD 0041C7E7
0042A0B1 0041C994
0042A0B5 0041D01C
0042A0B9 0041D237
0042A0BD 0041D942
0042A0C1 0041DC2E
0042A0C5 0041E0C1
0042A0C9 0041E120
0042A0CD 0041E3C8
0042A0D1 0041E65D
0042A0D5 0041E768
0042A0D9 0041E878
0042A0DD 0041E9C7
0042A0E1 0041EC7C
0042A0E5 0042A069
0042A0E9 0041F144
0042A0ED 0041F16C
0042A0F1 0041F550
0042A0F5 0041F7D3
0042A0F9 004200B6
0042A0FD 0041FE50
0042A101 00420202
0042A105 00420A81
0042A109 00420B0F
0042A10D 00420C23
0042A111 0042119F
0042A115 0042127C
0042A119 004217CE
0042A11D 00421EA7
0042A121 00422CC3
0042A125 00422E9D
0042A129 00423491
0042A12D 0042360F
0042A131 00423AD9
0042A135 00423B27
0042A139 004246E2
0042A13D 00424765
0042A141 00424EAA
0042A145 00425098
0042A149 004253A2
0042A14D 004253DC
0042A151 0042A069
0042A155 004255BA
0042A159 00425794
0042A15D 0042580C
0042A161 00425668
0042A165 00425876
0042A169 00425E01
0042A16D 00425CF3
0042A171 00425F8E
0042A175 00425F03
0042A179 0042A069
0042A17D 0042A069
0042A181 00425C50
0042A185 0041CD9D
0042A189 00425953
0042A18D 0042A069
0042A191 004258AD
0042A195 00425FCE
0042A199 0042609C
0042A19D 00426314
0042A1A1 004263D4
0042A1A5 00426477
0042A1A9 004264D2
0042A1AD 004265C6
0042A1B1 0042665A
0042A1B5 004265DC
0042A1B9 004268E1
0042A1BD 004269F0
0042A1C1 00426A87
0042A1C5 00426A91
0042A1C9 00426B46
0042A1CD 00426C97
0042A1D1 00426CE5
0042A1D5 00426F1E
0042A1D9 00427104
0042A1DD 0042318D
0042A1E1 004237EE
0042A1E5 00423946
0042A1E9 00423BDA
0042A1ED 00423BEA
0042A1F1 00424D4B
0042A1F5 004227D1
0042A1F9 00427626
0042A1FD 00423D60
0042A201 0042896D
0042A205 004281F9
0042A209 004285C2
0042A20D 0042863C
0042A211 00427C23
0042A215 00427FCB
0042A219 004278CD
0042A21D 00427BC2
0042A221 00428158
0042A225 00427768
0042A229 00427A73
0042A22D 00427E3F
0042A231 0042A069
0042A235 0042A069
0042A239 0042A069
0042A23D 0042A069
0042A241 0042A069
0042A245 0042A069
0042A249 0042A069
0042A24D 0042A069
0042A251 0042A069
0042A255 0042A069
0042A259 0042A069
0042A25D 0042A069
0042A261 004286A8
0042A265 004289FD
0042A269 00428A5A
0042A26D 00428AFF
0042A271 0042908C
0042A275 004291F0
0042A279 0041CA99
0042A27D 00429250
0042A281 0041C2F0
0042A285 0042A069
0042A289 00427414
0042A28D 0041CA99
0042A291 00428BCE
0042A295 0042A069
0042A299 0042937B
0042A29D 0042A069
0042A2A1 0042951C
0042A2A5 00429635
0042A2A9 004296E7
0042A2AD 00426046
0042A2B1 004297A3
0042A2B5 00429983
0042A2B9 004299B0
0042A2BD 00429A17
0042A2C1 00429A6E
0042A2C5 00429AD5
0042A2C9 0042A069
0042A2CD 0042A069
0042A2D1 00429CA2
0042A2D5 00429DB6
0042A2D9 00429F32
0042A2DD 00429E95

notify_userinfo_local_lock_window: [0x0041A27B,0x0041A62D) size=946
0041A27B 8b8d40feffff         mov     ecx, dword ptr [ebp - 0x1c0]
0041A281 e81ad8ffff           call    0x417aa0
0041A286 8985c4fcffff         mov     dword ptr [ebp - 0x33c], eax
0041A28C 83bdc4fcffff00       cmp     dword ptr [ebp - 0x33c], 0
0041A293 0f8482000000         je      0x41a31b
0041A299 8d952cfdffff         lea     edx, [ebp - 0x2d4]
0041A29F 52                   push    edx
0041A2A0 8b85c4fcffff         mov     eax, dword ptr [ebp - 0x33c]
0041A2A6 8b10                 mov     edx, dword ptr [eax]
0041A2A8 8b8dc4fcffff         mov     ecx, dword ptr [ebp - 0x33c]
0041A2AE ff9264010000         call    dword ptr [edx + 0x164]
0041A2B4 a1185ab700           mov     eax, dword ptr [0xb75a18]
0041A2B9 3b85c4fcffff         cmp     eax, dword ptr [ebp - 0x33c]
0041A2BF 7543                 jne     0x41a304
0041A2C1 8b0db8dcb500         mov     ecx, dword ptr [0xb5dcb8]
0041A2C7 e804b0feff           call    0x4052d0
0041A2CC 398540feffff         cmp     dword ptr [ebp - 0x1c0], eax
0041A2D2 7530                 jne     0x41a304
0041A2D4 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0041A2DA e8e137ffff           call    0x40dac0
0041A2DF 8bc8                 mov     ecx, eax
0041A2E1 e8caddffff           call    0x4180b0
0041A2E6 83f801               cmp     eax, 1
0041A2E9 7519                 jne     0x41a304
0041A2EB 8d8d2cfdffff         lea     ecx, [ebp - 0x2d4]
0041A2F1 51                   push    ecx
0041A2F2 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0041A2F8 e8c337ffff           call    0x40dac0
0041A2FD 8bc8                 mov     ecx, eax
0041A2FF e80ca04400           call    0x864310
0041A304 8b95f4fdffff         mov     edx, dword ptr [ebp - 0x20c]
0041A30A 52                   push    edx
0041A30B 8b8dc4fcffff         mov     ecx, dword ptr [ebp - 0x33c]
0041A311 e84adaffff           call    0x417d60
0041A316 e912030000           jmp     0x41a62d
0041A31B 0fb7b544feffff       movzx   esi, word ptr [ebp - 0x1bc]
0041A322 b9405ab700           mov     ecx, 0xb75a40
0041A327 e834a7feff           call    0x404a60
0041A32C 3bf0                 cmp     esi, eax
0041A32E 0f85f9020000         jne     0x41a62d
0041A334 8b8d40feffff         mov     ecx, dword ptr [ebp - 0x1c0]
0041A33A e831370e00           call    0x4fda70
0041A33F 8985c4fcffff         mov     dword ptr [ebp - 0x33c], eax
0041A345 8b8df09effff         mov     ecx, dword ptr [ebp - 0x6110]
0041A34B e8a0daffff           call    0x417df0
0041A350 8bc8                 mov     ecx, eax
0041A352 e86908ffff           call    0x40abc0
0041A357 8985b0fcffff         mov     dword ptr [ebp - 0x350], eax
0041A35D 6a00                 push    0
0041A35F 6a00                 push    0
0041A361 8b85c4fcffff         mov     eax, dword ptr [ebp - 0x33c]
0041A367 50                   push    eax
0041A368 0fb78d44feffff       movzx   ecx, word ptr [ebp - 0x1bc]
0041A36F 51                   push    ecx
0041A370 6a12                 push    0x12
0041A372 8b8db0fcffff         mov     ecx, dword ptr [ebp - 0x350]
0041A378 e803a0feff           call    0x404380
0041A37D 8bc8                 mov     ecx, eax
0041A37F e85cd70a00           call    0x4c7ae0
0041A384 8b8df09effff         mov     ecx, dword ptr [ebp - 0x6110]
0041A38A e861daffff           call    0x417df0
0041A38F 8bc8                 mov     ecx, eax
0041A391 e8aad7ffff           call    0x417b40
0041A396 8985b4fcffff         mov     dword ptr [ebp - 0x34c], eax
0041A39C 83bdb4fcffff00       cmp     dword ptr [ebp - 0x34c], 0
0041A3A3 7463                 je      0x41a408
0041A3A5 6a00                 push    0
0041A3A7 8b8db4fcffff         mov     ecx, dword ptr [ebp - 0x34c]
0041A3AD 83c134               add     ecx, 0x34
0041A3B0 e87beaffff           call    0x418e30
0041A3B5 8b10                 mov     edx, dword ptr [eax]
0041A3B7 8995a4fcffff         mov     dword ptr [ebp - 0x35c], edx
0041A3BD 6a01                 push    1
0041A3BF 8b8db4fcffff         mov     ecx, dword ptr [ebp - 0x34c]
0041A3C5 83c134               add     ecx, 0x34
0041A3C8 e863eaffff           call    0x418e30
0041A3CD 8b00                 mov     eax, dword ptr [eax]
0041A3CF 8985a8fcffff         mov     dword ptr [ebp - 0x358], eax
0041A3D5 6a00                 push    0
0041A3D7 8b8da8fcffff         mov     ecx, dword ptr [ebp - 0x358]
0041A3DD 51                   push    ecx
0041A3DE 8b95a4fcffff         mov     edx, dword ptr [ebp - 0x35c]
0041A3E4 52                   push    edx
0041A3E5 8b85c4fcffff         mov     eax, dword ptr [ebp - 0x33c]
0041A3EB 8b10                 mov     edx, dword ptr [eax]
0041A3ED 8b8dc4fcffff         mov     ecx, dword ptr [ebp - 0x33c]
0041A3F3 ff527c               call    dword ptr [edx + 0x7c]
0041A3F6 8b8df09effff         mov     ecx, dword ptr [ebp - 0x6110]
0041A3FC e8efd9ffff           call    0x417df0
0041A401 8bc8                 mov     ecx, eax
0041A403 e838434800           call    0x89e740
0041A408 8b85c4fcffff         mov     eax, dword ptr [ebp - 0x33c]
0041A40E 8b10                 mov     edx, dword ptr [eax]
0041A410 8b8dc4fcffff         mov     ecx, dword ptr [ebp - 0x33c]
0041A416 ff927c010000         call    dword ptr [edx + 0x17c]
0041A41C a1185ab700           mov     eax, dword ptr [0xb75a18]
0041A421 50                   push    eax
0041A422 8b8db0fcffff         mov     ecx, dword ptr [ebp - 0x350]
0041A428 e8d3940c00           call    0x4e3900
0041A42D c785acfcffff00000000 mov     dword ptr [ebp - 0x354], 0
0041A437 b934b4b500           mov     ecx, 0xb5b434
0041A43C e81fa6feff           call    0x404a60
0041A441 50                   push    eax
0041A442 b930b4b500           mov     ecx, 0xb5b430
0041A447 e814a6feff           call    0x404a60
0041A44C 50                   push    eax
0041A44D b948f1be00           mov     ecx, 0xbef148
0041A452 e8295a3000           call    0x71fe80
0041A457 8985acfcffff         mov     dword ptr [ebp - 0x354], eax
0041A45D 83bdacfcffff00       cmp     dword ptr [ebp - 0x354], 0
0041A464 0f842e010000         je      0x41a598
0041A46A 686c999900           push    0x99996c
0041A46F 8b8dacfcffff         mov     ecx, dword ptr [ebp - 0x354]
0041A475 83c120               add     ecx, 0x20
0041A478 51                   push    ecx
0041A479 e822e6ffff           call    0x418aa0
0041A47E 83c408               add     esp, 8
0041A481 0fb6d0               movzx   edx, al
0041A484 85d2                 test    edx, edx
0041A486 0f840c010000         je      0x41a598
0041A48C 6860999900           push    0x999960
0041A491 8b85acfcffff         mov     eax, dword ptr [ebp - 0x354]
0041A497 83c020               add     eax, 0x20
0041A49A 50                   push    eax
0041A49B e820a8feff           call    0x404cc0
0041A4A0 83c408               add     esp, 8
0041A4A3 0fb6c8               movzx   ecx, al
0041A4A6 85c9                 test    ecx, ecx
0041A4A8 7430                 je      0x41a4da
0041A4AA 6a00                 push    0
0041A4AC 6a00                 push    0
0041A4AE 6a10                 push    0x10
0041A4B0 68e6c89bff           push    0xff9bc8e6
0041A4B5 684d150000           push    0x154d
0041A4BA e8e1004900           call    0x8aa5a0
0041A4BF 83c404               add     esp, 4
0041A4C2 50                   push    eax
0041A4C3 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0041A4C9 e8222e0300           call    0x44d2f0
0041A4CE 8bc8                 mov     ecx, eax
0041A4D0 e88bb64200           call    0x845b60
0041A4D5 e9be000000           jmp     0x41a598
0041A4DA 6854999900           push    0x999954
0041A4DF 8b95acfcffff         mov     edx, dword ptr [ebp - 0x354]
0041A4E5 83c220               add     edx, 0x20
0041A4E8 52                   push    edx
0041A4E9 e8d2a7feff           call    0x404cc0
0041A4EE 83c408               add     esp, 8
0041A4F1 0fb6c0               movzx   eax, al
0041A4F4 85c0                 test    eax, eax
0041A4F6 751e                 jne     0x41a516
0041A4F8 6844999900           push    0x999944
0041A4FD 8b8dacfcffff         mov     ecx, dword ptr [ebp - 0x354]
0041A503 83c120               add     ecx, 0x20
0041A506 51                   push    ecx
0041A507 e8b4a7feff           call    0x404cc0
0041A50C 83c408               add     esp, 8
0041A50F 0fb6d0               movzx   edx, al
0041A512 85d2                 test    edx, edx
0041A514 742d                 je      0x41a543
0041A516 6a00                 push    0
0041A518 6a00                 push    0
0041A51A 6a10                 push    0x10
0041A51C 68e6c89bff           push    0xff9bc8e6
0041A521 684e150000           push    0x154e
0041A526 e875004900           call    0x8aa5a0
0041A52B 83c404               add     esp, 4
0041A52E 50                   push    eax
0041A52F 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0041A535 e8b62d0300           call    0x44d2f0
0041A53A 8bc8                 mov     ecx, eax
0041A53C e81fb64200           call    0x845b60
0041A541 eb55                 jmp     0x41a598
0041A543 8b8dacfcffff         mov     ecx, dword ptr [ebp - 0x354]
0041A549 83c13c               add     ecx, 0x3c
0041A54C e88f24ffff           call    0x40c9e0
0041A551 50                   push    eax
0041A552 68dc040000           push    0x4dc
0041A557 e844004900           call    0x8aa5a0
0041A55C 83c404               add     esp, 4
0041A55F 50                   push    eax
0041A560 6800010000           push    0x100
0041A565 8d85a4fbffff         lea     eax, [ebp - 0x45c]
0041A56B 50                   push    eax
0041A56C e83f984900           call    0x8b3db0
0041A571 83c410               add     esp, 0x10
0041A574 6a00                 push    0
0041A576 6a00                 push    0
0041A578 6a10                 push    0x10
0041A57A 68e6c89bff           push    0xff9bc8e6
0041A57F 8d8da4fbffff         lea     ecx, [ebp - 0x45c]
0041A585 51                   push    ecx
0041A586 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0041A58C e85f2d0300           call    0x44d2f0
0041A591 8bc8                 mov     ecx, eax
0041A593 e8c8b54200           call    0x845b60
0041A598 8d952cfdffff         lea     edx, [ebp - 0x2d4]
0041A59E 52                   push    edx
0041A59F 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0041A5A5 e81635ffff           call    0x40dac0
0041A5AA 8bc8                 mov     ecx, eax
0041A5AC e85f9d4400           call    0x864310
0041A5B1 6896030000           push    0x396
0041A5B6 6824999900           push    0x999924
0041A5BB e800950100           call    0x433ac0
0041A5C0 83c408               add     esp, 8
0041A5C3 8985e89effff         mov     dword ptr [ebp - 0x6118], eax
0041A5C9 6a02                 push    2
0041A5CB 6a00                 push    0
0041A5CD 6a00                 push    0
0041A5CF 8b85e89effff         mov     eax, dword ptr [ebp - 0x6118]
0041A5D5 8b10                 mov     edx, dword ptr [eax]
0041A5D7 8b8de89effff         mov     ecx, dword ptr [ebp - 0x6118]
0041A5DD ff5240               call    dword ptr [edx + 0x40]
0041A5E0 6897030000           push    0x397
0041A5E5 6824999900           push    0x999924
0041A5EA e8d1940100           call    0x433ac0
0041A5EF 83c408               add     esp, 8
0041A5F2 8985e49effff         mov     dword ptr [ebp - 0x611c], eax
0041A5F8 6a02                 push    2
0041A5FA 6a00                 push    0
0041A5FC 6a01                 push    1
0041A5FE 8b85e49effff         mov     eax, dword ptr [ebp - 0x611c]
0041A604 8b10                 mov     edx, dword ptr [eax]
0041A606 8b8de49effff         mov     ecx, dword ptr [ebp - 0x611c]
0041A60C ff5240               call    dword ptr [edx + 0x40]
0041A60F 8b8530fdffff         mov     eax, dword ptr [ebp - 0x2d0]
0041A615 50                   push    eax
0041A616 8d8d2cfdffff         lea     ecx, [ebp - 0x2d4]
0041A61C e80f05ffff           call    0x40ab30
0041A621 50                   push    eax
0041A622 8b0d04ddb500         mov     ecx, dword ptr [0xb5dd04]
0041A628 e883f44800           call    0x8a9ab0

notify136_release_window: [0x0042951C,0x004295DB) size=191
0042951C 6848200000           push    0x2048
00429521 6824999900           push    0x999924
00429526 e895a50000           call    0x433ac0
0042952B 83c408               add     esp, 8
0042952E 8985c49dffff         mov     dword ptr [ebp - 0x623c], eax
00429534 6a02                 push    2
00429536 6a01                 push    1
00429538 6a00                 push    0
0042953A 8b8dc49dffff         mov     ecx, dword ptr [ebp - 0x623c]
00429540 8b11                 mov     edx, dword ptr [ecx]
00429542 8b8dc49dffff         mov     ecx, dword ptr [ebp - 0x623c]
00429548 ff5240               call    dword ptr [edx + 0x40]
0042954B 6849200000           push    0x2049
00429550 6824999900           push    0x999924
00429555 e866a50000           call    0x433ac0
0042955A 83c408               add     esp, 8
0042955D 8985c09dffff         mov     dword ptr [ebp - 0x6240], eax
00429563 6a02                 push    2
00429565 6a01                 push    1
00429567 6a01                 push    1
00429569 8b85c09dffff         mov     eax, dword ptr [ebp - 0x6240]
0042956F 8b10                 mov     edx, dword ptr [eax]
00429571 8b8dc09dffff         mov     ecx, dword ptr [ebp - 0x6240]
00429577 ff5240               call    dword ptr [edx + 0x40]
0042957A b92ce5b500           mov     ecx, 0xb5e52c
0042957F e87ce8feff           call    0x417e00
00429584 85c0                 test    eax, eax
00429586 0f85a4000000         jne     0x429630
0042958C 6a0f                 push    0xf
0042958E e86d43feff           call    0x40d900
00429593 8bc8                 mov     ecx, eax
00429595 e8b6904800           call    0x8b2650
0042959A e86143feff           call    0x40d900
0042959F 8bc8                 mov     ecx, eax
004295A1 e81a8f4800           call    0x8b24c0
004295A6 c785bcb9ffff10270000 mov     dword ptr [ebp - 0x4644], 0x2710
004295B0 c785c0b9ffff00000000 mov     dword ptr [ebp - 0x4640], 0
004295BA 8b85c0b9ffff         mov     eax, dword ptr [ebp - 0x4640]
004295C0 50                   push    eax
004295C1 8b8dbcb9ffff         mov     ecx, dword ptr [ebp - 0x4644]
004295C7 51                   push    ecx
004295C8 e8637afdff           call    0x401030
004295CD 8bc8                 mov     ecx, eax
004295CF e8ccacfdff           call    0x4042a0
004295D4 8bc8                 mov     ecx, eax
004295D6 e8f5874700           call    0x8a1dd0

priority1_all_types_acquire_complete: [0x00494DA0,0x00494F72) size=466
00494DA0 55                   push    ebp
00494DA1 8bec                 mov     ebp, esp
00494DA3 51                   push    ecx
00494DA4 53                   push    ebx
00494DA5 8bd9                 mov     ebx, ecx
00494DA7 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
00494DAD 8b01                 mov     eax, dword ptr [ecx]
00494DAF 56                   push    esi
00494DB0 ff9080000000         call    dword ptr [eax + 0x80]
00494DB6 8bb0b0000000         mov     esi, dword ptr [eax + 0xb0]
00494DBC 85f6                 test    esi, esi
00494DBE 8975fc               mov     dword ptr [ebp - 4], esi
00494DC1 0f84a5010000         je      0x494f6c
00494DC7 8b0d4c16bf00         mov     ecx, dword ptr [0xbf164c]
00494DCD 8b11                 mov     edx, dword ptr [ecx]
00494DCF 57                   push    edi
00494DD0 6a00                 push    0
00494DD2 ff5214               call    dword ptr [edx + 0x14]
00494DD5 84c0                 test    al, al
00494DD7 7420                 je      0x494df9
00494DD9 a1e8dcb500           mov     eax, dword ptr [0xb5dce8]
00494DDE 8b4860               mov     ecx, dword ptr [eax + 0x60]
00494DE1 8a816c010000         mov     al, byte ptr [ecx + 0x16c]
00494DE7 84c0                 test    al, al
00494DE9 750e                 jne     0x494df9
00494DEB 6a01                 push    1
00494DED 8bcb                 mov     ecx, ebx
00494DEF e8bcfaffff           call    0x4948b0
00494DF4 e9f4000000           jmp     0x494eed
00494DF9 8b0d4c16bf00         mov     ecx, dword ptr [0xbf164c]
00494DFF 8b11                 mov     edx, dword ptr [ecx]
00494E01 6a01                 push    1
00494E03 ff5214               call    dword ptr [edx + 0x14]
00494E06 84c0                 test    al, al
00494E08 7420                 je      0x494e2a
00494E0A a1e8dcb500           mov     eax, dword ptr [0xb5dce8]
00494E0F 8b4860               mov     ecx, dword ptr [eax + 0x60]
00494E12 8a816c010000         mov     al, byte ptr [ecx + 0x16c]
00494E18 84c0                 test    al, al
00494E1A 750e                 jne     0x494e2a
00494E1C 6a00                 push    0
00494E1E 8bcb                 mov     ecx, ebx
00494E20 e88bfaffff           call    0x4948b0
00494E25 e9c3000000           jmp     0x494eed
00494E2A 33f6                 xor     esi, esi
00494E2C 33ff                 xor     edi, edi
00494E2E 8bff                 mov     edi, edi
00494E30 8b0d4c16bf00         mov     ecx, dword ptr [0xbf164c]
00494E36 8b11                 mov     edx, dword ptr [ecx]
00494E38 8d4751               lea     eax, [edi + 0x51]
00494E3B 50                   push    eax
00494E3C ff5214               call    dword ptr [edx + 0x14]
00494E3F 84c0                 test    al, al
00494E41 0f848e000000         je      0x494ed5
00494E47 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
00494E4D 8b5160               mov     edx, dword ptr [ecx + 0x60]
00494E50 8a826c010000         mov     al, byte ptr [edx + 0x16c]
00494E56 84c0                 test    al, al
00494E58 757b                 jne     0x494ed5
00494E5A 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
00494E60 83c110               add     ecx, 0x10
00494E63 e8c8d1f6ff           call    0x402030
00494E68 83f804               cmp     eax, 4
00494E6B 750e                 jne     0x494e7b
00494E6D 8b0dc859b700         mov     ecx, dword ptr [0xb759c8]
00494E73 57                   push    edi
00494E74 e857780600           call    0x4fc6d0
00494E79 eb40                 jmp     0x494ebb
00494E7B 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
00494E81 83c110               add     ecx, 0x10
00494E84 e8a7d1f6ff           call    0x402030
00494E89 83f806               cmp     eax, 6
00494E8C 750e                 jne     0x494e9c
00494E8E 8b0da85ab700         mov     ecx, dword ptr [0xb75aa8]
00494E94 57                   push    edi
00494E95 e806250700           call    0x5073a0
00494E9A eb1f                 jmp     0x494ebb
00494E9C 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
00494EA2 83c110               add     ecx, 0x10
00494EA5 e886d1f6ff           call    0x402030
00494EAA 83f807               cmp     eax, 7
00494EAD 750e                 jne     0x494ebd
00494EAF 8b0db85cb700         mov     ecx, dword ptr [0xb75cb8]
00494EB5 57                   push    edi
00494EB6 e8953e0700           call    0x508d50
00494EBB 8bf0                 mov     esi, eax
00494EBD 85f6                 test    esi, esi
00494EBF 7414                 je      0x494ed5
00494EC1 8b8610010000         mov     eax, dword ptr [esi + 0x110]
00494EC7 85c0                 test    eax, eax
00494EC9 740a                 je      0x494ed5
00494ECB 8a8870220000         mov     cl, byte ptr [eax + 0x2270]
00494ED1 84c9                 test    cl, cl
00494ED3 740f                 je      0x494ee4
00494ED5 47                   inc     edi
00494ED6 83ff08               cmp     edi, 8
00494ED9 0f8c51ffffff         jl      0x494e30
00494EDF 8b75fc               mov     esi, dword ptr [ebp - 4]
00494EE2 eb19                 jmp     0x494efd
00494EE4 8b8610010000         mov     eax, dword ptr [esi + 0x110]
00494EEA 8b75fc               mov     esi, dword ptr [ebp - 4]
00494EED 85c0                 test    eax, eax
00494EEF 740c                 je      0x494efd
00494EF1 8b8ef4000000         mov     ecx, dword ptr [esi + 0xf4]
00494EF7 50                   push    eax
00494EF8 e803ea0400           call    0x4e3900
00494EFD 8b0d4c16bf00         mov     ecx, dword ptr [0xbf164c]
00494F03 8b01                 mov     eax, dword ptr [ecx]
00494F05 6a0d                 push    0xd
00494F07 ff5018               call    dword ptr [eax + 0x18]
00494F0A 84c0                 test    al, al
00494F0C 5f                   pop     edi
00494F0D 7431                 je      0x494f40
00494F0F 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
00494F15 8b5160               mov     edx, dword ptr [ecx + 0x60]
00494F18 8a826c010000         mov     al, byte ptr [edx + 0x16c]
00494F1E 84c0                 test    al, al
00494F20 751e                 jne     0x494f40
00494F22 8b8ef4000000         mov     ecx, dword ptr [esi + 0xf4]
00494F28 6a00                 push    0
00494F2A 6a01                 push    1
00494F2C 6aff                 push    -1
00494F2E 6aff                 push    -1
00494F30 6aff                 push    -1
00494F32 6a64                 push    0x64
00494F34 6a64                 push    0x64
00494F36 6800000040           push    0x40000000
00494F3B e8600b0500           call    0x4e5aa0
00494F40 8b03                 mov     eax, dword ptr [ebx]
00494F42 8bcb                 mov     ecx, ebx
00494F44 ff9018030000         call    dword ptr [eax + 0x318]
00494F4A 84c0                 test    al, al
00494F4C 751e                 jne     0x494f6c
00494F4E 33f6                 xor     esi, esi
00494F50 8b13                 mov     edx, dword ptr [ebx]
00494F52 8bcb                 mov     ecx, ebx
00494F54 ff92d4050000         call    dword ptr [edx + 0x5d4]
00494F5A 8b10                 mov     edx, dword ptr [eax]
00494F5C 6a01                 push    1
00494F5E 6a00                 push    0
00494F60 56                   push    esi
00494F61 8bc8                 mov     ecx, eax
00494F63 ff5240               call    dword ptr [edx + 0x40]
00494F66 46                   inc     esi
00494F67 83fe04               cmp     esi, 4
00494F6A 7ce4                 jl      0x494f50
00494F6C 5e                   pop     esi
00494F6D 5b                   pop     ebx
00494F6E 8be5                 mov     esp, ebp
00494F70 5d                   pop     ebp
00494F71 c3                   ret     

priority1_all_types_release_complete: [0x00494440,0x004948A6) size=1126
00494440 55                   push    ebp
00494441 8bec                 mov     ebp, esp
00494443 51                   push    ecx
00494444 53                   push    ebx
00494445 8a5d08               mov     bl, byte ptr [ebp + 8]
00494448 56                   push    esi
00494449 8bf1                 mov     esi, ecx
0049444B 389e70220000         cmp     byte ptr [esi + 0x2270], bl
00494451 0f8447040000         je      0x49489e
00494457 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049445D 85c9                 test    ecx, ecx
0049445F 0f8439040000         je      0x49489e
00494465 8b01                 mov     eax, dword ptr [ecx]
00494467 ff9080000000         call    dword ptr [eax + 0x80]
0049446D 85c0                 test    eax, eax
0049446F 0f8429040000         je      0x49489e
00494475 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049447B 8b11                 mov     edx, dword ptr [ecx]
0049447D ff9280000000         call    dword ptr [edx + 0x80]
00494483 8b88b0000000         mov     ecx, dword ptr [eax + 0xb0]
00494489 85c9                 test    ecx, ecx
0049448B 0f840d040000         je      0x49489e
00494491 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
00494497 8b01                 mov     eax, dword ptr [ecx]
00494499 57                   push    edi
0049449A ff9080000000         call    dword ptr [eax + 0x80]
004944A0 84db                 test    bl, bl
004944A2 8bb8b0000000         mov     edi, dword ptr [eax + 0xb0]
004944A8 897d08               mov     dword ptr [ebp + 8], edi
004944AB 889e70220000         mov     byte ptr [esi + 0x2270], bl
004944B1 0f8490010000         je      0x494647
004944B7 8d9ea4010000         lea     ebx, [esi + 0x1a4]
004944BD 8bcb                 mov     ecx, ebx
004944BF e82c7c4100           call    0x8ac0f0
004944C4 3935145ab700         cmp     dword ptr [0xb75a14], esi
004944CA 757d                 jne     0x494549
004944CC 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
004944D2 8b7960               mov     edi, dword ptr [ecx + 0x60]
004944D5 6a00                 push    0
004944D7 6a00                 push    0
004944D9 6a10                 push    0x10
004944DB 6832dc32ff           push    0xff32dc32
004944E0 685a030000           push    0x35a
004944E5 e8b6604100           call    0x8aa5a0
004944EA 83c404               add     esp, 4
004944ED 50                   push    eax
004944EE 8bcf                 mov     ecx, edi
004944F0 e86b163b00           call    0x845b60
004944F5 8b15e8dcb500         mov     edx, dword ptr [0xb5dce8]
004944FB 8b7a60               mov     edi, dword ptr [edx + 0x60]
004944FE 6a00                 push    0
00494500 6a00                 push    0
00494502 6a10                 push    0x10
00494504 6832dc32ff           push    0xff32dc32
00494509 685b030000           push    0x35b
0049450E e88d604100           call    0x8aa5a0
00494513 83c404               add     esp, 4
00494516 50                   push    eax
00494517 8bcf                 mov     ecx, edi
00494519 e842163b00           call    0x845b60
0049451E a1e8dcb500           mov     eax, dword ptr [0xb5dce8]
00494523 8b7860               mov     edi, dword ptr [eax + 0x60]
00494526 6a00                 push    0
00494528 6a00                 push    0
0049452A 6a10                 push    0x10
0049452C 6832dc32ff           push    0xff32dc32
00494531 685c030000           push    0x35c
00494536 e865604100           call    0x8aa5a0
0049453B 83c404               add     esp, 4
0049453E 50                   push    eax
0049453F 8bcf                 mov     ecx, edi
00494541 e81a163b00           call    0x845b60
00494546 8b7d08               mov     edi, dword ptr [ebp + 8]
00494549 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
0049454F 83c110               add     ecx, 0x10
00494552 e8d9daf6ff           call    0x402030
00494557 83f804               cmp     eax, 4
0049455A 7416                 je      0x494572
0049455C 56                   push    esi
0049455D 8bcf                 mov     ecx, edi
0049455F e89c210300           call    0x4c6700
00494564 8b16                 mov     edx, dword ptr [esi]
00494566 6a01                 push    1
00494568 8bce                 mov     ecx, esi
0049456A ff9250050000         call    dword ptr [edx + 0x550]
00494570 eb59                 jmp     0x4945cb
00494572 a1c859b700           mov     eax, dword ptr [0xb759c8]
00494577 85c0                 test    eax, eax
00494579 743c                 je      0x4945b7
0049457B 83b88400000002       cmp     dword ptr [eax + 0x84], 2
00494582 7533                 jne     0x4945b7
00494584 8b06                 mov     eax, dword ptr [esi]
00494586 8bce                 mov     ecx, esi
00494588 ff9018030000         call    dword ptr [eax + 0x318]
0049458E 84c0                 test    al, al
00494590 740f                 je      0x4945a1
00494592 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
00494598 e8b3a0f9ff           call    0x42e650
0049459D 84c0                 test    al, al
0049459F 742a                 je      0x4945cb
004945A1 56                   push    esi
004945A2 8bcf                 mov     ecx, edi
004945A4 e857210300           call    0x4c6700
004945A9 8b16                 mov     edx, dword ptr [esi]
004945AB 6a01                 push    1
004945AD 8bce                 mov     ecx, esi
004945AF ff9250050000         call    dword ptr [edx + 0x550]
004945B5 eb14                 jmp     0x4945cb
004945B7 56                   push    esi
004945B8 8bcf                 mov     ecx, edi
004945BA e841210300           call    0x4c6700
004945BF 8b06                 mov     eax, dword ptr [esi]
004945C1 6a01                 push    1
004945C3 8bce                 mov     ecx, esi
004945C5 ff9050050000         call    dword ptr [eax + 0x550]
004945CB 8a450c               mov     al, byte ptr [ebp + 0xc]
004945CE 84c0                 test    al, al
004945D0 744f                 je      0x494621
004945D2 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
004945D8 6aff                 push    -1
004945DA e8a1a2f9ff           call    0x42e880
004945DF 85c0                 test    eax, eax
004945E1 0f84b6020000         je      0x49489d
004945E7 8b16                 mov     edx, dword ptr [esi]
004945E9 8bce                 mov     ecx, esi
004945EB ff9278050000         call    dword ptr [edx + 0x578]
004945F1 83f8ff               cmp     eax, -1
004945F4 0f84a3020000         je      0x49489d
004945FA 8b06                 mov     eax, dword ptr [esi]
004945FC 8bce                 mov     ecx, esi
004945FE ff9078050000         call    dword ptr [eax + 0x578]
00494604 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
0049460A 6aff                 push    -1
0049460C 8bf0                 mov     esi, eax
0049460E e86da2f9ff           call    0x42e880
00494613 5f                   pop     edi
00494614 c644064501           mov     byte ptr [esi + eax + 0x45], 1
00494619 5e                   pop     esi
0049461A 5b                   pop     ebx
0049461B 8be5                 mov     esp, ebp
0049461D 5d                   pop     ebp
0049461E c20800               ret     8
00494621 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
00494627 8b7160               mov     esi, dword ptr [ecx + 0x60]
0049462A 6a00                 push    0
0049462C 6a00                 push    0
0049462E 6a10                 push    0x10
00494630 68e6c89bff           push    0xff9bc8e6
00494635 8bcb                 mov     ecx, ebx
00494637 e8b47a4100           call    0x8ac0f0
0049463C 50                   push    eax
0049463D 685d030000           push    0x35d
00494642 e93b020000           jmp     0x494882
00494647 8d8ea4010000         lea     ecx, [esi + 0x1a4]
0049464D 894dfc               mov     dword ptr [ebp - 4], ecx
00494650 e89b7a4100           call    0x8ac0f0
00494655 56                   push    esi
00494656 8bcf                 mov     ecx, edi
00494658 e8b30c0300           call    0x4c5310
0049465D 84c0                 test    al, al
0049465F 7408                 je      0x494669
00494661 56                   push    esi
00494662 8bcf                 mov     ecx, edi
00494664 e897200300           call    0x4c6700
00494669 8b16                 mov     edx, dword ptr [esi]
0049466B 6a00                 push    0
0049466D 6a00                 push    0
0049466F 56                   push    esi
00494670 8bce                 mov     ecx, esi
00494672 ff5238               call    dword ptr [edx + 0x38]
00494675 50                   push    eax
00494676 6811010000           push    0x111
0049467B 8bcf                 mov     ecx, edi
0049467D e85e340300           call    0x4c7ae0
00494682 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
00494688 83c110               add     ecx, 0x10
0049468B e8a0d9f6ff           call    0x402030
00494690 83e804               sub     eax, 4
00494693 7422                 je      0x4946b7
00494695 83e802               sub     eax, 2
00494698 7538                 jne     0x4946d2
0049469A a1f0d6b500           mov     eax, dword ptr [0xb5d6f0]
0049469F 8b16                 mov     edx, dword ptr [esi]
004946A1 8b7830               mov     edi, dword ptr [eax + 0x30]
004946A4 8bce                 mov     ecx, esi
004946A6 ff9298010000         call    dword ptr [edx + 0x198]
004946AC 50                   push    eax
004946AD 56                   push    esi
004946AE 8bcf                 mov     ecx, edi
004946B0 e8cb1c4100           call    0x8a6380
004946B5 eb1b                 jmp     0x4946d2
004946B7 a1f0d6b500           mov     eax, dword ptr [0xb5d6f0]
004946BC 8b16                 mov     edx, dword ptr [esi]
004946BE 8b7828               mov     edi, dword ptr [eax + 0x28]
004946C1 8bce                 mov     ecx, esi
004946C3 ff9298010000         call    dword ptr [edx + 0x198]
004946C9 50                   push    eax
004946CA 56                   push    esi
004946CB 8bcf                 mov     ecx, edi
004946CD e81e834000           call    0x89c9f0
004946D2 3935145ab700         cmp     dword ptr [0xb75a14], esi
004946D8 8a5d0c               mov     bl, byte ptr [ebp + 0xc]
004946DB 0f8520010000         jne     0x494801
004946E1 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
004946E7 83c110               add     ecx, 0x10
004946EA e841d9f6ff           call    0x402030
004946EF 83f804               cmp     eax, 4
004946F2 745e                 je      0x494752
004946F4 84db                 test    bl, bl
004946F6 0f85c2000000         jne     0x4947be
004946FC 8b8e7c220000         mov     ecx, dword ptr [esi + 0x227c]
00494702 8b9678220000         mov     edx, dword ptr [esi + 0x2278]
00494708 8b06                 mov     eax, dword ptr [esi]
0049470A 51                   push    ecx
0049470B 8b8e74220000         mov     ecx, dword ptr [esi + 0x2274]
00494711 52                   push    edx
00494712 51                   push    ecx
00494713 8bce                 mov     ecx, esi
00494715 ff5070               call    dword ptr [eax + 0x70]
00494718 8b16                 mov     edx, dword ptr [esi]
0049471A 8bce                 mov     ecx, esi
0049471C ff92f8040000         call    dword ptr [edx + 0x4f8]
00494722 85c0                 test    eax, eax
00494724 0f8494000000         je      0x4947be
0049472A 8b06                 mov     eax, dword ptr [esi]
0049472C 8bce                 mov     ecx, esi
0049472E ff90f8040000         call    dword ptr [eax + 0x4f8]
00494734 8b8e7c220000         mov     ecx, dword ptr [esi + 0x227c]
0049473A 8b10                 mov     edx, dword ptr [eax]
0049473C 51                   push    ecx
0049473D 8b8e78220000         mov     ecx, dword ptr [esi + 0x2278]
00494743 51                   push    ecx
00494744 8b8e74220000         mov     ecx, dword ptr [esi + 0x2274]
0049474A 51                   push    ecx
0049474B 8bc8                 mov     ecx, eax
0049474D ff5270               call    dword ptr [edx + 0x70]
00494750 eb6c                 jmp     0x4947be
00494752 8b867c220000         mov     eax, dword ptr [esi + 0x227c]
00494758 8b8e78220000         mov     ecx, dword ptr [esi + 0x2278]
0049475E 8b16                 mov     edx, dword ptr [esi]
00494760 50                   push    eax
00494761 8b8674220000         mov     eax, dword ptr [esi + 0x2274]
00494767 51                   push    ecx
00494768 50                   push    eax
00494769 8bce                 mov     ecx, esi
0049476B ff5270               call    dword ptr [edx + 0x70]
0049476E 8b16                 mov     edx, dword ptr [esi]
00494770 8bce                 mov     ecx, esi
00494772 ff92f8040000         call    dword ptr [edx + 0x4f8]
00494778 85c0                 test    eax, eax
0049477A 7426                 je      0x4947a2
0049477C 8b06                 mov     eax, dword ptr [esi]
0049477E 8bce                 mov     ecx, esi
00494780 ff90f8040000         call    dword ptr [eax + 0x4f8]
00494786 8b8e7c220000         mov     ecx, dword ptr [esi + 0x227c]
0049478C 8b10                 mov     edx, dword ptr [eax]
0049478E 51                   push    ecx
0049478F 8b8e78220000         mov     ecx, dword ptr [esi + 0x2278]
00494795 51                   push    ecx
00494796 8b8e74220000         mov     ecx, dword ptr [esi + 0x2274]
0049479C 51                   push    ecx
0049479D 8bc8                 mov     ecx, eax
0049479F ff5270               call    dword ptr [edx + 0x70]
004947A2 8b15f0d6b500         mov     edx, dword ptr [0xb5d6f0]
004947A8 8b06                 mov     eax, dword ptr [esi]
004947AA 8b7a28               mov     edi, dword ptr [edx + 0x28]
004947AD 8bce                 mov     ecx, esi
004947AF ff9098010000         call    dword ptr [eax + 0x198]
004947B5 50                   push    eax
004947B6 56                   push    esi
004947B7 8bcf                 mov     ecx, edi
004947B9 e832824000           call    0x89c9f0
004947BE 8b16                 mov     edx, dword ptr [esi]
004947C0 6a00                 push    0
004947C2 6a00                 push    0
004947C4 6a00                 push    0
004947C6 6a00                 push    0
004947C8 6a00                 push    0
004947CA 8bce                 mov     ecx, esi
004947CC ff9240020000         call    dword ptr [edx + 0x240]
004947D2 33ff                 xor     edi, edi
004947D4 8b06                 mov     eax, dword ptr [esi]
004947D6 8bce                 mov     ecx, esi
004947D8 ff90d4050000         call    dword ptr [eax + 0x5d4]
004947DE 8b10                 mov     edx, dword ptr [eax]
004947E0 6a01                 push    1
004947E2 6a01                 push    1
004947E4 57                   push    edi
004947E5 8bc8                 mov     ecx, eax
004947E7 ff5240               call    dword ptr [edx + 0x40]
004947EA 47                   inc     edi
004947EB 83ff04               cmp     edi, 4
004947EE 7ce4                 jl      0x4947d4
004947F0 8b4508               mov     eax, dword ptr [ebp + 8]
004947F3 8b88f4000000         mov     ecx, dword ptr [eax + 0xf4]
004947F9 56                   push    esi
004947FA e801f10400           call    0x4e3900
004947FF eb15                 jmp     0x494816
00494801 8b16                 mov     edx, dword ptr [esi]
00494803 6a01                 push    1
00494805 8bce                 mov     ecx, esi
00494807 ff9278050000         call    dword ptr [edx + 0x578]
0049480D 50                   push    eax
0049480E e8bd30ffff           call    0x4878d0
00494813 83c408               add     esp, 8
00494816 84db                 test    bl, bl
00494818 7447                 je      0x494861
0049481A 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
00494820 6aff                 push    -1
00494822 e859a0f9ff           call    0x42e880
00494827 85c0                 test    eax, eax
00494829 7472                 je      0x49489d
0049482B 8b06                 mov     eax, dword ptr [esi]
0049482D 8bce                 mov     ecx, esi
0049482F ff9078050000         call    dword ptr [eax + 0x578]
00494835 83f8ff               cmp     eax, -1
00494838 7463                 je      0x49489d
0049483A 8b16                 mov     edx, dword ptr [esi]
0049483C 8bce                 mov     ecx, esi
0049483E ff9278050000         call    dword ptr [edx + 0x578]
00494844 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
0049484A 6aff                 push    -1
0049484C 8bf0                 mov     esi, eax
0049484E e82da0f9ff           call    0x42e880
00494853 5f                   pop     edi
00494854 c644064500           mov     byte ptr [esi + eax + 0x45], 0
00494859 5e                   pop     esi
0049485A 5b                   pop     ebx
0049485B 8be5                 mov     esp, ebp
0049485D 5d                   pop     ebp
0049485E c20800               ret     8
00494861 a1e8dcb500           mov     eax, dword ptr [0xb5dce8]
00494866 8b4dfc               mov     ecx, dword ptr [ebp - 4]
00494869 8b7060               mov     esi, dword ptr [eax + 0x60]
0049486C 6a00                 push    0
0049486E 6a00                 push    0
00494870 6a10                 push    0x10
00494872 68e6c89bff           push    0xff9bc8e6
00494877 e874784100           call    0x8ac0f0
0049487C 50                   push    eax
0049487D 685e030000           push    0x35e
00494882 e8195d4100           call    0x8aa5a0
00494887 83c404               add     esp, 4
0049488A 50                   push    eax
0049488B 6a00                 push    0
0049488D e81e724100           call    0x8abab0
00494892 83c40c               add     esp, 0xc
00494895 8bce                 mov     ecx, esi
00494897 50                   push    eax
00494898 e8c3123b00           call    0x845b60
0049489D 5f                   pop     edi
0049489E 5e                   pop     esi
0049489F 5b                   pop     ebx
004948A0 8be5                 mov     esp, ebp
004948A2 5d                   pop     ebp
004948A3 c20800               ret     8

priority1_acquire_caller_condition: [0x0079DDC2,0x0079DDF4) size=50
0079DDC2 a1145ab700           mov     eax, dword ptr [0xb75a14]
0079DDC7 85c0                 test    eax, eax
0079DDC9 5f                   pop     edi
0079DDCA 5e                   pop     esi
0079DDCB 5b                   pop     ebx
0079DDCC 7426                 je      0x79ddf4
0079DDCE 8a8870220000         mov     cl, byte ptr [eax + 0x2270]
0079DDD4 84c9                 test    cl, cl
0079DDD6 741c                 je      0x79ddf4
0079DDD8 8b0df0d6b500         mov     ecx, dword ptr [0xb5d6f0]
0079DDDE 6aff                 push    -1
0079DDE0 e88b09c9ff           call    0x42e770
0079DDE5 84c0                 test    al, al
0079DDE7 740b                 je      0x79ddf4
0079DDE9 8b0d145ab700         mov     ecx, dword ptr [0xb75a14]
0079DDEF e8ac6fcfff           call    0x494da0

native_scene_set_predicate_complete: [0x0042E770,0x0042E7A5) size=53
0042E770 55                   push    ebp
0042E771 8bec                 mov     ebp, esp
0042E773 8b4508               mov     eax, dword ptr [ebp + 8]
0042E776 83f8ff               cmp     eax, -1
0042E779 7508                 jne     0x42e783
0042E77B 83c110               add     ecx, 0x10
0042E77E e8ad38fdff           call    0x402030
0042E783 83c0fc               add     eax, -4
0042E786 83f805               cmp     eax, 5
0042E789 7714                 ja      0x42e79f
0042E78B 0fb680b0e74200       movzx   eax, byte ptr [eax + 0x42e7b0]
0042E792 ff2485a8e74200       jmp     dword ptr [eax*4 + 0x42e7a8]
0042E799 b001                 mov     al, 1
0042E79B 5d                   pop     ebp
0042E79C c20400               ret     4
0042E79F 32c0                 xor     al, al
0042E7A1 5d                   pop     ebp
0042E7A2 c20400               ret     4

native_scene_set_tables: [0x0042E7A8,0x0042E7B6) size=14
99e742009fe74200000100000100

mailbox_open_waiting_branch: [0x0080E01D,0x0080E040) size=35
0080E01D 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
0080E023 6a63                 push    0x63
0080E025 e826460a00           call    0x8b2650
0080E02A 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
0080E030 e88b440a00           call    0x8b24c0
0080E035 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0080E03B e970b3f8ff           jmp     0x7993b0

party_create_ctor_complete: [0x007C0D90,0x007C0E11) size=129
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
007C0E04 64890d00000000       mov     dword ptr fs:[0], ecx
007C0E0B 8be5                 mov     esp, ebp
007C0E0D 5d                   pop     ebp
007C0E0E c20400               ret     4

friend_addremove_ctor_complete: [0x007C0FF0,0x007C1071) size=129
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
007C1064 64890d00000000       mov     dword ptr fs:[0], ecx
007C106B 8be5                 mov     esp, ebp
007C106D 5d                   pop     ebp
007C106E c20400               ret     4

room_password_ctor_complete: [0x007C3300,0x007C3381) size=129
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
007C3374 64890d00000000       mov     dword ptr fs:[0], ecx
007C337B 8be5                 mov     esp, ebp
007C337D 5d                   pop     ebp
007C337E c20400               ret     4

party_create_vtable: [0x00A7E614,0x00A7E648) size=52
00A7E614 007D63D0
00A7E618 007C0E20
00A7E61C 007D5D20
00A7E620 007D6240
00A7E624 007D5C90
00A7E628 0065DB10
00A7E62C 007BD6C0
00A7E630 007BD720
00A7E634 007BD7C0
00A7E638 007BD7B0
00A7E63C 007BDC50
00A7E640 007BF640
00A7E644 007BF650

friend_addremove_vtable: [0x00A7E6B4,0x00A7E6E8) size=52
00A7E6B4 007F9B00
00A7E6B8 007C1080
00A7E6BC 007F9860
00A7E6C0 007F99F0
00A7E6C4 007F9A90
00A7E6C8 0065DB10
00A7E6CC 007BD6C0
00A7E6D0 007BD720
00A7E6D4 007BD7C0
00A7E6D8 007BD7B0
00A7E6DC 007BDC50
00A7E6E0 007BF640
00A7E6E4 007BF650

room_password_vtable: [0x00A7F154,0x00A7F188) size=52
00A7F154 00808160
00A7F158 007C3390
00A7F15C 00807EE0
00A7F160 00808040
00A7F164 008080F0
00A7F168 0065DB10
00A7F16C 007BD6C0
00A7F170 007BD720
00A7F174 007BD7C0
00A7F178 007BD7B0
00A7F17C 007BDC50
00A7F180 007BF640
00A7F184 007BF650

party_create_proc_complete: [0x007D5D20,0x007D6235) size=1301
007D5D20 55                   push    ebp
007D5D21 8bec                 mov     ebp, esp
007D5D23 83ec14               sub     esp, 0x14
007D5D26 56                   push    esi
007D5D27 8bf1                 mov     esi, ecx
007D5D29 8b4610               mov     eax, dword ptr [esi + 0x10]
007D5D2C 8b4870               mov     ecx, dword ptr [eax + 0x70]
007D5D2F 6a03                 push    3
007D5D31 e86a950700           call    0x84f2a0
007D5D36 84c0                 test    al, al
007D5D38 750c                 jne     0x7d5d46
007D5D3A 8b16                 mov     edx, dword ptr [esi]
007D5D3C 8bce                 mov     ecx, esi
007D5D3E ff5228               call    dword ptr [edx + 0x28]
007D5D41 5e                   pop     esi
007D5D42 8be5                 mov     esp, ebp
007D5D44 5d                   pop     ebp
007D5D45 c3                   ret     
007D5D46 8b4e08               mov     ecx, dword ptr [esi + 8]
007D5D49 53                   push    ebx
007D5D4A 57                   push    edi
007D5D4B e8b00c1200           call    0x8f6a00
007D5D50 8b4608               mov     eax, dword ptr [esi + 8]
007D5D53 8a4811               mov     cl, byte ptr [eax + 0x11]
007D5D56 84c9                 test    cl, cl
007D5D58 7469                 je      0x7d5dc3
007D5D5A 8b0d8451b500         mov     ecx, dword ptr [0xb55184]
007D5D60 3b480c               cmp     ecx, dword ptr [eax + 0xc]
007D5D63 755e                 jne     0x7d5dc3
007D5D65 8b5634               mov     edx, dword ptr [esi + 0x34]
007D5D68 8b420c               mov     eax, dword ptr [edx + 0xc]
007D5D6B a39c51b500           mov     dword ptr [0xb5519c], eax
007D5D70 a3a451b500           mov     dword ptr [0xb551a4], eax
007D5D75 e896691200           call    0x8fc710
007D5D7A 8bf8                 mov     edi, eax
007D5D7C e89f691200           call    0x8fc720
007D5D81 8b4e08               mov     ecx, dword ptr [esi + 8]
007D5D84 8b5114               mov     edx, dword ptr [ecx + 0x14]
007D5D87 8b4918               mov     ecx, dword ptr [ecx + 0x18]
007D5D8A 8d5a2b               lea     ebx, [edx + 0x2b]
007D5D8D 3bfb                 cmp     edi, ebx
007D5D8F 7c32                 jl      0x7d5dc3
007D5D91 81c2db000000         add     edx, 0xdb
007D5D97 3bfa                 cmp     edi, edx
007D5D99 7d28                 jge     0x7d5dc3
007D5D9B 8d5124               lea     edx, [ecx + 0x24]
007D5D9E 3bc2                 cmp     eax, edx
007D5DA0 7c21                 jl      0x7d5dc3
007D5DA2 83c134               add     ecx, 0x34
007D5DA5 3bc1                 cmp     eax, ecx
007D5DA7 7d1a                 jge     0x7d5dc3
007D5DA9 8b7e34               mov     edi, dword ptr [esi + 0x34]
007D5DAC 8bcf                 mov     ecx, edi
007D5DAE e83dc6fdff           call    0x7b23f0
007D5DB3 85c0                 test    eax, eax
007D5DB5 740c                 je      0x7d5dc3
007D5DB7 6848879900           push    0x998748
007D5DBC 8bcf                 mov     ecx, edi
007D5DBE e80dc6fdff           call    0x7b23d0
007D5DC3 8b4638               mov     eax, dword ptr [esi + 0x38]
007D5DC6 8a4811               mov     cl, byte ptr [eax + 0x11]
007D5DC9 84c9                 test    cl, cl
007D5DCB 740b                 je      0x7d5dd8
007D5DCD 8b0d8451b500         mov     ecx, dword ptr [0xb55184]
007D5DD3 3b480c               cmp     ecx, dword ptr [eax + 0xc]
007D5DD6 7412                 je      0x7d5dea
007D5DD8 8b4634               mov     eax, dword ptr [esi + 0x34]
007D5DDB 8a4878               mov     cl, byte ptr [eax + 0x78]
007D5DDE 84c9                 test    cl, cl
007D5DE0 c6407800             mov     byte ptr [eax + 0x78], 0
007D5DE4 0f843e020000         je      0x7d6028
007D5DEA 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007D5DED e82e681000           call    0x8dc620
007D5DF2 8b0db8dcb500         mov     ecx, dword ptr [0xb5dcb8]
007D5DF8 8bd8                 mov     ebx, eax
007D5DFA 895df8               mov     dword ptr [ebp - 8], ebx
007D5DFD c745f400000000       mov     dword ptr [ebp - 0xc], 0
007D5E04 e8c7f4c2ff           call    0x4052d0
007D5E09 8bb8fc000000         mov     edi, dword ptr [eax + 0xfc]
007D5E0F 85ff                 test    edi, edi
007D5E11 897df0               mov     dword ptr [ebp - 0x10], edi
007D5E14 7424                 je      0x7d5e3a
007D5E16 8bc3                 mov     eax, ebx
007D5E18 8d5001               lea     edx, [eax + 1]
007D5E1B eb03                 jmp     0x7d5e20
007D5E1D 8d4900               lea     ecx, [ecx]
007D5E20 8a08                 mov     cl, byte ptr [eax]
007D5E22 40                   inc     eax
007D5E23 84c9                 test    cl, cl
007D5E25 75f9                 jne     0x7d5e20
007D5E27 2bc2                 sub     eax, edx
007D5E29 8945ec               mov     dword ptr [ebp - 0x14], eax
007D5E2C 750c                 jne     0x7d5e3a
007D5E2E 8bcf                 mov     ecx, edi
007D5E30 e8ab0b0000           call    0x7d69e0
007D5E35 8bd8                 mov     ebx, eax
007D5E37 895df8               mov     dword ptr [ebp - 8], ebx
007D5E3A 33d2                 xor     edx, edx
007D5E3C eb03                 jmp     0x7d5e41
007D5E3E 8b5df8               mov     ebx, dword ptr [ebp - 8]
007D5E41 8b3c95645ab700       mov     edi, dword ptr [edx*4 + 0xb75a64]
007D5E48 8bc3                 mov     eax, ebx
007D5E4A 8d9b00000000         lea     ebx, [ebx]
007D5E50 8a18                 mov     bl, byte ptr [eax]
007D5E52 8acb                 mov     cl, bl
007D5E54 3a1f                 cmp     bl, byte ptr [edi]
007D5E56 751c                 jne     0x7d5e74
007D5E58 84c9                 test    cl, cl
007D5E5A 7414                 je      0x7d5e70
007D5E5C 8a5801               mov     bl, byte ptr [eax + 1]
007D5E5F 8acb                 mov     cl, bl
007D5E61 3a5f01               cmp     bl, byte ptr [edi + 1]
007D5E64 750e                 jne     0x7d5e74
007D5E66 83c002               add     eax, 2
007D5E69 83c702               add     edi, 2
007D5E6C 84c9                 test    cl, cl
007D5E6E 75e0                 jne     0x7d5e50
007D5E70 33c0                 xor     eax, eax
007D5E72 eb05                 jmp     0x7d5e79
007D5E74 1bc0                 sbb     eax, eax
007D5E76 83d8ff               sbb     eax, -1
007D5E79 42                   inc     edx
007D5E7A 85c0                 test    eax, eax
007D5E7C 7407                 je      0x7d5e85
007D5E7E 83fa06               cmp     edx, 6
007D5E81 7cbb                 jl      0x7d5e3e
007D5E83 eb03                 jmp     0x7d5e88
007D5E85 8955f4               mov     dword ptr [ebp - 0xc], edx
007D5E88 8b7df8               mov     edi, dword ptr [ebp - 8]
007D5E8B 8bc7                 mov     eax, edi
007D5E8D 8d5001               lea     edx, [eax + 1]
007D5E90 8a08                 mov     cl, byte ptr [eax]
007D5E92 40                   inc     eax
007D5E93 84c9                 test    cl, cl
007D5E95 75f9                 jne     0x7d5e90
007D5E97 2bc2                 sub     eax, edx
007D5E99 8945ec               mov     dword ptr [ebp - 0x14], eax
007D5E9C 7521                 jne     0x7d5ebf
007D5E9E 6a00                 push    0
007D5EA0 6a00                 push    0
007D5EA2 6861040000           push    0x461
007D5EA7 e8f4460d00           call    0x8aa5a0
007D5EAC 8b4e10               mov     ecx, dword ptr [esi + 0x10]
007D5EAF 83c404               add     esp, 4
007D5EB2 50                   push    eax
007D5EB3 6a23                 push    0x23
007D5EB5 e83640fcff           call    0x799ef0
007D5EBA e969010000           jmp     0x7d6028
007D5EBF 6838e9a600           push    0xa6e938
007D5EC4 57                   push    edi
007D5EC5 e836491400           call    0x91a800
007D5ECA 83c408               add     esp, 8
007D5ECD 85c0                 test    eax, eax
007D5ECF 740b                 je      0x7d5edc
007D5ED1 6a00                 push    0
007D5ED3 6a00                 push    0
007D5ED5 6862040000           push    0x462
007D5EDA ebcb                 jmp     0x7d5ea7
007D5EDC 8b5df0               mov     ebx, dword ptr [ebp - 0x10]
007D5EDF 85db                 test    ebx, ebx
007D5EE1 0f848a000000         je      0x7d5f71
007D5EE7 8b7e6c               mov     edi, dword ptr [esi + 0x6c]
007D5EEA 8bcb                 mov     ecx, ebx
007D5EEC 897df0               mov     dword ptr [ebp - 0x10], edi
007D5EEF e83c88d2ff           call    0x4fe730
007D5EF4 3bf8                 cmp     edi, eax
007D5EF6 7d0b                 jge     0x7d5f03
007D5EF8 6a00                 push    0
007D5EFA 6a00                 push    0
007D5EFC 6863040000           push    0x463
007D5F01 eba4                 jmp     0x7d5ea7
007D5F03 8b4308               mov     eax, dword ptr [ebx + 8]
007D5F06 85c0                 test    eax, eax
007D5F08 7510                 jne     0x7d5f1a
007D5F0A 837b2410             cmp     dword ptr [ebx + 0x24], 0x10
007D5F0E 7205                 jb      0x7d5f15
007D5F10 8b5310               mov     edx, dword ptr [ebx + 0x10]
007D5F13 eb15                 jmp     0x7d5f2a
007D5F15 8d5310               lea     edx, [ebx + 0x10]
007D5F18 eb10                 jmp     0x7d5f2a
007D5F1A 48                   dec     eax
007D5F1B 99                   cdq     
007D5F1C b906000000           mov     ecx, 6
007D5F21 f7f9                 idiv    ecx
007D5F23 8b1495645ab700       mov     edx, dword ptr [edx*4 + 0xb75a64]
007D5F2A 8b7df8               mov     edi, dword ptr [ebp - 8]
007D5F2D 8bc2                 mov     eax, edx
007D5F2F 90                   nop     
007D5F30 8a10                 mov     dl, byte ptr [eax]
007D5F32 8aca                 mov     cl, dl
007D5F34 3a17                 cmp     dl, byte ptr [edi]
007D5F36 751c                 jne     0x7d5f54
007D5F38 84c9                 test    cl, cl
007D5F3A 7414                 je      0x7d5f50
007D5F3C 8a5001               mov     dl, byte ptr [eax + 1]
007D5F3F 8aca                 mov     cl, dl
007D5F41 3a5701               cmp     dl, byte ptr [edi + 1]
007D5F44 750e                 jne     0x7d5f54
007D5F46 83c002               add     eax, 2
007D5F49 83c702               add     edi, 2
007D5F4C 84c9                 test    cl, cl
007D5F4E 75e0                 jne     0x7d5f30
007D5F50 33c0                 xor     eax, eax
007D5F52 eb05                 jmp     0x7d5f59
007D5F54 1bc0                 sbb     eax, eax
007D5F56 83d8ff               sbb     eax, -1
007D5F59 85c0                 test    eax, eax
007D5F5B 7514                 jne     0x7d5f71
007D5F5D 8b45f0               mov     eax, dword ptr [ebp - 0x10]
007D5F60 39433c               cmp     dword ptr [ebx + 0x3c], eax
007D5F63 750c                 jne     0x7d5f71
007D5F65 8b16                 mov     edx, dword ptr [esi]
007D5F67 8bce                 mov     ecx, esi
007D5F69 ff5228               call    dword ptr [edx + 0x28]
007D5F6C e9b7000000           jmp     0x7d6028
007D5F71 8b0d18b4b500         mov     ecx, dword ptr [0xb5b418]
007D5F77 8b7df8               mov     edi, dword ptr [ebp - 8]
007D5F7A 8b01                 mov     eax, dword ptr [ecx]
007D5F7C 57                   push    edi
007D5F7D ff5014               call    dword ptr [eax + 0x14]
007D5F80 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
007D5F86 6a0c                 push    0xc
007D5F88 e8c3c60d00           call    0x8b2650
007D5F8D 8a4df4               mov     cl, byte ptr [ebp - 0xc]
007D5F90 6a01                 push    1
007D5F92 8d55fc               lea     edx, [ebp - 4]
007D5F95 884dfc               mov     byte ptr [ebp - 4], cl
007D5F98 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
007D5F9E 52                   push    edx
007D5F9F e8acc40d00           call    0x8b2450
007D5FA4 8b45f4               mov     eax, dword ptr [ebp - 0xc]
007D5FA7 85c0                 test    eax, eax
007D5FA9 750c                 jne     0x7d5fb7
007D5FAB 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
007D5FB1 57                   push    edi
007D5FB2 e8197bc5ff           call    0x42dad0
007D5FB7 8a466c               mov     al, byte ptr [esi + 0x6c]
007D5FBA 6a01                 push    1
007D5FBC 8d4dfc               lea     ecx, [ebp - 4]
007D5FBF 51                   push    ecx
007D5FC0 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
007D5FC6 8845fc               mov     byte ptr [ebp - 4], al
007D5FC9 e882c40d00           call    0x8b2450
007D5FCE 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
007D5FD4 e8e7c40d00           call    0x8b24c0
007D5FD9 85db                 test    ebx, ebx
007D5FDB 7544                 jne     0x7d6021
007D5FDD 8b0d04ddb500         mov     ecx, dword ptr [0xb5dd04]
007D5FE3 6a04                 push    4
007D5FE5 e886350d00           call    0x8a9570
007D5FEA 83f801               cmp     eax, 1
007D5FED 7532                 jne     0x7d6021
007D5FEF 8b5610               mov     edx, dword ptr [esi + 0x10]
007D5FF2 8b7a60               mov     edi, dword ptr [edx + 0x60]
007D5FF5 53                   push    ebx
007D5FF6 53                   push    ebx
007D5FF7 6a10                 push    0x10
007D5FF9 68e6c89bff           push    0xff9bc8e6
007D5FFE 6864040000           push    0x464
007D6003 e898450d00           call    0x8aa5a0
007D6008 83c404               add     esp, 4
007D600B 50                   push    eax
007D600C 8bcf                 mov     ecx, edi
007D600E e84dfb0600           call    0x845b60
007D6013 8b0d04ddb500         mov     ecx, dword ptr [0xb5dd04]
007D6019 53                   push    ebx
007D601A 6a04                 push    4
007D601C e8df360d00           call    0x8a9700
007D6021 8b06                 mov     eax, dword ptr [esi]
007D6023 8bce                 mov     ecx, esi
007D6025 ff5028               call    dword ptr [eax + 0x28]
007D6028 8b4638               mov     eax, dword ptr [esi + 0x38]
007D602B 8a4811               mov     cl, byte ptr [eax + 0x11]
007D602E 84c9                 test    cl, cl
007D6030 8b1d8451b500         mov     ebx, dword ptr [0xb55184]
007D6036 741e                 je      0x7d6056
007D6038 3b580c               cmp     ebx, dword ptr [eax + 0xc]
007D603B 7519                 jne     0x7d6056
007D603D 6a00                 push    0
007D603F 6a00                 push    0
007D6041 6aff                 push    -1
007D6043 68f8d5a700           push    0xa7d5f8
007D6048 e843d8c5ff           call    0x433890
007D604D 8b1d8451b500         mov     ebx, dword ptr [0xb55184]
007D6053 83c410               add     esp, 0x10
007D6056 8b463c               mov     eax, dword ptr [esi + 0x3c]
007D6059 8a4811               mov     cl, byte ptr [eax + 0x11]
007D605C 84c9                 test    cl, cl
007D605E 7425                 je      0x7d6085
007D6060 3b580c               cmp     ebx, dword ptr [eax + 0xc]
007D6063 7520                 jne     0x7d6085
007D6065 8b16                 mov     edx, dword ptr [esi]
007D6067 8bce                 mov     ecx, esi
007D6069 ff5228               call    dword ptr [edx + 0x28]
007D606C 6a00                 push    0
007D606E 6a00                 push    0
007D6070 6aff                 push    -1
007D6072 68f8d5a700           push    0xa7d5f8
007D6077 e814d8c5ff           call    0x433890
007D607C 8b1d8451b500         mov     ebx, dword ptr [0xb55184]
007D6082 83c410               add     esp, 0x10
007D6085 8b4640               mov     eax, dword ptr [esi + 0x40]
007D6088 8a4811               mov     cl, byte ptr [eax + 0x11]
007D608B 84c9                 test    cl, cl
007D608D 743b                 je      0x7d60ca
007D608F 3b580c               cmp     ebx, dword ptr [eax + 0xc]
007D6092 7536                 jne     0x7d60ca
007D6094 8b4644               mov     eax, dword ptr [esi + 0x44]
007D6097 8a4810               mov     cl, byte ptr [eax + 0x10]
007D609A 84c9                 test    cl, cl
007D609C 8b4e44               mov     ecx, dword ptr [esi + 0x44]
007D609F 0f94c0               sete    al
007D60A2 884110               mov     byte ptr [ecx + 0x10], al
007D60A5 8b5648               mov     edx, dword ptr [esi + 0x48]
007D60A8 6a00                 push    0
007D60AA 6a00                 push    0
007D60AC 884210               mov     byte ptr [edx + 0x10], al
007D60AF 8b4e4c               mov     ecx, dword ptr [esi + 0x4c]
007D60B2 6aff                 push    -1
007D60B4 68f8d5a700           push    0xa7d5f8
007D60B9 884110               mov     byte ptr [ecx + 0x10], al
007D60BC e8cfd7c5ff           call    0x433890
007D60C1 8b1d8451b500         mov     ebx, dword ptr [0xb55184]
007D60C7 83c410               add     esp, 0x10
007D60CA 8d7e44               lea     edi, [esi + 0x44]
007D60CD 33c0                 xor     eax, eax
007D60CF 8bcf                 mov     ecx, edi
007D60D1 8b11                 mov     edx, dword ptr [ecx]
007D60D3 807a1100             cmp     byte ptr [edx + 0x11], 0
007D60D7 7405                 je      0x7d60de
007D60D9 3b5a0c               cmp     ebx, dword ptr [edx + 0xc]
007D60DC 740b                 je      0x7d60e9
007D60DE 40                   inc     eax
007D60DF 83c104               add     ecx, 4
007D60E2 83f803               cmp     eax, 3
007D60E5 7cea                 jl      0x7d60d1
007D60E7 eb33                 jmp     0x7d611c
007D60E9 83c002               add     eax, 2
007D60EC 89466c               mov     dword ptr [esi + 0x6c], eax
007D60EF 8b17                 mov     edx, dword ptr [edi]
007D60F1 c6421000             mov     byte ptr [edx + 0x10], 0
007D60F5 8b4648               mov     eax, dword ptr [esi + 0x48]
007D60F8 6a00                 push    0
007D60FA 6a00                 push    0
007D60FC c6401000             mov     byte ptr [eax + 0x10], 0
007D6100 8b4e4c               mov     ecx, dword ptr [esi + 0x4c]
007D6103 6aff                 push    -1
007D6105 68f8d5a700           push    0xa7d5f8
007D610A c6411000             mov     byte ptr [ecx + 0x10], 0
007D610E e87dd7c5ff           call    0x433890
007D6113 8b1d8451b500         mov     ebx, dword ptr [0xb55184]
007D6119 83c410               add     esp, 0x10
007D611C 8b4650               mov     eax, dword ptr [esi + 0x50]
007D611F 8a4811               mov     cl, byte ptr [eax + 0x11]
007D6122 84c9                 test    cl, cl
007D6124 744c                 je      0x7d6172
007D6126 3b580c               cmp     ebx, dword ptr [eax + 0xc]
007D6129 7547                 jne     0x7d6172
007D612B 8b5654               mov     edx, dword ptr [esi + 0x54]
007D612E 8a4210               mov     al, byte ptr [edx + 0x10]
007D6131 8bca                 mov     ecx, edx
007D6133 84c0                 test    al, al
007D6135 0f94c0               sete    al
007D6138 884110               mov     byte ptr [ecx + 0x10], al
007D613B 8b5658               mov     edx, dword ptr [esi + 0x58]
007D613E 884210               mov     byte ptr [edx + 0x10], al
007D6141 8b4e5c               mov     ecx, dword ptr [esi + 0x5c]
007D6144 884110               mov     byte ptr [ecx + 0x10], al
007D6147 8b5660               mov     edx, dword ptr [esi + 0x60]
007D614A 884210               mov     byte ptr [edx + 0x10], al
007D614D 8b4e64               mov     ecx, dword ptr [esi + 0x64]
007D6150 6a00                 push    0
007D6152 6a00                 push    0
007D6154 884110               mov     byte ptr [ecx + 0x10], al
007D6157 8b5668               mov     edx, dword ptr [esi + 0x68]
007D615A 6aff                 push    -1
007D615C 68f8d5a700           push    0xa7d5f8
007D6161 884210               mov     byte ptr [edx + 0x10], al
007D6164 e827d7c5ff           call    0x433890
007D6169 8b1d8451b500         mov     ebx, dword ptr [0xb55184]
007D616F 83c410               add     esp, 0x10
007D6172 33c0                 xor     eax, eax
007D6174 8d4e54               lea     ecx, [esi + 0x54]
007D6177 8b11                 mov     edx, dword ptr [ecx]
007D6179 807a1100             cmp     byte ptr [edx + 0x11], 0
007D617D 7405                 je      0x7d6184
007D617F 3b5a0c               cmp     ebx, dword ptr [edx + 0xc]
007D6182 740b                 je      0x7d618f
007D6184 40                   inc     eax
007D6185 83c104               add     ecx, 4
007D6188 83f806               cmp     eax, 6
007D618B 7cea                 jl      0x7d6177
007D618D eb5a                 jmp     0x7d61e9
007D618F 8b7e34               mov     edi, dword ptr [esi + 0x34]
007D6192 8b1c85645ab700       mov     ebx, dword ptr [eax*4 + 0xb75a64]
007D6199 8b07                 mov     eax, dword ptr [edi]
007D619B 8bcf                 mov     ecx, edi
007D619D ff5024               call    dword ptr [eax + 0x24]
007D61A0 53                   push    ebx
007D61A1 8d8fdc000000         lea     ecx, [edi + 0xdc]
007D61A7 e8e48b1100           call    0x8eed90
007D61AC 8b4e54               mov     ecx, dword ptr [esi + 0x54]
007D61AF c6411000             mov     byte ptr [ecx + 0x10], 0
007D61B3 8b5658               mov     edx, dword ptr [esi + 0x58]
007D61B6 c6421000             mov     byte ptr [edx + 0x10], 0
007D61BA 8b465c               mov     eax, dword ptr [esi + 0x5c]
007D61BD c6401000             mov     byte ptr [eax + 0x10], 0
007D61C1 8b4e60               mov     ecx, dword ptr [esi + 0x60]
007D61C4 c6411000             mov     byte ptr [ecx + 0x10], 0
007D61C8 8b5664               mov     edx, dword ptr [esi + 0x64]
007D61CB 6a00                 push    0
007D61CD 6a00                 push    0
007D61CF c6421000             mov     byte ptr [edx + 0x10], 0
007D61D3 8b4668               mov     eax, dword ptr [esi + 0x68]
007D61D6 6aff                 push    -1
007D61D8 68f8d5a700           push    0xa7d5f8
007D61DD c6401000             mov     byte ptr [eax + 0x10], 0
007D61E1 e8aad6c5ff           call    0x433890
007D61E6 83c410               add     esp, 0x10
007D61E9 6806010000           push    0x106
007D61EE 683c06a800           push    0xa8063c
007D61F3 e8c8d8c5ff           call    0x433ac0
007D61F8 83c408               add     esp, 8
007D61FB 85c0                 test    eax, eax
007D61FD 5f                   pop     edi
007D61FE 5b                   pop     ebx
007D61FF 741f                 je      0x7d6220
007D6201 6807010000           push    0x107
007D6206 683c06a800           push    0xa8063c
007D620B e8b0d8c5ff           call    0x433ac0
007D6210 8b10                 mov     edx, dword ptr [eax]
007D6212 83c408               add     esp, 8
007D6215 6a01                 push    1
007D6217 6a00                 push    0
007D6219 6a00                 push    0
007D621B 8bc8                 mov     ecx, eax
007D621D ff5240               call    dword ptr [edx + 0x40]
007D6220 8b4610               mov     eax, dword ptr [esi + 0x10]
007D6223 8b4860               mov     ecx, dword ptr [eax + 0x60]
007D6226 8b9190000000         mov     edx, dword ptr [ecx + 0x90]
007D622C c6421100             mov     byte ptr [edx + 0x11], 0
007D6230 5e                   pop     esi
007D6231 8be5                 mov     esp, ebp
007D6233 5d                   pop     ebp
007D6234 c3                   ret     

party_create_onclose_complete: [0x007D5C90,0x007D5CFD) size=109
007D5C90 56                   push    esi
007D5C91 8bf1                 mov     esi, ecx
007D5C93 8b4610               mov     eax, dword ptr [esi + 0x10]
007D5C96 8b4060               mov     eax, dword ptr [eax + 0x60]
007D5C99 8a886c010000         mov     cl, byte ptr [eax + 0x16c]
007D5C9F 84c9                 test    cl, cl
007D5CA1 7513                 jne     0x7d5cb6
007D5CA3 8b8884000000         mov     ecx, dword ptr [eax + 0x84]
007D5CA9 8b410c               mov     eax, dword ptr [ecx + 0xc]
007D5CAC a39c51b500           mov     dword ptr [0xb5519c], eax
007D5CB1 a3a451b500           mov     dword ptr [0xb551a4], eax
007D5CB6 6841010000           push    0x141
007D5CBB 681c06a800           push    0xa8061c
007D5CC0 e8fbddc5ff           call    0x433ac0
007D5CC5 83c408               add     esp, 8
007D5CC8 85c0                 test    eax, eax
007D5CCA 741f                 je      0x7d5ceb
007D5CCC 6842010000           push    0x142
007D5CD1 681c06a800           push    0xa8061c
007D5CD6 e8e5ddc5ff           call    0x433ac0
007D5CDB 8b10                 mov     edx, dword ptr [eax]
007D5CDD 83c408               add     esp, 8
007D5CE0 6a01                 push    1
007D5CE2 6a01                 push    1
007D5CE4 6a00                 push    0
007D5CE6 8bc8                 mov     ecx, eax
007D5CE8 ff5240               call    dword ptr [edx + 0x40]
007D5CEB 8b4610               mov     eax, dword ptr [esi + 0x10]
007D5CEE 8b4860               mov     ecx, dword ptr [eax + 0x60]
007D5CF1 8b9190000000         mov     edx, dword ptr [ecx + 0x90]
007D5CF7 c6421101             mov     byte ptr [edx + 0x11], 1
007D5CFB 5e                   pop     esi
007D5CFC c3                   ret     

friend_addremove_proc_complete: [0x007F9860,0x007F99EA) size=394
007F9860 55                   push    ebp
007F9861 8bec                 mov     ebp, esp
007F9863 51                   push    ecx
007F9864 56                   push    esi
007F9865 8bf1                 mov     esi, ecx
007F9867 8b4610               mov     eax, dword ptr [esi + 0x10]
007F986A 8b4870               mov     ecx, dword ptr [eax + 0x70]
007F986D 6a04                 push    4
007F986F e82c5a0500           call    0x84f2a0
007F9874 84c0                 test    al, al
007F9876 750c                 jne     0x7f9884
007F9878 8b16                 mov     edx, dword ptr [esi]
007F987A 8bce                 mov     ecx, esi
007F987C ff5228               call    dword ptr [edx + 0x28]
007F987F 5e                   pop     esi
007F9880 8be5                 mov     esp, ebp
007F9882 5d                   pop     ebp
007F9883 c3                   ret     
007F9884 8b4e08               mov     ecx, dword ptr [esi + 8]
007F9887 e874d10f00           call    0x8f6a00
007F988C 8b4608               mov     eax, dword ptr [esi + 8]
007F988F 8a4811               mov     cl, byte ptr [eax + 0x11]
007F9892 84c9                 test    cl, cl
007F9894 8b0d8451b500         mov     ecx, dword ptr [0xb55184]
007F989A 7415                 je      0x7f98b1
007F989C 3b480c               cmp     ecx, dword ptr [eax + 0xc]
007F989F 7510                 jne     0x7f98b1
007F98A1 8b4634               mov     eax, dword ptr [esi + 0x34]
007F98A4 8b400c               mov     eax, dword ptr [eax + 0xc]
007F98A7 a39c51b500           mov     dword ptr [0xb5519c], eax
007F98AC a3a451b500           mov     dword ptr [0xb551a4], eax
007F98B1 8b463c               mov     eax, dword ptr [esi + 0x3c]
007F98B4 8a5011               mov     dl, byte ptr [eax + 0x11]
007F98B7 84d2                 test    dl, dl
007F98B9 7405                 je      0x7f98c0
007F98BB 3b480c               cmp     ecx, dword ptr [eax + 0xc]
007F98BE 7412                 je      0x7f98d2
007F98C0 8b4634               mov     eax, dword ptr [esi + 0x34]
007F98C3 8a4878               mov     cl, byte ptr [eax + 0x78]
007F98C6 84c9                 test    cl, cl
007F98C8 c6407800             mov     byte ptr [eax + 0x78], 0
007F98CC 0f847d000000         je      0x7f994f
007F98D2 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007F98D5 e8462d0e00           call    0x8dc620
007F98DA 8d5001               lea     edx, [eax + 1]
007F98DD 8d4900               lea     ecx, [ecx]
007F98E0 8a08                 mov     cl, byte ptr [eax]
007F98E2 40                   inc     eax
007F98E3 84c9                 test    cl, cl
007F98E5 75f9                 jne     0x7f98e0
007F98E7 2bc2                 sub     eax, edx
007F98E9 8945fc               mov     dword ptr [ebp - 4], eax
007F98EC 751e                 jne     0x7f990c
007F98EE 6a00                 push    0
007F98F0 6a00                 push    0
007F98F2 68a8030000           push    0x3a8
007F98F7 e8a40c0b00           call    0x8aa5a0
007F98FC 8b4e10               mov     ecx, dword ptr [esi + 0x10]
007F98FF 83c404               add     esp, 4
007F9902 50                   push    eax
007F9903 6a23                 push    0x23
007F9905 e8e605faff           call    0x799ef0
007F990A eb43                 jmp     0x7f994f
007F990C 8b4640               mov     eax, dword ptr [esi + 0x40]
007F990F 85c0                 test    eax, eax
007F9911 751e                 jne     0x7f9931
007F9913 8b0dccdcb500         mov     ecx, dword ptr [0xb5dccc]
007F9919 57                   push    edi
007F991A 8b39                 mov     edi, dword ptr [ecx]
007F991C 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007F991F e8fc2c0e00           call    0x8dc620
007F9924 8b0dccdcb500         mov     ecx, dword ptr [0xb5dccc]
007F992A 50                   push    eax
007F992B ff5704               call    dword ptr [edi + 4]
007F992E 5f                   pop     edi
007F992F eb17                 jmp     0x7f9948
007F9931 8b4e34               mov     ecx, dword ptr [esi + 0x34]
007F9934 6a00                 push    0
007F9936 6a00                 push    0
007F9938 e8e32c0e00           call    0x8dc620
007F993D 8b4e10               mov     ecx, dword ptr [esi + 0x10]
007F9940 50                   push    eax
007F9941 6a35                 push    0x35
007F9943 e8a805faff           call    0x799ef0
007F9948 8b16                 mov     edx, dword ptr [esi]
007F994A 8bce                 mov     ecx, esi
007F994C ff5228               call    dword ptr [edx + 0x28]
007F994F 8b463c               mov     eax, dword ptr [esi + 0x3c]
007F9952 8a4811               mov     cl, byte ptr [eax + 0x11]
007F9955 84c9                 test    cl, cl
007F9957 741e                 je      0x7f9977
007F9959 8b0d8451b500         mov     ecx, dword ptr [0xb55184]
007F995F 3b480c               cmp     ecx, dword ptr [eax + 0xc]
007F9962 7513                 jne     0x7f9977
007F9964 6a00                 push    0
007F9966 6a00                 push    0
007F9968 6aff                 push    -1
007F996A 68f8d5a700           push    0xa7d5f8
007F996F e81c9fc3ff           call    0x433890
007F9974 83c410               add     esp, 0x10
007F9977 8b4638               mov     eax, dword ptr [esi + 0x38]
007F997A 8a4811               mov     cl, byte ptr [eax + 0x11]
007F997D 84c9                 test    cl, cl
007F997F 7425                 je      0x7f99a6
007F9981 8b158451b500         mov     edx, dword ptr [0xb55184]
007F9987 3b500c               cmp     edx, dword ptr [eax + 0xc]
007F998A 751a                 jne     0x7f99a6
007F998C 8b06                 mov     eax, dword ptr [esi]
007F998E 8bce                 mov     ecx, esi
007F9990 ff5028               call    dword ptr [eax + 0x28]
007F9993 6a00                 push    0
007F9995 6a00                 push    0
007F9997 6aff                 push    -1
007F9999 68f8d5a700           push    0xa7d5f8
007F999E e8ed9ec3ff           call    0x433890
007F99A3 83c410               add     esp, 0x10
007F99A6 6a6b                 push    0x6b
007F99A8 686c0ba800           push    0xa80b6c
007F99AD e80ea1c3ff           call    0x433ac0
007F99B2 83c408               add     esp, 8
007F99B5 85c0                 test    eax, eax
007F99B7 741c                 je      0x7f99d5
007F99B9 6a6c                 push    0x6c
007F99BB 686c0ba800           push    0xa80b6c
007F99C0 e8fba0c3ff           call    0x433ac0
007F99C5 8b10                 mov     edx, dword ptr [eax]
007F99C7 83c408               add     esp, 8
007F99CA 6a01                 push    1
007F99CC 6a00                 push    0
007F99CE 6a00                 push    0
007F99D0 8bc8                 mov     ecx, eax
007F99D2 ff5240               call    dword ptr [edx + 0x40]
007F99D5 8b4610               mov     eax, dword ptr [esi + 0x10]
007F99D8 8b4860               mov     ecx, dword ptr [eax + 0x60]
007F99DB 8b9190000000         mov     edx, dword ptr [ecx + 0x90]
007F99E1 c6421100             mov     byte ptr [edx + 0x11], 0
007F99E5 5e                   pop     esi
007F99E6 8be5                 mov     esp, ebp
007F99E8 5d                   pop     ebp
007F99E9 c3                   ret     

friend_addremove_onclose_complete: [0x007F9A90,0x007F9AFD) size=109
007F9A90 56                   push    esi
007F9A91 8bf1                 mov     esi, ecx
007F9A93 8b4610               mov     eax, dword ptr [esi + 0x10]
007F9A96 8b4060               mov     eax, dword ptr [eax + 0x60]
007F9A99 8a886c010000         mov     cl, byte ptr [eax + 0x16c]
007F9A9F 84c9                 test    cl, cl
007F9AA1 7513                 jne     0x7f9ab6
007F9AA3 8b8884000000         mov     ecx, dword ptr [eax + 0x84]
007F9AA9 8b410c               mov     eax, dword ptr [ecx + 0xc]
007F9AAC a39c51b500           mov     dword ptr [0xb5519c], eax
007F9AB1 a3a451b500           mov     dword ptr [0xb551a4], eax
007F9AB6 6897000000           push    0x97
007F9ABB 688c0ba800           push    0xa80b8c
007F9AC0 e8fb9fc3ff           call    0x433ac0
007F9AC5 83c408               add     esp, 8
007F9AC8 85c0                 test    eax, eax
007F9ACA 741f                 je      0x7f9aeb
007F9ACC 6898000000           push    0x98
007F9AD1 688c0ba800           push    0xa80b8c
007F9AD6 e8e59fc3ff           call    0x433ac0
007F9ADB 8b10                 mov     edx, dword ptr [eax]
007F9ADD 83c408               add     esp, 8
007F9AE0 6a01                 push    1
007F9AE2 6a01                 push    1
007F9AE4 6a00                 push    0
007F9AE6 8bc8                 mov     ecx, eax
007F9AE8 ff5240               call    dword ptr [edx + 0x40]
007F9AEB 8b4610               mov     eax, dword ptr [esi + 0x10]
007F9AEE 8b4860               mov     ecx, dword ptr [eax + 0x60]
007F9AF1 8b9190000000         mov     edx, dword ptr [ecx + 0x90]
007F9AF7 c6421101             mov     byte ptr [edx + 0x11], 1
007F9AFB 5e                   pop     esi
007F9AFC c3                   ret     

room_password_proc_complete: [0x00807EE0,0x0080803E) size=350
00807EE0 55                   push    ebp
00807EE1 8bec                 mov     ebp, esp
00807EE3 83ec08               sub     esp, 8
00807EE6 56                   push    esi
00807EE7 8bf1                 mov     esi, ecx
00807EE9 8b4e08               mov     ecx, dword ptr [esi + 8]
00807EEC e80feb0e00           call    0x8f6a00
00807EF1 8b4634               mov     eax, dword ptr [esi + 0x34]
00807EF4 8a4811               mov     cl, byte ptr [eax + 0x11]
00807EF7 84c9                 test    cl, cl
00807EF9 740b                 je      0x807f06
00807EFB 8b0d8451b500         mov     ecx, dword ptr [0xb55184]
00807F01 3b480c               cmp     ecx, dword ptr [eax + 0xc]
00807F04 7412                 je      0x807f18
00807F06 8b463c               mov     eax, dword ptr [esi + 0x3c]
00807F09 8a4878               mov     cl, byte ptr [eax + 0x78]
00807F0C 84c9                 test    cl, cl
00807F0E c6407800             mov     byte ptr [eax + 0x78], 0
00807F12 0f84b3000000         je      0x807fcb
00807F18 8b4e3c               mov     ecx, dword ptr [esi + 0x3c]
00807F1B 53                   push    ebx
00807F1C e8ff460d00           call    0x8dc620
00807F21 8bd8                 mov     ebx, eax
00807F23 8d5001               lea     edx, [eax + 1]
00807F26 8a08                 mov     cl, byte ptr [eax]
00807F28 40                   inc     eax
00807F29 84c9                 test    cl, cl
00807F2B 75f9                 jne     0x807f26
00807F2D 2bc2                 sub     eax, edx
00807F2F 57                   push    edi
00807F30 8bf8                 mov     edi, eax
00807F32 85ff                 test    edi, edi
00807F34 7e76                 jle     0x807fac
00807F36 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
00807F3C 33d2                 xor     edx, edx
00807F3E 8a5640               mov     dl, byte ptr [esi + 0x40]
00807F41 52                   push    edx
00807F42 e809a70a00           call    0x8b2650
00807F47 33c0                 xor     eax, eax
00807F49 668b4644             mov     ax, word ptr [esi + 0x44]
00807F4D 6a02                 push    2
00807F4F 8d4df8               lea     ecx, [ebp - 8]
00807F52 51                   push    ecx
00807F53 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
00807F59 8945f8               mov     dword ptr [ebp - 8], eax
00807F5C e8efa40a00           call    0x8b2450
00807F61 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
00807F67 6a01                 push    1
00807F69 8d55fc               lea     edx, [ebp - 4]
00807F6C 52                   push    edx
00807F6D c645fc01             mov     byte ptr [ebp - 4], 1
00807F71 e8daa40a00           call    0x8b2450
00807F76 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
00807F7C 6a04                 push    4
00807F7E 8d45f8               lea     eax, [ebp - 8]
00807F81 897df8               mov     dword ptr [ebp - 8], edi
00807F84 50                   push    eax
00807F85 8bf9                 mov     edi, ecx
00807F87 e8c4a40a00           call    0x8b2450
00807F8C 8b4df8               mov     ecx, dword ptr [ebp - 8]
00807F8F 51                   push    ecx
00807F90 53                   push    ebx
00807F91 8bcf                 mov     ecx, edi
00807F93 e8b8a40a00           call    0x8b2450
00807F98 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
00807F9E e81da50a00           call    0x8b24c0
00807FA3 8b16                 mov     edx, dword ptr [esi]
00807FA5 8bce                 mov     ecx, esi
00807FA7 ff5228               call    dword ptr [edx + 0x28]
00807FAA eb1d                 jmp     0x807fc9
00807FAC 6a10                 push    0x10
00807FAE 6872750000           push    0x7572
00807FB3 e8e8250a00           call    0x8aa5a0
00807FB8 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
00807FBE 83c404               add     esp, 4
00807FC1 50                   push    eax
00807FC2 6a00                 push    0
00807FC4 e82730f9ff           call    0x79aff0
00807FC9 5f                   pop     edi
00807FCA 5b                   pop     ebx
00807FCB 8b4638               mov     eax, dword ptr [esi + 0x38]
00807FCE 8a4811               mov     cl, byte ptr [eax + 0x11]
00807FD1 84c9                 test    cl, cl
00807FD3 7425                 je      0x807ffa
00807FD5 8b0d8451b500         mov     ecx, dword ptr [0xb55184]
00807FDB 3b480c               cmp     ecx, dword ptr [eax + 0xc]
00807FDE 751a                 jne     0x807ffa
00807FE0 8b16                 mov     edx, dword ptr [esi]
00807FE2 8bce                 mov     ecx, esi
00807FE4 ff5228               call    dword ptr [edx + 0x28]
00807FE7 6a00                 push    0
00807FE9 6a00                 push    0
00807FEB 6aff                 push    -1
00807FED 68f8d5a700           push    0xa7d5f8
00807FF2 e899b8c2ff           call    0x433890
00807FF7 83c410               add     esp, 0x10
00807FFA 6a62                 push    0x62
00807FFC 682011a800           push    0xa81120
00808001 e8babac2ff           call    0x433ac0
00808006 83c408               add     esp, 8
00808009 85c0                 test    eax, eax
0080800B 741c                 je      0x808029
0080800D 6a63                 push    0x63
0080800F 682011a800           push    0xa81120
00808014 e8a7bac2ff           call    0x433ac0
00808019 8b10                 mov     edx, dword ptr [eax]
0080801B 83c408               add     esp, 8
0080801E 6a01                 push    1
00808020 6a00                 push    0
00808022 6a00                 push    0
00808024 8bc8                 mov     ecx, eax
00808026 ff5240               call    dword ptr [edx + 0x40]
00808029 8b4610               mov     eax, dword ptr [esi + 0x10]
0080802C 8b4860               mov     ecx, dword ptr [eax + 0x60]
0080802F 8b9190000000         mov     edx, dword ptr [ecx + 0x90]
00808035 c6421100             mov     byte ptr [edx + 0x11], 0
00808039 5e                   pop     esi
0080803A 8be5                 mov     esp, ebp
0080803C 5d                   pop     ebp
0080803D c3                   ret     

room_password_onclose_complete: [0x008080F0,0x0080815D) size=109
008080F0 56                   push    esi
008080F1 8bf1                 mov     esi, ecx
008080F3 8b4610               mov     eax, dword ptr [esi + 0x10]
008080F6 8b4060               mov     eax, dword ptr [eax + 0x60]
008080F9 8a886c010000         mov     cl, byte ptr [eax + 0x16c]
008080FF 84c9                 test    cl, cl
00808101 7513                 jne     0x808116
00808103 8b8884000000         mov     ecx, dword ptr [eax + 0x84]
00808109 8b410c               mov     eax, dword ptr [ecx + 0xc]
0080810C a39c51b500           mov     dword ptr [0xb5519c], eax
00808111 a3a451b500           mov     dword ptr [0xb551a4], eax
00808116 688e000000           push    0x8e
0080811B 684411a800           push    0xa81144
00808120 e89bb9c2ff           call    0x433ac0
00808125 83c408               add     esp, 8
00808128 85c0                 test    eax, eax
0080812A 741f                 je      0x80814b
0080812C 688f000000           push    0x8f
00808131 684411a800           push    0xa81144
00808136 e885b9c2ff           call    0x433ac0
0080813B 8b10                 mov     edx, dword ptr [eax]
0080813D 83c408               add     esp, 8
00808140 6a01                 push    1
00808142 6a01                 push    1
00808144 6a00                 push    0
00808146 8bc8                 mov     ecx, eax
00808148 ff5240               call    dword ptr [edx + 0x40]
0080814B 8b4610               mov     eax, dword ptr [esi + 0x10]
0080814E 8b4860               mov     ecx, dword ptr [eax + 0x60]
00808151 8b9190000000         mov     edx, dword ptr [ecx + 0x90]
00808157 c6421101             mov     byte ptr [edx + 0x11], 1
0080815B 5e                   pop     esi
0080815C c3                   ret     

dialog_show_complete: [0x00799EF0,0x0079A08B) size=411
00799EF0 55                   push    ebp
00799EF1 8bec                 mov     ebp, esp
00799EF3 83ec08               sub     esp, 8
00799EF6 8b5508               mov     edx, dword ptr [ebp + 8]
00799EF9 53                   push    ebx
00799EFA 8bc2                 mov     eax, edx
00799EFC c1e004               shl     eax, 4
00799EFF 56                   push    esi
00799F00 8bd9                 mov     ebx, ecx
00799F02 57                   push    edi
00799F03 8d3c18               lea     edi, [eax + ebx]
00799F06 8b8fd8000000         mov     ecx, dword ptr [edi + 0xd8]
00799F0C 85c9                 test    ecx, ecx
00799F0E 8d87d4000000         lea     eax, [edi + 0xd4]
00799F14 8945f8               mov     dword ptr [ebp - 8], eax
00799F17 7504                 jne     0x799f1d
00799F19 33c0                 xor     eax, eax
00799F1B eb08                 jmp     0x799f25
00799F1D 8b4008               mov     eax, dword ptr [eax + 8]
00799F20 2bc1                 sub     eax, ecx
00799F22 c1f802               sar     eax, 2
00799F25 8b0c9528dea700       mov     ecx, dword ptr [edx*4 + 0xa7de28]
00799F2C 034d14               add     ecx, dword ptr [ebp + 0x14]
00799F2F 3bc8                 cmp     ecx, eax
00799F31 775f                 ja      0x799f92
00799F33 8b87d8000000         mov     eax, dword ptr [edi + 0xd8]
00799F39 8b30                 mov     esi, dword ptr [eax]
00799F3B 56                   push    esi
00799F3C 8bcb                 mov     ecx, ebx
00799F3E 8975fc               mov     dword ptr [ebp - 4], esi
00799F41 e8dafdffff           call    0x799d20
00799F46 8b8bfc0e0000         mov     ecx, dword ptr [ebx + 0xefc]
00799F4C 8b83f80e0000         mov     eax, dword ptr [ebx + 0xef8]
00799F52 3bc1                 cmp     eax, ecx
00799F54 745c                 je      0x799fb2
00799F56 3930                 cmp     dword ptr [eax], esi
00799F58 7407                 je      0x799f61
00799F5A 83c004               add     eax, 4
00799F5D 3bc1                 cmp     eax, ecx
00799F5F 75f5                 jne     0x799f56
00799F61 3bc1                 cmp     eax, ecx
00799F63 744d                 je      0x799fb2
00799F65 8b93fc0e0000         mov     edx, dword ptr [ebx + 0xefc]
00799F6B 8d4804               lea     ecx, [eax + 4]
00799F6E 2bd1                 sub     edx, ecx
00799F70 c1fa02               sar     edx, 2
00799F73 c1e202               shl     edx, 2
00799F76 52                   push    edx
00799F77 51                   push    ecx
00799F78 50                   push    eax
00799F79 e8c2fa1700           call    0x919a40
00799F7E 8b83fc0e0000         mov     eax, dword ptr [ebx + 0xefc]
00799F84 83c40c               add     esp, 0xc
00799F87 83c0fc               add     eax, -4
00799F8A 8983fc0e0000         mov     dword ptr [ebx + 0xefc], eax
00799F90 eb20                 jmp     0x799fb2
00799F92 8b8fe8070000         mov     ecx, dword ptr [edi + 0x7e8]
00799F98 85c9                 test    ecx, ecx
00799F9A 7469                 je      0x79a005
00799F9C 8b87ec070000         mov     eax, dword ptr [edi + 0x7ec]
00799FA2 2bc1                 sub     eax, ecx
00799FA4 c1f802               sar     eax, 2
00799FA7 85c0                 test    eax, eax
00799FA9 745a                 je      0x79a005
00799FAB 8bc1                 mov     eax, ecx
00799FAD 8b30                 mov     esi, dword ptr [eax]
00799FAF 8975fc               mov     dword ptr [ebp - 4], esi
00799FB2 85f6                 test    esi, esi
00799FB4 744c                 je      0x79a002
00799FB6 8b8fec070000         mov     ecx, dword ptr [edi + 0x7ec]
00799FBC 8b87e8070000         mov     eax, dword ptr [edi + 0x7e8]
00799FC2 3bc1                 cmp     eax, ecx
00799FC4 744e                 je      0x79a014
00799FC6 3930                 cmp     dword ptr [eax], esi
00799FC8 7407                 je      0x799fd1
00799FCA 83c004               add     eax, 4
00799FCD 3bc1                 cmp     eax, ecx
00799FCF 75f5                 jne     0x799fc6
00799FD1 3bc1                 cmp     eax, ecx
00799FD3 743f                 je      0x79a014
00799FD5 8b97ec070000         mov     edx, dword ptr [edi + 0x7ec]
00799FDB 8d4804               lea     ecx, [eax + 4]
00799FDE 2bd1                 sub     edx, ecx
00799FE0 c1fa02               sar     edx, 2
00799FE3 c1e202               shl     edx, 2
00799FE6 52                   push    edx
00799FE7 51                   push    ecx
00799FE8 50                   push    eax
00799FE9 e852fa1700           call    0x919a40
00799FEE 8b87ec070000         mov     eax, dword ptr [edi + 0x7ec]
00799FF4 83c40c               add     esp, 0xc
00799FF7 83c0fc               add     eax, -4
00799FFA 8987ec070000         mov     dword ptr [edi + 0x7ec], eax
0079A000 eb12                 jmp     0x79a014
0079A002 8b5508               mov     edx, dword ptr [ebp + 8]
0079A005 53                   push    ebx
0079A006 52                   push    edx
0079A007 e8543c0200           call    0x7bdc60
0079A00C 8bf0                 mov     esi, eax
0079A00E 83c408               add     esp, 8
0079A011 8975fc               mov     dword ptr [ebp - 4], esi
0079A014 85f6                 test    esi, esi
0079A016 7426                 je      0x79a03e
0079A018 8b4d14               mov     ecx, dword ptr [ebp + 0x14]
0079A01B 8b5510               mov     edx, dword ptr [ebp + 0x10]
0079A01E 8b06                 mov     eax, dword ptr [esi]
0079A020 51                   push    ecx
0079A021 8b4d0c               mov     ecx, dword ptr [ebp + 0xc]
0079A024 52                   push    edx
0079A025 51                   push    ecx
0079A026 8bce                 mov     ecx, esi
0079A028 ff500c               call    dword ptr [eax + 0xc]
0079A02B 84c0                 test    al, al
0079A02D 751a                 jne     0x79a049
0079A02F 8d55fc               lea     edx, [ebp - 4]
0079A032 52                   push    edx
0079A033 8d8fe4070000         lea     ecx, [edi + 0x7e4]
0079A039 e862750000           call    0x7a15a0
0079A03E 5f                   pop     edi
0079A03F 5e                   pop     esi
0079A040 33c0                 xor     eax, eax
0079A042 5b                   pop     ebx
0079A043 8be5                 mov     esp, ebp
0079A045 5d                   pop     ebp
0079A046 c21000               ret     0x10
0079A049 8b4df8               mov     ecx, dword ptr [ebp - 8]
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
0079A080 5f                   pop     edi
0079A081 8bc6                 mov     eax, esi
0079A083 5e                   pop     esi
0079A084 5b                   pop     ebx
0079A085 8be5                 mov     esp, ebp
0079A087 5d                   pop     ebp
0079A088 c21000               ret     0x10

dialog_remove_complete: [0x00799D20,0x00799E18) size=248
00799D20 55                   push    ebp
00799D21 8bec                 mov     ebp, esp
00799D23 83ec08               sub     esp, 8
00799D26 56                   push    esi
00799D27 8b7508               mov     esi, dword ptr [ebp + 8]
00799D2A 85f6                 test    esi, esi
00799D2C 57                   push    edi
00799D2D 8bf9                 mov     edi, ecx
00799D2F 897dfc               mov     dword ptr [ebp - 4], edi
00799D32 0f84d8000000         je      0x799e10
00799D38 6a00                 push    0
00799D3A 682023b500           push    0xb52320
00799D3F 6820d8af00           push    0xafd820
00799D44 6a00                 push    0
00799D46 56                   push    esi
00799D47 e8550f1800           call    0x91aca1
00799D4C 83c414               add     esp, 0x14
00799D4F 85c0                 test    eax, eax
00799D51 7412                 je      0x799d65
00799D53 8b8080000000         mov     eax, dword ptr [eax + 0x80]
00799D59 85c0                 test    eax, eax
00799D5B 7408                 je      0x799d65
00799D5D 50                   push    eax
00799D5E 8bcf                 mov     ecx, edi
00799D60 e8bbffffff           call    0x799d20
00799D65 8b06                 mov     eax, dword ptr [esi]
00799D67 53                   push    ebx
00799D68 8bce                 mov     ecx, esi
00799D6A ff5030               call    dword ptr [eax + 0x30]
00799D6D 8bd8                 mov     ebx, eax
00799D6F 8bcb                 mov     ecx, ebx
00799D71 c1e104               shl     ecx, 4
00799D74 8b840fd8000000       mov     eax, dword ptr [edi + ecx + 0xd8]
00799D7B 03f9                 add     edi, ecx
00799D7D 8b8fdc000000         mov     ecx, dword ptr [edi + 0xdc]
00799D83 3bc1                 cmp     eax, ecx
00799D85 0f8484000000         je      0x799e0f
00799D8B eb03                 jmp     0x799d90
00799D8D 8d4900               lea     ecx, [ecx]
00799D90 3930                 cmp     dword ptr [eax], esi
00799D92 7407                 je      0x799d9b
00799D94 83c004               add     eax, 4
00799D97 3bc1                 cmp     eax, ecx
00799D99 75f5                 jne     0x799d90
00799D9B 3bc1                 cmp     eax, ecx
00799D9D 7470                 je      0x799e0f
00799D9F 8b16                 mov     edx, dword ptr [esi]
00799DA1 8bce                 mov     ecx, esi
00799DA3 ff5210               call    dword ptr [edx + 0x10]
00799DA6 8b8fdc000000         mov     ecx, dword ptr [edi + 0xdc]
00799DAC 8b87d8000000         mov     eax, dword ptr [edi + 0xd8]
00799DB2 3bc1                 cmp     eax, ecx
00799DB4 741f                 je      0x799dd5
00799DB6 3930                 cmp     dword ptr [eax], esi
00799DB8 7407                 je      0x799dc1
00799DBA 83c004               add     eax, 4
00799DBD 3bc1                 cmp     eax, ecx
00799DBF 75f5                 jne     0x799db6
00799DC1 3bc1                 cmp     eax, ecx
00799DC3 7410                 je      0x799dd5
00799DC5 50                   push    eax
00799DC6 8d45f8               lea     eax, [ebp - 8]
00799DC9 50                   push    eax
00799DCA 8d8fd4000000         lea     ecx, [edi + 0xd4]
00799DD0 e85b4d0000           call    0x79eb30
00799DD5 8d4d08               lea     ecx, [ebp + 8]
00799DD8 51                   push    ecx
00799DD9 8d8fe4070000         lea     ecx, [edi + 0x7e4]
00799DDF e8bc770000           call    0x7a15a0
00799DE4 83fb0f               cmp     ebx, 0xf
00799DE7 7f04                 jg      0x799ded
00799DE9 33ff                 xor     edi, edi
00799DEB eb0b                 jmp     0x799df8
00799DED 33d2                 xor     edx, edx
00799DEF 83fb17               cmp     ebx, 0x17
00799DF2 0f9fc2               setg    dl
00799DF5 42                   inc     edx
00799DF6 8bfa                 mov     edi, edx
00799DF8 8b06                 mov     eax, dword ptr [esi]
00799DFA 8bce                 mov     ecx, esi
00799DFC ff502c               call    dword ptr [eax + 0x2c]
00799DFF 8b4dfc               mov     ecx, dword ptr [ebp - 4]
00799E02 8b8cb9c8000000       mov     ecx, dword ptr [ecx + edi*4 + 0xc8]
00799E09 50                   push    eax
00799E0A e811cb1500           call    0x8f6920
00799E0F 5b                   pop     ebx
00799E10 5f                   pop     edi
00799E11 5e                   pop     esi
00799E12 8be5                 mov     esp, ebp
00799E14 5d                   pop     ebp
00799E15 c20400               ret     4
