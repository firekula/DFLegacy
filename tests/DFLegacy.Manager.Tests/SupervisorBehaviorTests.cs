using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DFLegacy.Manager;

internal static class SupervisorBehaviorTests
{
    internal static async Task RunAsync(Action<bool, string> check, string serverDirectory)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy supervisor " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new ManagerPaths(root);
        Directory.CreateDirectory(paths.ServerDirectory);
        foreach (var file in Directory.EnumerateFiles(serverDirectory)) File.Copy(file, Path.Combine(paths.ServerDirectory, Path.GetFileName(file)));
        var pvf = Path.Combine(root, "fixture.pvf");
        WriteFixturePvf(pvf);
        var config = new ConfigurationStore(paths, new Dictionary<string, string?>());
        var ports = new HashSet<int>();
        int Port() { int port; do { port = HttpBehaviorTests.FreePort(); } while (!ports.Add(port)); return port; }
        var settings = config.Read().Saved with
        {
            ScriptPvfPath = pvf, AdminPort = Port(), EntrancePort = Port(), ChannelPort = Port(), AdditionalEntrancePort = Port(),
            CharacterPort = Port(), GameplayPrimaryPort = Port(), GameplaySecondaryPort = Port()
        };
        config.Save(settings);
        var log = new ManagerLog(paths);
        await using var supervisor = new ServerSupervisor(paths, config, log);
        try
        {
            await supervisor.InitializeAdminAsync(new("firstadmin", "A game password 123!", "12345678"));
            check(File.Exists(paths.GameData), "offline game admin initialization writes only selected game storage");
            var before = File.ReadAllText(paths.GameData);
            try { await supervisor.InitializeAdminAsync(new("firstadmin", "changed password 123!", "12345678")); check(false, "existing game administrator is not silently replaced"); }
            catch (InvalidOperationException) { check(File.ReadAllText(paths.GameData) == before, "duplicate bootstrap preserves existing password and saved data"); }
            var started = await supervisor.StartAsync();
            check(started.State == "running" && started.Pid is not null, "real game process reaches owned authenticated readiness");
            var repeated = await supervisor.StartAsync();
            check(repeated.Pid == started.Pid, "repeated start cannot create a second writer");
            using var client = new HttpClient();
            check((await client.GetAsync($"http://127.0.0.1:{settings.AdminPort}/health")).IsSuccessStatusCode, "game HTTP port responds after actual startup");
            var stopped = await supervisor.StopAsync();
            check(stopped.State == "stopped" && stopped.Pid is null, "soft stop waits for owned process to finish");
            await supervisor.StartAsync();
            var restarted = await supervisor.RestartAsync();
            check(restarted.State == "running", "restart re-establishes fresh private readiness");
            using (var process = Process.GetProcessById(restarted.Pid!.Value)) process.Kill(entireProcessTree: true);
            for (var attempt = 0; attempt < 50 && supervisor.Snapshot.State == "running"; attempt++) await Task.Delay(100);
            check(supervisor.Snapshot.State == "exited", "unexpected game process exit is visible while manager remains alive");
            check(log.Read().Any(line => line.Source == "server"), "game output is captured for troubleshooting");
            await supervisor.StopAsync();
            var document = config.ReadServerDocument();
            document["DFLegacy"]!["Processes"] = JsonSerializer.SerializeToNode(new { Channel = new { Enabled = true }, Game = new { EntranceBinding = false } });
            File.WriteAllText(paths.ServerConfig, document.ToJsonString());
            try { await supervisor.StartAsync(); check(false, "split topology does not partially start from manager"); }
            catch (InvalidOperationException) { check(supervisor.Snapshot.Pid is null, "unsupported split topology is rejected before starting a child"); }
        }
        finally
        {
            await supervisor.StopAsync();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    internal static void WriteFixturePvf(string path)
    {
        // Public synthetic text table, no game assets. Matches the documented PVF reader wire format.
        var name = Encoding.ASCII.GetBytes("creature/exptable.tbl");
        var payload = Encoding.ASCII.GetBytes("10\n100\n");
        var tree = new byte[(20 + name.Length + 3) & ~3];
        BinaryPrimitives.WriteUInt32LittleEndian(tree, 1);
        BinaryPrimitives.WriteInt32LittleEndian(tree.AsSpan(8), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(tree.AsSpan(16), name.Length);
        name.CopyTo(tree, 20);
        var body = new byte[(payload.Length + 3) & ~3]; payload.CopyTo(body, 0);
        static void Encrypt(byte[] bytes)
        {
            for (var offset = 0; offset < bytes.Length; offset += 4)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), BitOperations.RotateLeft(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)), 6));
        }
        Encrypt(tree); Encrypt(body);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("DNF_SCRIPT_PACK")); writer.Write(tree.Length); writer.Write(0u); writer.Write(1u); writer.Write(tree); writer.Write(body);
    }
}
