using System.Buffers.Binary;
using System.Text;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// 同队进副本（组队第二轮）的冒烟测试：实例模型、门口集结状态机、入场报文序列、共享状态广播。
/// 设计与证据见 docs/superpowers/specs/2026-09-29-dungeon-party-design.md 与
/// .superpowers/sdd/2026-09-29-dungeon-party-recon.md。
/// </summary>
internal static class DungeonPartySmokeTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new DungeonFixture();
        CheckSoloInstanceKey(check, fixture);
        CheckInstanceMembershipLifecycle(check, fixture);
        CheckLastMemberDestroysInstance(check, fixture);
        CheckConcurrentKillSettlesOnce(check, fixture);
        CheckRoomSnapshotsAndPassiveDrops(check, fixture);
        CheckLiveJoinDoesNotReplenishPassiveDrops(check, fixture);
        CheckSharedCollectionAccessors(check, fixture);
        CheckRoomSettlementReset(check);
        CheckRoomFirstVisitFlag(check, fixture);
        CheckPartyAdvanceIsFirstRequestWins(check, fixture);
        CheckForceAllMonstersDead(check, fixture);
        CheckPlanStartMapSharedPlanner(check, fixture);
        CheckRegistryEntryRequestMailbox(check, fixture);
        CheckPendingPartyMailLookup(check, fixture);
        CheckKillCountersAreShared(check, fixture);
        CheckChampionRewardOrdinalCap(check, fixture);
        CheckRunStartArbitration(check, fixture);
        CheckLockedRunReadAccessors(check, fixture);
        CheckHellRoomPassiveDetachOnMove(check, fixture);
        CheckSoloRunBindsByCharacterId(check, fixture);
        CheckSoloRoomChangeKeepsImmediateBehaviour(check, fixture);
        CheckAllMemberLoadingBarrier(check, fixture);
        CheckSettlementReturnTimeout(check, fixture);
        CheckSettlementReturnPullsWholeParty(check, fixture);
        await CheckSettlementCountdownAsync(check, fixture);
        CheckPartyDeathCountdown(check, fixture);
        CheckPartyRevivalArbitration(check, fixture);
        CheckPartyEntryAdmissionAfterWipe(check, fixture);
        await CheckPartyDeathBroadcastAndReturnsAsync(check, fixture);
        await EntranceSessionReadWaitSmokeTests.RunAsync(check, fixture);
        CheckSoloLookupNeverReusesPartyBinding(check, fixture);
        CheckStoppedInstanceCannotBeJoined(check, fixture);
        await CheckGatheringValidationAsync(check, fixture);
        await CheckRequestTargetingAsync(check, fixture);
        CheckMemberEntryPacketOrder(check, fixture);
        await CheckBroadcastReachesEveryMemberOnceAsync(check, fixture);
        await CheckPickupBroadcastOmitsSenderAsync(check, fixture);
        await CheckClearRewardAndFinishLoadingPartyDispatchAsync(check, fixture);
        CheckGroundItemClaimIsFirstComeFirstServed(check, fixture);
        await CheckMemberDeparturePacketAsync(check, fixture);
        await DungeonDepartureSmokeTests.RunAsync(check, fixture);
        await CheckGiveUpLeavesPartyAsync(check, fixture);
        await CheckCrossSessionDeliverySurvivesCanceledMembersAsync(check, fixture);
    }

    /// <summary>
    /// 回归真机 2026-10-23：队员先死不应独自回城；官服 die_user 只在全灭后
    /// send_dungeon_fail_msg（10 秒），party_dungeon_fail 到点再次检查全灭。
    /// </summary>
    private static void CheckPartyDeathCountdown(Action<bool, string> check, DungeonFixture fixture)
    {
        var now = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero);
        var leader = Guid.NewGuid();
        var member = Guid.NewGuid();
        var instance = fixture.CreatePartyInstance(1700);
        instance.AddMember(fixture.Member(leader, 0, "队长"));
        instance.AddMember(fixture.Member(member, 1, "队员"));

        check(instance.TrySetPartyMemberAlive(member, false, now)
                && instance.PartyWipeDeadline is null
                && instance.TakeExpiredPartyWipeReturns(now.AddMinutes(1)).Length == 0,
            "a dead member stays in the dungeon while a teammate lives, even beyond the old ten-second timeout");
        check(!instance.TrySetPartyMemberAlive(Guid.NewGuid(), false, now)
                && instance.PartyWipeDeadline is null,
            "an unknown character cannot change the instance's party death state");

        var lastDeath = now.AddMinutes(1);
        check(instance.TrySetPartyMemberAlive(leader, false, lastDeath)
                && instance.PartyWipeDeadline == lastDeath.AddSeconds(10)
                && instance.TakeExpiredPartyWipeReturns(lastDeath.AddSeconds(9)).Length == 0,
            "the party wipe countdown begins at the LAST death and waits ten full seconds");
        instance.TrySetPartyMemberAlive(leader, false, lastDeath.AddSeconds(2));
        check(instance.PartyWipeDeadline == lastDeath.AddSeconds(10),
            "a duplicate death report does not extend the wipe countdown");
        instance.TrySetPartyMemberAlive(member, true, lastDeath.AddSeconds(9));
        check(instance.PartyWipeDeadline is null
                && instance.TakeExpiredPartyWipeReturns(lastDeath.AddSeconds(10)).Length == 0,
            "reviving before expiry cancels the whole party's return");

        var secondDeath = lastDeath.AddSeconds(20);
        instance.TrySetPartyMemberAlive(member, false, secondDeath);
        var expired = instance.TakeExpiredPartyWipeReturns(secondDeath.AddSeconds(10));
        check(expired.Length == 2 && expired.Contains(leader) && expired.Contains(member)
                && instance.ShouldReturnForPartyWipe(leader, secondDeath.AddSeconds(10))
                && instance.ShouldReturnForPartyWipe(member, secondDeath.AddSeconds(10))
                && instance.TakeExpiredPartyWipeReturns(secondDeath.AddSeconds(11)).Length == 0,
            "a wipe returns every member exactly once when its shared deadline expires");
        instance.TrySetPartyMemberAlive(leader, true, secondDeath.AddSeconds(11));
        check(!instance.ShouldReturnForPartyWipe(leader, secondDeath.AddSeconds(11))
                && !instance.ShouldReturnForPartyWipe(member, secondDeath.AddSeconds(11)),
            "reviving after the sweep invalidates already queued return mail for BOTH members");

        // 最后一个活人离开后只剩死者，应开始新的失败倒计时；已有全灭成员依次回城不重开。
        var departure = secondDeath.AddSeconds(30);
        instance.RemoveMember(leader, departure);
        check(instance.PartyWipeDeadline == departure.AddSeconds(10)
                && instance.TakeExpiredPartyWipeReturns(departure.AddSeconds(9)).Length == 0,
            "the last living teammate leaving starts the remaining dead member's ten-second wipe countdown");
        var remaining = instance.TakeExpiredPartyWipeReturns(departure.AddSeconds(10));
        check(remaining.Length == 1 && remaining[0] == member,
            "a departed teammate is excluded from the party wipe return");
        instance.RemoveMember(member, departure.AddSeconds(10));
        check(instance.PartyWipeDeadline is null
                && !instance.ShouldReturnForPartyWipe(member, departure.AddSeconds(20)),
            "the last member leaving cancels all wipe state");

        var sequential = fixture.CreatePartyInstance(1701);
        sequential.AddMember(fixture.Member(leader, 0, "队长"));
        sequential.AddMember(fixture.Member(member, 1, "队员"));
        sequential.TrySetPartyMemberAlive(leader, false, now);
        sequential.TrySetPartyMemberAlive(member, false, now);
        sequential.TakeExpiredPartyWipeReturns(now.AddSeconds(10));
        sequential.RemoveMember(leader, now.AddSeconds(10));
        check(sequential.PartyWipeDeadline == now.AddSeconds(10)
                && sequential.ShouldReturnForPartyWipe(member, now.AddSeconds(10)),
            "the first dead member returning does not restart the survivor's already expired wipe deadline");
        sequential.AddMember(fixture.Member(leader, 0, "返回的队长"));
        check(sequential.PartyWipeDeadline is null
                && !sequential.ShouldReturnForPartyWipe(member, now.AddSeconds(11)),
            "an alive member joining invalidates an earlier wipe return instead of inheriting a stale death flag");

        var reset = fixture.CreatePartyInstance(1702);
        reset.AddMember(fixture.Member(leader, 0, "队长"));
        reset.AddMember(fixture.Member(member, 1, "队员"));
        check(reset.TryStartRun(fixture.Layout, 0, 0, out _), "the party death reset fixture starts");
        reset.TrySetPartyMemberAlive(leader, false, now);
        reset.TrySetPartyMemberAlive(member, false, now);
        reset.TakeExpiredPartyWipeReturns(now.AddSeconds(10));
        reset.StopRun();
        check(reset.PartyWipeDeadline is null
                && !reset.ShouldReturnForPartyWipe(member, now.AddSeconds(20))
                && reset.TryStartRun(fixture.Layout, 0, 0, out _),
            "stopping and restarting a run discards an earlier run's queued wipe");
        reset.TrySetPartyMemberAlive(member, false, now.AddSeconds(21));
        check(reset.PartyWipeDeadline is null,
            "all members start the next run alive, so one new death is not a wipe");

        var concurrent = fixture.CreatePartyInstance(1703);
        concurrent.AddMember(fixture.Member(leader, 0, "队长"));
        concurrent.AddMember(fixture.Member(member, 1, "队员"));
        Parallel.Invoke(
            () => concurrent.TrySetPartyMemberAlive(leader, false, now),
            () => concurrent.TrySetPartyMemberAlive(member, false, now));
        var taken = new int[2];
        Parallel.Invoke(
            () => taken[0] = concurrent.TakeExpiredPartyWipeReturns(now.AddSeconds(10)).Length,
            () => taken[1] = concurrent.TakeExpiredPartyWipeReturns(now.AddSeconds(10)).Length);
        check(taken.Sum() == 2 && taken.Count(count => count == 2) == 1,
            "simultaneous deaths and timer sweeps still issue one complete party return");

        var solo = fixture.CreateSoloInstance(leader);
        solo.AddMember(fixture.Member(leader, 0, "单人"));
        check(!solo.TrySetPartyMemberAlive(leader, false, now)
                && solo.PartyWipeDeadline is null
                && solo.TakeExpiredPartyWipeReturns(now.AddSeconds(20)).Length == 0,
            "solo instances never use the party wipe state machine (their existing session timer remains in charge)");
    }

    private static void CheckPartyRevivalArbitration(Action<bool, string> check, DungeonFixture fixture)
    {
        var now = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero);
        var leader = Guid.NewGuid();
        var member = Guid.NewGuid();

        DungeonInstance WipedParty(ushort partyId)
        {
            var result = fixture.CreatePartyInstance(partyId);
            result.AddMember(fixture.Member(leader, 0, "队长"));
            result.AddMember(fixture.Member(member, 1, "队员"));
            check(result.TryStartRun(fixture.Layout, 0, 0, out _), "the revival arbitration fixture starts");
            result.TrySetPartyMemberAlive(leader, false, now);
            result.TrySetPartyMemberAlive(member, false, now);
            result.TakeExpiredPartyWipeReturns(now.AddSeconds(10));
            return result;
        }

        // 回城先拿到锁：之后不能花币，也不能用晚到的成功通知重新打开实例。
        var returning = WipedParty(1710);
        check(returning.TryBeginPartyWipeReturn(leader, now.AddSeconds(10)),
            "the first expired return atomically claims the entire party wipe");
        var coins = 1;
        if (returning.TryBeginPartyRevival(member, now.AddSeconds(10)))
        {
            coins--;
        }
        check(coins == 1 && !returning.TrySetPartyMemberAlive(member, true, now.AddSeconds(10))
                && !returning.CanAcceptJoin
                && !returning.AddMember(fixture.Member(Guid.NewGuid(), 2, "迟到者")),
            "once town return starts, late revival is rejected before spending coins and late joins cannot reopen the wipe");
        returning.RemoveMember(leader, now.AddSeconds(11));
        check(returning.PartyWipeDeadline == now.AddSeconds(10)
                && returning.TryBeginPartyWipeReturn(member, now.AddSeconds(11)),
            "the sealed party's remaining member still returns after the first member leaves, without a new countdown");

        // 复活先拿到锁：异步保存期间全灭回城必须停住，失败后恢复原期限而不是续十秒。
        var saving = WipedParty(1711);
        check(saving.TryBeginPartyRevival(member, now.AddSeconds(10))
                && !saving.TryBeginPartyRevival(member, now.AddSeconds(10))
                && !saving.TryBeginPartyWipeReturn(leader, now.AddSeconds(11)),
            "a revival reservation wins over the timer while inventory save awaits and cannot be duplicated");
        saving.CancelPartyRevival(member, now.AddSeconds(13));
        check(saving.PartyWipeDeadline == now.AddSeconds(10)
                && saving.TakeExpiredPartyWipeReturns(now.AddSeconds(13)).Length == 2
                && saving.TryBeginPartyWipeReturn(leader, now.AddSeconds(13)),
            "failed coin persistence restores the original expired wipe and requeues mail that was discarded during the save");
        saving.RemoveMember(leader, now.AddSeconds(13));
        check(saving.TryBeginPartyWipeReturn(member, now.AddSeconds(13)),
            "a failed revival cannot split the all-dead party into town and dungeon");

        // 两人都在等待保存，任一成功都让旧全灭作废；都失败则只恢复一次原期限。
        var bothFail = WipedParty(1712);
        check(bothFail.TryBeginPartyRevival(leader, now.AddSeconds(10))
                && bothFail.TryBeginPartyRevival(member, now.AddSeconds(10)),
            "both dead members can reserve their own revival concurrently");
        bothFail.CancelPartyRevival(leader, now.AddSeconds(11));
        check(bothFail.PartyWipeDeadline is null,
            "one failed save cannot return the party while another revival is still in flight");
        bothFail.CancelPartyRevival(member, now.AddSeconds(12));
        check(bothFail.PartyWipeDeadline == now.AddSeconds(10)
                && bothFail.TakeExpiredPartyWipeReturns(now.AddSeconds(12)).Length == 2,
            "when both saves fail, their shared original deadline is restored instead of two new timers");

        var oneSucceeds = WipedParty(1713);
        oneSucceeds.TryBeginPartyRevival(leader, now.AddSeconds(10));
        oneSucceeds.TryBeginPartyRevival(member, now.AddSeconds(10));
        check(oneSucceeds.TrySetPartyMemberAlive(leader, true, now.AddSeconds(11)),
            "successful persistence commits a reserved revival");
        oneSucceeds.CancelPartyRevival(leader, now.AddSeconds(11));
        oneSucceeds.CancelPartyRevival(member, now.AddSeconds(12));
        check(oneSucceeds.PartyWipeDeadline is null
                && !oneSucceeds.TryBeginPartyWipeReturn(member, now.AddSeconds(12)),
            "cleanup after committed revival is harmless and another failed save cannot resurrect the old wipe");
        oneSucceeds.TrySetPartyMemberAlive(leader, false, now.AddSeconds(13));
        check(oneSucceeds.PartyWipeDeadline == now.AddSeconds(23),
            "a genuinely new wipe after successful revival gets its own full ten seconds");

        var concurrent = WipedParty(1714);
        var revivalWon = false;
        var returnWon = false;
        Parallel.Invoke(
            () => revivalWon = concurrent.TryBeginPartyRevival(member, now.AddSeconds(10)),
            () => returnWon = concurrent.TryBeginPartyWipeReturn(leader, now.AddSeconds(10)));
        check(revivalWon != returnWon,
            "simultaneous coin and town-return requests have exactly one winner under the instance lock");

        var solo = fixture.CreateSoloInstance(leader);
        solo.AddMember(fixture.Member(leader, 0, "单人"));
        check(!solo.TryBeginPartyRevival(leader, now)
                && !solo.TryBeginPartyWipeReturn(leader, now),
            "party revival arbitration never handles a solo instance");
    }

    private static void CheckPartyEntryAdmissionAfterWipe(Action<bool, string> check, DungeonFixture fixture)
    {
        var now = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero);
        var leader = Guid.NewGuid();
        var member = Guid.NewGuid();
        var late = Guid.NewGuid();
        DungeonInstance AwaitingWipe(ushort partyId)
        {
            var result = fixture.CreatePartyInstance(partyId);
            result.AddMember(fixture.Member(leader, 0, "队长"));
            result.AddMember(fixture.Member(member, 1, "队员"));
            check(result.TryStartRun(fixture.Layout, 0, 0, out _), "the late-entry admission fixture starts");
            result.TrySetPartyMemberAlive(leader, false, now);
            result.TrySetPartyMemberAlive(member, false, now);
            result.TakeExpiredPartyWipeReturns(now.AddSeconds(10));
            return result;
        }

        var sealedParty = AwaitingWipe(1715);
        check(sealedParty.TryStartRunForEntry(fixture.Layout, 0, 0, out _)
                == DungeonRunEntryDecision.AlreadyActive,
            "before wipe return begins an active run still directs entry through live-join admission");
        sealedParty.TryBeginPartyWipeReturn(leader, now.AddSeconds(10));
        var originalMember = sealedParty.FindMember(member);
        var version = sealedParty.Version;
        check(!sealedParty.TryRegisterMemberForEntry(fixture.Member(member, 1, "旧会话替代者"))
                && ReferenceEquals(sealedParty.FindMember(member), originalMember)
                && sealedParty.Version == version
                && sealedParty.ShouldReturnForPartyWipe(member, now.AddSeconds(10)),
            "sealed entry rejection preserves the original member handle and dead state instead of removing it first");
        check(!sealedParty.TryJoinRunForEntry(fixture.Member(late, 2, "迟到者"), out _)
                && !sealedParty.ContainsMember(late)
                && sealedParty.MemberCount == 2
                && sealedParty.TryStartRunForEntry(fixture.Layout, 0, 0, out _)
                    == DungeonRunEntryDecision.StartFailed,
            "a delayed entry cannot obtain a usable live-join snapshot or bypass the sealed wipe via AlreadyActive");
        sealedParty.RemoveMember(leader, now.AddSeconds(11));
        check(!sealedParty.TryJoinRunForEntry(fixture.Member(leader, 0, "返回的队长"), out _)
                && sealedParty.TryBeginPartyWipeReturn(member, now.AddSeconds(11)),
            "the first member returning cannot rejoin the old sealed run while a teammate still awaits return");

        var joinWins = AwaitingWipe(1716);
        check(joinWins.TryJoinRunForEntry(fixture.Member(late, 2, "及时到场者"), out var room)
                && room.MapId != 0 && joinWins.ContainsMember(late)
                && !joinWins.TryBeginPartyWipeReturn(leader, now.AddSeconds(10)),
            "a successfully admitted living member cancels the queued wipe before the caller changes scenes");

        var concurrent = AwaitingWipe(1717);
        var admitted = false;
        var returnStarted = false;
        Parallel.Invoke(
            () => admitted = concurrent.TryJoinRunForEntry(fixture.Member(late, 2, "并发到场者"), out _),
            () => returnStarted = concurrent.TryBeginPartyWipeReturn(leader, now.AddSeconds(10)));
        check(admitted != returnStarted && concurrent.ContainsMember(late) == admitted,
            "live-entry snapshot plus registration and wipe sealing have exactly one winner under the same instance lock");
    }

    private static async Task CheckPartyDeathBroadcastAndReturnsAsync(
        Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leader = harness.IdOf("阿尔法");
        var member = harness.IdOf("布拉沃");
        var now = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var registry = new DungeonInstanceRegistry();
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, clock, NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(1704);
        var leaderInbox = new List<GameServerPacket>();
        var memberInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(leader, 21, 0, "队长",
            packet => { leaderInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(member, 22, 1, "队员",
            packet => { memberInbox.Add(packet); return ValueTask.CompletedTask; }));
        registry.BindCharacter(leader, instance);
        registry.BindCharacter(member, instance);
        harness.ClearAll();

        check(await service.SetMemberAliveAsync(instance, member, false)
                && leaderInbox.Count == 1 && memberInbox.Count == 1
                && leaderInbox[0].ProtocolId == GameProtocolEngine.DieStateNotification
                && leaderInbox[0].Payload.SequenceEqual(new byte[] { 22, 0, 0 })
                && memberInbox[0].Payload.SequenceEqual(leaderInbox[0].Payload),
            "a party member's authoritative death is broadcast to both clients with the actual uid");
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.SweepInstanceTimersAsync(CancellationToken.None);
        check(!registry.HasPendingMail(leader) && !registry.HasPendingMail(member)
                && instance.ContainsMember(member),
            "the sweep never sends the first dead member back to town while the leader lives");

        await service.SetMemberAliveAsync(instance, leader, false);
        clock.Advance(TimeSpan.FromSeconds(9));
        await service.SweepInstanceTimersAsync(CancellationToken.None);
        check(!registry.HasPendingMail(leader) && !registry.HasPendingMail(member),
            "the party wipe sweep does not return anyone early");
        await service.SetMemberAliveAsync(instance, member, true);
        check(leaderInbox.Last().Payload.SequenceEqual(new byte[] { 22, 0, 1 })
                && memberInbox.Last().Payload.SequenceEqual(new byte[] { 22, 0, 1 }),
            "revival is broadcast to both clients before the caller closes the coin UI");
        clock.Advance(TimeSpan.FromSeconds(2));
        await service.SweepInstanceTimersAsync(CancellationToken.None);
        check(!registry.HasPendingMail(leader) && !registry.HasPendingMail(member),
            "revival cancels both members' automatic return in the service sweep");

        await service.SetMemberAliveAsync(instance, member, false);
        instance.ArmSettlementReturn(leader, clock.GetUtcNow().AddSeconds(10));
        instance.ArmSettlementReturn(member, clock.GetUtcNow().AddSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(10));
        await service.SweepInstanceTimersAsync(CancellationToken.None);
        check(registry.HasPendingMail(leader) && registry.HasPendingMail(member)
                && registry.TryTakeSettlementReturn(leader, out var leaderReason)
                && leaderReason == DungeonPartyService.PartyWipeReturnReason
                && registry.TryTakeSettlementReturn(member, out var memberReason)
                && memberReason == DungeonPartyService.PartyWipeReturnReason,
            "all-dead expiry queues one wakeable return for EVERY member; stale settlement deadlines cannot overwrite the wipe reason");
        await service.SweepInstanceTimersAsync(CancellationToken.None);
        check(!registry.HasPendingMail(leader) && !registry.HasPendingMail(member),
            "later sweeps do not reissue consumed party wipe returns");
        check(harness.Service.GetPartySlots(leader) is { Length: 2 }
                && harness.Service.GetPartySlots(member) is { Length: 2 }
                && harness.Of("阿尔法").Count == 0 && harness.Of("布拉沃").Count == 0,
            "party wipe returns preserve party membership and never issue kick or disconnect notices");
        await service.SetMemberAliveAsync(instance, leader, true);
        check(!instance.ShouldReturnForPartyWipe(member, clock.GetUtcNow()),
            "a revival after return mail is posted prevents another member from consuming a stale return");

        var solo = fixture.CreateSoloInstance(leader);
        var soloPackets = 0;
        solo.AddMember(new DungeonInstanceMember(leader, 21, 0, "单人",
            _ => { soloPackets++; return ValueTask.CompletedTask; }));
        check(!await service.SetMemberAliveAsync(solo, leader, false) && soloPackets == 0,
            "the party death service emits no new packet or timer for a solo dungeon");
    }

    /// <summary>
    /// 掉落拾取的"先到先得"与落库回滚（评审 Important 1/2，设计稿 §7 决策 5）：
    /// ①准入不看归属：阿尔法（uid 1）掉的（OwnerUserId=1）布拉沃（uid 2）照样能赢下；
    /// ②无主掉落（OwnerUserId=0，Task 5 的强制推进会登记这种）同样能捡；
    /// ③同一个 groundId 的并发/重复 claim 只有一个 true（输的一方拿到 false，实体已消失）；
    /// ④落库失败的原样放回：`TryReAddGroundItem` 之后同一个 GroundId/归属还在，别人还能再捡。
    /// **变异验证**（三处模型层变异，实测变红后已回滚）：
    /// ①让 `OwnerUserId` 重新参与准入 → 第一/第二条断言变红；
    /// ②`TryClaimGroundItem` 不看 `GroundItems.Remove` 的返回值 → 第三条变红；
    /// ③`TryReAddGroundItem` 不放回（直接 false）→ 第四条变红。
    /// 会话侧那几行（claim 的返回值决定 continue、落库失败调放回）不可单测——与会话接线同一局限。
    /// </summary>
    private static void CheckGroundItemClaimIsFirstComeFirstServed(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        instance.AddMember(new DungeonInstanceMember(
            alpha, 1, 0, "阿尔法", _ => ValueTask.CompletedTask));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 2, 1, "布拉沃", _ => ValueTask.CompletedTask));
        instance.EnterDetachedRoom(DungeonFixture.Room([DungeonFixture.Monster(uniqueId: 0)]));

        DungeonGroundItem Drop(ushort ownerUserId) => instance.AddGroundItem(
            new DungeonGeneratedDrop(IsGold: true, ItemId: 0, CountOrValue: 100),
            ownerUserId,
            sourceMonsterUniqueId: 0,
            roomX: 0,
            roomY: 0,
            x: 10,
            y: 20);

        // ① 怪物掉落的 OwnerUserId 恒是击杀者（uid 1）：归属只做显示，uid 2 的队友照样赢下。
        var ownedDrop = Drop(ownerUserId: 1);
        check(instance.TryGetGroundItem(ownedDrop.GroundId, 0, 0, out var ownedSeen)
                && ownedSeen.OwnerUserId == 1,
            "a monster drop keeps its owner id for the client-side ownership display");
        check(instance.TryClaimGroundItem(ownedDrop.GroundId, 0, 0, out var ownedClaim)
                && ownedClaim.GroundId == ownedDrop.GroundId
                && ownedClaim.OwnerUserId == 1
                && !instance.TryGetGroundItem(ownedDrop.GroundId, 0, 0, out _),
            "a teammate can claim another member's drop (ownership never gates the pickup)");

        // ② 无主掉落（Task 5 的倒计时强制推进按 ownerUserId=0 登记）也必须能捡。
        var ownerlessDrop = Drop(ownerUserId: 0);
        check(instance.TryClaimGroundItem(ownerlessDrop.GroundId, 0, 0, out var ownerlessClaim)
                && ownerlessClaim.OwnerUserId == 0,
            "an ownerless drop (OwnerUserId 0) can be claimed as well");

        // ③ 并发仲裁：只有一次 true（这就是会话里 continue 与否的唯一判定）。
        var contestedDrop = Drop(ownerUserId: 1);
        var winners = 0;
        Parallel.For(0, 8, iteration =>
        {
            _ = iteration;
            if (instance.TryClaimGroundItem(contestedDrop.GroundId, 0, 0, out _))
            {
                Interlocked.Increment(ref winners);
            }
        });
        check(winners == 1, "eight concurrent claims settle exactly one winner");
        check(!instance.TryClaimGroundItem(contestedDrop.GroundId, 0, 0, out _)
                && !instance.TryGetGroundItem(contestedDrop.GroundId, 0, 0, out _),
            "the loser of the race gets false and the entity is gone (nothing left to persist or broadcast)");

        // ④ 落库失败的原样放回：同一个 GroundId/归属，别人还能再捡。
        // 断言要对"claim 失败"保持可读（别在契约被破坏时抛 NRE）：先落成可空局部再判。
        var returnedDrop = Drop(ownerUserId: 1);
        var claimedForReturn = instance.TryClaimGroundItem(
            returnedDrop.GroundId, 0, 0, out var claimedEntity)
                ? claimedEntity
                : null;
        check(claimedForReturn is not null,
            "the entity is claimed before the pickup persistence runs");
        check(claimedForReturn is not null
                && instance.TryReAddGroundItem(claimedForReturn)
                && instance.TryGetGroundItem(returnedDrop.GroundId, 0, 0, out var restored)
                && restored.GroundId == claimedForReturn.GroundId
                && restored.OwnerUserId == claimedForReturn.OwnerUserId,
            "a failed pickup persistence returns the exact entity (same ground id and owner)");
        check(claimedForReturn is not null && !instance.TryReAddGroundItem(claimedForReturn),
            "returning the same entity twice cannot duplicate the row");
        check(instance.TryClaimGroundItem(returnedDrop.GroundId, 0, 0, out _),
            "the returned entity can be claimed again (first come, first served)");
    }

    /// <summary>
    /// 共享状态广播（设计稿 §3.3）：击杀/房间开放只判定一次（实例的原子方法），
    /// 但**每个成员各收一份**；判定不成立的陈旧上报一份都不发。
    /// 注意：本用例跑的是**模型层的口径复述**（自己在 `Members` 上循环），不经过生产的
    /// `DungeonPartyService.BroadcastToMembersAsync`——生产广播（含 omit 自己）的护栏是同文件的
    /// <see cref="CheckPickupBroadcastOmitsSenderAsync"/>，那里才有可复核的变异验证。
    /// </summary>
    private static async Task CheckBroadcastReachesEveryMemberOnceAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        List<GameServerPacket> alphaInbox = [];
        List<GameServerPacket> bravoInbox = [];
        instance.AddMember(new DungeonInstanceMember(
            alpha, 1, 0, "阿尔法", packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 2, 1, "布拉沃", packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; }));
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the fixture run starts for the broadcast case");
        var victim = instance.AliveMonsters.First();
        // 任务书原文这里传 `championRewardOrdinal: 0`：0 既不是"无奖励"哨兵（-1）也不在 1..11，
        // `CreateMonsterDie` 会抛 ArgumentOutOfRangeException（实测）。普通怪用引擎的哨兵常量。
        var monsterDie = GameProtocolEngine.CreateMonsterDie(
            uniqueId: victim, drops: [], championRewardOrdinal: GameProtocolEngine.NoChampionRewardOrdinal);
        // 模拟"两个会话都上报了同一只怪"：只有第一次判定成立，但成立的这一次要发给**每个人**。
        for (var round = 0; round < 2; round++)
        {
            if (!instance.TryTakeMonsterDeath(victim, out _))
            {
                continue;
            }

            foreach (var member in instance.Members)
            {
                await member.Send(monsterDie);
            }
        }

        check(alphaInbox.Count == 1 && bravoInbox.Count == 1
                && alphaInbox[0].ProtocolId == GameProtocolEngine.MonsterDieNotification
                && bravoInbox[0].ProtocolId == GameProtocolEngine.MonsterDieNotification,
            "a single kill is broadcast once to every member and stale reports broadcast nothing");
    }

    /// <summary>
    /// 拾取广播的 omit 语义（任务书 Step 4③的硬要求）：`BroadcastToMembersAsync` 只发给
    /// "除 omit 以外"的成员——拾取者那一份已经走本会话的 `stream` 发过，**绝不能再收第二份**。
    /// 会话接线本身没有单测（Task 2 已记），这里是能自动化的那一半证据。
    /// **变异验证**：把 `BroadcastToMembersAsync` 里的 omit 判断删掉 → 第一条断言变红。
    /// </summary>
    private static async Task CheckPickupBroadcastOmitsSenderAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            new DungeonInstanceRegistry(),
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(1000);
        var picker = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var pickerInbox = new List<GameServerPacket>();
        var peerInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            picker, 1, 0, "拾取者", packet => { pickerInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            peer, 2, 1, "队友", packet => { peerInbox.Add(packet); return ValueTask.CompletedTask; }));
        var pickup = GameProtocolEngine.CreateGoldItemPickup(
            groundId: 7, pickerUserId: 1, goldAmount: 100, pickerPartyIndex: 0);

        await service.BroadcastToMembersAsync(instance, pickup, omitCharacterId: picker);

        check(pickerInbox.Count == 0,
            "the picker's own broadcast copy is omitted (it already went out on the session stream)");
        check(peerInbox.Count == 1 && peerInbox[0].ProtocolId == GameProtocolEngine.GetItemNotification,
            "the other members receive exactly one pickup notification");
        // 不 omit 时两个成员各收一份：证明上面那次"0 份"确实来自 omit，而不是投递被吞了。
        await service.BroadcastToMembersAsync(instance, pickup);
        check(pickerInbox.Count == 1 && peerInbox.Count == 2,
            "without an omit target the same packet reaches every member (no delivery is swallowed)");
    }

    /// <summary>
    /// 结算期两条"按队伍派发"的模型级断言（真机 ⑧ 的两条修复，参考实现 opendnf-win-0.0.3 的日志证据）：
    /// ① CMD 74 选牌快照：发给发包者自己的那份回包会被**原样**（同一包实例）广播给同实例其他成员、
    ///    omit 自己；快照里被选中的 free 列标的是**发包者自己的槽位**（参考日志："select_card: pick
    ///    recorded, cmd 74 broadcast unique_id=3 party_index=0 card_type=1 card_number=0 member_slot=0"）；
    /// ② CMD 40 完成加载：其他成员先收**属于他自己的那份名册**（`GetRosterPacketFor` 按收件人解析槽位），
    ///    紧接着收一条空载荷 NOTI 30（参考日志："finish loading dispatched to party handler=..." /
    ///    "dispatch_finish_loading: found party user_idx=2 party_index=0"）。顺序必须名册 → NOTI 30：
    ///    客户端在收到 NOTI 30 那一刻按 uid 查本地 user 表装队伍 HUD，记录晚到 HUD 就永久空着。
    /// 覆盖到的生产件：`GameProtocolEngine` 的两个构建器、`DungeonPartyService.BroadcastToMembersAsync`
    /// （omit 语义）、`PartyService.GetRosterPacketFor`、`CrossSessionDelivery`。
    /// **会话接线本身不可触达**：EntranceService 里"仅 `IsParty` 时调这几步、且名册循环在 NOTI 30 广播
    /// 之前"是会话局部闭包（与 CheckBroadcastReachesEveryMemberOnceAsync / CheckGroundItemClaimIsFirstComeFirstServed
    /// 同一局限）——本用例复述的是那两步的**顺序契约**与可测的生产件，不假装覆盖接线。
    /// **变异验证**（实测变红后回滚）：
    /// ① 把 `GetRosterPacketFor` 改成对 `Guid.Empty` 建名册（收件人自己那格不再走他的通道 uid）→
    ///    "名册逐人取件"断言变红；
    /// ② 把 `BroadcastToMembersAsync` 的 omit 判断删掉 → "发包者零额外包"两条断言变红
    ///    （与 CheckPickupBroadcastOmitsSenderAsync 钉的是同一条护栏）。
    /// </summary>
    private static async Task CheckClearRewardAndFinishLoadingPartyDispatchAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            new DungeonInstanceRegistry(),
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);

        // ① CMD 74：发包者 = slot 1、选走 free 列 0——与 SendCardSelectionSnapshotAsync 同一构建器、
        // 同一槽位口径（localPartyIndex: PartySlotIndexOfThisSession()）。
        var selectionInstance = fixture.CreatePartyInstance(2000);
        var picker = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var pickerInbox = new List<GameServerPacket>();
        var peerInbox = new List<GameServerPacket>();
        selectionInstance.AddMember(new DungeonInstanceMember(
            picker, 1, 0, "队长", packet => { pickerInbox.Add(packet); return ValueTask.CompletedTask; }));
        selectionInstance.AddMember(new DungeonInstanceMember(
            peer, 2, 1, "队员", packet => { peerInbox.Add(packet); return ValueTask.CompletedTask; }));
        var selectionSnapshot = GameProtocolEngine.CreateSelectCardReply(
            freeCardIndex: 0,
            goldCardIndex: null,
            goldRewardRevealed: false,
            goldRewardItemId: null,
            localPartyIndex: 1);
        // 布局：u8 1 + 每列(free 归属 u8, gold 归属 u8, gold 预览 u8)；列 0 的 free 归属 = 发包者槽位。
        check(selectionSnapshot.Payload.Length == 13
                && selectionSnapshot.Payload[0] == 1
                && selectionSnapshot.Payload[1] == 1
                && selectionSnapshot.Payload[2] == byte.MaxValue
                && selectionSnapshot.Payload[4] == byte.MaxValue,
            "the CMD 74 snapshot marks the picked free column with the picker's own party slot");

        await service.BroadcastToMembersAsync(
            selectionInstance, selectionSnapshot, omitCharacterId: picker);
        check(pickerInbox.Count == 0,
            "the picker gets no second CMD 74 copy (its own reply already went out on the session stream)");
        check(peerInbox.Count == 1
                && ReferenceEquals(peerInbox[0], selectionSnapshot)
                && peerInbox[0].ProtocolId == GameProtocolEngine.SelectCardCommand,
            "the other member receives the picker's exact CMD 74 snapshot (the same packet instance)");

        // 单人零回归的结构面：单人实例 IsParty=false，接线里的广播分支进不去；即使误调广播，
        // omit 自己也让唯一成员零投递。
        var soloPicker = Guid.NewGuid();
        var soloInstance = fixture.CreateSoloInstance(soloPicker);
        var soloInbox = new List<GameServerPacket>();
        soloInstance.AddMember(new DungeonInstanceMember(
            soloPicker, 1, 0, "独行", packet => { soloInbox.Add(packet); return ValueTask.CompletedTask; }));
        check(!soloInstance.IsParty,
            "a solo instance can never enter the CMD 74 broadcast branch");
        await service.BroadcastToMembersAsync(soloInstance, selectionSnapshot, omitCharacterId: soloPicker);
        check(soloInbox.Count == 0,
            "a solo instance's broadcast omits its only member (not a single packet added)");

        // ② CMD 40：真实队伍（名册状态与槽位走 PartyService 真实路径）。把"布拉沃"的**通道** uid
        // 换成副本内的 44（目录里仍是城镇 uid 2）：这样"布拉沃自己那份名册"与"阿尔法视角的名册"
        // 必须在布拉沃那一格上不同——正好证明派发是**逐人取件**，而不是把同一个名册包广播出去。
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var alphaId = harness.IdOf("阿尔法");
        var bravoId = harness.IdOf("布拉沃");
        var alphaInbox = new List<GameServerPacket>();
        var bravoInbox = new List<GameServerPacket>();
        await harness.Service.AttachChannelAsync(
            "alice",
            "阿尔法",
            alphaId,
            harness.UidOf("阿尔法"),
            harness.SessionIdOf("阿尔法"),
            packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; },
            CancellationToken.None);
        await harness.Service.AttachChannelAsync(
            "bobby",
            "布拉沃",
            bravoId,
            44,
            harness.SessionIdOf("布拉沃"),
            packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; },
            CancellationToken.None);

        var loadingInstance = fixture.CreatePartyInstance(3000);
        loadingInstance.AddMember(new DungeonInstanceMember(
            alphaId, harness.UidOf("阿尔法"), 0, "阿尔法",
            packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; }));
        loadingInstance.AddMember(new DungeonInstanceMember(
            bravoId, 44, 1, "布拉沃",
            packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; }));

        // 复述 CMD 40 处理体的两步：先给每个其他成员重播他自己的名册，再广播 NOTI 30（omit 发包者）。
        foreach (var member in loadingInstance.Members)
        {
            if (member.CharacterId == alphaId)
            {
                continue;
            }

            var memberRoster = harness.Service.GetRosterPacketFor(member.CharacterId);
            if (memberRoster is null)
            {
                continue;
            }

            await CrossSessionDelivery.SendSafelyAsync(
                NullLogger.Instance,
                memberRoster,
                CrossSessionDelivery.Describe(
                    member.CharacterId, member.CharacterName, member.AreaUserId),
                member.Send);
        }

        await service.BroadcastToMembersAsync(
            loadingInstance,
            GameProtocolEngine.CreateFinishLoading(),
            alphaId,
            CancellationToken.None);

        var bravoRosterIndex = bravoInbox.FindIndex(
            packet => packet.ProtocolId == GameProtocolEngine.PartyInfoNotification);
        var bravoFinishIndex = bravoInbox.FindIndex(
            packet => packet.ProtocolId == GameProtocolEngine.FinishLoadingNotification);
        var bravoRoster = bravoRosterIndex >= 0 ? bravoInbox[bravoRosterIndex] : null;
        var bravoFinish = bravoFinishIndex >= 0 ? bravoInbox[bravoFinishIndex] : null;
        check(bravoInbox.Count == 2 && bravoRosterIndex == 0 && bravoFinishIndex == 1,
            "the other member receives exactly two packets: its own roster, then NOTI 30");
        check(bravoFinish is { } finishPacket && finishPacket.Payload.Length == 0,
            "the dispatched NOTI 30 is the empty-payload notification the client HUD rebuild listens on");

        // 名册布局：u16 1, u16 partyId, u8 0, u8 nameSel, u8 maxMembers, 4 × (u16 uid, u8 0), u8 leaderSlot
        // → 槽位 slot 的 uid 在偏移 7 + 3 * slot。
        static ushort SlotUid(GameServerPacket? roster, int slot) =>
            roster is { } packet && packet.Payload.Length >= 7 + 3 * 4
                ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload.AsSpan(7 + 3 * slot, 2))
                : ushort.MaxValue;
        var alphaViewRoster = harness.Service.GetRosterPacketFor(alphaId);
        check(SlotUid(bravoRoster, 1) == 44
                && SlotUid(alphaViewRoster, 1) == harness.UidOf("布拉沃")
                && !ReferenceEquals(bravoRoster, alphaViewRoster),
            "the roster replay is built per recipient: each member's own slot resolves against their own channel uid");
        check(alphaInbox.Count == 0,
            "the CMD 40 sender gets no extra packet from the party dispatch (its own two went out on its stream)");
    }

    /// <summary>
    /// I2（评审 Important 2）：入场中止路径（`TryStartRun` 失败 / 地狱票被拒 / 票落库失败）会留下
    /// "有成员、run 已停"的实例；那种实例**不可加入**，且取当前房间快照必须返回 false 而不是抛
    /// （异常逃到会话外层 catch 会把会话打断）。
    /// **变异验证**：① 把 `CanAcceptJoin` 里的 `_run.IsActive` 去掉 → 第三条断言变红；
    /// ② 把 `TryCaptureCurrentRoomSnapshot` 改回"没有房间就抛" → 第一条/第四条断言变红（异常）。
    /// </summary>
    private static void CheckStoppedInstanceCannotBeJoined(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), slot: 0, "队长"));
        check(!instance.CanAcceptJoin
                && !instance.TryCaptureCurrentRoomSnapshot(ownerUserId: 1, out _),
            "an instance whose run never started cannot be joined and has no current room");
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the fixture instance starts a run");
        check(instance.CanAcceptJoin
                && instance.TryCaptureCurrentRoomSnapshot(ownerUserId: 1, out var liveRoom)
                && liveRoom.RoomX == instance.Run.RoomX
                && liveRoom.RoomY == instance.Run.RoomY,
            "a live instance accepts joins and hands out its current room");
        instance.StopRun();
        check(!instance.CanAcceptJoin,
            "stopping the run closes the instance to joins");
        check(!instance.TryCaptureCurrentRoomSnapshot(ownerUserId: 1, out _),
            "a stopped instance reports no current room instead of throwing");
    }

    /// <summary>
    /// 硬规则 2（Task 2 评审遗留项）：`_byCharacter` 同时被 `BindCharacter` 用于队员（指向**队伍**实例），
    /// `GetOrCreateForSolo` **只许复用单人实例**——命中队伍实例时必须另建单人实例，
    /// 否则单人入场会把自己塞进队伍实例，房间/掉落/成员广播被两队共用。
    /// **变异验证**：把 `GetOrCreateForSolo` 改回 `_byCharacter.GetOrAdd`，本用例第一条变红。
    /// </summary>
    private static void CheckSoloLookupNeverReusesPartyBinding(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        var party = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        registry.BindCharacter(characterId, party);
        var solo = registry.GetOrCreateForSolo(
            characterId, () => fixture.CreateSoloInstance(characterId));
        check(!solo.IsParty && !ReferenceEquals(solo, party) && solo.SoloCharacterId == characterId,
            "GetOrCreateForSolo never hands out the party instance a character is bound to");
        check(ReferenceEquals(
                solo,
                registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId))),
            "the solo instance is still reused for that character");
    }

    /// <summary>
    /// 同区域校验（用户硬性要求，设计稿 §3.2）：队长 `CMD 16` 时逐人核对 `(town, area)`，
    /// 任一队员不在同一区域 → 整队放弃（只记日志、谁的状态都不改）。
    /// </summary>
    private static async Task CheckGatheringValidationAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        // BackgroundService 不实现 IAsyncDisposable（.NET 10 实测 CS8417），用同步 Dispose。
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leader = harness.IdOf("阿尔法");
        var member = harness.IdOf("布拉沃");
        var gathering = service.TryGetGatheringParty(leader, townId: 1, areaId: 0, out var outsiders);
        check(gathering is { Length: 2 } && outsiders.Length == 0,
            "a party standing in the same area passes the gathering check");

        // 把布拉沃挪到别的区域（目录是权威），队长再校验 → 整队放弃。
        await harness.MoveToAreaAsync("布拉沃", townId: 1, areaId: 1);
        var blocked = service.TryGetGatheringParty(leader, townId: 1, areaId: 0, out var blockedOutsiders);
        check(blocked is null && blockedOutsiders.Length == 1 && blockedOutsiders[0] == member,
            "one member outside the leader's area aborts the whole party entry");
        _ = fixture;
    }

    /// <summary>
    /// S4 核心不变量（官服 `CParty::move_map` @0x085a8f4e(987)，伪码 `official/ida_targeted_out.txt:362-454`）：
    /// **一次合法请求推进整队，不等第二个人**。官服那个函数里**没有任何"全员到门口"的条件**，
    /// 推进只看 `CBattle_Field::MoveMap(field, msg[13], msg[14], &out)` 对请求者自己目标的判定。
    /// 本用例钉三件事：
    /// ① 两人队里**第一个**请求（房 (0,0) → (1,0)）就推进成功 —— **判别性就在这里**：
    ///    谁要是重新引入"等其他人到门口"的判定（自研宽限窗口/集结状态机），第一条立刻变红；
    /// ② 推进后再拿**同一个目标**请求 → 失败 + 错误码 4（陈旧/重复，房间不再变化）；
    /// ③ 目标不在迷宫内 → 失败 + 错误码 4。
    /// **变异验证**：把 `TryAdvanceRoom` 改成"成员数 > 1 时先返回 false（等其他人）"→ 第一条变红。
    /// </summary>
    private static void CheckPartyAdvanceIsFirstRequestWins(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        var instance = fixture.CreatePartyInstance(1800);
        var captain = Guid.NewGuid();
        var member = Guid.NewGuid();
        instance.AddMember(fixture.Member(captain, slot: 0, "队长"));
        instance.AddMember(fixture.Member(member, slot: 1, "队员"));
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the fixture party run starts in room (0,0)");

        // ① 第一个请求即推进（队里有两个人，但没有任何"等第二个人"的条件）。
        check(instance.TryAdvanceRoom(1, 0, out var firstTransition, out var firstError)
                && firstError == 0
                && firstTransition is not null
                && instance.Snapshot().RoomX == 1
                && instance.Snapshot().RoomY == 0,
            "the FIRST valid request advances the whole party (official move_map has no all-members-at-door gate)");

        // ② 同一目标再请求（此时已是当前房间）→ 失败 + 4，房间不再变化。
        check(!instance.TryAdvanceRoom(1, 0, out _, out var duplicateError)
                && duplicateError == 4
                && instance.Snapshot().RoomX == 1,
            "a stale/duplicate request is rejected with error 4 and never moves the room twice");

        // ③ 目标不在迷宫内 → 失败 + 4。
        check(!instance.TryAdvanceRoom(2, 1, out _, out var invalidError)
                && invalidError == 4,
            "a target outside the maze is rejected with error 4");
    }

    /// <summary>
    /// 请求投递的成员过滤（Task 3 评审 Minor 4）：**队长自己不被投递**；不在同一区域的队员不被投递
    /// （异区时整队 abort，两条投递路径都返回 0）。全部走 `PartyHarness` 的真实组队/换区流程。
    /// **变异验证**：把投递循环里的"跳过队长"判断去掉 → 第一条/第二条断言变红；
    /// 把同区域校验换成"永远放行" → 第三条/第四条断言变红。
    /// </summary>
    private static async Task CheckRequestTargetingAsync(Action<bool, string> check, DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        using var service = new DungeonPartyService(
            harness.Service, harness.Directory, registry, TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var leader = harness.IdOf("阿尔法");
        var member = harness.IdOf("布拉沃");
        var instance = fixture.CreatePartyInstance(1000);

        check(service.PostEntryRequests(instance, 14, 0, 0, leader, townId: 1, areaId: 0) == 1
                && !registry.TryTakeEntryRequest(leader, out _)
                && registry.TryTakeEntryRequest(member, out var entry)
                && entry.LeaderCharacterId == leader,
            "an entry request goes to the other member only, never to the leader");
        check(service.PostSelectionRequests(leader, townId: 1, areaId: 0) == 1
                && !registry.TryTakeSelectionRequest(leader, out _)
                && registry.TryTakeSelectionRequest(member, out var selectionLeader)
                && selectionLeader == leader,
            "a selection request goes to the other member only, never to the leader");

        // 异区：整队放弃，两条投递路径都不投任何人（谁的状态都不改）。
        await harness.MoveToAreaAsync("布拉沃", townId: 1, areaId: 1);
        check(service.PostEntryRequests(instance, 14, 0, 0, leader, townId: 1, areaId: 0) == 0
                && !registry.TryTakeEntryRequest(leader, out _)
                && !registry.TryTakeEntryRequest(member, out _),
            "an outsider blocks the whole party: no entry request is posted");
        check(service.PostSelectionRequests(leader, townId: 1, areaId: 0) == 0
                && !registry.TryTakeSelectionRequest(leader, out _)
                && !registry.TryTakeSelectionRequest(member, out _),
            "an outsider blocks the whole party: no selection request is posted");
    }

    /// <summary>
    /// 结算期 EPLP 超时（官服同款；参考实现日志逐字 `town return: eplp timer 20`）：
    /// 结算包发出时登记到点时间，到点由扫掠取走并投"强制回城"信箱（只取一次）；
    /// 换房与成员离开都让排队中的到点时间作废。真机 2026-09-30：队员结算页卡死、一个包都不发，
    /// 只认客户端 EPLP 就永远回不去。
    /// </summary>
    private static void CheckSettlementReturnTimeout(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        instance.AddMember(fixture.Member(memberA, slot: 0, "队长"));
        instance.AddMember(fixture.Member(memberB, slot: 1, "队员"));
        var now = DateTimeOffset.UtcNow;
        instance.ArmSettlementReturn(memberA, now.AddSeconds(20));
        instance.ArmSettlementReturn(memberB, now.AddSeconds(20));
        check(instance.TakeExpiredSettlementReturns(now.AddSeconds(19)).Length == 0,
            "a settlement return deadline that has not expired yet is kept");
        var expired = instance.TakeExpiredSettlementReturns(now.AddSeconds(21));
        check(expired.Length == 2 && expired.Contains(memberA) && expired.Contains(memberB),
            "expired settlement return deadlines hand back every pending member");
        check(instance.TakeExpiredSettlementReturns(now.AddSeconds(22)).Length == 0,
            "an expired settlement return deadline is taken exactly once");

        instance.ArmSettlementReturn(memberA, now.AddSeconds(20));
        instance.ResetMemberLoadingForRoomChange();
        check(instance.TakeExpiredSettlementReturns(now.AddSeconds(21)).Length == 0,
            "a room change cancels queued settlement returns (the new room settles on its own)");

        instance.ArmSettlementReturn(memberB, now.AddSeconds(20));
        instance.RemoveMember(memberB);
        check(instance.TakeExpiredSettlementReturns(now.AddSeconds(21)).Length == 0,
            "a departing member drops their queued settlement return");
    }

    /// <summary>
    /// 结算期最后 10 秒的聊天框倒计时（用户 2026-09-30 真机反馈的原版行为：自动返回前 10 秒
    /// 在聊天框逐秒发系统消息）。**只播 10..1 各一次**、到点的不播（那条走强制回城）、
    /// 成员离开（deadline 被取消）后不再播，且只发给本人（别人的聊天框不受影响）。
    /// 机制用既有且真机验过的系统消息形态：NOTI 12 + `Type16Brown` + **目标 uid 0**（无发言人前缀）。
    /// **变异验证**：把"同一个整秒只播一次"的判定去掉 → 第二条变红；
    /// 把窗口判断改成"所有 pending 都播" → 第一条变红。
    /// </summary>
    private static async Task CheckSettlementCountdownAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        // ---- A) 模型层：窗口/去重/到点 ----
        var instance = fixture.CreatePartyInstance(1600);
        var captain = Guid.NewGuid();
        var member = Guid.NewGuid();
        instance.AddMember(fixture.Member(captain, slot: 0, "队长"));
        instance.AddMember(fixture.Member(member, slot: 1, "队员"));
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _), "the fixture run starts for the countdown case");
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        check(DungeonPartyService.SettlementReturnTimeout == TimeSpan.FromMinutes(2),
            "the settlement auto-return waits the original two minutes (user report), not the reference build's twenty seconds");
        check(DungeonPartyService.SettlementCountdownSeconds == 10,
            "the chat countdown covers the last ten seconds");
        instance.ArmSettlementReturn(member, now.AddSeconds(DungeonPartyService.SettlementReturnTimeout.TotalSeconds));

        check(instance.TakeSettlementCountdowns(now, DungeonPartyService.SettlementCountdownSeconds).Length == 0,
            "a settlement deadline far away announces nothing");
        var ten = instance.TakeSettlementCountdowns(now.AddSeconds(110), DungeonPartyService.SettlementCountdownSeconds);
        check(ten.Length == 1 && ten[0].CharacterId == member && ten[0].RemainingSeconds == 10,
            "entering the last ten seconds announces ten exactly once");
        check(instance.TakeSettlementCountdowns(now.AddSeconds(110.4), DungeonPartyService.SettlementCountdownSeconds)
                .Length == 0,
            "the same whole second is never announced twice");
        var nine = instance.TakeSettlementCountdowns(now.AddSeconds(111), DungeonPartyService.SettlementCountdownSeconds);
        check(nine.Length == 1 && nine[0].RemainingSeconds == 9, "the following second announces nine");
        check(instance.TakeSettlementCountdowns(now.AddSeconds(120), DungeonPartyService.SettlementCountdownSeconds)
                .Length == 0,
            "an expired deadline is not announced (it goes to the forced town return instead)");
        check(instance.TakeExpiredSettlementReturns(now.AddSeconds(120)).Length == 1,
            "the expired deadline is still handed to the forced town return");

        // 成员离开（解绑）后 deadline 被取消：不再有任何播报。
        instance.ArmSettlementReturn(captain, now.AddSeconds(5));
        instance.RemoveMember(captain);
        check(instance.TakeSettlementCountdowns(now, DungeonPartyService.SettlementCountdownSeconds).Length == 0,
            "a departed member's settlement deadline is cancelled (nothing is announced)");

        // ---- B) 服务层：扫描把系统消息发给**该成员本人**，别人一份都不发 ----
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        var clock = new ManualTimeProvider(now);
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            registry,
            clock,
            NullLogger<DungeonPartyService>.Instance);
        var sweepInstance = fixture.CreatePartyInstance(1601);
        var sweepCaptain = Guid.NewGuid();
        var sweepMember = Guid.NewGuid();
        var captainInbox = new List<GameServerPacket>();
        var memberInbox = new List<GameServerPacket>();
        sweepInstance.AddMember(new DungeonInstanceMember(
            sweepCaptain, 1, 0, "队长", packet => { captainInbox.Add(packet); return ValueTask.CompletedTask; }));
        sweepInstance.AddMember(new DungeonInstanceMember(
            sweepMember, 2, 1, "队员", packet => { memberInbox.Add(packet); return ValueTask.CompletedTask; }));
        registry.BindCharacter(sweepCaptain, sweepInstance);
        sweepInstance.ArmSettlementReturn(sweepMember, now.AddSeconds(5));

        await service.SweepInstanceTimersAsync(CancellationToken.None);

        var notice = memberInbox.Find(packet => packet.ProtocolId == GameProtocolEngine.MessageNotification);
        check(notice is not null
                && notice.Payload[0] == (byte)GameMessageType.Type16Brown
                && BinaryPrimitives.ReadUInt16LittleEndian(notice.Payload.AsSpan(1, 2)) == 0,
            "the sweep sends the countdown as a system chat line (no speaker prefix) to the member whose deadline is near");
        check(notice is not null
                && Encoding.GetEncoding(936).GetString(notice.Payload[7..]).Contains('5')
                && Encoding.GetEncoding(936).GetString(notice.Payload[7..]).Contains("返回城镇"),
            "the countdown line carries the remaining seconds and where the player is going");
        check(captainInbox.All(packet => packet.ProtocolId != GameProtocolEngine.MessageNotification),
            "the other member's chat box is left alone");
    }

    /// <summary>
    /// 官服：**一个人在篝火页选"返回城镇" → 全队一起回城**（不是各自等自己的超时）。
    /// `[静态]` 依据两处：① `CParty::ReturnToVillage` @0x085aca60 的组队分支是**按槽位循环**
    /// （`for i in 0..3: CParty::_checkValidUser(i) → CUser::make_basic_info(member[i]) →
    /// GameWorld::out_from_dungeon(member[i])`，循环增量在 0x085acf1d-0x085acf2a，成员指针 = `party+0x78+i*0x18`）；
    /// ② `Dispatcher_EPLPCommand::isReTurnToVillage` @0x0825f7f4 只判"这个人在副本里"
    /// （`CUser::getMoveSpace() == 1` → 返回 1，0x0825f828）——**不判是不是队长**。
    /// 所以我们把"其余成员"各投一次强制回城信箱（会话取走后走同一条 `ReturnToTownFromDungeonAsync`）。
    /// **不再额外区分"是不是通关后"**（用户 2026-09-30 决定，理由见 `PostSettlementReturnsForOthers` 注释）：
    /// 本机客户端 ESC 的返回走 `CMD 45`（那条是退队语义、不经过这里），EPLP 只在结算页出现。
    /// **变异验证**：把 `IsParty` 前置判断去掉 → 单人那条变红；把"跳过发起者"去掉 → 第一条变红。
    /// </summary>
    private static void CheckSettlementReturnPullsWholeParty(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var instance = fixture.CreatePartyInstance(1700);
        var captain = Guid.NewGuid();
        var member = Guid.NewGuid();
        instance.AddMember(fixture.Member(captain, slot: 0, "队长"));
        instance.AddMember(fixture.Member(member, slot: 1, "队员"));
        registry.BindCharacter(captain, instance);
        registry.BindCharacter(member, instance);

        var pulled = DungeonPartyService.PostSettlementReturnsForOthers(registry, captain, "settlement-party-return");

        check(pulled == 1 && !registry.TryTakeSettlementReturn(captain, out _),
            "the member who chose to return is not queued again (he is already returning on his own path)");
        check(registry.TryTakeSettlementReturn(member, out var reason)
                && reason == "settlement-party-return",
            "the other party member is queued for the same town return (official ReturnToVillage loops every slot)");
        check(!registry.TryTakeSettlementReturn(member, out _),
            "the queued return is taken exactly once");

        // 单人副本不参与：单人会话本来就是自己回城（且扫掠对非组队实例整体早退）。
        var soloOwner = Guid.NewGuid();
        var solo = fixture.CreateSoloInstance(soloOwner);
        solo.AddMember(fixture.Member(soloOwner, slot: 0, "独狼"));
        registry.BindCharacter(soloOwner, solo);
        check(DungeonPartyService.PostSettlementReturnsForOthers(registry, soloOwner, "settlement-party-return") == 0
                && !registry.TryTakeSettlementReturn(soloOwner, out _),
            "a solo instance never pulls anybody (single-player zero regression)");
    }

    /// <summary>
    /// 队员入场报文序列（设计稿 §3.2，硬顺序）：**名册 → NOTI 26（主机槽位）→ NOTI 11 → NOTI 28 → NOTI 29**；
    /// 没有名册时名册位跳过，其余顺序不变。`NOTI 26` 的载荷是**主机（队长）的槽位**（可以不是收件人的槽位）：
    /// 官服 `CParty::send_host_info` 反汇编实证 = 找到 `member[i] == party->host` 的槽位号 i 后 `put_byte(i)`
    /// 并 `send_to_party`（全队同一个值）；客户端把它当 0..7 槽位索引使用（DNF.exe 0x78D2B1 段）。
    /// **变异验证**：把 udpHost 的槽位改成 0、或把 dungeonInfo 放到 roster 之前，本用例变红。
    /// </summary>
    private static void CheckMemberEntryPacketOrder(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, difficulty: 0, dungeonOption: 0, out var transition),
            "the fixture run starts");
        var snapshot = instance.CaptureRoomSnapshot(instance.Run.RoomX, instance.Run.RoomY, transition, 7);
        var roster = GameProtocolEngine.CreatePartyInfoNotification(
            1000, nameSel: 1, maxMembers: 4, slotUids: [11, 22, 0xFFFF, 0xFFFF], leaderSlot: 0);
        var peerTable = GameProtocolEngine.CreateUdpPeerInfo([]);
        // 收件人是槽位 2 的队员，主机（队长）在槽位 0：两者必须不同，才能钉住"发的是主机槽位"。
        var packets = DungeonEntryPackets.PlanMemberEntry(instance, snapshot, roster, peerTable, hostSlot: 0);
        var ids = packets.Select(packet => (packet.Type, packet.ProtocolId)).ToArray();
        check(ids is
            [
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.PartyInfoNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.UdpHostNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.UdpPeerInfoNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.DungeonInfoNotification),
                (GameProtocolEngine.NotificationPacketType, GameProtocolEngine.StartMapNotification)
            ],
            "member entry sends roster, UDP host, peer table, DUNGEON_INFO and START_MAP in that order");
        check(packets[1].Payload[0] == 0,
            "NOTI 26 carries the host (leader) party slot index, not the recipient's own slot");
        var startMap = packets[4];
        // START_MAP 布局：roomX(0) roomY(1) randomSeed(2..5) roomStateFlag(6) mapId(7..8) ...
        check(BinaryPrimitives.ReadUInt16LittleEndian(startMap.Payload.AsSpan(7, 2)) == snapshot.MapId,
            "START_MAP carries the instance room map id");
        var withoutRoster = DungeonEntryPackets.PlanMemberEntry(instance, snapshot, null, peerTable, 0);
        check(withoutRoster.Count == 4 && withoutRoster[0].ProtocolId == GameProtocolEngine.UdpHostNotification,
            "a missing roster is skipped without disturbing the rest of the order");
    }

    /// <summary>
    /// 全员加载门槛（官服 `CParty::finish_loading` + `check_allmember_loading` 的语义）：
    /// 成员入队/入场默认"未加载"；每人上报 CMD 40 置位；**只有最后一位补齐时才报告"全员已加载"**；
    /// 换房（`ResetMemberLoadingForRoomChange`）清零，要求全员重新上报；成员离开后分母变小、重新评估。
    /// </summary>
    private static void CheckAllMemberLoadingBarrier(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        instance.AddMember(fixture.Member(memberA, slot: 0, "队长"));
        instance.AddMember(fixture.Member(memberB, slot: 1, "队员"));
        check(instance.MemberCount == 2, "the barrier fixture has two members");

        check(!instance.AllMembersLoaded,
            "a freshly entered member counts as not loaded (no NOTI 30 until they report CMD 40)");
        check(!instance.TryMarkMemberLoaded(memberA),
            "the first member reporting CMD 40 does not complete the barrier");
        check(!instance.AllMembersLoaded && !instance.TryMarkMemberLoaded(memberA),
            "the same member reporting twice still does not complete the barrier while the other is unloaded");
        check(instance.TryMarkMemberLoaded(memberB),
            "the last member completing the barrier is reported");
        // 真机 2026-09-30 08:55:30：客户端重载地图（"区域条目后重载房间"补包路径）会**再报一次 CMD 40**，
        // 这时必须继续放行 NOTI 30，否则两端一起黑屏。
        check(instance.TryMarkMemberLoaded(memberA),
            "a repeated CMD 40 after everyone is loaded still releases the party (reload case)");

        instance.ResetMemberLoadingForRoomChange();
        check(!instance.AllMembersLoaded,
            "a room change clears every member's loaded flag (official resetFinishLoadingVar)");
        check(!instance.TryMarkMemberLoaded(memberA),
            "after a room change the remaining member still has to report CMD 40 again");

        check(instance.RemoveMember(memberB), "the fixture drops the second member");
        check(instance.AllMembersLoaded,
            "after a member leaves, the remaining (already loaded) members count as all-loaded");
    }

    /// <summary>
    /// 单人零回归的结构性断言（设计稿 §4/§6 第 8 项）：单人进场绑定的是"以自己角色 id 为键"的实例、
    /// 实例里只有自己一个成员、`IsParty == false`（因此 `CMD 48` 走"立即换房间"）。
    /// **变异验证**：把 `GetOrCreateForSolo` 换成 `GetOrCreateForParty`、或让 AddMember 加两个人，本用例变红。
    /// （原先还有一条 `!instance.Doors.IsWaiting` 的断言：**S4 把门口集结机制整体删除后该断言不再有意义**
    /// —— `DungeonInstance` 里已经没有 `Doors`，结构上不可能进入集结状态。）
    /// </summary>
    private static void CheckSoloRunBindsByCharacterId(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        var instance = registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId));
        registry.BindCharacter(characterId, instance);
        instance.AddMember(fixture.Member(characterId, slot: 0, "独行"));
        check(!instance.IsParty && instance.MemberCount == 1
                && ReferenceEquals(instance, registry.TryGetForCharacter(characterId)),
            "a solo dungeon runs in a single-member instance keyed by the character id");
    }

    /// <summary>单人换房间：`TryMoveRoom` 立即推进（没有"等其他人"这一步），掉落在实例里跟着走。</summary>
    private static void CheckSoloRoomChangeKeepsImmediateBehaviour(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), slot: 0, "独行"));
        var started = instance.TryStartRun(fixture.Layout, 0, 0, out _);
        // `out` 一定要单独一句话（放在 && 里编译器会报"未赋值"）：先无条件调一次再断言结论。
        var moved = instance.TryMoveRoom(1, 0, out var transition);
        check(started && moved && instance.Run.RoomX == 1,
            "a solo room change applies immediately (no waiting state exists any more)");
        check(instance.CaptureRoomSnapshot(1, 0, transition, ownerUserId: 1).RoomX == 1,
            "the room snapshot follows the solo move");
    }

    /// <summary>单人实例：键 = 角色 id、单成员、`IsParty == false`（设计稿 §3.1/§7 决策 1）。</summary>
    private static void CheckSoloInstanceKey(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        var instance = registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId));
        check(!instance.IsParty && instance.SoloCharacterId == characterId && instance.PartyId == 0,
            "a solo dungeon instance is keyed by the character id");
        check(ReferenceEquals(
                instance,
                registry.GetOrCreateForSolo(characterId, () => fixture.CreateSoloInstance(characterId))),
            "asking for the same solo character twice returns the same instance");
        // 单人的键就是角色 id：`GetOrCreateForSolo` 用**单人专属**的键表（`_soloByCharacter`），
        // 与 <c>BindCharacter</c> 的会话绑定表（`_byCharacter`）分开——后者也会指向队伍实例。
        // 未登记过的角色才查不到。
        check(registry.TryGetForCharacter(Guid.NewGuid()) is null,
            "a character that never bound itself has no dungeon instance");
        registry.BindCharacter(characterId, instance);
        check(ReferenceEquals(instance, registry.TryGetForCharacter(characterId)),
            "binding a character maps it to its instance");
    }

    /// <summary>成员增删幂等 + 最后一个成员离开后实例销毁（设计稿 §3.1）。</summary>
    private static void CheckInstanceMembershipLifecycle(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var instance = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        check(instance.AddMember(fixture.Member(alpha, slot: 0, "阿尔法")),
            "a party instance accepts its first member");
        check(!instance.AddMember(fixture.Member(alpha, slot: 0, "阿尔法")),
            "adding the same character twice is idempotent");
        check(instance.AddMember(fixture.Member(bravo, slot: 2, "布拉沃")) && instance.MemberCount == 2,
            "a second member joins the same instance");
        check(instance.RemoveMember(bravo) && instance.MemberCount == 1 && !instance.ContainsMember(bravo),
            "leaving removes exactly that member");
        check(!instance.RemoveMember(bravo), "removing an absent member is a no-op");

        // 先起一局：解绑时的 StopRun 必须真的被断言到（不起 run 的话 !Run.IsActive 恒真）。
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _, random: fixture.Random),
            "the party instance starts a run before its last member leaves");

        // 设计稿 §3.4：最后一个成员离开 → 实例就地销毁（注册表两张表都要摘掉，运行态清空）。
        registry.BindCharacter(alpha, instance);
        check(ReferenceEquals(instance, registry.UnbindCharacter(alpha)) && !instance.ContainsMember(alpha),
            "unbinding the last member removes it from the instance");
        check(registry.TryGetForParty(1000) is null && !instance.Run.IsActive,
            "the last member leaving destroys the party instance and discards its run");
    }

    /// <summary>
    /// 收尾（设计稿 §3.4）：任一成员离开 → 只解绑自己，实例仍活（房间/掉落/run 留给其余成员）；
    /// 最后一个成员离开 → 实例销毁（房间/掉落全部丢弃）；队长离开不特殊——实例留给其余成员。
    /// 先起 run 再解绑：不起 run 的话 `Run.IsActive` 的两条断言恒真（Task 1 评审的"断言重言"教训）。
    /// **变异验证**（已实测变红后回滚）：把 `UnbindCharacter` 的"空了才 Remove"改成"每次解绑都
    /// Remove" → 上面三条（run 还活着 / 按队伍仍查得到 / 最后一人销毁并丢 run）变红；第一条
    /// （解绑返回值 MemberCount==1）**不会**变红——返回值来自实例本身，与注册表是否销毁它无关，
    /// 这正是必须补后两条断言的原因。
    /// </summary>
    private static void CheckLastMemberDestroysInstance(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var instance = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        instance.AddMember(fixture.Member(alpha, 0, "阿尔法"));
        instance.AddMember(fixture.Member(bravo, 2, "布拉沃"));
        registry.BindCharacter(alpha, instance);
        registry.BindCharacter(bravo, instance);
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _, random: fixture.Random),
            "the party instance runs before anyone leaves it");
        check(registry.UnbindCharacter(alpha) is { MemberCount: 1 },
            "leaving the party instance keeps it alive for the remaining member");
        // 关键：离开的那个人没有把"别人的一局"带走——其余成员还要接着打（真机第 6 项）。
        check(instance.Run.IsActive,
            "one member leaving does not stop the shared run (the survivors keep their rooms and drops)");
        check(ReferenceEquals(instance, registry.TryGetForParty(1000)),
            "the surviving member can still find the instance by party id");
        check(registry.UnbindCharacter(bravo) is { MemberCount: 0 }
                && registry.TryGetForParty(1000) is null
                && registry.TryGetForCharacter(bravo) is null
                && !instance.Run.IsActive,
            "the last member leaving destroys the instance and discards its run");
    }

    /// <summary>
    /// 离开提示（设计稿 §3.4）：离开者自己收不到（他已经在回城/断线路径上，客户端侧不留 actor），
    /// 其余成员各收一份既有的"角色离场"信号——USER_AREA 带 0xFF/0xFF 哨兵 + 离开者的 uid
    /// （客户端按区域不匹配销毁该 actor，见 TownSyncSmokeTests 的离场断言）。
    /// **变异验证**：把 `BroadcastToMembersAsync` 的 omit 去掉 → 第一条变红；
    /// 把 `CreateUserAreaDeparture` 换成同区域 `CreateUserArea(active: false)` → 第二条变红。
    /// 会话接线（三条离开路径何时调用它）不可单测：真机检查点是 Task 7 第 6 项。
    /// </summary>
    private static async Task CheckMemberDeparturePacketAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            new DungeonInstanceRegistry(),
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);
        var instance = fixture.CreatePartyInstance(1000);
        var leaver = Guid.NewGuid();
        var survivor = Guid.NewGuid();
        var leaverInbox = new List<GameServerPacket>();
        var survivorInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            leaver, 7, 0, "离开者", packet => { leaverInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            survivor, 8, 1, "留守者", packet => { survivorInbox.Add(packet); return ValueTask.CompletedTask; }));

        await service.NotifyMemberLeftAsync(
            instance, leaver, characterName: "离开者", areaUserId: 7, x: 120, y: 340, direction: 3);

        check(leaverInbox.Count == 0,
            "the member who left receives no departure notice of their own");
        check(survivorInbox.Count == 1
                && survivorInbox[0].ProtocolId == GameProtocolEngine.UserAreaNotification
                && survivorInbox[0].Payload.Length == 10
                && survivorInbox[0].Payload[0] == 7
                && survivorInbox[0].Payload[2] == 0xFF
                && survivorInbox[0].Payload[3] == 0xFF,
            "the remaining member receives the USER_AREA departure signal with the leaver's uid (0xFF/0xFF sentinel)");
    }

    /// <summary>
    /// 第 13 条（官服语义：**副本内擅自退出 = 退队**）。官服 `df_game_r` 三处实证：
    /// ① 客户端上行 **CMD 45 GIVEUP_GAME**（副本内"返回城镇"按钮，真机日志 `protocol=45`；`opcode_cmd.txt`
    ///    第 45 项）→ `CParty::giveup_game` @0x085b2baa，其中两处 `CParty::leave_user(party, user, 2)`
    ///    （0x085b2f2c / 0x085b33c1）；
    /// ② **CMD 13 LEAVE_PARTY** → `DisPatcher_LeaveParty::process` @0x081c4256 也是
    ///    `leave_user(party, user, 2)`（0x081c42f3）——**同一个函数、同一个实参**，所以"擅自退出"的
    ///    退队形态就是既有 CMD 13 的形态；
    /// ③ `CParty::leave_user` @0x0859c114（1822B）只 `CParty::change_manager` + `GameWorld::send_party_info_to_all`
    ///    （NOTI 9）与 `send_party_realtime_info`，**不含 NOTI 10**——NOTI 10 在另一个函数
    ///    `CParty::walkout_member` @0x0859ca4a → `CParty::SendWalkout` @0x0859cb00 里
    ///    ⇒ 对应 <see cref="PartyLeaveNotice.Silent"/>（既有 CMD 13 路径）。
    /// 反过来**不算**擅自退出的两条（同一份实证）：结算回城 `CParty::ReturnToVillage` @0x085aca60
    /// 的 58 个内部调用里**没有** `leave_user`；副本内死亡 `DisPatcher_DieCharacInDungeon::process`
    /// @0x081c2a12 调的是 `CParty::die_user` 而不是 `leave_user`。所以本用例也钉住"不在**队伍**实例里
    /// 一律一个包都不发"（单人零回归 + 选图界面/城镇里收到 CMD 45 都不该退队）。
    /// **"擅自退出"只在队里还有别人时才真的把他移出队伍**（用户 2026-09-30 真机更正）：一个人退队后名册为空
    /// **不解散**——他仍是这支 1 人队的队长（官服 `CParty::leave_user` 只有 `IsSinglePlay` 那支才 `CParty::destroy`）。
    /// **变异验证**：把 `AbandonPartyForDungeonGiveUpAsync` 的 `IsParty` 前置判断去掉 → 单人/未绑定那两条变红；
    /// 把通知换成 `PartyLeaveNotice.Kicked`/`Disconnected` → "没有人收到 NOTI 10" 变红；
    /// 不调 `TryLeavePartySilentlyAsync` → 前三条变红；
    /// 把"只有别人时才算退队"改成无条件退队 → C 组（队伍保留）变红。
    /// 会话接线（哪条回城路径调它）不可单测：真机检查点见 STATUS.md 第 13 条。
    /// </summary>
    private static async Task CheckGiveUpLeavesPartyAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();

        // ---- A) 队员擅自退出：自己退队，队长留下（队伍只少他一个） ----
        var memberInstance = fixture.CreatePartyInstance(1500);
        var memberRegistry = new DungeonInstanceRegistry();
        using (var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            memberRegistry,
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance))
        {
            await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
            var leader = harness.IdOf("阿尔法");
            var leaver = harness.IdOf("布拉沃");
            var leaderUid = harness.UidOf("阿尔法");
            memberInstance.AddMember(new DungeonInstanceMember(
                leader, leaderUid, 0, "阿尔法", _ => ValueTask.CompletedTask));
            memberInstance.AddMember(new DungeonInstanceMember(
                leaver, harness.UidOf("布拉沃"), 1, "布拉沃", _ => ValueTask.CompletedTask));
            memberRegistry.BindCharacter(leader, memberInstance);
            memberRegistry.BindCharacter(leaver, memberInstance);
            harness.ClearAll();

            var removed = await service.AbandonPartyForDungeonGiveUpAsync(leaver);

            check(removed && harness.Service.GetPartySlots(leaver) is null,
                "giving up inside a party dungeon takes the member out of the party");
            var leaderSlots = harness.Service.GetPartySlots(leader);
            check(leaderSlots is { Length: 1 } && leaderSlots[0].CharacterId == leader,
                "the leader keeps the party with exactly one member left");
            var leaderRoster = harness.FirstOf("阿尔法", GameProtocolEngine.PartyInfoNotification);
            check(leaderRoster is { Payload.Length: 20 }
                    && BinaryPrimitives.ReadUInt16LittleEndian(leaderRoster.Payload.AsSpan(7, 2)) == leaderUid
                    && leaderRoster.Payload[10] == 0xFF
                    && leaderRoster.Payload[11] == 0xFF
                    && leaderRoster.Payload[19] == 0,
                "the remaining member receives the roster with the leaver's slot cleared (leader unchanged)");
            var leaverRoster = harness.FirstOf("布拉沃", GameProtocolEngine.PartyInfoNotification);
            check(leaverRoster is { Payload.Length: 20 }
                    && leaverRoster.Payload[10] == 0xFF
                    && leaverRoster.Payload[11] == 0xFF,
                "the member who gave up receives a roster without themselves (client self-cleans, CMD 13 shape)");
            check(harness.Of("阿尔法").All(packet => packet.ProtocolId != GameProtocolEngine.WalkoutNotification)
                    && harness.Of("布拉沃").All(packet => packet.ProtocolId != GameProtocolEngine.WalkoutNotification),
                "nobody receives NOTI 10: the official give-up path is the silent leave (leave_user, not walkout_member)");
            check(memberInstance.MemberCount == 2,
                "the party leave does not touch the dungeon instance: unbinding stays with the session's return-to-town path");

            // ---- B) 队长的 CMD 45：队长也退队，队长身份**就地**继任（槽位不搬动） ----
            var leaderInstance = fixture.CreatePartyInstance(1501);
            var leaderRegistry = new DungeonInstanceRegistry();
            using var leaderService = new DungeonPartyService(
                harness.Service,
                harness.Directory,
                leaderRegistry,
                TimeProvider.System,
                NullLogger<DungeonPartyService>.Instance);
            await harness.InviteAndAcceptAsync("查理", "德尔塔");
            var goingLeader = harness.IdOf("查理");
            var successor = harness.IdOf("德尔塔");
            var successorUid = harness.UidOf("德尔塔");
            leaderInstance.AddMember(new DungeonInstanceMember(
                goingLeader, harness.UidOf("查理"), 0, "查理", _ => ValueTask.CompletedTask));
            leaderInstance.AddMember(new DungeonInstanceMember(
                successor, successorUid, 1, "德尔塔", _ => ValueTask.CompletedTask));
            leaderRegistry.BindCharacter(goingLeader, leaderInstance);
            leaderRegistry.BindCharacter(successor, leaderInstance);
            harness.ClearAll();

            check(await leaderService.AbandonPartyForDungeonGiveUpAsync(goingLeader)
                    && harness.Service.GetPartySlots(goingLeader) is null,
                "a leader giving up leaves the party as well");
            check(harness.Service.TryGetLeaderSlot(successor) == 1,
                "the remaining member takes over as leader in place (slot 1, never moved to slot 0)");
            var successorRoster = harness.FirstOf("德尔塔", GameProtocolEngine.PartyInfoNotification);
            check(successorRoster is { Payload.Length: 20 }
                    && BinaryPrimitives.ReadUInt16LittleEndian(successorRoster.Payload.AsSpan(10, 2)) == successorUid
                    && successorRoster.Payload[19] == 1,
                "the successor's roster keeps him in his own slot and marks him leader");
            check(harness.Of("查理").All(packet => packet.ProtocolId != GameProtocolEngine.WalkoutNotification),
                "the departing leader is not announced with NOTI 10 either");

            // ---- C) 队里只剩自己时擅自退出：**队伍保留**，他仍是这支 1 人队的队长（用户 2026-09-30 真机更正） ----
            // 官服语义：退队后名册为空**不**解散（`CParty::leave_user` 只有 `IsSinglePlay` 那支才 `CParty::destroy`，
            // 玩家自建的 1 人队不走那条）——所以"擅自退出"只在**还有别人**时才真把他移出队伍。
            leaderRegistry.UnbindCharacter(goingLeader);
            harness.ClearAll();
            check(!await leaderService.AbandonPartyForDungeonGiveUpAsync(successor),
                "the last member giving up does not remove him from his own party");
            var keptSlots = harness.Service.GetPartySlots(successor);
            check(keptSlots is { Length: 1 } && keptSlots[0].CharacterId == successor
                    && harness.Service.TryGetLeaderSlot(successor) == 1,
                "he stays in the party as its leader (single-member party, own slot, no slot move)");
            check(harness.Of("德尔塔").Count == 0,
                "nothing is sent when the party is kept (no disband record, no roster churn)");
        }

        // ---- D) 单人副本：一个包都不发（单人零回归）；E) 不在任何实例（选图/城镇里收到 CMD 45）同样不发 ----
        var soloRegistry = new DungeonInstanceRegistry();
        using (var soloService = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            soloRegistry,
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance))
        {
            var alpha = harness.IdOf("阿尔法");
            soloRegistry.BindCharacter(alpha, fixture.CreateSoloInstance(alpha));
            harness.ClearAll();
            check(!await soloService.AbandonPartyForDungeonGiveUpAsync(alpha)
                    && harness.Of("阿尔法").Count == 0,
                "a solo dungeon instance never touches the party (single-player zero regression)");
            soloRegistry.UnbindCharacter(alpha);
            harness.ClearAll();
            check(!await soloService.AbandonPartyForDungeonGiveUpAsync(alpha)
                    && harness.Of("阿尔法").Count == 0,
                "a member who is not in any dungeon instance is left alone (selection screen / town)");
        }
    }

    /// <summary>
    /// 并发语义：同一只怪被两个会话同时上报只结算一次（`AliveMonsters.Remove` 的返回值就是唯一判定，
    /// 与改造前 `aliveDungeonMonsters.Remove` 同口径）；房间开放（`TryEnableClear`）同样只翻一次。
    /// **变异验证**：让 `TryTakeMonsterDeath` 无视 `AliveMonsters.Remove` 的返回值（每条上报都返回 true），
    /// 本用例的两条死亡断言变红（已实测）。注意：把 `MarkMonsterDefeated` 提到判断之前**不会**变红——
    /// 结算的唯一判定就是锁内的那一次 `Remove`，`MarkMonsterDefeated` 自身幂等。
    /// </summary>
    private static void CheckConcurrentKillSettlesOnce(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, difficulty: 0, dungeonOption: 0, out _),
            "the fixture layout starts a run");
        var victim = instance.AliveMonsters.First();
        var wins = 0;
        Parallel.For(0, 8, iteration =>
        {
            _ = iteration;
            if (instance.TryTakeMonsterDeath(victim, out _))
            {
                Interlocked.Increment(ref wins);
            }
        });
        check(wins == 1, "eight concurrent death reports settle exactly one kill");
        check(!instance.TryTakeMonsterDeath(victim, out _), "a second report is stale");

        var clearWins = 0;
        Parallel.For(0, 8, iteration =>
        {
            _ = iteration;
            if (instance.TryEnableClear())
            {
                Interlocked.Increment(ref clearWins);
            }
        });
        check(clearWins == 1, "enabling room settlement flips exactly once");
    }

    /// <summary>
    /// 客户端要求"全场小怪一起死"（ALLDIE_MONSTER / BOSS_DIE_CHECK 的 native-all-die）时，
    /// 服务端必须**真的**把剩下的怪摘掉——否则 boss 死后小怪永远站着
    /// （真机 2026-10-01 15:47：日志只写 `waiting for normal monster-death confirmations`，
    /// 6 只小怪不自动死，13 秒后玩家手动打完才 `AllMonstersDefeated`）。
    /// **变异验证**：把 <see cref="DungeonInstance.ForceAllMonstersDead"/> 的摘除改成空实现 → 本用例变红。
    /// </summary>
    private static void CheckForceAllMonstersDead(Action<bool, string> check, DungeonFixture fixture)
    {
        // ① 带名单（调用方先取 SnapshotAliveMonsters）：名单里的全部摘掉，缺席的（= 客户端本地已清）
        //    不能把活怪留下，也不能把已经不在集合里的 id 当成有效死亡。
        var instance = fixture.CreateInstance();
        instance.EnterDetachedRoom(DungeonFixture.Room(
            [
                DungeonFixture.Monster(uniqueId: 0, type: GameDungeonMonsterTypes.Boss),
                DungeonFixture.Monster(uniqueId: 1),
                DungeonFixture.Monster(uniqueId: 2)
            ]));
        var reportedAlive = instance.SnapshotAliveMonsters();
        reportedAlive.Add(99); // 客户端本地已经清掉的 id：摘不动，但要允许被"通知死亡"
        check(instance.TryTakeMonsterDeath(0, out _), "the boss dies normally before the all-die request");
        instance.ForceAllMonstersDead(reportedAlive);
        check(instance.AliveMonsterCount == 0 && instance.SnapshotAliveMonsters().Count == 0,
            "an all-die request removes every monster still alive in the room");
        check(!instance.TryTakeMonsterDeath(1, out _),
            "a monster forced dead by the all-die request can no longer be reported as a fresh kill");

        // ② 没有名单时退回"全清"分支（调用方不持有快照的路径）。
        var bulk = fixture.CreateInstance();
        bulk.EnterDetachedRoom(DungeonFixture.Room(
            [
                DungeonFixture.Monster(uniqueId: 0),
                DungeonFixture.Monster(uniqueId: 1),
                DungeonFixture.Monster(uniqueId: 2),
                DungeonFixture.Monster(uniqueId: 3)
            ]));
        check(bulk.AliveMonsterCount == 4, "the bulk all-die case starts with four monsters");
        bulk.ForceAllMonstersDead();
        check(bulk.AliveMonsterCount == 0, "the no-list all-die path clears the whole alive set");

        // 连锁死亡包的参数必须合法：`championRewardOrdinal` 只接受哨兵值 -1（不写这一段）或 1..11。
        // 2026-10-01 16:29 真机：ALLDIE 分支写成 `CreateMonsterDie(id, [], 0)` ⇒ 抛
        // ArgumentOutOfRangeException('championRewardOrdinal') ⇒ 异常冒泡到会话 ⇒ 服务端主动断开
        // ⟹ 客户端弹"网络连接被中断"后卡死闪退、队长房间无法通关。下面两条把该 API 的契约钉死。
        check(GameProtocolEngine.CreateMonsterDie(7).ProtocolId == GameProtocolEngine.MonsterDieNotification
                && GameProtocolEngine.NoChampionRewardOrdinal < 0,
            "a cascade-death packet built with the no-champion-reward sentinel is a valid monster-death notification");
        var ordinalZeroRejected = false;
        try
        {
            _ = GameProtocolEngine.CreateMonsterDie(7, [], 0);
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName == "championRewardOrdinal")
        {
            ordinalZeroRejected = true;
        }

        check(ordinalZeroRejected,
            "ordinal 0 is rejected by the protocol engine — whoever builds a cascade-death packet must pass " +
            "the sentinel (ordinal 0 killed the member session live at 2026-10-01 16:29)");

        // 顺序契约（2026-10-01 17:18 真机事故）：处理 ALLDIE 必须**先广播死亡包、再清存活集合**。
        // 顺序反了的症状：小怪被"直接删掉"而不是正常死亡 → 客户端随后自己上报的死亡被当成陈旧
        // （`Ignored stale or unknown monster death`）→ 房间不结算 → 队长与队友都卡住。
        // 这里按生产顺序走一遍：快照 → 广播 → 客户端逐只上报（必须仍算有效击杀）→ 最后才清。
        var ordered = fixture.CreateInstance();
        ordered.EnterDetachedRoom(DungeonFixture.Room(
            [
                DungeonFixture.Monster(uniqueId: 1),
                DungeonFixture.Monster(uniqueId: 2),
                DungeonFixture.Monster(uniqueId: 3)
            ]));
        var orderedSnapshot = ordered.SnapshotAliveMonsters();
        foreach (var uniqueId in orderedSnapshot)
        {
            // 生产代码在这里广播 NOTI 38（CreateMonsterDie 的契约已由上面几条钉住）。
            check(GameProtocolEngine.CreateMonsterDie(uniqueId).ProtocolId
                    == GameProtocolEngine.MonsterDieNotification,
                "the all-die broadcast builds a valid death notification before the alive set is cleared");
        }

        var creditedKills = orderedSnapshot.Count(uniqueId => ordered.TryTakeMonsterDeath(uniqueId, out _));
        check(creditedKills == orderedSnapshot.Count,
            "a monster the all-die broadcast already announced is still credited when the client reports its death " +
            "(clearing the alive set first would make every one of these a stale report)");
        ordered.ForceAllMonstersDead(orderedSnapshot);
        check(ordered.AliveMonsterCount == 0, "the all-die sweep still clears the room after the broadcast");
    }

    /// <summary>房间快照：换房间刷新 `AliveMonsters`；被动掉落只在首次访问登记一次，重访只读已登记的。</summary>
    private static void CheckRoomSnapshotsAndPassiveDrops(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out var start),
            "the run starts in the fixture start room");
        var firstSnapshot = instance.CaptureRoomSnapshot(instance.Run.RoomX, instance.Run.RoomY, start, ownerUserId: 7);
        check(firstSnapshot.FirstVisit && !firstSnapshot.ReuseClientRoomState
                && firstSnapshot.Monsters.Length == start.Room.Monsters.Length,
            "the first visit snapshot carries the room monster table");
        check(instance.TryMoveRoom(1, 0, out var second),
            "moving to the neighbouring room succeeds");
        var secondSnapshot = instance.CaptureRoomSnapshot(1, 0, second, ownerUserId: 7);
        check(secondSnapshot.FirstVisit && instance.AliveMonsters.SetEquals(
                secondSnapshot.Monsters.Select(monster => monster.UniqueId)),
            "moving refreshes the alive monster set from the new room");
        check(!instance.TryMoveRoom(1, 0, out _), "moving into the current room is rejected");
        check(instance.TryMoveRoom(0, 0, out var revisit) && revisit.Revisit,
            "walking back marks the room as a revisit");
        var revisitSnapshot = instance.CaptureRoomSnapshot(0, 0, revisit, ownerUserId: 7);
        // 与 DungeonRunState 的既有契约一致：普通重访让客户端复用上次的 START_MAP 快照
        // （ReuseClientRoomState = true），只有"地狱房已清"的重访才必须重建为空地图。
        check(!revisitSnapshot.FirstVisit && revisitSnapshot.ReuseClientRoomState,
            "an ordinary revisit reuses the client's cached room state");
    }

    /// <summary>
    /// "加入一个已经在跑的实例"（`CaptureCurrentRoomSnapshot`）不许把被动掉落重新登记一遍：
    /// 判据必须是"这个房间登记过没有"，而不是"现在还剩几条掉落"——掉落被捡走（`Remove`）或 detach 后
    /// 后者会归零，而房间定义里的 `PassiveItemSpawns` 是静态的，重登 = 新 GroundId + 客户端幽灵物件 = 复制道具。
    /// 用 `EnterDetachedRoom`（教学副本路径）喂一个合成房间：不依赖 PVF 掉落脚本，跑的是同一条
    /// `CaptureCurrentRoomSnapshot → RegisterRoomPassiveDropsLocked` 代码。
    /// </summary>
    private static void CheckLiveJoinDoesNotReplenishPassiveDrops(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        var room = DungeonFixture.Room(
            [DungeonFixture.Monster(uniqueId: 0), DungeonFixture.Monster(uniqueId: 1)],
            passiveDropCount: 2);
        instance.EnterDetachedRoom(room);

        var firstJoin = instance.CaptureCurrentRoomSnapshot(ownerUserId: 7);
        check(firstJoin.PassiveObjectDrops.Length == 2
                && firstJoin.PassiveObjectDrops.All(drop => drop.OwnerUserId == 7)
                && instance.GetRoomGroundItems(0, 0).Length == 2,
            "a live join registers the room passive drops exactly once");
        var lootedGroundIds = instance.GetRoomGroundItems(0, 0)
            .Select(item => item.GroundId)
            .ToArray();

        // 队友进来之前这批掉落已经被捡光（= DungeonGroundItemState.Remove）。
        foreach (var groundId in lootedGroundIds)
        {
            instance.RemoveGroundItem(groundId);
        }

        var secondJoin = instance.CaptureCurrentRoomSnapshot(ownerUserId: 8);
        check(secondJoin.PassiveObjectDrops.Length == 0
                && instance.GetRoomGroundItems(0, 0).Length == 0,
            "a live join must not re-register passive drops that were already looted");

        // 换局/重开必须忘掉登记表：同一个实例再进这个房间时要重新登记（新 GroundId）。
        instance.EnterDetachedRoom(room);
        var reopened = instance.CaptureCurrentRoomSnapshot(ownerUserId: 9);
        check(reopened.PassiveObjectDrops.Length == 2
                && reopened.PassiveObjectDrops.All(drop => !lootedGroundIds.Contains(drop.GroundId)),
            "re-entering the room after a reset registers a fresh passive drop set");
    }

    /// <summary>
    /// 实例共享集合的锁内访问口（评审 Important 3）：裸集合一律 internal、只许在 `lock (Gate)` 内碰；
    /// `TryReportBossDeath` 复刻改造前 BOSS_DIE_CHECK 的"上报 + 全 boss 到齐后级联"三句
    /// （EntranceService.cs:12333-12341），并保持"同一 boss 重复上报幂等、不重复级联"。
    /// </summary>
    private static void CheckSharedCollectionAccessors(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.EnterDetachedRoom(DungeonFixture.Room(
            [
                DungeonFixture.Monster(uniqueId: 0, type: GameDungeonMonsterTypes.Boss),
                DungeonFixture.Monster(uniqueId: 1, type: GameDungeonMonsterTypes.Boss),
                DungeonFixture.Monster(uniqueId: 2)
            ]));

        check(instance.AliveMonsterCount == 3,
            "the alive monster count is read behind the instance gate");
        var aliveCopy = instance.SnapshotAliveMonsters();
        aliveCopy.Remove(0);
        check(instance.AliveMonsterCount == 3 && instance.SnapshotAliveMonsters().SetEquals([0, 1, 2]),
            "the alive monster snapshot is a copy that cannot corrupt the instance");

        ushort[] bossIds = [0, 1];
        check(instance.TryReportBossDeath(0, bossIds, out var allBossesReported)
                && !allBossesReported && instance.ReportedBossDeathCount == 1,
            "the first boss report is recorded without completing the boss list");
        check(!instance.TryReportBossDeath(0, bossIds, out _) && instance.ReportedBossDeathCount == 1,
            "a repeated boss report is idempotent");
        check(instance.TryReportBossDeath(1, bossIds, out allBossesReported) && allBossesReported
                && instance.ReportedBossDeathCount == 2
                && instance.BossCascadeMonsterDeaths.SetEquals([2]),
            "completing the boss list cascades the remaining non-boss monsters");

        var droppedItem = new CharacterItemRecord(Slot: ushort.MaxValue, ItemId: 3, CountOrValue: 5);
        var groundItem = instance.AddPlayerDroppedGroundItem(
            droppedItem, ownerUserId: 7, roomX: 0, roomY: 0, x: 12, y: 34);
        check(groundItem.ItemId == 3 && groundItem.CountOrValue == 5
                && instance.TryGetGroundItem(groundItem.GroundId, 0, 0, out var found)
                && ReferenceEquals(groundItem, found)
                && instance.GetRoomGroundItems(0, 0).Length == 1,
            "a player-dropped item is registered behind the instance gate");
        instance.ResetGroundItems();
        check(instance.GetRoomGroundItems(0, 0).Length == 0,
            "resetting the dungeon drops every ground item");
    }

    /// <summary>
    /// 换房间必须重置会话级「每房结算」标志（Task 5 评审 Important）：结果屏/卡片布局/选卡权/选卡截止/
    /// 结算奖励/两张已选卡要整组回到默认，否则 `ClearEnabled &amp;&amp; !ResultSent` 的守卫
    /// （`EntranceService` 的 `SET_PLAY_RESULT` 分支）会把第二间及以后的结算整个吞掉。
    /// 单人 <c>CMD 48</c> 一直这么做；门口集结制让房间也能被**别人**推进，所以这套状态搬进可测对象，
    /// 三条换房路径（单人立即推进 / 组队自己推进 / 被强制推进后的镜像追赶）共用
    /// `ResetRoomSettlementState()` → 这里的 `Reset()`。
    /// **变异验证**：删掉 `Reset()` 里任意一条赋值（实测删 `ResultSent = false;`）→ 第一条断言变红。
    /// </summary>
    private static void CheckRoomSettlementReset(Action<bool, string> check)
    {
        check(new DungeonRoomSettlementState() is
                {
                    ResultSent: false,
                    CardLayoutSent: false,
                    CardRightsSent: false,
                    CardSelectionDeadline: null,
                    ClearReward: null,
                    FreeCardIndex: null,
                    GoldCardIndex: null
                },
            "a fresh room starts with no settlement state (no result screen, cards or reward)");

        var state = new DungeonRoomSettlementState
        {
            ResultSent = true,
            CardLayoutSent = true,
            CardRightsSent = true,
            CardSelectionDeadline = DateTimeOffset.UtcNow.AddSeconds(30),
            ClearReward = new DungeonClearRewardRoll(
                GoldCardCost: 1000,
                GoldCardEnabled: true,
                FreeGoldAmount: 100,
                FreeItemId: 1,
                PremiumItemId: 2,
                GoldItemId: 3),
            FreeCardIndex = 1,
            GoldCardIndex = 2
        };
        state.Reset();
        check(state is
                {
                    ResultSent: false,
                    CardLayoutSent: false,
                    CardRightsSent: false,
                    CardSelectionDeadline: null,
                    ClearReward: null,
                    FreeCardIndex: null,
                    GoldCardIndex: null
                },
            "a room change resets every per-room settlement flag (the next room settles again)");
    }

    /// <summary>
    /// 追赶式房间疲劳的"首访才扣"门控（Task 5 评审 Minor 2）：`DungeonInstance.LastRoomEntryWasFirstVisit`
    /// 跟着**最近一次进房**走——开局与新房间是首访（true），重访是 false（重访让客户端复用缓存快照，
    /// 单人不扣、组队的镜像追赶也不能扣），实例收尾后没有当前房间 → 回到 false。
    /// **变异验证**：把 `TryMoveRoom` 里那行 `LastRoomEntryWasFirstVisit = transition.FirstVisit;`
    /// 改成恒 `true` → 第三条断言变红（实测）。
    /// </summary>
    private static void CheckRoomFirstVisitFlag(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _) && instance.LastRoomEntryWasFirstVisit,
            "starting the run counts as a first visit (the catch-up path charges the start room)");
        var moved = instance.TryMoveRoom(1, 0, out var firstMove);
        check(moved && firstMove.FirstVisit && instance.LastRoomEntryWasFirstVisit,
            "moving into a new room counts as a first visit");
        var revisited = instance.TryMoveRoom(0, 0, out var revisit);
        check(revisited && !revisit.FirstVisit && !instance.LastRoomEntryWasFirstVisit,
            "walking back into a visited room clears the first-visit flag (a revisit charges no fatigue)");
        instance.StopRun();
        check(!instance.LastRoomEntryWasFirstVisit,
            "a stopped instance has no current room and no first-visit flag");
    }
    /// <summary>
    /// 门口集结里"到过门口的人"各自的那份 `CMD 48` 成功回包（生产代码在 `Register` 时用请求帧就地生成）。
    /// 用例传的是**逐人独立**的实例：推进入口要把它原样投给那个成员的收件箱（不是别的人那份）。
    /// </summary>
    private static GameServerPacket Reply(Guid _) =>
        GameProtocolEngine.CreateCommandReply(
            new PacketFrame(
                GameProtocolEngine.CommandPacketType,
                GameProtocolEngine.MoveMapCommand,
                DeclaredCrc32: 0,
                Body: [0, 0, 1, 0]),
            success: true);

    /// <summary>
    /// 换房间报文的共享规划器（Task 5 Step 1：`PlanStartMap` 从 `PlanMemberEntry` 抽出来复用）：
    /// 首次进房 → 完整 `START_MAP`（房间/地图 id/怪物表都在载荷里）；重访（客户端复用上次快照）
    /// → 四字节的 cached `START_MAP`（第四字节 = 0，客户端据此跳过地图/怪物/掉落段）。
    /// **变异验证**：把 `PlanStartMap` 的 `ReuseClientRoomState` 分支删掉（恒发完整 START_MAP）→ 第二条变红。
    /// </summary>
    private static void CheckPlanStartMapSharedPlanner(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out var start),
            "the fixture run starts for the shared START_MAP planner case");
        var fresh = instance.CaptureRoomSnapshot(instance.Run.RoomX, instance.Run.RoomY, start, ownerUserId: 7);
        var startMap = DungeonEntryPackets.PlanStartMap(fresh);
        // START_MAP 布局：roomX(0) roomY(1) randomSeed(2..5) roomStateFlag(6) mapId(7..8) ...
        check(startMap.Type == GameProtocolEngine.NotificationPacketType
                && startMap.ProtocolId == GameProtocolEngine.StartMapNotification
                && startMap.Payload.Length > 9
                && startMap.Payload[0] == fresh.RoomX
                && startMap.Payload[1] == fresh.RoomY
                && BinaryPrimitives.ReadUInt16LittleEndian(startMap.Payload.AsSpan(7, 2)) == fresh.MapId,
            "a first visit plans a full START_MAP carrying the room, the map id and the monster table");

        // `out` 单独一句话（放在 && 里编译器会报"未赋值"）。
        check(instance.TryMoveRoom(1, 0, out _), "the planner case moves away from the start room");
        var revisited = instance.TryMoveRoom(0, 0, out var revisit);
        check(revisited, "walking back to the start room is a revisit");
        var revisitSnapshot = instance.CaptureRoomSnapshot(0, 0, revisit, ownerUserId: 7);
        var cached = DungeonEntryPackets.PlanStartMap(revisitSnapshot);
        // cached START_MAP 布局：roomX(0) roomY(1) randomSeed(2..5) state(6)=0，没有地图/怪物/掉落段。
        check(revisitSnapshot.ReuseClientRoomState
                && cached.ProtocolId == GameProtocolEngine.StartMapNotification
                && cached.Payload.Length == 7
                && cached.Payload[6] == 0,
            "a revisit plans the cached START_MAP without the map/monster sections");
    }    /// <summary>    /// <summary>
    /// 收件箱取第 index 份包（越界返回 null）：变异让某个成员少收/收错包时，
    /// 断言给的是一条可读的失败，而不是 <see cref="IndexOutOfRangeException"/> 把整个测试进程打断。
    /// </summary>
    private static GameServerPacket? PacketAt(IReadOnlyList<GameServerPacket> packets, int index) =>
        index < packets.Count ? packets[index] : null;
    /// <summary>待入队信箱：同一角色只留最后一条（重复投递幂等）、取走即清、未投递时为 false。</summary>
    private static void CheckRegistryEntryRequestMailbox(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        check(!registry.TryTakeEntryRequest(characterId, out _),
            "an untouched character has no pending dungeon entry");
        var request = new DungeonEntryRequest(
            PartyId: 1000, DungeonId: 14, Difficulty: 0, DungeonOption: 0,
            LeaderTownId: 1, LeaderAreaId: 0, LeaderCharacterId: Guid.NewGuid());
        registry.PostEntryRequest(characterId, request);
        registry.PostEntryRequest(characterId, request with { DungeonId = 15 });
        check(registry.TryTakeEntryRequest(characterId, out var taken) && taken.DungeonId == 15,
            "re-posting overwrites the pending request (last one wins)");
        check(!registry.TryTakeEntryRequest(characterId, out _),
            "taking the request clears it so the entry runs once");
    }

    /// <summary>
    /// 待处理组队邮箱的"有没有货"查询（终审 P2）：会话主循环的读等待只有在这个查询为真时才
    /// 缩短到短 tick，否则完全按原样等待（单人零回归）。两个信箱**任一**有货即为 true；
    /// 取走/解绑后必须回到 false——否则主循环会永远 250ms 空转。
    /// **变异验证**：把 `HasPendingMail` 改成恒 false（或只看其中一个信箱）→ 第三/第四/第五
    /// 条断言之一变红。会话里"用它缩短等待 + 短 tick 到点取件"的接线不可单测（无会话接缝），
    /// 真机检查点是 Task 7 的第 2 项。
    /// </summary>
    private static void CheckPendingPartyMailLookup(Action<bool, string> check, DungeonFixture fixture)
    {
        var registry = new DungeonInstanceRegistry();
        var characterId = Guid.NewGuid();
        check(!registry.HasPendingMail(characterId),
            "an untouched character has no pending party mail (the read wait stays untouched)");
        registry.PostSelectionRequest(characterId, Guid.NewGuid());
        check(registry.HasPendingMail(characterId),
            "a pending selection request counts as pending mail");
        registry.PostEntryRequest(
            characterId,
            new DungeonEntryRequest(
                PartyId: 1000, DungeonId: 14, Difficulty: 0, DungeonOption: 0,
                LeaderTownId: 1, LeaderAreaId: 0, LeaderCharacterId: Guid.NewGuid()));
        check(registry.TryTakeSelectionRequest(characterId, out _) && registry.HasPendingMail(characterId),
            "taking the selection request leaves the entry request pending");
        check(registry.TryTakeEntryRequest(characterId, out _) && !registry.HasPendingMail(characterId),
            "taking the last request clears the pending-mail signal");
        registry.PostEntryRequest(
            characterId,
            new DungeonEntryRequest(
                PartyId: 1000, DungeonId: 14, Difficulty: 0, DungeonOption: 0,
                LeaderTownId: 1, LeaderAreaId: 0, LeaderCharacterId: Guid.NewGuid()));
        registry.UnbindCharacter(characterId);
        check(!registry.HasPendingMail(characterId),
            "unbinding the character clears both mailboxes");

        // 真机 2026-09-29 新增的两类邮件：队长退选图的取消通知、队友击杀的经验信用。
        registry.PostSelectionCancel(characterId);
        check(registry.HasPendingMail(characterId) && registry.TryTakeSelectionCancel(characterId),
            "a selection cancel counts as pending mail and is taken exactly once");
        check(!registry.TryTakeSelectionCancel(characterId),
            "a taken selection cancel is gone");
        var credit = new DungeonMonsterKillCredit(
            MonsterLevel: 12, MonsterType: 0, MonsterIndex: 3, IsBossCascadeDeath: false);
        registry.PostKillCredit(characterId, credit);
        registry.PostKillCredit(characterId, credit with { MonsterIndex = 4 });
        check(registry.HasPendingMail(characterId),
            "kill credits count as pending mail (the member session must wake up to settle them)");
        check(registry.TryTakeKillCredit(characterId, out var first)
                && first.MonsterIndex == 3
                && registry.TryTakeKillCredit(characterId, out var second)
                && second.MonsterIndex == 4
                && !registry.TryTakeKillCredit(characterId, out _),
            "kill credits are queued FIFO (one drain per kill, not last-write-wins)");
        registry.UnbindCharacter(characterId);
        check(!registry.HasPendingMail(characterId),
            "unbinding also clears the cancel/kill-credit mailboxes");
    }

    /// <summary>
    /// 击杀计数与奖励动画序号在**实例**上共享（真机 2026-09-29：计数留在会话里时，
    /// 同队两人各按自己杀的那几只算 → 两个客户端的通关评分/奖励完全不同）。
    /// **变异验证**：把 `RegisterMonsterKill` 的计数改成只返回不累加，下面的共享断言变红。
    /// </summary>
    private static void CheckKillCountersAreShared(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, difficulty: 0, dungeonOption: 0, out _),
            "the fixture run starts for the shared kill counters");
        check(instance.NormalMonsterKills == 0
                && instance.ChampionMonsterKills == 0
                && instance.BossMonsterKills == 0
                && instance.ChampionRewardOrdinal == 0,
            "a fresh run starts with zeroed kill counters");
        check(instance.RegisterMonsterKill(GameDungeonMonsterTypes.Normal)
                == GameProtocolEngine.NoChampionRewardOrdinal
                && instance.NormalMonsterKills == 1,
            "a normal kill counts once and carries no champion reward ordinal");
        check(instance.RegisterMonsterKill(GameDungeonMonsterTypes.Champion) == 1
                && instance.RegisterMonsterKill(GameDungeonMonsterTypes.SuperChampion) == 2
                && instance.ChampionMonsterKills == 2
                && instance.ChampionRewardOrdinal == 2,
            "champion kills share one monotonic reward-ordinal sequence across the party");
        check(instance.RegisterMonsterKill(GameDungeonMonsterTypes.Boss)
                == GameProtocolEngine.NoChampionRewardOrdinal
                && instance.BossMonsterKills == 1,
            "a boss kill counts into the boss bucket");
        instance.StopRun();
        check(instance.NormalMonsterKills == 0 && instance.ChampionRewardOrdinal == 0,
            "stopping the run clears the per-run kill counters");
    }

    /// <summary>奖励动画序号的封顶（13338 至多 11 次）：到顶后不再增长、也不再返回序号。</summary>
    private static void CheckChampionRewardOrdinalCap(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the fixture run starts for the ordinal cap");
        for (var i = 1; i <= GameProtocolEngine.MaximumChampionRewardOrdinal; i++)
        {
            check(instance.RegisterMonsterKill(GameDungeonMonsterTypes.Champion) == (sbyte)i,
                $"champion reward ordinal {i} is handed out in order");
        }

        check(instance.RegisterMonsterKill(GameDungeonMonsterTypes.Champion)
                == GameProtocolEngine.NoChampionRewardOrdinal
                && instance.ChampionRewardOrdinal == GameProtocolEngine.MaximumChampionRewardOrdinal
                && instance.ChampionMonsterKills == GameProtocolEngine.MaximumChampionRewardOrdinal + 1,
            "past the animation cap the ordinal stops while the kill count keeps rising");
    }

    /// <summary>
    /// 并发 CMD 16 的开局仲裁（终审 P5）：两个成员会话可能都通过 CanAcceptJoin 门槛，
    /// 第二个人若照旧 `TryStartRun`，`DungeonRunState.TryStart` 会先 `Stop()` 把先到者的
    /// 迷宫/房间几何重掷成另一套（先到者的 NOTI 28/29 已经发出去）。
    /// `TryStartRunForEntry` 把"判定 + 开局"收进锁内：第二个人拿 `AlreadyActive`，房间一字不动。
    /// **变异验证**：把方法里的 `if (_run.IsActive) return AlreadyActive;` 删掉（或改回
    /// 无条件 `TryStartRun`）→ 第二/第三条断言变红（返回 Started、迷宫被重掷）。
    /// 会话说"拿到 AlreadyActive 就改走 live-join"的接线不可单测（会话无接缝）。
    /// </summary>
    private static void CheckRunStartArbitration(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        var first = instance.TryStartRunForEntry(fixture.Layout, 0, 0, out var firstTransition);
        check(first == DungeonRunEntryDecision.Started
                && instance.Run.IsActive
                && firstTransition.Room.MapId != 0,
            "the first entry claims the run start and enters the start room");
        // 让先到者真正离开起始房间并开放房间：一旦第二个人把 run 重掷（TryStart 会 Stop + 重进
        // 起始房间），下面两条断言都会看到 (0,0)/未开放而变红——这就是"重掷迷宫几何"的可观测面。
        check(instance.TryMoveRoom(1, 0, out _), "the claimed run advances to the next room");
        instance.TryEnableClear();
        var second = instance.TryStartRunForEntry(fixture.Layout, 0, 0, out _);
        check(second == DungeonRunEntryDecision.AlreadyActive,
            "the second concurrent entry is told the run is already active (it must live-join instead)");
        check(instance.Run.IsActive
                && instance.Run.RoomX == 1
                && instance.Run.RoomY == 0
                && instance.ClearEnabled,
            "the already-active run keeps its room and clear flags (a restart would re-enter the start room)");
        check(instance.TryStartRunForEntry(fixture.Layout, 0, 0, out _)
                == DungeonRunEntryDecision.AlreadyActive
                && instance.MemberCount == 1,
            "repeated arbitration is idempotent and never touches the roster");
    }

    /// <summary>
    /// 锁内的运行态只读（终审 P6）：会话不再裸读 `_run` 的 `HellMode`/`CurrentRoom`，
    /// 走实例的锁内访问器。访问器必须与 `DungeonRunState` 同口径（非地狱模式恒 false、
    /// run 未活时当前房间为 null）。
    /// **变异验证**：把 `HellMode` 改成恒 true（或 `RunCurrentRoom` 改成恒 null）→ 断言变红。
    /// 会话三处调用点的替换不可单测（同一接缝限制），但它们都只是取值来源的替换。
    /// </summary>
    private static void CheckLockedRunReadAccessors(Action<bool, string> check, DungeonFixture fixture)
    {
        var instance = fixture.CreateInstance();
        instance.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(!instance.HellMode && instance.RunCurrentRoom is null,
            "an idle instance reports neither hell mode nor a current room");
        check(instance.TryStartRun(fixture.Layout, 0, 0, out _)
                && !instance.HellMode
                && instance.RunCurrentRoom is { MapId: not 0 },
            "a normal run reports its current room through the locked accessor");
        check(instance.TryMoveRoom(1, 0, out var transition)
                && instance.RunCurrentRoom is { } movedRoom
                && movedRoom.MapId == transition.Room.MapId
                && movedRoom.Monsters.Length == transition.Room.Monsters.Length,
            "the locked current-room accessor follows the room the run actually entered");

        var hell = fixture.CreateInstance();
        hell.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(hell.TryStartRun(fixture.HellLayout, 0, dungeonOption: 1, out _) && hell.HellMode,
            "a hell dungeon run reports hell mode through the locked accessor");
    }

    /// <summary>
    /// 地狱房的被动掉落 detach（终审 I3）：detach 从单人的 CMD 48 分支搬进
    /// `DungeonInstance.TryMoveRoom` 的锁内后，三条换房路径（单人立即推进 / 组队自己推进 /
    /// 倒计时强制推进）都拿到同一份行为。两个条件各一个用例：
    /// ①退出**未清**的地狱房（`CompletedHellRoomOnExit`）→ 离开的那间房的被动掉落失联；
    /// ②走进**已清**的地狱房（客户端重建空场景）→ 那间房留下的被动掉落失联。
    /// detach 只摘掉"被动物件关联"（掉落本身还在本房间的地面表里，仍是普通掉落）——
    /// 所以两条都同时断言 `GetRoomGroundItems` 仍是 2 条。
    /// **变异验证**：把 `TryMoveRoom` 里那两段 detach 删掉 → 第二/第四条断言变红（掉落没被摘）。
    /// 组队两条推进路径的调用点（AdvancePartyRoomAsync / 扫描）不额外断言——它们走
    /// `TryMoveRoom` 同一份实现，本用例覆盖的就是那份实现。
    /// </summary>
    private static void CheckHellRoomPassiveDetachOnMove(Action<bool, string> check, DungeonFixture fixture)
    {
        // ① 退出未清的地狱房：`CompletedHellRoomOnExit` → detach 离开的那间房。
        var exit = fixture.CreateInstance();
        exit.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(exit.TryStartRun(fixture.HellLayout, 0, dungeonOption: 1, out _)
                && exit.TryMoveRoom(1, 0, out _),
            "the hell-mode run enters the hell room");
        check(exit.RegisterRoomPassiveDrops(
                    DungeonFixture.Room([], passiveDropCount: 2), 1, 1, 0).Length == 2
                && exit.GetRoomPassiveDrops(1, 0).Length == 2,
            "two passive drops are registered in the hell room");
        check(exit.TryMoveRoom(0, 0, out var leavingHell) && leavingHell.CompletedHellRoomOnExit,
            "leaving the uncleared hell room reports CompletedHellRoomOnExit");
        check(exit.GetRoomPassiveDrops(1, 0).Length == 0
                && exit.GetRoomGroundItems(1, 0).Length == 2,
            "leaving an uncleared hell room detaches its passive drops (they stay as plain ground items)");

        // ② 走进已清的地狱房：目标房间那次的被动掉落要 detach（客户端重建的是空场景）。
        var cleared = fixture.CreateInstance();
        cleared.AddMember(fixture.Member(Guid.NewGuid(), 0, "队长"));
        check(cleared.TryStartRun(fixture.HellLayout, 0, dungeonOption: 1, out _)
                && cleared.TryMoveRoom(1, 0, out _),
            "the second hell-mode run enters the hell room");
        check(cleared.RegisterRoomPassiveDrops(
                    DungeonFixture.Room([], passiveDropCount: 2), 1, 1, 0).Length == 2,
            "the second hell room also holds two passive drops");
        var hellRoomMonsters = cleared.RunCurrentRoom?.Monsters ?? [];
        check(hellRoomMonsters.Length != 0,
            "the hell room exposes its monsters through the locked current-room accessor");
        foreach (var monster in hellRoomMonsters)
        {
            cleared.TryTakeMonsterDeath(monster.UniqueId, out _);
        }

        check(cleared.IsCurrentHellRoomCleared(),
            "the hell encounter is cleared once every hell-room monster is defeated");
        check(cleared.TryMoveRoom(0, 0, out var leftCleared) && !leftCleared.CompletedHellRoomOnExit
                && cleared.GetRoomPassiveDrops(1, 0).Length == 2,
            "leaving a cleared hell room keeps its drops (the detach belongs to the return entry)");
        check(cleared.TryMoveRoom(1, 0, out _)
                && cleared.IsCurrentHellRoomCleared()
                && cleared.GetRoomPassiveDrops(1, 0).Length == 0
                && cleared.GetRoomGroundItems(1, 0).Length == 2,
            "re-entering the cleared hell room detaches the previous encounter's passive drops");
    }

    /// <summary>
    /// 跨会话投递的坏通道护栏（终审 I1）：每个成员句柄写的是**他自己会话**的流，
    /// 被踢/掉线的成员会抛**他自己 token** 的 `OperationCanceledException`；
    /// 现在的 filter 只捕 IOException/ObjectDisposed/Socket，OCE 会顺着**发送者**的会话冒泡
    /// （击杀/开放/拾取广播把健康玩家打断；扫描里让房间已经换了、后面的成员收不到 NOTI 29）。
    /// 两条生产路径：①`DungeonPartyService.BroadcastToMembersAsync`（生产广播实现）；
    /// ②`PartyService.SendToOtherMembersAsync`（队长外观推送走的真通道）。
    /// 同时钉住 P1 的单人护栏：不在队伍的角色一份包都不会多发。
    /// **变异验证**：把 `CrossSessionDelivery.IsBrokenChannel` 里的 `or OperationCanceledException`
    /// 删掉 → 第一次 await 就把 OCE 抛出去，本用例变红。
    /// 会话里的局部发送函数（AdvancePartyRoomAsync）与会话接线同一局限，但共用这份判定。
    /// </summary>
    private static async Task CheckCrossSessionDeliverySurvivesCanceledMembersAsync(
        Action<bool, string> check,
        DungeonFixture fixture)
    {
        await using var harness = await PartyHarness.CreateAsync();
        var registry = new DungeonInstanceRegistry();
        using var service = new DungeonPartyService(
            harness.Service,
            harness.Directory,
            registry,
            TimeProvider.System,
            NullLogger<DungeonPartyService>.Instance);

        // ① 实例广播：第一个成员（被踢）的句柄抛 OCE，排在它后面的健康成员仍各收一份。
        var instance = registry.GetOrCreateForParty(1000, () => fixture.CreatePartyInstance(1000));
        var canceledMember = Guid.NewGuid();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var alphaInbox = new List<GameServerPacket>();
        var bravoInbox = new List<GameServerPacket>();
        instance.AddMember(new DungeonInstanceMember(
            canceledMember, 1, 0, "被踢", _ => throw new OperationCanceledException()));
        instance.AddMember(new DungeonInstanceMember(
            alpha, 2, 1, "甲", packet => { alphaInbox.Add(packet); return ValueTask.CompletedTask; }));
        instance.AddMember(new DungeonInstanceMember(
            bravo, 3, 2, "乙", packet => { bravoInbox.Add(packet); return ValueTask.CompletedTask; }));
        await service.BroadcastToMembersAsync(
            instance, GameProtocolEngine.CreateEnableClearDungeon());
        check(alphaInbox.Count == 1 && bravoInbox.Count == 1,
            "members queued after a canceled channel still receive the instance broadcast");

        // 扫描里的跨会话投递同一条护栏（结算倒计时逐秒播报走 SendSafelyAsync）：
        // 被踢成员也登记了回城截止时间 ⇒ 扫描**一定会**试图给它发一条倒计时消息（抛它自己的 OCE）；
        // 去掉 OCE 判定的话那个异常会把整轮扫描打断，健康成员一条都收不到。
        var sweepInstance = registry.GetOrCreateForParty(
            1001, () => fixture.CreatePartyInstance(1001));
        var sweepInbox = new List<GameServerPacket>();
        var stayed = Guid.NewGuid();
        sweepInstance.AddMember(new DungeonInstanceMember(
            canceledMember, 1, 0, "被踢", _ => throw new OperationCanceledException()));
        sweepInstance.AddMember(new DungeonInstanceMember(
            bravo, 2, 1, "乙", packet => { sweepInbox.Add(packet); return ValueTask.CompletedTask; }));
        sweepInstance.AddMember(new DungeonInstanceMember(stayed, 3, 2, "留守", _ => ValueTask.CompletedTask));
        check(sweepInstance.TryStartRun(fixture.Layout, 0, 0, out _),
            "the sweep instance starts a run for the canceled-member case");
        var sweepNow = DateTimeOffset.UtcNow;
        // 三家都登记"还差 5 秒"（< SettlementCountdownSeconds = 10 ⇒ 这一轮扫描就该各播一条）。
        sweepInstance.ArmSettlementReturn(canceledMember, sweepNow.AddSeconds(5));
        sweepInstance.ArmSettlementReturn(bravo, sweepNow.AddSeconds(5));
        sweepInstance.ArmSettlementReturn(stayed, sweepNow.AddSeconds(5));

        await service.SweepInstanceTimersAsync(CancellationToken.None);
        check(sweepInbox.Count == 1
                && sweepInbox[0].ProtocolId == GameProtocolEngine.MessageNotification,
            "a canceled channel in the sweep does not stop the healthy member's countdown message");

        // ② PartyService 真通道（队长外观推送走的就是这条）：目标通道抛 OCE → 只告警、返回 0、
        //    不冒泡；不在队伍的角色（单人）走同一条路时零投递（P1 的"单人绝不多发一份包"护栏）。
        await harness.InviteAndAcceptAsync("阿尔法", "布拉沃");
        var bravoId = harness.IdOf("布拉沃");
        await harness.Service.AttachChannelAsync(
            "bobby",
            "布拉沃",
            bravoId,
            harness.UidOf("布拉沃"),
            harness.SessionIdOf("布拉沃"),
            _ => throw new OperationCanceledException(),
            CancellationToken.None);
        var delivered = await harness.Service.SendToOtherMembersAsync(
            harness.IdOf("阿尔法"),
            GameProtocolEngine.CreateEnableClearDungeon(),
            CancellationToken.None);
        check(delivered == 0,
            "a party send to a canceled channel returns 0 instead of throwing the recipient's OCE");

        var partyInbox = new List<GameServerPacket>();
        await harness.Service.AttachChannelAsync(
            "bobby",
            "布拉沃",
            bravoId,
            harness.UidOf("布拉沃"),
            harness.SessionIdOf("布拉沃"),
            packet => { partyInbox.Add(packet); return ValueTask.CompletedTask; },
            CancellationToken.None);
        var deliveredAgain = await harness.Service.SendToOtherMembersAsync(
            harness.IdOf("阿尔法"),
            GameProtocolEngine.CreateEnableClearDungeon(),
            CancellationToken.None);
        check(deliveredAgain == 1 && partyInbox.Count == 1,
            "a healthy member still receives the party send exactly once");

        partyInbox.Clear();
        harness.ClearAll();
        var soloDelivered = await harness.Service.SendToOtherMembersAsync(
            harness.IdOf("查理"),
            GameProtocolEngine.CreateEnableClearDungeon(),
            CancellationToken.None);
        check(soloDelivered == 0
                && partyInbox.Count == 0
                && harness.Of("阿尔法").Count == 0
                && harness.Of("布拉沃").Count == 0
                && harness.Of("查理").Count == 0,
            "a character outside any party delivers nothing (the solo path can never add a packet)");
    }
}

/// <summary>测试夹具：一份最小 PVF（3×1 迷宫 + 3 张地图）与实例工厂。</summary>
internal sealed class DungeonFixture : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"dflegacy-dungeon-party-{Guid.NewGuid():N}");

    public DungeonFixture(string? bossMonsterRows = null, string? bossMonsterTeams = null)
    {
        foreach (var directory in new[] { "dungeon", "map", "quest" })
        {
            Directory.CreateDirectory(Path.Combine(_root, directory));
        }

        File.WriteAllText(Path.Combine(_root, "dungeon/dungeon.lst"), "14 `test.dgn`\n15 `hell.dgn`\n");
        File.WriteAllText(Path.Combine(_root, "dungeon/test.dgn"),
            "[minimum level] 1\n[basis level] 1\n[maze info]\n[size] 3 1\n"
            + "[greed]\n`PPP`\n[start map]\n0 0\n[/start map]\n"
            + "[boss map]\n2 0\n1 0\n[/boss map]\n");
        // 地狱副本（终审 I3 用例）：地狱房在 (1,0)、封门地图 = normal.map（2）；boss 房在 (2,0)。
        File.WriteAllText(Path.Combine(_root, "dungeon/hell.dgn"),
            "[minimum level] 1\n[basis level] 1\n[hell dungeon] 1\n"
            + "[seal door map index] 2\n[seal door pos] 1 0\n"
            + "[maze info]\n[size] 3 1\n"
            + "[greed]\n`PPP`\n[start map]\n0 0\n[/start map]\n"
            + "[boss map]\n2 0\n[/boss map]\n");
        File.WriteAllText(Path.Combine(_root, "map/map.lst"), "1 `start.map`\n2 `normal.map`\n3 `boss.map`\n");
        foreach (var name in new[] { "start", "normal", "boss" })
        {
            // 两张地图表共用这三张 .map（与真实 PVF 一样按 [dungeon] 列出多个副本 id）。
            File.WriteAllText(Path.Combine(_root, $"map/{name}.map"),
                "[dungeon]\n14\n15\n[/dungeon]\n"
                + $"[type] `[{(name == "boss" ? "boss" : "normal")}]`\n"
                + "[greed]\n`P`\n[monster]\n"
                + (name == "boss" && bossMonsterRows is not null ? bossMonsterRows : "1 1 1 100 100 0 1 1 `[fixed]` `[normal]`\n")
                + "[/monster]\n"
                + (name == "boss" && bossMonsterTeams is not null
                    ? "[monster team]\n" + bossMonsterTeams + "\n[/monster team]\n" : ""));
        }

        var options = new ServerOptions { ScriptPvfPath = "", SkillScriptPath = _root };
        var scripts = new ScriptFileSystem(options, NullLogger<ScriptFileSystem>.Instance);
        Catalog = new DungeonCatalog(scripts,
            new ObjectDropGenerator(options,
                new ObjectDropCatalog(scripts, NullLogger<ObjectDropCatalog>.Instance),
                new GoldDropCatalog(scripts, NullLogger<GoldDropCatalog>.Instance)),
            NullLogger<DungeonCatalog>.Instance);
        Items = new ItemCatalog(scripts, NullLogger<ItemCatalog>.Instance);
        Random = new ZeroDropRandomSource();
        if (!Catalog.TryCreateLayout(14, out var layout, random: Random)
            || !Catalog.TryCreateLayout(15, out var hellLayout, random: Random))
        {
            throw new InvalidOperationException("the fixture dungeon layout is missing");
        }

        Layout = layout;
        HellLayout = hellLayout;
    }

    public DungeonCatalog Catalog { get; }
    public ItemCatalog Items { get; }
    public ZeroDropRandomSource Random { get; }
    public DungeonLayout Layout { get; }

    /// <summary>地狱副本（id 15，`[hell dungeon] 1`，地狱房 (1,0)）的布局，I3 的被动掉落 detach 用例用。</summary>
    public DungeonLayout HellLayout { get; }

    /// <summary>合成怪：只关心 UniqueId 与 Type（`EnterDetachedRoom` 路径不经过 PVF 解析）。</summary>
    public static GameDungeonMonster Monster(ushort uniqueId, byte type = GameDungeonMonsterTypes.Normal) =>
        new(MapListIndex: checked((byte)uniqueId), uniqueId, MonsterIndex: 1, Level: 2, Type: type);

    /// <summary>
    /// 合成房间（喂 `EnterDetachedRoom` 的"教学副本"路径）：`passiveDropCount` 个被动掉落。
    /// 用金币是为了不依赖物品脚本——`CreateGeneratedItem` 对金币直接返回 null，
    /// `GetClientAddInfo` 也走 `CountOrValue` 直通；非金币会落到 `CharacterItemWireProjection`。
    /// </summary>
    public static DungeonRoomDefinition Room(
        GameDungeonMonster[] monsters,
        int passiveDropCount = 0)
    {
        var spawns = new DungeonPassiveItemSpawn[passiveDropCount];
        for (var index = 0; index < passiveDropCount; index++)
        {
            spawns[index] = new DungeonPassiveItemSpawn(
                PassiveObjectIndex: checked((byte)index),
                AssociationSlot: checked((byte)index),
                new DungeonGeneratedDrop(IsGold: true, ItemId: 0, CountOrValue: 100))
            {
                X = 100,
                Y = 100
            };
        }

        return new DungeonRoomDefinition(
            MapId: 2,
            DungeonMapType.Normal,
            monsters,
            Topology: 'P',
            HasScriptBossActor: false,
            PassiveObjects: [],
            PassiveItemSpawns: spawns);
    }

    /// <summary>单人实例工厂（键 = 角色 id）。</summary>
    public DungeonInstance CreateSoloInstance(Guid characterId) =>
        new(partyId: 0, soloCharacterId: characterId, Catalog, Items);

    /// <summary>队伍实例工厂（键 = partyId）。</summary>
    public DungeonInstance CreatePartyInstance(ushort partyId) =>
        new(partyId: partyId, soloCharacterId: Guid.Empty, Catalog, Items);

    /// <summary>不关心键的独立实例（并发/房间快照用例用）。</summary>
    public DungeonInstance CreateInstance() => CreateSoloInstance(Guid.Empty);

    public DungeonInstanceMember Member(Guid characterId, byte slot, string name) =>
        new(characterId, AreaUserId: 1, slot, name, _ => ValueTask.CompletedTask);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// 可手动推进的 <see cref="TimeProvider"/>：倒计时扫描的用例不该真睡 10 秒。
/// 生产代码只经 `DungeonPartyService._timeProvider.GetUtcNow()` 取时刻，所以这一层就够。
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
