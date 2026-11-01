using System.Net.Http;
namespace DFLegacy.Launcher.Updates;
internal sealed class UpdateSession : IUpdateSession, IDisposable
{
    private readonly HttpClient http;
    private readonly string repository;
    private readonly bool includePrereleases;
    private readonly UpdateInstaller installer;
    private readonly string installation;
    internal UpdateSession(string installation, LauncherOptions options,HttpClient? http=null)
    {
        this.installation=installation;
        repository=options.UpdateRepository; includePrereleases=options.IncludePrereleaseUpdates;
        GitHubUpdateClient.ValidateRepository(repository);
        this.http=http??GitHubUpdateClient.CreateHttpClient(); installer=new(installation);
    }
    // Installed-package validation happens inside controller operations so missing legacy metadata becomes
    // a visible migration error instead of preventing the launcher window from opening.
    private GitHubUpdateClient Client()
    {
        UpdateSafety.NoLinks(Path.Combine(installation,UpdateSafety.InstalledManifestName));
        var manifest=ClientPackageManifest.LoadAndValidate(installation);
        return new(http,repository,includePrereleases,manifest.Flavor);
    }
    public UpdateRecovery? ReadRecovery()=>UpdateTransaction.InspectRecovery(installation);
    public UpdateState? ReadState()=>installer.ReadState();
    public Task<UpdateHandoff> PrepareRecoveryAsync(UpdateRecovery recovery,CancellationToken token)=>installer.PrepareRecoveryAsync(recovery,token);
    public UpdateApplyResult? ReadLastResult()=>installer.ReadLastResult();
    public async Task<UpdateRelease?> CheckAsync(long installed,CancellationToken token)
    {
        var client=Client();
        var manifest=ClientPackageManifest.LoadAndValidate(installation);
        var release=await client.CheckAsync(installed,token).ConfigureAwait(false);
        // A fresh tools package has no updater state yet. Its validated version
        // already identifies an identical release without a redundant ZIP download.
        if(release?.Version!=manifest.Version) return release;
        if(!installer.RecordInstalledReleaseIfMatching(release)) throw new IOException("检查期间安装文件已变化，请重新检查更新。");
        return null;
    }
    public Task<PreparedUpdate> DownloadAsync(UpdateRelease release,string root,IProgress<UpdateProgress> progress,CancellationToken token)=>Client().DownloadAsync(release,root,progress,token);
    public bool Adopt(PreparedUpdate update)=>installer.RecordCurrentIfMatching(update);
    public Task<UpdateHandoff> PrepareAsync(PreparedUpdate update,CancellationToken token)
    {
        UpdateSafety.ValidateInstalled(installation,update.Manifest);
        // The launcher image is still mapped in this process; only probe native
        // payloads here. The helper probes every target after this process exits.
        foreach(var file in update.Manifest.Files.Where(f=>f.Path!="DFLegacy.Launcher.exe"))
        {
            var path=Path.Combine(installation,file.Path); UpdateSafety.NoLinks(path);
            using var probe=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
        }
        return installer.PrepareAsync(update,token);
    }
    public Task<UpdateHelperHandle> StartAsync(UpdateHandoff handoff,CancellationToken token)=>installer.StartHelperAsync(handoff,token);
    public void Dispose()=>http.Dispose();
}
