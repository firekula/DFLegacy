using System.Diagnostics;
using System.Security.Cryptography;
using DFLegacy.Diagnostics;

namespace DFLegacy.Launcher.Diagnostics;

internal static class AutomaticDiagnostics
{
    private static readonly object Gate=new();
    internal static DiagnosticSession? Session { get; private set; }
    private static string? identity;
    private static readonly List<(DiagnosticSession Session,Process Process)> helpers=[];
    private static LauncherOptions? configuredOptions;
    private static DiagnosticSettings? configuredSettings;
    private static Func<DiagnosticSession,Process?>? helperFactory;
    private static bool suspended;
    private static string? configuredHelperDirectory;
    private static Func<ProcessStartInfo,Process?>? configuredStarter;

    internal static void Configure(LauncherOptions options,DiagnosticSettings? settings=null,Func<DiagnosticSession,Process?>? startHelper=null,string? helperDirectory=null,Func<ProcessStartInfo,Process?>? startProcess=null)
    {
        var registered=false;
        try
        {
            settings??=Program.UserDiagnosticSettings;
            var clientRoot=Path.GetDirectoryName(Program.ResolveClientPath(options.ClientPath))!;
            var site=string.IsNullOrWhiteSpace(options.Diagnostics.ServiceUrl)?options.ResolveAccountServiceUrl():options.Diagnostics.ServiceUrl;
            var preference=settings.Read();
            helperDirectory=Path.GetFullPath(helperDirectory??AppContext.BaseDirectory);
            var key=settings.Root+"|"+clientRoot+"|"+site+"|"+Program.Incidents.SessionId+"|"+preference.Enabled+"|"+helperDirectory;
            lock(Gate)
            {
                configuredOptions=options;configuredSettings=settings;helperFactory=startHelper;configuredHelperDirectory=helperDirectory;configuredStarter=startProcess;
                if(suspended)return;
                foreach(var owned in helpers.Where(h=>h.Process.HasExited).ToArray()){owned.Process.Dispose();helpers.Remove(owned);}
                // A durable current-user preference is authoritative over copied launcher.json defaults.
                options.Diagnostics.Enabled=preference.Enabled;
                // Dump preferences apply to the next game. Keep the live game's
                // immutable registration and capture helper until it exits.
                if(identity==key&&Session is not null&&(Session.Registration.LocalOnly||new DiagnosticQueue(Session).IsEligible)&&
                    (Session.Registration.LocalSmallDumps==preference.LocalSmallDumps||Session.GameAlive()))return;
                Session?.Complete();
                try {Session=DiagnosticSession.Start(clientRoot,options.Diagnostics.Enabled?site:"",Program.Incidents.SessionId,settings.Root,ResolveInstalledIdentity(helperDirectory));}
                catch(ArgumentException) {Session=DiagnosticSession.Start(clientRoot,"",Program.Incidents.SessionId,settings.Root,ResolveInstalledIdentity(helperDirectory));}
                identity=key;
                Program.Incidents.AutomaticReport=Record;
                registered=true;
                var helper=Path.Combine(helperDirectory,"DFLegacy.Diagnostics.exe");
                if(!Session.Registration.LocalOnly&&(startHelper is not null||File.Exists(helper)))
                {
                    DFLegacy.UpdateCore.PathPolicy.NoLinks(helper);
                    var process=startHelper is null?StartVerifiedHelper(helper,Session,startProcess):startHelper(Session);
                    if(process is not null)helpers.Add((Session,process));
                }
            }
        }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            if(!registered)lock(Gate){Session=null;identity=null;Program.Incidents.AutomaticReport=null;}
            Program.LogWarning="自动诊断暂不可用，仅保留本地记录。";
        }
    }
    internal static string ResolveInstalledIdentity(string directory)
    {
        static string Safe(string? value) => value is not null&&System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z0-9_.+\-]{1,128}\z")?value:"unknown";
        static string Build(string version,string commit) => Safe(version+"+"+commit) is var combined&&combined!="unknown"?combined:Safe("build."+commit);
        try {
            var installed=new DFLegacy.UpdateCore.InstallationStore(directory).ReadInstalled();
            if(installed is not null)return Build(installed.Release.Manifest.Version,installed.Release.Manifest.Commit);
            var path=Path.Combine(directory,"client-package.json");DFLegacy.UpdateCore.PathPolicy.NoLinks(path);
            if(File.Exists(path)) {
                using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(file.Length is <=0 or >16384)return "unknown";
                var bytes=new byte[(int)file.Length];file.ReadExactly(bytes);var legacy=ClientPackageManifest.Parse(bytes,directory);
                legacy.ValidateFiles();return Build(legacy.Version,legacy.Commit);
            }
            return Safe(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion);
        }catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException){return "unknown";}
    }
    // Retained Process handles bind the exact child identity. Never terminate by name/PID.
    internal static async Task SuspendForUpdateAsync(CancellationToken token)
    {
        (DiagnosticSession Session,Process Process)[] owned;
        lock(Gate)
        {
            if(Session?.GameAlive()==true)throw new IOException("请先退出游戏再更新；本地现场助手仍在使用客户端文件。");
            suspended=true;identity=null;
            Session?.Complete();owned=helpers.ToArray();
            foreach(var child in owned)child.Session.Complete();
        }
        using var bound=CancellationTokenSource.CreateLinkedTokenSource(token);bound.CancelAfter(TimeSpan.FromSeconds(12));
        try {foreach(var child in owned)await child.Process.WaitForExitAsync(bound.Token).ConfigureAwait(false);}
        catch(OperationCanceledException) when(!token.IsCancellationRequested){throw new IOException("自动诊断助手尚未退出，请稍后重试更新。");}
        lock(Gate)foreach(var child in owned){if(helpers.Remove(child))child.Process.Dispose();}
    }
    internal static void ResumeAfterUpdate()
    {
        LauncherOptions? options;DiagnosticSettings? settings;Func<DiagnosticSession,Process?>? factory;string? directory;Func<ProcessStartInfo,Process?>? starter;
        lock(Gate){if(!suspended)return;suspended=false;options=configuredOptions;settings=configuredSettings;factory=helperFactory;directory=configuredHelperDirectory;starter=configuredStarter;}
        if(options is not null)Configure(options,settings,factory,directory,starter);
    }
    private static Process? StartVerifiedHelper(string helper,DiagnosticSession session,Func<ProcessStartInfo,Process?>? startProcess)
    {
        var directory=Path.GetDirectoryName(helper)!;
        var installed=new DFLegacy.UpdateCore.InstallationStore(directory).ReadInstalled();
        long size;string hash;
        if(installed is not null)
        {
            // A V4 receipt takes precedence; never authorize an unmanaged helper
            // through an unrelated legacy receipt or unverified online metadata.
            var file=installed.Release.Manifest.Files.SingleOrDefault(f=>f.Path=="DFLegacy.Diagnostics.exe");
            if(file is null)return null;
            size=file.Size;hash=file.Sha256;
        }
        else
        {
            var metadata=Path.Combine(directory,"client-package.json");DFLegacy.UpdateCore.PathPolicy.NoLinks(metadata);
            if(!File.Exists(metadata))return null;
            using var source=new FileStream(metadata,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(source.Length is <=0 or >16384)return null;
            var bytes=new byte[(int)source.Length];source.ReadExactly(bytes);
            var file=ClientPackageManifest.Parse(bytes,directory).Files.SingleOrDefault(f=>f.Path=="DFLegacy.Diagnostics.exe");
            if(file is null)return null;
            size=file.Size;hash=file.Sha256;
        }
        // Keep the verified image open without write/delete sharing until the
        // OS has created the child; a replacement cannot race the checksum.
        using var image=new FileStream(helper,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(image.Length!=size||Convert.ToHexStringLower(SHA256.HashData(image))!=hash)
        {Program.LogWarning="自动诊断助手校验失败，等待可信更新修复；仅保留本地记录。";return null;}
        return (startProcess??Process.Start)(CreateHelperStartInfo(helper,session));
    }
    internal static ProcessStartInfo CreateHelperStartInfo(string helper,DiagnosticSession session)
    {
        var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(helper)!};
        foreach(var value in new[]{"--session",session.DirectoryPath,"--capability",session.Registration.Capability})start.ArgumentList.Add(value);
        return start;
    }
    private static void Record(DiagnosticIncident incident)
    {
        try
        {
            var session=Session;if(session is null||!new DiagnosticQueue(session).IsEligible)return;
            if(incident.Stage=="game-exit"&&session.FindConfirmedNativeCause() is { } cause)incident=incident with {RelatedEventId=cause};
            session.WriteIncident(incident,"confirmed");
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { }
    }
}
