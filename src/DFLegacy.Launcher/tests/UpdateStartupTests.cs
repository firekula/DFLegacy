using System.Diagnostics;
using DFLegacy.Launcher;
internal static class UpdateStartupTests
{
    internal static async Task Run(string root,Action<bool,string> check)
    {
        var target=Path.Combine(root,"early-helper-startup"); Directory.CreateDirectory(target);
        var source=Path.GetDirectoryName(typeof(App).Assembly.Location)!;
        foreach(var path in Directory.EnumerateFiles(source))
        {
            var name=Path.GetFileName(path);
            if(name=="DFLegacy.Launcher.exe" || name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) || name=="DFLegacy.Launcher.deps.json" || name=="DFLegacy.Launcher.runtimeconfig.json") File.Copy(path,Path.Combine(target,name));
        }
        var config=Path.Combine(target,"launcher.json"); File.WriteAllText(config,"{broken JSON");
        foreach(var pipe in new[]{"not-a-valid-pipe","dflegacy-update-"+Guid.NewGuid().ToString("N")})
        {
            var info=new ProcessStartInfo(Path.Combine(target,"DFLegacy.Launcher.exe")) { UseShellExecute=false,WorkingDirectory=target };
            info.ArgumentList.Add("--dflegacy-apply-update"); info.ArgumentList.Add(pipe);
            using var process=Process.Start(info)!;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch(OperationCanceledException) { if(!process.HasExited) process.Kill(); await process.WaitForExitAsync(); throw new TimeoutException("Owned helper startup did not exit; rejection must not leave an orphan modal."); }
            check(process.ExitCode==(pipe=="not-a-valid-pipe"?2:1),"real launcher helper dispatch bypasses malformed normal JSON and exits for invalid/absent pipe without modal");
        }
        check(File.ReadAllText(config)=="{broken JSON","helper UI leaves malformed normal configuration bytes untouched");
        Console.WriteLine("Update early helper WPF startup checks executed");
    }
}
