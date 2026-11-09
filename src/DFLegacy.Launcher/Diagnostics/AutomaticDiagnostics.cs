using System.Diagnostics;
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

    internal static void Configure(LauncherOptions options,DiagnosticSettings? settings=null,Func<DiagnosticSession,Process?>? startHelper=null)
    {
        var registered=false;
        try
        {
            settings??=Program.UserDiagnosticSettings;
            var clientRoot=Path.GetDirectoryName(Program.ResolveClientPath(options.ClientPath))!;
            var site=string.IsNullOrWhiteSpace(options.Diagnostics.ServiceUrl)?options.ResolveAccountServiceUrl():options.Diagnostics.ServiceUrl;
            var preference=settings.Read();
            var key=settings.Root+"|"+clientRoot+"|"+site+"|"+Program.Incidents.SessionId+"|"+preference.Enabled;
            lock(Gate)
            {
                configuredOptions=options;configuredSettings=settings;helperFactory=startHelper;
                if(suspended)return;
                foreach(var owned in helpers.Where(h=>h.Process.HasExited).ToArray()){owned.Process.Dispose();helpers.Remove(owned);}
                // A durable current-user preference is authoritative over copied launcher.json defaults.
                options.Diagnostics.Enabled=preference.Enabled;
                // Dump preferences apply to the next game. Keep the live game's
                // immutable registration and capture helper until it exits.
                if(identity==key&&Session is not null&&(Session.Registration.LocalOnly||new DiagnosticQueue(Session).IsEligible)&&
                    (Session.Registration.LocalSmallDumps==preference.LocalSmallDumps||Session.GameAlive()))return;
                Session?.Complete();
                try {Session=DiagnosticSession.Start(clientRoot,options.Diagnostics.Enabled?site:"",Program.Incidents.SessionId,settings.Root,typeof(Program).Assembly.GetName().Version?.ToString()??"unknown");}
                catch(ArgumentException) {Session=DiagnosticSession.Start(clientRoot,"",Program.Incidents.SessionId,settings.Root,typeof(Program).Assembly.GetName().Version?.ToString()??"unknown");}
                identity=key;
                Program.Incidents.AutomaticReport=Record;
                registered=true;
                var helper=Path.Combine(AppContext.BaseDirectory,"DFLegacy.Diagnostics.exe");
                if(!Session.Registration.LocalOnly&&(startHelper is not null||File.Exists(helper)))
                {
                    DFLegacy.UpdateCore.PathPolicy.NoLinks(helper);
                    var process=startHelper is null?Process.Start(CreateHelperStartInfo(helper,Session)):startHelper(Session);
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
        LauncherOptions? options;DiagnosticSettings? settings;Func<DiagnosticSession,Process?>? factory;
        lock(Gate){if(!suspended)return;suspended=false;options=configuredOptions;settings=configuredSettings;factory=helperFactory;}
        if(options is not null)Configure(options,settings,factory);
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
