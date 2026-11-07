using System.Text.Json;
using DFLegacy.Launcher.Updates;

internal static class UpdatePublicSmokeTests
{
    // Explicit opt-in only: normal tests are deterministic and never query GitHub.
    internal static async Task Run(string root)
    {
        using var http=GitHubUpdateClient.CreateHttpClient();
        var client=new GitHubUpdateClient(http,"firekula/DFLegacy");
        var release=await client.CheckAsync();
        PreparedUpdate? package=null;
        if(release!=null) package=await client.DownloadAsync(release,root);
        var report=new {checkedAtUtc=DateTimeOffset.UtcNow,repository="firekula/DFLegacy",status=release==null?"no_compatible_release":"download_and_hash_validation_passed",
            releaseId=release?.Id,version=release?.Version,asset=release?.DownloadUrl.ToString(),flavor=package?.Manifest.Flavor,
            installed=false,note="This opt-in check uses real public HTTPS. It never installs into a user client or treats a missing compatible asset as a completed live update."};
        var json=JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true});
        Console.WriteLine(json);
        var evidence=Environment.GetEnvironmentVariable("DFLEGACY_PUBLIC_UPDATE_EVIDENCE");
        if(!string.IsNullOrWhiteSpace(evidence)) File.WriteAllText(evidence,json);
    }
}
