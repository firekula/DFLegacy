0041E878 c6853bedffff00           mov      byte ptr [ebp - 0x12c5], 0
0041E87F 66c78530edffff0000       mov      word ptr [ebp - 0x12d0], 0
0041E888 66c7853cedffff0000       mov      word ptr [ebp - 0x12c4], 0
0041E891 66c78534edffff0000       mov      word ptr [ebp - 0x12cc], 0
0041E89A 66c7852cedffff6400       mov      word ptr [ebp - 0x12d4], 0x64
0041E8A3 8d8530edffff             lea      eax, [ebp - 0x12d0]
0041E8A9 50                       push     eax
0041E8AA e891684d00               call     0x8f5140
0041E8AF 83c404                   add      esp, 4
0041E8B2 8d8d3cedffff             lea      ecx, [ebp - 0x12c4]
0041E8B8 51                       push     ecx
0041E8B9 e882684d00               call     0x8f5140
0041E8BE 83c404                   add      esp, 4
0041E8C1 8d9534edffff             lea      edx, [ebp - 0x12cc]
0041E8C7 52                       push     edx
0041E8C8 e873684d00               call     0x8f5140
0041E8CD 83c404                   add      esp, 4
0041E8D0 8d853bedffff             lea      eax, [ebp - 0x12c5]
0041E8D6 50                       push     eax
0041E8D7 e844684d00               call     0x8f5120
0041E8DC 83c404                   add      esp, 4
0041E8DF 8d8d2cedffff             lea      ecx, [ebp - 0x12d4]
0041E8E5 51                       push     ecx
0041E8E6 e855684d00               call     0x8f5140
0041E8EB 83c404                   add      esp, 4
0041E8EE 0fb7b530edffff           movzx    esi, word ptr [ebp - 0x12d0]
0041E8F5 b9405ab700               mov      ecx, 0xb75a40
0041E8FA e86161feff               call     0x404a60
0041E8FF 3bf0                     cmp      esi, eax
0041E901 0f84b1000000             je       0x41e9b8
0041E907 668b9530edffff           mov      dx, word ptr [ebp - 0x12d0]
0041E90E 52                       push     edx
0041E90F 8b0db8dcb500             mov      ecx, dword ptr [0xb5dcb8]
0041E915 e896f40d00               call     0x4fddb0
0041E91A 85c0                     test     eax, eax
0041E91C 0f8496000000             je       0x41e9b8
0041E922 668b8530edffff           mov      ax, word ptr [ebp - 0x12d0]
0041E929 50                       push     eax
0041E92A 8b0db8dcb500             mov      ecx, dword ptr [0xb5dcb8]
0041E930 e87bf40d00               call     0x4fddb0
0041E935 8bc8                     mov      ecx, eax
0041E937 e86491ffff               call     0x417aa0
0041E93C 898528edffff             mov      dword ptr [ebp - 0x12d8], eax
0041E942 8b8df09effff             mov      ecx, dword ptr [ebp - 0x6110]
0041E948 e8a394ffff               call     0x417df0
0041E94D 8bc8                     mov      ecx, eax
0041E94F e86cc2feff               call     0x40abc0
0041E954 8bc8                     mov      ecx, eax
0041E956 e8255afeff               call     0x404380
0041E95B 898524edffff             mov      dword ptr [ebp - 0x12dc], eax
0041E961 83bd24edffff00           cmp      dword ptr [ebp - 0x12dc], 0
0041E968 744c                     je       0x41e9b6
0041E96A 8b8d28edffff             mov      ecx, dword ptr [ebp - 0x12d8]
0041E970 51                       push     ecx
0041E971 8b8d24edffff             mov      ecx, dword ptr [ebp - 0x12dc]
0041E977 e894690a00               call     0x4c5310
0041E97C 0fb6d0                   movzx    edx, al
0041E97F 85d2                     test     edx, edx
0041E981 7433                     je       0x41e9b6
0041E983 668b852cedffff           mov      ax, word ptr [ebp - 0x12d4]
0041E98A 50                       push     eax
0041E98B 8a8d3bedffff             mov      cl, byte ptr [ebp - 0x12c5]
0041E991 51                       push     ecx
0041E992 0fbf9534edffff           movsx    edx, word ptr [ebp - 0x12cc]
0041E999 52                       push     edx
0041E99A 0fbf853cedffff           movsx    eax, word ptr [ebp - 0x12c4]
0041E9A1 50                       push     eax
0041E9A2 8b8d28edffff             mov      ecx, dword ptr [ebp - 0x12d8]
0041E9A8 8b11                     mov      edx, dword ptr [ecx]
0041E9AA 8b8d28edffff             mov      ecx, dword ptr [ebp - 0x12d8]
0041E9B0 ff9270010000             call     dword ptr [edx + 0x170]
0041E9B6 eb0a                     jmp      0x41e9c2
0041E9B8 b9405ab700               mov      ecx, 0xb75a40
0041E9BD e89e60feff               call     0x404a60
0041E9C2 e9a2b60000               jmp      0x42a069
