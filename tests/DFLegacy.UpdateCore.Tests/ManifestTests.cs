using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.UpdateCore.Tests;

internal static class ManifestTests
{
    internal static readonly string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    internal static ClientManifest Sample() => new(4, "v4.0.1-test", new string('a', 40), "x86", "test", "gitee-test", 2, 0,
        [Entry("ImagePacks2/a.npk", "abc"), Entry("Script.pvf", "pvf"), Entry("DFLegacy.Client.dll", "dll"), Entry("nested/empty.dat", "")], [], []);
    internal static ManagedFile Entry(string path, string content)
    {
        var bytes = TestRun.Bytes(content); var hash = TestRun.Hash(bytes);
        return new(path, bytes.Length, hash, [new(hash, bytes.Length, "v4-test", "sha256-" + hash + ".bin")]);
    }
    internal static async Task Run(string root)
    {
        var m = Sample(); var parsed = ManifestCodec.Parse(ManifestCodec.Serialize(m));
        TestRun.Check(parsed.Files[3].Size == 0 && parsed.Files[3].Sha256 == EmptyHash, "zero-byte managed file supported");
        TestRun.Check(parsed.Architecture == "x86" && parsed.Flavor == "test", "channel identity retained");
        void Bad(Action<JsonObject> mutate, string reason)
        {
            var obj = JsonNode.Parse(ManifestCodec.Serialize(m))!.AsObject(); mutate(obj);
            TestRun.Reject(() => ManifestCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(obj)), reason);
        }
        Bad(o => o["schemaVersion"] = 3, "unknown protocol");
        Bad(o => o["minimumSequence"] = 3, "impossible baseline range");
        Bad(o => o["architecture"] = "arm64", "unsupported architecture");
        Bad(o => o["files"]!.AsArray().Add(o["files"]![0]!.DeepClone()), "duplicate path");
        Bad(o => o["files"]![1]!["path"] = "imagepacks2/A.NPK", "case collision");
        Bad(o => o["files"]![1]!["path"] = "ImagePacks2", "file-as-parent collision");
        Bad(o => o["files"]![0]!["sha256"] = "bad", "bad hash");
        Bad(o => o["files"]![0]!["parts"]![0]!["size"] = 4, "part totals");
        Bad(o => o["files"]![0]!["parts"]![0]!["release"] = "https://evil.test/x", "arbitrary resource origin");
        Bad(o => o["files"]![0]!["size"] = long.MaxValue, "oversized logical file");
        foreach (var path in new[] { "../out", "a/../b", "/absolute", "C:/a", "x:y", "a\\b", "con", "a/NUL.txt", "a/com¹.dll", "a.", "a /x", "a//x", "a\u0001x", "launcher.JSON", "Config.ini", ".dflegacy/a", "Logs/a", "Screenshot/a", "client-package.json", "ImagePacks2/aux.npk" })
            Bad(o => o["files"]![0]!["path"] = path, "unsafe path " + path);
        var raw = System.Text.Encoding.UTF8.GetString(ManifestCodec.Serialize(m));
        foreach(var alias in new[]{"LAUNCH~1.JSO","IMAGEP~1/test.npk"})
            Bad(o=>o["files"]![0]!["path"]=alias,"Windows short-path alias "+alias);
        TestRun.Reject(() => ManifestCodec.Parse(TestRun.Bytes(raw.Replace("\"schemaVersion\":4", "\"schemaVersion\":4,\"schemaVersion\":4"))), "duplicate JSON key");
        Bad(o => o["extra"] = true, "unknown JSON key");
        var retirement = m with { Retired = [new("old/patch.dll", "delete", 1), new("custom.dat", "unmanage", 1)] };
        TestRun.Check(ManifestCodec.Parse(ManifestCodec.Serialize(retirement)).Retired.Length == 2, "explicit retirement actions");
        TestRun.Reject(() => ManifestCodec.Parse(ManifestCodec.Serialize(m with { Retired = [new("Logs/player.txt", "delete", 1)] })), "protected retirement");
        foreach (var (file, content) in m.Files.Zip(new[] { "abc", "pvf", "dll", "" }))
        {
            var path = PathPolicy.Resolve(root, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        }
        TestRun.Check((await FileVerifier.VerifyAsync(root, m)).IsValid, "nested resources fully validate");
        File.WriteAllText(Path.Combine(root, "ImagePacks2/a.npk"), "xyz");
        File.Delete(Path.Combine(root, "nested/empty.dat"));
        var result = await FileVerifier.VerifyAsync(root, m);
        TestRun.Check(result.Problems.Count == 2 && result.Problems.Any(p => p.Path == "ImagePacks2/a.npk" && p.ActualSha256 == "3608bca1e44ea6c4d268eb6db02260269892c0b42b86bbf1e77a6fa16c3c9282"), "same-length corruption and missing empty file detected");
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (IOException ex) when (OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 1314)
        {
            // Junctions exercise the same reparse-point policy without developer-mode privileges.
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.Arguments = $"/d /c mklink /J \"{link}\" \"{outside}\"";
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            TestRun.Check(process.ExitCode == 0, "junction fixture created");
        }
        TestRun.Reject(() => PathPolicy.Resolve(root, "linked/file.bin"), "directory link");
        Directory.Delete(link);
    }
}
