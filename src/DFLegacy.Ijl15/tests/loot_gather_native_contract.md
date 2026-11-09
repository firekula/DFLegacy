# Solo loot gather native contract

Read-only original: `G:\dnf_local\DNF_2008\DNF.exe`. SHA256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`. Image base `0x400000`. Capstone 5.0.6.

`0x849280` is the upstream chat thiscall with six stack arguments: body, type, 16-bit user ID (upper stack word is unspecified), sender name, display mode, extra. Original cleanup is `ret 0x18`. Notification caller `0x41CA65..0x41CA8F` reads a word UID into DX without clearing EDX. The handler itself uses `movzx ecx, word [ebp+0x10]`. The shim therefore masks the UID to 16 bits and preserves all six arguments on the ordinary path.

`0x846670` is too late to recover UID: its three arguments are body, type, name. It displays text through `0x845B60` before emitting CHAT_MESSAGE at `0x846696`. The existing Type16 audio gate remains unchanged.

Only the upstream 9-byte prologue is replaced. Ordinary chat replays it and continues at `0x849289`; exact self Type16 marker queues once and returns `0x18` before display. The original `0x433B47` boss frame hook is shared, with gather before the boss-dependent early return. No other frame patch is installed.

Ground/controller layouts follow the already verified `client_test_control_runtime.cpp` ApplyGround path: actual current-map actors, kind `0x121`, vtable `0x999574`, active `+0x128`, pending `+0x7A5`, protected ground ID `+0xD4`, descriptor `+0x7A0`. Native setter `0x4715F0(actor,axis,int)` updates controller/mirrors; no pickup or remove/create packet is issued.

Validation: focused test first failed `exact Type16 marker must be recognized`, then passed. Guarded memory fixtures cover solo/alive/scene/sender checks, visible/pending selection, repeated new drops, unchanged non-position bytes, per-actor room revalidation, unknown layout, bad signatures/protected mirrors, duplicate identities, missing membership and actor bounds. The real naked shim test redirects only its final continuation immediate to a synthetic original and checks six arguments, this, word UID, consume/pass-through and one-shot queue behavior. It does not execute the original game controller. Actual game acceptance remains a separate root task.

## 00849280–0084928B (exclusive)
Bytes SHA256 `f9874c68e0f59725eb4eeb90b274ab227cd26e5d977f4ac1d3b22f076d4a0994`.
```text
00849280 55                       push     ebp
00849281 8bec                     mov      ebp, esp
00849283 81ec00010000             sub      esp, 0x100
00849289 53                       push     ebx
0084928A 56                       push     esi
```

## 008492D6–0084930B (exclusive)
Bytes SHA256 `1604a4e6e58ddaecb3098b6a751eb4f5c6a43d53fd59b49a3ec926b1a3d3a05b`.
```text
008492D6 8b750c                   mov      esi, dword ptr [ebp + 0xc]
008492D9 83fe12                   cmp      esi, 0x12
008492DC 752d                     jne      0x84930b
008492DE 8b0de8dcb500             mov      ecx, dword ptr [0xb5dce8]
008492E4 8b8904100000             mov      ecx, dword ptr [ecx + 0x1004]
008492EA 85c9                     test     ecx, ecx
008492EC 0f84ac020000             je       0x84959e
008492F2 8b4514                   mov      eax, dword ptr [ebp + 0x14]
008492F5 8b11                     mov      edx, dword ptr [ecx]
008492F7 50                       push     eax
008492F8 8d8500ffffff             lea      eax, [ebp - 0x100]
008492FE 50                       push     eax
008492FF ff524c                   call     dword ptr [edx + 0x4c]
00849302 5f                       pop      edi
00849303 5e                       pop      esi
00849304 5b                       pop      ebx
00849305 8be5                     mov      esp, ebp
00849307 5d                       pop      ebp
00849308 c21800                   ret      0x18
```

## 00849342–0084934E (exclusive)
Bytes SHA256 `3ee9d8b3b6c68f4afc44fbf6e113ac5e46783a1f84f3b90fd28a660965d6f230`.
```text
00849342 0fb74d10                 movzx    ecx, word ptr [ebp + 0x10]
00849346 3bc8                     cmp      ecx, eax
00849348 0f8494010000             je       0x8494e2
```

## 0041CA65–0041CA94 (exclusive)
Bytes SHA256 `fab0b01b528646911c878cb4f8bd71017a9587458880d5a49b2080b133492458`.
```text
0041CA65 6aff                     push     -1
0041CA67 6a00                     push     0
0041CA69 6a00                     push     0
0041CA6B 668b9550f7ffff           mov      dx, word ptr [ebp - 0x8b0]
0041CA72 52                       push     edx
0041CA73 0fb68557f7ffff           movzx    eax, byte ptr [ebp - 0x8a9]
0041CA7A 50                       push     eax
0041CA7B 8d8d4cf6ffff             lea      ecx, [ebp - 0x9b4]
0041CA81 51                       push     ecx
0041CA82 8b0de8dcb500             mov      ecx, dword ptr [0xb5dce8]
0041CA88 e863080300               call     0x44d2f0
0041CA8D 8bc8                     mov      ecx, eax
0041CA8F e8ecc74200               call     0x849280
```

## 00846670–008466A2 (exclusive)
Bytes SHA256 `b66b34e9fc3bb11884b6d51069297d74a8c616dc67da9c9de83622c9b2f8fa1c`.
```text
00846670 55                       push     ebp
00846671 8bec                     mov      ebp, esp
00846673 8b4510                   mov      eax, dword ptr [ebp + 0x10]
00846676 8b550c                   mov      edx, dword ptr [ebp + 0xc]
00846679 6a00                     push     0
0084667B 50                       push     eax
0084667C 8b4508                   mov      eax, dword ptr [ebp + 8]
0084667F 52                       push     edx
00846680 68e6c89bff               push     0xff9bc8e6
00846685 50                       push     eax
00846686 e8d5f4ffff               call     0x845b60
0084668B 6a00                     push     0
0084668D 6a00                     push     0
0084668F 6aff                     push     -1
00846691 689030a800               push     0xa83090
00846696 e8f5d1beff               call     0x433890
0084669B 83c410                   add      esp, 0x10
0084669E 5d                       pop      ebp
0084669F c20c00                   ret      0xc
```

## 00433B36–00433B4C (exclusive)
Bytes SHA256 `b13542522b701a45426a916ad800e5d12f4c400832f8aa580f329dbb2981be77`.
```text
00433B36 8b0de0dcb500             mov      ecx, dword ptr [0xb5dce0]
00433B3C 8b01                     mov      eax, dword ptr [ecx]
00433B3E ff507c                   call     dword ptr [eax + 0x7c]
00433B41 8b0de4dcb500             mov      ecx, dword ptr [0xb5dce4]
00433B47 e8948e3500               call     0x78c9e0
```

## 004715F0–004715FF (exclusive)
Bytes SHA256 `c7fa8c88a728ea1d71ae005d82809482d2be0a0889a4324215526f620958f789`.
```text
004715F0 55                       push     ebp
004715F1 8bec                     mov      ebp, esp
004715F3 8b89e0000000             mov      ecx, dword ptr [ecx + 0xe0]
004715F9 5d                       pop      ebp
004715FA e951870500               jmp      0x4c9d50
```
