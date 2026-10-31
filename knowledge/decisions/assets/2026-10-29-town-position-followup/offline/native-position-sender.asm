00479A40 55 push ebp
00479A41 8bec mov ebp, esp
00479A43 83ec0c sub esp, 0xc
00479A46 56 push esi
00479A47 8bf1 mov esi, ecx
00479A49 8b06 mov eax, dword ptr [esi]
00479A4B ff9084000000 call dword ptr [eax + 0x84]
00479A51 3b8654020000 cmp eax, dword ptr [esi + 0x254]
00479A57 7516 jne 0x479a6f
00479A59 8b16 mov edx, dword ptr [esi]
00479A5B 8bce mov ecx, esi
00479A5D ff9288000000 call dword ptr [edx + 0x88]
00479A63 3b8658020000 cmp eax, dword ptr [esi + 0x258]
00479A69 0f84f1000000 je 0x479b60
00479A6F 8b8638020000 mov eax, dword ptr [esi + 0x238]
00479A75 53 push ebx
00479A76 32db xor bl, bl
00479A78 83f804 cmp eax, 4
00479A7B 57 push edi
00479A7C 7504 jne 0x479a82
00479A7E b303 mov bl, 3
00479A80 eb07 jmp 0x479a89
00479A82 83f802 cmp eax, 2
00479A85 7502 jne 0x479a89
00479A87 b306 mov bl, 6
00479A89 8b8634020000 mov eax, dword ptr [esi + 0x234]
00479A8F 83f804 cmp eax, 4
00479A92 7504 jne 0x479a98
00479A94 fec3 inc bl
00479A96 eb08 jmp 0x479aa0
00479A98 83f801 cmp eax, 1
00479A9B 7503 jne 0x479aa0
00479A9D 80c302 add bl, 2
00479AA0 8b06 mov eax, dword ptr [esi]
00479AA2 8bce mov ecx, esi
00479AA4 ff9084000000 call dword ptr [eax + 0x84]
00479AAA 8b16 mov edx, dword ptr [esi]
00479AAC 8bce mov ecx, esi
00479AAE 8bf8 mov edi, eax
00479AB0 ff9288000000 call dword ptr [edx + 0x88]
00479AB6 8b0d9c0abf00 mov ecx, dword ptr [0xbf0a9c]
00479ABC 6a25 push 0x25
00479ABE 8945f4 mov dword ptr [ebp - 0xc], eax
00479AC1 e88a8b4300 call 0x8b2650
00479AC6 8b0d9c0abf00 mov ecx, dword ptr [0xbf0a9c]
00479ACC 6a02 push 2
00479ACE 8d45f8 lea eax, [ebp - 8]
00479AD1 50 push eax
00479AD2 897df8 mov dword ptr [ebp - 8], edi
00479AD5 e876894300 call 0x8b2450
00479ADA 8b4df4 mov ecx, dword ptr [ebp - 0xc]
00479ADD 6a02 push 2
00479ADF 8d55f4 lea edx, [ebp - 0xc]
00479AE2 894df4 mov dword ptr [ebp - 0xc], ecx
00479AE5 8b0d9c0abf00 mov ecx, dword ptr [0xbf0a9c]
00479AEB 52 push edx
00479AEC e85f894300 call 0x8b2450
00479AF1 8b0d9c0abf00 mov ecx, dword ptr [0xbf0a9c]
00479AF7 6a01 push 1
00479AF9 8d45fc lea eax, [ebp - 4]
00479AFC fec3 inc bl
00479AFE 50 push eax
00479AFF 885dfc mov byte ptr [ebp - 4], bl
00479B02 e849894300 call 0x8b2450
00479B07 8b3d9c0abf00 mov edi, dword ptr [0xbf0a9c]
00479B0D 8d8e44020000 lea ecx, [esi + 0x244]
00479B13 e848e7f9ff call 0x418260
00479B18 d80db4989900 fmul dword ptr [0x9998b4]
00479B1E e8e51b4a00 call 0x91b708
00479B23 6a02 push 2
00479B25 8d4df4 lea ecx, [ebp - 0xc]
00479B28 51 push ecx
00479B29 8bcf mov ecx, edi
00479B2B 8945f4 mov dword ptr [ebp - 0xc], eax
00479B2E e81d894300 call 0x8b2450
00479B33 8b0d9c0abf00 mov ecx, dword ptr [0xbf0a9c]
00479B39 e882894300 call 0x8b24c0
00479B3E 8b16 mov edx, dword ptr [esi]
00479B40 8bce mov ecx, esi
00479B42 ff9284000000 call dword ptr [edx + 0x84]
00479B48 898654020000 mov dword ptr [esi + 0x254], eax
00479B4E 8b06 mov eax, dword ptr [esi]
00479B50 8bce mov ecx, esi
00479B52 ff9088000000 call dword ptr [eax + 0x88]
00479B58 5f pop edi
00479B59 898658020000 mov dword ptr [esi + 0x258], eax
00479B5F 5b pop ebx
00479B60 5e pop esi
00479B61 8be5 mov esp, ebp
00479B63 5d pop ebp
00479B64 c3 ret 