using DFLegacy.Launcher;
using System.Diagnostics;
using System.Text.Json;

internal static class BootstrapManagedTests
{
    internal static async Task<int?> Dispatch(string[] args)
    {
        if(args.Length!=4 || args[0]!="--bootstrap-managed-fixture") return null;
        Console.Out.NewLine="\n";
        var mode=args[1]; var nonce=args[2]; var patch=args[3];
        if(mode=="timeout") { await Task.Delay(30000); return 1; }
        if(mode=="eof") return 1;
        if(mode=="eof-loader") return unchecked((int)0xC0000135);
        if(mode=="partial") { Console.Write("{\"event\":"); return 1; }
        if(mode=="native-disconnected") { Console.WriteLine(JsonSerializer.Serialize(new{schemaVersion=1,@event="error",nonce,code="parent_disconnected",message="secret must not be echoed"})); return 1; }
        if(mode=="oversized") { Console.WriteLine(new string('x',8192)); return 1; }
        if(mode=="malformed") { Console.WriteLine("{\"nonce\":\""+nonce+"\",\"event\":\"ready\",\"event\":\"ready\"}"); return 1; }
        if(mode=="error") { Console.WriteLine(JsonSerializer.Serialize(new{schemaVersion=1,@event="error",nonce,code="initialization_failed",message="secret must not be echoed"})); return 1; }
        using var child = mode is "reuse" or "wrong-path" ? Process.GetProcessById(int.Parse(Environment.GetEnvironmentVariable("DFL_FIXTURE_PID")!)) : StartChild();
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(new{schemaVersion=1,@event="ready",nonce,pid=child.Id,createdFileTime=(child.StartTime.ToUniversalTime().ToFileTimeUtc()+(mode=="reuse"?1:0)).ToString(),patchPath=mode=="wrong-path"?Path.Combine(Path.GetDirectoryName(patch)!,"unexpected.dll"):patch}));
            await Console.In.ReadLineAsync(); if(mode=="extra") Console.WriteLine("unexpected trailing output"); return mode=="exit-failure"?1:0;
        }
        finally { if(mode is not ("reuse" or "wrong-path" or "exit-failure" or "extra") && !child.HasExited) child.Kill(true); }
    }
    private static Process StartChild()
    {
        var start=Self(); start.ArgumentList.Add("--startup-test-child"); start.ArgumentList.Add("wait"); return Process.Start(start)!;
    }
    private static ProcessStartInfo Self()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(start.FileName)=="dotnet") start.ArgumentList.Add(typeof(BootstrapManagedTests).Assembly.Location);
        return start;
    }
    internal static async Task Run(string parent,Action<bool,string> check,bool initialOnly=false)
    {
        var root=Path.Combine(parent,"bootstrap 中文 𠮷 space"); Directory.CreateDirectory(root);
        var native=Environment.GetEnvironmentVariable("DFL_NATIVE_FIXTURES") ?? throw new InvalidOperationException("Set DFL_NATIVE_FIXTURES to the matching CMake build directory (Release native process fixtures required).");
        foreach(var pair in new[]{("Release/bootstrap_fixture.exe","DNF.exe"),("fixture/Release/DFLegacy.Client.dll","DFLegacy.Client.dll"),("fixture-original/Release/ijl15.dll","ijl15.dll"),("Release/RS.dll","RS.dll")})
            File.Copy(Path.Combine(native,pair.Item1),Path.Combine(root,pair.Item2));
        File.Copy(Environment.ProcessPath!,Path.Combine(root,"DFLegacy.Launcher.exe"));
        File.WriteAllText(Path.Combine(root,"Script.pvf"),"synthetic pvf"); File.WriteAllText(Path.Combine(root,"fixture.npk"),"synthetic npk");
        StandaloneTests.WriteManifest(root); var manifest=ClientPackageManifest.LoadAndValidate(root); var client=Path.Combine(root,"DNF.exe");
        var original= new[]{"DNF.exe","ijl15.dll","Script.pvf","fixture.npk"}.ToDictionary(n=>n,n=>ClientIjlMigration.Hash(Path.Combine(root,n)));
        string? reported=null;
        Process started;
        try { started=await ClientBootstrapLauncher.StartCoreAsync(client,Path.Combine(root,"entry.marker"),manifest,CancellationToken.None,
            Path.Combine(native,"Release/DFLegacy.Bootstrap32.exe"),TimeSpan.FromSeconds(20),ready=>reported=ready); }
        catch { var status=Path.Combine(root,"fixture-startup-status.txt"); Console.Error.WriteLine("Synthetic initialization status: "+(File.Exists(status)?File.ReadAllText(status):"no-status")); throw; }
        using(var process=started)
        {
            for(var i=0;i<100 && !File.Exists(Path.Combine(root,"entry.marker"));i++) await Task.Delay(10);
            check(File.Exists(Path.Combine(root,"entry.marker")),"managed commit releases synthetic entry gate");
            check(reported==Path.GetFullPath(Path.Combine(root,"DFLegacy.Client.dll")),"M1 exact Unicode canonical loaded patchPath value");
            process.Kill(true); await process.WaitForExitAsync();
        }
        check(original.All(p=>ClientIjlMigration.Hash(Path.Combine(root,p.Key))==p.Value),"bootstrap never writes EXE/IJL/PVF/NPK");
        if(initialOnly) return;
        using(var cancelled=new CancellationTokenSource(150))
        {
            try { using var p=await ClientBootstrapLauncher.StartCoreAsync(client,"timeout-cancel",manifest,cancelled.Token,Path.Combine(native,"Release/DFLegacy.Bootstrap32.exe"),TimeSpan.FromSeconds(20));check(false,"cancelled bootstrap cannot hand off child"); }
            catch(OperationCanceledException) { }
        }
        File.Copy(Path.Combine(native,"Release/debug.dll"),Path.Combine(root,"debug.dll"));
        StandaloneTests.WriteManifest(root,"test"); var testManifest=ClientPackageManifest.LoadAndValidate(root);
        using(var p=await ClientBootstrapLauncher.StartCoreAsync(client,Path.Combine(root,"test-mode.marker"),testManifest,CancellationToken.None,Path.Combine(native,"Release/DFLegacy.Bootstrap32.exe"),TimeSpan.FromSeconds(20))) { p.Kill(); await p.WaitForExitAsync(); }
        StandaloneTests.WriteManifest(root);
        using(var p=await ClientBootstrapLauncher.StartCoreAsync(client,Path.Combine(root,"retained-debug-normal.marker"),ClientPackageManifest.LoadAndValidate(root),CancellationToken.None,Path.Combine(native,"Release/DFLegacy.Bootstrap32.exe"),TimeSpan.FromSeconds(20)))
        {
            check(File.Exists(Path.Combine(root,"debug.dll")) && !p.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Equals("debug.dll",StringComparison.OrdinalIgnoreCase)),"retained_debug_is_not_loaded_by_normal_mode: actual owned child modules");
            p.Kill(); await p.WaitForExitAsync();
        }
        foreach(var mode in new[]{"fail","timeout"})
        {
            try { using var p=await ClientBootstrapLauncher.StartCoreAsync(client,mode,manifest,CancellationToken.None,Path.Combine(native,"Release/DFLegacy.Bootstrap32.exe"),TimeSpan.FromSeconds(15)); check(false,"native initialization failure blocks entry"); }
            catch(InvalidDataException ex) { check(ex.Message.Contains("补丁") && !ex.Message.Contains("secret"),"readable safe bootstrap errors"); }
        }
        foreach(var mode in new[]{"wrong-path","error","eof","eof-loader","partial","native-disconnected","timeout","reuse","exit-failure","extra","oversized","malformed"})
        {
            using var unrelated=StartChild();
            try
            {
                using var p=await ClientBootstrapLauncher.StartCoreAsync(Environment.ProcessPath!,"secret",manifest,CancellationToken.None,Environment.ProcessPath!,TimeSpan.FromMilliseconds(mode=="timeout"?150:5000),null,
                    nonce=>{var start=Self();start.Environment["DFL_FIXTURE_PID"]=unrelated.Id.ToString();start.ArgumentList.Add("--bootstrap-managed-fixture");start.ArgumentList.Add(mode);start.ArgumentList.Add(nonce);start.ArgumentList.Add(Path.Combine(root,"DFLegacy.Client.dll"));return start;});
                check(false,"managed protocol rejects "+mode);
            }
            catch(InvalidDataException ex)
            {
                check(!ex.Message.Contains("secret"),"protocol never echoes helper explanation/credentials");
                var expected=mode switch {"wrong-path"=>"module_identity","error"=>"initialization_failed","eof" or "eof-loader"=>"bootstrap_no_reply","native-disconnected"=>"parent_disconnected","timeout"=>"initialization_timeout","reuse"=>"child_identity","exit-failure" or "extra"=>"handshake_failed",_=>"invalid_protocol"};
                check(Equals(ex.Data["BootstrapCode"],expected),"exact protocol rejection for "+mode+": "+ex.Data["BootstrapCode"]);
                if (mode is "eof" or "eof-loader")
                {
                    var exitCode = mode == "eof" ? 1 : unchecked((int)0xC0000135);
                    var display = mode == "eof" ? "0x00000001" : "0xC0000135";
                    check(Equals(ex.Data["BootstrapExitCode"],exitCode) && ex.Message.Contains(display),"silent bootstrap exit includes its exact code: "+mode);
                }
                else if (mode == "native-disconnected")
                    check(!ex.Data.Contains("BootstrapExitCode"),"native disconnect remains a protocol error reply");
            }
            finally { check(!unrelated.HasExited,"malformed/reused PID never kills unrelated process"); if(!unrelated.HasExited) unrelated.Kill(); await unrelated.WaitForExitAsync(); }
        }
    }
}
