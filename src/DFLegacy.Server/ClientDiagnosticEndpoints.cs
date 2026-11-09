using System.IO.Compression;
using System.Text.Json;

namespace DFLegacy.Server;

public static class ClientDiagnosticEndpoints
{
    public static void MapClientDiagnostics(this WebApplication app, ServerOptions options)
    {
        app.MapPost("/api/client-diagnostics/reports", async (HttpContext http, ClientDiagnosticStore store) =>
        {
            if (!options.ClientDiagnostics.Enabled || http.Connection.LocalPort != options.Admin.Port) return Results.NotFound();
            if (!store.TryBeginUpload(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", out var lease)) {
                http.Response.Headers.RetryAfter = "60"; return Results.StatusCode(429);
            }
            using (lease)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                if (http.Request.ContentLength > ClientDiagnosticReport.MaxWireBytes) return Results.StatusCode(413);
                if (!http.Request.HasJsonContentType()) return Results.StatusCode(415);
                var encoding = http.Request.Headers.ContentEncoding.ToString();
                if (encoding.Length > 0 && !encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(415);
                try {
                    var wire = await ReadBounded(http.Request.Body, ClientDiagnosticReport.MaxWireBytes, timeout.Token);
                    if (wire is null) return Results.StatusCode(413);
                    var body = wire;
                    if (encoding.Length > 0) {
                        using var input = new MemoryStream(wire); using var gzip = new GZipStream(input, CompressionMode.Decompress);
                        body = await ReadBounded(gzip, ClientDiagnosticReport.MaxExpandedBytes, timeout.Token);
                        if (body is null) return Results.StatusCode(413);
                    }
                    var result = await store.ReceiveAsync(body, timeout.Token);
                    return result.Receipt is null ? Results.StatusCode(result.StatusCode) : Results.Json(result.Receipt, statusCode: result.StatusCode);
                }
                catch (InvalidDataException) { return Results.BadRequest(); }
                catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested) { return Results.StatusCode(408); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
            }
        });

        app.MapGet("/api/admin/diagnostics/groups", async (HttpContext http, ClientDiagnosticStore store, int? page, int? pageSize, string? kind, string? version, string? status, string? system, string? gpu, DateTimeOffset? from, DateTimeOffset? to) => {
            if (!IsGm(http, options)) return Results.NotFound();
            if (!IsAdmin(http)) return Results.Unauthorized();
            http.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(await store.ListAsync(new(page ?? 1, pageSize ?? 25, kind, version, status, system, gpu, from, to), http.RequestAborted)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
        });
        app.MapGet("/api/admin/diagnostics/reports/{id}", async (HttpContext http, string id, ClientDiagnosticStore store) => {
            if (!IsGm(http, options)) return Results.NotFound();
            if (!IsAdmin(http)) return Results.Unauthorized();
            http.Response.Headers.CacheControl = "no-store";
            try { var detail = await store.GetAsync(id, http.RequestAborted); return detail is null ? Results.NotFound() : Results.Ok(detail); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
        });
        app.MapGet("/api/admin/diagnostics/reports/{id}/download", async (HttpContext http, string id, ClientDiagnosticStore store) => {
            if (!IsGm(http, options)) return Results.NotFound();
            if (!IsAdmin(http)) return Results.Unauthorized();
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            try { var body = await store.DownloadAsync(id, http.RequestAborted); return body is null ? Results.NotFound() : Results.File(body, "application/json", "diagnostic-" + id + ".json"); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
        });
        app.MapPatch("/api/admin/diagnostics/groups/{id}", async (HttpContext http, string id, ClientDiagnosticStore store) => {
            if (!IsGm(http, options)) return Results.NotFound();
            if (!IsAdmin(http)) return Results.Unauthorized();
            try {
                var bytes = await ReadBounded(http.Request.Body, 1024, http.RequestAborted);
                if (bytes is null) return Results.StatusCode(413);
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1 || !document.RootElement.TryGetProperty("status", out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not ("new" or "investigating" or "resolved")) return Results.BadRequest();
                var actor = ((AdminTokenInfo)http.Items[AdminAccessPolicy.TokenInfoKey]!).UserName;
                var updated = await store.SetStatusAsync(id, value.GetString()!, actor, http.RequestAborted);
                if (updated) app.Logger.LogInformation("Client diagnostic status changed by {Actor}: {GroupId}, {Status}", actor, id, value.GetString());
                return updated ? Results.NoContent() : Results.NotFound();
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
        });
        app.MapDelete("/api/admin/diagnostics/reports/{id}", async (HttpContext http, string id, ClientDiagnosticStore store) => {
            if (!IsGm(http, options)) return Results.NotFound();
            if (!IsAdmin(http)) return Results.Unauthorized();
            try {
                var actor = ((AdminTokenInfo)http.Items[AdminAccessPolicy.TokenInfoKey]!).UserName;
                var deleted = await store.DeleteAsync(id, actor, http.RequestAborted);
                if (deleted) app.Logger.LogInformation("Client diagnostic deleted by {Actor}: {ReceiptId}", actor, id);
                return deleted ? Results.NoContent() : Results.NotFound();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Results.StatusCode(503); }
        });
    }
    private static bool IsGm(HttpContext http, ServerOptions options) => options.WebPanels.Gm.Enabled && http.Connection.LocalPort == options.WebPanels.Gm.Port;
    private static bool IsAdmin(HttpContext http) => http.Items[AdminAccessPolicy.TokenInfoKey] is AdminTokenInfo { Role: AccountRole.Admin };
    private static async Task<byte[]?> ReadBounded(Stream input, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true) {
            var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum + 1 - (int)output.Length)), cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > maximum) return null;
            output.Write(buffer, 0, count);
        }
    }
}
