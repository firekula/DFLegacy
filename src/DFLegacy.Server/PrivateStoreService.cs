using System.Collections.Concurrent;
using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>Ordinary ACT1 shops. Only the calling actor installs inventory; seller income uses durable credits.</summary>
public sealed class PrivateStoreService(JsonGameStore store, ItemCatalog items, PrivateStoreCatalog catalog,
    TownSessionDirectory town, PartyService parties, ILogger<PrivateStoreService> logger,
    TimeSpan? deliveryTimeout = null) : BackgroundService
{
    private sealed record Channel(string Account, Guid Id, string Name, ushort Uid, Guid Session,
        Func<GameServerPacket, ValueTask> Send, Action? Kick, Action? Wake)
    {
        internal readonly Queue<GameServerPacket> Outbox = new();
        internal readonly SemaphoreSlim Delivery = new(1, 1);
        internal bool Retired;
        internal Guid StoreToken;
    }
    private sealed class Shop(Channel owner, PrivateStorePosition position)
    {
        internal readonly Channel Owner = owner;
        internal readonly Guid Token = Guid.NewGuid();
        internal readonly PrivateStorePosition Position = position;
        internal byte[] Title = [];
        internal bool Open;
        internal readonly HashSet<Guid> Viewers = [];
    }
    private sealed record Delivery(Channel Channel, GameServerPacket Packet);
    private readonly Dictionary<Guid, Channel> _channels = [];
    private readonly Dictionary<Guid, Shop> _shops = [];
    private readonly Dictionary<Guid, Shop> _viewing = [];
    private readonly ConcurrentDictionary<Guid, byte> _goldUpdates = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _lifecycles = new();
    private readonly ConcurrentQueue<Action> _kicks = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _deliveryTimeout = deliveryTimeout ?? TimeSpan.FromSeconds(5);
    private static readonly Encoding TextEncoding = StoreEncoding();
    private static Encoding StoreEncoding()
    { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
    private Channel? Current(Guid id, Guid session) => _channels.GetValueOrDefault(id) is { Retired: false } c && c.Session == session ? c : null;
    public bool IsOwner(Guid id) => store.Commerce.Is(id, CommerceActivity.Store) || store.Commerce.Is(id, CommerceActivity.StoreRevoked);
    public bool HasPendingGold(Guid id) => _goldUpdates.ContainsKey(id);
    public bool TryTakeGoldUpdate(Guid id) => _goldUpdates.TryRemove(id, out _);
    private async Task RunAsync(Func<List<Delivery>, Task> action, CancellationToken ct, Guid? caller = null)
    {
        var deliveries = new List<Delivery>(); Channel[] drains;
        await _gate.WaitAsync(ct);
        try
        {
            await action(deliveries);
            foreach (var d in deliveries) if (!d.Channel.Retired) d.Channel.Outbox.Enqueue(d.Packet);
            drains = deliveries.Select(d => d.Channel).Distinct().ToArray();
        }
        finally { _gate.Release(); }
        while (_kicks.TryDequeue(out var kick)) InvokeKick(kick);
        // Other recipients drain independently. The caller's own FIFO must finish
        // before its actor emits a newer inventory snapshot on the same stream.
        var pending = drains.Select(c => (Channel: c, Task: DrainAsync(c))).ToArray();
        if (caller is { } id) await Task.WhenAll(pending.Where(p => p.Channel.Id == id).Select(p => p.Task));
    }
    private async Task DrainAsync(Channel channel)
    {
        await channel.Delivery.WaitAsync();
        try
        {
            while (true)
            {
                GameServerPacket? packet = null;
                await _gate.WaitAsync();
                try
                {
                    if (channel.Retired) channel.Outbox.Clear();
                    else if (channel.Outbox.Count > 0) packet = channel.Outbox.Dequeue();
                }
                finally { _gate.Release(); }
                if (packet is null) break;
                if (Volatile.Read(ref channel.Retired)) continue;
                Task? sending = null;
                try
                {
                    sending = channel.Send(packet).AsTask();
                    await sending.WaitAsync(_deliveryTimeout);
                }
                catch (Exception e)
                {
                    await _gate.WaitAsync();
                    try { channel.Retired = true; channel.Outbox.Clear(); }
                    finally { _gate.Release(); }
                    if (sending is not null) _ = ObserveSendAsync(sending);
                    logger.LogWarning(e, "Retired stalled/broken store transport {Character}, packet {Packet}.", channel.Id, packet.ProtocolId);
                    if (channel.Kick is { } kick) InvokeKick(kick);
                    break;
                }
            }
        }
        finally { channel.Delivery.Release(); }
    }
    private static async Task ObserveSendAsync(Task task) { try { await task; } catch { } }
    private void InvokeKick(Action kick)
    { try { kick(); } catch (ObjectDisposedException) { } }
    private async Task LifecycleAsync(Guid id, Func<Task> action, CancellationToken ct)
    {
        var gate = _lifecycles.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct); try { await action(); } finally { gate.Release(); }
    }
    private void Kick(Channel c) { if (c.Kick is { } kick) _kicks.Enqueue(kick); }
    private void Area(List<Delivery> deliveries, Shop shop, GameServerPacket packet)
    {
        foreach (var c in _channels.Values)
            if (c.Id != shop.Owner.Id && town.SharesPublicArea(c.Id, shop.Owner.Id)) deliveries.Add(new(c, packet));
    }
    private void ExitViewer(Guid id)
    { if (_viewing.Remove(id, out var shop)) shop.Viewers.Remove(id); }
    private void RemoveShop(List<Delivery> deliveries, Shop shop, bool release = true)
    {
        foreach (var id in shop.Viewers.ToArray())
        {
            if (_channels.TryGetValue(id, out var c)) deliveries.Add(new(c, GameProtocolEngine.CreatePrivateStoreForceClose()));
            ExitViewer(id);
        }
        Area(deliveries, shop, GameProtocolEngine.CreatePrivateStoreRemoved(shop.Owner.Uid));
        _shops.Remove(shop.Owner.Id);
        if (release) store.Commerce.Release(shop.Token, shop.Owner.Id);
    }
    private PrivateStoreGoodsRow[] Rows(CharacterRecord c) => c.PrivateStoreStock?.Listings.Select(l =>
    {
        var wire = CharacterItemWireProjection.Project(l.Item, items);
        return new PrivateStoreGoodsRow(l.Slot, l.UnitPrice, l.Item.ItemId, l.AddInfo, wire.ItemAttr, wire.Durability);
    }).ToArray() ?? [];
    private static byte[] BuyerName(string name)
    {
        while (TextEncoding.GetByteCount(name) > 19) name = name[..^1];
        return TextEncoding.GetBytes(name);
    }
    public Task AttachChannelAsync(string account, Guid id, ushort uid, Guid session, Func<GameServerPacket, ValueTask> send,
        CancellationToken ct = default, Action? kick = null, Action? wake = null) => LifecycleAsync(id, async () =>
    {
        Channel? old = null;
        await RunAsync(async d =>
        {
            if (_channels.TryGetValue(id, out old))
            {
                old.Retired = true; old.Outbox.Clear();
                await CloseCoreAsync(d, old, true, false, ct);
                if (store.Commerce.Owns(id, CommerceActivity.StoreRevoked, old.StoreToken))
                    await store.ClosePrivateStoreAsync(id, old.StoreToken, true, ct);
                ExitViewer(id); _channels.Remove(id); store.Commerce.Release(old.StoreToken, id);
            }
        }, ct);
        if (old is not null) await DrainAsync(old);
        var character = await store.GetCharacterAsync(account, id, ct);
        if (character is null) return;
        await RunAsync(_ => { _channels[id] = new(account, id, character.Name, uid, session, send, kick, wake); return Task.CompletedTask; }, ct);
    }, ct);
    public Task DetachChannelAsync(Guid id, Guid session, CancellationToken ct = default) => LifecycleAsync(id, async () =>
    {
        Channel? old = null;
        await RunAsync(async d =>
        {
            old = _channels.GetValueOrDefault(id);
            if (old?.Session != session) { old = null; return; }
            old.Retired = true; old.Outbox.Clear();
            await CloseCoreAsync(d, old, true, false, ct);
            if (store.Commerce.Owns(id, CommerceActivity.StoreRevoked, old.StoreToken))
                await store.ClosePrivateStoreAsync(id, old.StoreToken, true, ct);
            ExitViewer(id); _channels.Remove(id); _goldUpdates.TryRemove(id, out _);
            store.Commerce.Release(old.StoreToken, id);
        }, ct);
        if (old is not null) await DrainAsync(old);
    }, ct);

    public async Task<bool> CreateAsync(Guid id, Guid session, PrivateStorePosition position, CancellationToken ct = default)
    {
        var created = false;
        await RunAsync(async d =>
        {
            var c = Current(id, session); if (c is null) return;
            void Reject(byte error) => d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(88, error)));
            if (_shops.ContainsKey(id)) { Reject(20); return; }
            if (!town.IsPublicTownMember(id) || !town.TryGetSnapshot(id, out var t) || t.TownId != position.Town || t.AreaId != position.Area)
            { Reject(62); return; }
            var error = catalog.CheckPosition(position); if (error != 0) { Reject(error); return; }
            if (!(await store.GetPremiumServicesAsync(c.Account, ct)).Any(s => s.ServiceType == 8 && s.Active)) { Reject(63); return; }
            if (_shops.Values.Any(s => PrivateStoreCatalog.Collides(s.Position, position))) { Reject(56); return; }
            var shop = new Shop(c, position);
            if (!store.Commerce.TryAcquire(CommerceActivity.Store, shop.Token, id)) { Reject(20); return; }
            if (store.IsTradeDeliveryPending(id)) { store.Commerce.Release(shop.Token, id); Reject(20); return; }
            c.StoreToken = shop.Token; ExitViewer(id); _shops[id] = shop; created = true;
            d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(88)));
            Area(d, shop, GameProtocolEngine.CreatePrivateStoreCreated(c.Uid, position));
            logger.LogInformation("Private store {Store} created by {Owner} at {Town}/{Area} {X},{Y}.", shop.Token, c.Name, position.Town, position.Area, position.X, position.Y);
        }, ct, id);
        if (created) await parties.TryLeavePartySilentlyAsync(id, ct);
        return created;
    }
    public async Task<CharacterRecord?> DisplayAsync(Guid id, Guid session, byte[] title, IReadOnlyList<PrivateStoreDisplayRequest> goods, CancellationToken ct = default)
    {
        CharacterRecord? installed = null;
        await RunAsync(async d =>
        {
            var c = Current(id, session); if (c is null) return;
            if (!_shops.TryGetValue(id, out var shop) || shop.Open) { d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(90, 57))); return; }
            var valid = false;
            try { var text = TextEncoding.GetString(title); valid = title.Length is > 0 and <= 40 && !text.Any(char.IsControl) && !string.IsNullOrWhiteSpace(text); }
            catch (DecoderFallbackException) { }
            if (!valid) { d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(90, 159))); return; }
            var result = await store.DisplayPrivateStoreAsync(id, shop.Token, goods, ct);
            if (result.Status == DungeonRewardLedgerStatus.Unknown) { Kick(c); return; }
            if (result.Status != DungeonRewardLedgerStatus.Committed) { d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(90, result.Error))); return; }
            installed = result.Character!; shop.Open = true; shop.Title = title.ToArray();
            d.Add(new(c, EntranceService.CreateMainInventoryRefresh(installed.Gold, installed.VictoryPoints, installed.Inventory ?? [], items)));
            Area(d, shop, GameProtocolEngine.CreatePrivateStoreDisplayed(c.Uid, shop.Title));
            d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(90)));
            logger.LogInformation("Private store {Store} opened with {Count} listings.", shop.Token, goods.Count);
        }, ct, id); return installed;
    }
    public Task EnterAsync(Guid id, Guid session, uint ownerUid, CancellationToken ct = default) => RunAsync(async d =>
    {
        var c = Current(id, session); if (c is null) return;
        var shop = _shops.Values.FirstOrDefault(s => s.Owner.Uid == ownerUid);
        if (shop is null || !shop.Open || id == shop.Owner.Id || !town.SharesPublicArea(id, shop.Owner.Id)
            || store.Commerce.Is(id, CommerceActivity.Trade) || IsOwner(id))
        { d.Add(new(c, GameProtocolEngine.CreatePrivateStoreEnterFailure())); return; }
        var owner = await store.GetCharacterAsync(shop.Owner.Account, shop.Owner.Id, ct);
        if (owner?.PrivateStoreStock?.StoreId != shop.Token) { d.Add(new(c, GameProtocolEngine.CreatePrivateStoreEnterFailure())); return; }
        ExitViewer(id); _viewing[id] = shop; shop.Viewers.Add(id);
        d.Add(new(c, GameProtocolEngine.CreatePrivateStoreEnterReply(shop.Owner.Uid, Rows(owner))));
    }, ct, id);
    public Task ExitAsync(Guid id, Guid session, CancellationToken ct = default) => RunAsync(d =>
    { if (Current(id, session) is { } c) { ExitViewer(id); d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(87))); } return Task.CompletedTask; }, ct, id);
    public async Task<CharacterRecord?> BuyAsync(Guid id, Guid session, ushort slot, uint addInfo, CancellationToken ct = default)
    {
        CharacterRecord? installed = null;
        await RunAsync(async d =>
        {
            var c = Current(id, session); if (c is null) return;
            void Reject(byte error) => d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(85, error)));
            if (IsOwner(id)) { Reject(60); return; }
            if (!_viewing.TryGetValue(id, out var shop) || !shop.Open || !town.SharesPublicArea(id, shop.Owner.Id)) { Reject(21); return; }
            var purchase = Guid.NewGuid();
            if (!store.Commerce.TryAcquire(CommerceActivity.Purchase, purchase, id)) { Reject(1); return; }
            PrivateStorePurchaseResult result;
            try { result = await store.BuyPrivateStoreAsync(id, shop.Owner.Id, shop.Token, slot, addInfo, ct); }
            finally { store.Commerce.Release(purchase, id); }
            if (result.Status == DungeonRewardLedgerStatus.Unknown) { Kick(c); Kick(shop.Owner); return; }
            if (result.Status != DungeonRewardLedgerStatus.Committed)
            {
                Reject(result.Error);
                if (result.Error == 22) d.Add(new(shop.Owner, GameProtocolEngine.CreatePrivateStoreSaleFailure(22)));
                return;
            }
            installed = result.Buyer!; _goldUpdates[shop.Owner.Id] = 0;
            shop.Owner.Wake?.Invoke();
            foreach (var viewer in shop.Viewers)
                if (_channels.TryGetValue(viewer, out var channel)) d.Add(new(channel, GameProtocolEngine.CreatePrivateStoreGoodsSold(slot, result.SoldQuantity)));
            d.Add(new(shop.Owner, GameProtocolEngine.CreatePrivateStoreSale(result.ProjectedSellerGold, result.Item!.ItemId, slot, result.SoldQuantity,
                BuyerName(c.Name), result.Gross, result.Fee)));
            var wire = CharacterItemWireProjection.Project(result.Item!, items);
            d.Add(new(c, GameProtocolEngine.CreatePrivateStoreBuyReply((uint)installed.Gold, installed.VictoryPoints, result.Cash,
                result.DestinationSlot, result.Item!.ItemId, wire.AddInfo, wire.ItemAttr, wire.Durability)));
            logger.LogInformation("Private store {Store} sold slot {Slot} count {Count}: {Buyer}, gross {Gross}, fee {Fee}.", shop.Token, slot, result.SoldQuantity, c.Name, result.Gross, result.Fee);
        }, ct, id); return installed;
    }
    private async Task<CharacterRecord?> CloseCoreAsync(List<Delivery> d, Channel c, bool force, bool reply, CancellationToken ct)
    {
        var wasViewing = _viewing.ContainsKey(c.Id);
        ExitViewer(c.Id);
        if (!_shops.TryGetValue(c.Id, out var shop))
        {
            if (reply) d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(89, 58)));
            else if (force && wasViewing) d.Add(new(c, GameProtocolEngine.CreatePrivateStoreForceClose()));
            return null;
        }
        var result = await store.ClosePrivateStoreAsync(c.Id, shop.Token, force, ct);
        if (result.Status == DungeonRewardLedgerStatus.Unknown) { RemoveShop(d, shop); Kick(c); return null; }
        if (result.Status != DungeonRewardLedgerStatus.Committed)
        {
            if (force) { RemoveShop(d, shop, release: false); Kick(c); }
            if (reply) d.Add(new(c, GameProtocolEngine.CreatePrivateStoreReply(89, result.Error)));
            return null;
        }
        RemoveShop(d, shop); _goldUpdates.TryRemove(c.Id, out _);
        d.Add(new(c, reply ? GameProtocolEngine.CreatePrivateStoreReply(89) : GameProtocolEngine.CreatePrivateStoreForceClose()));
        return result.Character;
    }
    public async Task<CharacterRecord?> CloseAsync(Guid id, Guid session, bool force = false, bool reply = true, CancellationToken ct = default)
    {
        CharacterRecord? installed = null;
        await RunAsync(async d => { if (Current(id, session) is { } c) installed = await CloseCoreAsync(d, c, force, reply, ct); }, ct, id);
        return installed;
    }
    public Task SendAreaStoresAsync(Guid id, Guid session, CancellationToken ct = default) => RunAsync(d =>
    {
        var c = Current(id, session); if (c is null) return Task.CompletedTask;
        var summaries = _shops.Values.Where(s => s.Owner.Id != id && town.SharesPublicArea(s.Owner.Id, id))
            .Select(s => new PrivateStoreSummary(s.Owner.Uid, s.Title, s.Open, s.Position)).ToArray();
        foreach (var batch in summaries.Chunk(255)) d.Add(new(c, GameProtocolEngine.CreatePrivateStoreList(batch)));
        return Task.CompletedTask;
    }, ct, id);
    internal Task SweepAsync(CancellationToken ct = default) => RunAsync(async d =>
    {
        foreach (var shop in _shops.Values.ToArray())
        {
            var current = await store.GetCharacterAsync(shop.Owner.Account, shop.Owner.Id, ct);
            if (current is not null && store.Commerce.Owns(shop.Owner.Id, CommerceActivity.Store, shop.Token)
                && (!shop.Open || current.PrivateStoreStock?.StoreId == shop.Token)) continue;
            RemoveShop(d, shop, release: false); Kick(shop.Owner);
        }
    }, ct);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await SweepAsync(stoppingToken);
    }
}
