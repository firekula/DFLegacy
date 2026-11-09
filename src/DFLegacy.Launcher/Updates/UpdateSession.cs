using System.Net.Http;
using System.Runtime.InteropServices;
namespace DFLegacy.Launcher.Updates;
internal sealed class UpdateSession : IUpdateSession, IDisposable
{
    private readonly HttpClient http;
    private readonly string repository;
    private readonly bool includePrereleases;
    private readonly bool githubEnabled;
    private readonly Uri? hostedOrigin;
    private readonly UpdateInstaller installer;
    private readonly string installation;
    private static string? ArchitectureName => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x64",
        _ => null
    };
    internal UpdateSession(string installation, LauncherOptions options,HttpClient? http=null)
    {
        this.installation=installation;
        repository=options.UpdateRepository; includePrereleases=options.IncludePrereleaseUpdates;
        githubEnabled = !options.DisableGitHubUpdates && ArchitectureName is not null;
        if (!string.IsNullOrWhiteSpace(options.UpdateServiceUrl))
            hostedOrigin = HostedUpdateClient.ParseOrigin(options.UpdateServiceUrl);
        else if (githubEnabled)
            GitHubUpdateClient.ValidateRepository(repository);
        this.http=http??GitHubUpdateClient.CreateHttpClient(); installer=new(installation);
    }
    // Installed-package validation happens inside controller operations so missing legacy metadata becomes
    // a visible migration error instead of preventing the launcher window from opening.
    private ClientPackageManifest ReadManifest()
    {
        UpdateSafety.NoLinks(Path.Combine(installation,UpdateSafety.InstalledManifestName));
        return ClientPackageManifest.LoadAndValidate(installation);
    }
    public UpdateRecovery? ReadRecovery()=>UpdateTransaction.InspectRecovery(installation);
    public UpdateState? ReadState()=>installer.ReadState();
    public Task<UpdateHandoff> PrepareRecoveryAsync(UpdateRecovery recovery,CancellationToken token)=>installer.PrepareRecoveryAsync(recovery,token);
    public UpdateApplyResult? ReadLastResult()=>installer.ReadLastResult();
    public async Task<UpdateRelease?> CheckAsync(long installed,CancellationToken token)
    {
        var architecture = ArchitectureName;
        if (architecture is null || hostedOrigin is null && !githubEnabled) return null;
        var manifest = ReadManifest();
        var release = hostedOrigin is null
            ? await new GitHubUpdateClient(http, repository, includePrereleases, manifest.Flavor, architecture).CheckAsync(installed, token).ConfigureAwait(false)
            : await new HostedUpdateClient(http, hostedOrigin, architecture, manifest.Flavor).CheckAsync(installed, token).ConfigureAwait(false);
        // A fresh tools package has no updater state yet. Its validated version
        // already identifies an identical release without a redundant ZIP download.
        if(release?.Version!=manifest.Version || UpdateSafety.RequiresScriptPvf(release) && !manifest.HasScriptPvf) return release;
        if(!installer.RecordInstalledReleaseIfMatching(release)) throw new IOException("检查期间安装文件已变化，请重新检查更新。");
        return null;
    }
    public Task<PreparedUpdate> DownloadAsync(UpdateRelease release,string root,IProgress<UpdateProgress> progress,CancellationToken token)
    {
        var architecture = ArchitectureName;
        if (architecture is null || hostedOrigin is null && !githubEnabled)
            throw new InvalidOperationException("当前架构未配置更新源。");
        var flavor = ReadManifest().Flavor;
        return hostedOrigin is null
            ? new GitHubUpdateClient(http, repository, includePrereleases, flavor, architecture).DownloadAsync(release, root, progress, token)
            : new HostedUpdateClient(http, hostedOrigin, architecture, flavor).DownloadAsync(release, root, progress, token);
    }
    public bool Adopt(PreparedUpdate update)=>installer.RecordCurrentIfMatching(update);
    public Task<UpdateHandoff> PrepareAsync(PreparedUpdate update,CancellationToken token)
    {
        UpdateSafety.ValidateInstalled(installation,update.Manifest);
        // The launcher image is still mapped in this process; only probe native
        // payloads here. The helper probes every target after this process exits.
        foreach(var file in update.Manifest.Files.Where(f=>f.Path!="DFLegacy.Launcher.exe"))
        {
            var path=Path.Combine(installation,file.Path); UpdateSafety.NoLinks(path);
            if(file.Path==ClientPackageManifest.ScriptPvfName && !UpdateSafety.ScriptPvfExists(path)) continue;
            using var probe=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
        }
        return installer.PrepareAsync(update,token);
    }
    public Task<UpdateHelperHandle> StartAsync(UpdateHandoff handoff,CancellationToken token)=>installer.StartHelperAsync(handoff,token);
    public void Dispose()=>http.Dispose();
}
