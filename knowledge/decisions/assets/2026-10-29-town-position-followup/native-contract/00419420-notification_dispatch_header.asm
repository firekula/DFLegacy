00419420 55                       push     ebp
00419421 8bec                     mov      ebp, esp
00419423 6aff                     push     -1
00419425 68eab49500               push     0x95b4ea
0041942A 64a100000000             mov      eax, dword ptr fs:[0]
00419430 50                       push     eax
00419431 64892500000000           mov      dword ptr fs:[0], esp
00419438 b840620000               mov      eax, 0x6240
0041943D e8fe1b5000               call     0x91b040
00419442 53                       push     ebx
00419443 56                       push     esi
00419444 57                       push     edi
00419445 898df09effff             mov      dword ptr [ebp - 0x6110], ecx
0041944B 8b4508                   mov      eax, dword ptr [ebp + 8]
0041944E 8a4801                   mov      cl, byte ptr [eax + 1]
00419451 884df3                   mov      byte ptr [ebp - 0xd], cl
00419454 0fb655f3                 movzx    edx, byte ptr [ebp - 0xd]
00419458 8995ec9effff             mov      dword ptr [ebp - 0x6114], edx
0041945E 81bdec9effff97000000     cmp      dword ptr [ebp - 0x6114], 0x97
00419468 0f87fb0b0100             ja       0x42a069
0041946E 8b85ec9effff             mov      eax, dword ptr [ebp - 0x6114]
00419474 ff248581a04200           jmp      dword ptr [eax*4 + 0x42a081]
