004C9BF0 55                       push     ebp
004C9BF1 8bec                     mov      ebp, esp
004C9BF3 8b4178                   mov      eax, dword ptr [ecx + 0x78]
004C9BF6 85c0                     test     eax, eax
004C9BF8 7410                     je       0x4c9c0a
004C9BFA 8a417c                   mov      al, byte ptr [ecx + 0x7c]
004C9BFD 84c0                     test     al, al
004C9BFF 7409                     je       0x4c9c0a
004C9C01 8b4978                   mov      ecx, dword ptr [ecx + 0x78]
004C9C04 8b01                     mov      eax, dword ptr [ecx]
004C9C06 5d                       pop      ebp
004C9C07 ff6028                   jmp      dword ptr [eax + 0x28]
004C9C0A 8b01                     mov      eax, dword ptr [ecx]
004C9C0C 85c0                     test     eax, eax
004C9C0E 7503                     jne      0x4c9c13
004C9C10 8d4108                   lea      eax, [ecx + 8]
004C9C13 8b4d08                   mov      ecx, dword ptr [ebp + 8]
004C9C16 8b10                     mov      edx, dword ptr [eax]
004C9C18 51                       push     ecx
004C9C19 8bc8                     mov      ecx, eax
004C9C1B ff12                     call     dword ptr [edx]
004C9C1D 51                       push     ecx
004C9C1E d91c24                   fstp     dword ptr [esp]
004C9C21 e81a9df6ff               call     0x433940
004C9C26 83c404                   add      esp, 4
004C9C29 5d                       pop      ebp
004C9C2A c20400                   ret      4
