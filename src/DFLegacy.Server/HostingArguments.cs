using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace DFLegacy.Server;

public sealed record HostingArguments(string ConfigPath, bool InitializeAdmin, string[] HostArguments)
{
    public static HostingArguments Parse(string[] args)
    {
        string? config = null;
        var initialize = false;
        var remaining = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--config", StringComparison.OrdinalIgnoreCase))
            {
                if (config is not null || ++index >= args.Length || !Path.IsPathFullyQualified(args[index]))
                    throw new ArgumentException("--config requires one absolute configuration file path.");
                config = Path.GetFullPath(args[index]);
            }
            else if (string.Equals(args[index], "--initialize-admin", StringComparison.OrdinalIgnoreCase))
            {
                if (initialize) throw new ArgumentException("--initialize-admin can only be specified once.");
                initialize = true;
            }
            else remaining.Add(args[index]);
        }
        if (initialize && remaining.Count != 0)
            throw new ArgumentException("--initialize-admin cannot be combined with other host or maintenance arguments.");
        return new(config ?? Path.Combine(AppContext.BaseDirectory, "server.json"), initialize, remaining.ToArray());
    }

    public static async Task<int> InitializeAdministratorAsync(ServerOptions options, TextReader input, TextWriter output)
    {
        try
        {
            // Bound stdin as well as fields; never include input or exception text in diagnostics.
            var buffer = new char[16 * 1024 + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var count = await input.ReadAsync(buffer.AsMemory(length));
                if (count == 0) break;
                length += count;
            }
            if (length > 16 * 1024)
                return await FailureAsync(output, 400, "Initialization input exceeds the size limit.");
            var request = JsonSerializer.Deserialize<AdministratorInput>(buffer.AsSpan(0, length),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (request is null)
                return await FailureAsync(output, 400, "Administrator input is required.");
            var store = new JsonGameStore(options, NullLogger<JsonGameStore>.Instance);
            await store.InitializeAsync();
            var result = await store.CreateAccountAsync(new(request.UserName ?? "", request.Password ?? "",
                OicqCode: request.OicqCode));
            if (!result.Created)
            {
                var duplicate = await store.GetAccountAsync(request.UserName ?? "") is not null;
                return await FailureAsync(output, duplicate ? 409 : 400,
                    duplicate ? "Account already exists." : "Account name, password or recovery code is invalid.");
            }
            var promoted = await store.SetRoleAsync(result.Account!.UserName, AccountRole.Admin,
                result.Account.UserName);
            if (!promoted.Success)
                return await FailureAsync(output, 500, "The administrator role could not be assigned.");
            await output.WriteLineAsync(JsonSerializer.Serialize(new { success = true }));
            return 0;
        }
        catch (JsonException)
        {
            return await FailureAsync(output, 400, "Administrator input must be valid JSON.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return await FailureAsync(output, 500, "The game store could not be initialized.");
        }
    }

    private static async Task<int> FailureAsync(TextWriter output, int status, string error)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(new { success = false, status, error }));
        return 2;
    }

    private sealed record AdministratorInput(string? UserName, string? Password, string? OicqCode);
}
