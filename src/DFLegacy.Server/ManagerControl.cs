using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DFLegacy.Server;

public static class ManagerControl
{
    public const string TokenEnvironmentVariable = "DFLEGACY_MANAGER_CONTROL_TOKEN";
    public const string TokenHeader = "X-DFLegacy-Manager";

    public static bool IsAuthorized(HttpContext context, string? secret)
    {
        var source = context.Connection.RemoteIpAddress;
        if (source is null || !IPAddress.IsLoopback(source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source)
            || string.IsNullOrEmpty(secret)) return false;
        var supplied = context.Request.Headers[TokenHeader];
        return supplied.Count == 1 && !string.IsNullOrEmpty(supplied[0])
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied[0]!), Encoding.UTF8.GetBytes(secret));
    }

    public static void UseManagerControl(this WebApplication app, string? secret)
    {
        app.Use(async (context, next) =>
        {
            // Own the entire prefix so future private routes cannot fall through to game auth.
            if (!context.Request.Path.StartsWithSegments("/internal/manager", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }
            if (!IsAuthorized(context, secret))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Manager authorization required." });
                return;
            }
            var path = context.Request.Path.Value?.TrimEnd('/');
            if (HttpMethods.IsGet(context.Request.Method)
                && string.Equals(path, "/internal/manager/status", StringComparison.OrdinalIgnoreCase))
            {
                var readiness = app.Services.GetRequiredService<ListenerReadiness>();
                await context.Response.WriteAsJsonAsync(new { managed = true, ready = readiness.IsReady, pending = readiness.Pending });
                return;
            }
            if (HttpMethods.IsPost(context.Request.Method)
                && string.Equals(path, "/internal/manager/stop", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnCompleted(() =>
                {
                    app.Lifetime.StopApplication();
                    return Task.CompletedTask;
                });
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                await context.Response.WriteAsJsonAsync(new { stopping = true });
                return;
            }
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });
    }
}
