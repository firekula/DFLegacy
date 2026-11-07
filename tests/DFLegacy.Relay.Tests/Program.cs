using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DFLegacy.Relay;

var tests = new (string Name, Func<Task> Run)[]
{
    ("fragmented duplex data and half-close preserve bytes and report EOF", FragmentedDuplexAsync),
    ("server reset reports socket error and releases the idle opposite pump", ServerResetAsync),
    ("host cancellation awaits connection cleanup and releases the listener", HostCancellationAsync),
    ("connection refusal reports the target and socket error", ConnectionRefusalAsync),
    ("concurrent connections have distinct correlated diagnostics", ConcurrentConnectionsAsync),
    ("start logger failure does not interrupt forwarding", () => LifecycleLoggerFailureAsync("connection-start")),
    ("direction logger failure does not erase byte totals", () => LifecycleLoggerFailureAsync("direction-end")),
    ("end logger failure does not fail host shutdown", () => LifecycleLoggerFailureAsync("connection-end")),
    ("connection refusal keeps its code when cancellation races reporting", () => TransportFailureCancellationRaceAsync(SocketError.ConnectionRefused)),
    ("connection reset keeps its code when cancellation races reporting", () => TransportFailureCancellationRaceAsync(SocketError.ConnectionReset))
};
var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}
Console.WriteLine($"Relay tests: {tests.Length - failed}/{tests.Length} passed.");
return failed == 0 ? 0 : 1;

static async Task FragmentedDuplexAsync()
{
    await using var fixture = new RelayFixture();
    using var pair = await fixture.ConnectAsync();
    var request = new byte[131117];
    var response = new byte[65573];
    new Random(173).NextBytes(request);
    new Random(29).NextBytes(response);
    const string privatePayload = "relay-private-payload-DO-NOT-LOG";
    Encoding.ASCII.GetBytes(privatePayload).CopyTo(request, 0);

    var receivedRequest = ReadToEofAsync(pair.Server, fixture.Deadline);
    var receivedResponse = ReadToEofAsync(pair.Client, fixture.Deadline);
    await Task.WhenAll(
        WriteFragmentsAsync(pair.Client, request, fixture.Deadline),
        WriteFragmentsAsync(pair.Server, response.AsMemory(0, 12345), fixture.Deadline));
    pair.Client.Client.Shutdown(SocketShutdown.Send);
    Require((await receivedRequest).AsSpan().SequenceEqual(request), "fragmented request bytes changed");

    // The reply deliberately continues after the server has observed request EOF.
    // Cancelling the other pump on graceful EOF would truncate this reply.
    await WriteFragmentsAsync(pair.Server, response.AsMemory(12345), fixture.Deadline);
    pair.Server.Client.Shutdown(SocketShutdown.Send);
    Require((await receivedResponse).AsSpan().SequenceEqual(response), "reply after request EOF was truncated or changed");
    await fixture.Logs.WaitForAsync(line => Has(line, "event=connection-end"), fixture.Deadline);
    var logs = fixture.Logs.Connections;
    Require(logs.Length == 4, $"expected four bounded lifecycle entries, got {logs.Length}");
    var id = Field(logs.Single(line => Has(line, "event=connection-start")), "connection");
    Require(logs.All(line => Field(line, "connection") == id), "connection log correlation changed");
    Require(logs.Any(line => Has(line, "direction=client->server", "bytes=131117", "reason=eof")), "missing exact request byte count / EOF");
    Require(logs.Any(line => Has(line, "direction=server->client", "bytes=65573", "reason=eof")), "missing exact response byte count / EOF");
    Require(logs.All(line => !line.Contains(privatePayload, StringComparison.Ordinal)), "payload leaked into diagnostics");
}

static async Task ServerResetAsync()
{
    await using var fixture = new RelayFixture();
    using var pair = await fixture.ConnectAsync();
    await ExchangeSmallPayloadsAsync(pair, fixture.Deadline);
    pair.Server.Client.LingerState = new LingerOption(true, 0);
    // Dispose the raw socket: TcpClient/NetworkStream disposal performs a
    // Shutdown(Both) first, which can turn this into a graceful EOF on Windows.
    pair.Server.Client.Dispose();

    // The client remains open and idle. A reset must terminate its blocked read
    // inside the relay without requiring host shutdown or an arbitrary idle timeout.
    await RequireClosedAsync(pair.Client, fixture.Deadline);
    var end = await fixture.Logs.WaitForAsync(line => Has(line, "event=connection-end"), fixture.Deadline);
    Require(Has(end, "reason=socket-error"), "connection did not retain its socket failure reason");
    var logs = fixture.Logs.Connections;
    var reset = logs.Single(line => Has(line, "direction=server->client"));
    Require(Has(reset, "bytes=3", "reason=socket-error", "socketError=ConnectionReset"), "reset direction/code/bytes missing");
    Require(int.TryParse(Field(reset, "nativeError"), out var nativeError) && nativeError != 0, "native socket error code missing");
    Require(logs.Any(line => Has(line, "direction=client->server", "bytes=7", "reason=cancelled", "cancellation=peer-error")), "idle opposite pump did not terminate on reset");
    Require(logs.Length == 4, "reset diagnostics were missing or unbounded");
}

static async Task HostCancellationAsync()
{
    await using var fixture = new RelayFixture();
    using var pair = await fixture.ConnectAsync();
    await ExchangeSmallPayloadsAsync(pair, fixture.Deadline);
    await fixture.CancelHostAsync();
    var lines = fixture.Logs.All;
    Require(lines.Count(line => Has(line, "event=direction-end", "reason=cancelled", "cancellation=host-stop")) == 2,
        "host completed before both cancelled pump summaries were recorded");
    Require(lines.Any(line => Has(line, "event=connection-end", "reason=cancelled")), "cancelled connection end missing");
    Require(Array.FindIndex(lines, line => Has(line, "event=connection-end")) < Array.FindIndex(lines, line => line == "relay stopped."),
        "host stopped before connection disposal completed");
    await RequireClosedAsync(pair.Client, fixture.Deadline);
    await RequireClosedAsync(pair.Server, fixture.Deadline);
    var replacement = new TcpListener(IPAddress.Loopback, fixture.Port);
    try { replacement.Start(); }
    finally { replacement.Stop(); }
}

static async Task ConnectionRefusalAsync()
{
    await using var fixture = new RelayFixture();
    fixture.StopTarget();
    using var client = new TcpClient(AddressFamily.InterNetwork);
    await client.ConnectAsync(IPAddress.Loopback, fixture.Port, fixture.Deadline);
    var end = await fixture.Logs.WaitForAsync(line => Has(line, "event=connection-end"), fixture.Deadline);
    Require(Has(end, "phase=connect", "reason=socket-error", "socketError=ConnectionRefused"), "connect refusal lost its phase or socket code");
    Require(fixture.Logs.Connections.Any(line => Has(line, "event=connection-start", $"target=127.0.0.2:{fixture.Port}")), "target endpoint missing");
    Require(fixture.Logs.Connections.Length == 2, "unconnected sockets should only have start/end diagnostics");
    await RequireClosedAsync(client, fixture.Deadline);
}

static async Task ConcurrentConnectionsAsync()
{
    await using var fixture = new RelayFixture();
    using var first = await fixture.ConnectAsync();
    using var second = await fixture.ConnectAsync();
    await Task.WhenAll(ExchangeSmallPayloadsAsync(first, fixture.Deadline), ExchangeSmallPayloadsAsync(second, fixture.Deadline));
    await fixture.CancelHostAsync();
    var starts = fixture.Logs.Connections.Where(line => Has(line, "event=connection-start")).ToArray();
    Require(starts.Length == 2, "start entry missing for an accepted connection");
    var ids = starts.Select(line => Field(line, "connection")).Distinct().ToArray();
    Require(ids.Length == 2 && ids.All(id => long.TryParse(id, out var value) && value > 0), "connection identifiers are missing or reused");
    foreach (var id in ids)
    {
        var logs = fixture.Logs.Connections.Where(line => Field(line, "connection") == id).ToArray();
        Require(logs.Length == 4, "concurrent connection summaries intermingled or missing");
        Require(logs.Any(line => Has(line, "direction=client->server", "bytes=7")), "concurrent request byte count changed");
        Require(logs.Any(line => Has(line, "direction=server->client", "bytes=3")), "concurrent response byte count changed");
    }
}

static async Task LifecycleLoggerFailureAsync(string failingEvent)
{
    await using var fixture = new RelayFixture(failingEvent);
    using var pair = await fixture.ConnectAsync();
    await ExchangeSmallPayloadsAsync(pair, fixture.Deadline);
    var clientStream = pair.Client.GetStream();
    var serverStream = pair.Server.GetStream();
    pair.Client.Client.Shutdown(SocketShutdown.Send);
    pair.Server.Client.Shutdown(SocketShutdown.Send);
    await RequireStreamClosedAsync(clientStream, fixture.Deadline);
    await RequireStreamClosedAsync(serverStream, fixture.Deadline);
    var end = await fixture.Logs.WaitForAsync(line => Has(line, "event=connection-end"), fixture.Deadline);
    Require(Has(end, "reason=eof", "clientToServerBytes=7", "serverToClientBytes=3"), "logger failure replaced the transfer result");
    await fixture.CancelHostAsync();
    Require(fixture.InjectedLogFailures > 0, "test did not inject its logger failure");
}

static async Task TransportFailureCancellationRaceAsync(SocketError expectedError)
{
    // Capture a real transport failure, then model host cancellation occurring
    // before the faulted await's continuation classifies that captured error.
    var failure = await ObserveSocketFailureAsync(expectedError);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var result = TcpForwarder.DescribeFailure(new IOException("Transport failed.", failure), 7, cancellation.Token, cancellation.Token);
    Require(result.Reason == "socket-error" && result.SocketError == expectedError && result.NativeError == failure.NativeErrorCode,
        "host cancellation hid the concrete transport error and codes");
    Require(result.Bytes == 7, "transport error lost completed byte count");
    var cancelled = TcpForwarder.DescribeFailure(new OperationCanceledException(cancellation.Token), 3, cancellation.Token, cancellation.Token);
    Require(cancelled.Reason == "cancelled" && cancelled.Cancellation == "host-stop", "actual cancellation was misclassified as a transport error");
}

static async Task<SocketException> ObserveSocketFailureAsync(SocketError expectedError)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var endpoint = (IPEndPoint)listener.LocalEndpoint;
    using var client = new TcpClient(AddressFamily.InterNetwork);
    try
    {
        if (expectedError == SocketError.ConnectionRefused) listener.Stop();
        await client.ConnectAsync(endpoint, deadline.Token);
        using var server = await listener.AcceptTcpClientAsync(deadline.Token);
        var stream = client.GetStream();
        server.Client.LingerState = new LingerOption(true, 0);
        server.Client.Dispose();
        await stream.ReadExactlyAsync(new byte[1], deadline.Token);
    }
    catch (Exception exception) when (exception.GetBaseException() is SocketException)
    {
        var socketException = (SocketException)exception.GetBaseException();
        Require(socketException.SocketErrorCode == expectedError, $"fixture expected {expectedError}, got {socketException.SocketErrorCode}");
        return socketException;
    }
    finally { listener.Stop(); }
    throw new InvalidOperationException("fixture did not produce a transport failure");
}

static async Task ExchangeSmallPayloadsAsync(RelayPair pair, CancellationToken cancellationToken)
{
    byte[] request = [0, 255, 17, 42, 3, 0, 91];
    byte[] response = [255, 0, 63];
    await pair.Client.GetStream().WriteAsync(request, cancellationToken);
    var received = new byte[7];
    await pair.Server.GetStream().ReadExactlyAsync(received, cancellationToken);
    Require(received.AsSpan().SequenceEqual(request), "request bytes changed");
    await pair.Server.GetStream().WriteAsync(response, cancellationToken);
    received = new byte[3];
    await pair.Client.GetStream().ReadExactlyAsync(received, cancellationToken);
    Require(received.AsSpan().SequenceEqual(response), "response bytes changed");
}

static async Task WriteFragmentsAsync(TcpClient client, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
{
    for (var offset = 0; offset < payload.Length; offset += 137)
        await client.GetStream().WriteAsync(payload.Slice(offset, Math.Min(137, payload.Length - offset)), cancellationToken);
}

static async Task<byte[]> ReadToEofAsync(TcpClient client, CancellationToken cancellationToken)
{
    using var result = new MemoryStream();
    await client.GetStream().CopyToAsync(result, cancellationToken);
    return result.ToArray();
}

static Task RequireClosedAsync(TcpClient client, CancellationToken cancellationToken) =>
    RequireStreamClosedAsync(client.GetStream(), cancellationToken);

static async Task RequireStreamClosedAsync(NetworkStream stream, CancellationToken cancellationToken)
{
    try
    {
        Require(await stream.ReadAsync(new byte[1], cancellationToken) == 0, "socket did not reach EOF");
    }
    catch (IOException exception) when (exception.InnerException is SocketException)
    {
        // An abortive close also proves that the peer socket was released.
    }
}

static bool Has(string line, params string[] fields) => fields.All(field => line.Split(' ').Contains(field));

static string Field(string line, string name) => line.Split(' ')
    .SingleOrDefault(field => field.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..] ?? "";

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class RelayFixture : IAsyncDisposable
{
    private readonly TcpListener _target = new(IPAddress.Parse("127.0.0.2"), 0);
    private readonly CancellationTokenSource _hostCancellation = new();
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(10));
    private readonly RelayHost _host;
    private readonly Task _run;
    private int _injectedLogFailures;

    public RelayFixture(string? failingLogEvent = null)
    {
        _target.Start();
        Port = ((IPEndPoint)_target.LocalEndpoint).Port;
        _host = new RelayHost(IPAddress.Loopback, IPAddress.Parse("127.0.0.2"), [Port], [], (level, message) =>
        {
            Logs.Add(level, message);
            if (failingLogEvent is not null && message.Split(' ').Contains($"event={failingLogEvent}"))
            {
                Interlocked.Increment(ref _injectedLogFailures);
                throw new IOException("Simulated diagnostic sink failure.");
            }
        });
        _host.Start();
        _run = _host.RunAsync(_hostCancellation.Token);
    }

    public int Port { get; }
    public int InjectedLogFailures => _injectedLogFailures;
    public LogCapture Logs { get; } = new();
    public CancellationToken Deadline => _deadline.Token;

    public async Task<RelayPair> ConnectAsync()
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, Port, Deadline);
            var server = await _target.AcceptTcpClientAsync(Deadline);
            server.NoDelay = true;
            return new RelayPair(client, server);
        }
        catch { client.Dispose(); throw; }
    }

    public void StopTarget() => _target.Stop();

    public async Task CancelHostAsync()
    {
        _hostCancellation.Cancel();
        await _run.WaitAsync(Deadline);
    }

    public async ValueTask DisposeAsync()
    {
        _hostCancellation.Cancel();
        try { await _run.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally
        {
            _host.Stop();
            _target.Stop();
            _hostCancellation.Dispose();
            _deadline.Dispose();
        }
    }
}

internal sealed record RelayPair(TcpClient Client, TcpClient Server) : IDisposable
{
    public void Dispose() { Client.Dispose(); Server.Dispose(); }
}

internal sealed class LogCapture
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly SemaphoreSlim _changed = new(0);
    public string[] All => _lines.ToArray();
    public string[] Connections => All.Where(line => line.Contains("connection=", StringComparison.Ordinal)).ToArray();
    public void Add(RelayLogLevel level, string message) { _lines.Enqueue(message); _changed.Release(); }

    public async Task<string> WaitForAsync(Func<string, bool> predicate, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_lines.FirstOrDefault(predicate) is { } match) return match;
            try { await _changed.WaitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("expected lifecycle diagnostic was not emitted; logs: " + string.Join(" | ", All));
            }
        }
    }
}
