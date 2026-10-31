0859C114 55                       push     ebp
0859C115 89e5                     mov      ebp, esp
0859C117 57                       push     edi
0859C118 56                       push     esi
0859C119 53                       push     ebx
0859C11A 81ec6c010000             sub      esp, 0x16c
0859C120 837d0c00                 cmp      dword ptr [ebp + 0xc], 0
0859C124 7536                     jne      0x859c15c
0859C126 c7442410b098cb08         mov      dword ptr [esp + 0x10], 0x8cb98b0
0859C12E c744240c6d050000         mov      dword ptr [esp + 0xc], 0x56d
0859C136 c7442408a0bacb08         mov      dword ptr [esp + 8], 0x8cbbaa0
0859C13E c7442404f597cb08         mov      dword ptr [esp + 4], 0x8cb97f5
0859C146 c7042401000000           mov      dword ptr [esp], 1
0859C14D e8b87a5300               call     0x8ad3c0a ; LogManager::logFormat
0859C152 b801000000               mov      eax, 1
0859C157 e9cb060000               jmp      0x859c827
0859C15C 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C15F 890424                   mov      dword ptr [esp], eax
0859C162 e8a717c9ff               call     0x822d90e ; CParty::GetPartyTelePort
0859C167 8b550c                   mov      edx, dword ptr [ebp + 0xc]
0859C16A 89542404                 mov      dword ptr [esp + 4], edx
0859C16E 890424                   mov      dword ptr [esp], eax
0859C171 e82aff0100               call     0x85bc0a0 ; CPartyTelePort::process_leave_user_at_teleport
0859C176 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C179 05a4020000               add      eax, 0x2a4
0859C17E 890424                   mov      dword ptr [esp], eax
0859C181 e886535100               call     0x8ab150c ; secretshop::SECRET_SHOP_DATA::IsOpen
0859C186 84c0                     test     al, al
0859C188 7428                     je       0x859c1b2
0859C18A 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C18D 890424                   mov      dword ptr [esp], eax
0859C190 e8a3c40e00               call     0x8688638 ; CUser::GetUserPosInParty
0859C195 8b5508                   mov      edx, dword ptr [ebp + 8]
0859C198 81c2a4020000             add      edx, 0x2a4
0859C19E c744240800000000         mov      dword ptr [esp + 8], 0
0859C1A6 89442404                 mov      dword ptr [esp + 4], eax
0859C1AA 891424                   mov      dword ptr [esp], edx
0859C1AD e880535100               call     0x8ab1532 ; secretshop::SECRET_SHOP_DATA::SetBuying
0859C1B2 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C1B5 8b80ac0c0000             mov      eax, dword ptr [eax + 0xcac]
0859C1BB 85c0                     test     eax, eax
0859C1BD 0f84e4000000             je       0x859c2a7
0859C1C3 8d9dccfeffff             lea      ebx, [ebp - 0x134]
0859C1C9 b800000000               mov      eax, 0
0859C1CE ba40000000               mov      edx, 0x40
0859C1D3 89df                     mov      edi, ebx
0859C1D5 89d1                     mov      ecx, edx
0859C1D7 f3ab                     rep stosd dword ptr es:[edi], eax
0859C1D9 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C1DC 8b80d80c0000             mov      eax, dword ptr [eax + 0xcd8]
0859C1E2 89c7                     mov      edi, eax
0859C1E4 8d85ccfeffff             lea      eax, [ebp - 0x134]
0859C1EA 89442404                 mov      dword ptr [esp + 4], eax
0859C1EE 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C1F1 890424                   mov      dword ptr [esp], eax
0859C1F4 e8e5890100               call     0x85b4bde ; CParty::_getMemberNames
0859C1F9 89c6                     mov      esi, eax
0859C1FB 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C1FE 05240b0000               add      eax, 0xb24
0859C203 890424                   mov      dword ptr [esp], eax
0859C206 e811d6b5ff               call     0x80f981c ; CBattle_Field::get_dungeon_diff
0859C20B 89c3                     mov      ebx, eax
0859C20D 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C210 8b80ac0c0000             mov      eax, dword ptr [eax + 0xcac]
0859C216 890424                   mov      dword ptr [esp], eax
0859C219 e88893baff               call     0x81455a6 ; CDungeon::GetDungeonName
0859C21E 8b550c                   mov      edx, dword ptr [ebp + 0xc]
0859C221 81c200970700             add      edx, 0x79700
0859C227 897c2410                 mov      dword ptr [esp + 0x10], edi
0859C22B 8974240c                 mov      dword ptr [esp + 0xc], esi
0859C22F 895c2408                 mov      dword ptr [esp + 8], ebx
0859C233 89442404                 mov      dword ptr [esp + 4], eax
0859C237 891424                   mov      dword ptr [esp], edx
0859C23A e89d870e00               call     0x86849dc ; cUserHistoryLog::LeaveDungeon
0859C23F 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C242 890424                   mov      dword ptr [esp], eax
0859C245 e8c815c9ff               call     0x822d812 ; CParty::IsSinglePlay
0859C24A 84c0                     test     al, al
0859C24C 7416                     je       0x859c264
0859C24E 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C251 890424                   mov      dword ptr [esp], eax
0859C254 e883d90a00               call     0x8649bdc ; CUser::isDungeonClearHackCheck
0859C259 84c0                     test     al, al
0859C25B 7407                     je       0x859c264
0859C25D b801000000               mov      eax, 1
0859C262 eb05                     jmp      0x859c269
0859C264 b800000000               mov      eax, 0
0859C269 84c0                     test     al, al
0859C26B 743a                     je       0x859c2a7
0859C26D 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C270 890424                   mov      dword ptr [esp], eax
0859C273 e8e2d6b5ff               call     0x80f995a ; CUser::getHackAnalyzer
0859C278 c744241400000000         mov      dword ptr [esp + 0x14], 0
0859C280 c744241000000000         mov      dword ptr [esp + 0x10], 0
0859C288 c744240c01000000         mov      dword ptr [esp + 0xc], 1
0859C290 c74424089c010000         mov      dword ptr [esp + 8], 0x19c
0859C298 8b550c                   mov      edx, dword ptr [ebp + 0xc]
0859C29B 89542404                 mov      dword ptr [esp + 4], edx
0859C29F 890424                   mov      dword ptr [esp], eax
0859C2A2 e8d7c9b5ff               call     0x80f8c7e ; WongWork::CHackAnalyzer::addServerHackCnt
0859C2A7 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C2AA 890424                   mov      dword ptr [esp], eax
0859C2AD e87616c9ff               call     0x822d928 ; CParty::get_quick_party_index
0859C2B2 83f8ff                   cmp      eax, -1
0859C2B5 0f95c0                   setne    al
0859C2B8 84c0                     test     al, al
0859C2BA 741c                     je       0x859c2d8
0859C2BC e8cdfeb2ff               call     0x80cc18e ; _Z14G_CGameManagerv
0859C2C1 c744240800000000         mov      dword ptr [esp + 8], 0
0859C2C9 8b5508                   mov      edx, dword ptr [ebp + 8]
0859C2CC 89542404                 mov      dword ptr [esp + 4], edx
0859C2D0 890424                   mov      dword ptr [esp], eax
0859C2D3 e824c1cfff               call     0x82983fc ; CGameManager::CheckOutQuickParty
0859C2D8 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C2DB 890424                   mov      dword ptr [esp], eax
0859C2DE e86f16c9ff               call     0x822d952 ; CParty::is_quick_party
0859C2E3 84c0                     test     al, al
0859C2E5 7442                     je       0x859c329
0859C2E7 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C2EA 89442408                 mov      dword ptr [esp + 8], eax
0859C2EE c744240400000000         mov      dword ptr [esp + 4], 0
0859C2F6 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C2F9 890424                   mov      dword ptr [esp], eax
0859C2FC e83df4ffff               call     0x859b73e ; CParty::send_quick_party_matching_result
0859C301 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C304 890424                   mov      dword ptr [esp], eax
0859C307 e85edeffff               call     0x859a16a ; CParty::get_member_count
0859C30C 83f802                   cmp      eax, 2
0859C30F 0f94c0                   sete     al
0859C312 84c0                     test     al, al
0859C314 7413                     je       0x859c329
0859C316 c744240400000000         mov      dword ptr [esp + 4], 0
0859C31E 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C321 890424                   mov      dword ptr [esp], eax
0859C324 e80d16c9ff               call     0x822d936 ; CParty::set_quick_party
0859C329 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C32C 890424                   mov      dword ptr [esp], eax
0859C32F e836deffff               call     0x859a16a ; CParty::get_member_count
0859C334 8945dc                   mov      dword ptr [ebp - 0x24], eax
0859C337 837ddc01                 cmp      dword ptr [ebp - 0x24], 1
0859C33B 0f8ff4020000             jg       0x859c635
0859C341 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C344 890424                   mov      dword ptr [esp], eax
0859C347 e8023bc9ff               call     0x822fe4e ; CUser::GetPartyIndex
0859C34C 0fbff8                   movsx    edi, ax
0859C34F 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C352 890424                   mov      dword ptr [esp], eax
0859C355 e832e0b3ff               call     0x80da38c ; CUser::get_state
0859C35A 89c6                     mov      esi, eax
0859C35C 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C35F 890424                   mov      dword ptr [esp], eax
0859C362 e807e0b3ff               call     0x80da36e ; CUser::get_acc_id
0859C367 c744240400000000         mov      dword ptr [esp + 4], 0
0859C36F 890424                   mov      dword ptr [esp], eax
0859C372 e8d4ccb6ff               call     0x810904b ; _Z14NumberToStringji
0859C377 89c3                     mov      ebx, eax
0859C379 c744240c00000000         mov      dword ptr [esp + 0xc], 0
0859C381 c7442408b8050000         mov      dword ptr [esp + 8], 0x5b8
0859C389 c7442404a0bacb08         mov      dword ptr [esp + 4], 0x8cbbaa0
0859C391 8d45cc                   lea      eax, [ebp - 0x34]
0859C394 890424                   mov      dword ptr [esp], eax
0859C397 e87c33fbff               call     0x854f718 ; cMyTrace
0859C39C 897c2410                 mov      dword ptr [esp + 0x10], edi
0859C3A0 8974240c                 mov      dword ptr [esp + 0xc], esi
0859C3A4 895c2408                 mov      dword ptr [esp + 8], ebx
0859C3A8 c74424044898cb08         mov      dword ptr [esp + 4], 0x8cb9848
0859C3B0 8d45cc                   lea      eax, [ebp - 0x34]
0859C3B3 890424                   mov      dword ptr [esp], eax
0859C3B6 e8cd33fbff               call     0x854f788 ; cMyTrace
0859C3BB 837ddc00                 cmp      dword ptr [ebp - 0x24], 0
0859C3BF 0f8fd4010000             jg       0x859c599
0859C3C5 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C3C8 0fb6406a                 movzx    eax, byte ptr [eax + 0x6a]
0859C3CC 0fbed8                   movsx    ebx, al
0859C3CF 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C3D2 8b4864                   mov      ecx, dword ptr [eax + 0x64]
0859C3D5 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C3D8 0fb7803e010000           movzx    eax, word ptr [eax + 0x13e]
0859C3DF 0fb7d0                   movzx    edx, ax
0859C3E2 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C3E5 8b4074                   mov      eax, dword ptr [eax + 0x74]
0859C3E8 895c2424                 mov      dword ptr [esp + 0x24], ebx
0859C3EC 894c2420                 mov      dword ptr [esp + 0x20], ecx
0859C3F0 8954241c                 mov      dword ptr [esp + 0x1c], edx
0859C3F4 89442418                 mov      dword ptr [esp + 0x18], eax
0859C3F8 8b45dc                   mov      eax, dword ptr [ebp - 0x24]
0859C3FB 89442414                 mov      dword ptr [esp + 0x14], eax
0859C3FF c7442410d498cb08         mov      dword ptr [esp + 0x10], 0x8cb98d4
0859C407 c744240cc5050000         mov      dword ptr [esp + 0xc], 0x5c5
0859C40F c7442408a0bacb08         mov      dword ptr [esp + 8], 0x8cbbaa0
0859C417 c7442404f597cb08         mov      dword ptr [esp + 4], 0x8cb97f5
0859C41F c7042401000000           mov      dword ptr [esp], 1
0859C426 e8df775300               call     0x8ad3c0a ; LogManager::logFormat
0859C42B c745e000000000           mov      dword ptr [ebp - 0x20], 0
0859C432 e953010000               jmp      0x859c58a
0859C437 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C43A 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C43D 89d0                     mov      eax, edx
0859C43F 01c0                     add      eax, eax
0859C441 01d0                     add      eax, edx
0859C443 c1e003                   shl      eax, 3
0859C446 8d0401                   lea      eax, [ecx + eax]
0859C449 83c078                   add      eax, 0x78
0859C44C 8b00                     mov      eax, dword ptr [eax]
0859C44E 85c0                     test     eax, eax
0859C450 7553                     jne      0x859c4a5
0859C452 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C455 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C458 89d0                     mov      eax, edx
0859C45A 01c0                     add      eax, eax
0859C45C 01d0                     add      eax, edx
0859C45E c1e003                   shl      eax, 3
0859C461 8d0401                   lea      eax, [ecx + eax]
0859C464 83e880                   sub      eax, -0x80
0859C467 8b00                     mov      eax, dword ptr [eax]
0859C469 89442418                 mov      dword ptr [esp + 0x18], eax
0859C46D 8b45e0                   mov      eax, dword ptr [ebp - 0x20]
0859C470 89442414                 mov      dword ptr [esp + 0x14], eax
0859C474 c74424103499cb08         mov      dword ptr [esp + 0x10], 0x8cb9934
0859C47C c744240ccd050000         mov      dword ptr [esp + 0xc], 0x5cd
0859C484 c7442408a0bacb08         mov      dword ptr [esp + 8], 0x8cbbaa0
0859C48C c7442404f597cb08         mov      dword ptr [esp + 4], 0x8cb97f5
0859C494 c7042401000000           mov      dword ptr [esp], 1
0859C49B e86a775300               call     0x8ad3c0a ; LogManager::logFormat
0859C4A0 e9e1000000               jmp      0x859c586
0859C4A5 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C4A8 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C4AB 89d0                     mov      eax, edx
0859C4AD 01c0                     add      eax, eax
0859C4AF 01d0                     add      eax, edx
0859C4B1 c1e003                   shl      eax, 3
0859C4B4 8d0401                   lea      eax, [ecx + eax]
0859C4B7 83c078                   add      eax, 0x78
0859C4BA 8b00                     mov      eax, dword ptr [eax]
0859C4BC 890424                   mov      dword ptr [esp], eax
0859C4BF e88af7b2ff               call     0x80cbc4e ; CUserCharacInfo::getCurCharacNo
0859C4C4 89c6                     mov      esi, eax
0859C4C6 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C4C9 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C4CC 89d0                     mov      eax, edx
0859C4CE 01c0                     add      eax, eax
0859C4D0 01d0                     add      eax, edx
0859C4D2 c1e003                   shl      eax, 3
0859C4D5 8d0401                   lea      eax, [ecx + eax]
0859C4D8 83e880                   sub      eax, -0x80
0859C4DB 8b38                     mov      edi, dword ptr [eax]
0859C4DD 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C4E0 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C4E3 89d0                     mov      eax, edx
0859C4E5 01c0                     add      eax, eax
0859C4E7 01d0                     add      eax, edx
0859C4E9 c1e003                   shl      eax, 3
0859C4EC 8d0401                   lea      eax, [ecx + eax]
0859C4EF 83c078                   add      eax, 0x78
0859C4F2 8b00                     mov      eax, dword ptr [eax]
0859C4F4 890424                   mov      dword ptr [esp], eax
0859C4F7 e89ac7b2ff               call     0x80c8c96 ; CUser::GetUID
0859C4FC 89c3                     mov      ebx, eax
0859C4FE 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C501 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C504 89d0                     mov      eax, edx
0859C506 01c0                     add      eax, eax
0859C508 01d0                     add      eax, edx
0859C50A c1e003                   shl      eax, 3
0859C50D 8d0401                   lea      eax, [ecx + eax]
0859C510 83c078                   add      eax, 0x78
0859C513 8b00                     mov      eax, dword ptr [eax]
0859C515 890424                   mov      dword ptr [esp], eax
0859C518 e86fdeb3ff               call     0x80da38c ; CUser::get_state
0859C51D 89c1                     mov      ecx, eax
0859C51F 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
0859C522 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C525 8985c4feffff             mov      dword ptr [ebp - 0x13c], eax
0859C52B 89d0                     mov      eax, edx
0859C52D 01c0                     add      eax, eax
0859C52F 01d0                     add      eax, edx
0859C531 c1e003                   shl      eax, 3
0859C534 0385c4feffff             add      eax, dword ptr [ebp - 0x13c]
0859C53A 83c078                   add      eax, 0x78
0859C53D 8b00                     mov      eax, dword ptr [eax]
0859C53F 89742428                 mov      dword ptr [esp + 0x28], esi
0859C543 897c2424                 mov      dword ptr [esp + 0x24], edi
0859C547 895c2420                 mov      dword ptr [esp + 0x20], ebx
0859C54B 894c241c                 mov      dword ptr [esp + 0x1c], ecx
0859C54F 89442418                 mov      dword ptr [esp + 0x18], eax
0859C553 8b45e0                   mov      eax, dword ptr [ebp - 0x20]
0859C556 89442414                 mov      dword ptr [esp + 0x14], eax
0859C55A c74424106c99cb08         mov      dword ptr [esp + 0x10], 0x8cb996c
0859C562 c744240cd8050000         mov      dword ptr [esp + 0xc], 0x5d8
0859C56A c7442408a0bacb08         mov      dword ptr [esp + 8], 0x8cbbaa0
0859C572 c7442404f597cb08         mov      dword ptr [esp + 4], 0x8cb97f5
0859C57A c7042401000000           mov      dword ptr [esp], 1
0859C581 e884765300               call     0x8ad3c0a ; LogManager::logFormat
0859C586 8345e001                 add      dword ptr [ebp - 0x20], 1
0859C58A 837de003                 cmp      dword ptr [ebp - 0x20], 3
0859C58E 0f9ec0                   setle    al
0859C591 84c0                     test     al, al
0859C593 0f859efeffff             jne      0x859c437
0859C599 c7442404ffffffff         mov      dword ptr [esp + 4], 0xffffffff
0859C5A1 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C5A4 890424                   mov      dword ptr [esp], eax
0859C5A7 e8ea370200               call     0x85bfd96 ; CUser::SetPartyIndex
0859C5AC 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C5AF c744240400000000         mov      dword ptr [esp + 4], 0
0859C5B7 890424                   mov      dword ptr [esp], eax
0859C5BA e827d4ecff               call     0x84699e6 ; CUserCharacInfo::set_charac_party_bonus_exp
0859C5BF 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C5C2 c744240400000000         mov      dword ptr [esp + 4], 0
0859C5CA 890424                   mov      dword ptr [esp], eax
0859C5CD e84ed4ecff               call     0x8469a20 ; CUserCharacInfo::set_charac_member_bonus_exp
0859C5D2 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C5D5 c744240400000000         mov      dword ptr [esp + 4], 0
0859C5DD 890424                   mov      dword ptr [esp], eax
0859C5E0 e81dd4ecff               call     0x8469a02 ; CUserCharacInfo::set_charac_fatigue_buf_bonus_exp
0859C5E5 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C5E8 c744240400000000         mov      dword ptr [esp + 4], 0
0859C5F0 890424                   mov      dword ptr [esp], eax
0859C5F3 e846d4ecff               call     0x8469a3e ; CUserCharacInfo::set_charac_seria_buf_bonus_exp
0859C5F8 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C5FB 890424                   mov      dword ptr [esp], eax
0859C5FE e8c7480f00               call     0x8690eca ; CUser::getMoveSpace
0859C603 83f801                   cmp      eax, 1
0859C606 0f95c0                   setne    al
0859C609 84c0                     test     al, al
0859C60B 7413                     je       0x859c620
0859C60D c744240403000000         mov      dword ptr [esp + 4], 3
0859C615 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C618 890424                   mov      dword ptr [esp], eax
0859C61B e892270e00               call     0x867edb2 ; CUser::set_state
0859C620 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C623 890424                   mov      dword ptr [esp], eax
0859C626 e821e7ffff               call     0x859ad4c ; CParty::destroy
0859C62B b800000000               mov      eax, 0
0859C630 e9f2010000               jmp      0x859c827
0859C635 837d0c00                 cmp      dword ptr [ebp + 0xc], 0
0859C639 7426                     je       0x859c661
0859C63B 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C63E 890424                   mov      dword ptr [esp], eax
0859C641 e814d3b5ff               call     0x80f995a ; CUser::getHackAnalyzer
0859C646 890424                   mov      dword ptr [esp], eax
0859C649 e832bab5ff               call     0x80f8080 ; WongWork::CHackAnalyzer::reportHackInfo
0859C64E 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C651 890424                   mov      dword ptr [esp], eax
0859C654 e801d3b5ff               call     0x80f995a ; CUser::getHackAnalyzer
0859C659 890424                   mov      dword ptr [esp], eax
0859C65C e845bbb5ff               call     0x80f81a6 ; WongWork::CHackAnalyzer::resetHackInfo
0859C661 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C664 8b4074                   mov      eax, dword ptr [eax + 0x74]
0859C667 3b450c                   cmp      eax, dword ptr [ebp + 0xc]
0859C66A 7555                     jne      0x859c6c1
0859C66C 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C66F 890424                   mov      dword ptr [esp], eax
0859C672 e8bb010000               call     0x859c832 ; CParty::change_manager
0859C677 8b5d10                   mov      ebx, dword ptr [ebp + 0x10]
0859C67A e828ddb3ff               call     0x80da3a7 ; _Z11G_GameWorldv
0859C67F 895c2408                 mov      dword ptr [esp + 8], ebx
0859C683 8b5508                   mov      edx, dword ptr [ebp + 8]
0859C686 89542404                 mov      dword ptr [esp + 4], edx
0859C68A 890424                   mov      dword ptr [esp], eax
0859C68D e8f8c01200               call     0x86c878a ; GameWorld::send_party_info_to_all
0859C692 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C695 890424                   mov      dword ptr [esp], eax
0859C698 e887060000               call     0x859cd24 ; CParty::send_party_realtime_info
0859C69D 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C6A0 890424                   mov      dword ptr [esp], eax
0859C6A3 e8f4fe0000               call     0x85ac59c ; CParty::checkInoutConditionDungeon
0859C6A8 89442404                 mov      dword ptr [esp + 4], eax
0859C6AC 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C6AF 890424                   mov      dword ptr [esp], eax
0859C6B2 e8a7020100               call     0x85ac95e ; CParty::sendInoutConditionDungeon
0859C6B7 b800000000               mov      eax, 0
0859C6BC e966010000               jmp      0x859c827
0859C6C1 c745e400000000           mov      dword ptr [ebp - 0x1c], 0
0859C6C8 e946010000               jmp      0x859c813
0859C6CD 8b45e4                   mov      eax, dword ptr [ebp - 0x1c]
0859C6D0 89442404                 mov      dword ptr [esp + 4], eax
0859C6D4 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C6D7 890424                   mov      dword ptr [esp], eax
0859C6DA e833860100               call     0x85b4d12 ; CParty::_checkValidUser
0859C6DF 83f001                   xor      eax, 1
0859C6E2 84c0                     test     al, al
0859C6E4 0f8524010000             jne      0x859c80e
0859C6EA 8b55e4                   mov      edx, dword ptr [ebp - 0x1c]
0859C6ED 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C6F0 89d0                     mov      eax, edx
0859C6F2 01c0                     add      eax, eax
0859C6F4 01d0                     add      eax, edx
0859C6F6 c1e003                   shl      eax, 3
0859C6F9 8d0401                   lea      eax, [ecx + eax]
0859C6FC 83c078                   add      eax, 0x78
0859C6FF 8b00                     mov      eax, dword ptr [eax]
0859C701 3b450c                   cmp      eax, dword ptr [ebp + 0xc]
0859C704 0f8505010000             jne      0x859c80f
0859C70A 8b55e4                   mov      edx, dword ptr [ebp - 0x1c]
0859C70D 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C710 89d0                     mov      eax, edx
0859C712 01c0                     add      eax, eax
0859C714 01d0                     add      eax, edx
0859C716 c1e003                   shl      eax, 3
0859C719 8d0401                   lea      eax, [ecx + eax]
0859C71C 83c078                   add      eax, 0x78
0859C71F c70000000000             mov      dword ptr [eax], 0
0859C725 8b55e4                   mov      edx, dword ptr [ebp - 0x1c]
0859C728 8b4d08                   mov      ecx, dword ptr [ebp + 8]
0859C72B 89d0                     mov      eax, edx
0859C72D 01c0                     add      eax, eax
0859C72F 01d0                     add      eax, edx
0859C731 c1e003                   shl      eax, 3
0859C734 8d0401                   lea      eax, [ecx + eax]
0859C737 83e880                   sub      eax, -0x80
0859C73A c70000000000             mov      dword ptr [eax], 0
0859C740 c7442404ffffffff         mov      dword ptr [esp + 4], 0xffffffff
0859C748 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C74B 890424                   mov      dword ptr [esp], eax
0859C74E e843360200               call     0x85bfd96 ; CUser::SetPartyIndex
0859C753 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C756 c744240400000000         mov      dword ptr [esp + 4], 0
0859C75E 890424                   mov      dword ptr [esp], eax
0859C761 e880d2ecff               call     0x84699e6 ; CUserCharacInfo::set_charac_party_bonus_exp
0859C766 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C769 c744240400000000         mov      dword ptr [esp + 4], 0
0859C771 890424                   mov      dword ptr [esp], eax
0859C774 e8a7d2ecff               call     0x8469a20 ; CUserCharacInfo::set_charac_member_bonus_exp
0859C779 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C77C c744240400000000         mov      dword ptr [esp + 4], 0
0859C784 890424                   mov      dword ptr [esp], eax
0859C787 e876d2ecff               call     0x8469a02 ; CUserCharacInfo::set_charac_fatigue_buf_bonus_exp
0859C78C 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C78F c744240400000000         mov      dword ptr [esp + 4], 0
0859C797 890424                   mov      dword ptr [esp], eax
0859C79A e89fd2ecff               call     0x8469a3e ; CUserCharacInfo::set_charac_seria_buf_bonus_exp
0859C79F 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C7A2 890424                   mov      dword ptr [esp], eax
0859C7A5 e820470f00               call     0x8690eca ; CUser::getMoveSpace
0859C7AA 83f801                   cmp      eax, 1
0859C7AD 0f95c0                   setne    al
0859C7B0 84c0                     test     al, al
0859C7B2 7413                     je       0x859c7c7
0859C7B4 c744240403000000         mov      dword ptr [esp + 4], 3
0859C7BC 8b450c                   mov      eax, dword ptr [ebp + 0xc]
0859C7BF 890424                   mov      dword ptr [esp], eax
0859C7C2 e8eb250e00               call     0x867edb2 ; CUser::set_state
0859C7C7 8b5d10                   mov      ebx, dword ptr [ebp + 0x10]
0859C7CA e8d8dbb3ff               call     0x80da3a7 ; _Z11G_GameWorldv
0859C7CF 895c2408                 mov      dword ptr [esp + 8], ebx
0859C7D3 8b5508                   mov      edx, dword ptr [ebp + 8]
0859C7D6 89542404                 mov      dword ptr [esp + 4], edx
0859C7DA 890424                   mov      dword ptr [esp], eax
0859C7DD e8a8bf1200               call     0x86c878a ; GameWorld::send_party_info_to_all
0859C7E2 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C7E5 890424                   mov      dword ptr [esp], eax
0859C7E8 e837050000               call     0x859cd24 ; CParty::send_party_realtime_info
0859C7ED 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C7F0 890424                   mov      dword ptr [esp], eax
0859C7F3 e8a4fd0000               call     0x85ac59c ; CParty::checkInoutConditionDungeon
0859C7F8 89442404                 mov      dword ptr [esp + 4], eax
0859C7FC 8b4508                   mov      eax, dword ptr [ebp + 8]
0859C7FF 890424                   mov      dword ptr [esp], eax
0859C802 e857010100               call     0x85ac95e ; CParty::sendInoutConditionDungeon
0859C807 b800000000               mov      eax, 0
0859C80C eb19                     jmp      0x859c827
0859C80E 90                       nop      
0859C80F 8345e401                 add      dword ptr [ebp - 0x1c], 1
0859C813 837de403                 cmp      dword ptr [ebp - 0x1c], 3
0859C817 0f9ec0                   setle    al
0859C81A 84c0                     test     al, al
0859C81C 0f85abfeffff             jne      0x859c6cd
0859C822 b801000000               mov      eax, 1
0859C827 81c46c010000             add      esp, 0x16c
0859C82D 5b                       pop      ebx
0859C82E 5e                       pop      esi
0859C82F 5f                       pop      edi
0859C830 5d                       pop      ebp
0859C831 c3                       ret      
