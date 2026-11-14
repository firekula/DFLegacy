using DFLegacy.Launcher;

internal static class ConnectionConfigTests
{
    internal static int Run()
    {
        try
        {
            var domain = ChapterTwoConnection.Parse("game.example.test", null);
            Require(domain.UseRelay && domain.Host == "game.example.test" && domain.AccountServiceUrl == "http://game.example.test:48091", "domain supplies both game and account service");
            Require(ChapterTwoConnection.Parse("2001:db8::20", null).AccountServiceUrl == "http://[2001:db8::20]:48091", "IPv6 account authority is bracketed");
            Require(!ChapterTwoConnection.Parse(null, null).UseRelay && ChapterTwoConnection.Parse(null, null).Host == "127.0.0.2", "existing local profile stays local");
            Require(ChapterTwoConnection.Parse(null, "192.0.2.20") is { UseRelay: false, LegacyIpv4: "192.0.2.20" }, "legacy IPv4 configuration stays compatible");
            foreach (var bad in new[] { "", "localhost", "127.0.0.2", "::", "0.0.0.0", "https://game.example.test", "game.example.test/path", " game.example.test", "ff02::1", "fe80::1", "::ffff:192.0.2.20", "game.example.test:42312" })
                Reject(() => ChapterTwoConnection.Parse(bad, null));
            Reject(() => ChapterTwoConnection.Parse("game.example.test", "192.0.2.20"));
            Require(new[] { 7004, 7110, 7111, 7112, 7100, 7105, 48090 }.All(ChapterTwoConnection.TcpPorts.Contains), "all PK and regional endpoints are included");
            Console.WriteLine("PASS Chapter Two domain, IPv6, legacy configuration and invalid destinations");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL connection configuration: " + error); return 1; }
    }
    private static void Require(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("invalid connection configuration accepted");
    }
}
