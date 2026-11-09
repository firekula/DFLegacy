using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DFLegacy.Protocol;
using DFLegacy.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class IdleCeraRefreshSmokeTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-idle-cera-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldCipher = Environment.GetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER");
        Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", "1");
        try
        {
            var options = new ServerOptions
            {
                DataPath = Path.Combine(root, "state.json"), SeedTestAccount = false,
                ScriptPvfPath = "", SkillScriptPath = root, EnableTestCeraTopUp = true,
                Entrance = new() { Host = "127.0.0.44", Port = 0 },
                Channel = new() { Host = "127.0.0.44", Port = 47001 },
                GameplayDatagram = new() { Enabled = false },
                CharacterDatagram = new() { Enabled = false }, FallbackRelay = new() { Enabled = false },
                Pvp = new() { Enabled = false }
            };
            var services = new ServiceCollection();
            services.AddLogging();
            // Resolve the real EntranceService dependency graph, with no fake read loop.
            foreach (var type in typeof(EntranceService).Assembly.GetTypes()
                         .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters))
                services.AddSingleton(type);
            services.AddSingleton(options);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new AreaUserIdPool(100));
            await using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<JsonGameStore>();
            await store.InitializeAsync();
            await store.CreateAccountAsync(new("idlecera", "fixture-password", OicqCode: "123456"));
            var first = (await store.CreateCharacterAsync("idlecera", new("IdleCeraA"))).Character!;
            var alternate = (await store.CreateCharacterAsync("idlecera", new("IdleCeraB"))).Character!;
            var sessions = provider.GetRequiredService<CharacterSessionRegistry>();
            var entrance = provider.GetRequiredService<EntranceService>();
            await entrance.StartAsync(CancellationToken.None);
            try
            {
                await using var a = await NativeClient.ConnectAsync();
                await using var b = await NativeClient.ConnectAsync();
                await a.LoginAndSelectAsync(0);
                await b.LoginAndSelectAsync(1);
                // Drain initial traffic; the next notification must be caused by the external grant.
                await a.BarrierAsync(); await b.BarrierAsync();
                var sentA = a.SentCommands; var sentB = b.SentCommands;
                await GrantAsync("IdleCeraA", 50_000);
                var firstA = await a.TryWaitCeraAsync(50_000);
                var firstB = await b.TryWaitCeraAsync(50_000);
                check(firstA && firstB && a.SentCommands == sentA && b.SentCommands == sentB,
                    "the actual idle Chapter Two EntranceService publishes a top-up to both same-wallet actors without another client command");
                if (!firstA || !firstB) return;

                await GrantAsync("IdleCeraB", 13);
                await GrantAsync("IdleCeraA", 17);
                check(await a.TryWaitCeraAsync(50_030) && await b.TryWaitCeraAsync(50_030)
                        && a.SentCommands == sentA && b.SentCommands == sentB,
                    "rapid grants through alternate character names refresh both idle clients to the latest shared wallet");

                // Queue a legacy absolute notification only after a newer durable debit.
                var actor = (await store.GetCharacterAsync("idlecera", first.Id))!;
                await store.SaveCharacterCeraShopPurchaseAsync("idlecera", first.Id, 123,
                    actor.Gold, actor.Inventory ?? [], actor.Equipment ?? []);
                sessions.NotifyCera(first.Id, 50_030); sessions.NotifyCera(alternate.Id, 50_030);
                check(await a.TryWaitCeraAsync(123) && await b.TryWaitCeraAsync(123),
                    "an older absolute top-up notice after a debit reloads canonical123 for both idle sessions");

                // A real command arriving after idle wakes must still use the held socket read.
                await a.BarrierAsync(); await b.BarrierAsync();
                check(a.SentCommands == sentA + 1 && b.SentCommands == sentB + 1,
                    "the original pending TCP reads remain usable after idle cash wake-ups");

                await using var relogged = await NativeClient.ConnectAsync();
                await relogged.LoginAndSelectAsync(0);
                await GrantAsync("IdleCeraA", 1);
                check(await relogged.TryWaitCeraAsync(124) && await b.TryWaitCeraAsync(124),
                    "re-login after the debit retains canonical123 and the next idle grant reaches both actors as124");
            }
            finally
            {
                await entrance.StopAsync(CancellationToken.None);
                // StopAsync closes listeners; per-session final saves finish before
                // releasing their leases. Wait for that boundary before disk reads.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (sessions.IsOnline(first.Id) || sessions.IsOnline(alternate.Id))
                    await Task.Delay(10, cleanup.Token);
            }

            // Reopen the persisted wallet after sessions have stopped; no external live file reader.
            var reloaded = new JsonGameStore(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonGameStore>.Instance);
            await reloaded.InitializeAsync();
            check(await reloaded.GetAccountCeraAsync("idlecera") == 124
                    && (await reloaded.GetCharactersAsync("idlecera")).All(c => c.Cash == 124),
                "canonical account and alternate-character cash remain124 after reopening the saved state");

            async Task GrantAsync(string name, int amount)
            {
                var context = new DefaultHttpContext();
                context.Request.ContentType = "application/x-www-form-urlencoded";
                context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes($"characterName={name}&amount={amount}"));
                var response = await TestCeraEndpoint.HandleAsync(context.Request, options, store, sessions, CancellationToken.None);
                if (response is not IStatusCodeHttpResult { StatusCode: 200 })
                    throw new InvalidOperationException("The real test top-up endpoint did not persist a successful grant.");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DFL_S1C2_TRIAL_CHANNEL_CIPHER", oldCipher);
            Directory.Delete(root, true);
        }
    }

    private sealed class NativeClient : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly CancellationTokenSource stop = new();
        private readonly Channel<(byte Kind, byte Code, byte[] Body)> frames = Channel.CreateUnbounded<(byte, byte, byte[])>();
        private readonly Task reader;
        private ushort sequence;
        internal int SentCommands { get; private set; }
        private NativeClient(TcpClient client) { this.client = client; reader = ReceiveAsync(); }
        internal static async Task<NativeClient> ConnectAsync()
        {
            var peer = new TcpClient();
            await peer.ConnectAsync(IPAddress.Parse("127.0.0.44"), 47001);
            return new(peer);
        }
        internal async Task LoginAndSelectAsync(byte slot)
        {
            await WaitAsync(0, 0);
            await SendAsync(0, new byte[16]); await WaitAsync(0, 1);
            await SendAsync(1, [.. Prefix("idlecera"), .. Prefix("fixture-password")]);
            await WaitAsync(1, 1);
            await SendAsync(4, [slot]); await WaitAsync(1, 4);
            await SendAsync(38, [1, 2, 74, 1, 240, 0]); await WaitAsync(0, 136);
        }
        internal async Task SendAsync(byte code, byte[] payload)
        {
            var padded = new byte[(payload.Length + 15) / 16 * 16]; payload.CopyTo(padded, 0);
            using var aes = Aes.Create();
            aes.Key = code == 0 ? "dfls-local\0\0\0\0\0\0"u8.ToArray()
                : Convert.FromHexString("46494A4946474546444A44404A424758");
            var encrypted = aes.EncryptEcb(padded, PaddingMode.None);
            var seq = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(seq, ++sequence);
            await new PacketFrame(1, code, Crc32.Compute([.. seq, .. padded]), [.. seq, .. encrypted])
                .WriteAsync(client.GetStream(), stop.Token);
            SentCommands++;
        }
        internal Task WaitCeraAsync(uint expected) => WaitAsync(0, 53,
            body => body.Length == 5 && body[0] == 1 && BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(1)) == expected);
        internal async Task BarrierAsync() { await SendAsync(68, []); await WaitAsync(1, 68); }
        internal async Task<bool> TryWaitCeraAsync(uint expected)
        {
            try { await WaitCeraAsync(expected); return true; }
            catch (OperationCanceledException) { return false; }
        }
        private async Task WaitAsync(byte kind, byte code, Func<byte[], bool>? predicate = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (true)
            {
                var frame = await frames.Reader.ReadAsync(deadline.Token);
                if (frame.Kind == kind && frame.Code == code && (predicate?.Invoke(frame.Body) ?? true)) return;
            }
        }
        private async Task ReceiveAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var header = new byte[6];
                    await client.GetStream().ReadExactlyAsync(header, stop.Token);
                    var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2));
                    if (size is < 6 or > 4 * 1024 * 1024) throw new InvalidDataException("Invalid native server frame.");
                    var body = new byte[size - 6];
                    await client.GetStream().ReadExactlyAsync(body, stop.Token);
                    for (var i = 0; i < body.Length; i++) body[i] = (byte)(((body[i] << 6) | (body[i] >> 2)) ^ 0xb5);
                    await frames.Writer.WriteAsync((header[0], header[1], body), stop.Token);
                }
            }
            catch (Exception error) when (stop.IsCancellationRequested || error is EndOfStreamException or IOException)
            { frames.Writer.TryComplete(); }
        }
        private static byte[] Prefix(string value)
        {
            var text = Encoding.ASCII.GetBytes(value); var result = new byte[4 + text.Length];
            BinaryPrimitives.WriteInt32LittleEndian(result, text.Length); text.CopyTo(result, 4); return result;
        }
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync(); client.Dispose();
            await reader; stop.Dispose();
        }
    }
}
