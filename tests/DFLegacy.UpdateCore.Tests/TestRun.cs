using System.Security.Cryptography;
using System.Text;

namespace DFLegacy.UpdateCore.Tests;

internal static class TestRun
{
    private static int checks;
    internal static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception("FAIL: " + message);
    }
    internal static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { checks++; return; }
        throw new Exception("FAIL: accepted " + message);
    }
    internal static async Task RejectAsync(Func<Task> action, string message)
    {
        try { await action(); } catch (InvalidDataException) { checks++; return; }
        throw new Exception("FAIL: accepted " + message);
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    internal static async Task<int> RunAsync(string[] args)
    {
        if (await LocalBundleTests.Dispatch(args) is { } bundleResult) return bundleResult;
        if (TransactionTests.Dispatch(args) is { } child) return child;
        var root = Path.Combine(Path.GetTempPath(), "dflegacy-v4-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await ManifestTests.Run(root);
            await DownloadTests.Run(root);
            await ProviderTests.Run();
            MigrationTests.Run();
            await TransactionTests.Run(root);
            await EngineTests.Run(root);
            await CacheTests.Run(root);
            Console.WriteLine($"Update Core: {checks} assertions passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
