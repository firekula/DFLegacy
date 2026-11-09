using System.Text;

namespace DFLegacy.Server;

public static class AdminRewardEndpoints
{
    public static void MapAdminRewardEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/characters/{characterId:guid}/victory-points", async (
            Guid characterId, AdminVictoryPointRequest request, HttpContext context,
            JsonGameStore store, CharacterSessionRegistry sessions, CancellationToken ct) =>
        {
            var result = await store.GrantAdminVictoryPointsAsync(Actor(context), request.OperationId, characterId, request.Amount, ct);
            if (result.Status == AdminOperationStatus.Completed) sessions.NotifyVictoryPointsChanged(characterId);
            return Results.Json(result, statusCode: Status(result.Status, result.Error));
        });

        app.MapPost("/api/admin/mail/broadcast/preview", async (
            AdminMailBatchRequest request, HttpContext context, JsonGameStore store, CancellationToken ct) =>
        {
            if (request.Mail is null) return Results.BadRequest(new { error = "invalid-request" });
            try
            {
                var encoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                _ = encoding.GetBytes(request.Mail.Sender ?? "");
                _ = encoding.GetBytes(request.Mail.Text ?? "");
            }
            catch (EncoderFallbackException) { return Results.BadRequest(new { error = "邮件包含客户端无法显示的字符。" }); }
            var result = await store.PrepareAdminMailBatchAsync(Actor(context), request.OperationId, request.Mail, request.RetryOf, ct);
            return Results.Json(result, statusCode: Status(result.Status, result.Error));
        });

        app.MapPost("/api/admin/mail/broadcast/{operationId:guid}/send", async (
            Guid operationId, HttpContext context, JsonGameStore store,
            CharacterSessionRegistry sessions, CancellationToken ct) =>
        {
            var result = await store.SendAdminMailBatchAsync(Actor(context), operationId, ct);
            if (result.Status == AdminOperationStatus.Completed)
                foreach (var delivery in result.Deliveries.Where(d => d.Success))
                    sessions.NotifyMailReceived(delivery.CharacterId);
            return Results.Json(result, statusCode: Status(result.Status, result.Error));
        });

        app.MapGet("/api/admin/mail/broadcast/{operationId:guid}", async (
            Guid operationId, HttpContext context, JsonGameStore store, CancellationToken ct) =>
        {
            var result = await store.GetAdminMailBatchAsync(Actor(context), operationId, ct);
            return Results.Json(result, statusCode: Status(result.Status, result.Error));
        });
    }

    private static string Actor(HttpContext context) =>
        ((AdminTokenInfo)context.Items[AdminAccessPolicy.TokenInfoKey]!).UserName;

    private static int Status(AdminOperationStatus status, string? error) => status switch
    {
        AdminOperationStatus.Unknown => StatusCodes.Status503ServiceUnavailable,
        AdminOperationStatus.Rejected when error is "operation-conflict" => StatusCodes.Status409Conflict,
        AdminOperationStatus.Rejected when error is "character-not-found" or "batch-not-found" => StatusCodes.Status404NotFound,
        AdminOperationStatus.Rejected => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status200OK
    };
}
