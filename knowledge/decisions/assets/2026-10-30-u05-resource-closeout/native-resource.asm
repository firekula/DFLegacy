RANGE 004A40A0 100
004A40A0 55                   push    ebp
004A40A1 8bec                 mov     ebp, esp
004A40A3 6aff                 push    -1
004A40A5 68590b9600           push    0x960b59
004A40AA 64a100000000         mov     eax, dword ptr fs:[0]
004A40B0 50                   push    eax
004A40B1 64892500000000       mov     dword ptr fs:[0], esp
004A40B8 83ec0c               sub     esp, 0xc
004A40BB 53                   push    ebx
004A40BC 33db                 xor     ebx, ebx
004A40BE 56                   push    esi
004A40BF 8bf1                 mov     esi, ecx
004A40C1 895df0               mov     dword ptr [ebp - 0x10], ebx
004A40C4 8b8ecc010000         mov     ecx, dword ptr [esi + 0x1cc]
004A40CA 3bcb                 cmp     ecx, ebx
004A40CC 740f                 je      0x4a40dd
004A40CE 8b86d0010000         mov     eax, dword ptr [esi + 0x1d0]
004A40D4 2bc1                 sub     eax, ecx
004A40D6 c1f803               sar     eax, 3
004A40D9 3bc3                 cmp     eax, ebx
004A40DB 751c                 jne     0x4a40f9
004A40DD 8d450c               lea     eax, [ebp + 0xc]
004A40E0 50                   push    eax
004A40E1 8d4de8               lea     ecx, [ebp - 0x18]
004A40E4 895d0c               mov     dword ptr [ebp + 0xc], ebx
004A40E7 e80489f6ff           call    0x40c9f0
004A40EC 895dfc               mov     dword ptr [ebp - 4], ebx
004A40EF bb01000000           mov     ebx, 1
004A40F4 895df0               mov     dword ptr [ebp - 0x10], ebx
004A40F7 eb1d                 jmp     0x4a4116
004A40F9 8b450c               mov     eax, dword ptr [ebp + 0xc]
004A40FC 83f8ff               cmp     eax, -1
004A40FF 750b                 jne     0x4a410c
004A4101 8d8ee0020000         lea     ecx, [esi + 0x2e0]
004A4107 e824dff5ff           call    0x402030
004A410C 8bb6cc010000         mov     esi, dword ptr [esi + 0x1cc]
004A4112 48                   dec     eax
004A4113 8d04c6               lea     eax, [esi + eax*8]
004A4116 8bc8                 mov     ecx, eax
004A4118 e813dff5ff           call    0x402030
004A411D f6c301               test    bl, 1
004A4120 8bf0                 mov     esi, eax
004A4122 c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
004A4129 7408                 je      0x4a4133
004A412B 8d4de8               lea     ecx, [ebp - 0x18]
004A412E e8addef5ff           call    0x401fe0
004A4133 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
004A4136 8bc6                 mov     eax, esi
004A4138 5e                   pop     esi
004A4139 5b                   pop     ebx
004A413A 64890d00000000       mov     dword ptr fs:[0], ecx
004A4141 8be5                 mov     esp, ebp
004A4143 5d                   pop     ebp
004A4144 c20800               ret     8
004A4147 cc                   int3    
004A4148 cc                   int3    
004A4149 cc                   int3    
004A414A cc                   int3    
004A414B cc                   int3    
004A414C cc                   int3    
004A414D cc                   int3    
004A414E cc                   int3    
004A414F cc                   int3    
004A4150 55                   push    ebp
004A4151 8bec                 mov     ebp, esp
004A4153 51                   push    ecx
004A4154 56                   push    esi
004A4155 8bf1                 mov     esi, ecx
004A4157 8b5608               mov     edx, dword ptr [esi + 8]
004A415A 8b4e04               mov     ecx, dword ptr [esi + 4]
004A415D 57                   push    edi
004A415E 8b7e0c               mov     edi, dword ptr [esi + 0xc]
004A4161 8bc2                 mov     eax, edx
004A4163 2bc1                 sub     eax, ecx
004A4165 2bf9                 sub     edi, ecx
004A4167 c1f802               sar     eax, 2
004A416A c1ff02               sar     edi, 2
004A416D 3bc7                 cmp     eax, edi
004A416F 7315                 jae     0x4a4186
004A4171 8b4d08               mov     ecx, dword ptr [ebp + 8]
004A4174 8d4204               lea     eax, [edx + 4]
004A4177 894608               mov     dword ptr [esi + 8], eax
004A417A 8b01                 mov     eax, dword ptr [ecx]
004A417C 5f                   pop     edi
004A417D 8902                 mov     dword ptr [edx], eax
004A417F 5e                   pop     esi
004A4180 8be5                 mov     esp, ebp
004A4182 5d                   pop     ebp
004A4183 c20400               ret     4
004A4186 53                   push    ebx
004A4187 40                   inc     eax
004A4188 50                   push    eax
004A4189 8bce                 mov     ecx, esi
004A418B e8a0994000           call    0x8adb30
004A4190 8d1c8500000000       lea     ebx, [eax*4]
004A4197 53                   push    ebx
004A4198 895dfc               mov     dword ptr [ebp - 4], ebx
004A419B e89c6c4700           call    0x91ae3c
RANGE 004A4B10 90
004A4B10 55                   push    ebp
004A4B11 8bec                 mov     ebp, esp
004A4B13 6aff                 push    -1
004A4B15 68390e9600           push    0x960e39
004A4B1A 64a100000000         mov     eax, dword ptr fs:[0]
004A4B20 50                   push    eax
004A4B21 64892500000000       mov     dword ptr fs:[0], esp
004A4B28 83ec0c               sub     esp, 0xc
004A4B2B 53                   push    ebx
004A4B2C 56                   push    esi
004A4B2D 8bf1                 mov     esi, ecx
004A4B2F 8d4508               lea     eax, [ebp + 8]
004A4B32 50                   push    eax
004A4B33 8d4df0               lea     ecx, [ebp - 0x10]
004A4B36 51                   push    ecx
004A4B37 33db                 xor     ebx, ebx
004A4B39 8d8e4c1e0000         lea     ecx, [esi + 0x1e4c]
004A4B3F 895de8               mov     dword ptr [ebp - 0x18], ebx
004A4B42 e839110300           call    0x4d5c80
004A4B47 8bb6541e0000         mov     esi, dword ptr [esi + 0x1e54]
004A4B4D 8b45f0               mov     eax, dword ptr [ebp - 0x10]
004A4B50 3bc6                 cmp     eax, esi
004A4B52 751c                 jne     0x4a4b70
004A4B54 8d5508               lea     edx, [ebp + 8]
004A4B57 52                   push    edx
004A4B58 8d4dec               lea     ecx, [ebp - 0x14]
004A4B5B 895d08               mov     dword ptr [ebp + 8], ebx
004A4B5E e88d7ef6ff           call    0x40c9f0
004A4B63 895dfc               mov     dword ptr [ebp - 4], ebx
004A4B66 bb01000000           mov     ebx, 1
004A4B6B 895de8               mov     dword ptr [ebp - 0x18], ebx
004A4B6E eb03                 jmp     0x4a4b73
004A4B70 83c00c               add     eax, 0xc
004A4B73 8bc8                 mov     ecx, eax
004A4B75 e8b6d4f5ff           call    0x402030
004A4B7A f6c301               test    bl, 1
004A4B7D 8bf0                 mov     esi, eax
004A4B7F c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
004A4B86 7408                 je      0x4a4b90
004A4B88 8d4dec               lea     ecx, [ebp - 0x14]
004A4B8B e850d4f5ff           call    0x401fe0
004A4B90 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
004A4B93 8bc6                 mov     eax, esi
004A4B95 5e                   pop     esi
004A4B96 5b                   pop     ebx
004A4B97 64890d00000000       mov     dword ptr fs:[0], ecx
004A4B9E 8be5                 mov     esp, ebp
RANGE 0049C600 680
0049C600 55                   push    ebp
0049C601 8bec                 mov     ebp, esp
0049C603 6aff                 push    -1
0049C605 6864069600           push    0x960664
0049C60A 64a100000000         mov     eax, dword ptr fs:[0]
0049C610 50                   push    eax
0049C611 64892500000000       mov     dword ptr fs:[0], esp
0049C618 81ec30020000         sub     esp, 0x230
0049C61E 56                   push    esi
0049C61F 8bf1                 mov     esi, ecx
0049C621 c745e800000000       mov     dword ptr [ebp - 0x18], 0
0049C628 e8f36ffdff           call    0x473620
0049C62D 84c0                 test    al, al
0049C62F 0f84af050000         je      0x49cbe4
0049C635 83be5401000002       cmp     dword ptr [esi + 0x154], 2
0049C63C 0f84a2050000         je      0x49cbe4
0049C642 8b4d08               mov     ecx, dword ptr [ebp + 8]
0049C645 8b06                 mov     eax, dword ptr [esi]
0049C647 53                   push    ebx
0049C648 51                   push    ecx
0049C649 8bce                 mov     ecx, esi
0049C64B ff901c050000         call    dword ptr [eax + 0x51c]
0049C651 8bd8                 mov     ebx, eax
0049C653 85db                 test    ebx, ebx
0049C655 895dec               mov     dword ptr [ebp - 0x14], ebx
0049C658 7514                 jne     0x49c66e
0049C65A 5b                   pop     ebx
0049C65B 32c0                 xor     al, al
0049C65D 5e                   pop     esi
0049C65E 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
0049C661 64890d00000000       mov     dword ptr fs:[0], ecx
0049C668 8be5                 mov     esp, ebp
0049C66A 5d                   pop     ebp
0049C66B c21000               ret     0x10
0049C66E 8d8be0020000         lea     ecx, [ebx + 0x2e0]
0049C674 e8b759f6ff           call    0x402030
0049C679 85c0                 test    eax, eax
0049C67B 8945c4               mov     dword ptr [ebp - 0x3c], eax
0049C67E c645f301             mov     byte ptr [ebp - 0xd], 1
0049C682 74d6                 je      0x49c65a
0049C684 8b16                 mov     edx, dword ptr [esi]
0049C686 8bce                 mov     ecx, esi
0049C688 ff92dc050000         call    dword ptr [edx + 0x5dc]
0049C68E 84c0                 test    al, al
0049C690 7404                 je      0x49c696
0049C692 c6450c00             mov     byte ptr [ebp + 0xc], 0
0049C696 57                   push    edi
0049C697 6aff                 push    -1
0049C699 56                   push    esi
0049C69A 8bcb                 mov     ecx, ebx
0049C69C e8ff790000           call    0x4a40a0
0049C6A1 8bf8                 mov     edi, eax
0049C6A3 85ff                 test    edi, edi
0049C6A5 897ddc               mov     dword ptr [ebp - 0x24], edi
0049C6A8 7456                 je      0x49c700
0049C6AA 8b4508               mov     eax, dword ptr [ebp + 8]
0049C6AD 50                   push    eax
0049C6AE 8bce                 mov     ecx, esi
0049C6B0 e85b840000           call    0x4a4b10
0049C6B5 3bc7                 cmp     eax, edi
0049C6B7 7c47                 jl      0x49c700
0049C6B9 8a450c               mov     al, byte ptr [ebp + 0xc]
0049C6BC 84c0                 test    al, al
0049C6BE 743c                 je      0x49c6fc
0049C6C0 8b0de8dcb500         mov     ecx, dword ptr [0xb5dce8]
0049C6C6 8b7960               mov     edi, dword ptr [ecx + 0x60]
0049C6C9 6a00                 push    0
0049C6CB 6a00                 push    0
0049C6CD 6a10                 push    0x10
0049C6CF 68e6c89bff           push    0xff9bc8e6
0049C6D4 680d0e0000           push    0xe0d
0049C6D9 e8c2de4000           call    0x8aa5a0
0049C6DE 83c404               add     esp, 4
0049C6E1 50                   push    eax
0049C6E2 8bcf                 mov     ecx, edi
0049C6E4 e877943a00           call    0x845b60
0049C6E9 6a00                 push    0
0049C6EB 6a00                 push    0
0049C6ED 6aff                 push    -1
0049C6EF 68ccc59900           push    0x99c5cc
0049C6F4 e89771f9ff           call    0x433890
0049C6F9 83c410               add     esp, 0x10
0049C6FC c645f300             mov     byte ptr [ebp - 0xd], 0
0049C700 8bcb                 mov     ecx, ebx
0049C702 e8e9660500           call    0x4f2df0
0049C707 84c0                 test    al, al
0049C709 744e                 je      0x49c759
0049C70B 8a450c               mov     al, byte ptr [ebp + 0xc]
0049C70E 84c0                 test    al, al
0049C710 7443                 je      0x49c755
0049C712 8b15e8dcb500         mov     edx, dword ptr [0xb5dce8]
0049C718 8b7a60               mov     edi, dword ptr [edx + 0x60]
0049C71B 6a00                 push    0
0049C71D 6a00                 push    0
0049C71F 6a10                 push    0x10
0049C721 68e6c89bff           push    0xff9bc8e6
0049C726 6855030000           push    0x355
0049C72B e870de4000           call    0x8aa5a0
0049C730 83c404               add     esp, 4
0049C733 50                   push    eax
0049C734 8bcf                 mov     ecx, edi
0049C736 e825943a00           call    0x845b60
0049C73B 6a00                 push    0
0049C73D 6a00                 push    0
0049C73F 6aff                 push    -1
0049C741 68ccc59900           push    0x99c5cc
0049C746 e84571f9ff           call    0x433890
0049C74B 83c410               add     esp, 0x10
0049C74E 8bce                 mov     ecx, esi
0049C750 e89b48fcff           call    0x460ff0
0049C755 c645f300             mov     byte ptr [ebp - 0xd], 0
0049C759 8a4510               mov     al, byte ptr [ebp + 0x10]
0049C75C 84c0                 test    al, al
0049C75E 7474                 je      0x49c7d4
0049C760 8d8e70110000         lea     ecx, [esi + 0x1170]
0049C766 e8c558f6ff           call    0x402030
0049C76B 6aff                 push    -1
0049C76D 56                   push    esi
0049C76E 8bcb                 mov     ecx, ebx
0049C770 8bf8                 mov     edi, eax
0049C772 e8b9850500           call    0x4f4d30
0049C777 3bf8                 cmp     edi, eax
0049C779 7d59                 jge     0x49c7d4
0049C77B 8a450c               mov     al, byte ptr [ebp + 0xc]
0049C77E 84c0                 test    al, al
0049C780 744e                 je      0x49c7d0
0049C782 a1e8dcb500           mov     eax, dword ptr [0xb5dce8]
0049C787 8b7860               mov     edi, dword ptr [eax + 0x60]
0049C78A 6a00                 push    0
0049C78C 6a00                 push    0
0049C78E 6a10                 push    0x10
0049C790 68e6c89bff           push    0xff9bc8e6
0049C795 6856030000           push    0x356
0049C79A e801de4000           call    0x8aa5a0
0049C79F 83c404               add     esp, 4
0049C7A2 50                   push    eax
0049C7A3 8bcf                 mov     ecx, edi
0049C7A5 e8b6933a00           call    0x845b60
0049C7AA 83bebc20000010       cmp     dword ptr [esi + 0x20bc], 0x10
0049C7B1 7208                 jb      0x49c7bb
0049C7B3 8b86a8200000         mov     eax, dword ptr [esi + 0x20a8]
0049C7B9 eb06                 jmp     0x49c7c1
0049C7BB 8d86a8200000         lea     eax, [esi + 0x20a8]
0049C7C1 6a00                 push    0
0049C7C3 6a00                 push    0
0049C7C5 6aff                 push    -1
0049C7C7 50                   push    eax
0049C7C8 e8c370f9ff           call    0x433890
0049C7CD 83c410               add     esp, 0x10
0049C7D0 c645f300             mov     byte ptr [ebp - 0xd], 0
0049C7D4 3935145ab700         cmp     dword ptr [0xb75a14], esi
0049C7DA 0f8513020000         jne     0x49c9f3
0049C7E0 8a4514               mov     al, byte ptr [ebp + 0x14]
0049C7E3 84c0                 test    al, al
0049C7E5 0f8408020000         je      0x49c9f3
0049C7EB 33c0                 xor     eax, eax
0049C7ED 8945cc               mov     dword ptr [ebp - 0x34], eax
0049C7F0 8945d0               mov     dword ptr [ebp - 0x30], eax
0049C7F3 8945d4               mov     dword ptr [ebp - 0x2c], eax
0049C7F6 8bbbbc010000         mov     edi, dword ptr [ebx + 0x1bc]
0049C7FC 8945fc               mov     dword ptr [ebp - 4], eax
0049C7FF 3bbbc0010000         cmp     edi, dword ptr [ebx + 0x1c0]
0049C805 0f8498000000         je      0x49c8a3
0049C80B eb03                 jmp     0x49c810
0049C80D 8d4900               lea     ecx, [ecx]
0049C810 8bcf                 mov     ecx, edi
0049C812 e81958f6ff           call    0x402030
0049C817 8d8be0020000         lea     ecx, [ebx + 0x2e0]
0049C81D 894514               mov     dword ptr [ebp + 0x14], eax
0049C820 e80b58f6ff           call    0x402030
0049C825 8b4dec               mov     ecx, dword ptr [ebp - 0x14]
0049C828 8bd8                 mov     ebx, eax
0049C82A e8415f0500           call    0x4f2770
0049C82F 50                   push    eax
0049C830 53                   push    ebx
0049C831 8bcf                 mov     ecx, edi
0049C833 e8b8740000           call    0x4a3cf0
0049C838 8bd8                 mov     ebx, eax
0049C83A 85db                 test    ebx, ebx
0049C83C 744f                 je      0x49c88d
0049C83E 8b8e2c240000         mov     ecx, dword ptr [esi + 0x242c]
0049C844 85c9                 test    ecx, ecx
0049C846 7445                 je      0x49c88d
0049C848 8b5514               mov     edx, dword ptr [ebp + 0x14]
0049C84B 52                   push    edx
0049C84C e84f11fcff           call    0x45d9a0
0049C851 83f8ff               cmp     eax, -1
0049C854 8945d8               mov     dword ptr [ebp - 0x28], eax
0049C857 0f84cf000000         je      0x49c92c
0049C85D 8b8e2c240000         mov     ecx, dword ptr [esi + 0x242c]
0049C863 50                   push    eax
0049C864 e85780f6ff           call    0x4048c0
0049C869 8b10                 mov     edx, dword ptr [eax]
0049C86B 8bc8                 mov     ecx, eax
0049C86D ff5234               call    dword ptr [edx + 0x34]
0049C870 3bc3                 cmp     eax, ebx
0049C872 0f82b4000000         jb      0x49c92c
0049C878 8b45d8               mov     eax, dword ptr [ebp - 0x28]
0049C87B 8d4de0               lea     ecx, [ebp - 0x20]
0049C87E 51                   push    ecx
0049C87F 8d4dc8               lea     ecx, [ebp - 0x38]
0049C882 8945e0               mov     dword ptr [ebp - 0x20], eax
0049C885 895de4               mov     dword ptr [ebp - 0x1c], ebx
0049C888 e873a4f6ff           call    0x406d00
0049C88D 8b55ec               mov     edx, dword ptr [ebp - 0x14]
0049C890 8b82c0010000         mov     eax, dword ptr [edx + 0x1c0]
0049C896 83c718               add     edi, 0x18
0049C899 3bf8                 cmp     edi, eax
0049C89B 8bda                 mov     ebx, edx
0049C89D 0f856dffffff         jne     0x49c810
0049C8A3 8bbe2c240000         mov     edi, dword ptr [esi + 0x242c]
0049C8A9 85ff                 test    edi, edi
0049C8AB 0f8433010000         je      0x49c9e4
0049C8B1 807df301             cmp     byte ptr [ebp - 0xd], 1
0049C8B5 0f8529010000         jne     0x49c9e4
0049C8BB 8d4dc8               lea     ecx, [ebp - 0x38]
0049C8BE e8fd9c3600           call    0x8065c0
0049C8C3 84c0                 test    al, al
0049C8C5 0f8519010000         jne     0x49c9e4
0049C8CB 6a02                 push    2
0049C8CD 8d4dc8               lea     ecx, [ebp - 0x38]
0049C8D0 51                   push    ecx
0049C8D1 8bcf                 mov     ecx, edi
0049C8D3 e8e824fcff           call    0x45edc0
0049C8D8 84c0                 test    al, al
0049C8DA 0f8504010000         jne     0x49c9e4
0049C8E0 8a450c               mov     al, byte ptr [ebp + 0xc]
0049C8E3 84c0                 test    al, al
0049C8E5 0f84f5000000         je      0x49c9e0
0049C8EB 8b15e8dcb500         mov     edx, dword ptr [0xb5dce8]
0049C8F1 8b7a60               mov     edi, dword ptr [edx + 0x60]
0049C8F4 6a00                 push    0
0049C8F6 6a00                 push    0
0049C8F8 6a10                 push    0x10
0049C8FA 68e6c89bff           push    0xff9bc8e6
0049C8FF 680c0e0000           push    0xe0c
0049C904 e897dc4000           call    0x8aa5a0
0049C909 83c404               add     esp, 4
0049C90C 50                   push    eax
0049C90D 8bcf                 mov     ecx, edi
0049C90F e84c923a00           call    0x845b60
0049C914 83bebc20000010       cmp     dword ptr [esi + 0x20bc], 0x10
0049C91B 0f82aa000000         jb      0x49c9cb
0049C921 8b86a8200000         mov     eax, dword ptr [esi + 0x20a8]
0049C927 e9a5000000           jmp     0x49c9d1
0049C92C 8a450c               mov     al, byte ptr [ebp + 0xc]
0049C92F 84c0                 test    al, al
0049C931 747b                 je      0x49c9ae
0049C933 8b4514               mov     eax, dword ptr [ebp + 0x14]
0049C936 50                   push    eax
0049C937 e824f7fbff           call    0x45c060
0049C93C 83c404               add     esp, 4
0049C93F 50                   push    eax
0049C940 6857030000           push    0x357
0049C945 e856dc4000           call    0x8aa5a0
0049C94A 83c404               add     esp, 4
0049C94D 50                   push    eax
0049C94E 8d8dc4fdffff         lea     ecx, [ebp - 0x23c]
0049C954 6800020000           push    0x200
0049C959 51                   push    ecx
0049C95A e851744100           call    0x8b3db0
0049C95F a1e8dcb500           mov     eax, dword ptr [0xb5dce8]
0049C964 8b4860               mov     ecx, dword ptr [eax + 0x60]
0049C967 83c410               add     esp, 0x10
0049C96A 6a00                 push    0
0049C96C 6a00                 push    0
0049C96E 6a10                 push    0x10
0049C970 68e6c89bff           push    0xff9bc8e6
0049C975 8d95c4fdffff         lea     edx, [ebp - 0x23c]
0049C97B 52                   push    edx
0049C97C e8df913a00           call    0x845b60
0049C981 83bebc20000010       cmp     dword ptr [esi + 0x20bc], 0x10
0049C988 7208                 jb      0x49c992
0049C98A 8b86a8200000         mov     eax, dword ptr [esi + 0x20a8]
0049C990 eb06                 jmp     0x49c998
0049C992 8d86a8200000         lea     eax, [esi + 0x20a8]
0049C998 6a00                 push    0
0049C99A 6a00                 push    0
0049C99C 6aff                 push    -1
0049C99E 50                   push    eax
0049C99F e8ec6ef9ff           call    0x433890
0049C9A4 83c410               add     esp, 0x10
0049C9A7 8bce                 mov     ecx, esi
0049C9A9 e8d2b8ffff           call    0x498280
0049C9AE 8d4dc8               lea     ecx, [ebp - 0x38]
0049C9B1 e87af12a00           call    0x74bb30
0049C9B6 5f                   pop     edi
0049C9B7 5b                   pop     ebx
0049C9B8 32c0                 xor     al, al
0049C9BA 5e                   pop     esi
0049C9BB 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
0049C9BE 64890d00000000       mov     dword ptr fs:[0], ecx
0049C9C5 8be5                 mov     esp, ebp
0049C9C7 5d                   pop     ebp
0049C9C8 c21000               ret     0x10
0049C9CB 8d86a8200000         lea     eax, [esi + 0x20a8]
0049C9D1 6a00                 push    0
0049C9D3 6a00                 push    0
0049C9D5 6aff                 push    -1
0049C9D7 50                   push    eax
0049C9D8 e8b36ef9ff           call    0x433890
0049C9DD 83c410               add     esp, 0x10
0049C9E0 c645f300             mov     byte ptr [ebp - 0xd], 0
0049C9E4 8d4dc8               lea     ecx, [ebp - 0x38]
0049C9E7 c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
0049C9EE e83df12a00           call    0x74bb30
0049C9F3 8a45f3               mov     al, byte ptr [ebp - 0xd]
0049C9F6 84c0                 test    al, al
0049C9F8 751e                 jne     0x49ca18
0049C9FA 8a450c               mov     al, byte ptr [ebp + 0xc]
0049C9FD 84c0                 test    al, al
0049C9FF 750e                 jne     0x49ca0f
0049CA01 8b06                 mov     eax, dword ptr [esi]
0049CA03 8bce                 mov     ecx, esi
0049CA05 ff90dc050000         call    dword ptr [eax + 0x5dc]
0049CA0B 84c0                 test    al, al
0049CA0D 74a7                 je      0x49c9b6
0049CA0F 8bce                 mov     ecx, esi
0049CA11 e86ab8ffff           call    0x498280
0049CA16 eb9e                 jmp     0x49c9b6
0049CA18 8b45dc               mov     eax, dword ptr [ebp - 0x24]
0049CA1B 85c0                 test    eax, eax
0049CA1D 741a                 je      0x49ca39
0049CA1F 8d8bd8010000         lea     ecx, [ebx + 0x1d8]
0049CA25 e80656f6ff           call    0x402030
0049CA2A 85c0                 test    eax, eax
0049CA2C 740b                 je      0x49ca39
0049CA2E 8b4d08               mov     ecx, dword ptr [ebp + 8]
0049CA31 51                   push    ecx
0049CA32 8bce                 mov     ecx, esi
0049CA34 e837980000           call    0x4a6270
0049CA39 8a4510               mov     al, byte ptr [ebp + 0x10]
0049CA3C 84c0                 test    al, al
0049CA3E 7447                 je      0x49ca87
0049CA40 6aff                 push    -1
0049CA42 56                   push    esi
0049CA43 8bcb                 mov     ecx, ebx
0049CA45 e8e6820500           call    0x4f4d30
0049CA4A 89450c               mov     dword ptr [ebp + 0xc], eax
0049CA4D 8b06                 mov     eax, dword ptr [esi]
0049CA4F 6a01                 push    1
0049CA51 8d8e70110000         lea     ecx, [esi + 0x1170]
0049CA57 894514               mov     dword ptr [ebp + 0x14], eax
0049CA5A e8d155f6ff           call    0x402030
0049CA5F 8b7d0c               mov     edi, dword ptr [ebp + 0xc]
0049CA62 2bc7                 sub     eax, edi
0049CA64 50                   push    eax
0049CA65 8b4514               mov     eax, dword ptr [ebp + 0x14]
0049CA68 8bce                 mov     ecx, esi
0049CA6A ff903c030000         call    dword ptr [eax + 0x33c]
0049CA70 8b16                 mov     edx, dword ptr [esi]
0049CA72 8bce                 mov     ecx, esi
0049CA74 ff92dc050000         call    dword ptr [edx + 0x5dc]
0049CA7A 84c0                 test    al, al
0049CA7C 7509                 jne     0x49ca87
0049CA7E 57                   push    edi
0049CA7F e8bc58ffff           call    0x492340
0049CA84 83c404               add     esp, 4
0049CA87 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CA8D 8b01                 mov     eax, dword ptr [ecx]
0049CA8F 6a02                 push    2
0049CA91 6a19                 push    0x19
0049CA93 ff5008               call    dword ptr [eax + 8]
0049CA96 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CA9C 8b39                 mov     edi, dword ptr [ecx]
0049CA9E 8b16                 mov     edx, dword ptr [esi]
0049CAA0 8bce                 mov     ecx, esi
0049CAA2 ff5234               call    dword ptr [edx + 0x34]
0049CAA5 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CAAB 50                   push    eax
0049CAAC ff5724               call    dword ptr [edi + 0x24]
0049CAAF a1e0dcb500           mov     eax, dword ptr [0xb5dce0]
0049CAB4 8b16                 mov     edx, dword ptr [esi]
0049CAB6 8b38                 mov     edi, dword ptr [eax]
0049CAB8 8bce                 mov     ecx, esi
0049CABA ff5238               call    dword ptr [edx + 0x38]
0049CABD 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CAC3 50                   push    eax
0049CAC4 ff5724               call    dword ptr [edi + 0x24]
0049CAC7 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CACD 8b5508               mov     edx, dword ptr [ebp + 8]
0049CAD0 8b01                 mov     eax, dword ptr [ecx]
0049CAD2 52                   push    edx
0049CAD3 ff5020               call    dword ptr [eax + 0x20]
0049CAD6 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CADC 8b55c4               mov     edx, dword ptr [ebp - 0x3c]
0049CADF 8b01                 mov     eax, dword ptr [ecx]
0049CAE1 52                   push    edx
0049CAE2 ff5020               call    dword ptr [eax + 0x20]
0049CAE5 8a4510               mov     al, byte ptr [ebp + 0x10]
0049CAE8 84c0                 test    al, al
0049CAEA 7408                 je      0x49caf4
0049CAEC 8d8670110000         lea     eax, [esi + 0x1170]
0049CAF2 eb1e                 jmp     0x49cb12
0049CAF4 8d450c               lea     eax, [ebp + 0xc]
0049CAF7 50                   push    eax
0049CAF8 8d4de0               lea     ecx, [ebp - 0x20]
0049CAFB c7450c00000000       mov     dword ptr [ebp + 0xc], 0
0049CB02 e8e9fef6ff           call    0x40c9f0
0049CB07 b901000000           mov     ecx, 1
0049CB0C 894dfc               mov     dword ptr [ebp - 4], ecx
0049CB0F 894de8               mov     dword ptr [ebp - 0x18], ecx
0049CB12 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CB18 8b39                 mov     edi, dword ptr [ecx]
0049CB1A 8bc8                 mov     ecx, eax
0049CB1C e80f55f6ff           call    0x402030
0049CB21 8b0de0dcb500         mov     ecx, dword ptr [0xb5dce0]
0049CB27 50                   push    eax
0049CB28 ff572c               call    dword ptr [edi + 0x2c]
0049CB2B f645e801             test    byte ptr [ebp - 0x18], 1
0049CB2F c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
0049CB36 7408                 je      0x49cb40
0049CB38 8d4de0               lea     ecx, [ebp - 0x20]
0049CB3B e8a054f6ff           call    0x401fe0
0049CB40 8b0d6ccbbd00         mov     ecx, dword ptr [0xbdcb6c]
0049CB46 8b11                 mov     edx, dword ptr [ecx]
0049CB48 56                   push    esi
0049CB49 6aff                 push    -1
0049CB4B 6a01                 push    1
0049CB4D ff5240               call    dword ptr [edx + 0x40]
0049CB50 8b06                 mov     eax, dword ptr [esi]
0049CB52 8bce                 mov     ecx, esi
0049CB54 ff90dc050000         call    dword ptr [eax + 0x5dc]
0049CB5A 84c0                 test    al, al
0049CB5C 7571                 jne     0x49cbcf
0049CB5E 8d4b68               lea     ecx, [ebx + 0x68]
0049CB61 e8ca54f6ff           call    0x402030
0049CB66 83f801               cmp     eax, 1
0049CB69 7464                 je      0x49cbcf
0049CB6B 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
0049CB71 6a29                 push    0x29
0049CB73 e8d85a4100           call    0x8b2650
0049CB78 8a4d08               mov     cl, byte ptr [ebp + 8]
0049CB7B 6a01                 push    1
0049CB7D 8d550f               lea     edx, [ebp + 0xf]
0049CB80 884d0f               mov     byte ptr [ebp + 0xf], cl
0049CB83 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
0049CB89 52                   push    edx
0049CB8A e8c1584100           call    0x8b2450
0049CB8F a1145ab700           mov     eax, dword ptr [0xb75a14]
0049CB94 8b359c0abf00         mov     esi, dword ptr [0xbf0a9c]
0049CB9A 6aff                 push    -1
0049CB9C 50                   push    eax
0049CB9D 8bcb                 mov     ecx, ebx
0049CB9F e8ac7f0500           call    0x4f4b50
0049CBA4 6a02                 push    2
0049CBA6 8d4d10               lea     ecx, [ebp + 0x10]
0049CBA9 51                   push    ecx
0049CBAA 8bce                 mov     ecx, esi
0049CBAC 894510               mov     dword ptr [ebp + 0x10], eax
0049CBAF e89c584100           call    0x8b2450
0049CBB4 8b0d9c0abf00         mov     ecx, dword ptr [0xbf0a9c]
0049CBBA e801594100           call    0x8b24c0
0049CBBF 8b15145ab700         mov     edx, dword ptr [0xb75a14]
0049CBC5 6aff                 push    -1
0049CBC7 52                   push    edx
0049CBC8 8bcb                 mov     ecx, ebx
0049CBCA e8817f0500           call    0x4f4b50
0049CBCF 5f                   pop     edi
0049CBD0 5b                   pop     ebx
0049CBD1 b001                 mov     al, 1
0049CBD3 5e                   pop     esi
0049CBD4 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
0049CBD7 64890d00000000       mov     dword ptr fs:[0], ecx
0049CBDE 8be5                 mov     esp, ebp
0049CBE0 5d                   pop     ebp
0049CBE1 c21000               ret     0x10
0049CBE4 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
0049CBE7 32c0                 xor     al, al
0049CBE9 5e                   pop     esi
0049CBEA 64890d00000000       mov     dword ptr fs:[0], ecx
0049CBF1 8be5                 mov     esp, ebp
0049CBF3 5d                   pop     ebp
0049CBF4 c21000               ret     0x10
0049CBF7 cc                   int3    
0049CBF8 cc                   int3    
0049CBF9 cc                   int3    
0049CBFA cc                   int3    
0049CBFB cc                   int3    
0049CBFC cc                   int3    
0049CBFD cc                   int3    
0049CBFE cc                   int3    
0049CBFF cc                   int3    
0049CC00 55                   push    ebp
0049CC01 8bec                 mov     ebp, esp
0049CC03 83ec28               sub     esp, 0x28
0049CC06 53                   push    ebx
0049CC07 56                   push    esi
0049CC08 57                   push    edi
0049CC09 894df4               mov     dword ptr [ebp - 0xc], ecx
0049CC0C 8db1ec1a0000         lea     esi, [ecx + 0x1aec]
0049CC12 bfd8000000           mov     edi, 0xd8
0049CC17 8b06                 mov     eax, dword ptr [esi]
0049CC19 85c0                 test    eax, eax
0049CC1B 744e                 je      0x49cc6b
0049CC1D 8d88d8020000         lea     ecx, [eax + 0x2d8]
0049CC23 e80854f6ff           call    0x402030
0049CC28 8b0e                 mov     ecx, dword ptr [esi]
0049CC2A 8b91e4020000         mov     edx, dword ptr [ecx + 0x2e4]
0049CC30 8b1d58d4cf00         mov     ebx, dword ptr [0xcfd458]
0049CC36 8b5b44               mov     ebx, dword ptr [ebx + 0x44]
0049CC39 8d1493               lea     edx, [ebx + edx*4]
0049CC3C 81c1e0020000         add     ecx, 0x2e0
0049CC42 8bda                 mov     ebx, edx
0049CC44 331d5cd4cf00         xor     ebx, dword ptr [0xcfd45c]
0049CC4A 33d8                 xor     ebx, eax
0049CC4C 891a                 mov     dword ptr [edx], ebx
0049CC4E 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
0049CC54 8b5248               mov     edx, dword ptr [edx + 0x48]
0049CC57 8b4904               mov     ecx, dword ptr [ecx + 4]
0049CC5A 8b1d5cd4cf00         mov     ebx, dword ptr [0xcfd45c]
0049CC60 8d0c8a               lea     ecx, [edx + ecx*4]
0049CC63 8bd1                 mov     edx, ecx
0049CC65 33d3                 xor     edx, ebx
0049CC67 33d0                 xor     edx, eax
0049CC69 8911                 mov     dword ptr [ecx], edx
0049CC6B 83c604               add     esi, 4
0049CC6E 4f                   dec     edi
0049CC6F 75a6                 jne     0x49cc17
0049CC71 8b5df4               mov     ebx, dword ptr [ebp - 0xc]
0049CC74 8d8b741e0000         lea     ecx, [ebx + 0x1e74]
0049CC7A e861940000           call    0x4a60e0
0049CC7F 33                   .byte   0x33
RANGE 004F4F50 260
004F4F50 55                   push    ebp
004F4F51 8bec                 mov     ebp, esp
004F4F53 83ec0c               sub     esp, 0xc
004F4F56 56                   push    esi
004F4F57 57                   push    edi
004F4F58 8bf9                 mov     edi, ecx
004F4F5A 8d8fec020000         lea     ecx, [edi + 0x2ec]
004F4F60 897dfc               mov     dword ptr [ebp - 4], edi
004F4F63 e8c8d0f0ff           call    0x402030
004F4F68 85c0                 test    eax, eax
004F4F6A 0f8401010000         je      0x4f5071
004F4F70 8b7508               mov     esi, dword ptr [ebp + 8]
004F4F73 85f6                 test    esi, esi
004F4F75 0f84f6000000         je      0x4f5071
004F4F7B 53                   push    ebx
004F4F7C 6aff                 push    -1
004F4F7E 56                   push    esi
004F4F7F 8bcf                 mov     ecx, edi
004F4F81 e8aafdffff           call    0x4f4d30
004F4F86 8bd8                 mov     ebx, eax
004F4F88 8b06                 mov     eax, dword ptr [esi]
004F4F8A 8bce                 mov     ecx, esi
004F4F8C ff9040030000         call    dword ptr [eax + 0x340]
004F4F92 3bc3                 cmp     eax, ebx
004F4F94 7d0b                 jge     0x4f4fa1
004F4F96 5b                   pop     ebx
004F4F97 5f                   pop     edi
004F4F98 32c0                 xor     al, al
004F4F9A 5e                   pop     esi
004F4F9B 8be5                 mov     esp, ebp
004F4F9D 5d                   pop     ebp
004F4F9E c20400               ret     4
004F4FA1 3935145ab700         cmp     dword ptr [0xb75a14], esi
004F4FA7 0f85b9000000         jne     0x4f5066
004F4FAD 8b9fbc010000         mov     ebx, dword ptr [edi + 0x1bc]
004F4FB3 3b9fc0010000         cmp     ebx, dword ptr [edi + 0x1c0]
004F4FB9 0f84a7000000         je      0x4f5066
004F4FBF 8d7308               lea     esi, [ebx + 8]
004F4FC2 eb03                 jmp     0x4f4fc7
004F4FC4 8b7dfc               mov     edi, dword ptr [ebp - 4]
004F4FC7 8bcb                 mov     ecx, ebx
004F4FC9 e862d0f0ff           call    0x402030
004F4FCE 8d8fe0020000         lea     ecx, [edi + 0x2e0]
004F4FD4 8945f4               mov     dword ptr [ebp - 0xc], eax
004F4FD7 e854d0f0ff           call    0x402030
004F4FDC 8d4f20               lea     ecx, [edi + 0x20]
004F4FDF 894508               mov     dword ptr [ebp + 8], eax
004F4FE2 e849d0f0ff           call    0x402030
004F4FE7 8d4e08               lea     ecx, [esi + 8]
004F4FEA 8945f8               mov     dword ptr [ebp - 8], eax
004F4FED e83ed0f0ff           call    0x402030
004F4FF2 8bce                 mov     ecx, esi
004F4FF4 8bf8                 mov     edi, eax
004F4FF6 e835d0f0ff           call    0x402030
004F4FFB 8b4d08               mov     ecx, dword ptr [ebp + 8]
004F4FFE 8b55f8               mov     edx, dword ptr [ebp - 8]
004F5001 2bf8                 sub     edi, eax
004F5003 49                   dec     ecx
004F5004 0faff9               imul    edi, ecx
004F5007 8d4aff               lea     ecx, [edx - 1]
004F500A 8bc7                 mov     eax, edi
004F500C 99                   cdq     
004F500D f7f9                 idiv    ecx
004F500F 8bce                 mov     ecx, esi
004F5011 8bf8                 mov     edi, eax
004F5013 e818d0f0ff           call    0x402030
004F5018 03f8                 add     edi, eax
004F501A 7433                 je      0x4f504f
004F501C 8b55f4               mov     edx, dword ptr [ebp - 0xc]
004F501F 8b0decdcb500         mov     ecx, dword ptr [0xb5dcec]
004F5025 52                   push    edx
004F5026 e87589f6ff           call    0x45d9a0
004F502B 83f8ff               cmp     eax, -1
004F502E 0f8462ffffff         je      0x4f4f96
004F5034 8b0decdcb500         mov     ecx, dword ptr [0xb5dcec]
004F503A 50                   push    eax
004F503B e880f8f0ff           call    0x4048c0
004F5040 8b10                 mov     edx, dword ptr [eax]
004F5042 8bc8                 mov     ecx, eax
004F5044 ff5234               call    dword ptr [edx + 0x34]
004F5047 3bc7                 cmp     eax, edi
004F5049 0f8247ffffff         jb      0x4f4f96
004F504F 8b45fc               mov     eax, dword ptr [ebp - 4]
004F5052 8b88c0010000         mov     ecx, dword ptr [eax + 0x1c0]
004F5058 83c318               add     ebx, 0x18
004F505B 83c618               add     esi, 0x18
004F505E 3bd9                 cmp     ebx, ecx
004F5060 0f855effffff         jne     0x4f4fc4
004F5066 5b                   pop     ebx
004F5067 5f                   pop     edi
004F5068 b001                 mov     al, 1
004F506A 5e                   pop     esi
004F506B 8be5                 mov     esp, ebp
004F506D 5d                   pop     ebp
004F506E c20400               ret     4
004F5071 5f                   pop     edi
004F5072 32c0                 xor     al, al
004F5074 5e                   pop     esi
004F5075 8be5                 mov     esp, ebp
004F5077 5d                   pop     ebp
004F5078 c20400               ret     4
004F507B cc                   int3    
004F507C cc                   int3    
004F507D cc                   int3    
004F507E cc                   int3    
004F507F cc                   int3    
004F5080 53                   push    ebx
004F5081 56                   push    esi
004F5082 8bf1                 mov     esi, ecx
004F5084 57                   push    edi
004F5085 33ff                 xor     edi, edi
004F5087 8d4e08               lea     ecx, [esi + 8]
004F508A 897e04               mov     dword ptr [esi + 4], edi
004F508D e87e16fbff           call    0x4a6710
004F5092 89bee8020000         mov     dword ptr [esi + 0x2e8], edi
004F5098 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F509E 8b5244               mov     edx, dword ptr [edx + 0x44]
004F50A1 8b8edc020000         mov     ecx, dword ptr [esi + 0x2dc]
004F50A7 8d0c8a               lea     ecx, [edx + ecx*4]
004F50AA 8b1d5cd4cf00         mov     ebx, dword ptr [0xcfd45c]
004F50B0 8bd1                 mov     edx, ecx
004F50B2 33d3                 xor     edx, ebx
004F50B4 8911                 mov     dword ptr [ecx], edx
004F50B6 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F50BC 8b5248               mov     edx, dword ptr [edx + 0x48]
004F50BF 8b8edc020000         mov     ecx, dword ptr [esi + 0x2dc]
004F50C5 8d0c8a               lea     ecx, [edx + ecx*4]
004F50C8 8b3d5cd4cf00         mov     edi, dword ptr [0xcfd45c]
004F50CE 8d86d8020000         lea     eax, [esi + 0x2d8]
004F50D4 8bd1                 mov     edx, ecx
004F50D6 33d7                 xor     edx, edi
004F50D8 8911                 mov     dword ptr [ecx], edx
004F50DA 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F50E0 8b5244               mov     edx, dword ptr [edx + 0x44]
004F50E3 8b8ee4020000         mov     ecx, dword ptr [esi + 0x2e4]
004F50E9 8d0c8a               lea     ecx, [edx + ecx*4]
004F50EC 8b1d5cd4cf00         mov     ebx, dword ptr [0xcfd45c]
004F50F2 8bd1                 mov     edx, ecx
004F50F4 33d3                 xor     edx, ebx
004F50F6 8911                 mov     dword ptr [ecx], edx
004F50F8 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F50FE 8b5248               mov     edx, dword ptr [edx + 0x48]
004F5101 8b8ee4020000         mov     ecx, dword ptr [esi + 0x2e4]
004F5107 8b1d5cd4cf00         mov     ebx, dword ptr [0xcfd45c]
004F510D 8d0c8a               lea     ecx, [edx + ecx*4]
004F5110 8dbee0020000         lea     edi, [esi + 0x2e0]
004F5116 8bd1                 mov     edx, ecx
004F5118 33d3                 xor     edx, ebx
004F511A 8911                 mov     dword ptr [ecx], edx
004F511C 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F5122 8b5244               mov     edx, dword ptr [edx + 0x44]
004F5125 8b8ef0020000         mov     ecx, dword ptr [esi + 0x2f0]
004F512B 8d0c8a               lea     ecx, [edx + ecx*4]
004F512E 8bd1                 mov     edx, ecx
004F5130 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
004F5136 8d9eec020000         lea     ebx, [esi + 0x2ec]
004F513C 83f201               xor     edx, 1
004F513F 8911                 mov     dword ptr [ecx], edx
004F5141 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F5147 8b5248               mov     edx, dword ptr [edx + 0x48]
004F514A 8b4b04               mov     ecx, dword ptr [ebx + 4]
004F514D 8d0c8a               lea     ecx, [edx + ecx*4]
004F5150 8bd1                 mov     edx, ecx
004F5152 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
004F5158 83f201               xor     edx, 1
004F515B 8911                 mov     dword ptr [ecx], edx
004F515D 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F5163 8b5244               mov     edx, dword ptr [edx + 0x44]
004F5166 8b8ef8020000         mov     ecx, dword ptr [esi + 0x2f8]
004F516C 8d0c8a               lea     ecx, [edx + ecx*4]
004F516F 8bd1                 mov     edx, ecx
004F5171 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
004F5177 8911                 mov     dword ptr [ecx], edx
004F5179 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F517F 8b5248               mov     edx, dword ptr [edx + 0x48]
004F5182 8b8ef8020000         mov     ecx, dword ptr [esi + 0x2f8]
004F5188 8d0c8a               lea     ecx, [edx + ecx*4]
004F518B 8bd1                 mov     edx, ecx
004F518D 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
004F5193 8911                 mov     dword ptr [ecx], edx
004F5195 8b1558d4cf00         mov     edx, dword ptr [0xcfd458]
004F519B 8b5244               mov     edx, dword ptr [edx + 0x44]
004F519E 8b8e00030000         mov     ecx, dword ptr [esi + 0x300]
004F51A4 8d0c8a               lea     ecx, [edx + ecx*4]
004F51A7 8bd1                 mov     edx, ecx
004F51A9 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
004F51AF 89                   .byte   0x89
RANGE 0045D9A0 180
0045D9A0 55                   push    ebp
0045D9A1 8bec                 mov     ebp, esp
0045D9A3 51                   push    ecx
0045D9A4 53                   push    ebx
0045D9A5 56                   push    esi
0045D9A6 57                   push    edi
0045D9A7 894dfc               mov     dword ptr [ebp - 4], ecx
0045D9AA 8d7910               lea     edi, [ecx + 0x10]
0045D9AD 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0045D9B3 33db                 xor     ebx, ebx
0045D9B5 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
0045D9BB 8b07                 mov     eax, dword ptr [edi]
0045D9BD 33c2                 xor     eax, edx
0045D9BF 8b5704               mov     edx, dword ptr [edi + 4]
0045D9C2 2bc7                 sub     eax, edi
0045D9C4 3bc2                 cmp     eax, edx
0045D9C6 740e                 je      0x45d9d6
0045D9C8 6a3f                 push    0x3f
0045D9CA 57                   push    edi
0045D9CB e8a03a4a00           call    0x901470
0045D9D0 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0045D9D6 8b4704               mov     eax, dword ptr [edi + 4]
0045D9D9 8b5144               mov     edx, dword ptr [ecx + 0x44]
0045D9DC c1e002               shl     eax, 2
0045D9DF 8b3402               mov     esi, dword ptr [edx + eax]
0045D9E2 03d0                 add     edx, eax
0045D9E4 33f2                 xor     esi, edx
0045D9E6 33355cd4cf00         xor     esi, dword ptr [0xcfd45c]
0045D9EC 8b5148               mov     edx, dword ptr [ecx + 0x48]
0045D9EF 03c2                 add     eax, edx
0045D9F1 8b10                 mov     edx, dword ptr [eax]
0045D9F3 33d0                 xor     edx, eax
0045D9F5 33155cd4cf00         xor     edx, dword ptr [0xcfd45c]
0045D9FB 3bf2                 cmp     esi, edx
0045D9FD 740e                 je      0x45da0d
0045D9FF 6a40                 push    0x40
0045DA01 57                   push    edi
0045DA02 e8693a4a00           call    0x901470
0045DA07 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0045DA0D 3bde                 cmp     ebx, esi
0045DA0F 7d35                 jge     0x45da46
0045DA11 8b45fc               mov     eax, dword ptr [ebp - 4]
0045DA14 8b5018               mov     edx, dword ptr [eax + 0x18]
0045DA17 833c9a00             cmp     dword ptr [edx + ebx*4], 0
0045DA1B 7418                 je      0x45da35
0045DA1D 8bc2                 mov     eax, edx
0045DA1F 8b0c98               mov     ecx, dword ptr [eax + ebx*4]
0045DA22 8b11                 mov     edx, dword ptr [ecx]
0045DA24 ff5218               call    dword ptr [edx + 0x18]
0045DA27 8b4d08               mov     ecx, dword ptr [ebp + 8]
0045DA2A 394804               cmp     dword ptr [eax + 4], ecx
0045DA2D 740c                 je      0x45da3b
0045DA2F 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
0045DA35 43                   inc     ebx
0045DA36 e97affffff           jmp     0x45d9b5
0045DA3B 5f                   pop     edi
0045DA3C 5e                   pop     esi
0045DA3D 8bc3                 mov     eax, ebx
0045DA3F 5b                   pop     ebx
0045DA40 8be5                 mov     esp, ebp
0045DA42 5d                   pop     ebp
0045DA43 c20400               ret     4
0045DA46 5f                   pop     edi
0045DA47 5e                   pop     esi
0045DA48 83c8ff               or      eax, 0xffffffff
0045DA4B 5b                   pop     ebx
0045DA4C 8be5                 mov     esp, ebp
0045DA4E 5d                   pop     ebp
0045DA4F c20400               ret     4
0045DA52 cc                   int3    
0045DA53 cc                   int3    
0045DA54 cc                   int3    
0045DA55 cc                   int3    
0045DA56 cc                   int3    
0045DA57 cc                   int3    
0045DA58 cc                   int3    
0045DA59 cc                   int3    
0045DA5A cc                   int3    
0045DA5B cc                   int3    
0045DA5C cc                   int3    
0045DA5D cc                   int3    
0045DA5E cc                   int3    
0045DA5F cc                   int3    
0045DA60 55                   push    ebp
0045DA61 8bec                 mov     ebp, esp
0045DA63 8b4508               mov     eax, dword ptr [ebp + 8]
0045DA66 85c0                 test    eax, eax
0045DA68 57                   push    edi
0045DA69 8bf9                 mov     edi, ecx
0045DA6B 7508                 jne     0x45da75
0045DA6D 83c8ff               or      eax, 0xffffffff
0045DA70 5f                   pop     edi
0045DA71 5d                   pop     ebp
0045DA72 c20400               ret     4
0045DA75 53                   push    ebx
0045DA76 8d5f10               lea     ebx, [edi + 0x10]
0045DA79 56                   push    esi
0045DA7A 8bcb                 mov     ecx, ebx
0045DA7C 33f6                 xor     esi, esi
0045DA7E e8ad45faff           call    0x402030
0045DA83 85c0                 test    eax, eax
0045DA85 7e17                 jle     0x45da9e
0045DA87 8b4718               mov     eax, dword ptr [edi + 0x18]
0045DA8A 8b4d08               mov     ecx, dword ptr [ebp + 8]
0045DA8D 390cb0               cmp     dword ptr [eax + esi*4], ecx
0045DA90 7416                 je      0x45daa8
0045DA92 8bcb                 mov     ecx, ebx
0045DA94 46                   inc     esi
0045DA95 e89645faff           call    0x402030
0045DA9A 3bf0                 cmp     esi, eax
0045DA9C 7ce9                 jl      0x45da87
0045DA9E 5e                   pop     esi
0045DA9F 5b                   pop     ebx
0045DAA0 83c8ff               or      eax, 0xffffffff
0045DAA3 5f                   pop     edi
0045DAA4 5d                   pop     ebp
0045DAA5 c20400               ret     4
0045DAA8 8bc6                 mov     eax, esi
0045DAAA 5e                   pop     esi
0045DAAB 5b                   pop     ebx
0045DAAC 5f                   pop     edi
0045DAAD 5d                   pop     ebp
0045DAAE c20400               ret     4
0045DAB1 cc                   int3    
0045DAB2 cc                   int3    
0045DAB3 cc                   int3    
0045DAB4 cc                   int3    
0045DAB5 cc                   int3    
0045DAB6 cc                   int3    
0045DAB7 cc                   int3    
0045DAB8 cc                   int3    
0045DAB9 cc                   int3    
0045DABA cc                   int3    
0045DABB cc                   int3    
0045DABC cc                   int3    
0045DABD cc                   int3    
0045DABE cc                   int3    
0045DABF cc                   int3    
0045DAC0 55                   push    ebp
0045DAC1 8bec                 mov     ebp, esp
0045DAC3 53                   push    ebx
0045DAC4 56                   push    esi
0045DAC5 57                   push    edi
0045DAC6 8b7d08               mov     edi, dword ptr [ebp + 8]
0045DAC9 8b07                 mov     eax, dword ptr [edi]
0045DACB 83ceff               or      esi, 0xffffffff
0045DACE 85c0                 test    eax, eax
0045DAD0 7405                 je      0x45dad7
0045DAD2 83f809               cmp     eax, 9
0045DAD5 7523                 jne     0x45dafa
0045DAD7 8b5d0c               mov     ebx, dword ptr [ebp + 0xc]
0045DADA 8b0decdcb500         mov     ecx, dword ptr [0xb5dcec]
0045DAE0 53                   push    ebx
0045DAE1 e87affffff           call    0x45da60
0045DAE6 8bf0                 mov     esi, eax
0045DAE8 83feff               cmp     esi, -1
0045DAEB 7410                 je      0x45dafd
0045DAED c70700000000         mov     dword ptr [edi], 0
0045DAF3 5f                   pop     edi
0045DAF4 8bc6                 mov     eax, esi
0045DAF6 5e                   pop     esi
0045DAF7 5b                   pop     ebx
0045DAF8 5d                   pop     ebp
0045DAF9 c3                   ret     
0045DAFA 8b5d0c               mov     ebx, dword ptr [ebp + 0xc]
0045DAFD 8b07                 mov     eax, dword ptr [edi]
0045DAFF 83f801               cmp     eax, 1
0045DB02 7405                 je      0x45db09
0045DB04 83f809               cmp     eax, 9
0045DB07 751e                 jne     0x45db27
0045DB09 8b0df4dcb500         mov     ecx, dword ptr [0xb5dcf4]
0045DB0F 53                   push    ebx
0045DB10 e84bffffff           call    0x45da60
0045DB15 8bf0                 mov     esi, eax
0045DB17 83feff               cmp     esi, -1
0045DB1A 740b                 je      0x45db27
0045DB1C c7                   .byte   0xc7
0045DB1D 07                   pop     es
0045DB1E 0100                 add     dword ptr [eax], eax
RANGE 004F3BA0 400
004F3BA0 55                   push    ebp
004F3BA1 8bec                 mov     ebp, esp
004F3BA3 83ec14               sub     esp, 0x14
004F3BA6 56                   push    esi
004F3BA7 8b7508               mov     esi, dword ptr [ebp + 8]
004F3BAA 85f6                 test    esi, esi
004F3BAC 0f84e0020000         je      0x4f3e92
004F3BB2 53                   push    ebx
004F3BB3 33db                 xor     ebx, ebx
004F3BB5 57                   push    edi
004F3BB6 895df0               mov     dword ptr [ebp - 0x10], ebx
004F3BB9 8da42400000000       lea     esp, [esp]
004F3BC0 8b06                 mov     eax, dword ptr [esi]
004F3BC2 53                   push    ebx
004F3BC3 8bce                 mov     ecx, esi
004F3BC5 ff9068030000         call    dword ptr [eax + 0x368]
004F3BCB 8bf8                 mov     edi, eax
004F3BCD 85ff                 test    edi, edi
004F3BCF 897df4               mov     dword ptr [ebp - 0xc], edi
004F3BD2 0f84ab020000         je      0x4f3e83
004F3BD8 8b17                 mov     edx, dword ptr [edi]
004F3BDA 8bcf                 mov     ecx, edi
004F3BDC ff92e0000000         call    dword ptr [edx + 0xe0]
004F3BE2 8bd8                 mov     ebx, eax
004F3BE4 33c0                 xor     eax, eax
004F3BE6 8945f8               mov     dword ptr [ebp - 8], eax
004F3BE9 8945fc               mov     dword ptr [ebp - 4], eax
004F3BEC 8b8398020000         mov     eax, dword ptr [ebx + 0x298]
004F3BF2 85c0                 test    eax, eax
004F3BF4 0f8446010000         je      0x4f3d40
004F3BFA 8b8b9c020000         mov     ecx, dword ptr [ebx + 0x29c]
004F3C00 2bc8                 sub     ecx, eax
004F3C02 b8398ee338           mov     eax, 0x38e38e39
004F3C07 f7e9                 imul    ecx
004F3C09 8b4df8               mov     ecx, dword ptr [ebp - 8]
004F3C0C c1fa03               sar     edx, 3
004F3C0F 8bc2                 mov     eax, edx
004F3C11 c1e81f               shr     eax, 0x1f
004F3C14 03c2                 add     eax, edx
004F3C16 3bc8                 cmp     ecx, eax
004F3C18 0f8322010000         jae     0x4f3d40
004F3C1E 8b4dfc               mov     ecx, dword ptr [ebp - 4]
004F3C21 8b8398020000         mov     eax, dword ptr [ebx + 0x298]
004F3C27 8b5514               mov     edx, dword ptr [ebp + 0x14]
004F3C2A 03c1                 add     eax, ecx
004F3C2C 395020               cmp     dword ptr [eax + 0x20], edx
004F3C2F 0f85f6000000         jne     0x4f3d2b
004F3C35 8b355cd4cf00         mov     esi, dword ptr [0xcfd45c]
004F3C3B 8b4808               mov     ecx, dword ptr [eax + 8]
004F3C3E 8d7804               lea     edi, [eax + 4]
004F3C41 8b07                 mov     eax, dword ptr [edi]
004F3C43 33c6                 xor     eax, esi
004F3C45 2bc7                 sub     eax, edi
004F3C47 3bc1                 cmp     eax, ecx
004F3C49 740e                 je      0x4f3c59
004F3C4B 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
004F3C51 6a3f                 push    0x3f
004F3C53 57                   push    edi
004F3C54 e817d84000           call    0x901470
004F3C59 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
004F3C5F 8b5144               mov     edx, dword ptr [ecx + 0x44]
004F3C62 8b4704               mov     eax, dword ptr [edi + 4]
004F3C65 c1e002               shl     eax, 2
004F3C68 8d0c02               lea     ecx, [edx + eax]
004F3C6B 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
004F3C71 8bf1                 mov     esi, ecx
004F3C73 33f2                 xor     esi, edx
004F3C75 8b11                 mov     edx, dword ptr [ecx]
004F3C77 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
004F3C7D 33f2                 xor     esi, edx
004F3C7F 8b5148               mov     edx, dword ptr [ecx + 0x48]
004F3C82 03c2                 add     eax, edx
004F3C84 8b155cd4cf00         mov     edx, dword ptr [0xcfd45c]
004F3C8A 8bc8                 mov     ecx, eax
004F3C8C 33ca                 xor     ecx, edx
004F3C8E 3308                 xor     ecx, dword ptr [eax]
004F3C90 3bf1                 cmp     esi, ecx
004F3C92 740e                 je      0x4f3ca2
004F3C94 8b0d58d4cf00         mov     ecx, dword ptr [0xcfd458]
004F3C9A 6a40                 push    0x40
004F3C9C 57                   push    edi
004F3C9D e8ced74000           call    0x901470
004F3CA2 3b7518               cmp     esi, dword ptr [ebp + 0x18]
004F3CA5 0f8580000000         jne     0x4f3d2b
004F3CAB 8b45fc               mov     eax, dword ptr [ebp - 4]
004F3CAE 8b9398020000         mov     edx, dword ptr [ebx + 0x298]
004F3CB4 8b4d10               mov     ecx, dword ptr [ebp + 0x10]
004F3CB7 8d3410               lea     esi, [eax + edx]
004F3CBA 394e0c               cmp     dword ptr [esi + 0xc], ecx
004F3CBD 756c                 jne     0x4f3d2b
004F3CBF 833e05               cmp     dword ptr [esi], 5
004F3CC2 7411                 je      0x4f3cd5
004F3CC4 8b4d08               mov     ecx, dword ptr [ebp + 8]
004F3CC7 8b11                 mov     edx, dword ptr [ecx]
004F3CC9 ff928c040000         call    dword ptr [edx + 0x48c]
004F3CCF 8b0e                 mov     ecx, dword ptr [esi]
004F3CD1 3b08                 cmp     ecx, dword ptr [eax]
004F3CD3 7556                 jne     0x4f3d2b
004F3CD5 8b8398020000         mov     eax, dword ptr [ebx + 0x298]
004F3CDB 8b75fc               mov     esi, dword ptr [ebp - 4]
004F3CDE 03c6                 add     eax, esi
004F3CE0 8d4810               lea     ecx, [eax + 0x10]
004F3CE3 e848e3f0ff           call    0x402030
004F3CE8 85c0                 test    eax, eax
004F3CEA 8b8398020000         mov     eax, dword ptr [ebx + 0x298]
004F3CF0 742c                 je      0x4f3d1e
004F3CF2 db450c               fild    dword ptr [ebp + 0xc]
004F3CF5 03c6                 add     eax, esi
004F3CF7 8d4818               lea     ecx, [eax + 0x18]
004F3CFA d95d0c               fstp    dword ptr [ebp + 0xc]
004F3CFD e82ee3f0ff           call    0x402030
004F3D02 8945ec               mov     dword ptr [ebp - 0x14], eax
004F3D05 db45ec               fild    dword ptr [ebp - 0x14]
004F3D08 d80d70a79900         fmul    dword ptr [0x99a770]
004F3D0E d84d0c               fmul    dword ptr [ebp + 0xc]
004F3D11 d8450c               fadd    dword ptr [ebp + 0xc]
004F3D14 e8ef794200           call    0x91b708
004F3D19 89450c               mov     dword ptr [ebp + 0xc], eax
004F3D1C eb0d                 jmp     0x4f3d2b
004F3D1E 03c6                 add     eax, esi
004F3D20 8d4818               lea     ecx, [eax + 0x18]
004F3D23 e808e3f0ff           call    0x402030
004F3D28 01450c               add     dword ptr [ebp + 0xc], eax
004F3D2B 8b4df8               mov     ecx, dword ptr [ebp - 8]
004F3D2E 8b45fc               mov     eax, dword ptr [ebp - 4]
004F3D31 8b7df4               mov     edi, dword ptr [ebp - 0xc]
004F3D34 41                   inc     ecx
004F3D35 894df8               mov     dword ptr [ebp - 8], ecx
004F3D38 83c024               add     eax, 0x24
004F3D3B e9a9feffff           jmp     0x4f3be9
004F3D40 8b17                 mov     edx, dword ptr [edi]
004F3D42 8bcf                 mov     ecx, edi
004F3D44 ff9218010000         call    dword ptr [edx + 0x118]
004F3D4A 8bf0                 mov     esi, eax
004F3D4C 85f6                 test    esi, esi
004F3D4E 0f8429010000         je      0x4f3e7d
004F3D54 33db                 xor     ebx, ebx
004F3D56 895df8               mov     dword ptr [ebp - 8], ebx
004F3D59 8da42400000000       lea     esp, [esp]
004F3D60 8b8698020000         mov     eax, dword ptr [esi + 0x298]
004F3D66 85c0                 test    eax, eax
004F3D68 0f840f010000         je      0x4f3e7d
004F3D6E 8b8e9c020000         mov     ecx, dword ptr [esi + 0x29c]
004F3D74 2bc8                 sub     ecx, eax
004F3D76 b8398ee338           mov     eax, 0x38e38e39
004F3D7B f7e9                 imul    ecx
004F3D7D 8b4df8               mov     ecx, dword ptr [ebp - 8]
004F3D80 c1fa03               sar     edx, 3
004F3D83 8bc2                 mov     eax, edx
004F3D85 c1e81f               shr     eax, 0x1f
004F3D88 03c2                 add     eax, edx
004F3D8A 3bc8                 cmp     ecx, eax
004F3D8C 0f83eb000000         jae     0x4f3e7d
004F3D92 8b07                 mov     eax, dword ptr [edi]
004F3D94 8bcf                 mov     ecx, edi
004F3D96 ff9028010000         call    dword ptr [eax + 0x128]
004F3D9C 84c0                 test    al, al
004F3D9E 0f84c7000000         je      0x4f3e6b
004F3DA4 8b17                 mov     edx, dword ptr [edi]
004F3DA6 8bcf                 mov     ecx, edi
004F3DA8 ff9214010000         call    dword ptr [edx + 0x114]
004F3DAE 84c0                 test    al, al
004F3DB0 0f84b5000000         je      0x4f3e6b
004F3DB6 8b8698020000         mov     eax, dword ptr [esi + 0x298]
004F3DBC 8b4d14               mov     ecx, dword ptr [ebp + 0x14]
004F3DBF 8b541820             mov     edx, dword ptr [eax + ebx + 0x20]
004F3DC3 03c3                 add     eax, ebx
004F3DC5 3bd1                 cmp     edx, ecx
004F3DC7 0f859e000000         jne     0x4f3e6b
004F3DCD 8d4804               lea     ecx, [eax + 4]
004F3DD0 e85be2f0ff           call    0x402030
004F3DD5 3b4518               cmp     eax, dword ptr [ebp + 0x18]
004F3DD8 0f858d000000         jne     0x4f3e6b
004F3DDE 8b9698020000         mov     edx, dword ptr [esi + 0x298]
004F3DE4 8b4510               mov     eax, dword ptr [ebp + 0x10]
004F3DE7 8b4c130c             mov     ecx, dword ptr [ebx + edx + 0xc]
004F3DEB 3bc8                 cmp     ecx, eax
004F3DED 8d3c13               lea     edi, [ebx + edx]
004F3DF0 7579                 jne     0x4f3e6b
004F3DF2 833f05               cmp     dword ptr [edi], 5
004F3DF5 7411                 je      0x4f3e08
004F3DF7 8b4d08               mov     ecx, dword ptr [ebp + 8]
004F3DFA 8b11                 mov     edx, dword ptr [ecx]
004F3DFC ff928c040000         call    dword ptr [edx + 0x48c]
004F3E02 8b0f                 mov     ecx, dword ptr [edi]
004F3E04 3b08                 cmp     ecx, dword ptr [eax]
004F3E06 7563                 jne     0x4f3e6b
004F3E08 8b8698020000         mov     eax, dword ptr [esi + 0x298]
004F3E0E 03c3                 add     eax, ebx
004F3E10 8d4810               lea     ecx, [eax + 0x10]
004F3E13 e818e2f0ff           call    0x402030
004F3E18 85c0                 test    eax, eax
004F3E1A 8b8698020000         mov     eax, dword ptr [esi + 0x298]
004F3E20 743c                 je      0x4f3e5e
004F3E22 db450c               fild    dword ptr [ebp + 0xc]
004F3E25 03c3                 add     eax, ebx
004F3E27 8d4818               lea     ecx, [eax + 0x18]
004F3E2A d95d0c               fstp    dword ptr [ebp + 0xc]
004F3E2D e8fee1f0ff           call    0x402030
004F3E32 8945ec               mov     dword ptr [ebp - 0x14], eax
004F3E35 db45ec               fild    dword ptr [ebp - 0x14]
004F3E38 d80d70a79900         fmul    dword ptr [0x99a770]
004F3E3E d84d0c               fmul    dword ptr [ebp + 0xc]
004F3E41 d8450c               fadd    dword ptr [ebp + 0xc]
004F3E44 e8bf784200           call    0x91b708
004F3E49 8b4df8               mov     ecx, dword ptr [ebp - 8]
004F3E4C 8b7df4               mov     edi, dword ptr [ebp - 0xc]
004F3E4F 41                   inc     ecx
004F3E50 89450c               mov     dword ptr [ebp + 0xc], eax
004F3E53 894df8               mov     dword ptr [ebp - 8], ecx
004F3E56 83c324               add     ebx, 0x24
004F3E59 e902ffffff           jmp     0x4f3d60
004F3E5E 03c3                 add     eax, ebx
004F3E60 8d4818               lea     ecx, [eax + 0x18]
004F3E63 e8c8e1f0ff           call    0x402030
004F3E68 01450c               add     dword ptr [ebp + 0xc], eax
004F3E6B 8b4df8               mov     ecx, dword ptr [ebp - 8]
004F3E6E 8b7df4               mov     edi, dword ptr [ebp - 0xc]
004F3E71 41                   inc     ecx
004F3E72 894df8               mov     dword ptr [ebp - 8], ecx
004F3E75 83c324               add     ebx, 0x24
004F3E78 e9e3feffff           jmp     0x4f3d60
004F3E7D 8b5df0               mov     ebx, dword ptr [ebp - 0x10]
004F3E80 8b7508               mov     esi, dword ptr [ebp + 8]
004F3E83 43                   inc     ebx
004F3E84 83fb17               cmp     ebx, 0x17
004F3E87 895df0               mov     dword ptr [ebp - 0x10], ebx
004F3E8A 0f8c30fdffff         jl      0x4f3bc0
004F3E90 5f                   pop     edi
004F3E91 5b                   pop     ebx
004F3E92 8b450c               mov     eax, dword ptr [ebp + 0xc]
004F3E95 5e                   pop     esi
004F3E96 8be5                 mov     esp, ebp
004F3E98 5d                   pop     ebp
004F3E99 c21400               ret     0x14
004F3E9C cc                   int3    
004F3E9D cc                   int3    
004F3E9E cc                   int3    
004F3E9F cc                   int3    
004F3EA0 55                   push    ebp
004F3EA1 8bec                 mov     ebp, esp
004F3EA3 56                   push    esi
004F3EA4 8bf1                 mov     esi, ecx
004F3EA6 8b8e30020000         mov     ecx, dword ptr [esi + 0x230]
004F3EAC 85c9                 test    ecx, ecx
004F3EAE 57                   push    edi
004F3EAF 7412                 je      0x4f3ec3
004F3EB1 8b8634020000         mov     eax, dword ptr [esi + 0x234]
004F3EB7 8b7d08               mov     edi, dword ptr [ebp + 8]
004F3EBA 2bc1                 sub     eax, ecx
004F3EBC c1f803               sar     eax, 3
004F3EBF 3bf8                 cmp     edi, eax
004F3EC1 7208                 jb      0x4f3ecb
004F3EC3 5f                   pop     edi
004F3EC4 33c0                 xor     eax, eax
004F3EC6 5e                   pop     esi
004F3EC7 5d                   pop     ebp
004F3EC8 c20800               ret     8
004F3ECB 8b8630020000         mov     eax, dword ptr [esi + 0x230]
004F3ED1 53                   push    ebx
004F3ED2 8d0cf8               lea     ecx, [eax + edi*8]
004F3ED5 e856e1f0ff           call    0x402030
004F3EDA 8d4e08               lea     ecx, [esi + 8]
004F3EDD 8bd8                 mov     ebx, eax
004F3EDF e84ce1f0ff           call    0x402030
004F3EE4 8b4d0c               mov     ecx, dword ptr [ebp + 0xc]
004F3EE7 50                   push    eax
004F3EE8 6a00                 push    0
004F3EEA 57                   push    edi
004F3EEB 53                   push    ebx
004F3EEC 51                   push    ecx
004F3EED 8bce                 mov     ecx, esi
004F3EEF e8acfcffff           call    0x4f3ba0
004F3EF4 5b                   pop     ebx
004F3EF5 5f                   pop     edi
004F3EF6 5e                   pop     esi
004F3EF7 5d                   pop     ebp
004F3EF8 c20800               ret     8
004F3EFB cc                   int3    
004F3EFC cc                   int3    
004F3EFD cc                   int3    
004F3EFE cc                   int3    
004F3EFF cc                   int3    
004F3F00 55                   push    ebp
004F3F01 8bec                 mov     ebp, esp
004F3F03 56                   push    esi
004F3F04 8bf1                 mov     esi, ecx
004F3F06 57                   push    edi
004F3F07 8d4e20               lea     ecx, [esi + 0x20]
004F3F0A e821e1f0ff           call    0x402030
004F3F0F 8b7d14               mov     edi, dword ptr [ebp + 0x14]
004F3F12 8b4d0c               mov     ecx, dword ptr [ebp + 0xc]
004F3F15 48                   dec     eax
004F3F16 50                   push    eax
004F3F17 8b4510               mov     eax, dword ptr [ebp + 0x10]
004F3F1A 48                   dec     eax
004F3F1B 50                   push    eax
004F3F1C 57                   push    edi
004F3F1D 51                   push    ecx
004F3F1E 8bce                 mov     ecx, esi
004F3F20 e87bffffff           call    0x4f3ea0
004F3F25 8b5508               mov     edx, dword ptr [ebp + 8]
004F3F28 894510               mov     dword ptr [ebp + 0x10], eax
004F3F2B db4510               fild    dword ptr [ebp + 0x10]
004F3F2E 51                   push    ecx
004F3F2F 8bce                 mov     ecx, esi
004F3F31 d91c24               fstp    dword ptr [esp]
004F3F34 57                   push    edi
004F3F35 52                   push    edx
004F3F36 e865ffffff           call    0x4f3ea0
004F3F3B 894510               mov     dword ptr [ebp + 0x10], eax
004F3F3E db4510               fild    dword ptr [ebp + 0x10]
004F3F41 51                   push    ecx
004F3F42 d91c24               fstp    dword ptr [esp]
004F3F45 e896104000           call    0x8f4fe0
004F3F4A 83c40c               add     esp, 0xc
004F3F4D d91c24               fstp    dword ptr [esp]
004F3F50 e8ebf9f3ff           call    0x433940
004F3F55 83c404               add     esp, 4
004F3F58 5f                   pop     edi
004F3F59 5e                   pop     esi
004F3F5A 5d                   pop     ebp
004F3F5B c21000               ret     0x10
004F3F5E cc                   int3    
004F3F5F cc                   int3    
004F3F60 55                   push    ebp
004F3F61 8bec                 mov     ebp, esp
004F3F63 56                   push    esi
004F3F64 8bf1                 mov     esi, ecx
004F3F66 57                   push    edi
004F3F67 8d4e20               lea     ecx, [esi + 0x20]
004F3F6A e8c1e0f0ff           call    0x402030
004F3F6F 8b7d14               mov     edi, dword ptr [ebp + 0x14]
004F3F72 8b4d0c               mov     ecx, dword ptr [ebp + 0xc]
004F3F75 48                   dec     eax
004F3F76 50                   push    eax
004F3F77 8b4510               mov     eax, dword ptr [ebp + 0x10]
004F3F7A 48                   dec     eax
004F3F7B 50                   push    eax
004F3F7C 57                   push    edi
004F3F7D 51                   push    ecx
004F3F7E 8bce                 mov     ecx, esi
004F3F80 e81bffffff           call    0x4f3ea0
004F3F85 8b5508               mov     edx, dword ptr [ebp + 8]
004F3F88 894510               mov     dword ptr [ebp + 0x10], eax
004F3F8B db4510               fild    dword ptr [ebp + 0x10]
004F3F8E 51                   push    ecx
004F3F8F 8bce                 mov     ecx, esi
004F3F91 d91c24               fstp    dword ptr [esp]
004F3F94 57                   push    edi
004F3F95 52                   push    edx
004F3F96 e805ffffff           call    0x4f3ea0
004F3F9B 894510               mov     dword ptr [ebp + 0x10], eax
004F3F9E db                   .byte   0xdb
004F3F9F 45                   inc     ebp
