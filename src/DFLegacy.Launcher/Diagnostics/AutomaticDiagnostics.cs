using System.Diagnostics;
using DFLegacy.Diagnostics;

namespace DFLegacy.Launcher.Diagnostics;

internal static class AutomaticDiagnostics
{
    private static readonly object Gate=new();
    internal static DiagnosticSession? Session { get; private set; }
    private static string? identity;

    internal static void Configure(LauncherOptions options,DiagnosticSettings? settings=null)
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
                if(!Session.Registration.LocalOnly&&File.Exists(helper)) { DFLegacy.UpdateCore.PathPolicy.NoLinks(helper);using var process=Process.Start(CreateHelperStartInfo(helper,Session)); }
            }
        }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            if(!registered)lock(Gate){Session=null;identity=null;Program.Incidents.AutomaticReport=null;}
            Program.LogWarning="自动诊断暂不可用，仅保留本地记录。";
        }
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
