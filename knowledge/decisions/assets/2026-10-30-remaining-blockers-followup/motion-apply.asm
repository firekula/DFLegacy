004C9270 push     ebp
004C9271 mov      ebp, esp
004C9273 sub      esp, 0x24
004C9276 push     ebx
004C9277 push     esi
004C9278 push     edi
004C9279 mov      edi, dword ptr [ebp + 8]
004C927C mov      ebx, edi
004C927E shr      ebx, 4
004C9281 and      bl, 1
004C9284 mov      esi, ecx
004C9286 mov      byte ptr [ebp - 1], bl
004C9289 je       0x4c9368
004C928F xor      edi, edi
004C9291 mov      ecx, dword ptr [0xb5dce0]
004C9297 mov      eax, dword ptr [ecx]
004C9299 lea      edx, [ebp - 0xc]
004C929C push     edx
004C929D call     dword ptr [eax + 0x54]
004C92A0 mov      ecx, dword ptr [esi + 4]
004C92A3 test     ecx, ecx
004C92A5 jne      0x4c92aa
004C92A7 lea      ecx, [esi + 0x3d]
004C92AA mov      edx, dword ptr [ebp - 0xc]
004C92AD mov      eax, dword ptr [ecx]
004C92AF push     edx
004C92B0 push     edi
004C92B1 call     dword ptr [eax + 4]
004C92B4 inc      edi
004C92B5 cmp      edi, 3
004C92B8 jl       0x4c9291
004C92BA xor      edi, edi
004C92BC lea      esp, [esp]
004C92C0 mov      ecx, dword ptr [0xb5dce0]
004C92C6 mov      eax, dword ptr [ecx]
004C92C8 lea      edx, [ebp - 0xc]
004C92CB push     edx
004C92CC call     dword ptr [eax + 0x54]
004C92CF mov      ecx, dword ptr [esi + 4]
004C92D2 test     ecx, ecx
004C92D4 jne      0x4c92d9
004C92D6 lea      ecx, [esi + 0x3d]
004C92D9 mov      edx, dword ptr [ebp - 0xc]
004C92DC mov      eax, dword ptr [ecx]
004C92DE push     edx
004C92DF push     edi
004C92E0 call     dword ptr [eax + 0xc]
004C92E3 inc      edi
004C92E4 cmp      edi, 3
004C92E7 jl       0x4c92c0
004C92E9 mov      edi, 0x1c
004C92EE mov      edi, edi
004C92F0 mov      ecx, dword ptr [0xb5dce0]
004C92F6 mov      eax, dword ptr [ecx]
004C92F8 lea      edx, [ebp - 8]
004C92FB push     edx
004C92FC call     dword ptr [eax + 0x58]
004C92FF mov      eax, dword ptr [esi + 4]
004C9302 test     eax, eax
004C9304 jne      0x4c9309
004C9306 lea      eax, [esi + 0x3d]
004C9309 mov      ecx, dword ptr [ebp - 8]
004C930C mov      dword ptr [edi + eax], ecx
004C930F add      edi, 4
004C9312 cmp      edi, 0x28
004C9315 jl       0x4c92f0
004C9317 mov      edi, 0x28
004C931C lea      esp, [esp]
004C9320 mov      ecx, dword ptr [0xb5dce0]
004C9326 mov      edx, dword ptr [ecx]
004C9328 lea      eax, [ebp - 8]
004C932B push     eax
004C932C call     dword ptr [edx + 0x58]
004C932F mov      eax, dword ptr [esi + 4]
004C9332 test     eax, eax
004C9334 jne      0x4c9339
004C9336 lea      eax, [esi + 0x3d]
004C9339 mov      ecx, dword ptr [ebp - 8]
004C933C mov      dword ptr [edi + eax], ecx
004C933F add      edi, 4
004C9342 cmp      edi, 0x34
004C9345 jl       0x4c9320
004C9347 mov      ecx, dword ptr [0xb5dce0]
004C934D mov      edx, dword ptr [ecx]
004C934F lea      eax, [ebp + 0xb]
004C9352 push     eax
004C9353 call     dword ptr [edx + 0x4c]
004C9356 mov      eax, dword ptr [esi + 4]
004C9359 test     eax, eax
004C935B jne      0x4c9360
004C935D lea      eax, [esi + 0x3d]
004C9360 mov      cl, byte ptr [ebp + 0xb]
004C9363 jmp      0x4c9916
004C9368 shr      edi, 0x1a
004C936B mov      eax, edi
004C936D and      eax, 3
004C9370 cmp      eax, 1
004C9373 mov      dword ptr [ebp - 0x1c], edi
004C9376 jne      0x4c938d
004C9378 mov      ecx, dword ptr [esi + 4]
004C937B test     ecx, ecx
004C937D jne      0x4c9382
004C937F lea      ecx, [esi + 0x3d]
004C9382 mov      edx, dword ptr [ecx]
004C9384 push     0
004C9386 push     0
004C9388 call     dword ptr [edx + 4]
004C938B jmp      0x4c93ea
004C938D test     eax, eax
004C938F jne      0x4c93be
004C9391 mov      ecx, dword ptr [0xb5dce0]
004C9397 push     4
004C9399 lea      edx, [ebp - 0x14]
004C939C mov      dword ptr [ebp - 0x14], eax
004C939F mov      eax, dword ptr [ecx]
004C93A1 push     edx
004C93A2 call     dword ptr [eax + 0x60]
004C93A5 mov      eax, dword ptr [esi + 4]
004C93A8 test     eax, eax
004C93AA jne      0x4c93af
004C93AC lea      eax, [esi + 0x3d]
004C93AF mov      ecx, dword ptr [ebp - 0x14]
004C93B2 mov      edx, dword ptr [eax]
004C93B4 push     ecx
004C93B5 push     0
004C93B7 mov      ecx, eax
004C93B9 call     dword ptr [edx + 4]
004C93BC jmp      0x4c93ea
004C93BE mov      eax, dword ptr [esi]
004C93C0 test     eax, eax
004C93C2 mov      ecx, eax
004C93C4 jne      0x4c93c9
004C93C6 lea      ecx, [esi + 8]
004C93C9 mov      eax, dword ptr [esi + 4]
004C93CC test     eax, eax
004C93CE jne      0x4c93d3
004C93D0 lea      eax, [esi + 0x3d]
004C93D3 mov      edx, dword ptr [ecx]
004C93D5 mov      ebx, dword ptr [eax]
004C93D7 push     0
004C93D9 mov      dword ptr [ebp - 0x14], eax
004C93DC call     dword ptr [edx]
004C93DE push     ecx
004C93DF mov      ecx, dword ptr [ebp - 0x14]
004C93E2 fstp     dword ptr [esp]
004C93E5 push     0
004C93E7 call     dword ptr [ebx + 4]
004C93EA mov      eax, edi
004C93EC shr      eax, 2
004C93EF and      eax, 3
004C93F2 cmp      eax, 1
004C93F5 jne      0x4c940c
004C93F7 mov      ecx, dword ptr [esi + 4]
004C93FA test     ecx, ecx
004C93FC jne      0x4c9401
004C93FE lea      ecx, [esi + 0x3d]
004C9401 mov      eax, dword ptr [ecx]
004C9403 push     0
004C9405 push     1
004C9407 call     dword ptr [eax + 4]
004C940A jmp      0x4c9468
004C940C test     eax, eax
004C940E jne      0x4c943d
004C9410 mov      ecx, dword ptr [0xb5dce0]
004C9416 mov      dword ptr [ebp - 0x14], eax
004C9419 mov      edx, dword ptr [ecx]
004C941B push     4
004C941D lea      eax, [ebp - 0x14]
004C9420 push     eax
004C9421 call     dword ptr [edx + 0x60]
004C9424 mov      eax, dword ptr [esi + 4]
004C9427 test     eax, eax
004C9429 jne      0x4c942e
004C942B lea      eax, [esi + 0x3d]
004C942E mov      ecx, dword ptr [ebp - 0x14]
004C9431 mov      edx, dword ptr [eax]
004C9433 push     ecx
004C9434 push     1
004C9436 mov      ecx, eax
004C9438 call     dword ptr [edx + 4]
004C943B jmp      0x4c9468
004C943D mov      eax, dword ptr [esi]
004C943F test     eax, eax
004C9441 jne      0x4c9446
004C9443 lea      eax, [esi + 8]
004C9446 mov      edi, dword ptr [esi + 4]
004C9449 test     edi, edi
004C944B jne      0x4c9450
004C944D lea      edi, [esi + 0x3d]
004C9450 mov      edx, dword ptr [eax]
004C9452 mov      ebx, dword ptr [edi]
004C9454 push     1
004C9456 mov      ecx, eax
004C9458 call     dword ptr [edx]
004C945A push     ecx
004C945B fstp     dword ptr [esp]
004C945E push     1
004C9460 mov      ecx, edi
004C9462 call     dword ptr [ebx + 4]
004C9465 mov      edi, dword ptr [ebp - 0x1c]
004C9468 shr      edi, 4
004C946B and      edi, 3
004C946E mov      eax, edi
004C9470 cmp      eax, 1
004C9473 jne      0x4c948a
004C9475 mov      ecx, dword ptr [esi + 4]
004C9478 test     ecx, ecx
004C947A jne      0x4c947f
004C947C lea      ecx, [esi + 0x3d]
004C947F mov      eax, dword ptr [ecx]
004C9481 push     0
004C9483 push     2
004C9485 call     dword ptr [eax + 4]
004C9488 jmp      0x4c94e3
004C948A test     eax, eax
004C948C jne      0x4c94bb
004C948E mov      ecx, dword ptr [0xb5dce0]
004C9494 mov      dword ptr [ebp - 0x14], eax
004C9497 mov      edx, dword ptr [ecx]
004C9499 push     4
004C949B lea      eax, [ebp - 0x14]
004C949E push     eax
004C949F call     dword ptr [edx + 0x60]
004C94A2 mov      eax, dword ptr [esi + 4]
004C94A5 test     eax, eax
004C94A7 jne      0x4c94ac
004C94A9 lea      eax, [esi + 0x3d]
004C94AC mov      ecx, dword ptr [ebp - 0x14]
004C94AF mov      edx, dword ptr [eax]
004C94B1 push     ecx
004C94B2 push     2
004C94B4 mov      ecx, eax
004C94B6 call     dword ptr [edx + 4]
004C94B9 jmp      0x4c94e3
004C94BB mov      eax, dword ptr [esi]
004C94BD test     eax, eax
004C94BF jne      0x4c94c4
004C94C1 lea      eax, [esi + 8]
004C94C4 mov      edi, dword ptr [esi + 4]
004C94C7 test     edi, edi
004C94C9 jne      0x4c94ce
004C94CB lea      edi, [esi + 0x3d]
004C94CE mov      edx, dword ptr [eax]
004C94D0 mov      ebx, dword ptr [edi]
004C94D2 push     2
004C94D4 mov      ecx, eax
004C94D6 call     dword ptr [edx]
004C94D8 push     ecx
004C94D9 fstp     dword ptr [esp]
004C94DC push     2
004C94DE mov      ecx, edi
004C94E0 call     dword ptr [ebx + 4]
004C94E3 mov      edi, dword ptr [ebp + 8]
004C94E6 shr      edi, 0x14
004C94E9 and      edi, 0x3f
004C94EC mov      eax, edi
004C94EE and      eax, 3
004C94F1 cmp      eax, 1
004C94F4 jne      0x4c950b
004C94F6 mov      ecx, dword ptr [esi + 4]
004C94F9 test     ecx, ecx
004C94FB jne      0x4c9500
004C94FD lea      ecx, [esi + 0x3d]
004C9500 mov      eax, dword ptr [ecx]
004C9502 push     0
004C9504 push     0
004C9506 call     dword ptr [eax + 0xc]
004C9509 jmp      0x4c9569
004C950B test     eax, eax
004C950D jne      0x4c953c
004C950F mov      ecx, dword ptr [0xb5dce0]
004C9515 mov      dword ptr [ebp - 0x14], eax
004C9518 mov      edx, dword ptr [ecx]
004C951A push     4
004C951C lea      eax, [ebp - 0x14]
004C951F push     eax
004C9520 call     dword ptr [edx + 0x60]
004C9523 mov      eax, dword ptr [esi + 4]
004C9526 test     eax, eax
004C9528 jne      0x4c952d
004C952A lea      eax, [esi + 0x3d]
004C952D mov      ecx, dword ptr [ebp - 0x14]
004C9530 mov      edx, dword ptr [eax]
004C9532 push     ecx
004C9533 push     0
004C9535 mov      ecx, eax
004C9537 call     dword ptr [edx + 0xc]
004C953A jmp      0x4c9569
004C953C mov      eax, dword ptr [esi]
004C953E test     eax, eax
004C9540 mov      ecx, eax
004C9542 jne      0x4c9547
004C9544 lea      ecx, [esi + 8]
004C9547 mov      eax, dword ptr [esi + 4]
004C954A test     eax, eax
004C954C jne      0x4c9551
004C954E lea      eax, [esi + 0x3d]
004C9551 mov      edx, dword ptr [ecx]
004C9553 mov      ebx, dword ptr [eax]
004C9555 push     0
004C9557 mov      dword ptr [ebp - 0x14], eax
004C955A call     dword ptr [edx + 8]
004C955D push     ecx
004C955E mov      ecx, dword ptr [ebp - 0x14]
004C9561 fstp     dword ptr [esp]
004C9564 push     0
004C9566 call     dword ptr [ebx + 0xc]
004C9569 mov      eax, edi
004C956B shr      eax, 2
004C956E and      eax, 3
004C9571 cmp      eax, 1
004C9574 jne      0x4c958b
004C9576 mov      ecx, dword ptr [esi + 4]
004C9579 test     ecx, ecx
004C957B jne      0x4c9580
004C957D lea      ecx, [esi + 0x3d]
004C9580 mov      eax, dword ptr [ecx]
004C9582 push     0
004C9584 push     1
004C9586 call     dword ptr [eax + 0xc]
004C9589 jmp      0x4c95e9
004C958B test     eax, eax
004C958D jne      0x4c95bc
004C958F mov      ecx, dword ptr [0xb5dce0]
004C9595 mov      dword ptr [ebp - 0x14], eax
004C9598 mov      edx, dword ptr [ecx]
004C959A push     4
004C959C lea      eax, [ebp - 0x14]
004C959F push     eax
004C95A0 call     dword ptr [edx + 0x60]
004C95A3 mov      eax, dword ptr [esi + 4]
004C95A6 test     eax, eax
004C95A8 jne      0x4c95ad
004C95AA lea      eax, [esi + 0x3d]
004C95AD mov      ecx, dword ptr [ebp - 0x14]
004C95B0 mov      edx, dword ptr [eax]
004C95B2 push     ecx
004C95B3 push     1
004C95B5 mov      ecx, eax
004C95B7 call     dword ptr [edx + 0xc]
004C95BA jmp      0x4c95e9
004C95BC mov      eax, dword ptr [esi]
004C95BE test     eax, eax
004C95C0 mov      ecx, eax
004C95C2 jne      0x4c95c7
004C95C4 lea      ecx, [esi + 8]
004C95C7 mov      eax, dword ptr [esi + 4]
004C95CA test     eax, eax
004C95CC jne      0x4c95d1
004C95CE lea      eax, [esi + 0x3d]
004C95D1 mov      edx, dword ptr [ecx]
004C95D3 mov      ebx, dword ptr [eax]
004C95D5 push     1
004C95D7 mov      dword ptr [ebp - 0x14], eax
004C95DA call     dword ptr [edx + 8]
004C95DD push     ecx
004C95DE mov      ecx, dword ptr [ebp - 0x14]
004C95E1 fstp     dword ptr [esp]
004C95E4 push     1
004C95E6 call     dword ptr [ebx + 0xc]
004C95E9 shr      edi, 4
004C95EC and      edi, 3
004C95EF cmp      edi, 1
004C95F2 jne      0x4c9609
004C95F4 mov      ecx, dword ptr [esi + 4]
004C95F7 test     ecx, ecx
004C95F9 jne      0x4c95fe
004C95FB lea      ecx, [esi + 0x3d]
004C95FE mov      eax, dword ptr [ecx]
004C9600 push     0
004C9602 push     2
004C9604 call     dword ptr [eax + 0xc]
004C9607 jmp      0x4c9663
004C9609 test     edi, edi
004C960B jne      0x4c963a
004C960D mov      ecx, dword ptr [0xb5dce0]
004C9613 push     4
004C9615 lea      eax, [ebp - 0x14]
004C9618 mov      dword ptr [ebp - 0x14], edi
004C961B mov      edx, dword ptr [ecx]
004C961D push     eax
004C961E call     dword ptr [edx + 0x60]
004C9621 mov      eax, dword ptr [esi + 4]
004C9624 test     eax, eax
004C9626 jne      0x4c962b
004C9628 lea      eax, [esi + 0x3d]
004C962B mov      ecx, dword ptr [ebp - 0x14]
004C962E mov      edx, dword ptr [eax]
004C9630 push     ecx
004C9631 push     2
004C9633 mov      ecx, eax
004C9635 call     dword ptr [edx + 0xc]
004C9638 jmp      0x4c9663
004C963A mov      eax, dword ptr [esi]
004C963C test     eax, eax
004C963E jne      0x4c9643
004C9640 lea      eax, [esi + 8]
004C9643 mov      edi, dword ptr [esi + 4]
004C9646 test     edi, edi
004C9648 jne      0x4c964d
004C964A lea      edi, [esi + 0x3d]
004C964D mov      edx, dword ptr [eax]
004C964F mov      ebx, dword ptr [edi]
004C9651 push     2
004C9653 mov      ecx, eax
004C9655 call     dword ptr [edx + 8]
004C9658 push     ecx
004C9659 fstp     dword ptr [esp]
004C965C push     2
004C965E mov      ecx, edi
004C9660 call     dword ptr [ebx + 0xc]
004C9663 mov      edi, dword ptr [ebp + 8]
004C9666 shr      edi, 0xe
004C9669 and      edi, 0x3f
004C966C mov      eax, edi
004C966E and      eax, 3
004C9671 xor      ebx, ebx
004C9673 cmp      eax, 1
004C9676 jne      0x4c9687
004C9678 mov      eax, dword ptr [esi + 4]
004C967B cmp      eax, ebx
004C967D jne      0x4c9682
004C967F lea      eax, [esi + 0x3d]
004C9682 mov      dword ptr [eax + 0x1c], ebx
004C9685 jmp      0x4c96cc
004C9687 cmp      eax, ebx
004C9689 jne      0x4c96b1
004C968B mov      ecx, dword ptr [0xb5dce0]
004C9691 push     4
004C9693 lea      edx, [ebp - 0x14]
004C9696 mov      dword ptr [ebp - 0x14], ebx
004C9699 mov      eax, dword ptr [ecx]
004C969B push     edx
004C969C call     dword ptr [eax + 0x60]
004C969F mov      eax, dword ptr [esi + 4]
004C96A2 cmp      eax, ebx
004C96A4 jne      0x4c96a9
004C96A6 lea      eax, [esi + 0x3d]
004C96A9 mov      ecx, dword ptr [ebp - 0x14]
004C96AC mov      dword ptr [eax + 0x1c], ecx
004C96AF jmp      0x4c96cc
004C96B1 mov      eax, dword ptr [esi]
004C96B3 cmp      eax, ebx
004C96B5 mov      ecx, eax
004C96B7 jne      0x4c96bc
004C96B9 lea      ecx, [esi + 8]
004C96BC mov      eax, dword ptr [esi + 4]
004C96BF cmp      eax, ebx
004C96C1 jne      0x4c96c6
004C96C3 lea      eax, [esi + 0x3d]
004C96C6 mov      edx, dword ptr [ecx + 0x1c]
004C96C9 mov      dword ptr [eax + 0x1c], edx
004C96CC mov      eax, edi
004C96CE shr      eax, 2
004C96D1 and      eax, 3
004C96D4 cmp      eax, 1
004C96D7 jne      0x4c96e8
004C96D9 mov      eax, dword ptr [esi + 4]
004C96DC cmp      eax, ebx
004C96DE jne      0x4c96e3
004C96E0 lea      eax, [esi + 0x3d]
004C96E3 mov      dword ptr [eax + 0x20], ebx
004C96E6 jmp      0x4c972d
004C96E8 cmp      eax, ebx
004C96EA jne      0x4c9712
004C96EC mov      ecx, dword ptr [0xb5dce0]
004C96F2 push     4
004C96F4 lea      edx, [ebp - 0x14]
004C96F7 mov      dword ptr [ebp - 0x14], ebx
004C96FA mov      eax, dword ptr [ecx]
004C96FC push     edx
004C96FD call     dword ptr [eax + 0x60]
004C9700 mov      eax, dword ptr [esi + 4]
004C9703 cmp      eax, ebx
004C9705 jne      0x4c970a
004C9707 lea      eax, [esi + 0x3d]
004C970A mov      ecx, dword ptr [ebp - 0x14]
004C970D mov      dword ptr [eax + 0x20], ecx
004C9710 jmp      0x4c972d
004C9712 mov      eax, dword ptr [esi]
004C9714 cmp      eax, ebx
004C9716 mov      ecx, eax
004C9718 jne      0x4c971d
004C971A lea      ecx, [esi + 8]
004C971D mov      eax, dword ptr [esi + 4]
004C9720 cmp      eax, ebx
004C9722 jne      0x4c9727
004C9724 lea      eax, [esi + 0x3d]
004C9727 mov      edx, dword ptr [ecx + 0x20]
004C972A mov      dword ptr [eax + 0x20], edx
004C972D shr      edi, 4
004C9730 and      edi, 3
004C9733 cmp      edi, 1
004C9736 jne      0x4c9747
004C9738 mov      eax, dword ptr [esi + 4]
004C973B cmp      eax, ebx
004C973D jne      0x4c9742
004C973F lea      eax, [esi + 0x3d]
004C9742 mov      dword ptr [eax + 0x24], ebx
004C9745 jmp      0x4c978c
004C9747 cmp      edi, ebx
004C9749 jne      0x4c9771
004C974B mov      ecx, dword ptr [0xb5dce0]
004C9751 push     4
004C9753 lea      edx, [ebp - 0x14]
004C9756 mov      dword ptr [ebp - 0x14], ebx
004C9759 mov      eax, dword ptr [ecx]
004C975B push     edx
004C975C call     dword ptr [eax + 0x60]
004C975F mov      eax, dword ptr [esi + 4]
004C9762 cmp      eax, ebx
004C9764 jne      0x4c9769
004C9766 lea      eax, [esi + 0x3d]
004C9769 mov      ecx, dword ptr [ebp - 0x14]
004C976C mov      dword ptr [eax + 0x24], ecx
004C976F jmp      0x4c978c
004C9771 mov      eax, dword ptr [esi]
004C9773 cmp      eax, ebx
004C9775 mov      ecx, eax
004C9777 jne      0x4c977c
004C9779 lea      ecx, [esi + 8]
004C977C mov      eax, dword ptr [esi + 4]
004C977F cmp      eax, ebx
004C9781 jne      0x4c9786
004C9783 lea      eax, [esi + 0x3d]
004C9786 mov      edx, dword ptr [ecx + 0x24]
004C9789 mov      dword ptr [eax + 0x24], edx
004C978C mov      edi, dword ptr [ebp + 8]
004C978F shr      edi, 8
004C9792 and      edi, 0x3f
004C9795 mov      eax, edi
004C9797 and      eax, 3
004C979A or       ebx, 0xffffffff
004C979D cmp      eax, 3
004C97A0 jne      0x4c97b1
004C97A2 mov      eax, dword ptr [esi + 4]
004C97A5 test     eax, eax
004C97A7 jne      0x4c97ac
004C97A9 lea      eax, [esi + 0x3d]
004C97AC mov      dword ptr [eax + 0x28], ebx
004C97AF jmp      0x4c980e
004C97B1 cmp      eax, 1
004C97B4 jne      0x4c97c9
004C97B6 mov      eax, dword ptr [esi + 4]
004C97B9 test     eax, eax
004C97BB jne      0x4c97c0
004C97BD lea      eax, [esi + 0x3d]
004C97C0 mov      dword ptr [eax + 0x28], 0
004C97C7 jmp      0x4c980e
004C97C9 test     eax, eax
004C97CB jne      0x4c97f3
004C97CD mov      ecx, dword ptr [0xb5dce0]
004C97D3 push     4
004C97D5 lea      edx, [ebp - 0x14]
004C97D8 mov      dword ptr [ebp - 0x14], eax
004C97DB mov      eax, dword ptr [ecx]
004C97DD push     edx
004C97DE call     dword ptr [eax + 0x60]
004C97E1 mov      eax, dword ptr [esi + 4]
004C97E4 test     eax, eax
004C97E6 jne      0x4c97eb
004C97E8 lea      eax, [esi + 0x3d]
004C97EB mov      ecx, dword ptr [ebp - 0x14]
004C97EE mov      dword ptr [eax + 0x28], ecx
004C97F1 jmp      0x4c980e
004C97F3 mov      eax, dword ptr [esi]
004C97F5 test     eax, eax
004C97F7 mov      ecx, eax
004C97F9 jne      0x4c97fe
004C97FB lea      ecx, [esi + 8]
004C97FE mov      eax, dword ptr [esi + 4]
004C9801 test     eax, eax
004C9803 jne      0x4c9808
004C9805 lea      eax, [esi + 0x3d]
004C9808 mov      edx, dword ptr [ecx + 0x28]
004C980B mov      dword ptr [eax + 0x28], edx
004C980E mov      eax, edi
004C9810 shr      eax, 2
004C9813 and      eax, 3
004C9816 cmp      eax, 3
004C9819 jne      0x4c982a
004C981B mov      eax, dword ptr [esi + 4]
004C981E test     eax, eax
004C9820 jne      0x4c9825
004C9822 lea      eax, [esi + 0x3d]
004C9825 mov      dword ptr [eax + 0x2c], ebx
004C9828 jmp      0x4c9887
004C982A cmp      eax, 1
004C982D jne      0x4c9842
004C982F mov      eax, dword ptr [esi + 4]
004C9832 test     eax, eax
004C9834 jne      0x4c9839
004C9836 lea      eax, [esi + 0x3d]
004C9839 mov      dword ptr [eax + 0x2c], 0
004C9840 jmp      0x4c9887
004C9842 test     eax, eax
004C9844 jne      0x4c986c
004C9846 mov      ecx, dword ptr [0xb5dce0]
004C984C push     4
004C984E lea      edx, [ebp - 0x14]
004C9851 mov      dword ptr [ebp - 0x14], eax
004C9854 mov      eax, dword ptr [ecx]
004C9856 push     edx
004C9857 call     dword ptr [eax + 0x60]
004C985A mov      eax, dword ptr [esi + 4]
004C985D test     eax, eax
004C985F jne      0x4c9864
004C9861 lea      eax, [esi + 0x3d]
004C9864 mov      ecx, dword ptr [ebp - 0x14]
004C9867 mov      dword ptr [eax + 0x2c], ecx
004C986A jmp      0x4c9887
004C986C mov      eax, dword ptr [esi]
004C986E test     eax, eax
004C9870 mov      ecx, eax
004C9872 jne      0x4c9877
004C9874 lea      ecx, [esi + 8]
004C9877 mov      eax, dword ptr [esi + 4]
004C987A test     eax, eax
004C987C jne      0x4c9881
004C987E lea      eax, [esi + 0x3d]
004C9881 mov      edx, dword ptr [ecx + 0x2c]
004C9884 mov      dword ptr [eax + 0x2c], edx
004C9887 shr      edi, 4
004C988A and      edi, 3
004C988D mov      eax, edi
004C988F cmp      eax, 3
004C9892 jne      0x4c98a3
004C9894 mov      eax, dword ptr [esi + 4]
004C9897 test     eax, eax
004C9899 jne      0x4c989e
004C989B lea      eax, [esi + 0x3d]
004C989E mov      dword ptr [eax + 0x30], ebx
004C98A1 jmp      0x4c9900
004C98A3 cmp      eax, 1
004C98A6 jne      0x4c98bb
004C98A8 mov      eax, dword ptr [esi + 4]
004C98AB test     eax, eax
004C98AD jne      0x4c98b2
004C98AF lea      eax, [esi + 0x3d]
004C98B2 mov      dword ptr [eax + 0x30], 0
004C98B9 jmp      0x4c9900
004C98BB test     eax, eax
004C98BD jne      0x4c98e5
004C98BF mov      ecx, dword ptr [0xb5dce0]
004C98C5 push     4
004C98C7 lea      edx, [ebp - 0x14]
004C98CA mov      dword ptr [ebp - 0x14], eax
004C98CD mov      eax, dword ptr [ecx]
004C98CF push     edx
004C98D0 call     dword ptr [eax + 0x60]
004C98D3 mov      eax, dword ptr [esi + 4]
004C98D6 test     eax, eax
004C98D8 jne      0x4c98dd
004C98DA lea      eax, [esi + 0x3d]
004C98DD mov      ecx, dword ptr [ebp - 0x14]
004C98E0 mov      dword ptr [eax + 0x30], ecx
004C98E3 jmp      0x4c9900
004C98E5 mov      eax, dword ptr [esi]
004C98E7 test     eax, eax
004C98E9 mov      ecx, eax
004C98EB jne      0x4c98f0
004C98ED lea      ecx, [esi + 8]
004C98F0 mov      eax, dword ptr [esi + 4]
004C98F3 test     eax, eax
004C98F5 jne      0x4c98fa
004C98F7 lea      eax, [esi + 0x3d]
004C98FA mov      edx, dword ptr [ecx + 0x30]
004C98FD mov      dword ptr [eax + 0x30], edx
004C9900 mov      eax, dword ptr [esi + 4]
004C9903 test     eax, eax
004C9905 jne      0x4c990a
004C9907 lea      eax, [esi + 0x3d]
004C990A mov      ecx, dword ptr [ebp + 8]
004C990D mov      bl, byte ptr [ebp - 1]
004C9910 shr      ecx, 5
004C9913 and      cl, 7
004C9916 mov      byte ptr [eax + 0x34], cl
004C9919 mov      eax, dword ptr [esi + 4]
004C991C test     eax, eax
004C991E mov      edi, eax
004C9920 jne      0x4c9925
004C9922 lea      edi, [esi + 0x3d]
004C9925 test     bl, bl
004C9927 je       0x4c994b
004C9929 test     eax, eax
004C992B jne      0x4c9930
004C992D lea      eax, [esi + 0x3d]
004C9930 mov      ecx, dword ptr [esi]
004C9932 test     ecx, ecx
004C9934 jne      0x4c9939
004C9936 lea      ecx, [esi + 8]
004C9939 push     eax
004C993A push     ecx
004C993B mov      ecx, esi
004C993D call     0x4c8b70
004C9942 pop      edi
004C9943 pop      esi
004C9944 pop      ebx
004C9945 mov      esp, ebp
004C9947 pop      ebp
004C9948 ret      4
004C994B mov      eax, 0xfffffff4
004C9950 lea      ebx, [edi + 0x28]
004C9953 sub      eax, edi
004C9955 mov      dword ptr [ebp + 8], 0
004C995C mov      dword ptr [ebp - 0xc], 0x28
004C9963 mov      dword ptr [ebp - 0x1c], ebx
004C9966 mov      dword ptr [ebp - 0x10], eax
004C9969 lea      esp, [esp]
004C9970 mov      ecx, dword ptr [esi]
004C9972 test     ecx, ecx
004C9974 jne      0x4c9979
004C9976 lea      ecx, [esi + 8]
004C9979 mov      eax, dword ptr [ebp + 8]
004C997C mov      edx, dword ptr [ecx]
004C997E push     eax
004C997F call     dword ptr [edx]
004C9981 fstp     dword ptr [ebp - 0x18]
004C9984 mov      eax, dword ptr [ebp + 8]
004C9987 mov      edx, dword ptr [edi]
004C9989 push     eax
004C998A mov      ecx, edi
004C998C call     dword ptr [edx]
004C998E fld      dword ptr [ebp - 0x18]
004C9991 fucompp  
004C9993 fnstsw   ax
004C9995 test     ah, 0x44
004C9998 jp       0x4c99ef
004C999A mov      eax, dword ptr [esi]
004C999C test     eax, eax
004C999E jne      0x4c99a3
004C99A0 lea      eax, [esi + 8]
004C99A3 mov      ebx, dword ptr [eax]
004C99A5 mov      edx, dword ptr [edi]
004C99A7 mov      dword ptr [ebp - 0x14], eax
004C99AA mov      eax, dword ptr [ebp + 8]
004C99AD push     eax
004C99AE mov      ecx, edi
004C99B0 call     dword ptr [edx + 8]
004C99B3 push     ecx
004C99B4 mov      ecx, dword ptr [ebp + 8]
004C99B7 fstp     dword ptr [esp]
004C99BA push     ecx
004C99BB mov      ecx, dword ptr [ebp - 0x14]
004C99BE call     dword ptr [ebx + 0xc]
004C99C1 mov      eax, dword ptr [esi]
004C99C3 test     eax, eax
004C99C5 jne      0x4c99ca
004C99C7 lea      eax, [esi + 8]
004C99CA mov      ebx, dword ptr [ebp - 0x1c]
004C99CD mov      ecx, dword ptr [ebp - 0x10]
004C99D0 lea      edx, [ebx + eax]
004C99D3 mov      eax, dword ptr [ebx - 0xc]
004C99D6 mov      dword ptr [edx + ecx], eax
004C99D9 mov      eax, dword ptr [esi]
004C99DB test     eax, eax
004C99DD jne      0x4c99e2
004C99DF lea      eax, [esi + 8]
004C99E2 mov      edx, dword ptr [ebx]
004C99E4 mov      ecx, dword ptr [ebp - 0xc]
004C99E7 mov      dword ptr [eax + ecx], edx
004C99EA jmp      0x4c9b2b
004C99EF lea      edx, [edi + 0x1c]
004C99F2 test     edx, edx
004C99F4 mov      dword ptr [ebp - 8], 0x1f4
004C99FB jbe      0x4c9a06
004C99FD mov      eax, dword ptr [ebx]
004C99FF test     eax, eax
004C9A01 jle      0x4c9a06
004C9A03 mov      dword ptr [ebp - 8], eax
004C9A06 mov      ecx, dword ptr [ebp + 8]
004C9A09 mov      eax, dword ptr [edi]
004C9A0B push     ecx
004C9A0C mov      ecx, edi
004C9A0E call     dword ptr [eax + 8]
004C9A11 mov      edx, dword ptr [ebx - 0xc]
004C9A14 imul     edx, dword ptr [ebp - 8]
004C9A18 mov      ecx, dword ptr [ebp + 8]
004C9A1B mov      eax, dword ptr [edi]
004C9A1D mov      dword ptr [ebp - 0x1c], edx
004C9A20 push     ecx
004C9A21 mov      ecx, edi
004C9A23 fild     dword ptr [ebp - 0x1c]
004C9A26 fmul     dword ptr [0x99c1c4]
004C9A2C faddp    st(1)
004C9A2E fstp     dword ptr [ebp - 0x14]
004C9A31 fild     dword ptr [ebp - 8]
004C9A34 fstp     dword ptr [ebp - 0x1c]
004C9A37 call     dword ptr [eax + 8]
004C9A3A fadd     dword ptr [ebp - 0x14]
004C9A3D mov      eax, dword ptr [esi]
004C9A3F test     eax, eax
004C9A41 fmul     dword ptr [ebp - 0x1c]
004C9A44 fmul     dword ptr [0x99fdf0]
004C9A4A fstp     dword ptr [ebp - 0x24]
004C9A4D je       0x4c9a54
004C9A4F mov      dword ptr [ebp - 0x18], eax
004C9A52 jmp      0x4c9a5a
004C9A54 lea      edx, [esi + 8]
004C9A57 mov      dword ptr [ebp - 0x18], edx
004C9A5A mov      ecx, dword ptr [ebp + 8]
004C9A5D mov      eax, dword ptr [edi]
004C9A5F push     ecx
004C9A60 mov      ecx, edi
004C9A62 call     dword ptr [eax]
004C9A64 fstp     dword ptr [ebp - 0x20]
004C9A67 mov      ecx, dword ptr [ebp - 0x18]
004C9A6A mov      eax, dword ptr [ebp + 8]
004C9A6D mov      edx, dword ptr [ecx]
004C9A6F push     eax
004C9A70 call     dword ptr [edx]
004C9A72 fsubr    dword ptr [ebp - 0x20]
004C9A75 mov      eax, dword ptr [esi]
004C9A77 test     eax, eax
004C9A79 fadd     dword ptr [ebp - 0x24]
004C9A7C je       0x4c9a83
004C9A7E mov      dword ptr [ebp - 0x18], eax
004C9A81 jmp      0x4c9a89
004C9A83 lea      ecx, [esi + 8]
004C9A86 mov      dword ptr [ebp - 0x18], ecx
004C9A89 mov      eax, dword ptr [ebp - 8]
004C9A8C fld      dword ptr [ebp - 0x1c]
004C9A8F fmul     dword ptr [ebp - 0x14]
004C9A92 mov      edx, eax
004C9A94 imul     edx, eax
004C9A97 fmul     dword ptr [0x99c1c4]
004C9A9D fsub     st(1)
004C9A9F mov      dword ptr [ebp - 0x24], edx
004C9AA2 fadd     st(0), st(0)
004C9AA4 fild     dword ptr [ebp - 0x24]
004C9AA7 fmul     dword ptr [0x99fdec]
004C9AAD fdivp    st(1)
004C9AAF call     0x91b708
004C9AB4 fstp     st(0)
004C9AB6 mov      ecx, dword ptr [ebp - 0x10]
004C9AB9 mov      edx, dword ptr [ebp - 0x18]
004C9ABC add      ecx, ebx
004C9ABE mov      dword ptr [edx + ecx], eax
004C9AC1 mov      eax, dword ptr [esi]
004C9AC3 test     eax, eax
004C9AC5 mov      ecx, eax
004C9AC7 jne      0x4c9acc
004C9AC9 lea      ecx, [esi + 8]
004C9ACC test     eax, eax
004C9ACE mov      dword ptr [ebp - 0x1c], ecx
004C9AD1 jne      0x4c9ad6
004C9AD3 lea      eax, [esi + 8]
004C9AD6 mov      edx, dword ptr [ecx]
004C9AD8 mov      ecx, dword ptr [ebp - 0x10]
004C9ADB add      ecx, ebx
004C9ADD mov      eax, dword ptr [eax + ecx]
004C9AE0 imul     eax, dword ptr [ebp - 8]
004C9AE4 mov      dword ptr [ebp - 0x24], eax
004C9AE7 push     ecx
004C9AE8 mov      ecx, dword ptr [ebp + 8]
004C9AEB fild     dword ptr [ebp - 0x24]
004C9AEE fmul     dword ptr [0x99c1c4]
004C9AF4 fsubr    dword ptr [ebp - 0x14]
004C9AF7 fstp     dword ptr [esp]
004C9AFA push     ecx
004C9AFB mov      ecx, dword ptr [ebp - 0x1c]
004C9AFE call     dword ptr [edx + 0xc]
004C9B01 mov      eax, dword ptr [ebx - 0xc]
004C9B04 test     eax, eax
004C9B06 je       0x4c9b19
004C9B08 mov      ecx, dword ptr [ebx]
004C9B0A test     ecx, ecx
004C9B0C je       0x4c9b19
004C9B0E mov      eax, dword ptr [esi]
004C9B10 test     eax, eax
004C9B12 jne      0x4c9b25
004C9B14 lea      eax, [esi + 8]
004C9B17 jmp      0x4c9b25
004C9B19 mov      eax, dword ptr [esi]
004C9B1B test     eax, eax
004C9B1D jne      0x4c9b22
004C9B1F lea      eax, [esi + 8]
004C9B22 mov      ecx, dword ptr [ebp - 8]
004C9B25 mov      edx, dword ptr [ebp - 0xc]
004C9B28 mov      dword ptr [eax + edx], ecx
004C9B2B mov      eax, dword ptr [ebp - 0xc]
004C9B2E mov      edx, dword ptr [ebp + 8]
004C9B31 add      eax, 4
004C9B34 inc      edx
004C9B35 add      ebx, 4
004C9B38 cmp      eax, 0x34
004C9B3B mov      dword ptr [ebp + 8], edx
004C9B3E mov      dword ptr [ebp - 0xc], eax
004C9B41 mov      dword ptr [ebp - 0x1c], ebx
004C9B44 jl       0x4c9970
004C9B4A mov      eax, dword ptr [esi]
004C9B4C test     eax, eax
004C9B4E jne      0x4c9b53
004C9B50 lea      eax, [esi + 8]
004C9B53 mov      cl, byte ptr [edi + 0x34]
004C9B56 pop      edi
004C9B57 pop      esi
004C9B58 mov      byte ptr [eax + 0x34], cl
004C9B5B pop      ebx
004C9B5C mov      esp, ebp
004C9B5E pop      ebp
004C9B5F ret      4