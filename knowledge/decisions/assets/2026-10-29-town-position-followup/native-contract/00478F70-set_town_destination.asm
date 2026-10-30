00478F70 55                       push     ebp
00478F71 8bec                     mov      ebp, esp
00478F73 8b81c8030000             mov      eax, dword ptr [ecx + 0x3c8]
00478F79 85c0                     test     eax, eax
00478F7B 740a                     je       0x478f87
00478F7D 8a90a0010000             mov      dl, byte ptr [eax + 0x1a0]
00478F83 84d2                     test     dl, dl
00478F85 751a                     jne      0x478fa1
00478F87 8b4508                   mov      eax, dword ptr [ebp + 8]
00478F8A 8b550c                   mov      edx, dword ptr [ebp + 0xc]
00478F8D 89813c020000             mov      dword ptr [ecx + 0x23c], eax
00478F93 8b01                     mov      eax, dword ptr [ecx]
00478F95 899140020000             mov      dword ptr [ecx + 0x240], edx
00478F9B ff90a4010000             call     dword ptr [eax + 0x1a4]
00478FA1 5d                       pop      ebp
00478FA2 c20800                   ret      8
