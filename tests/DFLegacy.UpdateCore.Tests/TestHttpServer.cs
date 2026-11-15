using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DFLegacy.UpdateCore.Tests;

internal sealed record TestRequest(string Path, Dictionary<string, string> Headers);
internal sealed record TestResponse(int Status, byte[] Body, Dictionary<string, string>? Headers = null, int? TruncateAt = null, int DelayBodyMs = 0);
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentBag<Task> clients = [];
    private readonly Func<TestRequest, TestResponse> respond;
    private readonly Task accept;
    public Uri Origin { get; }
    public ConcurrentQueue<TestRequest> Requests { get; } = new();
    public TestHttpServer(Func<TestRequest, TestResponse> respond)
    {
        this.respond = respond; listener.Start();
        Origin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        accept = Task.Run(async () =>
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stopping.Token);
                    clients.Add(Serve(client));
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    private async Task Serve(TcpClient client)
    {
        using (client)
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
            var first = await reader.ReadLineAsync(stopping.Token) ?? "";
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadLineAsync(stopping.Token) is { Length: > 0 } line)
            {
                var colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim();
            }
            var request = new TestRequest(first.Split(' ').ElementAtOrDefault(1) ?? "/", headers); Requests.Enqueue(request);
            var result = respond(request);
            var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["Content-Length"] = result.Body.Length.ToString(), ["Content-Type"] = "application/octet-stream", ["Connection"] = "close", ["ETag"] = "\"fixture-v1\"" };
            if (result.Headers != null) foreach (var item in result.Headers) responseHeaders[item.Key] = item.Value;
            var text = $"HTTP/1.1 {result.Status} Fixture\r\n" + string.Join("\r\n", responseHeaders.Select(x => x.Key + ": " + x.Value)) + "\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(text), stopping.Token);
            await stream.FlushAsync(stopping.Token);
            if (result.DelayBodyMs > 0) await Task.Delay(result.DelayBodyMs, stopping.Token);
            await stream.WriteAsync(result.Body.AsMemory(0, result.TruncateAt ?? result.Body.Length), stopping.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); listener.Stop(); await accept; await Task.WhenAll(clients); stopping.Dispose();
    }
}
internal sealed class LocalTransport(HttpClient http, Uri origin) : IResourceTransport
{
    public string Identity => origin.ToString();
    public async Task<HttpResponseMessage> OpenAsync(ResourcePart part, long offset, string? etag, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, part.Asset));
        if (offset > 0)
        {
            request.Headers.Range = new(offset, null);
            if (etag != null) request.Headers.TryAddWithoutValidation("If-Range", etag);
        }
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }
}
