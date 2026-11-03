using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.StaticFiles;

namespace DFLegacy.Launcher;

internal sealed class LocalPaymentSite(WebApplication app, HttpClient upstream, string baseUrl) : IAsyncDisposable
{
    internal string BaseUrl { get; } = baseUrl;

    internal static async Task<LocalPaymentSite> StartAsync(string serviceUrl, CancellationToken token)
    {
        var service = ClientServiceConfiguration.NormalizeBaseUrl(serviceUrl);
        var prefix = "/" + Guid.NewGuid().ToString("N");
        var assembly = typeof(LocalPaymentSite).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("ClientPay/", StringComparison.Ordinal))
            .ToDictionary(name => name.Replace('\\', '/')[10..], StringComparer.Ordinal);
        var types = new FileExtensionContentTypeProvider();
        var upstream = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 16384
        };
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxRequestBodySize = 8192;
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != context.Connection.LocalPort)
            {
                context.Response.StatusCode = 400;
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.MapGet(prefix + "/client/client-pay-settings.js", () =>
            Results.Text("window.DFLegacyClientPay={testCeraEnabled:true};", "application/javascript"));
        app.MapGet(prefix + "/client/{**file}", async (string? file) =>
        {
            if (file is null || !resources.TryGetValue(file, out var resource)
                || !types.TryGetContentType(file, out var contentType))
                return Results.NotFound();
            var stream = assembly.GetManifestResourceStream(resource)!;
            if (!file.EndsWith(".htm", StringComparison.Ordinal))
                return Results.Stream(stream, contentType);
            using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false);
            var html = (await reader.ReadToEndAsync()).Replace("/api/client-pay/test-", prefix + "/api/client-pay/test-", StringComparison.Ordinal);
            return Results.Bytes(Encoding.Latin1.GetBytes(html), "text/html; charset=gb2312");
        });
        app.MapPost(prefix + "/api/client-pay/test-cera", (HttpRequest request) =>
            ForwardAsync(request, upstream, service + "/api/client-pay/test-cera", "amount"));
        app.MapPost(prefix + "/api/client-pay/test-diamond", (HttpRequest request) =>
            ForwardAsync(request, upstream, service + "/api/client-pay/test-diamond", "days"));
        try
        {
            await app.StartAsync(token);
            return new LocalPaymentSite(app, upstream, app.Urls.Single() + prefix);
        }
        catch
        {
            upstream.Dispose();
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task<IResult> ForwardAsync(HttpRequest request, HttpClient upstream, string url, string quantity)
    {
        try
        {
            if (!request.HasFormContentType) return Message("表单格式无效。", 400);
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            if (form["characterName"].Count != 1 || form[quantity].Count != 1)
                return Message("请输入角色名和数量。", 400);
            using var content = new FormUrlEncodedContent([
                new("characterName", form["characterName"].ToString()), new(quantity, form[quantity].ToString())
            ]);
            using var response = await upstream.PostAsync(url, content, request.HttpContext.RequestAborted);
            var html = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(request.HttpContext.RequestAborted));
            const string marker = "<body style=\"margin:0;background:transparent;color:#ddd;font:12px sans-serif\">";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0 && response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest)
            {
                start += marker.Length;
                var end = html.IndexOf("<!--", start, StringComparison.Ordinal);
                if (end >= start && end - start <= 1024)
                    return Message(WebUtility.HtmlDecode(html[start..end]), (int)response.StatusCode);
            }
            return Message("账号服务返回异常，无法确认提交结果。请核对角色状态后再试。", 502);
        }
        catch (InvalidDataException) { return Message("表单内容无效。", 400); }
        catch (BadHttpRequestException) { return Message("表单内容无效。", 400); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            return Message("无法连接账号服务或确认结果。请核对角色状态后再试。", 502);
        }
    }

    private static IResult Message(string text, int status) => Results.Text(
        "<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:transparent;color:#ddd;font:12px sans-serif\">"
        + WebUtility.HtmlEncode(text) + "<!--" + new string(' ', 1100) + "--></body></html>",
        "text/html", Encoding.UTF8, status);

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        upstream.Dispose();
    }
}
