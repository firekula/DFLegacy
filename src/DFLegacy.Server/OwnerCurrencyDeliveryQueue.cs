using System.Buffers.Binary;
using System.Collections.Concurrent;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>Rebind external absolute Gold notices at the owning actor's currency safe point.</summary>
internal sealed class OwnerCurrencyDeliveryQueue(Action wake)
{
    private sealed record Pending(GameServerPacket Packet, int GoldOffset);
    private readonly ConcurrentQueue<Pending> _pending = new();
    private int _retired;
    internal bool HasPending => !_pending.IsEmpty && Volatile.Read(ref _retired) == 0;
    internal void Retire() { Interlocked.Exchange(ref _retired, 1); _pending.Clear(); }

    internal ValueTask SendTradeAsync(GameServerPacket packet, Func<GameServerPacket, ValueTask> send)
    {
        // Only the cancellation refresh: main list, exactly one slot0/item0 Gold entry.
        var p = packet.Payload;
        if (packet.Type == 0 && packet.ProtocolId == 14 && p.Length == 15 && p[0] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(1)) == 1
            && BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(3)) == 0)
            return Enqueue(packet, 7);
        return send(packet);
    }

    internal ValueTask SendPrivateStoreAsync(GameServerPacket packet, Func<GameServerPacket, ValueTask> send) =>
        packet.Type == 0 && packet.ProtocolId == 82 && packet.Payload.Length >= 5 && packet.Payload[0] == 1
            ? Enqueue(packet, 1) : send(packet);

    private ValueTask Enqueue(GameServerPacket packet, int goldOffset)
    {
        if (Volatile.Read(ref _retired) != 0) return ValueTask.CompletedTask;
        _pending.Enqueue(new(new(packet.Type, packet.ProtocolId, packet.Payload.ToArray()), goldOffset));
        wake();
        return ValueTask.CompletedTask;
    }

    internal async Task DrainAsync(JsonGameStore store, string account, Guid owner, SemaphoreSlim currencyGate,
        Func<Task> installPendingTrade, Action<CharacterRecord> installGold,
        Func<GameServerPacket, ValueTask> send, CancellationToken ct)
    {
        if (!HasPending) return;
        await currencyGate.WaitAsync(ct);
        try
        {
            if (!HasPending) return;
            // Freeze the notices before sampling. A purchase arriving during socket delivery belongs
            // to the next owner pass, where its newly awarded store credit can be installed first.
            var batch = new List<Pending>();
            var count = _pending.Count;
            for (var i = 0; i < count && _pending.TryDequeue(out var notice); i++) batch.Add(notice);
            await installPendingTrade();
            // Seller notices include awarded sale income. Install those credits now even if their wake
            // arrived after the actor's earlier store-credit poll. Mentor credit remains for N76's pair.
            var character = await store.ConsumePrivateStoreGoldCreditsAsync(owner, ct)
                ?? await store.GetCharacterAsync(account, owner, ct);
            if (character is null) return;
            installGold(character);
            var gold = checked((uint)Math.Max(0, character.Gold));
            foreach (var pending in batch)
            {
                if (Volatile.Read(ref _retired) != 0) break;
                // Retain all sale/cancel details. The service's earlier sampled absolute is never sent.
                BinaryPrimitives.WriteUInt32LittleEndian(pending.Packet.Payload.AsSpan(pending.GoldOffset), gold);
                await send(pending.Packet);
            }
        }
        finally { currencyGate.Release(); }
    }
}
