using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace DFLegacy.Launcher;

internal sealed class ChapterTwoPaymentRelay : IAsyncDisposable
{
    private const int MaximumBody = 1024;
    private const int MaximumReply = 2 * 1024 * 1024;
    private static readonly Dictionary<string, (string Path, bool Post)> Routes = new(StringComparer.Ordinal)
    {
        ["cera-page"] = ("/client/client_pay.htm", false),
        ["diamond-page"] = ("/client/client_diamond.htm", false),
        ["cera-bank-page"] = ("/client/client_pay_bank.htm", false),
        ["cera-netbar-page"] = ("/client/client_pay_netbar.htm", false),
        ["diamond-netbar-page"] = ("/client/client_diamond_netbar.htm", false),
        ["settings"] = ("/client/client-pay-settings.js", false),
        ["auth"] = ("/client/client-auth.js", false),
        ["layout"] = ("/client/client-layout.js", false),
        ["style"] = ("/client/css/commen.css", false),
        ["image-pic"] = ("/client/images/pic.png", false),
        ["image-hz"] = ("/client/images/hz.gif", false),
        ["image-cz"] = ("/client/images/cz.png", false),
        ["cera-submit"] = ("/api/client-pay/test-cera", true),
        ["diamond-submit"] = ("/api/client-pay/test-diamond", true),
    };

    private readonly LocalPaymentSite _site;
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = MaximumReply
    };
    private readonly HashSet<(int Process, uint Request)> _answered = [];

    internal string ReplyDirectory { get; }
    internal string BaseUrl => _site.BaseUrl;

    private ChapterTwoPaymentRelay(LocalPaymentSite site, string replyDirectory)
    {
        _site = site;
        ReplyDirectory = replyDirectory;
    }

    internal static async Task<ChapterTwoPaymentRelay> StartAsync(string runtime, string serviceUrl, CancellationToken token)
    {
        var directory = Path.Combine(runtime, "pay-" + Guid.NewGuid().ToString("N"));
        var site = await LocalPaymentSite.StartAsync(serviceUrl, token, useProxy: false, inlineStyle: true,
            listenAddress: IPAddress.Parse(ChapterTwoClient.Host), connectionLifetime: TimeSpan.FromSeconds(30));
        var relay = new ChapterTwoPaymentRelay(site, directory);
        try
        {
            ClientLogPaths.EnsureDirectory(directory);
            await relay.PreparePagesAsync(token);
            return relay;
        }
        catch
        {
            await relay.DisposeAsync();
            throw;
        }
    }

    private async Task PreparePagesAsync(CancellationToken token)
    {
        foreach (var (name, route) in Routes)
        {
            if (route.Post) continue;
            using var response = await _client.GetAsync(BaseUrl + route.Path, token);
            response.EnsureSuccessStatusCode();
            var mime = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var body = await response.Content.ReadAsByteArrayAsync(token);
            WriteReply(PagePath(name), (int)response.StatusCode, mime, body);
        }
    }

    internal async Task RespondAsync(int pid, string requestId, string routeId, string hex, CancellationToken token)
    {
        if (!uint.TryParse(requestId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0 ||
            !Routes.TryGetValue(routeId, out var route) || !route.Post || !TryReadForm(hex, out var form) ||
            _answered.Count >= 2048 || !_answered.Add((pid, id))) return;

        int status;
        string mime;
        byte[] body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + route.Path);
            request.Content = new ByteArrayContent(form);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var response = await _client.SendAsync(request, token);
            status = (int)response.StatusCode;
            mime = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            body = await response.Content.ReadAsByteArrayAsync(token);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is (HttpRequestException or OperationCanceledException))
        {
            status = 502;
            mime = "text/html; charset=utf-8";
            body = Encoding.UTF8.GetBytes("<meta charset=utf-8>无法确认充值结果，请先核对角色状态。");
        }

        try { WriteReply(ReplyPath(pid, id), status, mime, body); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Program.WriteLauncherLog("Chapter Two local payment reply could not be published.");
        }
    }

    private static void WriteReply(string file, int status, string mime, byte[] body)
    {
        if (mime.Length is 0 or > 100 || body.Length > MaximumReply)
            throw new InvalidDataException("第二章本地充值页内容超出支持范围。");
        var temporary = file + ".tmp";
        try
        {
            using (var output = new BinaryWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                output.Write(status);
                output.Write(mime.Length);
                output.Write(body.Length);
                output.Write(Encoding.ASCII.GetBytes(mime));
                output.Write(body);
            }
            File.Move(temporary, file);
        }
        finally { TryDelete(temporary); }
    }

    private static bool TryReadForm(string hex, out byte[] form)
    {
        form = [];
        if (hex.Length is < 2 or > 2 * MaximumBody || (hex.Length & 1) != 0 || !hex.All(Uri.IsHexDigit)) return false;
        form = Convert.FromHexString(hex);
        return true;
    }

    private string PagePath(string route) => Path.Combine(ReplyDirectory, route + ".rsp");

    private string ReplyPath(int pid, uint id) => Path.Combine(ReplyDirectory, FormattableString.Invariant($"{pid}-{id}.rsp"));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Program.WriteLauncherLog("Chapter Two local payment file could not be cleaned up."); }
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _site.DisposeAsync();
        try
        {
            if (!Directory.Exists(ReplyDirectory)) return;
            foreach (var (name, route) in Routes)
                if (!route.Post) TryDelete(PagePath(name));
            foreach (var (pid, id) in _answered) TryDelete(ReplyPath(pid, id));
            Directory.Delete(ReplyDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Program.WriteLauncherLog("Chapter Two local payment files could not be cleaned up.");
        }
    }
}
