00433940 55                       push     ebp
00433941 8bec                     mov      ebp, esp
00433943 d94508                   fld      dword ptr [ebp + 8]
00433946 d81d842ca900             fcomp    dword ptr [0xa92c84]
0043394C dfe0                     fnstsw   ax
0043394E f6c401                   test     ah, 1
00433951 750f                     jne      0x433962
00433953 d9054cdfb000             fld      dword ptr [0xb0df4c]
00433959 d84508                   fadd     dword ptr [ebp + 8]
0043395C 5d                       pop      ebp
0043395D e9a67d4e00               jmp      0x91b708
00433962 d94508                   fld      dword ptr [ebp + 8]
00433965 d8254cdfb000             fsub     dword ptr [0xb0df4c]
0043396B 5d                       pop      ebp
0043396C e9977d4e00               jmp      0x91b708
