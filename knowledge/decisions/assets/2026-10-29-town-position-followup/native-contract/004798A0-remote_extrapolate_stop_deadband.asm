004798A0 55                       push     ebp
004798A1 8bec                     mov      ebp, esp
004798A3 668b4518                 mov      ax, word ptr [ebp + 0x18]
004798A7 6685c0                   test     ax, ax
004798AA 53                       push     ebx
004798AB 56                       push     esi
004798AC 57                       push     edi
004798AD 8bf9                     mov      edi, ecx
004798AF 7455                     je       0x479906
004798B1 8b1558d4cf00             mov      edx, dword ptr [0xcfd458]
004798B7 8b8f48020000             mov      ecx, dword ptr [edi + 0x248]
004798BD 8b1d5cd4cf00             mov      ebx, dword ptr [0xcfd45c]
004798C3 0fbfc0                   movsx    eax, ax
004798C6 894518                   mov      dword ptr [ebp + 0x18], eax
004798C9 8b4244                   mov      eax, dword ptr [edx + 0x44]
004798CC 8d0488                   lea      eax, [eax + ecx*4]
004798CF db4518                   fild     dword ptr [ebp + 0x18]
004798D2 8bd0                     mov      edx, eax
004798D4 33d3                     xor      edx, ebx
004798D6 d80d70a79900             fmul     dword ptr [0x99a770]
004798DC d95d18                   fstp     dword ptr [ebp + 0x18]
004798DF 8b4d18                   mov      ecx, dword ptr [ebp + 0x18]
004798E2 33d1                     xor      edx, ecx
004798E4 8910                     mov      dword ptr [eax], edx
004798E6 8b1558d4cf00             mov      edx, dword ptr [0xcfd458]
004798EC 8b5248                   mov      edx, dword ptr [edx + 0x48]
004798EF 8b8748020000             mov      eax, dword ptr [edi + 0x248]
004798F5 8b1d5cd4cf00             mov      ebx, dword ptr [0xcfd45c]
004798FB 8d0482                   lea      eax, [edx + eax*4]
004798FE 8bd0                     mov      edx, eax
00479900 33d3                     xor      edx, ebx
00479902 33d1                     xor      edx, ecx
00479904 8910                     mov      dword ptr [eax], edx
00479906 837d1404                 cmp      dword ptr [ebp + 0x14], 4
0047990A 8d9f44020000             lea      ebx, [edi + 0x244]
00479910 8bcb                     mov      ecx, ebx
00479912 750d                     jne      0x479921
00479914 e847e9f9ff               call     0x418260
00479919 d80df8d69900             fmul     dword ptr [0x99d6f8]
0047991F eb0b                     jmp      0x47992c
00479921 e83ae9f9ff               call     0x418260
00479926 d80df4d69900             fmul     dword ptr [0x99d6f4]
0047992C e8d71d4a00               call     0x91b708
00479931 8bf0                     mov      esi, eax
00479933 837d1004                 cmp      dword ptr [ebp + 0x10], 4
00479937 8bcb                     mov      ecx, ebx
00479939 750d                     jne      0x479948
0047993B e820e9f9ff               call     0x418260
00479940 d80df0d69900             fmul     dword ptr [0x99d6f0]
00479946 eb0b                     jmp      0x479953
00479948 e813e9f9ff               call     0x418260
0047994D d80decd69900             fmul     dword ptr [0x99d6ec]
00479953 e8b01d4a00               call     0x91b708
00479958 8b5d08                   mov      ebx, dword ptr [ebp + 8]
0047995B 8bc8                     mov      ecx, eax
0047995D 8b4510                   mov      eax, dword ptr [ebp + 0x10]
00479960 85c0                     test     eax, eax
00479962 7508                     jne      0x47996c
00479964 69f60cfeffff             imul     esi, esi, 0xfffffe0c
0047996A eb0b                     jmp      0x479977
0047996C 83f801                   cmp      eax, 1
0047996F 7519                     jne      0x47998a
00479971 69f6f4010000             imul     esi, esi, 0x1f4
00479977 b8d34d6210               mov      eax, 0x10624dd3
0047997C f7ee                     imul     esi
0047997E c1fa06                   sar      edx, 6
00479981 8bc2                     mov      eax, edx
00479983 c1e81f                   shr      eax, 0x1f
00479986 03c2                     add      eax, edx
00479988 03d8                     add      ebx, eax
0047998A 8b4514                   mov      eax, dword ptr [ebp + 0x14]
0047998D 83f802                   cmp      eax, 2
00479990 8b750c                   mov      esi, dword ptr [ebp + 0xc]
00479993 751b                     jne      0x4799b0
00479995 69c90cfeffff             imul     ecx, ecx, 0xfffffe0c
0047999B b8d34d6210               mov      eax, 0x10624dd3
004799A0 f7e9                     imul     ecx
004799A2 c1fa06                   sar      edx, 6
004799A5 8bca                     mov      ecx, edx
004799A7 c1e91f                   shr      ecx, 0x1f
004799AA 03ca                     add      ecx, edx
004799AC 03f1                     add      esi, ecx
004799AE eb1e                     jmp      0x4799ce
004799B0 83f803                   cmp      eax, 3
004799B3 7519                     jne      0x4799ce
004799B5 69c9f4010000             imul     ecx, ecx, 0x1f4
004799BB b8d34d6210               mov      eax, 0x10624dd3
004799C0 f7e9                     imul     ecx
004799C2 c1fa06                   sar      edx, 6
004799C5 8bc2                     mov      eax, edx
004799C7 c1e81f                   shr      eax, 0x1f
004799CA 03c2                     add      eax, edx
004799CC 03f0                     add      esi, eax
004799CE 8b4d10                   mov      ecx, dword ptr [ebp + 0x10]
004799D1 b804000000               mov      eax, 4
004799D6 3bc8                     cmp      ecx, eax
004799D8 7551                     jne      0x479a2b
004799DA 394514                   cmp      dword ptr [ebp + 0x14], eax
004799DD 754c                     jne      0x479a2b
004799DF 8b17                     mov      edx, dword ptr [edi]
004799E1 8bcf                     mov      ecx, edi
004799E3 ff9284000000             call     dword ptr [edx + 0x84]
004799E9 8bcb                     mov      ecx, ebx
004799EB 2bc8                     sub      ecx, eax
004799ED 8bc1                     mov      eax, ecx
004799EF 99                       cdq      
004799F0 33c2                     xor      eax, edx
004799F2 2bc2                     sub      eax, edx
004799F4 83f832                   cmp      eax, 0x32
004799F7 7d0c                     jge      0x479a05
004799F9 8b17                     mov      edx, dword ptr [edi]
004799FB 8bcf                     mov      ecx, edi
004799FD ff9284000000             call     dword ptr [edx + 0x84]
00479A03 8bd8                     mov      ebx, eax
00479A05 8b07                     mov      eax, dword ptr [edi]
00479A07 8bcf                     mov      ecx, edi
00479A09 ff9088000000             call     dword ptr [eax + 0x88]
00479A0F 8bce                     mov      ecx, esi
00479A11 2bc8                     sub      ecx, eax
00479A13 8bc1                     mov      eax, ecx
00479A15 99                       cdq      
00479A16 33c2                     xor      eax, edx
00479A18 2bc2                     sub      eax, edx
00479A1A 83f832                   cmp      eax, 0x32
00479A1D 7d0c                     jge      0x479a2b
00479A1F 8b17                     mov      edx, dword ptr [edi]
00479A21 8bcf                     mov      ecx, edi
00479A23 ff9288000000             call     dword ptr [edx + 0x88]
00479A29 8bf0                     mov      esi, eax
00479A2B 8b07                     mov      eax, dword ptr [edi]
00479A2D 56                       push     esi
00479A2E 53                       push     ebx
00479A2F 8bcf                     mov      ecx, edi
00479A31 ff9078010000             call     dword ptr [eax + 0x178]
00479A37 5f                       pop      edi
00479A38 5e                       pop      esi
00479A39 5b                       pop      ebx
00479A3A 5d                       pop      ebp
00479A3B c21400                   ret      0x14
