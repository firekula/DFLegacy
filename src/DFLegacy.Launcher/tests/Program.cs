using DFLegacy.Launcher;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Buffers.Binary;

if (args.Length == 2 && args[0] == "--startup-test-child")
{
    if (args[1] == "wait") { await Task.Delay(30_000); return 0; }
    return int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
}

if (await UpdateHelperTests.Dispatch(args) is int helperExit) return helperExit;
if (await UpdateRecoveryTests.Dispatch(args) is int recoveryExit) return recoveryExit;
if (await BootstrapManagedTests.Dispatch(args) is int bootstrapExit) return bootstrapExit;
var failures = 0;
var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (condition) return;
    Console.Error.WriteLine("FAIL: " + message);
    failures++;
}

var root = Path.Combine(Path.GetTempPath(), "dflegacy-launcher-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    if(args.Contains("--update-public-smoke")) { await UpdatePublicSmokeTests.Run(root); return 0; }
    if(args.Contains("--compact-update-tests")) { UpdateStartupFlowTests.RunOnSta(Check); Console.WriteLine($"Compact update tests: {checks} assertions, {failures} failures"); return failures==0?0:1; }
    if(args.Contains("--launch-ui-tests")) { ClassicUiTests.Run(Check,launchOnly:true); Console.WriteLine($"Launch UI tests: {checks} assertions, {failures} failures"); return failures==0?0:1; }
    if(args.Contains("--bootstrap-first-only")) { await BootstrapManagedTests.Run(root,Check,initialOnly:true); return failures==0?0:1; }
    if(args.Contains("--update-v2-tests")) { await UpdateV2Tests.Run(root,Check); Console.WriteLine($"Update V2 tests: {checks} assertions, {failures} failures"); return failures==0?0:1; }
    if(args.Contains("--update-helper-tests")) { await UpdateHelperTests.Run(root,Check); Console.WriteLine($"Update helper tests: {checks} assertions, {failures} failures"); return failures==0?0:1; }
    if(args.Contains("--update-recovery-tests")) { await UpdateRecoveryTests.Run(root,Check); Console.WriteLine($"Update recovery tests: {checks} assertions, {failures} failures"); return failures==0?0:1; }
    if(args.Length==0 || args.Contains("--standalone-tests")) await StandaloneTests.Run(root, Check);
    if (args.Contains("--standalone-tests")) { Console.WriteLine($"Standalone tests: {failures} failures"); return failures == 0 ? 0 : 1; }
    if (args.Contains("--login-input-tests"))
    {
        ClassicUiTests.Run(Check, loginOnly: true);
        Console.WriteLine(failures == 0 ? "Login input tests passed" : $"Login input tests failed: {failures}");
        return failures == 0 ? 0 : 1;
    }
    if (args.Contains("--server-selection-tests"))
    {
        ClassicUiTests.Run(Check);
        LocalServerSelectionTests.Run(root, Check);
        Console.WriteLine(failures == 0 ? "Server selection tests passed" : $"Server selection tests failed: {failures}");
        return failures == 0 ? 0 : 1;
    }
    await UpdateAcquisitionTests.Run(root, Check);
    if (args.Contains("--update-acquisition-tests")) return failures == 0 ? 0 : 1;
    await UpdateSessionVersionTests.Run(root, Check);
    await UpdateControllerTests.Run(root, Check);
    await UpdateArtifactTests.Run(root, Check);
    await UpdateStartupTests.Run(root, Check);
    if (args.Contains("--update-ui-tests")) { ClassicUiTests.Run(Check); return failures == 0 ? 0 : 1; }
    await UpdateTransportTests.Run(root, Check);
    await UpdateV2Tests.Run(root, Check);
    await UpdateRecoveryTests.Run(root, Check);
    await UpdateInstallerTests.Run(root, Check);
    await UpdateHelperTests.Run(root, Check);
    await UpdateBoundaryTests.Run(root, Check);
    if (args.Contains("--update-tests")) { Console.WriteLine($"Update tests: {checks} assertions, {failures} failures"); return failures == 0 ? 0 : 1; }
    DisplayConfigurationTests.Run(root, Check);
    RenderDiagnosticTests.Run(root, Check);
    ClassicUiTests.Run(Check);
    LocalServerSelectionTests.Run(root, Check);
    byte[] BootstrapFixture(bool packed)
    {
        var image = new byte[0x1000];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), value);
        U16(0, 0x5a4d); U32(60, 0x80); U32(0x80, 0x4550);
        U16(0x84, 0x14c); U16(0x86, 3); U16(0x94, 0xe0); U16(0x96, 0x102);
        var optional = 0x98; U16(optional, 0x10b);
        U32(optional + 16, packed ? 0xa8d17bU : 0x51e6fdU);
        U32(optional + 28, 0x400000); U32(optional + 32, 0x1000);
        U32(optional + 36, 0x200); U32(optional + 56, 0xb39000); U32(optional + 60, 0x400);
        U32(optional + 92, 16);
        void Section(int index, string name, uint size, uint address, uint rawSize, uint raw)
        {
            var s = optional + 0xe0 + 40 * index;
            Encoding.ASCII.GetBytes(name).CopyTo(image, s);
            U32(s + 8, size); U32(s + 12, address); U32(s + 16, rawSize); U32(s + 20, raw);
            U32(s + 36, 0x60000020);
        }
        Section(0, "nsn\0t", 0x5967e6, 0x1000, packed ? 0x400U : 0x596800U, 0x400);
        Section(1, "nsn", 0xa6000, 0xa8d000, 0x400, 0x800);
        Section(2, ".vmp0", 0x426c, 0xb33000, 0x400, 0xc00);
        return image;
    }
    var packedPath = Path.Combine(root, "renamed-packed.exe");
    File.WriteAllBytes(packedPath, BootstrapFixture(true));
    try { ClientBootstrapInspector.ValidateForLaunch(packedPath); Check(false, "known packed bootstrap is rejected before creating a child"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("加壳"), "packed-client error explains required compatible bootstrap"); }
    var rebuiltPath = Path.Combine(root, "renamed-rebuilt.exe");
    File.WriteAllBytes(rebuiltPath, BootstrapFixture(false));
    ClientBootstrapInspector.ValidateForLaunch(rebuiltPath);

    Process StartFixture(string mode)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(NativeProfile).Assembly.Location);
        info.ArgumentList.Add("--startup-test-child"); info.ArgumentList.Add(mode);
        return Process.Start(info)!;
    }
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        var invalid = new LauncherOptions { ClientPath = Path.Combine(root, "not-created.exe"), Account = "player", Password = "secret" };
        try { await LaunchFlow.RunAsync(invalid, invalid.GetSelectedAccount(), cancelled.Token); Check(false, "pre-cancelled launch stops before preflight/installation"); }
        catch (OperationCanceledException) { }
        catch (Exception) { Check(false, "pre-cancelled launch performed preflight side effects"); }
        var created = 0;
        try
        {
            await ClientProcessLifetime.RunAsync(() => { created++; return StartFixture("0"); },
                (_, _) => Task.CompletedTask, handoffOnSuccess: true, cancelled.Token);
            Check(false, "pre-cancelled lifetime must not start a child");
        }
        catch (OperationCanceledException) { }
        Check(created == 0, "cancelled ownership boundary does not invoke child factory");
    }
    Process? cancellationProbe = null;
    using (var cancelled = new CancellationTokenSource(150))
    {
        try
        {
            try
            {
                await ClientProcessLifetime.RunAsync(() =>
                {
                    var child = StartFixture("wait");
                    cancellationProbe = Process.GetProcessById(child.Id);
                    _ = cancellationProbe.SafeHandle;
                    return child;
                }, (child, token) => ClientStartupMonitor.ObserveAsync(child, TimeSpan.FromSeconds(5), token),
                    handoffOnSuccess: true, cancelled.Token);
                Check(false, "cancelled startup propagates cancellation");
            }
            catch (OperationCanceledException) { }
            Check(cancellationProbe is not null && cancellationProbe.HasExited,
                "cancelling actual startup stops and awaits the owned child before returning");
        }
        finally
        {
            if (cancellationProbe is not null)
            {
                if (!cancellationProbe.HasExited) cancellationProbe.Kill(entireProcessTree: true);
                await cancellationProbe.WaitForExitAsync(); cancellationProbe.Dispose();
            }
        }
    }
    foreach (var handoff in new[] { false, true })
    {
        Process? probe = null;
        try
        {
            await ClientProcessLifetime.RunAsync(() =>
            {
                var child = StartFixture("wait");
                probe = Process.GetProcessById(child.Id); _ = probe.SafeHandle;
                return child;
            }, (child, token) => ClientStartupMonitor.ObserveAsync(child, TimeSpan.FromMilliseconds(100), token),
                handoffOnSuccess: handoff, CancellationToken.None);
            Check(probe is not null && probe.HasExited != handoff,
                handoff ? "successful no-relay handoff keeps the real client alive" : "ending an owned lifetime stops its still-running child");
        }
        finally
        {
            if (probe is not null)
            {
                if (!probe.HasExited) probe.Kill(entireProcessTree: true);
                await probe.WaitForExitAsync(); probe.Dispose();
            }
        }
    }
    foreach (var code in new[] { 0, 37 })
    {
        using var child = StartFixture(code.ToString());
        try { await ClientStartupMonitor.ObserveAsync(child, TimeSpan.FromSeconds(5)); Check(false, "a child that exits before startup observation is not reported successful"); }
        catch (InvalidOperationException ex)
        { Check(ex.Message.Contains($"0x{code:X8}") && ex.Message.Contains("退出"), "early exit error contains the real child exit code"); }
    }
    using (var child = StartFixture("wait"))
    {
        try
        {
            await ClientStartupMonitor.ObserveAsync(child, TimeSpan.FromMilliseconds(100));
            Check(!child.HasExited, "startup observation succeeds while actual child remains alive");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await ClientStartupMonitor.ObserveAsync(child, TimeSpan.FromSeconds(5), cancelled.Token); Check(false, "startup cancellation propagates"); }
            catch (OperationCanceledException) { }
        }
        finally { if (!child.HasExited) child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
    }

    var store = new LauncherAccountStore(Path.Combine(root, "launcher.json"));
    var fresh = store.Load();
    Check(fresh.Account == "" && fresh.Password == "" && fresh.Accounts.Count == 0,
        "a missing release profile must not create test/test credentials");
    File.WriteAllText(Path.Combine(root, "launcher.json"), """
        {"Account":"","Password":"","Accounts":[],"StartServer":false}
        """);
    var empty = store.Load();
    try
    {
        DFLegacy.Launcher.Program.ValidateOptions(empty);
    }
    catch (Exception ex)
    {
        Check(false, "empty saved credentials must allow UI-entered login: " + ex.Message);
    }
    Check(empty.GetSelectedAccount().Account == "" && empty.GetSelectedAccount().Password == "",
        "empty saved profile must remain empty");
    store.Save(empty);
    Check(!File.ReadAllText(Path.Combine(root, "launcher.json")).Contains("test"),
        "saving an empty profile must not generate a test account");
    try
    {
        _ = DFLegacy.Launcher.Program.BuildLauncherData(empty);
        Check(false, "headless launch must reject absent login credentials");
    }
    catch (InvalidDataException) { }
    var remembered = new LauncherOptions { Account = "player", Password = "secret" };
    Check(DFLegacy.Launcher.Program.BuildLauncherData(remembered).StartsWith("99?127.0.0.1?2311?player?secret?"),
        "existing remembered credentials still create launcher data");

    // Installing a release patch needs no saved login, and must propagate the
    // same resolved URL that the normal launch flow uses before starting DNF.
    var launcherConfig = Path.Combine(AppContext.BaseDirectory, "launcher.json");
    var originalLauncherConfig = File.Exists(launcherConfig) ? File.ReadAllBytes(launcherConfig) : null;
    try
    {
        var clientDirectory = Path.Combine(root, "headless-install");
        Directory.CreateDirectory(clientDirectory);
        var clientPath = Path.Combine(clientDirectory, "DNF.exe");
        File.WriteAllBytes(clientPath, []);
        File.WriteAllBytes(Path.Combine(clientDirectory, "Script.pvf"), []);
        File.WriteAllText(launcherConfig, System.Text.Json.JsonSerializer.Serialize(new LauncherOptions
        {
            ClientPath = clientPath, Account = "", Password = "", Accounts = [],
            AccountServiceUrl = "https://remote.example/service", StartServer = false
        }));
        var exitCode = await DFLegacy.Launcher.Program.RunHeadlessAsync(["--install-ijl15", "--no-dialog"]);
        Check(exitCode == 1, "obsolete production IJL replacement is explicitly rejected even without credentials");
        var installedConfig = Path.Combine(clientDirectory, "Config.ini");
        Check(!File.Exists(installedConfig) && !File.Exists(Path.Combine(clientDirectory,"ijl15.dll")),
            "rejected obsolete install makes zero config/IJL writes");
        StandaloneClientInstaller.Configure(clientPath,"https://remote.example/service");
        Check(File.ReadAllText(installedConfig).Contains("ClientServiceUrl=https://remote.example/service") && !File.Exists(Path.Combine(clientDirectory,"ijl15.dll")),
            "standalone configuration propagates service URL without replacing IJL");
    }
    finally
    {
        if (originalLauncherConfig is null) File.Delete(launcherConfig);
        else File.WriteAllBytes(launcherConfig, originalLauncherConfig);
    }

    {
        // Exercise the real installer, using its PE validation and packaged native DLL.
        var client = Path.Combine(root, "DNF.exe");
        File.WriteAllBytes(client, []);
        var config = Path.Combine(root, "Config.ini");
        var externalDefaults = Path.Combine(AppContext.BaseDirectory, "Config.ini");
        var savedDefaults = externalDefaults + ".test-backup";
        if (File.Exists(externalDefaults)) File.Move(externalDefaults, savedDefaults);
        try
        {
            Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
            var generated = File.ReadAllText(config);
            Check(generated.Contains("[Features]") && generated.Contains("EnableDebugOverlay=0"), "launcher creates embedded defaults without a loose INI template");
        }
        finally { if (File.Exists(savedDefaults)) File.Move(savedDefaults, externalDefaults); }
        const string customized = "; 用户自定义\r\n[Resolution]\r\nWidth=1234\r\n[Network]\r\nOther=keep\r\nClientServiceUrl=http://old.example\r\n[Custom]\r\nValue=保留\r\n";
        File.WriteAllText(config, customized);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api///");
        var text = File.ReadAllText(config);
        Check(text.Contains("ClientServiceUrl=https://game.example:8443/api"), "installer writes the remote URL");
        Check(text.Contains("Width=1234") && text.Contains("Other=keep") && text.Contains("Value=保留") && text.Contains("; 用户自定义"),
            "installer preserves unrelated customized INI content");
        File.WriteAllText(config, "[Network]\r\nOther=keep\r\n[Custom]\r\nValue=保留\r\n[Network]\r\nClientServiceUrl=http://old.example\r\n");
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        var effective = new StringBuilder(1100);
        _ = NativeProfile.GetPrivateProfileString("Network", "ClientServiceUrl", "", effective, effective.Capacity, config);
        Check(effective.ToString() == "https://game.example:8443/api", "duplicate INI sections cannot shadow the installed URL");
        var ansi = Encoding.Latin1.GetBytes("; legacy ANSI \u0081\u0040\r\n[Custom]\r\nValue=\u00fe\u00ff\r\n");
        File.WriteAllBytes(config, ansi);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        Check(File.ReadAllBytes(config).AsSpan().StartsWith(ansi), "legacy ANSI custom bytes are preserved");
        File.WriteAllText(config, customized, Encoding.Unicode);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        Check(File.ReadAllBytes(config).AsSpan().StartsWith(Encoding.Unicode.GetPreamble())
            && File.ReadAllText(config).Contains("Value=保留"), "UTF-16 BOM and custom text are preserved");
        effective.Clear();
        _ = NativeProfile.GetPrivateProfileString("Network", "ClientServiceUrl", "", effective, effective.Capacity, config);
        Check(effective.ToString() == "https://game.example:8443/api", "native profile API reads updated Unicode INI");
        File.WriteAllText(config, customized);
        Ijl15ReplacementInstaller.Install(client, "https://game.example:8443/api");
        text = File.ReadAllText(config);
        var before = text;
        foreach (var invalid in new[] { "ftp://game.example", "https://user:password@game.example", "https://game.example/%s", "https://game.example?x=1", "https://game.example\n[Features]", "https://game.example:70000" })
        {
            try
            {
                Ijl15ReplacementInstaller.Install(client, invalid);
                Check(false, "installer rejects an invalid URL: " + invalid);
            }
            catch (InvalidDataException) { }
            Check(File.ReadAllText(config) == before, "invalid URL must not alter the existing config");
        }
        var boundary = "https://example.org/" + new string('a', 980);
        Ijl15ReplacementInstaller.Install(client, boundary);
        Check(File.ReadAllText(config).Contains("ClientServiceUrl=" + boundary), "1000 byte URL base is accepted");
        try
        {
            Ijl15ReplacementInstaller.Install(client, boundary + "a");
            Check(false, "1001 byte URL base must be rejected");
        }
        catch (InvalidDataException) { }
        File.Delete(config);
        Ijl15ReplacementInstaller.Install(client, "http://game.example:8081");
        Check(File.ReadAllText(config).Contains("ClientServiceUrl=http://game.example:8081"),
            "first installation writes the URL before game launch");
        var resolved = new LauncherOptions { EntranceHost = "2001:db8::1", AccountServicePort = 8081 };
        Check(resolved.ResolveAccountServiceUrl() == "http://[2001:db8::1]:8081", "IPv6 account service base uses URL brackets");
    }
}
finally { Directory.Delete(root, recursive: true); }
Console.WriteLine(failures == 0 ? $"Launcher configuration tests passed ({checks} assertions)" : $"Launcher configuration tests failed: {failures} / {checks}");
return failures == 0 ? 0 : 1;

internal static class NativeProfile
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPrivateProfileStringW")]
    internal static extern uint GetPrivateProfileString(string section, string key, string fallback,
        StringBuilder output, int capacity, string path);
}
