
SYMBOL _ZN17DisPatcher_SetPos4readER9PacketBufR8MSG_BASE
081C97A6 55 push ebp
081C97A7 89e5 mov ebp, esp
081C97A9 83ec28 sub esp, 0x28
081C97AC 8b4510 mov eax, dword ptr [ebp + 0x10]
081C97AF 8945f4 mov dword ptr [ebp - 0xc], eax
081C97B2 8b45f4 mov eax, dword ptr [ebp - 0xc]
081C97B5 83c00d add eax, 0xd
081C97B8 89442404 mov dword ptr [esp + 4], eax
081C97BC 8b450c mov eax, dword ptr [ebp + 0xc]
081C97BF 890424 mov dword ptr [esp], eax
081C97C2 e8e9383c00 call 0x858d0b0
081C97C7 83f001 xor eax, 1
081C97CA 84c0 test al, al
081C97CC 7429 je 0x81c97f7
081C97CE c744240c00000000 mov dword ptr [esp + 0xc], 0
081C97D6 c744240800000000 mov dword ptr [esp + 8], 0
081C97DE c74424042055bd08 mov dword ptr [esp + 4], 0x8bd5520
081C97E6 c70424a21c0000 mov dword ptr [esp], 0x1ca2
081C97ED e8e5703c00 call 0x85908d7
081C97F2 e9ce000000 jmp 0x81c98c5
081C97F7 8b45f4 mov eax, dword ptr [ebp - 0xc]
081C97FA 83c00f add eax, 0xf
081C97FD 89442404 mov dword ptr [esp + 4], eax
081C9801 8b450c mov eax, dword ptr [ebp + 0xc]
081C9804 890424 mov dword ptr [esp], eax
081C9807 e8a4383c00 call 0x858d0b0
081C980C 83f001 xor eax, 1
081C980F 84c0 test al, al
081C9811 7429 je 0x81c983c
081C9813 c744240c00000000 mov dword ptr [esp + 0xc], 0
081C981B c744240800000000 mov dword ptr [esp + 8], 0
081C9823 c74424042055bd08 mov dword ptr [esp + 4], 0x8bd5520
081C982B c70424a41c0000 mov dword ptr [esp], 0x1ca4
081C9832 e8a0703c00 call 0x85908d7
081C9837 e989000000 jmp 0x81c98c5
081C983C 8b45f4 mov eax, dword ptr [ebp - 0xc]
081C983F 83c011 add eax, 0x11
081C9842 89442404 mov dword ptr [esp + 4], eax
081C9846 8b450c mov eax, dword ptr [ebp + 0xc]
081C9849 890424 mov dword ptr [esp], eax
081C984C e8d1363c00 call 0x858cf22
081C9851 83f001 xor eax, 1
081C9854 84c0 test al, al
081C9856 7426 je 0x81c987e
081C9858 c744240c00000000 mov dword ptr [esp + 0xc], 0
081C9860 c744240800000000 mov dword ptr [esp + 8], 0
081C9868 c74424042055bd08 mov dword ptr [esp + 4], 0x8bd5520
081C9870 c70424a61c0000 mov dword ptr [esp], 0x1ca6
081C9877 e85b703c00 call 0x85908d7
081C987C eb47 jmp 0x81c98c5
081C987E 8b45f4 mov eax, dword ptr [ebp - 0xc]
081C9881 83c012 add eax, 0x12
081C9884 89442404 mov dword ptr [esp + 4], eax
081C9888 8b450c mov eax, dword ptr [ebp + 0xc]
081C988B 890424 mov dword ptr [esp], eax
081C988E e81d383c00 call 0x858d0b0
081C9893 83f001 xor eax, 1
081C9896 84c0 test al, al
081C9898 7426 je 0x81c98c0
081C989A c744240c00000000 mov dword ptr [esp + 0xc], 0
081C98A2 c744240800000000 mov dword ptr [esp + 8], 0
081C98AA c74424042055bd08 mov dword ptr [esp + 4], 0x8bd5520
081C98B2 c70424a81c0000 mov dword ptr [esp], 0x1ca8
081C98B9 e819703c00 call 0x85908d7
081C98BE eb05 jmp 0x81c98c5
081C98C0 b800000000 mov eax, 0
081C98C5 c9 leave 
081C98C6 c3 ret 

SYMBOL _ZN9GameWorld13move_positionEP5CUseriiit
086C5706 55 push ebp
086C5707 89e5 mov ebp, esp
086C5709 56 push esi
086C570A 53 push ebx
086C570B 83ec50 sub esp, 0x50
086C570E 8b451c mov eax, dword ptr [ebp + 0x1c]
086C5711 668945c4 mov word ptr [ebp - 0x3c], ax
086C5715 837d0c00 cmp dword ptr [ebp + 0xc], 0
086C5719 0f8402020000 je 0x86c5921
086C571F 8b450c mov eax, dword ptr [ebp + 0xc]
086C5722 890424 mov dword ptr [esp], eax
086C5725 e8b04ba1ff call 0x80da2da
086C572A 84c0 test al, al
086C572C 0f85f2010000 jne 0x86c5924
086C5732 8b4518 mov eax, dword ptr [ebp + 0x18]
086C5735 0fbec8 movsx ecx, al
086C5738 8b4514 mov eax, dword ptr [ebp + 0x14]
086C573B 0fb7d0 movzx edx, ax
086C573E 8b4510 mov eax, dword ptr [ebp + 0x10]
086C5741 0fb7c0 movzx eax, ax
086C5744 894c240c mov dword ptr [esp + 0xc], ecx
086C5748 89542408 mov dword ptr [esp + 8], edx
086C574C 89442404 mov dword ptr [esp + 4], eax
086C5750 8b450c mov eax, dword ptr [ebp + 0xc]
086C5753 890424 mov dword ptr [esp], eax
086C5756 e8cfb6c2ff call 0x82f0e2a
086C575B 8d45cc lea eax, [ebp - 0x34]
086C575E 890424 mov dword ptr [esp], eax
086C5761 e8e685ecff call 0x858dd4c
086C5766 c744240816000000 mov dword ptr [esp + 8], 0x16
086C576E c744240400000000 mov dword ptr [esp + 4], 0
086C5776 8d45cc lea eax, [ebp - 0x34]
086C5779 890424 mov dword ptr [esp], eax
086C577C e87b61a0ff call 0x80cb8fc
086C5781 8b450c mov eax, dword ptr [ebp + 0xc]
086C5784 890424 mov dword ptr [esp], eax
086C5787 e8f04ba1ff call 0x80da37c
086C578C 0fb7c0 movzx eax, ax
086C578F 89442404 mov dword ptr [esp + 4], eax
086C5793 8d45cc lea eax, [ebp - 0x34]
086C5796 890424 mov dword ptr [esp], eax
086C5799 e80647a1ff call 0x80d9ea4
086C579E 8b4510 mov eax, dword ptr [ebp + 0x10]
086C57A1 89442404 mov dword ptr [esp + 4], eax
086C57A5 8d45cc lea eax, [ebp - 0x34]
086C57A8 890424 mov dword ptr [esp], eax
086C57AB e8f446a1ff call 0x80d9ea4
086C57B0 8b4514 mov eax, dword ptr [ebp + 0x14]
086C57B3 89442404 mov dword ptr [esp + 4], eax
086C57B7 8d45cc lea eax, [ebp - 0x34]
086C57BA 890424 mov dword ptr [esp], eax
086C57BD e8e246a1ff call 0x80d9ea4
086C57C2 8b4518 mov eax, dword ptr [ebp + 0x18]
086C57C5 89442404 mov dword ptr [esp + 4], eax
086C57C9 8d45cc lea eax, [ebp - 0x34]
086C57CC 890424 mov dword ptr [esp], eax
086C57CF e84c61a0ff call 0x80cb920
086C57D4 0fb745c4 movzx eax, word ptr [ebp - 0x3c]
086C57D8 89442404 mov dword ptr [esp + 4], eax
086C57DC 8d45cc lea eax, [ebp - 0x34]
086C57DF 890424 mov dword ptr [esp], eax
086C57E2 e8bd46a1ff call 0x80d9ea4
086C57E7 c744240401000000 mov dword ptr [esp + 4], 1
086C57EF 8d45cc lea eax, [ebp - 0x34]
086C57F2 890424 mov dword ptr [esp], eax
086C57F5 e85e61a0ff call 0x80cb958
086C57FA 8b450c mov eax, dword ptr [ebp + 0xc]
086C57FD 890424 mov dword ptr [esp], eax
086C5800 e85ffdf7ff call 0x8645564
086C5805 0fbec0 movsx eax, al
086C5808 8945e8 mov dword ptr [ebp - 0x18], eax
086C580B c744240400000000 mov dword ptr [esp + 4], 0
086C5813 8b450c mov eax, dword ptr [ebp + 0xc]
086C5816 890424 mov dword ptr [esp], eax
086C5819 e8a0bbfbff call 0x86813be
086C581E 8945ec mov dword ptr [ebp - 0x14], eax
086C5821 8b45ec mov eax, dword ptr [ebp - 0x14]
086C5824 89442408 mov dword ptr [esp + 8], eax
086C5828 8b45e8 mov eax, dword ptr [ebp - 0x18]
086C582B 89442404 mov dword ptr [esp + 4], eax
086C582F 8b4508 mov eax, dword ptr [ebp + 8]
086C5832 890424 mov dword ptr [esp], eax
086C5835 e8b4f1ffff call 0x86c49ee
086C583A 84c0 test al, al
086C583C 0f84d2000000 je 0x86c5914
086C5842 8b450c mov eax, dword ptr [ebp + 0xc]
086C5845 89442404 mov dword ptr [esp + 4], eax
086C5849 8b4508 mov eax, dword ptr [ebp + 8]
086C584C 890424 mov dword ptr [esp], eax
086C584F e8e4770000 call 0x86cd038
086C5854 8945f0 mov dword ptr [ebp - 0x10], eax
086C5857 837df000 cmp dword ptr [ebp - 0x10], 0
086C585B 7456 je 0x86c58b3
086C585D 8b45ec mov eax, dword ptr [ebp - 0x14]
086C5860 89442404 mov dword ptr [esp + 4], eax
086C5864 8b45f0 mov eax, dword ptr [ebp - 0x10]
086C5867 890424 mov dword ptr [esp], eax
086C586A e833e3ffff call 0x86c3ba2
086C586F 8945f4 mov dword ptr [ebp - 0xc], eax
086C5872 837df400 cmp dword ptr [ebp - 0xc], 0
086C5876 0f8498000000 je 0x86c5914
086C587C 8b450c mov eax, dword ptr [ebp + 0xc]
086C587F 890424 mov dword ptr [esp], eax
086C5882 e85d66fcff call 0x868bee4
086C5887 84c0 test al, al
086C5889 7414 je 0x86c589f
086C588B 8d45cc lea eax, [ebp - 0x34]
086C588E 89442404 mov dword ptr [esp + 4], eax
086C5892 8b45f4 mov eax, dword ptr [ebp - 0xc]
086C5895 890424 mov dword ptr [esp], eax
086C5898 e88dd4ffff call 0x86c2d2a
086C589D eb75 jmp 0x86c5914
086C589F 8d45cc lea eax, [ebp - 0x34]
086C58A2 89442404 mov dword ptr [esp + 4], eax
086C58A6 8b450c mov eax, dword ptr [ebp + 0xc]
086C58A9 890424 mov dword ptr [esp], eax
086C58AC e8092df8ff call 0x86485ba
086C58B1 eb61 jmp 0x86c5914
086C58B3 c744240c05000000 mov dword ptr [esp + 0xc], 5
086C58BB c7442408ec050000 mov dword ptr [esp + 8], 0x5ec
086C58C3 c744240460d5cf08 mov dword ptr [esp + 4], 0x8cfd560
086C58CB 8d45d8 lea eax, [ebp - 0x28]
086C58CE 890424 mov dword ptr [esp], eax
086C58D1 e8429ee8ff call 0x854f718
086C58D6 8b45ec mov eax, dword ptr [ebp - 0x14]
086C58D9 8944240c mov dword ptr [esp + 0xc], eax
086C58DD 8b45e8 mov eax, dword ptr [ebp - 0x18]
086C58E0 89442408 mov dword ptr [esp + 8], eax
086C58E4 c744240438b8cf08 mov dword ptr [esp + 4], 0x8cfb838
086C58EC 8d45d8 lea eax, [ebp - 0x28]
086C58EF 890424 mov dword ptr [esp], eax
086C58F2 e8919ee8ff call 0x854f788
086C58F7 eb1b jmp 0x86c5914
086C58F9 89d3 mov ebx, edx
086C58FB 89c6 mov esi, eax
086C58FD 8d45cc lea eax, [ebp - 0x34]
086C5900 890424 mov dword ptr [esp], eax
086C5903 e87885ecff call 0x858de80
086C5908 89f0 mov eax, esi
086C590A 89da mov edx, ebx
086C590C 890424 mov dword ptr [esp], eax
086C590F e83cde4100 call 0x8ae3750
086C5914 8d45cc lea eax, [ebp - 0x34]
086C5917 890424 mov dword ptr [esp], eax
086C591A e86185ecff call 0x858de80
086C591F eb04 jmp 0x86c5925
086C5921 90 nop 
086C5922 eb01 jmp 0x86c5925
086C5924 90 nop 
086C5925 83c450 add esp, 0x50
086C5928 5b pop ebx
086C5929 5e pop esi
086C592A 5d pop ebp
086C592B c3 ret 

SYMBOL _ZN17DisPatcher_SetPos7processEP5CUserR8MSG_BASER9ParamBase
081C98C8 55 push ebp
081C98C9 89e5 mov ebp, esp
081C98CB 57 push edi
081C98CC 56 push esi
081C98CD 53 push ebx
081C98CE 83ec4c sub esp, 0x4c
081C98D1 8b4510 mov eax, dword ptr [ebp + 0x10]
081C98D4 89442408 mov dword ptr [esp + 8], eax
081C98D8 8b450c mov eax, dword ptr [ebp + 0xc]
081C98DB 89442404 mov dword ptr [esp + 4], eax
081C98DF 8b4508 mov eax, dword ptr [ebp + 8]
081C98E2 890424 mov dword ptr [esp], eax
081C98E5 e8aa000000 call 0x81c9994
081C98EA 8945e0 mov dword ptr [ebp - 0x20], eax
081C98ED 837de000 cmp dword ptr [ebp - 0x20], 0
081C98F1 7e0a jle 0x81c98fd
081C98F3 b8ffffffff mov eax, 0xffffffff
081C98F8 e98e000000 jmp 0x81c998b
081C98FD 837de000 cmp dword ptr [ebp - 0x20], 0
081C9901 792d jns 0x81c9930
081C9903 8b450c mov eax, dword ptr [ebp + 0xc]
081C9906 890424 mov dword ptr [esp], eax
081C9909 e8600af1ff call 0x80da36e
081C990E 8944240c mov dword ptr [esp + 0xc], eax
081C9912 c744240800000000 mov dword ptr [esp + 8], 0
081C991A c7442404c054bd08 mov dword ptr [esp + 4], 0x8bd54c0
081C9922 c70424b81c0000 mov dword ptr [esp], 0x1cb8
081C9929 e8a96f3c00 call 0x85908d7
081C992E eb5b jmp 0x81c998b
081C9930 8b4510 mov eax, dword ptr [ebp + 0x10]
081C9933 8945e4 mov dword ptr [ebp - 0x1c], eax
081C9936 8b45e4 mov eax, dword ptr [ebp - 0x1c]
081C9939 0fb74012 movzx eax, word ptr [eax + 0x12]
081C993D 0fb7c0 movzx eax, ax
081C9940 8945d4 mov dword ptr [ebp - 0x2c], eax
081C9943 8b45e4 mov eax, dword ptr [ebp - 0x1c]
081C9946 0fb64011 movzx eax, byte ptr [eax + 0x11]
081C994A 0fbef8 movsx edi, al
081C994D 8b45e4 mov eax, dword ptr [ebp - 0x1c]
081C9950 0fb7400f movzx eax, word ptr [eax + 0xf]
081C9954 0fb7f0 movzx esi, ax
081C9957 8b45e4 mov eax, dword ptr [ebp - 0x1c]
081C995A 0fb7400d movzx eax, word ptr [eax + 0xd]
081C995E 0fb7d8 movzx ebx, ax
081C9961 e8410af1ff call 0x80da3a7
081C9966 8b55d4 mov edx, dword ptr [ebp - 0x2c]
081C9969 89542414 mov dword ptr [esp + 0x14], edx
081C996D 897c2410 mov dword ptr [esp + 0x10], edi
081C9971 8974240c mov dword ptr [esp + 0xc], esi
081C9975 895c2408 mov dword ptr [esp + 8], ebx
081C9979 8b550c mov edx, dword ptr [ebp + 0xc]
081C997C 89542404 mov dword ptr [esp + 4], edx
081C9980 890424 mov dword ptr [esp], eax
081C9983 e87ebd4f00 call 0x86c5706
081C9988 8b45e0 mov eax, dword ptr [ebp - 0x20]
081C998B 83c44c add esp, 0x4c
081C998E 5b pop ebx
081C998F 5e pop esi
081C9990 5f pop edi
081C9991 5d pop ebp
081C9992 c3 ret 

SYMBOL _ZN17DisPatcher_SetPos11check_errorEP5CUserR8MSG_BASE
081C9994 55 push ebp
081C9995 89e5 mov ebp, esp
081C9997 83ec18 sub esp, 0x18
081C999A 8b450c mov eax, dword ptr [ebp + 0xc]
081C999D 890424 mov dword ptr [esp], eax
081C99A0 e8e709f1ff call 0x80da38c
081C99A5 83f803 cmp eax, 3
081C99A8 0f95c0 setne al
081C99AB 84c0 test al, al
081C99AD 7407 je 0x81c99b6
081C99AF b813000000 mov eax, 0x13
081C99B4 eb05 jmp 0x81c99bb
081C99B6 b800000000 mov eax, 0
081C99BB c9 leave 
081C99BC c3 ret 

SYMBOL _ZN5CUser12set_positionEttc
082F0E2A 55 push ebp
082F0E2B 89e5 mov ebp, esp
082F0E2D 83ec0c sub esp, 0xc
082F0E30 8b4d0c mov ecx, dword ptr [ebp + 0xc]
082F0E33 8b5510 mov edx, dword ptr [ebp + 0x10]
082F0E36 8b4514 mov eax, dword ptr [ebp + 0x14]
082F0E39 66894dfc mov word ptr [ebp - 4], cx
082F0E3D 668955f8 mov word ptr [ebp - 8], dx
082F0E41 8845f4 mov byte ptr [ebp - 0xc], al
082F0E44 8b4508 mov eax, dword ptr [ebp + 8]
082F0E47 0fb755fc movzx edx, word ptr [ebp - 4]
082F0E4B 668990fccf0800 mov word ptr [eax + 0x8cffc], dx
082F0E52 8b4508 mov eax, dword ptr [ebp + 8]
082F0E55 0fb755f8 movzx edx, word ptr [ebp - 8]
082F0E59 668990fecf0800 mov word ptr [eax + 0x8cffe], dx
082F0E60 8b4508 mov eax, dword ptr [ebp + 8]
082F0E63 0fb655f4 movzx edx, byte ptr [ebp - 0xc]
082F0E67 889000d00800 mov byte ptr [eax + 0x8d000], dl
082F0E6D c9 leave 
082F0E6E c3 ret 