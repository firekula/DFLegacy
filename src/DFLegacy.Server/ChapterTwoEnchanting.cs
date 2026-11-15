using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed class ChapterTwoEnchanting(
    JsonGameStore store, TownSessionDirectory towns, ItemTradeService trades,
    ItemCatalog items, ChapterTwoEnchantCatalog catalog, ILogger logger)
{
    private const byte RequestEnchant = 6;
    private const byte OfferEnchant = 7;
    private static readonly TimeSpan InviteTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OpenTime = TimeSpan.FromMinutes(5);

    internal GameSkillEntry? GetProfessionSkill(byte expertJob) => catalog.GetProfessionSkill(expertJob);

    private sealed record Peer(TownMemberTicket Ticket, string Account, Action Wake)
    {
        public Guid Id => Ticket.CharacterId;
        public ushort Uid => Ticket.AreaUserId;
    }

    private sealed record Proposal(CharacterItemRecord Item, EnchantRecipe Recipe, ushort Chance, uint Proficiency);
    private sealed record Install(Peer Peer, Guid TransactionId, CharacterRecord Character);

    private sealed class Session(Peer inviter, Peer invited, byte type)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Peer Inviter { get; } = inviter;
        public Peer Invited { get; } = invited;
        public Peer Requester { get; } = type == RequestEnchant ? inviter : invited;
        public Peer Enchanter { get; } = type == RequestEnchant ? invited : inviter;
        public byte Type { get; } = type;
        public CancellationTokenSource Lifetime { get; } = new();
        public bool Open { get; set; }
        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow + InviteTime;
        public uint? Fee { get; set; }
        public ushort LastRequesterSequence { get; set; }
        public byte TargetList { get; set; }
        public CharacterItemRecord? Target { get; set; }
        public Proposal? Proposal { get; set; }
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Peer> _peers = [];
    private readonly Dictionary<Guid, Session> _sessions = [];
    private readonly ConcurrentDictionary<Guid, Install> _pending = new();

    public async Task AttachAsync(TownMemberTicket ticket, string account, Action wake, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_peers.TryGetValue(ticket.CharacterId, out var old) && !ReferenceEquals(old.Ticket, ticket))
                await RemovePeerAsync(old);
            _peers[ticket.CharacterId] = new(ticket, account, wake);
        }
        finally { _gate.Release(); }
    }

    public async Task LeaveAsync(TownMemberTicket ticket, bool notifyLeaving)
    {
        await _gate.WaitAsync();
        try
        {
            if (_peers.TryGetValue(ticket.CharacterId, out var peer) && ReferenceEquals(peer.Ticket, ticket))
                await RemovePeerAsync(peer, notifyLeaving);
        }
        finally { _gate.Release(); }
    }

    private async Task RemovePeerAsync(Peer peer, bool notifyLeaving = false)
    {
        _peers.Remove(peer.Id);
        if (_pending.TryRemove(peer.Id, out var install))
            store.AcknowledgeTradeDelivery(peer.Id, install.TransactionId);
        if (_sessions.TryGetValue(peer.Id, out var session))
            await CloseAsync(session, notifyLeaving ? null : peer);
    }

    public bool TryTakeInstall(TownMemberTicket? ticket, out Guid transactionId, out CharacterRecord character)
    {
        transactionId = default;
        character = null!;
        if (ticket is null || !_pending.TryGetValue(ticket.CharacterId, out var pending)
            || !ReferenceEquals(ticket, pending.Peer.Ticket)
            || !_pending.TryRemove(new KeyValuePair<Guid, Install>(ticket.CharacterId, pending)))
            return false;
        transactionId = pending.TransactionId;
        character = pending.Character;
        return true;
    }

    public async Task HandlePeerAsync(TownMemberTicket ticket, ushort otherUid, byte type, bool response,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!TryFind(ticket, out var sender)) return;
            await SweepAsync();
            if (!response)
            {
                var target = _peers.Values.FirstOrDefault(p => p.Uid == otherUid);
                if (target is null || target == sender || _sessions.ContainsKey(sender.Id)
                    || _sessions.ContainsKey(target.Id) || !CanInteract(sender, target))
                    return;
                var session = new Session(sender, target, type);
                if ((await store.GetCharacterAsync(session.Enchanter.Account, session.Enchanter.Id, ct))?.ExpertJob != 1)
                    return;
                _sessions.Add(sender.Id, session);
                _sessions.Add(target.Id, session);
                if (await SendAsync(target, GameProtocolEngine.CreateEnchantPeerPacket(
                    false, sender.Uid, type, type == OfferEnchant ? 1u : 0u)))
                    _ = ExpireSessionAsync(session, session.Lifetime.Token);
                else
                    RemoveSession(session);
                return;
            }

            if (!_sessions.TryGetValue(sender.Id, out var pending) || pending.Open
                || pending.Invited != sender || pending.Inviter.Uid != otherUid || pending.Type != type)
                return;
            if (!CanInteract(sender, pending.Inviter))
            {
                RemoveSession(pending);
                return;
            }

            if (!store.Commerce.TryAcquire(CommerceActivity.Enchant, pending.Id,
                    pending.Requester.Id, pending.Enchanter.Id))
            {
                RemoveSession(pending);
                return;
            }

            pending.Open = true;
            pending.ExpiresAt = DateTimeOffset.UtcNow + OpenTime;
            // NOTI8 mode 6 opens the equipment-owner controls (CMD181).
            // Mode 7 opens the enchanter's recipe controls (CMD180).
            if (!await SendAsync(pending.Requester,
                    GameProtocolEngine.CreateEnchantPeerPacket(true, pending.Enchanter.Uid, RequestEnchant))
                || !await SendAsync(pending.Enchanter,
                    GameProtocolEngine.CreateEnchantPeerPacket(true, pending.Requester.Uid, OfferEnchant)))
                await CloseAsync(pending);
        }
        finally { _gate.Release(); }
    }

    public async Task HandleCommandAsync(TownMemberTicket ticket, byte op, byte[] body, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!TryFind(ticket, out var sender)) return;
            await SweepAsync();
            if (!_sessions.TryGetValue(sender.Id, out var session) || !session.Open)
            {
                // Closing a native window can send its cancellation command again.
                var closedWindow = op == GameProtocolEngine.ConfirmEnchantProposalCommand
                    && GameProtocolEngine.TryReadEnchantConsent(body, out var ownerClose) && ownerClose == 2
                    || op == GameProtocolEngine.SubmitEnchantRecipeCommand
                    && GameProtocolEngine.TryReadCooperativeEnchantRecipe(body, out _, out var workerClose) && workerClose;
                if (!closedWindow) await SendAsync(sender, GameProtocolEngine.CreateEnchantError(op, 17));
                return;
            }
            if (op == GameProtocolEngine.SetEnchantTermsCommand && sender == session.Requester
                && GameProtocolEngine.TryReadEnchantTerms(body, out var fee, out var list, out var slot))
            {
                session.LastRequesterSequence = BinaryPrimitives.ReadUInt16LittleEndian(body);
                await TermsAsync(session, fee, list, slot, ct);
                return;
            }
            if (op == GameProtocolEngine.SubmitEnchantRecipeCommand
                && GameProtocolEngine.TryReadCooperativeEnchantRecipe(body, out var recipeSlot, out var cancel))
            {
                if (cancel && sender == session.Enchanter)
                {
                    await CloseAsync(session);
                    return;
                }
                if (!cancel && sender == session.Enchanter)
                {
                    await ProposeAsync(session, recipeSlot, ct);
                    return;
                }
            }
            if (op == GameProtocolEngine.ConfirmEnchantProposalCommand
                && GameProtocolEngine.TryReadEnchantConsent(body, out var consent))
            {
                if (consent == 2 && sender == session.Requester)
                {
                    await CloseAsync(session);
                    return;
                }
                if (consent < 2 && sender == session.Requester && session.Proposal is not null)
                {
                    var sequence = BinaryPrimitives.ReadUInt16LittleEndian(body);
                    var advance = unchecked((ushort)(sequence - session.LastRequesterSequence));
                    if (advance is > 0 and < 0x8000)
                    {
                        session.LastRequesterSequence = sequence;
                        await ConsentAsync(session, consent, ct);
                        return;
                    }
                }
            }

            await SendAsync(sender, GameProtocolEngine.CreateEnchantError(op, 17));
            await CloseAsync(session, sender);
        }
        finally { _gate.Release(); }
    }

    private async Task TermsAsync(Session session, uint? fee, byte list, ushort slot, CancellationToken ct)
    {
        if (session.Proposal is not null)
        {
            await FailAsync(session, session.Requester, GameProtocolEngine.SetEnchantTermsCommand, 17);
            return;
        }
        var requester = await store.GetCharacterAsync(session.Requester.Account, session.Requester.Id, ct);
        if (requester is null) return;
        if (fee is { } value)
        {
            var enchanter = await store.GetCharacterAsync(session.Enchanter.Account, session.Enchanter.Id, ct);
            if (value > requester.Gold || enchanter is null || value > int.MaxValue - enchanter.Gold)
            {
                await FailAsync(session, session.Requester, GameProtocolEngine.SetEnchantTermsCommand, 95);
                return;
            }
            session.Fee = value;
        }
        else
        {
            var target = (list == 0 ? requester.Inventory : requester.Equipment)?.FirstOrDefault(item => item.Slot == slot);
            if (target is null || target.InstanceId == Guid.Empty || !items.TryGetDefinition(target.ItemId, out var equipment)
                || equipment.InventoryCategory != ItemInventoryCategory.Equipment
                || trades.IsEscrowed(requester.Id, list == 9 ? (byte)3 : list, slot))
            {
                await FailAsync(session, session.Requester, GameProtocolEngine.SetEnchantTermsCommand, 21);
                return;
            }
            session.Target = target;
            session.TargetList = list == 9 ? (byte)3 : list;
        }

        session.ExpiresAt = DateTimeOffset.UtcNow + OpenTime;
        await SendAsync(session.Requester, GameProtocolEngine.CreateEnchantReply(GameProtocolEngine.SetEnchantTermsCommand));
        if (!await SendAsync(session.Enchanter, GameProtocolEngine.CreateEnchantTermsNotification(fee, session.Target?.ItemId ?? 0)))
            await CloseAsync(session);
    }

    private async Task ProposeAsync(Session session, ushort recipeSlot, CancellationToken ct)
    {
        if (session.Target is not { } target || session.Fee is null || session.Proposal is not null)
        {
            await FailAsync(session, session.Enchanter, GameProtocolEngine.SubmitEnchantRecipeCommand, 21);
            return;
        }
        var enchanter = await store.GetCharacterAsync(session.Enchanter.Account, session.Enchanter.Id, ct);
        var inventory = enchanter?.Inventory ?? [];
        var item = inventory.FirstOrDefault(row => row.Slot == recipeSlot && row.CountOrValue > 0
            && !trades.IsEscrowed(session.Enchanter.Id, 0, row.Slot));
        var recipe = item is null ? null : catalog.GetRecipe(item.ItemId);
        if (recipe is null || enchanter?.ExpertJob != 1
            || catalog.Level(enchanter.EnchantingProficiency) < recipe.RequiredLevel
            || recipe.Ingredients.Any(need => inventory.Where(row => row.ItemId == need.ItemId
                && !trades.IsEscrowed(enchanter.Id, 0, row.Slot)).Sum(row => (long)row.CountOrValue) < need.Count))
        {
            await FailAsync(session, session.Enchanter, GameProtocolEngine.SubmitEnchantRecipeCommand, 17);
            return;
        }

        if (!items.TryGetDefinition(target.ItemId, out var equipment) || !recipe.EquipmentTypes.Contains(equipment.TypeTag))
        {
            await FailAsync(session, session.Enchanter, GameProtocolEngine.SubmitEnchantRecipeCommand, 23);
            return;
        }

        var chance = catalog.Chance(enchanter, equipment, recipe);
        session.Proposal = new(item!, recipe, chance, enchanter.EnchantingProficiency);
        session.ExpiresAt = DateTimeOffset.UtcNow + OpenTime;
        if (!await SendAsync(session.Requester, GameProtocolEngine.CreateEnchantProposal(chance, recipe.RecipeId)))
            await CloseAsync(session);
    }

    private async Task ConsentAsync(Session session, byte consent, CancellationToken ct)
    {
        var proposal = session.Proposal!;
        if (consent == 0)
        {
            session.Proposal = null;
            await SendAsync(session.Requester, GameProtocolEngine.CreateEnchantReply(
                GameProtocolEngine.ConfirmEnchantProposalCommand, 0));
            await SendAsync(session.Enchanter, GameProtocolEngine.CreateEnchantDeclined());
            return;
        }

        var transactionId = Guid.NewGuid();
        EnchantCommit? commit = null;
        try
        {
            commit = await store.CommitCooperativeEnchantAsync(transactionId,
                session.Requester.Account, session.Requester.Id, session.Enchanter.Account, session.Enchanter.Id,
                session.TargetList, session.Target!, proposal.Item, proposal.Recipe, session.Fee!.Value,
                proposal.Chance, proposal.Proficiency, catalog, () => CanInteract(session.Requester, session.Enchanter),
                trades.IsEscrowed, ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(error, "Failed to persist cooperative enchant {TransactionId}.", transactionId);
        }

        if (commit is null)
        {
            await FailAsync(session, session.Requester, GameProtocolEngine.ConfirmEnchantProposalCommand, 17);
            return;
        }

        RemoveSession(session);
        // Install queues are populated before replies to keep later owner commands on the same durable revision.
        _pending[session.Requester.Id] = new(session.Requester, transactionId, commit.Requester);
        _pending[session.Enchanter.Id] = new(session.Enchanter, transactionId, commit.Enchanter);
        try
        {
            await SendAsync(session.Requester, GameProtocolEngine.CreateEnchantReply(
                GameProtocolEngine.ConfirmEnchantProposalCommand, 1));
            var proficiency = commit.Enchanter.EnchantingProficiency;
            await SendAsync(session.Requester, GameProtocolEngine.CreateEnchantResult(
                commit.Succeeded, commit.OldProficiency, proficiency, []));
            await SendAsync(session.Enchanter, GameProtocolEngine.CreateEnchantResult(
                commit.Succeeded, commit.OldProficiency, proficiency, commit.Materials));
        }
        finally
        {
            session.Requester.Wake();
            session.Enchanter.Wake();
        }
    }

    private async Task FailAsync(Session session, Peer peer, byte op, byte error)
    {
        await SendAsync(peer, GameProtocolEngine.CreateEnchantError(op, error));
        await CloseAsync(session, peer);
    }

    private bool TryFind(TownMemberTicket ticket, out Peer peer) =>
        _peers.TryGetValue(ticket.CharacterId, out peer!) && ReferenceEquals(ticket, peer.Ticket);

    private bool CanInteract(Peer a, Peer b) =>
        _peers.GetValueOrDefault(a.Id) == a && _peers.GetValueOrDefault(b.Id) == b
        && towns.SharesPublicArea(a.Id, b.Id)
        && HasCurrentTownTicket(a) && HasCurrentTownTicket(b);

    private bool HasCurrentTownTicket(Peer peer) => towns.TryGetSnapshot(peer.Id, out var member)
        && member.AreaUserId == peer.Ticket.AreaUserId && member.TownId == peer.Ticket.TownId
        && member.AreaId == peer.Ticket.AreaId;

    private async Task SweepAsync()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _sessions.Values.Distinct().Where(s =>
            s.ExpiresAt <= now || !CanInteract(s.Inviter, s.Invited)).ToArray())
            await CloseAsync(session);
    }

    private async Task ExpireSessionAsync(Session session, CancellationToken ct)
    {
        var interval = InviteTime;
        try
        {
            while (true)
            {
                await Task.Delay(interval, ct);
                await _gate.WaitAsync(ct);
                try
                {
                    interval = session.ExpiresAt - DateTimeOffset.UtcNow;
                    if (interval > TimeSpan.Zero) continue;
                    await CloseAsync(session);
                    return;
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task CloseAsync(Session session, Peer? skip = null)
    {
        RemoveSession(session);
        if (!session.Open) return;
        var close = GameProtocolEngine.CreateCloseEnchantWindow();
        if (session.Inviter != skip) await SendAsync(session.Inviter, close);
        if (session.Invited != skip) await SendAsync(session.Invited, close);
    }

    private void RemoveSession(Session session)
    {
        _sessions.Remove(session.Inviter.Id);
        _sessions.Remove(session.Invited.Id);
        session.Lifetime.Cancel();
        session.Lifetime.Dispose();
        if (session.Open) store.Commerce.Release(session.Id, session.Requester.Id, session.Enchanter.Id);
    }

    private static async ValueTask<bool> SendAsync(Peer target, GameServerPacket packet)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await target.Ticket.Member.SendAsync(packet, timeout.Token);
            return true;
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}
