using System.Text;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

internal sealed record DungeonDebugHiddenMonster(
    ushort UniqueId, ushort MonsterIndex, byte Level, byte BoxIndex,
    byte State, ushort X, ushort Y);

internal sealed record DungeonDebugOverlaySnapshot(
    long RunId, ushort[] UserIds, ushort DungeonId, ushort CurrentMapId,
    ushort BossMapId, byte RoomX, byte RoomY, byte BossX, byte BossY,
    bool ClearEnabled, bool AtBoss, DungeonDebugHiddenMonster[] Hidden,
    bool DropTestEnabled = false, Dictionary<ushort, DungeonDebugDropState>? DropTestStates = null);

internal static class DungeonDebugOverlayWire
{
    // Local diagnostic files, never gameplay packets. Matches Debug/debug_model.h.
    internal const uint Magic = 0x31474244;
    internal static byte[] Encode(DungeonDebugOverlaySnapshot snapshot, ushort userId, long fileTime)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(2u);
        writer.Write(fileTime);
        writer.Write(snapshot.RunId);
        writer.Write(userId);
        writer.Write(snapshot.DungeonId);
        writer.Write(snapshot.CurrentMapId);
        writer.Write(snapshot.BossMapId);
        writer.Write(snapshot.RoomX);
        writer.Write(snapshot.RoomY);
        writer.Write(snapshot.BossX);
        writer.Write(snapshot.BossY);
        writer.Write(snapshot.ClearEnabled);
        writer.Write(snapshot.AtBoss);
        writer.Write((ushort)snapshot.Hidden.Length);
        foreach (var monster in snapshot.Hidden)
        {
            writer.Write(monster.UniqueId);
            writer.Write(monster.MonsterIndex);
            writer.Write(monster.Level);
            writer.Write(monster.BoxIndex);
            writer.Write(monster.State);
            writer.Write((byte)0);
            writer.Write(monster.X);
            writer.Write(monster.Y);
        }
        var state = snapshot.DropTestStates?.GetValueOrDefault(userId);
        writer.Write(snapshot.DropTestEnabled);
        writer.Write(state?.Allowed ?? false);
        writer.Write((byte)(state?.Result ?? DungeonDebugDropResult.None));
        writer.Write((byte)0);
        writer.Write(state?.RequestId ?? 0);
        return stream.ToArray();
    }
}

public sealed class DungeonDebugOverlayService(
    ServerOptions options, DungeonInstanceRegistry instances,
    ILogger<DungeonDebugOverlayService> logger, JsonGameStore? store = null,
    ItemCatalog? items = null, CharacterStatCatalog? stats = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Debug.ClientOverlay.Enabled) return;
        var directory = Path.GetFullPath(options.Debug.ClientOverlay.SnapshotDirectory, AppContext.BaseDirectory);
        var testControl = new DungeonTestControl(options, instances, store, items, stats);
        var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(directory);
            logger.LogInformation("Client debug overlay enabled: {Directory}; boss encounters are prepared on entry and reused.", directory);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            do
            {
                var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var instance in instances.Instances)
                {
                    var snapshot = instance.CaptureDebugOverlay();
                    if (snapshot is null) continue;
                    foreach (var userId in snapshot.UserIds)
                        await ConsumeDropControlAsync(directory, userId, instance, stoppingToken);
                    snapshot = instance.CaptureDebugOverlay();
                    if (snapshot is null) continue;
                    foreach (var userId in snapshot.UserIds)
                    {
                        var path = Path.Combine(directory, $"dfdebug-{userId}.bin");
                        active.Add(path);
                        var data = DungeonDebugOverlayWire.Encode(snapshot, userId, DateTime.UtcNow.ToFileTimeUtc());
                        try
                        {
                            await File.WriteAllBytesAsync(path + ".tmp", data, stoppingToken);
                            File.Move(path + ".tmp", path, overwrite: true);
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        {
                            // A diagnostic reader may briefly hold the old file.
                            // Keep publishing on the next tick instead of stopping.
                            logger.LogDebug(error, "Debug snapshot temporarily busy: {Path}", path);
                        }
                    }
                }
                try { await testControl.PublishAsync(directory, active, stoppingToken); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    logger.LogDebug(error, "Test control snapshot temporarily unavailable.");
                    active.UnionWith(published.Where(path => Path.GetFileName(path).StartsWith("dfdebug-test-", StringComparison.Ordinal)));
                }
                foreach (var expired in published.Except(active))
                {
                    try { File.Delete(expired); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { active.Add(expired); }
                }
                published = active;
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Diagnostics must never take down the game service.
            logger.LogWarning(error, "Client debug overlay stopped; gameplay remains available.");
        }
        finally
        {
            foreach (var path in published)
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private async Task ConsumeDropControlAsync(string directory, ushort userId, DungeonInstance instance, CancellationToken ct)
    {
        var path = Path.Combine(directory, $"dfdebug-drop-{userId}.bin");
        var claimed = path + ".processing";
        try
        {
            // Claim before reading: cleanup must not delete a newer request atomically published at path.
            if (!File.Exists(path)) return;
            File.Move(path, claimed, overwrite: true);
            var bytes = new byte[DungeonDebugDropRequest.Size + 1];
            int count;
            using (var file = new FileStream(claimed, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                count = 0;
                while (count < bytes.Length)
                {
                    var read = await file.ReadAsync(bytes.AsMemory(count), ct);
                    if (read == 0) break;
                    count += read;
                }
            }
            if (!DungeonDebugDropRequest.TryDecode(bytes.AsSpan(0, count), out var command) || command.UserId != userId) return;
            var result = instance.ApplyDebugDropRequest(command, DateTime.UtcNow.ToFileTimeUtc());
            logger.LogInformation("Debug drop-test {Enabled} requested by user {UserId} for run {RunId}: {Result} ({RequestId}).",
                command.Enabled, userId, command.RunId, result, command.RequestId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(error, "Debug drop-test control temporarily busy: {Path}", path);
        }
        finally
        {
            try { File.Delete(claimed); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
