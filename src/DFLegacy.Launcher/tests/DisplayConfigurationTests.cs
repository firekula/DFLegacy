using System.Runtime.InteropServices;
using System.Text;
using DFLegacy.Launcher;

internal static class DisplayConfigurationTests
{
    internal static void Run(string parent, Action<bool, string> check)
    {
        TargetAuthority(parent, check);
        var root = Path.Combine(parent, "display");
        Directory.CreateDirectory(root);
        var client = Path.Combine(root, "DNF.exe");
        File.WriteAllBytes(client, []);
        var path = Path.Combine(root, "Config.ini");
        const string original = "; keep 中文\r\n[Resolution]\r\nWidth=800\r\nHeight=600\r\n; keep resolution\r\n[Rendering]\r\nScaleFilter=point\r\nPresentationFilter=legacy\r\n[Network]\r\nClientServiceUrl=https://example.test/path\r\n[Custom]\r\nValue=unchanged\r\n";
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode })
        {
            var before = encoding.GetPreamble().Concat(encoding.GetBytes(original)).ToArray();
            File.WriteAllBytes(path, before);
            var snapshot = ClientDisplayConfiguration.Load(client);
            check(snapshot.Width == 800 && snapshot.Height == 600 && snapshot.PresentationFilter == "legacy", "display reads actual legacy settings");
            ClientDisplayConfiguration.Save(snapshot, 1024, 768, "sharp");
            var expected = original.Replace("Width=800", "Width=1024").Replace("Height=600", "Height=768").Replace("PresentationFilter=legacy", "PresentationFilter=sharp");
            check(File.ReadAllBytes(path).SequenceEqual(encoding.GetPreamble().Concat(encoding.GetBytes(expected))), "display edits preserve all unrelated bytes and encoding " + encoding.WebName);
            check(Read(path, "Resolution", "Width") == "1024" && Read(path, "Resolution", "Height") == "768" && Read(path, "Rendering", "PresentationFilter") == "sharp", "native INI reader sees managed display edits " + encoding.WebName);
        }

        // Literal legacy ANSI bytes must survive; decoding them as UTF8 would destroy them.
        File.WriteAllText(path, "[Rendering]\nPresentationFilter=\u00a0sharp\u00a0\n", Encoding.Unicode);
        check(ClientDisplayConfiguration.Load(client).PresentationFilter == "legacy", "non-ASCII padding cannot silently promote a mode the native ANSI reader rejects");
        foreach (var commented in new[] { "[Resolution] ; resolution comment", "[Resolution] trailing text", "[ Resolution ]", "[Resolution" })
        {
            var headers = "[Features]\r\nKeep=1\r\n" + commented + "\r\nWidth=777\r\nHeight=555\r\n[Rendering] ; rendering comment\r\nPresentationFilter=legacy\r\n[Custom] ; unrelated section\r\nWidth=7\r\nHeight=9\r\nPresentationFilter=keep\r\n";
            File.WriteAllText(path, headers, Encoding.Unicode);
            ClientDisplayConfiguration.Save(ClientDisplayConfiguration.Load(client), 800, 600, "sharp");
            var edited = File.ReadAllText(path, Encoding.Unicode);
            check(edited.EndsWith("[Custom] ; unrelated section\r\nWidth=7\r\nHeight=9\r\nPresentationFilter=keep\r\n", StringComparison.Ordinal), "commented custom section remains byte-for-byte outside managed sections");
            check(Read(path, "Resolution", "Width") == "800" && Read(path, "Resolution", "Height") == "600"
                && Read(path, "Rendering", "PresentationFilter") == "sharp" && Read(path, "Custom", "Width") == "7", "native reader sees edits in commented managed headers without shadow sections");
        }
        var ansi = Encoding.ASCII.GetBytes("; ").Concat(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }).Concat(Encoding.ASCII.GetBytes("\n[Resolution]\nWidth=800\nHeight=600\n[Rendering]\nScaleFilter=point\n")).ToArray();
        File.WriteAllBytes(path, ansi);
        ClientDisplayConfiguration.Save(ClientDisplayConfiguration.Load(client), 800, 600, "sharp");
        check(File.ReadAllBytes(path).AsSpan(0, 8).SequenceEqual(ansi.AsSpan(0, 8)), "display retains ANSI comment bytes");
        check(Read(path, "Rendering", "PresentationFilter") == "sharp", "display inserts missing mode section key");

        File.WriteAllText(path, "[Resolution]\nWidth=800\nHeight=600\n[Resolution]\nWidth=640\nHeight=480\n[Rendering]\nPresentationFilter=point\nPresentationFilter=linear\n[Rendering]\nPresentationFilter=legacy\n", Encoding.ASCII);
        ClientDisplayConfiguration.Save(ClientDisplayConfiguration.Load(client), 1280, 960, "sharp");
        check(Read(path, "Resolution", "Width") == "1280" && Read(path, "Resolution", "Height") == "960" && Read(path, "Rendering", "PresentationFilter") == "sharp", "duplicate keys cannot shadow saved display values");
        var reread = ClientDisplayConfiguration.Load(client);
        check(reread.Width == 1280 && reread.Height == 960 && reread.PresentationFilter == "sharp", "managed/native duplicate-section results agree");

        var stamp = File.GetLastWriteTimeUtc(path);
        ClientDisplayConfiguration.Save(reread, 1280, 960, "sharp");
        check(File.GetLastWriteTimeUtc(path) == stamp, "display no-op does not rewrite the file");

        var stale = ClientDisplayConfiguration.Load(client);
        File.AppendAllText(path, "; external edit\n");
        var changed = File.ReadAllBytes(path);
        ExpectFailure<IOException>(() => ClientDisplayConfiguration.Save(stale, 800, 600, "sharp"), check, "stale display snapshot rejected");
        check(File.ReadAllBytes(path).SequenceEqual(changed), "stale save preserves external edits");
        var lockedSnapshot = ClientDisplayConfiguration.Load(client);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ExpectFailure<IOException>(() => ClientDisplayConfiguration.Save(lockedSnapshot, 800, 600, "sharp"), check, "locked display file rejected");
        check(File.ReadAllBytes(path).SequenceEqual(changed), "locked save preserves old bytes");
        var invalid = ClientDisplayConfiguration.Load(client);
        ExpectFailure<InvalidDataException>(() => ClientDisplayConfiguration.Save(invalid, 800, 480, "sharp"), check, "enhanced mode rejects stretched geometry");
        ExpectFailure<InvalidDataException>(() => ClientDisplayConfiguration.Save(invalid, 800, 600, "unknown"), check, "display rejects unknown requested mode");
        check(File.ReadAllBytes(path).SequenceEqual(changed), "invalid display input leaves file untouched");

        File.WriteAllText(path, "[Resolution]\nWidth=1234\nHeight=700\n[Rendering]\nScaleFilter=point\n", Encoding.ASCII);
        var custom = ClientDisplayConfiguration.Load(client);
        check(custom.Width == 1234 && custom.Height == 700 && custom.PresentationFilter == "legacy", "existing custom size and missing mode stay legacy");
        ExpectFailure<InvalidDataException>(() => ClientDisplayConfiguration.Save(custom, 1234, 700, "sharp"), check, "old custom geometry is not silently enhanced");

        File.WriteAllBytes(path, Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes(original)).ToArray());
        ExpectFailure<InvalidDataException>(() => ClientDisplayConfiguration.Load(client), check, "unsupported native UTF32 config rejected");
        File.WriteAllBytes(path, Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(original)).ToArray());
        ExpectFailure<InvalidDataException>(() => ClientDisplayConfiguration.Load(client), check, "unsupported native big-endian UTF16 config rejected");
        var bomHeader = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("[Resolution]\nWidth=960\nHeight=720\n")).ToArray();
        File.WriteAllBytes(path, bomHeader);
        ExpectFailure<InvalidDataException>(() => ClientDisplayConfiguration.Load(client), check, "UTF8 BOM hiding first native section is rejected");
        check(File.ReadAllBytes(path).SequenceEqual(bomHeader), "unsupported native encoding is not rewritten");

        File.Delete(path);
        var missing = ClientDisplayConfiguration.Load(client);
        check(!File.Exists(path) && missing.Width == 800 && missing.Height == 600 && missing.PresentationFilter == "sharp", "opening missing display config uses defaults without writing");
        ClientDisplayConfiguration.Save(missing, 800, 600, "sharp");
        check(File.Exists(path) && Read(path, "Rendering", "PresentationFilter") == "sharp" && File.ReadAllText(path).Contains("[Features]"), "first explicit display save creates complete embedded defaults");
        check(ClientDisplayConfiguration.SameClientPath(client, Path.Combine(root, ".", "dnf.EXE")), "equivalent client spelling remains the same target");
        check(!ClientDisplayConfiguration.SameClientPath(client, Path.Combine(root, "other", "DNF.exe")), "unsaved different client path invalidates the display target");
        ExpectFailure<FileNotFoundException>(() => ClientDisplayConfiguration.Load(Path.Combine(root, "not-installed.exe")), check, "display cannot target a missing installation");
        Console.WriteLine("Display configuration behavior checks executed");
    }

    private static void TargetAuthority(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "target-authority");
        var a = Path.Combine(root, "a", "DNF.exe"); var b = Path.Combine(root, "b", "DNF.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(a)!); Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllBytes(a, []); File.WriteAllBytes(b, []);
        var iniA = Path.Combine(Path.GetDirectoryName(a)!, "Config.ini"); var iniB = Path.Combine(Path.GetDirectoryName(b)!, "Config.ini");
        const string original = "[Resolution]\nWidth=800\nHeight=600\n[Rendering]\nPresentationFilter=legacy\n";
        File.WriteAllText(iniA, original); File.WriteAllText(iniB, original);
        var storePath = Path.Combine(root, "launcher.json"); var store = new LauncherAccountStore(storePath);
        store.Save(new LauncherOptions { ClientPath = a }); var snapshot = ClientDisplayConfiguration.Load(a);
        void Refuse(string cached, string edited, string reason)
        {
            var rejected = false;
            try { ClientDisplayConfiguration.SaveWithAuthority(store, snapshot, cached, edited, 1280, 960, "sharp"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(iniA) == original && File.ReadAllText(iniB) == original, reason);
        }
        store.Save(new LauncherOptions { ClientPath = b });
        Refuse(a, a, "external persisted client path switch cannot save the old displayed installation");
        store.Save(new LauncherOptions { ClientPath = a });
        Refuse(b, b, "snapshot path must agree with edited and cached client paths");
        using (File.Open(storePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var saveFailed = false; var loadFailed = false;
            try { store.Save(new LauncherOptions { ClientPath = b }); } catch (IOException) { saveFailed = true; }
            try { store.Load(); } catch (IOException) { loadFailed = true; }
            check(saveFailed && loadFailed, "connection save and recovery load can both fail under a real exclusive lock");
            Refuse(b, b, "unreadable saved-path authority cannot save an old display snapshot");
        }
        Refuse(b, b, "failed connection save/load state cannot re-enable an old snapshot for a new cached path");
        ClientDisplayConfiguration.SaveWithAuthority(store, snapshot, a, a, 800, 600, "sharp");
        check(Read(iniA, "Rendering", "PresentationFilter") == "sharp" && File.ReadAllText(iniB) == original, "verified persisted/edited/snapshot target can save only installation A");
        using (store.AcquireReadLease())
        {
            var blocked = false;try { store.Save(new LauncherOptions { ClientPath = b }); } catch (IOException) { blocked = true; }
            check(blocked, "saved client authority lease prevents a concurrent connection overwrite");
        }
        File.WriteAllText(storePath, File.ReadAllText(storePath), Encoding.Unicode);
        var wideStore = File.ReadAllBytes(storePath); var wideAccepted = false;
        try
        {
            ClientDisplayConfiguration.SaveWithAuthority(store, ClientDisplayConfiguration.Load(a), a, a, 1024, 768, "sharp");
            wideAccepted = true;
        }
        catch (System.Text.Json.JsonException) { }
        check(wideAccepted && File.ReadAllBytes(storePath).SequenceEqual(wideStore), "authority lease preserves existing UTF16 launcher JSON compatibility without rewriting it");
    }

    private static void ExpectFailure<T>(Action action, Action<bool, string> check, string name) where T : Exception
    {
        try { action(); check(false, name); }
        catch (T) { }
    }

    private static string Read(string path, string section, string key)
    {
        var result = new StringBuilder(256);
        GetPrivateProfileString(section, key, "", result, result.Capacity, path);
        return result.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder result, int size, string path);
}
