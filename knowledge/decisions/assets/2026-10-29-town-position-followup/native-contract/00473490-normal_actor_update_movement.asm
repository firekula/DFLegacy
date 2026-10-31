00473490 55                       push     ebp
00473491 8bec                     mov      ebp, esp
00473493 83ec0c                   sub      esp, 0xc
00473496 56                       push     esi
00473497 8bf1                     mov      esi, ecx
00473499 8b8654010000             mov      eax, dword ptr [esi + 0x154]
0047349F 85c0                     test     eax, eax
004734A1 742b                     je       0x4734ce
004734A3 83be58010000ff           cmp      dword ptr [esi + 0x158], -1
004734AA 7422                     je       0x4734ce
004734AC 8d4e08                   lea      ecx, [esi + 8]
004734AF e8ac0a4800               call     0x8f3f60
004734B4 8b8e5c010000             mov      ecx, dword ptr [esi + 0x15c]
004734BA 038e58010000             add      ecx, dword ptr [esi + 0x158]
004734C0 3bc1                     cmp      eax, ecx
004734C2 7c0a                     jl       0x4734ce
004734C4 8b16                     mov      edx, dword ptr [esi]
004734C6 8bce                     mov      ecx, esi
004734C8 ff92f8000000             call     dword ptr [edx + 0xf8]
004734CE 8b8ee0000000             mov      ecx, dword ptr [esi + 0xe0]
004734D4 e8276a0500               call     0x4c9f00
004734D9 8b06                     mov      eax, dword ptr [esi]
004734DB 8bce                     mov      ecx, esi
004734DD ff908c000000             call     dword ptr [eax + 0x8c]
004734E3 8b16                     mov      edx, dword ptr [esi]
004734E5 8945fc                   mov      dword ptr [ebp - 4], eax
004734E8 db45fc                   fild     dword ptr [ebp - 4]
004734EB 8bce                     mov      ecx, esi
004734ED d95df4                   fstp     dword ptr [ebp - 0xc]
004734F0 ff9288000000             call     dword ptr [edx + 0x88]
004734F6 8945fc                   mov      dword ptr [ebp - 4], eax
004734F9 db45fc                   fild     dword ptr [ebp - 4]
004734FC 8b06                     mov      eax, dword ptr [esi]
004734FE 8bce                     mov      ecx, esi
00473500 d95df8                   fstp     dword ptr [ebp - 8]
00473503 ff9084000000             call     dword ptr [eax + 0x84]
00473509 8b4df8                   mov      ecx, dword ptr [ebp - 8]
0047350C 8b55f4                   mov      edx, dword ptr [ebp - 0xc]
0047350F 8945fc                   mov      dword ptr [ebp - 4], eax
00473512 db45fc                   fild     dword ptr [ebp - 4]
00473515 898e40010000             mov      dword ptr [esi + 0x140], ecx
0047351B 899644010000             mov      dword ptr [esi + 0x144], edx
00473521 6a02                     push     2
00473523 d99e3c010000             fstp     dword ptr [esi + 0x13c]
00473529 8b06                     mov      eax, dword ptr [esi]
0047352B 8bce                     mov      ecx, esi
0047352D ff502c                   call     dword ptr [eax + 0x2c]
00473530 84c0                     test     al, al
00473532 0f8497000000             je       0x4735cf
00473538 8b8ee0000000             mov      ecx, dword ptr [esi + 0xe0]
0047353E 8b4978                   mov      ecx, dword ptr [ecx + 0x78]
00473541 85c9                     test     ecx, ecx
00473543 7412                     je       0x473557
00473545 8b11                     mov      edx, dword ptr [ecx]
00473547 ff5234                   call     dword ptr [edx + 0x34]
0047354A 84c0                     test     al, al
0047354C 7509                     jne      0x473557
0047354E 8b06                     mov      eax, dword ptr [esi]
00473550 6a00                     push     0
00473552 8bce                     mov      ecx, esi
00473554 ff5058                   call     dword ptr [eax + 0x58]
00473557 8a8629010000             mov      al, byte ptr [esi + 0x129]
0047355D b201                     mov      dl, 1
0047355F 3ac2                     cmp      al, dl
00473561 7538                     jne      0x47359b
00473563 8b86e8000000             mov      eax, dword ptr [esi + 0xe8]
00473569 85c0                     test     eax, eax
0047356B 742e                     je       0x47359b
0047356D 8a88fa000000             mov      cl, byte ptr [eax + 0xfa]
00473573 84c9                     test     cl, cl
00473575 7424                     je       0x47359b
00473577 8b8e10010000             mov      ecx, dword ptr [esi + 0x110]
0047357D 85c9                     test     ecx, ecx
0047357F 740f                     je       0x473590
00473581 8b8614010000             mov      eax, dword ptr [esi + 0x114]
00473587 2bc1                     sub      eax, ecx
00473589 c1f802                   sar      eax, 2
0047358C 85c0                     test     eax, eax
0047358E 750b                     jne      0x47359b
00473590 8b16                     mov      edx, dword ptr [esi]
00473592 6a00                     push     0
00473594 8bce                     mov      ecx, esi
00473596 ff5258                   call     dword ptr [edx + 0x58]
00473599 eb34                     jmp      0x4735cf
0047359B 8b86e8000000             mov      eax, dword ptr [esi + 0xe8]
004735A1 85c0                     test     eax, eax
004735A3 752a                     jne      0x4735cf
004735A5 38962a010000             cmp      byte ptr [esi + 0x12a], dl
004735AB 7522                     jne      0x4735cf
004735AD 8b8e10010000             mov      ecx, dword ptr [esi + 0x110]
004735B3 85c9                     test     ecx, ecx
004735B5 740f                     je       0x4735c6
004735B7 8b8614010000             mov      eax, dword ptr [esi + 0x114]
004735BD 2bc1                     sub      eax, ecx
004735BF c1f802                   sar      eax, 2
004735C2 85c0                     test     eax, eax
004735C4 7509                     jne      0x4735cf
004735C6 8b06                     mov      eax, dword ptr [esi]
004735C8 6a00                     push     0
004735CA 8bce                     mov      ecx, esi
004735CC ff5058                   call     dword ptr [eax + 0x58]
004735CF 8b16                     mov      edx, dword ptr [esi]
004735D1 8bce                     mov      ecx, esi
004735D3 ff9254010000             call     dword ptr [edx + 0x154]
