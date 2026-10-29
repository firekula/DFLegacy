00478F00 55 push ebp
00478F01 8bec mov ebp, esp
00478F03 56 push esi
00478F04 8bf1 mov esi, ecx
00478F06 8b86c8030000 mov eax, dword ptr [esi + 0x3c8]
00478F0C 85c0 test eax, eax
00478F0E 740a je 0x478f1a
00478F10 8a88a0010000 mov cl, byte ptr [eax + 0x1a0]
00478F16 84c9 test cl, cl
00478F18 754f jne 0x478f69
00478F1A 0fb64510 movzx eax, byte ptr [ebp + 0x10]
00478F1E 48 dec eax
00478F1F 99 cdq 
00478F20 b903000000 mov ecx, 3
00478F25 f7f9 idiv ecx
00478F27 85d2 test edx, edx
00478F29 740b je 0x478f36
00478F2B 4a dec edx
00478F2C f7da neg edx
00478F2E 1bd2 sbb edx, edx
00478F30 83e2fd and edx, 0xfffffffd
00478F33 83c204 add edx, 4
00478F36 85c0 test eax, eax
00478F38 7507 jne 0x478f41
00478F3A b803000000 mov eax, 3
00478F3F eb0e jmp 0x478f4f
00478F41 33c9 xor ecx, ecx
00478F43 83f801 cmp eax, 1
00478F46 0f94c1 sete cl
00478F49 8d4c0902 lea ecx, [ecx + ecx + 2]
00478F4D 8bc1 mov eax, ecx
00478F4F 8b4d14 mov ecx, dword ptr [ebp + 0x14]
00478F52 57 push edi
00478F53 8b3e mov edi, dword ptr [esi]
00478F55 51 push ecx
00478F56 50 push eax
00478F57 8b4508 mov eax, dword ptr [ebp + 8]
00478F5A 52 push edx
00478F5B 8b550c mov edx, dword ptr [ebp + 0xc]
00478F5E 52 push edx
00478F5F 50 push eax
00478F60 8bce mov ecx, esi
00478F62 ff9774010000 call dword ptr [edi + 0x174]
00478F68 5f pop edi
00478F69 5e pop esi
00478F6A 5d pop ebp
00478F6B c21000 ret 0x10
00478F6E cc int3 
00478F6F cc int3 
00478F70 55 push ebp
00478F71 8bec mov ebp, esp
00478F73 8b81c8030000 mov eax, dword ptr [ecx + 0x3c8]
00478F79 85c0 test eax, eax
00478F7B 740a je 0x478f87
00478F7D 8a90a0010000 mov dl, byte ptr [eax + 0x1a0]
00478F83 84d2 test dl, dl
00478F85 751a jne 0x478fa1
00478F87 8b4508 mov eax, dword ptr [ebp + 8]
00478F8A 8b550c mov edx, dword ptr [ebp + 0xc]
00478F8D 89813c020000 mov dword ptr [ecx + 0x23c], eax
00478F93 8b01 mov eax, dword ptr [ecx]
00478F95 899140020000 mov dword ptr [ecx + 0x240], edx
00478F9B ff90a4010000 call dword ptr [eax + 0x1a4]
00478FA1 5d pop ebp
00478FA2 c20800 ret 8