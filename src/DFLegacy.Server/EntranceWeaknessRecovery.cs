using System.Collections.Concurrent;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

/// <summary>
/// The minute callback is a wake-up hint, not an authoritative value: its
/// notification can arrive after a newer recovery purchase or dungeon wipe.
/// Only the session loop reads and installs the current persisted recovery.
/// </summary>
internal sealed class EntranceWeaknessRecovery(EntranceSessionWakeSignal wakeSignal)
{
    private readonly ConcurrentQueue<Guid> _pendingCharacters = new();
    private readonly object _entryGate = new();
    private long _entryGeneration;
    private long _startedGeneration;

    internal void Notify(Guid characterId)
    {
        _pendingCharacters.Enqueue(characterId);
        wakeSignal.Pulse();
    }

    internal async Task<CharacterRecord?> TakeLatestAsync(
        Guid? currentCharacterId,
        bool isDungeonActive,
        Func<Guid, CancellationToken, Task<CharacterRecord?>> findCharacter,
        CancellationToken cancellationToken)
    {
        // Entry details and their owner's HP are one frozen projection until
        // this recipient has actually received NOTI 30. Do not even install
        // newer fields while the client can still be in pure selection.
        if (!CanProcessPending(isDungeonActive))
        {
            return null;
        }

        var hasCurrentNotification = false;
        while (_pendingCharacters.TryDequeue(out var characterId))
        {
            hasCurrentNotification |= characterId == currentCharacterId;
        }

        if (!hasCurrentNotification || currentCharacterId is not { } currentId)
        {
            return null;
        }

        var latest = await findCharacter(currentId, cancellationToken);
        return latest?.Id == currentId ? latest : null;
    }

    internal bool CanProcessPending(bool isDungeonActive)
    {
        lock (_entryGate)
        {
            return !_pendingCharacters.IsEmpty
                && (!isDungeonActive || _startedGeneration == _entryGeneration);
        }
    }

    internal Func<GameServerPacket, ValueTask> BeginDungeonDelivery(
        Func<GameServerPacket, ValueTask> send)
    {
        long generation;
        lock (_entryGate)
        {
            generation = ++_entryGeneration;
        }
        return async packet =>
        {
            await send(packet);
            if (packet.Type == GameProtocolEngine.NotificationPacketType
                && packet.ProtocolId == GameProtocolEngine.FinishLoadingNotification)
            {
                // Complete only after the per-session write/flush succeeds.
                // A delegate retained by an old run cannot release a new one.
                // The check and assignment share Begin's gate so a delayed old
                // completion cannot overwrite a newer completed generation.
                lock (_entryGate)
                {
                    if (generation != _entryGeneration)
                    {
                        return;
                    }
                    _startedGeneration = generation;
                }
                wakeSignal.Pulse();
            }
        };
    }

    internal static CharacterRecord WithRecoveryFrom(
        CharacterRecord target,
        CharacterRecord recoverySource) =>
        target.Id == recoverySource.Id
            ? target with
            {
                WeaknessRecovery = recoverySource.WeaknessRecovery,
                LegacyIsWeakened = recoverySource.LegacyIsWeakened
            }
            : target;
}
