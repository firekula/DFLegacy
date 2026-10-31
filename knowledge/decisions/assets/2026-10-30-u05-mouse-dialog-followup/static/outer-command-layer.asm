outer_command_state_update_complete: [0x008BAAD0,0x008BAC84)
008BAAD0 55                   push    ebp
008BAAD1 8bec                 mov     ebp, esp
008BAAD3 83ec18               sub     esp, 0x18
008BAAD6 53                   push    ebx
008BAAD7 56                   push    esi
008BAAD8 8bf1                 mov     esi, ecx
008BAADA 57                   push    edi
008BAADB 8d4e04               lea     ecx, [esi + 4]
008BAADE e87d940300           call    0x8f3f60
008BAAE3 8b4e54               mov     ecx, dword ptr [esi + 0x54]
008BAAE6 8b5658               mov     edx, dword ptr [esi + 0x58]
008BAAE9 8bf8                 mov     edi, eax
008BAAEB 8bc1                 mov     eax, ecx
008BAAED 3bc2                 cmp     eax, edx
008BAAEF 897dfc               mov     dword ptr [ebp - 4], edi
008BAAF2 7418                 je      0x8bab0c
008BAAF4 8a580c               mov     bl, byte ptr [eax + 0xc]
008BAAF7 84db                 test    bl, bl
008BAAF9 7511                 jne     0x8bab0c
008BAAFB 8b5e60               mov     ebx, dword ptr [esi + 0x60]
008BAAFE 035804               add     ebx, dword ptr [eax + 4]
008BAB01 3bdf                 cmp     ebx, edi
008BAB03 7d07                 jge     0x8bab0c
008BAB05 83c010               add     eax, 0x10
008BAB08 3bc2                 cmp     eax, edx
008BAB0A 75e8                 jne     0x8baaf4
008BAB0C 3bc1                 cmp     eax, ecx
008BAB0E 743d                 je      0x8bab4d
008BAB10 8b5658               mov     edx, dword ptr [esi + 0x58]
008BAB13 3bc2                 cmp     eax, edx
008BAB15 8955f8               mov     dword ptr [ebp - 8], edx
008BAB18 7430                 je      0x8bab4a
008BAB1A 8d9b00000000         lea     ebx, [ebx]
008BAB20 8bd0                 mov     edx, eax
008BAB22 8b1a                 mov     ebx, dword ptr [edx]
008BAB24 8bf9                 mov     edi, ecx
008BAB26 891f                 mov     dword ptr [edi], ebx
008BAB28 8b5a04               mov     ebx, dword ptr [edx + 4]
008BAB2B 895f04               mov     dword ptr [edi + 4], ebx
008BAB2E 8b5a08               mov     ebx, dword ptr [edx + 8]
008BAB31 895f08               mov     dword ptr [edi + 8], ebx
008BAB34 8b520c               mov     edx, dword ptr [edx + 0xc]
008BAB37 89570c               mov     dword ptr [edi + 0xc], edx
008BAB3A 8b55f8               mov     edx, dword ptr [ebp - 8]
008BAB3D 83c010               add     eax, 0x10
008BAB40 83c110               add     ecx, 0x10
008BAB43 3bc2                 cmp     eax, edx
008BAB45 75d9                 jne     0x8bab20
008BAB47 8b7dfc               mov     edi, dword ptr [ebp - 4]
008BAB4A 894e58               mov     dword ptr [esi + 0x58], ecx
008BAB4D 8b4654               mov     eax, dword ptr [esi + 0x54]
008BAB50 3b4658               cmp     eax, dword ptr [esi + 0x58]
008BAB53 7426                 je      0x8bab7b
008BAB55 8a480d               mov     cl, byte ptr [eax + 0xd]
008BAB58 84c9                 test    cl, cl
008BAB5A 7404                 je      0x8bab60
008BAB5C c6400d00             mov     byte ptr [eax + 0xd], 0
008BAB60 8a480c               mov     cl, byte ptr [eax + 0xc]
008BAB63 84c9                 test    cl, cl
008BAB65 740a                 je      0x8bab71
008BAB67 8b5004               mov     edx, dword ptr [eax + 4]
008BAB6A 8bcf                 mov     ecx, edi
008BAB6C 2bca                 sub     ecx, edx
008BAB6E 894808               mov     dword ptr [eax + 8], ecx
008BAB71 8b4e58               mov     ecx, dword ptr [esi + 0x58]
008BAB74 83c010               add     eax, 0x10
008BAB77 3bc1                 cmp     eax, ecx
008BAB79 75da                 jne     0x8bab55
008BAB7B 8b4648               mov     eax, dword ptr [esi + 0x48]
008BAB7E 8b38                 mov     edi, dword ptr [eax]
008BAB80 3bf8                 cmp     edi, eax
008BAB82 0f84f5000000         je      0x8bac7d
008BAB88 8b16                 mov     edx, dword ptr [esi]
008BAB8A 8b5f0c               mov     ebx, dword ptr [edi + 0xc]
008BAB8D 8d4710               lea     eax, [edi + 0x10]
008BAB90 50                   push    eax
008BAB91 8bce                 mov     ecx, esi
008BAB93 ff5230               call    dword ptr [edx + 0x30]
008BAB96 84c0                 test    al, al
008BAB98 7527                 jne     0x8babc1
008BAB9A 8b4e6c               mov     ecx, dword ptr [esi + 0x6c]
008BAB9D 85c9                 test    ecx, ecx
008BAB9F 7520                 jne     0x8babc1
008BABA1 8b5678               mov     edx, dword ptr [esi + 0x78]
008BABA4 8b0a                 mov     ecx, dword ptr [edx]
008BABA6 3bca                 cmp     ecx, edx
008BABA8 7417                 je      0x8babc1
008BABAA 8d9b00000000         lea     ebx, [ebx]
008BABB0 395908               cmp     dword ptr [ecx + 8], ebx
008BABB3 7406                 je      0x8babbb
008BABB5 8b09                 mov     ecx, dword ptr [ecx]
008BABB7 3bca                 cmp     ecx, edx
008BABB9 75f5                 jne     0x8babb0
008BABBB 3bca                 cmp     ecx, edx
008BABBD 7402                 je      0x8babc1
008BABBF b001                 mov     al, 1
008BABC1 8b4e58               mov     ecx, dword ptr [esi + 0x58]
008BABC4 2b4e54               sub     ecx, dword ptr [esi + 0x54]
008BABC7 c1f904               sar     ecx, 4
008BABCA 49                   dec     ecx
008BABCB 781a                 js      0x8babe7
008BABCD 8bd1                 mov     edx, ecx
008BABCF c1e204               shl     edx, 4
008BABD2 035654               add     edx, dword ptr [esi + 0x54]
008BABD5 391a                 cmp     dword ptr [edx], ebx
008BABD7 7506                 jne     0x8babdf
008BABD9 807a0c00             cmp     byte ptr [edx + 0xc], 0
008BABDD 7508                 jne     0x8babe7
008BABDF 49                   dec     ecx
008BABE0 83ea10               sub     edx, 0x10
008BABE3 85c9                 test    ecx, ecx
008BABE5 7dee                 jge     0x8babd5
008BABE7 84c0                 test    al, al
008BABE9 742b                 je      0x8bac16
008BABEB 83f9ff               cmp     ecx, -1
008BABEE 753e                 jne     0x8bac2e
008BABF0 8b4dfc               mov     ecx, dword ptr [ebp - 4]
008BABF3 8d55e8               lea     edx, [ebp - 0x18]
008BABF6 894dec               mov     dword ptr [ebp - 0x14], ecx
008BABF9 52                   push    edx
008BABFA 8d4e50               lea     ecx, [esi + 0x50]
008BABFD c745f000000000       mov     dword ptr [ebp - 0x10], 0
008BAC04 895de8               mov     dword ptr [ebp - 0x18], ebx
008BAC07 c645f401             mov     byte ptr [ebp - 0xc], 1
008BAC0B c645f501             mov     byte ptr [ebp - 0xb], 1
008BAC0F e8cc6cc2ff           call    0x4e18e0
008BAC14 eb18                 jmp     0x8bac2e
008BAC16 83f9ff               cmp     ecx, -1
008BAC19 7413                 je      0x8bac2e
008BAC1B 8b4654               mov     eax, dword ptr [esi + 0x54]
008BAC1E c1e104               shl     ecx, 4
008BAC21 c644010c00           mov     byte ptr [ecx + eax + 0xc], 0
008BAC26 8b5654               mov     edx, dword ptr [esi + 0x54]
008BAC29 c644110d01           mov     byte ptr [ecx + edx + 0xd], 1
008BAC2E 8a471d               mov     al, byte ptr [edi + 0x1d]
008BAC31 84c0                 test    al, al
008BAC33 753f                 jne     0x8bac74
008BAC35 8b4f08               mov     ecx, dword ptr [edi + 8]
008BAC38 8a411d               mov     al, byte ptr [ecx + 0x1d]
008BAC3B 84c0                 test    al, al
008BAC3D 7518                 jne     0x8bac57
008BAC3F 8b01                 mov     eax, dword ptr [ecx]
008BAC41 8a501d               mov     dl, byte ptr [eax + 0x1d]
008BAC44 84d2                 test    dl, dl
008BAC46 750b                 jne     0x8bac53
008BAC48 8bc8                 mov     ecx, eax
008BAC4A 8b01                 mov     eax, dword ptr [ecx]
008BAC4C 8a501d               mov     dl, byte ptr [eax + 0x1d]
008BAC4F 84d2                 test    dl, dl
008BAC51 74f5                 je      0x8bac48
008BAC53 8bf9                 mov     edi, ecx
008BAC55 eb1d                 jmp     0x8bac74
008BAC57 8b4704               mov     eax, dword ptr [edi + 4]
008BAC5A 8a481d               mov     cl, byte ptr [eax + 0x1d]
008BAC5D 84c9                 test    cl, cl
008BAC5F 7511                 jne     0x8bac72
008BAC61 3b7808               cmp     edi, dword ptr [eax + 8]
008BAC64 750c                 jne     0x8bac72
008BAC66 8bf8                 mov     edi, eax
008BAC68 8b4004               mov     eax, dword ptr [eax + 4]
008BAC6B 8a481d               mov     cl, byte ptr [eax + 0x1d]
008BAC6E 84c9                 test    cl, cl
008BAC70 74ef                 je      0x8bac61
008BAC72 8bf8                 mov     edi, eax
008BAC74 3b7e48               cmp     edi, dword ptr [esi + 0x48]
008BAC77 0f850bffffff         jne     0x8bab88
008BAC7D 5f                   pop     edi
008BAC7E 5e                   pop     esi
008BAC7F 5b                   pop     ebx
008BAC80 8be5                 mov     esp, ebp
008BAC82 5d                   pop     ebp
008BAC83 c3                   ret     
