085B2BAA 55                       push     ebp
085B2BAB 89e5                     mov      ebp, esp
085B2BAD 57                       push     edi
085B2BAE 56                       push     esi
085B2BAF 53                       push     ebx
085B2BB0 81ec7c010000             sub      esp, 0x17c
085B2BB6 8b4d10                   mov      ecx, dword ptr [ebp + 0x10]
085B2BB9 8b5514                   mov      edx, dword ptr [ebp + 0x14]
085B2BBC 8b4518                   mov      eax, dword ptr [ebp + 0x18]
085B2BBF 888da4feffff             mov      byte ptr [ebp - 0x15c], cl
085B2BC5 8895a0feffff             mov      byte ptr [ebp - 0x160], dl
085B2BCB 88859cfeffff             mov      byte ptr [ebp - 0x164], al
085B2BD1 c645c300                 mov      byte ptr [ebp - 0x3d], 0
085B2BD5 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2BD8 8b80ac0c0000             mov      eax, dword ptr [eax + 0xcac]
085B2BDE 8945c4                   mov      dword ptr [ebp - 0x3c], eax
085B2BE1 837dc400                 cmp      dword ptr [ebp - 0x3c], 0
085B2BE5 7425                     je       0x85b2c0c
085B2BE7 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2BEA 0fb6809f080000           movzx    eax, byte ptr [eax + 0x89f]
085B2BF1 84c0                     test     al, al
085B2BF3 7e04                     jle      0x85b2bf9
085B2BF5 c645c301                 mov      byte ptr [ebp - 0x3d], 1
085B2BF9 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2BFC 890424                   mov      dword ptr [esp], eax
085B2BFF e8f688c7ff               call     0x822b4fa ; CDungeon::isTowerOfDespairDungeon
085B2C04 84c0                     test     al, al
085B2C06 7404                     je       0x85b2c0c
085B2C08 c645c301                 mov      byte ptr [ebp - 0x3d], 1
085B2C0C 837dc400                 cmp      dword ptr [ebp - 0x3c], 0
085B2C10 7443                     je       0x85b2c55
085B2C12 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2C15 890424                   mov      dword ptr [esp], eax
085B2C18 e81b8dcbff               call     0x826b938 ; CDungeon::get_dimension_possible
085B2C1D 84c0                     test     al, al
085B2C1F 7f1d                     jg       0x85b2c3e
085B2C21 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2C24 0fb6809c080000           movzx    eax, byte ptr [eax + 0x89c]
085B2C2B 84c0                     test     al, al
085B2C2D 750f                     jne      0x85b2c3e
085B2C2F 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2C32 890424                   mov      dword ptr [esp], eax
085B2C35 e8d025dbff               call     0x836520a ; CDungeon::isTournamentDungeon
085B2C3A 84c0                     test     al, al
085B2C3C 7407                     je       0x85b2c45
085B2C3E b801000000               mov      eax, 1
085B2C43 eb05                     jmp      0x85b2c4a
085B2C45 b800000000               mov      eax, 0
085B2C4A 84c0                     test     al, al
085B2C4C 7407                     je       0x85b2c55
085B2C4E c685a4feffff00           mov      byte ptr [ebp - 0x15c], 0
085B2C55 837dc400                 cmp      dword ptr [ebp - 0x3c], 0
085B2C59 7415                     je       0x85b2c70
085B2C5B 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2C5E 0fb6805c080000           movzx    eax, byte ptr [eax + 0x85c]
085B2C65 84c0                     test     al, al
085B2C67 7407                     je       0x85b2c70
085B2C69 c685a4feffff00           mov      byte ptr [ebp - 0x15c], 0
085B2C70 837dc400                 cmp      dword ptr [ebp - 0x3c], 0
085B2C74 7415                     je       0x85b2c8b
085B2C76 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2C79 0fb6807a080000           movzx    eax, byte ptr [eax + 0x87a]
085B2C80 84c0                     test     al, al
085B2C82 7407                     je       0x85b2c8b
085B2C84 c685a4feffff00           mov      byte ptr [ebp - 0x15c], 0
085B2C8B c645cb00                 mov      byte ptr [ebp - 0x35], 0
085B2C8F 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2C92 8b80d80c0000             mov      eax, dword ptr [eax + 0xcd8]
085B2C98 83f801                   cmp      eax, 1
085B2C9B 7504                     jne      0x85b2ca1
085B2C9D c645cb01                 mov      byte ptr [ebp - 0x35], 1
085B2CA1 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2CA4 890424                   mov      dword ptr [esp], eax
085B2CA7 e8ea300900               call     0x8645d96 ; CUserCharacInfo::reset_level_before_dungeon
085B2CAC 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2CAF 057c0c0000               add      eax, 0xc7c
085B2CB4 c7442404c0b4cb08         mov      dword ptr [esp + 4], 0x8cbb4c0
085B2CBC 890424                   mov      dword ptr [esp], eax
085B2CBF e86ec0d4ff               call     0x82fed32 ; CDungeonClearTracer::Trace
085B2CC4 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2CC7 890424                   mov      dword ptr [esp], eax
085B2CCA e89f640000               call     0x85b916e ; CParty::Reset_party_overlapped_drop_ratio
085B2CCF 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2CD2 890424                   mov      dword ptr [esp], eax
085B2CD5 e89074feff               call     0x859a16a ; CParty::get_member_count
085B2CDA 8945cc                   mov      dword ptr [ebp - 0x34], eax
085B2CDD 837d0c00                 cmp      dword ptr [ebp + 0xc], 0
085B2CE1 7421                     je       0x85b2d04
085B2CE3 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2CE6 890424                   mov      dword ptr [esp], eax
085B2CE9 e844d7b6ff               call     0x8120432 ; CUserCharacInfo::getCurCharacR
085B2CEE 85c0                     test     eax, eax
085B2CF0 7412                     je       0x85b2d04
085B2CF2 0fb645cb                 movzx    eax, byte ptr [ebp - 0x35]
085B2CF6 83f001                   xor      eax, 1
085B2CF9 84c0                     test     al, al
085B2CFB 7407                     je       0x85b2d04
085B2CFD b801000000               mov      eax, 1
085B2D02 eb05                     jmp      0x85b2d09
085B2D04 b800000000               mov      eax, 0
085B2D09 84c0                     test     al, al
085B2D0B 7418                     je       0x85b2d25
085B2D0D 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2D10 890424                   mov      dword ptr [esp], eax
085B2D13 e81ad7b6ff               call     0x8120432 ; CUserCharacInfo::getCurCharacR
085B2D18 0fb74027                 movzx    eax, word ptr [eax + 0x27]
085B2D1C 98                       cwde     
085B2D1D 890424                   mov      dword ptr [esp], eax
085B2D20 e88f8b0500               call     0x860b8b4 ; Statistics::DungeonFail
085B2D25 c745dc00000000           mov      dword ptr [ebp - 0x24], 0
085B2D2C eb56                     jmp      0x85b2d84
085B2D2E 8b45dc                   mov      eax, dword ptr [ebp - 0x24]
085B2D31 89442404                 mov      dword ptr [esp + 4], eax
085B2D35 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2D38 890424                   mov      dword ptr [esp], eax
085B2D3B e8d21f0000               call     0x85b4d12 ; CParty::_checkValidUser
085B2D40 83f001                   xor      eax, 1
085B2D43 84c0                     test     al, al
085B2D45 7538                     jne      0x85b2d7f
085B2D47 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2D4A 890424                   mov      dword ptr [esp], eax
085B2D4D e8d6e2b4ff               call     0x8101028 ; CUserCharacInfo::getCurCharacName
085B2D52 89c1                     mov      ecx, eax
085B2D54 8b55dc                   mov      edx, dword ptr [ebp - 0x24]
085B2D57 8b5d08                   mov      ebx, dword ptr [ebp + 8]
085B2D5A 89d0                     mov      eax, edx
085B2D5C 01c0                     add      eax, eax
085B2D5E 01d0                     add      eax, edx
085B2D60 c1e003                   shl      eax, 3
085B2D63 8d0403                   lea      eax, [ebx + eax]
085B2D66 83c078                   add      eax, 0x78
085B2D69 8b00                     mov      eax, dword ptr [eax]
085B2D6B 8b80f8960700             mov      eax, dword ptr [eax + 0x796f8]
085B2D71 894c2404                 mov      dword ptr [esp + 4], ecx
085B2D75 890424                   mov      dword ptr [esp], eax
085B2D78 e8f372f0ff               call     0x84ba070 ; HistoryLog::WriteGiveUp
085B2D7D eb01                     jmp      0x85b2d80
085B2D7F 90                       nop      
085B2D80 8345dc01                 add      dword ptr [ebp - 0x24], 1
085B2D84 837ddc03                 cmp      dword ptr [ebp - 0x24], 3
085B2D88 0f9ec0                   setle    al
085B2D8B 84c0                     test     al, al
085B2D8D 759f                     jne      0x85b2d2e
085B2D8F 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2D92 0fb7804c0c0000           movzx    eax, word ptr [eax + 0xc4c]
085B2D99 6685c0                   test     ax, ax
085B2D9C 7518                     jne      0x85b2db6
085B2D9E 837dc400                 cmp      dword ptr [ebp - 0x3c], 0
085B2DA2 7419                     je       0x85b2dbd
085B2DA4 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2DA7 890424                   mov      dword ptr [esp], eax
085B2DAA e841afb4ff               call     0x80fdcf0 ; CDungeon::get_index
085B2DAF 3d10270000               cmp      eax, 0x2710
085B2DB4 7507                     jne      0x85b2dbd
085B2DB6 b801000000               mov      eax, 1
085B2DBB eb05                     jmp      0x85b2dc2
085B2DBD b800000000               mov      eax, 0
085B2DC2 84c0                     test     al, al
085B2DC4 7407                     je       0x85b2dcd
085B2DC6 c685a4feffff00           mov      byte ptr [ebp - 0x15c], 0
085B2DCD 80bda0feffff00           cmp      byte ptr [ebp - 0x160], 0
085B2DD4 7414                     je       0x85b2dea
085B2DD6 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2DD9 89442404                 mov      dword ptr [esp + 4], eax
085B2DDD 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2DE0 890424                   mov      dword ptr [esp], eax
085B2DE3 e814ca0000               call     0x85bf7fc ; CParty::OnSuccessedDungeonClear
085B2DE8 eb42                     jmp      0x85b2e2c
085B2DEA 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2DED 890424                   mov      dword ptr [esp], eax
085B2DF0 e87573feff               call     0x859a16a ; CParty::get_member_count
085B2DF5 83f801                   cmp      eax, 1
085B2DF8 0f94c0                   sete     al
085B2DFB 84c0                     test     al, al
085B2DFD 741b                     je       0x85b2e1a
085B2DFF 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2E02 890424                   mov      dword ptr [esp], eax
085B2E05 e8a2820000               call     0x85bb0ac ; CParty::CheckHackAverageHitCount
085B2E0A 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2E0D 05bc1a0000               add      eax, 0x1abc
085B2E12 890424                   mov      dword ptr [esp], eax
085B2E15 e8b07cccff               call     0x827aaca ; Secu_HackLogCheckByParty::dungeonFail
085B2E1A 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2E1D 89442404                 mov      dword ptr [esp + 4], eax
085B2E21 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2E24 890424                   mov      dword ptr [esp], eax
085B2E27 e8aec90000               call     0x85bf7da ; CParty::OnFailedDungeonClear
085B2E2C 837d0c00                 cmp      dword ptr [ebp + 0xc], 0
085B2E30 741b                     je       0x85b2e4d
085B2E32 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2E35 05240b0000               add      eax, 0xb24
085B2E3A 890424                   mov      dword ptr [esp], eax
085B2E3D e80ea1c7ff               call     0x822cf50 ; CBattle_Field::check_start_point
085B2E42 84c0                     test     al, al
085B2E44 7407                     je       0x85b2e4d
085B2E46 b801000000               mov      eax, 1
085B2E4B eb05                     jmp      0x85b2e52
085B2E4D b800000000               mov      eax, 0
085B2E52 84c0                     test     al, al
085B2E54 743a                     je       0x85b2e90
085B2E56 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2E59 890424                   mov      dword ptr [esp], eax
085B2E5C e8f96ab4ff               call     0x80f995a ; CUser::getHackAnalyzer
085B2E61 c744241400000000         mov      dword ptr [esp + 0x14], 0
085B2E69 c744241000000000         mov      dword ptr [esp + 0x10], 0
085B2E71 c744240c01000000         mov      dword ptr [esp + 0xc], 1
085B2E79 c744240825030000         mov      dword ptr [esp + 8], 0x325
085B2E81 8b550c                   mov      edx, dword ptr [ebp + 0xc]
085B2E84 89542404                 mov      dword ptr [esp + 4], edx
085B2E88 890424                   mov      dword ptr [esp], eax
085B2E8B e8ee5db4ff               call     0x80f8c7e ; WongWork::CHackAnalyzer::addServerHackCnt
085B2E90 c744240403000000         mov      dword ptr [esp + 4], 3
085B2E98 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2E9B 890424                   mov      dword ptr [esp], eax
085B2E9E e87d060000               call     0x85b3520 ; CParty::SaveCoin
085B2EA3 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2EA6 890424                   mov      dword ptr [esp], eax
085B2EA9 e86e29b9ff               call     0x814581c ; CParty::GetEPLPState
085B2EAE 8845d3                   mov      byte ptr [ebp - 0x2d], al
085B2EB1 807dd301                 cmp      byte ptr [ebp - 0x2d], 1
085B2EB5 7543                     jne      0x85b2efa
085B2EB7 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2EBA 890424                   mov      dword ptr [esp], eax
085B2EBD e8be28b9ff               call     0x8145780 ; CParty::getManager
085B2EC2 890424                   mov      dword ptr [esp], eax
085B2EC5 e800e00d00               call     0x8690eca ; CUser::getMoveSpace
085B2ECA 83f801                   cmp      eax, 1
085B2ECD 0f95c0                   setne    al
085B2ED0 84c0                     test     al, al
085B2ED2 7426                     je       0x85b2efa
085B2ED4 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B2ED7 890424                   mov      dword ptr [esp], eax
085B2EDA e811aeb4ff               call     0x80fdcf0 ; CDungeon::get_index
085B2EDF 89c3                     mov      ebx, eax
085B2EE1 e875b20500               call     0x860e15b ; _Z29GetInstanceDungeonEntranceLogv
085B2EE6 c744240800000000         mov      dword ptr [esp + 8], 0
085B2EEE 895c2404                 mov      dword ptr [esp + 4], ebx
085B2EF2 890424                   mov      dword ptr [esp], eax
085B2EF5 e82ab40500               call     0x860e324 ; CDungeonEntranceLog::DecrementDungeonEntrance
085B2EFA 8d45b4                   lea      eax, [ebp - 0x4c]
085B2EFD 890424                   mov      dword ptr [esp], eax
085B2F00 e847aefdff               call     0x858dd4c ; PacketGuard
085B2F05 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2F08 0fb6803c010000           movzx    eax, byte ptr [eax + 0x13c]
085B2F0F 84c0                     test     al, al
085B2F11 0f842b010000             je       0x85b3042
085B2F17 c744240802000000         mov      dword ptr [esp + 8], 2
085B2F1F 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2F22 89442404                 mov      dword ptr [esp + 4], eax
085B2F26 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2F29 890424                   mov      dword ptr [esp], eax
085B2F2C e8e391feff               call     0x859c114 ; CParty::leave_user
085B2F31 e87174b2ff               call     0x80da3a7 ; _Z11G_GameWorldv
085B2F36 8b550c                   mov      edx, dword ptr [ebp + 0xc]
085B2F39 89542404                 mov      dword ptr [esp + 4], edx
085B2F3D 890424                   mov      dword ptr [esp], eax
085B2F40 e8d13a1100               call     0x86c6a16 ; GameWorld::out_from_dungeon
085B2F45 0fb645cb                 movzx    eax, byte ptr [ebp - 0x35]
085B2F49 83f001                   xor      eax, 1
085B2F4C 84c0                     test     al, al
085B2F4E 7425                     je       0x85b2f75
085B2F50 0fb645c3                 movzx    eax, byte ptr [ebp - 0x3d]
085B2F54 83f001                   xor      eax, 1
085B2F57 84c0                     test     al, al
085B2F59 741a                     je       0x85b2f75
085B2F5B 807dd301                 cmp      byte ptr [ebp - 0x2d], 1
085B2F5F 7514                     jne      0x85b2f75
085B2F61 80bda4feffff00           cmp      byte ptr [ebp - 0x15c], 0
085B2F68 740b                     je       0x85b2f75
085B2F6A 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2F6D 890424                   mov      dword ptr [esp], eax
085B2F70 e849570c00               call     0x86786be ; CUser::giveup_panalty
085B2F75 8d45b4                   lea      eax, [ebp - 0x4c]
085B2F78 890424                   mov      dword ptr [esp], eax
085B2F7B e86689b1ff               call     0x80cb8e6 ; InterfacePacketBuf::clear
085B2F80 c744240802000000         mov      dword ptr [esp + 8], 2
085B2F88 c744240400000000         mov      dword ptr [esp + 4], 0
085B2F90 8d45b4                   lea      eax, [ebp - 0x4c]
085B2F93 890424                   mov      dword ptr [esp], eax
085B2F96 e86189b1ff               call     0x80cb8fc ; InterfacePacketBuf::put_header
085B2F9B c744240400000000         mov      dword ptr [esp + 4], 0
085B2FA3 8d45b4                   lea      eax, [ebp - 0x4c]
085B2FA6 890424                   mov      dword ptr [esp], eax
085B2FA9 e87289b1ff               call     0x80cb920 ; InterfacePacketBuf::put_byte
085B2FAE c744240401000000         mov      dword ptr [esp + 4], 1
085B2FB6 8d45b4                   lea      eax, [ebp - 0x4c]
085B2FB9 890424                   mov      dword ptr [esp], eax
085B2FBC e8e36eb2ff               call     0x80d9ea4 ; InterfacePacketBuf::put_short
085B2FC1 8d45b4                   lea      eax, [ebp - 0x4c]
085B2FC4 c744240800000000         mov      dword ptr [esp + 8], 0
085B2FCC 89442404                 mov      dword ptr [esp + 4], eax
085B2FD0 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B2FD3 890424                   mov      dword ptr [esp], eax
085B2FD6 e873740a00               call     0x865a44e ; CUser::make_basic_info
085B2FDB c744240401000000         mov      dword ptr [esp + 4], 1
085B2FE3 8d45b4                   lea      eax, [ebp - 0x4c]
085B2FE6 890424                   mov      dword ptr [esp], eax
085B2FE9 e86a89b1ff               call     0x80cb958 ; InterfacePacketBuf::finalize
085B2FEE 8b4508                   mov      eax, dword ptr [ebp + 8]
085B2FF1 890424                   mov      dword ptr [esp], eax
085B2FF4 e88727b9ff               call     0x8145780 ; CParty::getManager
085B2FF9 890424                   mov      dword ptr [esp], eax
085B2FFC e8c9de0d00               call     0x8690eca ; CUser::getMoveSpace
085B3001 83f801                   cmp      eax, 1
085B3004 0f95c0                   setne    al
085B3007 84c0                     test     al, al
085B3009 7420                     je       0x85b302b
085B300B e89773b2ff               call     0x80da3a7 ; _Z11G_GameWorldv
085B3010 8b550c                   mov      edx, dword ptr [ebp + 0xc]
085B3013 89542408                 mov      dword ptr [esp + 8], edx
085B3017 8d55b4                   lea      edx, [ebp - 0x4c]
085B301A 89542404                 mov      dword ptr [esp + 4], edx
085B301E 890424                   mov      dword ptr [esp], eax
085B3021 e83619b8ff               call     0x813495c ; GameWorld::send_all
085B3026 e9df040000               jmp      0x85b350a
085B302B 8d45b4                   lea      eax, [ebp - 0x4c]
085B302E 89442404                 mov      dword ptr [esp + 4], eax
085B3032 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3035 890424                   mov      dword ptr [esp], eax
085B3038 e811a1feff               call     0x859d14e ; CParty::send_to_party
085B303D e9c8040000               jmp      0x85b350a
085B3042 0fb685a0feffff           movzx    eax, byte ptr [ebp - 0x160]
085B3049 83f001                   xor      eax, 1
085B304C 84c0                     test     al, al
085B304E 741a                     je       0x85b306a
085B3050 c744240800000000         mov      dword ptr [esp + 8], 0
085B3058 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B305B 89442404                 mov      dword ptr [esp + 4], eax
085B305F 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3062 890424                   mov      dword ptr [esp], eax
085B3065 e852f9ffff               call     0x85b29bc ; CParty::set_charac_live
085B306A 8d9db4feffff             lea      ebx, [ebp - 0x14c]
085B3070 b800000000               mov      eax, 0
085B3075 ba40000000               mov      edx, 0x40
085B307A 89df                     mov      edi, ebx
085B307C 89d1                     mov      ecx, edx
085B307E f3ab                     rep stosd dword ptr es:[edi], eax
085B3080 8d85b4feffff             lea      eax, [ebp - 0x14c]
085B3086 89442404                 mov      dword ptr [esp + 4], eax
085B308A 8b4508                   mov      eax, dword ptr [ebp + 8]
085B308D 890424                   mov      dword ptr [esp], eax
085B3090 e8491b0000               call     0x85b4bde ; CParty::_getMemberNames
085B3095 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3098 890424                   mov      dword ptr [esp], eax
085B309B e83a69ebff               call     0x84699da ; CParty::GetSelectedEPLPCmd
085B30A0 3c03                     cmp      al, 3
085B30A2 740f                     je       0x85b30b3
085B30A4 8b4508                   mov      eax, dword ptr [ebp + 8]
085B30A7 890424                   mov      dword ptr [esp], eax
085B30AA e8a3faffff               call     0x85b2b52 ; CParty::check_allmember_die
085B30AF 84c0                     test     al, al
085B30B1 7512                     jne      0x85b30c5
085B30B3 80bda0feffff00           cmp      byte ptr [ebp - 0x160], 0
085B30BA 7509                     jne      0x85b30c5
085B30BC 80bd9cfeffff00           cmp      byte ptr [ebp - 0x164], 0
085B30C3 7407                     je       0x85b30cc
085B30C5 b801000000               mov      eax, 1
085B30CA eb05                     jmp      0x85b30d1
085B30CC b800000000               mov      eax, 0
085B30D1 84c0                     test     al, al
085B30D3 0f84d3020000             je       0x85b33ac
085B30D9 c745e000000000           mov      dword ptr [ebp - 0x20], 0
085B30E0 e996010000               jmp      0x85b327b
085B30E5 8b45e0                   mov      eax, dword ptr [ebp - 0x20]
085B30E8 89442404                 mov      dword ptr [esp + 4], eax
085B30EC 8b4508                   mov      eax, dword ptr [ebp + 8]
085B30EF 890424                   mov      dword ptr [esp], eax
085B30F2 e81b1c0000               call     0x85b4d12 ; CParty::_checkValidUser
085B30F7 83f001                   xor      eax, 1
085B30FA 84c0                     test     al, al
085B30FC 0f8574010000             jne      0x85b3276
085B3102 837dc400                 cmp      dword ptr [ebp - 0x3c], 0
085B3106 747e                     je       0x85b3186
085B3108 8b4508                   mov      eax, dword ptr [ebp + 8]
085B310B 8b80d80c0000             mov      eax, dword ptr [eax + 0xcd8]
085B3111 89c7                     mov      edi, eax
085B3113 8d85b4feffff             lea      eax, [ebp - 0x14c]
085B3119 89442404                 mov      dword ptr [esp + 4], eax
085B311D 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3120 890424                   mov      dword ptr [esp], eax
085B3123 e8b61a0000               call     0x85b4bde ; CParty::_getMemberNames
085B3128 89c6                     mov      esi, eax
085B312A 8b4508                   mov      eax, dword ptr [ebp + 8]
085B312D 05240b0000               add      eax, 0xb24
085B3132 890424                   mov      dword ptr [esp], eax
085B3135 e8e266b4ff               call     0x80f981c ; CBattle_Field::get_dungeon_diff
085B313A 89c3                     mov      ebx, eax
085B313C 8b45c4                   mov      eax, dword ptr [ebp - 0x3c]
085B313F 890424                   mov      dword ptr [esp], eax
085B3142 e85f24b9ff               call     0x81455a6 ; CDungeon::GetDungeonName
085B3147 89c1                     mov      ecx, eax
085B3149 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
085B314C 8b4508                   mov      eax, dword ptr [ebp + 8]
085B314F 898598feffff             mov      dword ptr [ebp - 0x168], eax
085B3155 89d0                     mov      eax, edx
085B3157 01c0                     add      eax, eax
085B3159 01d0                     add      eax, edx
085B315B c1e003                   shl      eax, 3
085B315E 038598feffff             add      eax, dword ptr [ebp - 0x168]
085B3164 83c078                   add      eax, 0x78
085B3167 8b00                     mov      eax, dword ptr [eax]
085B3169 0500970700               add      eax, 0x79700
085B316E 897c2410                 mov      dword ptr [esp + 0x10], edi
085B3172 8974240c                 mov      dword ptr [esp + 0xc], esi
085B3176 895c2408                 mov      dword ptr [esp + 8], ebx
085B317A 894c2404                 mov      dword ptr [esp + 4], ecx
085B317E 890424                   mov      dword ptr [esp], eax
085B3181 e856180d00               call     0x86849dc ; cUserHistoryLog::LeaveDungeon
085B3186 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3189 0fb7804c0c0000           movzx    eax, word ptr [eax + 0xc4c]
085B3190 6685c0                   test     ax, ax
085B3193 7473                     je       0x85b3208
085B3195 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
085B3198 8b4d08                   mov      ecx, dword ptr [ebp + 8]
085B319B 89d0                     mov      eax, edx
085B319D 01c0                     add      eax, eax
085B319F 01d0                     add      eax, edx
085B31A1 c1e003                   shl      eax, 3
085B31A4 8d0401                   lea      eax, [ecx + eax]
085B31A7 83c078                   add      eax, 0x78
085B31AA 8b00                     mov      eax, dword ptr [eax]
085B31AC 3b450c                   cmp      eax, dword ptr [ebp + 0xc]
085B31AF 7457                     je       0x85b3208
085B31B1 80bda0feffff00           cmp      byte ptr [ebp - 0x160], 0
085B31B8 7428                     je       0x85b31e2
085B31BA 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
085B31BD 8b4d08                   mov      ecx, dword ptr [ebp + 8]
085B31C0 89d0                     mov      eax, edx
085B31C2 01c0                     add      eax, eax
085B31C4 01d0                     add      eax, edx
085B31C6 c1e003                   shl      eax, 3
085B31C9 8d0401                   lea      eax, [ecx + eax]
085B31CC 83c078                   add      eax, 0x78
085B31CF 8b00                     mov      eax, dword ptr [eax]
085B31D1 89442404                 mov      dword ptr [esp + 4], eax
085B31D5 8b4508                   mov      eax, dword ptr [ebp + 8]
085B31D8 890424                   mov      dword ptr [esp], eax
085B31DB e81cc60000               call     0x85bf7fc ; CParty::OnSuccessedDungeonClear
085B31E0 eb26                     jmp      0x85b3208
085B31E2 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
085B31E5 8b4d08                   mov      ecx, dword ptr [ebp + 8]
085B31E8 89d0                     mov      eax, edx
085B31EA 01c0                     add      eax, eax
085B31EC 01d0                     add      eax, edx
085B31EE c1e003                   shl      eax, 3
085B31F1 8d0401                   lea      eax, [ecx + eax]
085B31F4 83c078                   add      eax, 0x78
085B31F7 8b00                     mov      eax, dword ptr [eax]
085B31F9 89442404                 mov      dword ptr [esp + 4], eax
085B31FD 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3200 890424                   mov      dword ptr [esp], eax
085B3203 e8d2c50000               call     0x85bf7da ; CParty::OnFailedDungeonClear
085B3208 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
085B320B 8b4d08                   mov      ecx, dword ptr [ebp + 8]
085B320E 89d0                     mov      eax, edx
085B3210 01c0                     add      eax, eax
085B3212 01d0                     add      eax, edx
085B3214 c1e003                   shl      eax, 3
085B3217 8d0401                   lea      eax, [ecx + eax]
085B321A 83c078                   add      eax, 0x78
085B321D 8b18                     mov      ebx, dword ptr [eax]
085B321F e88371b2ff               call     0x80da3a7 ; _Z11G_GameWorldv
085B3224 895c2404                 mov      dword ptr [esp + 4], ebx
085B3228 890424                   mov      dword ptr [esp], eax
085B322B e8e6371100               call     0x86c6a16 ; GameWorld::out_from_dungeon
085B3230 0fb645cb                 movzx    eax, byte ptr [ebp - 0x35]
085B3234 83f001                   xor      eax, 1
085B3237 84c0                     test     al, al
085B3239 743c                     je       0x85b3277
085B323B 0fb645c3                 movzx    eax, byte ptr [ebp - 0x3d]
085B323F 83f001                   xor      eax, 1
085B3242 84c0                     test     al, al
085B3244 7431                     je       0x85b3277
085B3246 807dd301                 cmp      byte ptr [ebp - 0x2d], 1
085B324A 752b                     jne      0x85b3277
085B324C 80bda4feffff00           cmp      byte ptr [ebp - 0x15c], 0
085B3253 7422                     je       0x85b3277
085B3255 8b55e0                   mov      edx, dword ptr [ebp - 0x20]
085B3258 8b4d08                   mov      ecx, dword ptr [ebp + 8]
085B325B 89d0                     mov      eax, edx
085B325D 01c0                     add      eax, eax
085B325F 01d0                     add      eax, edx
085B3261 c1e003                   shl      eax, 3
085B3264 8d0401                   lea      eax, [ecx + eax]
085B3267 83c078                   add      eax, 0x78
085B326A 8b00                     mov      eax, dword ptr [eax]
085B326C 890424                   mov      dword ptr [esp], eax
085B326F e84a540c00               call     0x86786be ; CUser::giveup_panalty
085B3274 eb01                     jmp      0x85b3277
085B3276 90                       nop      
085B3277 8345e001                 add      dword ptr [ebp - 0x20], 1
085B327B 837de003                 cmp      dword ptr [ebp - 0x20], 3
085B327F 0f9ec0                   setle    al
085B3282 84c0                     test     al, al
085B3284 0f855bfeffff             jne      0x85b30e5
085B328A 8d45b4                   lea      eax, [ebp - 0x4c]
085B328D 890424                   mov      dword ptr [esp], eax
085B3290 e85186b1ff               call     0x80cb8e6 ; InterfacePacketBuf::clear
085B3295 c744240802000000         mov      dword ptr [esp + 8], 2
085B329D c744240400000000         mov      dword ptr [esp + 4], 0
085B32A5 8d45b4                   lea      eax, [ebp - 0x4c]
085B32A8 890424                   mov      dword ptr [esp], eax
085B32AB e84c86b1ff               call     0x80cb8fc ; InterfacePacketBuf::put_header
085B32B0 c744240400000000         mov      dword ptr [esp + 4], 0
085B32B8 8d45b4                   lea      eax, [ebp - 0x4c]
085B32BB 890424                   mov      dword ptr [esp], eax
085B32BE e85d86b1ff               call     0x80cb920 ; InterfacePacketBuf::put_byte
085B32C3 8b4508                   mov      eax, dword ptr [ebp + 8]
085B32C6 890424                   mov      dword ptr [esp], eax
085B32C9 e89c6efeff               call     0x859a16a ; CParty::get_member_count
085B32CE 89442404                 mov      dword ptr [esp + 4], eax
085B32D2 8d45b4                   lea      eax, [ebp - 0x4c]
085B32D5 890424                   mov      dword ptr [esp], eax
085B32D8 e8c76bb2ff               call     0x80d9ea4 ; InterfacePacketBuf::put_short
085B32DD c745e400000000           mov      dword ptr [ebp - 0x1c], 0
085B32E4 eb4e                     jmp      0x85b3334
085B32E6 8b45e4                   mov      eax, dword ptr [ebp - 0x1c]
085B32E9 89442404                 mov      dword ptr [esp + 4], eax
085B32ED 8b4508                   mov      eax, dword ptr [ebp + 8]
085B32F0 890424                   mov      dword ptr [esp], eax
085B32F3 e81a1a0000               call     0x85b4d12 ; CParty::_checkValidUser
085B32F8 83f001                   xor      eax, 1
085B32FB 84c0                     test     al, al
085B32FD 7530                     jne      0x85b332f
085B32FF 8d4db4                   lea      ecx, [ebp - 0x4c]
085B3302 8b55e4                   mov      edx, dword ptr [ebp - 0x1c]
085B3305 8b5d08                   mov      ebx, dword ptr [ebp + 8]
085B3308 89d0                     mov      eax, edx
085B330A 01c0                     add      eax, eax
085B330C 01d0                     add      eax, edx
085B330E c1e003                   shl      eax, 3
085B3311 8d0403                   lea      eax, [ebx + eax]
085B3314 83c078                   add      eax, 0x78
085B3317 8b00                     mov      eax, dword ptr [eax]
085B3319 c744240800000000         mov      dword ptr [esp + 8], 0
085B3321 894c2404                 mov      dword ptr [esp + 4], ecx
085B3325 890424                   mov      dword ptr [esp], eax
085B3328 e821710a00               call     0x865a44e ; CUser::make_basic_info
085B332D eb01                     jmp      0x85b3330
085B332F 90                       nop      
085B3330 8345e401                 add      dword ptr [ebp - 0x1c], 1
085B3334 837de403                 cmp      dword ptr [ebp - 0x1c], 3
085B3338 0f9ec0                   setle    al
085B333B 84c0                     test     al, al
085B333D 75a7                     jne      0x85b32e6
085B333F c744240401000000         mov      dword ptr [esp + 4], 1
085B3347 8d45b4                   lea      eax, [ebp - 0x4c]
085B334A 890424                   mov      dword ptr [esp], eax
085B334D e80686b1ff               call     0x80cb958 ; InterfacePacketBuf::finalize
085B3352 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3355 890424                   mov      dword ptr [esp], eax
085B3358 e8b3300000               call     0x85b6410 ; CParty::IsExistInvisible
085B335D 84c0                     test     al, al
085B335F 7414                     je       0x85b3375
085B3361 8d45b4                   lea      eax, [ebp - 0x4c]
085B3364 89442404                 mov      dword ptr [esp + 4], eax
085B3368 8b4508                   mov      eax, dword ptr [ebp + 8]
085B336B 890424                   mov      dword ptr [esp], eax
085B336E e8db9dfeff               call     0x859d14e ; CParty::send_to_party
085B3373 eb14                     jmp      0x85b3389
085B3375 e82d70b2ff               call     0x80da3a7 ; _Z11G_GameWorldv
085B337A 8d55b4                   lea      edx, [ebp - 0x4c]
085B337D 89542404                 mov      dword ptr [esp + 4], edx
085B3381 890424                   mov      dword ptr [esp], eax
085B3384 e88b581100               call     0x86c8c14 ; GameWorld::send_all
085B3389 c744240401000000         mov      dword ptr [esp + 4], 1
085B3391 8b4508                   mov      eax, dword ptr [ebp + 8]
085B3394 890424                   mov      dword ptr [esp], eax
085B3397 e8dc85cbff               call     0x826b978 ; CParty::set_state
085B339C 8b4508                   mov      eax, dword ptr [ebp + 8]
085B339F 890424                   mov      dword ptr [esp], eax
085B33A2 e81b7bfeff               call     0x859aec2 ; CParty::battle_data_reset
085B33A7 e919010000               jmp      0x85b34c5
085B33AC c744240802000000         mov      dword ptr [esp + 8], 2
085B33B4 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B33B7 89442404                 mov      dword ptr [esp + 4], eax
085B33BB 8b4508                   mov      eax, dword ptr [ebp + 8]
085B33BE 890424                   mov      dword ptr [esp], eax
085B33C1 e84e8dfeff               call     0x859c114 ; CParty::leave_user
085B33C6 8945d4                   mov      dword ptr [ebp - 0x2c], eax
085B33C9 837dd401                 cmp      dword ptr [ebp - 0x2c], 1
085B33CD 0f84d8000000             je       0x85b34ab
085B33D3 e8cf6fb2ff               call     0x80da3a7 ; _Z11G_GameWorldv
085B33D8 8b550c                   mov      edx, dword ptr [ebp + 0xc]
085B33DB 89542404                 mov      dword ptr [esp + 4], edx
085B33DF 890424                   mov      dword ptr [esp], eax
085B33E2 e82f361100               call     0x86c6a16 ; GameWorld::out_from_dungeon
085B33E7 0fb645cb                 movzx    eax, byte ptr [ebp - 0x35]
085B33EB 83f001                   xor      eax, 1
085B33EE 84c0                     test     al, al
085B33F0 7425                     je       0x85b3417
085B33F2 0fb645c3                 movzx    eax, byte ptr [ebp - 0x3d]
085B33F6 83f001                   xor      eax, 1
085B33F9 84c0                     test     al, al
085B33FB 741a                     je       0x85b3417
085B33FD 807dd301                 cmp      byte ptr [ebp - 0x2d], 1
085B3401 7514                     jne      0x85b3417
085B3403 80bda4feffff00           cmp      byte ptr [ebp - 0x15c], 0
085B340A 740b                     je       0x85b3417
085B340C 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B340F 890424                   mov      dword ptr [esp], eax
085B3412 e8a7520c00               call     0x86786be ; CUser::giveup_panalty
085B3417 8d45b4                   lea      eax, [ebp - 0x4c]
085B341A 890424                   mov      dword ptr [esp], eax
085B341D e8c484b1ff               call     0x80cb8e6 ; InterfacePacketBuf::clear
085B3422 c744240802000000         mov      dword ptr [esp + 8], 2
085B342A c744240400000000         mov      dword ptr [esp + 4], 0
085B3432 8d45b4                   lea      eax, [ebp - 0x4c]
085B3435 890424                   mov      dword ptr [esp], eax
085B3438 e8bf84b1ff               call     0x80cb8fc ; InterfacePacketBuf::put_header
085B343D c744240400000000         mov      dword ptr [esp + 4], 0
085B3445 8d45b4                   lea      eax, [ebp - 0x4c]
085B3448 890424                   mov      dword ptr [esp], eax
085B344B e8d084b1ff               call     0x80cb920 ; InterfacePacketBuf::put_byte
085B3450 c744240401000000         mov      dword ptr [esp + 4], 1
085B3458 8d45b4                   lea      eax, [ebp - 0x4c]
085B345B 890424                   mov      dword ptr [esp], eax
085B345E e8416ab2ff               call     0x80d9ea4 ; InterfacePacketBuf::put_short
085B3463 8d45b4                   lea      eax, [ebp - 0x4c]
085B3466 c744240800000000         mov      dword ptr [esp + 8], 0
085B346E 89442404                 mov      dword ptr [esp + 4], eax
085B3472 8b450c                   mov      eax, dword ptr [ebp + 0xc]
085B3475 890424                   mov      dword ptr [esp], eax
085B3478 e8d16f0a00               call     0x865a44e ; CUser::make_basic_info
085B347D c744240401000000         mov      dword ptr [esp + 4], 1
085B3485 8d45b4                   lea      eax, [ebp - 0x4c]
085B3488 890424                   mov      dword ptr [esp], eax
085B348B e8c884b1ff               call     0x80cb958 ; InterfacePacketBuf::finalize
085B3490 e8126fb2ff               call     0x80da3a7 ; _Z11G_GameWorldv
085B3495 8b550c                   mov      edx, dword ptr [ebp + 0xc]
085B3498 89542408                 mov      dword ptr [esp + 8], edx
085B349C 8d55b4                   lea      edx, [ebp - 0x4c]
085B349F 89542404                 mov      dword ptr [esp + 4], edx
085B34A3 890424                   mov      dword ptr [esp], eax
085B34A6 e8b114b8ff               call     0x813495c ; GameWorld::send_all
085B34AB 8b4508                   mov      eax, dword ptr [ebp + 8]
085B34AE 890424                   mov      dword ptr [esp], eax
085B34B1 e8e690ffff               call     0x85ac59c ; CParty::checkInoutConditionDungeon
085B34B6 89442404                 mov      dword ptr [esp + 4], eax
085B34BA 8b4508                   mov      eax, dword ptr [ebp + 8]
085B34BD 890424                   mov      dword ptr [esp], eax
085B34C0 e89994ffff               call     0x85ac95e ; CParty::sendInoutConditionDungeon
085B34C5 e8c48cb1ff               call     0x80cc18e ; _Z14G_CGameManagerv
085B34CA 890424                   mov      dword ptr [esp], eax
085B34CD e8f25aceff               call     0x8298fc4 ; CGameManager::GetSpecialItemRoutingManager
085B34D2 8945d8                   mov      dword ptr [ebp - 0x28], eax
085B34D5 837dd800                 cmp      dword ptr [ebp - 0x28], 0
085B34D9 742f                     je       0x85b350a
085B34DB 8b4508                   mov      eax, dword ptr [ebp + 8]
085B34DE 89442404                 mov      dword ptr [esp + 4], eax
085B34E2 8b45d8                   mov      eax, dword ptr [ebp - 0x28]
085B34E5 890424                   mov      dword ptr [esp], eax
085B34E8 e8d57d0500               call     0x860b2c2 ; CSpecialItemRoutingManager::ProcessGiveUp
085B34ED eb1b                     jmp      0x85b350a
085B34EF 89d3                     mov      ebx, edx
085B34F1 89c6                     mov      esi, eax
085B34F3 8d45b4                   lea      eax, [ebp - 0x4c]
085B34F6 890424                   mov      dword ptr [esp], eax
085B34F9 e882a9fdff               call     0x858de80 ; PacketGuard
085B34FE 89f0                     mov      eax, esi
085B3500 89da                     mov      edx, ebx
085B3502 890424                   mov      dword ptr [esp], eax
085B3505 e846025300               call     0x8ae3750 ; _Unwind_Resume
085B350A 8d45b4                   lea      eax, [ebp - 0x4c]
085B350D 890424                   mov      dword ptr [esp], eax
085B3510 e86ba9fdff               call     0x858de80 ; PacketGuard
085B3515 81c47c010000             add      esp, 0x17c
085B351B 5b                       pop      ebx
085B351C 5e                       pop      esi
085B351D 5f                       pop      edi
085B351E 5d                       pop      ebp
085B351F c3                       ret      
