using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace DFLegacy.Launcher;

internal sealed class ChapterTwoGuildSite(WebApplication app, HttpClient upstream, string origin,
    Func<string, Task> openBrowser) : IAsyncDisposable
{
    private const string GuildPage = "/client/guild";
    private const string ApiPrefix = "/api/client-guild/";
    private const string RequestHeader = "X-DFLegacy-Guild-Request";
    private static readonly HashSet<string> Pages = new(StringComparer.Ordinal)
    {
        GuildPage, "/client/guild.css", "/client/guild.js", "/client/guild-hero.jpg",
        "/client/guild-tile.jpg", "/client/guild-banner.jpg"
    };

    internal string Url { get; } = origin + GuildPage;

    internal Task OpenInBrowserAsync() => openBrowser(Url);

    internal static async Task<ChapterTwoGuildSite> StartAsync(string serviceUrl, CancellationToken token,
        Func<string, Task>? openBrowser = null)
    {
        var address = IPAddress.Parse(ChapterTwoClient.Host);
        var service = ClientServiceConfiguration.NormalizeBaseUrl(serviceUrl);
        var upstream = ChapterTwoConnection.CreateAccountHttpClient(service, useCookies: false);
        upstream.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(address, 0);
            options.Limits.MaxRequestBodySize = 8192;
        });
        var app = builder.Build();
        // Browsers scope cookies to a host, so namespace them by launcher instance.
        var cookiePrefix = "dfl-guild-" + Guid.NewGuid().ToString("N") + "-";
        app.Run(context => ForwardAsync(context, upstream, service, cookiePrefix));
        try
        {
            await app.StartAsync(token);
            return new ChapterTwoGuildSite(app, upstream, app.Urls.Single(), openBrowser ?? OpenDefaultBrowserAsync);
        }
        catch
        {
            upstream.Dispose();
            await app.DisposeAsync();
            throw;
        }
    }

    private static Task OpenDefaultBrowserAsync(string url) =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Program.WriteLauncherLog("Chapter Two guild page passed to the default browser.");
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            {
                Program.WriteLauncherLog($"Chapter Two guild browser could not be started. {error.GetType().Name}");
            }
        }).Task;

    private static async Task ForwardAsync(HttpContext context, HttpClient upstream, string service, string cookiePrefix)
    {
        var request = context.Request;
        var response = context.Response;
        if (request.Host.Host != ChapterTwoClient.Host || request.Host.Port != context.Connection.LocalPort)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        var path = request.Path.Value ?? "";
        var api = path.StartsWith(ApiPrefix, StringComparison.Ordinal) && path.Length > ApiPrefix.Length
            && path[ApiPrefix.Length..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/');
        var post = HttpMethods.IsPost(request.Method);
        if ((!api && !Pages.Contains(path)) || (!HttpMethods.IsGet(request.Method) && !(api && post)))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var requestOrigin = request.Headers.Origin;
        var localOrigin = "http://" + request.Host;
        if ((requestOrigin.Count > 0 && requestOrigin != localOrigin)
            || (post && (requestOrigin != localOrigin || request.Headers[RequestHeader] != "1")))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var forwarded = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get,
            service + path + request.QueryString.ToUriComponent());
        // The proxy has checked the browser's loopback origin. The upstream
        // account service receives a server-side request, potentially through TLS termination.
        if (post)
        {
            forwarded.Content = new StreamContent(request.Body);
            forwarded.Content.Headers.TryAddWithoutValidation(HeaderNames.ContentType, request.ContentType);
            forwarded.Headers.TryAddWithoutValidation(RequestHeader, request.Headers[RequestHeader].ToString());
        }
        if (api)
        {
            var cookies = request.Cookies.Where(cookie => cookie.Key.StartsWith(cookiePrefix, StringComparison.Ordinal))
                .Select(cookie => cookie.Key[cookiePrefix.Length..] + "=" + Uri.EscapeDataString(cookie.Value));
            var header = string.Join("; ", cookies);
            if (header.Length > 0) forwarded.Headers.TryAddWithoutValidation(HeaderNames.Cookie, header);
        }

        try
        {
            using var reply = await upstream.SendAsync(forwarded, context.RequestAborted);
            var status = (int)reply.StatusCode;
            if (status is >= 300 and <= 399)
            {
                response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            response.StatusCode = status;
            response.ContentType = reply.Content.Headers.ContentType?.ToString();
            if (reply.Headers.TryGetValues("Content-Security-Policy", out var policy))
                response.Headers.ContentSecurityPolicy = policy.ToArray();
            if (api && reply.Headers.TryGetValues(HeaderNames.SetCookie, out var cookies))
            {
                foreach (var cookie in SetCookieHeaderValue.ParseList(cookies.ToList()))
                {
                    cookie.Name = cookiePrefix + cookie.Name;
                    cookie.Domain = default;
                    cookie.Secure = false; // The browser-facing origin is HTTP on loopback.
                    response.Headers.Append(HeaderNames.SetCookie, cookie.ToString());
                }
            }
            await reply.Content.CopyToAsync(response.Body, context.RequestAborted);
        }
        catch (BadHttpRequestException error) when (!response.HasStarted)
        {
            response.Clear();
            response.StatusCode = error.StatusCode;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException
            && !context.RequestAborted.IsCancellationRequested && !response.HasStarted)
        {
            response.Clear();
            response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        upstream.Dispose();
    }
}
