004C8C00 push     ebp
004C8C01 mov      ebp, esp
004C8C03 sub      esp, 0x20
004C8C06 push     esi
004C8C07 mov      esi, ecx
004C8C09 mov      ecx, dword ptr [esi]
004C8C0B test     ecx, ecx
004C8C0D push     edi
004C8C0E jne      0x4c8c13
004C8C10 lea      ecx, [esi + 8]
004C8C13 mov      edi, dword ptr [ebp + 8]
004C8C16 mov      eax, dword ptr [ecx]
004C8C18 push     edi
004C8C19 call     dword ptr [eax + 8]
004C8C1C fld      dword ptr [0xa92c84]
004C8C22 fucompp  
004C8C24 fnstsw   ax
004C8C26 test     ah, 0x44
004C8C29 jp       0x4c8c46
004C8C2B mov      eax, dword ptr [esi]
004C8C2D test     eax, eax
004C8C2F jne      0x4c8c34
004C8C31 lea      eax, [esi + 8]
004C8C34 mov      ecx, dword ptr [eax + edi*4 + 0x1c]
004C8C38 test     ecx, ecx
004C8C3A jne      0x4c8c46
004C8C3C pop      edi
004C8C3D xor      eax, eax
004C8C3F pop      esi
004C8C40 mov      esp, ebp
004C8C42 pop      ebp
004C8C43 ret      8
004C8C46 mov      ecx, dword ptr [esi]
004C8C48 test     ecx, ecx
004C8C4A jne      0x4c8c4f
004C8C4C lea      ecx, [esi + 8]
004C8C4F mov      edx, dword ptr [ecx]
004C8C51 push     edi
004C8C52 call     dword ptr [edx + 8]
004C8C55 fstp     dword ptr [ebp + 8]
004C8C58 mov      eax, dword ptr [esi]
004C8C5A test     eax, eax
004C8C5C mov      ecx, eax
004C8C5E jne      0x4c8c63
004C8C60 lea      ecx, [esi + 8]
004C8C63 test     eax, eax
004C8C65 mov      edx, dword ptr [ecx + edi*4 + 0x1c]
004C8C69 mov      dword ptr [ebp - 0xc], edx
004C8C6C mov      ecx, eax
004C8C6E jne      0x4c8c73
004C8C70 lea      ecx, [esi + 8]
004C8C73 test     edx, edx
004C8C75 mov      ecx, dword ptr [ecx + edi*4 + 0x28]
004C8C79 push     ebx
004C8C7A mov      dword ptr [ebp - 4], ecx
004C8C7D jne      0x4c8c87
004C8C7F fld      dword ptr [ebp + 8]
004C8C82 jmp      0x4c8dc9
004C8C87 cmp      ecx, -1
004C8C8A je       0x4c8d3b
004C8C90 cmp      ecx, dword ptr [ebp + 0xc]
004C8C93 jge      0x4c8d3b
004C8C99 test     eax, eax
004C8C9B mov      ecx, eax
004C8C9D jne      0x4c8ca5
004C8C9F lea      ecx, [esi + 8]
004C8CA2 lea      eax, [esi + 8]
004C8CA5 mov      edx, dword ptr [ecx]
004C8CA7 mov      dword ptr [ebp - 8], eax
004C8CAA mov      eax, dword ptr [eax]
004C8CAC push     edi
004C8CAD mov      dword ptr [ebp - 0x10], eax
004C8CB0 call     dword ptr [edx + 8]
004C8CB3 mov      ebx, dword ptr [ebp - 4]
004C8CB6 mov      eax, ebx
004C8CB8 imul     eax, dword ptr [ebp - 0xc]
004C8CBC mov      dword ptr [ebp - 0xc], eax
004C8CBF mov      eax, dword ptr [ebp - 0x10]
004C8CC2 push     ecx
004C8CC3 fild     dword ptr [ebp - 0xc]
004C8CC6 mov      ecx, dword ptr [ebp - 8]
004C8CC9 fmul     dword ptr [0x99c1c4]
004C8CCF faddp    st(1)
004C8CD1 fstp     dword ptr [esp]
004C8CD4 push     edi
004C8CD5 call     dword ptr [eax + 0xc]
004C8CD8 mov      eax, dword ptr [ebp + 0xc]
004C8CDB sub      eax, ebx
004C8CDD mov      dword ptr [ebp - 0x10], eax
004C8CE0 mov      eax, dword ptr [esi]
004C8CE2 xor      ebx, ebx
004C8CE4 cmp      eax, ebx
004C8CE6 jne      0x4c8ceb
004C8CE8 lea      eax, [esi + 8]
004C8CEB mov      edx, dword ptr [eax]
004C8CED push     edi
004C8CEE mov      ecx, eax
004C8CF0 call     dword ptr [edx + 8]
004C8CF3 fadd     dword ptr [ebp + 8]
004C8CF6 mov      ecx, dword ptr [esi]
004C8CF8 cmp      ecx, ebx
004C8CFA fmul     dword ptr [0x99f088]
004C8D00 fstp     dword ptr [ebp + 8]
004C8D03 jne      0x4c8d08
004C8D05 lea      ecx, [esi + 8]
004C8D08 mov      eax, dword ptr [ecx]
004C8D0A push     edi
004C8D0B call     dword ptr [eax + 8]
004C8D0E fimul    dword ptr [ebp - 0x10]
004C8D11 mov      eax, dword ptr [esi]
004C8D13 cmp      eax, ebx
004C8D15 fild     dword ptr [ebp - 4]
004C8D18 fmul     dword ptr [ebp + 8]
004C8D1B faddp    st(1)
004C8D1D fidiv    dword ptr [ebp + 0xc]
004C8D20 jne      0x4c8d25
004C8D22 lea      eax, [esi + 8]
004C8D25 mov      dword ptr [eax + edi*4 + 0x28], ebx
004C8D29 mov      eax, dword ptr [esi]
004C8D2B cmp      eax, ebx
004C8D2D jne      0x4c8d32
004C8D2F lea      eax, [esi + 8]
004C8D32 mov      dword ptr [eax + edi*4 + 0x1c], ebx
004C8D36 jmp      0x4c8dc9
004C8D3B test     eax, eax
004C8D3D mov      ecx, eax
004C8D3F jne      0x4c8d47
004C8D41 lea      ecx, [esi + 8]
004C8D44 lea      eax, [esi + 8]
004C8D47 mov      edx, dword ptr [ecx]
004C8D49 mov      dword ptr [ebp - 8], eax
004C8D4C mov      eax, dword ptr [eax]
004C8D4E push     edi
004C8D4F mov      dword ptr [ebp - 0x14], eax
004C8D52 call     dword ptr [edx + 8]
004C8D55 mov      eax, dword ptr [ebp - 0xc]
004C8D58 mov      ebx, dword ptr [ebp + 0xc]
004C8D5B imul     eax, ebx
004C8D5E mov      dword ptr [ebp - 0x10], eax
004C8D61 mov      eax, dword ptr [ebp - 0x14]
004C8D64 push     ecx
004C8D65 fild     dword ptr [ebp - 0x10]
004C8D68 mov      ecx, dword ptr [ebp - 8]
004C8D6B fmul     dword ptr [0x99c1c4]
004C8D71 faddp    st(1)
004C8D73 fstp     dword ptr [esp]
004C8D76 push     edi
004C8D77 call     dword ptr [eax + 0xc]
004C8D7A mov      eax, dword ptr [esi]
004C8D7C test     eax, eax
004C8D7E jne      0x4c8d83
004C8D80 lea      eax, [esi + 8]
004C8D83 mov      edx, dword ptr [eax]
004C8D85 push     edi
004C8D86 mov      ecx, eax
004C8D88 call     dword ptr [edx + 8]
004C8D8B fadd     dword ptr [ebp + 8]
004C8D8E cmp      dword ptr [ebp - 4], -1
004C8D92 fmul     dword ptr [0x99f088]
004C8D98 je       0x4c8dc9
004C8D9A mov      eax, dword ptr [esi]
004C8D9C test     eax, eax
004C8D9E jne      0x4c8da3
004C8DA0 lea      eax, [esi + 8]
004C8DA3 sub      dword ptr [eax + edi*4 + 0x28], ebx
004C8DA7 mov      eax, dword ptr [esi]
004C8DA9 test     eax, eax
004C8DAB mov      ecx, eax
004C8DAD jne      0x4c8db2
004C8DAF lea      ecx, [esi + 8]
004C8DB2 mov      edx, dword ptr [ecx + edi*4 + 0x28]
004C8DB6 test     edx, edx
004C8DB8 jne      0x4c8dc9
004C8DBA test     eax, eax
004C8DBC jne      0x4c8dc1
004C8DBE lea      eax, [esi + 8]
004C8DC1 mov      dword ptr [eax + edi*4 + 0x1c], 0
004C8DC9 fild     dword ptr [ebp + 0xc]
004C8DCC fmul     st(1)
004C8DCE fmul     dword ptr [0x99c1c4]
004C8DD4 fstp     dword ptr [ebp + 0xc]
004C8DD7 fstp     st(0)
004C8DD9 fld      dword ptr [ebp + 0xc]
004C8DDC fcomp    dword ptr [0x99fde8]
004C8DE2 fnstsw   ax
004C8DE4 test     ah, 0x41
004C8DE7 jne      0x4c8df2
004C8DE9 mov      dword ptr [ebp + 0xc], 0x44fa0000
004C8DF0 jmp      0x4c8e09
004C8DF2 fld      dword ptr [ebp + 0xc]
004C8DF5 fcomp    dword ptr [0x99fde4]
004C8DFB fnstsw   ax
004C8DFD test     ah, 5
004C8E00 jp       0x4c8e09
004C8E02 mov      dword ptr [ebp + 0xc], 0xc4fa0000
004C8E09 mov      eax, dword ptr [esi + 0x78]
004C8E0C xor      ebx, ebx
004C8E0E cmp      eax, ebx
004C8E10 je       0x4c8e1d
004C8E12 mov      al, byte ptr [esi + 0x7c]
004C8E15 test     al, al
004C8E17 jne      0x4c8ecc
004C8E1D cmp      dword ptr [esi + 0x74], ebx
004C8E20 je       0x4c8ecc
004C8E26 mov      dword ptr [ebp - 0x20], ebx
004C8E29 mov      dword ptr [ebp - 0x1c], ebx
004C8E2C mov      dword ptr [ebp - 0x18], ebx
004C8E2F nop      
004C8E30 mov      ecx, dword ptr [esi]
004C8E32 test     ecx, ecx
004C8E34 jne      0x4c8e39
004C8E36 lea      ecx, [esi + 8]
004C8E39 mov      eax, dword ptr [ecx]
004C8E3B push     ebx
004C8E3C call     dword ptr [eax]
004C8E3E push     ecx
004C8E3F fstp     dword ptr [esp]
004C8E42 call     0x433940
004C8E47 add      esp, 4
004C8E4A mov      dword ptr [ebp + ebx*4 - 0x20], eax
004C8E4E inc      ebx
004C8E4F cmp      ebx, 3
004C8E52 jl       0x4c8e30
004C8E54 mov      ecx, dword ptr [esi]
004C8E56 test     ecx, ecx
004C8E58 jne      0x4c8e5d
004C8E5A lea      ecx, [esi + 8]
004C8E5D mov      edx, dword ptr [ecx]
004C8E5F push     edi
004C8E60 call     dword ptr [edx]
004C8E62 fadd     dword ptr [ebp + 0xc]
004C8E65 push     ecx
004C8E66 fstp     dword ptr [esp]
004C8E69 call     0x433940
004C8E6E mov      ebx, eax
004C8E70 mov      eax, dword ptr [ebp + edi*4 - 0x20]
004C8E74 add      esp, 4
004C8E77 cmp      eax, ebx
004C8E79 je       0x4c8ecc
004C8E7B jmp      0x4c8e80
004C8E7D lea      ecx, [ecx]
004C8E80 mov      eax, dword ptr [ebp + edi*4 - 0x20]
004C8E84 cmp      eax, ebx
004C8E86 mov      dword ptr [ebp - 0x14], eax
004C8E89 jge      0x4c8e8e
004C8E8B inc      eax
004C8E8C jmp      0x4c8e8f
004C8E8E dec      eax
004C8E8F cmp      edi, 2
004C8E92 mov      ecx, dword ptr [esi + 0x74]
004C8E95 mov      dword ptr [ebp + edi*4 - 0x20], eax
004C8E99 mov      dword ptr [ebp + 8], 0
004C8EA0 mov      eax, dword ptr [ecx]
004C8EA2 jne      0x4c8eb0
004C8EA4 mov      edx, dword ptr [ebp - 0x18]
004C8EA7 push     edx
004C8EA8 call     dword ptr [eax + 0x90]
004C8EAE jmp      0x4c8ec2
004C8EB0 lea      edx, [ebp + 8]
004C8EB3 push     edx
004C8EB4 mov      edx, dword ptr [ebp - 0x1c]
004C8EB7 push     edx
004C8EB8 mov      edx, dword ptr [ebp - 0x20]
004C8EBB push     edx
004C8EBC call     dword ptr [eax + 0x94]
004C8EC2 test     al, al
004C8EC4 je       0x4c8f32
004C8EC6 cmp      dword ptr [ebp + edi*4 - 0x20], ebx
004C8ECA jne      0x4c8e80
004C8ECC mov      eax, dword ptr [esi]
004C8ECE test     eax, eax
004C8ED0 mov      ecx, eax
004C8ED2 jne      0x4c8eda
004C8ED4 lea      ecx, [esi + 8]
004C8ED7 lea      eax, [esi + 8]
004C8EDA mov      edx, dword ptr [ecx]
004C8EDC mov      ebx, dword ptr [eax]
004C8EDE push     edi
004C8EDF mov      dword ptr [ebp + 8], eax
004C8EE2 call     dword ptr [edx]
004C8EE4 fadd     dword ptr [ebp + 0xc]
004C8EE7 push     ecx
004C8EE8 mov      ecx, dword ptr [ebp + 8]
004C8EEB fstp     dword ptr [esp]
004C8EEE push     edi
004C8EEF call     dword ptr [ebx + 4]
004C8EF2 mov      eax, dword ptr [esi]
004C8EF4 test     eax, eax
004C8EF6 mov      ecx, eax
004C8EF8 jne      0x4c8efd
004C8EFA lea      ecx, [esi + 8]
004C8EFD mov      edx, dword ptr [ecx + edi*4 + 0x1c]
004C8F01 test     edx, edx
004C8F03 jne      0x4c8f98
004C8F09 test     eax, eax
004C8F0B jne      0x4c8f10
004C8F0D lea      eax, [esi + 8]
004C8F10 mov      edx, dword ptr [eax]
004C8F12 push     edi
004C8F13 mov      ecx, eax
004C8F15 call     dword ptr [edx + 8]
004C8F18 fld      dword ptr [0xa92c84]
004C8F1E fucompp  
004C8F20 fnstsw   ax
004C8F22 test     ah, 0x44
004C8F25 jp       0x4c8f8a
004C8F27 pop      ebx
004C8F28 pop      edi
004C8F29 xor      eax, eax
004C8F2B pop      esi
004C8F2C mov      esp, ebp
004C8F2E pop      ebp
004C8F2F ret      8
004C8F32 mov      ecx, dword ptr [esi]
004C8F34 test     ecx, ecx
004C8F36 jne      0x4c8f3b
004C8F38 lea      ecx, [esi + 8]
004C8F3B fild     dword ptr [ebp - 0x14]
004C8F3E mov      eax, dword ptr [ecx]
004C8F40 push     ecx
004C8F41 fstp     dword ptr [esp]
004C8F44 push     edi
004C8F45 call     dword ptr [eax + 4]
004C8F48 mov      ecx, dword ptr [esi + 0x74]
004C8F4B mov      edx, dword ptr [ecx]
004C8F4D push     1
004C8F4F call     dword ptr [edx + 0x2c]
004C8F52 test     al, al
004C8F54 je       0x4c8f7c
004C8F56 mov      ecx, dword ptr [esi]
004C8F58 test     ecx, ecx
004C8F5A jne      0x4c8f5f
004C8F5C lea      ecx, [esi + 8]
004C8F5F mov      eax, dword ptr [esi + 0x74]
004C8F62 mov      edx, dword ptr [ebp + 8]
004C8F65 mov      ebx, dword ptr [eax]
004C8F67 mov      eax, dword ptr [ecx]
004C8F69 push     edx
004C8F6A push     edi
004C8F6B call     dword ptr [eax + 8]
004C8F6E push     ecx
004C8F6F mov      ecx, dword ptr [esi + 0x74]
004C8F72 fstp     dword ptr [esp]
004C8F75 push     edi
004C8F76 call     dword ptr [ebx + 0x228]
004C8F7C pop      ebx
004C8F7D pop      edi
004C8F7E mov      eax, 4
004C8F83 pop      esi
004C8F84 mov      esp, ebp
004C8F86 pop      ebp
004C8F87 ret      8
004C8F8A pop      ebx
004C8F8B pop      edi
004C8F8C mov      eax, 2
004C8F91 pop      esi
004C8F92 mov      esp, ebp
004C8F94 pop      ebp
004C8F95 ret      8
004C8F98 test     eax, eax
004C8F9A mov      ecx, eax
004C8F9C jne      0x4c8fce
004C8F9E lea      ecx, [esi + 8]
004C8FA1 add      esi, 8
004C8FA4 mov      eax, dword ptr [ecx]
004C8FA6 push     edi
004C8FA7 call     dword ptr [eax + 8]
004C8FAA fimul    dword ptr [esi + edi*4 + 0x1c]
004C8FAE fcomp    dword ptr [0xa92c84]
004C8FB4 fnstsw   ax
004C8FB6 test     ah, 5
004C8FB9 mov      eax, 1
004C8FBE jnp      0x4c8fc5
004C8FC0 mov      eax, 3
004C8FC5 pop      ebx
004C8FC6 pop      edi
004C8FC7 pop      esi
004C8FC8 mov      esp, ebp
004C8FCA pop      ebp
004C8FCB ret      8
004C8FCE mov      esi, eax
004C8FD0 jmp      0x4c8fa4
004C8FD2 int3     
004C8FD3 int3     
004C8FD4 int3     
004C8FD5 int3     
004C8FD6 int3     
004C8FD7 int3     
004C8FD8 int3     
004C8FD9 int3     
004C8FDA int3     
004C8FDB int3     
004C8FDC int3     
004C8FDD int3     
004C8FDE int3     
004C8FDF int3     
004C9F00 push     ebp
004C9F01 mov      ebp, esp
004C9F03 sub      esp, 0x14
004C9F06 push     esi
004C9F07 mov      esi, ecx
004C9F09 mov      al, byte ptr [esi + 0xe0]
004C9F0F test     al, al
004C9F11 jne      0x4ca2e9
004C9F17 push     ebx
004C9F18 push     edi
004C9F19 lea      edi, [esi + 0xa0]
004C9F1F mov      ecx, edi
004C9F21 call     0x8f3f60
004C9F26 sub      eax, dword ptr [esi + 0xe4]
004C9F2C mov      ecx, edi
004C9F2E mov      dword ptr [ebp - 8], eax
004C9F31 call     0x8f3f60
004C9F36 mov      ecx, dword ptr [esi + 0x90]
004C9F3C test     ecx, ecx
004C9F3E mov      dword ptr [esi + 0xe4], eax
004C9F44 je       0x4c9f9a
004C9F46 mov      eax, dword ptr [ecx]
004C9F48 call     dword ptr [eax + 0x84]
004C9F4E add      eax, dword ptr [esi + 0x94]
004C9F54 push     eax
004C9F55 push     0
004C9F57 mov      ecx, esi
004C9F59 call     0x4c9d50
004C9F5E mov      ecx, dword ptr [esi + 0x90]
004C9F64 mov      edx, dword ptr [ecx]
004C9F66 call     dword ptr [edx + 0x88]
004C9F6C add      eax, dword ptr [esi + 0x98]
004C9F72 push     eax
004C9F73 push     1
004C9F75 mov      ecx, esi
004C9F77 call     0x4c9d50
004C9F7C mov      ecx, dword ptr [esi + 0x90]
004C9F82 mov      eax, dword ptr [ecx]
004C9F84 call     dword ptr [eax + 0x8c]
004C9F8A add      eax, dword ptr [esi + 0x9c]
004C9F90 push     eax
004C9F91 push     2
004C9F93 mov      ecx, esi
004C9F95 call     0x4c9d50
004C9F9A xor      edi, edi
004C9F9C lea      ebx, [esi + 0xec]
004C9FA2 mov      ecx, dword ptr [ebp - 8]
004C9FA5 push     ecx
004C9FA6 push     edi
004C9FA7 mov      ecx, esi
004C9FA9 call     0x4c8c00
004C9FAE mov      dword ptr [ebx], eax
004C9FB0 inc      edi
004C9FB1 add      ebx, 4
004C9FB4 cmp      edi, 3
004C9FB7 jl       0x4c9fa2
004C9FB9 mov      eax, dword ptr [esi + 0x78]
004C9FBC test     eax, eax
004C9FBE je       0x4ca29e
004C9FC4 mov      al, byte ptr [esi + 0x7c]
004C9FC7 test     al, al
004C9FC9 je       0x4ca29e
004C9FCF cmp      dword ptr [esi + 0x80], 2
004C9FD6 jne      0x4c9ffd
004C9FD8 mov      al, byte ptr [esi + 0x84]
004C9FDE test     al, al
004C9FE0 je       0x4c9ffd
004C9FE2 mov      eax, dword ptr [esi + 0x88]
004C9FE8 mov      ecx, dword ptr [esi + 0x74]
004C9FEB mov      edx, dword ptr [ecx]
004C9FED push     eax
004C9FEE push     0
004C9FF0 push     -1
004C9FF2 call     dword ptr [edx + 0x228]
004C9FF8 jmp      0x4ca29e
004C9FFD xor      edi, edi
004C9FFF nop      
