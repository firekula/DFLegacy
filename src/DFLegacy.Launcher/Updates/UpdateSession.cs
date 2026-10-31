using System.Net.Http;
namespace DFLegacy.Launcher.Updates;
internal sealed class UpdateSession : IUpdateSession, IDisposable
{
    private readonly HttpClient http=GitHubUpdateClient.CreateHttpClient();
    private readonly GitHubUpdateClient client;
    private readonly UpdateInstaller installer;
    private readonly string installation;
    internal UpdateSession(string installation, LauncherOptions options)
    {
        this.installation=installation;
        client=new(http,options.UpdateRepository,options.IncludePrereleaseUpdates); installer=new(installation);
    }
    public UpdateState? ReadState()=>installer.ReadState();
    public UpdateApplyResult? ReadLastResult()=>installer.ReadLastResult();
    public Task<UpdateRelease?> CheckAsync(long installed,CancellationToken token)=>client.CheckAsync(installed,token);
    public Task<PreparedUpdate> DownloadAsync(UpdateRelease release,string root,IProgress<UpdateProgress> progress,CancellationToken token)=>client.DownloadAsync(release,root,progress,token);
    public bool Adopt(PreparedUpdate update)=>installer.RecordCurrentIfMatching(update);
    public Task<UpdateHandoff> PrepareAsync(PreparedUpdate update,CancellationToken token)
    {
        var dll=Path.Combine(installation,"ijl15.dll"); UpdateSafety.NoLinks(dll);
        using(var probe=new FileStream(dll,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite)) { }
        return installer.PrepareAsync(update,token);
    }
    public Task<UpdateHelperHandle> StartAsync(UpdateHandoff handoff,CancellationToken token)=>installer.StartHelperAsync(handoff,token);
    public void Dispose()=>http.Dispose();
}
