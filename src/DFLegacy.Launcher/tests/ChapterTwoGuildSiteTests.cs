using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DFLegacy.Launcher;

internal static class ChapterTwoGuildSiteTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var upstream = builder.Build();
        upstream.MapGet("/account/client/guild", () => Results.Content("<link href='/client/guild.css'>", "text/html"));
        upstream.MapGet("/account/client/guild-hero.jpg", () => Results.Bytes([0xff, 0xd8, 0xff, 0xd9], "image/jpeg"));
        upstream.MapPost("/account/api/client-guild/login", async (HttpRequest request) =>
        {
            if (request.Headers.Origin.Count != 0 || request.Headers["X-DFLegacy-Guild-Request"] != "1"
                || await new StreamReader(request.Body).ReadToEndAsync() != "{\"user\":\"guild\"}")
                return Results.BadRequest();
            request.HttpContext.Response.Cookies.Append("guild_fixture", "test-session",
                new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
            return Results.Json(new { accepted = true });
        });
        upstream.MapGet("/account/api/client-guild/state", (HttpRequest request) =>
            request.Cookies["guild_fixture"] == "test-session" && request.Query["page"] == "2"
                ? Results.Text("member") : Results.Unauthorized());
        upstream.MapPost("/account/api/client-guild/logout", (HttpResponse response) =>
        {
            response.Cookies.Delete("guild_fixture", new() { HttpOnly = true, Secure = true, Path = "/" });
            return Results.NoContent();
        });
        upstream.MapGet("/account/api/client-guild/old", () => Results.Redirect("https://example.invalid/"));
        upstream.MapGet("/account/api/unrelated", () => Results.Text("outside"));
        await upstream.StartAsync();
        var serviceUrl = upstream.Urls.Single() + "/account";

        await using var site = await ChapterTwoGuildSite.StartAsync(serviceUrl, CancellationToken.None);
        var siteUrl = new Uri(site.Url);
        var origin = siteUrl.GetLeftPart(UriPartial.Authority);
        using var browser = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        check(siteUrl.AbsolutePath == "/client/guild" && siteUrl.IsLoopback
            && (await browser.GetStringAsync(siteUrl)).Contains("href='/client/guild.css'"),
            "Chapter Two guild starts a loopback page with its original relative links");
        using var asset = await browser.GetAsync(origin + "/client/guild-hero.jpg");
        check(asset.Content.Headers.ContentType?.MediaType == "image/jpeg"
            && (await asset.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 0xff, 0xd8, 0xff, 0xd9 }),
            "Chapter Two guild proxy preserves an image response");

        using var foreignLogin = Login(origin, "http://127.0.0.2:1");
        using var rejected = await browser.SendAsync(foreignLogin);
        check(rejected.StatusCode == HttpStatusCode.Forbidden, "Chapter Two guild proxy rejects a foreign browser origin");
        using var login = Login(origin, origin);
        using var loggedIn = await browser.SendAsync(login);
        check(loggedIn.StatusCode == HttpStatusCode.OK
            && loggedIn.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Single().Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookies.Single().Contains("samesite=strict", StringComparison.OrdinalIgnoreCase),
            "Chapter Two guild proxy preserves same-origin login and cookie protections on loopback HTTP");
        check(await browser.GetStringAsync(origin + "/api/client-guild/state?page=2") == "member",
            "Chapter Two guild proxy forwards the browser session and query");
        await using var otherSite = await ChapterTwoGuildSite.StartAsync(serviceUrl, CancellationToken.None);
        using var other = await browser.GetAsync(new Uri(new Uri(otherSite.Url), "/api/client-guild/state?page=2"));
        check(other.StatusCode == HttpStatusCode.Unauthorized, "Chapter Two guild session is scoped to its launcher instance");

        using var logout = new HttpRequestMessage(HttpMethod.Post, origin + "/api/client-guild/logout")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        logout.Headers.Add("Origin", origin);
        logout.Headers.Add("X-DFLegacy-Guild-Request", "1");
        using var loggedOut = await browser.SendAsync(logout);
        using var afterLogout = await browser.GetAsync(origin + "/api/client-guild/state?page=2");
        check(loggedOut.StatusCode == HttpStatusCode.NoContent && afterLogout.StatusCode == HttpStatusCode.Unauthorized,
            "Chapter Two guild proxy expires the browser session on logout");

        using var outside = await browser.GetAsync(origin + "/api/unrelated");
        using var redirect = await browser.GetAsync(origin + "/api/client-guild/old");
        check(outside.StatusCode == HttpStatusCode.NotFound && redirect.StatusCode == HttpStatusCode.BadGateway,
            "Chapter Two guild proxy excludes other account routes and stops upstream redirects");
    }

    private static HttpRequestMessage Login(string baseUrl, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/client-guild/login")
        {
            Content = new StringContent("{\"user\":\"guild\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-DFLegacy-Guild-Request", "1");
        return request;
    }
}
