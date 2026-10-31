REF 007C335D
007C332D 00c7                 add     bh, al
007C332F 46                   inc     esi
007C3330 0471                 add     al, 0x71
007C3332 0000                 add     byte ptr [eax], al
007C3334 00897e08897e         add     byte ptr [ecx + 0x7e89087e], cl
007C333A 0c89                 or      al, 0x89
007C333C 7e10                 jle     0x7c334e
007C333E 897e14               mov     dword ptr [esi + 0x14], edi
007C3341 897e18               mov     dword ptr [esi + 0x18], edi
007C3344 897e1c               mov     dword ptr [esi + 0x1c], edi
007C3347 897e20               mov     dword ptr [esi + 0x20], edi
007C334A c74624ffffffff       mov     dword ptr [esi + 0x24], 0xffffffff
007C3351 e8daa7ffff           call    0x7bdb30
007C3356 8bce                 mov     ecx, esi
007C3358 897dfc               mov     dword ptr [ebp - 4], edi
007C335B c70654f1a700         mov     dword ptr [esi], 0xa7f154
007C3361 e82a4b0400           call    0x807e90
007C3366 8bce                 mov     ecx, esi
007C3368 e8d34e0400           call    0x808240
007C336D 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C3370 5f                   pop     edi
007C3371 8bc6                 mov     eax, esi
007C3373 5e                   pop     esi
007C3374 64890d00000000       mov     dword ptr fs:[0], ecx
007C337B 8be5                 mov     esp, ebp
007C337D 5d                   pop     ebp
007C337E c20400               ret     4
007C3381 cc                   int3    
007C3382 cc                   int3    
007C3383 cc                   int3    
007C3384 cc                   int3    
007C3385 cc                   int3    
007C3386 cc                   int3    
007C3387 cc                   int3    
007C3388 cc                   int3    
007C3389 cc                   int3    
007C338A cc                   int3    
007C338B cc                   int3    
007C338C cc                   int3    
007C338D cc                   int3    
007C338E cc                   int3    
007C338F cc                   int3    
007C3390 55                   push    ebp
007C3391 8bec                 mov     ebp, esp
007C3393 56                   push    esi
007C3394 8bf1                 mov     esi, ecx
007C3396 e825000000           call    0x7c33c0
007C339B f6450801             test    byte ptr [ebp + 8], 1
007C339F 7409                 je      0x7c33aa
007C33A1 56                   push    esi
007C33A2 e8d6691500           call    0x919d7d
007C33A7 83c404               add     esp, 4
007C33AA 8bc6                 mov     eax, esi
007C33AC 5e                   pop     esi
007C33AD 5d                   pop     ebp
007C33AE c20400               ret     4
007C33B1 cc                   int3    
007C33B2 cc                   int3    
007C33B3 cc                   int3    
007C33B4 cc                   int3    
007C33B5 cc                   int3    
007C33B6 cc                   int3    
007C33B7 cc                   int3    
007C33B8 cc                   int3    
007C33B9 cc                   int3    
007C33BA cc                   int3    
007C33BB cc                   int3    
007C33BC cc                   int3    
007C33BD cc                   int3    
007C33BE cc                   int3    
007C33BF cc                   int3    
007C33C0 55                   push    ebp
007C33C1 8bec                 mov     ebp, esp
007C33C3 6aff                 push    -1
007C33C5 68286a9800           push    0x986a28
007C33CA 64a100000000         mov     eax, dword ptr fs:[0]
007C33D0 50                   push    eax
007C33D1 64892500000000       mov     dword ptr fs:[0], esp
007C33D8 51                   push    ecx
007C33D9 56                   push    esi
007C33DA 8bf1                 mov     esi, ecx
007C33DC 57                   push    edi
007C33DD 8975f0               mov     dword ptr [ebp - 0x10], esi
007C33E0 c70654f1a700         mov     dword ptr [esi], 0xa7f154
007C33E6 33ff                 xor     edi, edi
007C33E8 897dfc               mov     dword ptr [ebp - 4], edi
007C33EB e8b04a0400           call    0x807ea0
007C33F0 8b4e08               mov     ecx, dword ptr [esi + 8]
007C33F3 3bcf                 cmp     ecx, edi
007C33F5 c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
007C33FC c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C3402 7406                 je      0x7c340a
007C3404 8b01                 mov     eax, dword ptr [ecx]
007C3406 6a01                 push    1
007C3408 ff10                 call    dword ptr [eax]
007C340A 8b4e0c               mov     ecx, dword ptr [esi + 0xc]
007C340D 3bcf                 cmp     ecx, edi
007C340F 897e08               mov     dword ptr [esi + 8], edi
007C3412 7406                 je      0x7c341a
007C3414 8b11                 mov     edx, dword ptr [ecx]
007C3416 6a01                 push    1
007C3418 ff12                 call    dword ptr [edx]
007C341A 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C341D 897e0c               mov     dword ptr [esi + 0xc], edi
007C3420 5f                   pop     edi
007C3421 5e                   pop     esi
007C3422 64890d00000000       mov     dword ptr fs:[0], ecx
007C3429 8be5                 mov     esp, ebp
007C342B 5d                   pop     ebp
007C342C c3                   ret     
007C342D cc                   int3    
007C342E cc                   int3    
007C342F cc                   int3    
007C3430 55                   push    ebp
007C3431 8bec                 mov     ebp, esp
007C3433 6aff                 push    -1
007C3435 68286a9800           push    0x986a28
007C343A 64a100000000         mov     eax, dword ptr fs:[0]
007C3440 50                   push    eax
007C3441 64892500000000       mov     dword ptr fs:[0], esp
007C3448 51                   push    ecx
007C3449 8b4508               mov     eax, dword ptr [ebp + 8]
007C344C 53                   push    ebx
007C344D 56                   push    esi
007C344E 8bf1                 mov     esi, ecx
007C3450 33db                 xor     ebx, ebx
007C3452 6a16                 push    0x16
007C3454 50                   push    eax
007C3455 8975f0               mov     dword ptr [ebp - 0x10], esi
007C3458 c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C345E c7460471000000       mov     dword ptr [esi + 4], 0x71
007C3465 895e08               mov     dword ptr [esi + 8], ebx
007C3468 895e0c               mov     dword ptr [esi + 0xc], ebx
007C346B 895e10               mov     dword ptr [esi + 0x10], ebx
007C346E 895e14               mov     dword ptr [esi + 0x14], ebx
007C3471 895e18               mov     dword ptr [esi + 0x18], ebx
007C3474 895e1c               mov     dword ptr [esi + 0x1c], ebx
007C3477 895e20               mov     dword ptr [esi + 0x20], ebx
007C347A c74624ffffffff       mov     dword ptr [esi + 0x24], 0xffffffff
007C3481 e8aaa6ffff           call    0x7bdb30
007C3486 c706a4f1a700         mov     dword ptr [esi], 0xa7f1a4
007C348C 885e3c               mov     byte ptr [esi + 0x3c], bl
007C348F 885e3d               mov     byte ptr [esi + 0x3d], bl
007C3492 885e42               mov     byte ptr [esi + 0x42], bl
007C3495 66c7463effff         mov     word ptr [esi + 0x3e], 0xffff
007C349B 66c746400100         mov     word ptr [esi + 0x40], 1
007C34A1 885e43               mov     byte ptr [esi + 0x43], bl
007C34A4 895e44               mov     dword ptr [esi + 0x44], ebx
007C34A7 8bce                 mov     ecx, esi
007C34A9 895dfc               mov     dword ptr [ebp - 4], ebx
007C34AC 895e4c               mov     dword ptr [esi + 0x4c], ebx
007C34AF 895e50               mov     dword ptr [esi + 0x50], ebx
007C34B2 895e54               mov     dword ptr [esi + 0x54], ebx
007C34B5 895e58               mov     dword ptr [esi + 0x58], ebx
007C34B8 885e5d               mov     byte ptr [esi + 0x5d], bl
007C34BB 885e5e               mov     byte ptr [esi + 0x5e], bl
007C34BE 885e5f               mov     byte ptr [esi + 0x5f], bl
007C34C1 e80a4c0000           call    0x7c80d0
007C34C6 8bce                 mov     ecx, esi
007C34C8 e8c3610000           call    0x7c9690
007C34CD 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C34D0 8bc6                 mov     eax, esi
007C34D2 5e                   pop     esi
007C34D3 5b                   pop     ebx
007C34D4 64890d00000000       mov     dword ptr fs:[0], ecx
007C34DB 8be5                 mov     esp, ebp
007C34DD 5d                   pop     ebp
007C34DE c20400               ret     4
007C34E1 cc                   int3    
007C34E2 cc                   int3    
007C34E3 cc                   int3    
007C34E4 cc                   int3    
007C34E5 cc                   int3    
007C34E6 cc                   int3    
007C34E7 cc                   int3    
007C34E8 cc                   int3    
007C34E9 cc                   int3    
007C34EA cc                   int3    
007C34EB cc                   int3    
007C34EC cc                   int3    
007C34ED cc                   int3    
007C34EE cc                   int3    
007C34EF cc                   int3    
007C34F0 55                   push    ebp
007C34F1 8bec                 mov     ebp, esp
007C34F3 56                   push    esi
007C34F4 8bf1                 mov     esi, ecx
007C34F6 e825000000           call    0x7c3520
007C34FB f6450801             test    byte ptr [ebp + 8], 1
007C34FF 7409                 je      0x7c350a
007C3501 56                   push    esi
007C3502 e876681500           call    0x919d7d
007C3507 83c404               add     esp, 4
007C350A 8bc6                 mov     eax, esi
007C350C 5e                   pop     esi
007C350D 5d                   pop     ebp
007C350E c20400               ret     4
007C3511 cc                   int3    
007C3512 cc                   int3    
007C3513 cc                   int3    
007C3514 cc                   int3    
007C3515 cc                   int3    
007C3516 cc                   int3    
007C3517 cc                   int3    
007C3518 cc                   int3    
007C3519 cc                   int3    
007C351A cc                   int3    
007C351B cc                   int3    
007C351C cc                   int3    
007C351D cc                   int3    
007C351E cc                   int3    
007C351F cc                   int3    
007C3520 55                   push    ebp
007C3521 8bec                 mov     ebp, esp
007C3523 6aff                 push    -1
007C3525 68286a9800           push    0x986a28
007C352A 64                   .byte   0x64
007C352B a1                   .byte   0xa1
007C352C 00                   .byte   0x00
REF 007C33E2
007C33B2 cc                   int3    
007C33B3 cc                   int3    
007C33B4 cc                   int3    
007C33B5 cc                   int3    
007C33B6 cc                   int3    
007C33B7 cc                   int3    
007C33B8 cc                   int3    
007C33B9 cc                   int3    
007C33BA cc                   int3    
007C33BB cc                   int3    
007C33BC cc                   int3    
007C33BD cc                   int3    
007C33BE cc                   int3    
007C33BF cc                   int3    
007C33C0 55                   push    ebp
007C33C1 8bec                 mov     ebp, esp
007C33C3 6aff                 push    -1
007C33C5 68286a9800           push    0x986a28
007C33CA 64a100000000         mov     eax, dword ptr fs:[0]
007C33D0 50                   push    eax
007C33D1 64892500000000       mov     dword ptr fs:[0], esp
007C33D8 51                   push    ecx
007C33D9 56                   push    esi
007C33DA 8bf1                 mov     esi, ecx
007C33DC 57                   push    edi
007C33DD 8975f0               mov     dword ptr [ebp - 0x10], esi
007C33E0 c70654f1a700         mov     dword ptr [esi], 0xa7f154
007C33E6 33ff                 xor     edi, edi
007C33E8 897dfc               mov     dword ptr [ebp - 4], edi
007C33EB e8b04a0400           call    0x807ea0
007C33F0 8b4e08               mov     ecx, dword ptr [esi + 8]
007C33F3 3bcf                 cmp     ecx, edi
007C33F5 c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
007C33FC c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C3402 7406                 je      0x7c340a
007C3404 8b01                 mov     eax, dword ptr [ecx]
007C3406 6a01                 push    1
007C3408 ff10                 call    dword ptr [eax]
007C340A 8b4e0c               mov     ecx, dword ptr [esi + 0xc]
007C340D 3bcf                 cmp     ecx, edi
007C340F 897e08               mov     dword ptr [esi + 8], edi
007C3412 7406                 je      0x7c341a
007C3414 8b11                 mov     edx, dword ptr [ecx]
007C3416 6a01                 push    1
007C3418 ff12                 call    dword ptr [edx]
007C341A 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C341D 897e0c               mov     dword ptr [esi + 0xc], edi
007C3420 5f                   pop     edi
007C3421 5e                   pop     esi
007C3422 64890d00000000       mov     dword ptr fs:[0], ecx
007C3429 8be5                 mov     esp, ebp
007C342B 5d                   pop     ebp
007C342C c3                   ret     
007C342D cc                   int3    
007C342E cc                   int3    
007C342F cc                   int3    
007C3430 55                   push    ebp
007C3431 8bec                 mov     ebp, esp
007C3433 6aff                 push    -1
007C3435 68286a9800           push    0x986a28
007C343A 64a100000000         mov     eax, dword ptr fs:[0]
007C3440 50                   push    eax
007C3441 64892500000000       mov     dword ptr fs:[0], esp
007C3448 51                   push    ecx
007C3449 8b4508               mov     eax, dword ptr [ebp + 8]
007C344C 53                   push    ebx
007C344D 56                   push    esi
007C344E 8bf1                 mov     esi, ecx
007C3450 33db                 xor     ebx, ebx
007C3452 6a16                 push    0x16
007C3454 50                   push    eax
007C3455 8975f0               mov     dword ptr [ebp - 0x10], esi
007C3458 c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C345E c7460471000000       mov     dword ptr [esi + 4], 0x71
007C3465 895e08               mov     dword ptr [esi + 8], ebx
007C3468 895e0c               mov     dword ptr [esi + 0xc], ebx
007C346B 895e10               mov     dword ptr [esi + 0x10], ebx
007C346E 895e14               mov     dword ptr [esi + 0x14], ebx
007C3471 895e18               mov     dword ptr [esi + 0x18], ebx
007C3474 895e1c               mov     dword ptr [esi + 0x1c], ebx
007C3477 895e20               mov     dword ptr [esi + 0x20], ebx
007C347A c74624ffffffff       mov     dword ptr [esi + 0x24], 0xffffffff
007C3481 e8aaa6ffff           call    0x7bdb30
007C3486 c706a4f1a700         mov     dword ptr [esi], 0xa7f1a4
007C348C 885e3c               mov     byte ptr [esi + 0x3c], bl
007C348F 885e3d               mov     byte ptr [esi + 0x3d], bl
007C3492 885e42               mov     byte ptr [esi + 0x42], bl
007C3495 66c7463effff         mov     word ptr [esi + 0x3e], 0xffff
007C349B 66c746400100         mov     word ptr [esi + 0x40], 1
007C34A1 885e43               mov     byte ptr [esi + 0x43], bl
007C34A4 895e44               mov     dword ptr [esi + 0x44], ebx
007C34A7 8bce                 mov     ecx, esi
007C34A9 895dfc               mov     dword ptr [ebp - 4], ebx
007C34AC 895e4c               mov     dword ptr [esi + 0x4c], ebx
007C34AF 895e50               mov     dword ptr [esi + 0x50], ebx
007C34B2 895e54               mov     dword ptr [esi + 0x54], ebx
007C34B5 895e58               mov     dword ptr [esi + 0x58], ebx
007C34B8 885e5d               mov     byte ptr [esi + 0x5d], bl
007C34BB 885e5e               mov     byte ptr [esi + 0x5e], bl
007C34BE 885e5f               mov     byte ptr [esi + 0x5f], bl
007C34C1 e80a4c0000           call    0x7c80d0
007C34C6 8bce                 mov     ecx, esi
007C34C8 e8c3610000           call    0x7c9690
007C34CD 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C34D0 8bc6                 mov     eax, esi
007C34D2 5e                   pop     esi
007C34D3 5b                   pop     ebx
007C34D4 64890d00000000       mov     dword ptr fs:[0], ecx
007C34DB 8be5                 mov     esp, ebp
007C34DD 5d                   pop     ebp
007C34DE c20400               ret     4
007C34E1 cc                   int3    
007C34E2 cc                   int3    
007C34E3 cc                   int3    
007C34E4 cc                   int3    
007C34E5 cc                   int3    
007C34E6 cc                   int3    
007C34E7 cc                   int3    
007C34E8 cc                   int3    
007C34E9 cc                   int3    
007C34EA cc                   int3    
007C34EB cc                   int3    
007C34EC cc                   int3    
007C34ED cc                   int3    
007C34EE cc                   int3    
007C34EF cc                   int3    
007C34F0 55                   push    ebp
007C34F1 8bec                 mov     ebp, esp
007C34F3 56                   push    esi
007C34F4 8bf1                 mov     esi, ecx
007C34F6 e825000000           call    0x7c3520
007C34FB f6450801             test    byte ptr [ebp + 8], 1
007C34FF 7409                 je      0x7c350a
007C3501 56                   push    esi
007C3502 e876681500           call    0x919d7d
007C3507 83c404               add     esp, 4
007C350A 8bc6                 mov     eax, esi
007C350C 5e                   pop     esi
007C350D 5d                   pop     ebp
007C350E c20400               ret     4
007C3511 cc                   int3    
007C3512 cc                   int3    
007C3513 cc                   int3    
007C3514 cc                   int3    
007C3515 cc                   int3    
007C3516 cc                   int3    
007C3517 cc                   int3    
007C3518 cc                   int3    
007C3519 cc                   int3    
007C351A cc                   int3    
007C351B cc                   int3    
007C351C cc                   int3    
007C351D cc                   int3    
007C351E cc                   int3    
007C351F cc                   int3    
007C3520 55                   push    ebp
007C3521 8bec                 mov     ebp, esp
007C3523 6aff                 push    -1
007C3525 68286a9800           push    0x986a28
007C352A 64a100000000         mov     eax, dword ptr fs:[0]
007C3530 50                   push    eax
007C3531 64892500000000       mov     dword ptr fs:[0], esp
007C3538 51                   push    ecx
007C3539 56                   push    esi
007C353A 8bf1                 mov     esi, ecx
007C353C 57                   push    edi
007C353D 8975f0               mov     dword ptr [ebp - 0x10], esi
007C3540 c706a4f1a700         mov     dword ptr [esi], 0xa7f1a4
007C3546 33ff                 xor     edi, edi
007C3548 897dfc               mov     dword ptr [ebp - 4], edi
007C354B e8d04b0000           call    0x7c8120
007C3550 8b4e08               mov     ecx, dword ptr [esi + 8]
007C3553 3bcf                 cmp     ecx, edi
007C3555 c745fcffffffff       mov     dword ptr [ebp - 4], 0xffffffff
007C355C c706f4dfa700         mov     dword ptr [esi], 0xa7dff4
007C3562 7406                 je      0x7c356a
007C3564 8b01                 mov     eax, dword ptr [ecx]
007C3566 6a01                 push    1
007C3568 ff10                 call    dword ptr [eax]
007C356A 8b4e0c               mov     ecx, dword ptr [esi + 0xc]
007C356D 3bcf                 cmp     ecx, edi
007C356F 897e08               mov     dword ptr [esi + 8], edi
007C3572 7406                 je      0x7c357a
007C3574 8b11                 mov     edx, dword ptr [ecx]
007C3576 6a01                 push    1
007C3578 ff12                 call    dword ptr [edx]
007C357A 8b4df4               mov     ecx, dword ptr [ebp - 0xc]
007C357D 897e0c               mov     dword ptr [esi + 0xc], edi
007C3580 5f                   pop     edi
007C3581 5e                   pop     esi
007C3582 64890d00000000       mov     dword ptr fs:[0], ecx
007C3589 8be5                 mov     esp, ebp
007C358B 5d                   pop     ebp
007C358C c3                   ret     
007C358D cc                   int3    
007C358E cc                   int3    
007C358F cc                   int3    
007C3590 55                   push    ebp
007C3591 8bec                 mov     ebp, esp
007C3593 6aff                 push    -1
007C3595 68286a9800           push    0x986a28
007C359A 64a100000000         mov     eax, dword ptr fs:[0]
007C35A0 50                   push    eax
007C35A1 64892500000000       mov     dword ptr fs:[0], esp
007C35A8 51                   push    ecx
007C35A9 8b4508               mov     eax, dword ptr [ebp + 8]
007C35AC 53                   push    ebx
007C35AD 56                   push    esi
007C35AE 8bf1                 mov     esi, ecx
007C35B0 33db                 xor     ebx, ebx
