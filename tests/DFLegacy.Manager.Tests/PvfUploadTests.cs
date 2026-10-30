using System.Text;
using System.Buffers.Binary;
using System.Numerics;
using DFLegacy.Manager;

internal static class PvfUploadTests
{
    internal static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-pvf-upload-" + Guid.NewGuid().ToString("N"));
        var paths = new ManagerPaths(root);
        var failures = 0;
        void Check(bool value, string name) { if (!value) { failures++; Console.Error.WriteLine("FAIL: " + name); } }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); Check(false, name); }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException or OperationCanceledException) { Check(true, name); }
        }
        try
        {
            var fixture = Path.Combine(root, "fixture.pvf"); SupervisorBehaviorTests.WriteFixturePvf(fixture);
            var valid = File.ReadAllBytes(fixture);
            var config = new ConfigurationStore(paths, new Dictionary<string, string?>());
            await using var supervisor = new ServerSupervisor(paths, config, new ManagerLog(paths));
            var initial = supervisor.GetPvfStatus(); Check(!initial.Available, "fresh manager reports missing resource");
            var status = await supervisor.UploadPvfAsync(new MemoryStream(valid));
            Check(status.Available && status.SizeBytes == valid.Length && File.ReadAllBytes(status.Path).SequenceEqual(valid), "streamed PVF persists and is automatically selected");
            Check(config.Read().Saved.ScriptPvfPath == status.Path && !config.Read().Configured, "upload selects the resource without silently confirming other settings");
            Check(supervisor.Snapshot.State == "not_configured", "resource upload alone preserves the unconfigured lifecycle state");
            File.WriteAllText(status.Path, "corrupted previously stored resource");
            await supervisor.UploadPvfAsync(new MemoryStream(valid));
            Check(File.ReadAllBytes(status.Path).SequenceEqual(valid), "upload repairs a damaged resource with the same content address");
            var before = File.ReadAllText(paths.ServerConfig);
            foreach (var bad in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("not a PVF"), valid[..20] })
                await Reject(() => supervisor.UploadPvfAsync(new MemoryStream(bad)), "malformed PVF is rejected");
            var malformedTree = new byte["DNF_SCRIPT_PACK"u8.Length + 12 + 24];
            "DNF_SCRIPT_PACK"u8.CopyTo(malformedTree);
            BinaryPrimitives.WriteInt32LittleEndian(malformedTree.AsSpan(15), 24);
            BinaryPrimitives.WriteUInt32LittleEndian(malformedTree.AsSpan(23), 1);
            await Reject(() => supervisor.UploadPvfAsync(new MemoryStream(malformedTree)), "valid-looking header with an invalid decrypted tree cannot replace selected PVF");
            await Reject(() => supervisor.UploadPvfAsync(new MemoryStream(valid[..^4])), "valid index referencing a truncated payload cannot replace selected PVF");
            var keyedTree = BuildLargeKeyedIndex();
            var keyed = await new PvfResourceStore(paths).StoreAsync(new MemoryStream(keyedTree));
            Check(keyed.SizeBytes == keyedTree.Length, "nonzero PVF key and index records crossing the streaming buffer are accepted");
            if (keyed.Created) File.Delete(keyed.Path);
            using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); await Reject(() => supervisor.UploadPvfAsync(new MemoryStream(valid), cancel.Token), "cancelled upload is rejected"); }
            await Reject(() => supervisor.UploadPvfAsync(new HookStream(valid, failAfterRead: true)), "interrupted upload is rejected");
            Check(File.ReadAllText(paths.ServerConfig) == before && File.ReadAllBytes(status.Path).SequenceEqual(valid), "rejected uploads preserve the previous config and PVF");
            Check(!Directory.GetFiles(Path.GetDirectoryName(status.Path)!, "*.tmp").Any(), "failed uploads leave no partial files");
            var store = new PvfResourceStore(paths, 256);
            await Reject(() => store.StoreAsync(new MemoryStream(new byte[257])), "streaming size limit works without trusting Content-Length");
            var backupDirectory = paths.Backups + ".saved";
            Directory.Move(paths.Backups, backupDirectory); File.WriteAllText(paths.Backups, "blocked backup directory");
            try
            {
                var different = valid.Concat(new byte[] { 0, 0, 0, 0 }).ToArray();
                await Reject(() => supervisor.UploadPvfAsync(new MemoryStream(different)), "configuration commit failure rejects resource change");
                Check(File.ReadAllText(paths.ServerConfig) == before && File.Exists(status.Path), "configuration commit failure preserves old resource and configuration");
                Check(Directory.GetFiles(Path.GetDirectoryName(status.Path)!, "*.pvf").Length == 1, "configuration failure removes the unreferenced candidate");
            }
            finally { File.Delete(paths.Backups); Directory.Move(backupDirectory, paths.Backups); }
            var controlled = new ConfigurationStore(paths, new Dictionary<string, string?> { ["DFLegacy__ScriptPvfPath"] = fixture });
            await using (var mounted = new ServerSupervisor(paths, controlled, new ManagerLog(paths)))
            {
                Check(mounted.GetPvfStatus().ExternallyControlled, "mounted PVF reports environment ownership");
                await Reject(() => mounted.UploadPvfAsync(new MemoryStream(valid)), "environment controlled PVF cannot be overwritten");
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = supervisor.UploadPvfAsync(new PausedStream(valid, entered, release));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                await Reject(() => supervisor.UploadPvfAsync(new MemoryStream(valid)), "concurrent upload is rejected");
                using var cancelledStart = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Reject(() => supervisor.StartAsync(cancelledStart.Token), "startup cannot pass an in-flight upload");
            }
            finally { release.TrySetResult(); }
            await held;
            await using var reopened = new ServerSupervisor(paths, new ConfigurationStore(paths, new Dictionary<string, string?>()), new ManagerLog(paths));
            Check(reopened.GetPvfStatus().Available && reopened.GetPvfStatus().Path == status.Path, "selected uploaded PVF survives restart");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine($"PVF upload: {failures} failures"); return failures == 0 ? 0 : 1;
    }

    private sealed class HookStream(byte[] bytes, bool failAfterRead) : MemoryStream(bytes)
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read && failAfterRead) throw new IOException("interrupted fixture");
            _read = true; return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private static byte[] BuildLargeKeyedIndex()
    {
        const uint key = 0xa1b2c3d4;
        using var tree = new MemoryStream();
        using (var writer = new BinaryWriter(tree, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var path in new[] { new byte[65535], new byte[] { (byte)'b' } })
            {
                Array.Fill(path, (byte)'a');
                writer.Write(1u); writer.Write(0u); writer.Write(1); writer.Write(0u); writer.Write(path.Length); writer.Write(path);
            }
            while (tree.Length % 4 != 0) writer.Write((byte)0);
        }
        var encrypted = tree.ToArray();
        for (var offset = 0; offset < encrypted.Length; offset += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(encrypted.AsSpan(offset, 4), BitOperations.RotateLeft(BinaryPrimitives.ReadUInt32LittleEndian(encrypted.AsSpan(offset, 4)), 6) ^ key);
        using var archive = new MemoryStream();
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        { writer.Write("DNF_SCRIPT_PACK"u8); writer.Write(encrypted.Length); writer.Write(key); writer.Write(2u); writer.Write(encrypted); writer.Write(0u); }
        return archive.ToArray();
    }

    private sealed class PausedStream(byte[] bytes, TaskCompletionSource entered, TaskCompletionSource release) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { entered.TrySetResult(); await release.Task.WaitAsync(cancellationToken); return await base.ReadAsync(buffer, cancellationToken); }
    }
}
