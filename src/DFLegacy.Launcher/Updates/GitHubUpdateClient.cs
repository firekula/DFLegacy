using System.Net;
using System.Net.Http;
using System.Text.Json;
namespace DFLegacy.Launcher.Updates;

/// <summary>Public unauthenticated transport. Inject a client with redirects/cookies disabled; CreateHttpClient supplies the production configuration.</summary>
public sealed class GitHubUpdateClient
{
    public const string AssetName="DFLegacy-client-update-v2-win-x64.zip";
    public const string TestAssetName="DFLegacy-client-update-v2-test-win-x64.zip";
    private readonly string flavor;
    private readonly string architecture;
    private string SelectedAssetName => $"DFLegacy-client-update-v2{(flavor == "test" ? "-test" : "")}-win-{architecture}.zip";
    private readonly HttpClient client;
    private readonly string repository;
    private readonly bool includePrereleases;
    internal static void ValidateRepository(string repository)
    {
        if(repository!="firekula/DFLegacy") throw new ArgumentException("仅允许配置的公开更新仓库。",nameof(repository));
    }
    public GitHubUpdateClient(HttpClient client, string repository, bool includePrereleases=true, string flavor="normal", string architecture="x64")
    {
        ValidateRepository(repository);
        if(flavor is not ("normal" or "test")) throw new ArgumentException("无效的客户端包类型。",nameof(flavor));
        if (architecture is not ("x64" or "x86")) throw new ArgumentException("无效的客户端架构。", nameof(architecture));
        this.architecture = architecture;
        this.flavor=flavor;
        this.client=client; this.repository=repository; this.includePrereleases=includePrereleases;
    }
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false, UseDefaultCredentials=false, Credentials=null }) { Timeout=Timeout.InfiniteTimeSpan };
    private void ValidateAsset(Uri url, bool redirect=false)
    {
        if(url.Scheme!="https" || !url.IsDefaultPort || url.UserInfo.Length!=0 || url.Fragment.Length!=0) throw new InvalidDataException("更新下载地址无效。");
        if(url.Host=="github.com" && url.AbsolutePath.StartsWith("/"+repository+"/releases/download/",StringComparison.Ordinal) && url.AbsolutePath.EndsWith("/"+SelectedAssetName,StringComparison.Ordinal)) return;
        if(redirect && (url.Host=="release-assets.githubusercontent.com" || url.Host=="objects.githubusercontent.com")) return;
        throw new InvalidDataException("更新下载域名不受信任。");
    }
    private async Task<HttpResponseMessage> Get(Uri uri, bool asset, CancellationToken token)
    {
        UpdateDownload.RequireNoCredentials(client);
        for(int hop=0;hop<5;hop++)
        {
            if(asset) ValidateAsset(uri,hop>0);
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd("DFLegacy-Updater/1.0");
            request.Headers.Accept.ParseAdd(asset ? "application/octet-stream" : "application/vnd.github+json");
            var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(response.RequestMessage?.RequestUri is { } actual && actual!=uri) { response.Dispose(); throw new InvalidDataException("更新客户端必须禁用自动重定向。"); }
            if((int)response.StatusCode is >=300 and <400)
            {
                var next=response.Headers.Location; response.Dispose();
                if(!asset || next==null) throw new InvalidDataException("不支持的更新重定向。");
                uri=next.IsAbsoluteUri?next:new Uri(uri,next); continue;
            }
            return response;
        }
        throw new InvalidDataException("更新重定向过多。");
    }
    public async Task<UpdateRelease?> CheckAsync(long installedReleaseId=0,CancellationToken cancellationToken=default)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var found=new List<UpdateRelease>();
        // Never follow arbitrary Link URLs. A full final page cannot prove that
        // the search is complete (a stable release may follow many previews).
        for(int page=1;page<=3;page++)
        {
            using var response=await Get(new Uri($"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}"),false,timeout.Token).ConfigureAwait(false);
            if(response.StatusCode==HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var body=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer=new MemoryStream(); await UpdateDownload.CopyBounded(body,buffer,4*1024*1024,null,timeout.Token).ConfigureAwait(false);
            using var doc=JsonDocument.Parse(buffer.ToArray());
            if(doc.RootElement.ValueKind!=JsonValueKind.Array) throw new InvalidDataException("GitHub 更新响应无效。");
            foreach(var item in doc.RootElement.EnumerateArray())
            {
                if(item.GetProperty("draft").GetBoolean() || (!includePrereleases && item.GetProperty("prerelease").GetBoolean())) continue;
                var id=item.GetProperty("id").GetInt64(); if(id<=installedReleaseId) continue;
                var assets=item.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==SelectedAssetName).ToArray();
                if(assets.Length!=1) continue;
                var asset=assets[0]; var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
                if(digest==null || !digest.StartsWith("sha256:",StringComparison.Ordinal) || !UpdateSafety.IsHash(digest[7..])) continue;
                var size=asset.GetProperty("size").GetInt64(); if(size<=0 || size>UpdateSafety.MaxZip) continue;
                var uri=new Uri(asset.GetProperty("browser_download_url").GetString()!); ValidateAsset(uri);
                found.Add(new(id,item.GetProperty("tag_name").GetString()!,item.GetProperty("published_at").GetDateTimeOffset(),item.GetProperty("prerelease").GetBoolean(),uri,size,digest[7..]));
            }
            if(doc.RootElement.GetArrayLength()<100) break;
            if(page==3 && found.Count==0) throw new IOException("更新列表尚未查询完整，暂时无法确认最新版本。请稍后重试或查看官方发布页。");
        }
        return found.OrderByDescending(r=>r.PublishedAt).ThenByDescending(r=>r.Id).FirstOrDefault();
    }
    public async Task<PreparedUpdate> DownloadAsync(UpdateRelease release,string stagingRoot,IProgress<UpdateProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        ValidateAsset(release.DownloadUrl);
        return await UpdateDownload.FetchAsync(release,stagingRoot,progress,cancellationToken,flavor,(uri,token)=>Get(uri,true,token)).ConfigureAwait(false);
    }
}
