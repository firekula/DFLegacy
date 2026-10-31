004C5310 55                       push     ebp
004C5311 8bec                     mov      ebp, esp
004C5313 8b5110                   mov      edx, dword ptr [ecx + 0x10]
004C5316 85d2                     test     edx, edx
004C5318 7504                     jne      0x4c531e
004C531A 33c0                     xor      eax, eax
004C531C eb08                     jmp      0x4c5326
004C531E 8b4114                   mov      eax, dword ptr [ecx + 0x14]
004C5321 2bc2                     sub      eax, edx
004C5323 c1f802                   sar      eax, 2
004C5326 56                       push     esi
004C5327 33d2                     xor      edx, edx
004C5329 85c0                     test     eax, eax
004C532B 7e12                     jle      0x4c533f
004C532D 8b4910                   mov      ecx, dword ptr [ecx + 0x10]
004C5330 8b7508                   mov      esi, dword ptr [ebp + 8]
004C5333 3931                     cmp      dword ptr [ecx], esi
004C5335 740f                     je       0x4c5346
004C5337 42                       inc      edx
004C5338 83c104                   add      ecx, 4
004C533B 3bd0                     cmp      edx, eax
004C533D 7cf4                     jl       0x4c5333
004C533F 32c0                     xor      al, al
004C5341 5e                       pop      esi
004C5342 5d                       pop      ebp
004C5343 c20400                   ret      4
004C5346 b001                     mov      al, 1
004C5348 5e                       pop      esi
004C5349 5d                       pop      ebp
004C534A c20400                   ret      4
