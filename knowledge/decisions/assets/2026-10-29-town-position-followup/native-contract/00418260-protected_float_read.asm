00418260 55                       push     ebp
00418261 8bec                     mov      ebp, esp
00418263 51                       push     ecx
00418264 53                       push     ebx
00418265 8b1d5cd4cf00             mov      ebx, dword ptr [0xcfd45c]
0041826B 56                       push     esi
0041826C 57                       push     edi
0041826D 8bf9                     mov      edi, ecx
0041826F 8b07                     mov      eax, dword ptr [edi]
00418271 8b4f04                   mov      ecx, dword ptr [edi + 4]
00418274 33c3                     xor      eax, ebx
00418276 2bc7                     sub      eax, edi
00418278 3bc1                     cmp      eax, ecx
0041827A 7414                     je       0x418290
0041827C 8b0d58d4cf00             mov      ecx, dword ptr [0xcfd458]
00418282 6a3f                     push     0x3f
00418284 57                       push     edi
00418285 e8e6914e00               call     0x901470
0041828A 8b1d5cd4cf00             mov      ebx, dword ptr [0xcfd45c]
00418290 8b0d58d4cf00             mov      ecx, dword ptr [0xcfd458]
00418296 8b4704                   mov      eax, dword ptr [edi + 4]
00418299 8b5144                   mov      edx, dword ptr [ecx + 0x44]
0041829C c1e002                   shl      eax, 2
0041829F 8b3402                   mov      esi, dword ptr [edx + eax]
004182A2 03d0                     add      edx, eax
004182A4 33f2                     xor      esi, edx
004182A6 8b5148                   mov      edx, dword ptr [ecx + 0x48]
004182A9 03c2                     add      eax, edx
004182AB 8b10                     mov      edx, dword ptr [eax]
004182AD 33d0                     xor      edx, eax
004182AF 33f3                     xor      esi, ebx
004182B1 33d3                     xor      edx, ebx
004182B3 3bf2                     cmp      esi, edx
004182B5 7408                     je       0x4182bf
004182B7 6a40                     push     0x40
004182B9 57                       push     edi
004182BA e8b1914e00               call     0x901470
004182BF 5f                       pop      edi
004182C0 8975fc                   mov      dword ptr [ebp - 4], esi
004182C3 d945fc                   fld      dword ptr [ebp - 4]
004182C6 5e                       pop      esi
004182C7 5b                       pop      ebx
004182C8 8be5                     mov      esp, ebp
004182CA 5d                       pop      ebp
004182CB c3                       ret      
