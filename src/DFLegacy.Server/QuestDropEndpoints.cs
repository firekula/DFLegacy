using System.Text.Json;

namespace DFLegacy.Server;

/// <summary>GM-only runtime control for quest task-item drop rates.</summary>
public static class QuestDropEndpoints
{
    public static void MapQuestDropSettings(this WebApplication app, ServerOptions options)
    {
        app.MapGet("/api/admin/drop/quest-rate", (HttpContext http, QuestDropSettings settings) =>
        {
            if (!IsGm(http, options) || !IsAdmin(http)) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(Describe(settings));
        });

        app.MapPut("/api/admin/drop/quest-rate", async (HttpContext http, QuestDropSettings settings) =>
        {
            if (!IsGm(http, options) || !IsAdmin(http)) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                var bytes = await ReadBounded(http.Request.Body, 256, http.RequestAborted);
                if (bytes is null) return Results.StatusCode(413);
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || root.EnumerateObject().Count() != 1
                    || !root.TryGetProperty("ratePercent", out var value)
                    || value.ValueKind != JsonValueKind.Number
                    || !value.TryGetInt32(out var rate)
                    || rate is < QuestDropSettings.MinimumRatePercent or > QuestDropSettings.MaximumRatePercent)
                {
                    return Results.BadRequest();
                }

                if (!settings.IsPersistent) return Results.StatusCode(503);
                if (!await settings.SetRateAsync(rate, http.RequestAborted)) return Results.StatusCode(503);
                var actor = ((AdminTokenInfo)http.Items[AdminAccessPolicy.TokenInfoKey]!).UserName;
                app.Logger.LogInformation("Quest drop rate changed by {Actor}: {Rate}%", actor, rate);
                return Results.Ok(Describe(settings));
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
        });
    }

    private static object Describe(QuestDropSettings settings) => new
    {
        ratePercent = settings.RatePercent,
        minimum = QuestDropSettings.MinimumRatePercent,
        maximum = QuestDropSettings.MaximumRatePercent,
        persistent = settings.IsPersistent
    };

    private static bool IsGm(HttpContext http, ServerOptions options) =>
        options.WebPanels.Gm.Enabled && http.Connection.LocalPort == options.WebPanels.Gm.Port;

    private static bool IsAdmin(HttpContext http) =>
        http.Items[AdminAccessPolicy.TokenInfoKey] is AdminTokenInfo { Role: AccountRole.Admin };

    private static async Task<byte[]?> ReadBounded(Stream input, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[256];
        while (true)
        {
            var count = await input.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)output.Length)),
                cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > maximum) return null;
            output.Write(buffer, 0, count);
        }
    }
}
