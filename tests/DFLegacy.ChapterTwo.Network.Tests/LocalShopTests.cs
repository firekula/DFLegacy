using System.Net;
using System.Net.Http.Json;
using DFLegacy.Launcher;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

internal static class LocalShopTests
{
    internal static async Task<int> Run()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = timeout.Token;
            var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders();
            await using var upstream = builder.Build(); upstream.Urls.Add("http://127.0.0.1:0");
            var calls = 0; var fail = false;
            upstream.MapPost("/api/client-pay/test-cera", async (HttpRequest request) => {
                calls++; var form = await request.ReadFormAsync(token);
                if (form["characterName"] != "relayqa" || form["amount"] != "100") return Results.BadRequest();
                if (fail) return Results.StatusCode(500);
                return Results.Text("<body style=\"margin:0;background:transparent;color:#ddd;font:12px sans-serif\">TEST OK<!-- -->", "text/html");
            });
            upstream.MapPost("/api/auth/login", () => Results.Json(new { role = 1, token = "isolated-test-token" }));
            upstream.MapGet("/api/admin/characters", () => Results.Json(Array.Empty<DebugCharacter>()));
            await upstream.StartAsync(token);
            var domainUrl = new UriBuilder(upstream.Urls.Single()) { Host = "localhost" }.Uri.GetLeftPart(UriPartial.Authority);
            var lookups = 0;
            using (var racing = new HttpClient(new SocketsHttpHandler {
                UseProxy=false, PooledConnectionLifetime=TimeSpan.FromMilliseconds(1),
                ConnectCallback=(context, ct) => DFLegacy.Relay.DomainHttpConnection.ConnectAsync(
                    context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct, (_, _) => {
                        lookups++;
                        return Task.FromResult(Enumerable.Range(2, 16).Select(i => IPAddress.Parse($"::{i:x}")).Append(IPAddress.Loopback).ToArray());
                    })
            }))
            {
                var testHost = new UriBuilder(upstream.Urls.Single()) { Host="shop.example.test", Path="/api/admin/characters" }.Uri;
                using var first = await racing.GetAsync(testHost, token);
                first.EnsureSuccessStatusCode();
                await Task.Delay(5, token);
                using var next = await racing.GetAsync(testHost, token);
                next.EnsureSuccessStatusCode();
                if (lookups != 2) throw new InvalidOperationException("HTTP did not re-resolve after bounded pooling");
            }
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try
                {
                    using var unexpected = await DFLegacy.Relay.DomainHttpConnection.ConnectAsync("localhost", new Uri(domainUrl).Port, cancelled.Token);
                    throw new InvalidOperationException("cancelled HTTP connect unexpectedly succeeded");
                }
                catch (OperationCanceledException) { }
            }
            Console.WriteLine("PASS HTTP multi-address fallback, repeated DNS resolution and cancelled connect");
            await using var site = await LocalPaymentSite.StartAsync(domainUrl, token, useProxy:false, inlineStyle:true,
                listenAddress:IPAddress.Parse("127.0.0.2"), connectionLifetime:TimeSpan.FromMilliseconds(1));
            using var http = new HttpClient(new HttpClientHandler { UseProxy=false });
            foreach (var file in new[] { "client_pay.htm", "client_diamond.htm", "css/commen.css", "images/pic.png", "images/hz.gif" })
            {
                using var response = await http.GetAsync(site.BaseUrl + "/client/" + file, token);
                if (!response.IsSuccessStatusCode || (await response.Content.ReadAsByteArrayAsync(token)).Length == 0)
                    throw new InvalidOperationException("missing local shop resource: " + file);
            }
            using var form = new FormUrlEncodedContent(new Dictionary<string,string> { ["characterName"]="relayqa", ["amount"]="100" });
            using var accepted = await http.PostAsync(site.BaseUrl + "/api/client-pay/test-cera", form, token);
            if (!accepted.IsSuccessStatusCode || !(await accepted.Content.ReadAsStringAsync(token)).Contains("TEST OK") || calls != 1)
                throw new InvalidOperationException("local submit did not reach domain account service exactly once");
            fail = true;
            using var declined = await http.PostAsync(site.BaseUrl + "/api/client-pay/test-cera", form, token);
            if (declined.StatusCode != HttpStatusCode.BadGateway || calls != 2)
                throw new InvalidOperationException("uncertain submission was retried or reported successful");
            using var debug = new ChapterTwoDebugApi(serviceUrl:domainUrl);
            await debug.Login("test", "test");
            if ((await debug.Characters()).Length != 0) throw new InvalidOperationException("debug lookup used another service");
            await upstream.StopAsync(token);
            Console.WriteLine("PASS local shop content, domain account submit, no failed-submit retry, domain debug service");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL local shop: " + error); return 1; }
    }
}
