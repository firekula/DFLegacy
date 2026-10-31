004795F0 55                       push     ebp
004795F1 8bec                     mov      ebp, esp
004795F3 83ec08                   sub      esp, 8
004795F6 56                       push     esi
004795F7 8bf1                     mov      esi, ecx
004795F9 8b864c020000             mov      eax, dword ptr [esi + 0x24c]
004795FF 57                       push     edi
00479600 8bbe3c020000             mov      edi, dword ptr [esi + 0x23c]
00479606 3bc7                     cmp      eax, edi
00479608 0f8d9a000000             jge      0x4796a8
0047960E 8b06                     mov      eax, dword ptr [esi]
00479610 ff9084000000             call     dword ptr [eax + 0x84]
00479616 3bc7                     cmp      eax, edi
00479618 0f8e84000000             jle      0x4796a2
0047961E 83be3402000001           cmp      dword ptr [esi + 0x234], 1
00479625 757b                     jne      0x4796a2
00479627 8b16                     mov      edx, dword ptr [esi]
00479629 8bce                     mov      ecx, esi
0047962B ff928c000000             call     dword ptr [edx + 0x8c]
00479631 50                       push     eax
00479632 8b06                     mov      eax, dword ptr [esi]
00479634 8bce                     mov      ecx, esi
00479636 ff9088000000             call     dword ptr [eax + 0x88]
0047963C 8b8e3c020000             mov      ecx, dword ptr [esi + 0x23c]
00479642 50                       push     eax
00479643 51                       push     ecx
00479644 8bce                     mov      ecx, esi
00479646 e8657fffff               call     0x4715b0
0047964B c7863402000004000000     mov      dword ptr [esi + 0x234], 4
00479655 8bbe40020000             mov      edi, dword ptr [esi + 0x240]
0047965B 39be50020000             cmp      dword ptr [esi + 0x250], edi
00479661 53                       push     ebx
00479662 bb03000000               mov      ebx, 3
00479667 0f8dc7000000             jge      0x479734
0047966D 8b16                     mov      edx, dword ptr [esi]
0047966F 8bce                     mov      ecx, esi
00479671 ff9288000000             call     dword ptr [edx + 0x88]
00479677 3bc7                     cmp      eax, edi
00479679 0f8eaf000000             jle      0x47972e
0047967F 399e38020000             cmp      dword ptr [esi + 0x238], ebx
00479685 0f85a3000000             jne      0x47972e
0047968B 8b06                     mov      eax, dword ptr [esi]
0047968D 8bce                     mov      ecx, esi
0047968F ff908c000000             call     dword ptr [eax + 0x8c]
00479695 8b8e40020000             mov      ecx, dword ptr [esi + 0x240]
0047969B 50                       push     eax
0047969C 51                       push     ecx
0047969D e9c1000000               jmp      0x479763
004796A2 39be4c020000             cmp      dword ptr [esi + 0x24c], edi
004796A8 7e3e                     jle      0x4796e8
004796AA 8b16                     mov      edx, dword ptr [esi]
004796AC 8bce                     mov      ecx, esi
004796AE ff9284000000             call     dword ptr [edx + 0x84]
004796B4 3b863c020000             cmp      eax, dword ptr [esi + 0x23c]
004796BA 7d2c                     jge      0x4796e8
004796BC 8b8634020000             mov      eax, dword ptr [esi + 0x234]
004796C2 85c0                     test     eax, eax
004796C4 7522                     jne      0x4796e8
004796C6 8b06                     mov      eax, dword ptr [esi]
004796C8 8bce                     mov      ecx, esi
004796CA ff908c000000             call     dword ptr [eax + 0x8c]
004796D0 8b16                     mov      edx, dword ptr [esi]
004796D2 50                       push     eax
004796D3 8bce                     mov      ecx, esi
004796D5 ff9288000000             call     dword ptr [edx + 0x88]
004796DB 50                       push     eax
004796DC 8b863c020000             mov      eax, dword ptr [esi + 0x23c]
004796E2 50                       push     eax
004796E3 e95cffffff               jmp      0x479644
004796E8 8b16                     mov      edx, dword ptr [esi]
004796EA 8bce                     mov      ecx, esi
004796EC ff9284000000             call     dword ptr [edx + 0x84]
004796F2 3b863c020000             cmp      eax, dword ptr [esi + 0x23c]
004796F8 7d0f                     jge      0x479709
004796FA c7863402000001000000     mov      dword ptr [esi + 0x234], 1
00479704 e94cffffff               jmp      0x479655
00479709 8b06                     mov      eax, dword ptr [esi]
0047970B 8bce                     mov      ecx, esi
0047970D ff9084000000             call     dword ptr [eax + 0x84]
00479713 3b863c020000             cmp      eax, dword ptr [esi + 0x23c]
00479719 0f8e2cffffff             jle      0x47964b
0047971F c7863402000000000000     mov      dword ptr [esi + 0x234], 0
00479729 e927ffffff               jmp      0x479655
0047972E 39be50020000             cmp      dword ptr [esi + 0x250], edi
00479734 7e67                     jle      0x47979d
00479736 8b06                     mov      eax, dword ptr [esi]
00479738 8bce                     mov      ecx, esi
0047973A ff9088000000             call     dword ptr [eax + 0x88]
00479740 3b8640020000             cmp      eax, dword ptr [esi + 0x240]
00479746 7d55                     jge      0x47979d
00479748 83be3802000002           cmp      dword ptr [esi + 0x238], 2
0047974F 754c                     jne      0x47979d
00479751 8b16                     mov      edx, dword ptr [esi]
00479753 8bce                     mov      ecx, esi
00479755 ff928c000000             call     dword ptr [edx + 0x8c]
0047975B 50                       push     eax
0047975C 8b8640020000             mov      eax, dword ptr [esi + 0x240]
00479762 50                       push     eax
00479763 8b16                     mov      edx, dword ptr [esi]
00479765 8bce                     mov      ecx, esi
00479767 ff9284000000             call     dword ptr [edx + 0x84]
0047976D 50                       push     eax
0047976E 8bce                     mov      ecx, esi
00479770 e83b7effff               call     0x4715b0
00479775 c7863802000004000000     mov      dword ptr [esi + 0x238], 4
0047977F 83be3802000004           cmp      dword ptr [esi + 0x238], 4
00479786 8dbe44020000             lea      edi, [esi + 0x244]
0047978C 8bcf                     mov      ecx, edi
0047978E 7545                     jne      0x4797d5
00479790 e8cbeaf9ff               call     0x418260
00479795 d80df8d69900             fmul     dword ptr [0x99d6f8]
0047979B eb43                     jmp      0x4797e0
0047979D 8b06                     mov      eax, dword ptr [esi]
0047979F 8bce                     mov      ecx, esi
004797A1 ff9088000000             call     dword ptr [eax + 0x88]
004797A7 3b8640020000             cmp      eax, dword ptr [esi + 0x240]
004797AD 7d08                     jge      0x4797b7
004797AF 899e38020000             mov      dword ptr [esi + 0x238], ebx
004797B5 ebc8                     jmp      0x47977f
004797B7 8b16                     mov      edx, dword ptr [esi]
004797B9 8bce                     mov      ecx, esi
004797BB ff9288000000             call     dword ptr [edx + 0x88]
004797C1 3b8640020000             cmp      eax, dword ptr [esi + 0x240]
004797C7 7eac                     jle      0x479775
004797C9 c7863802000002000000     mov      dword ptr [esi + 0x238], 2
004797D3 ebaa                     jmp      0x47977f
004797D5 e886eaf9ff               call     0x418260
004797DA d80df4d69900             fmul     dword ptr [0x99d6f4]
004797E0 e8231f4a00               call     0x91b708
004797E5 8bd8                     mov      ebx, eax
004797E7 83be3402000004           cmp      dword ptr [esi + 0x234], 4
004797EE 895dfc                   mov      dword ptr [ebp - 4], ebx
004797F1 8bcf                     mov      ecx, edi
004797F3 750d                     jne      0x479802
004797F5 e866eaf9ff               call     0x418260
004797FA d80df0d69900             fmul     dword ptr [0x99d6f0]
00479800 eb0b                     jmp      0x47980d
00479802 e859eaf9ff               call     0x418260
00479807 d80decd69900             fmul     dword ptr [0x99d6ec]
0047980D e8f61e4a00               call     0x91b708
00479812 8b8e34020000             mov      ecx, dword ptr [esi + 0x234]
00479818 83f904                   cmp      ecx, 4
0047981B 8945f8                   mov      dword ptr [ebp - 8], eax
0047981E 7509                     jne      0x479829
00479820 c745fc00000000           mov      dword ptr [ebp - 4], 0
00479827 eb09                     jmp      0x479832
00479829 85c9                     test     ecx, ecx
0047982B 7505                     jne      0x479832
0047982D f7db                     neg      ebx
0047982F 895dfc                   mov      dword ptr [ebp - 4], ebx
00479832 8b8e38020000             mov      ecx, dword ptr [esi + 0x238]
00479838 83f904                   cmp      ecx, 4
0047983B 5b                       pop      ebx
0047983C 7509                     jne      0x479847
0047983E c745f800000000           mov      dword ptr [ebp - 8], 0
00479845 eb0a                     jmp      0x479851
00479847 83f902                   cmp      ecx, 2
0047984A 7505                     jne      0x479851
0047984C f7d8                     neg      eax
0047984E 8945f8                   mov      dword ptr [ebp - 8], eax
00479851 8b86e0000000             mov      eax, dword ptr [esi + 0xe0]
00479857 8b08                     mov      ecx, dword ptr [eax]
00479859 85c9                     test     ecx, ecx
0047985B 7503                     jne      0x479860
0047985D 8d4808                   lea      ecx, [eax + 8]
00479860 db45fc                   fild     dword ptr [ebp - 4]
00479863 8b01                     mov      eax, dword ptr [ecx]
00479865 51                       push     ecx
00479866 d91c24                   fstp     dword ptr [esp]
00479869 6a00                     push     0
0047986B ff500c                   call     dword ptr [eax + 0xc]
0047986E 8bb6e0000000             mov      esi, dword ptr [esi + 0xe0]
00479874 8b06                     mov      eax, dword ptr [esi]
00479876 85c0                     test     eax, eax
00479878 7503                     jne      0x47987d
0047987A 8d4608                   lea      eax, [esi + 8]
0047987D db45f8                   fild     dword ptr [ebp - 8]
00479880 8b10                     mov      edx, dword ptr [eax]
00479882 51                       push     ecx
00479883 8bc8                     mov      ecx, eax
00479885 d91c24                   fstp     dword ptr [esp]
00479888 6a01                     push     1
0047988A ff520c                   call     dword ptr [edx + 0xc]
0047988D 5f                       pop      edi
0047988E 5e                       pop      esi
0047988F 8be5                     mov      esp, ebp
00479891 5d                       pop      ebp
00479892 c3                       ret      
