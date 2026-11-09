using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace DFLegacy.Server;

public static class WebPanelHosting
{
    public static void Validate(ServerOptions options)
    {
        var ports = new Dictionary<int, string>();
        void Add(string name, int port)
        {
            if (port is < 1 or > 65535) throw new InvalidOperationException($"{name} TCP port must be in 1..65535.");
            if (ports.TryGetValue(port, out var existing)) throw new InvalidOperationException($"{name} TCP port {port} conflicts with {existing}.");
            ports.Add(port, name);
        }
        Add("Admin", options.Admin.Port);
        // Panel ports must not overlap the existing game or inter-process TCP listeners.
        var reserved = new List<(string Name, int Port)> { ("Channel", options.Channel.Port) };
        if (options.Entrance.Enabled && options.Processes.Game.EntranceBinding)
        {
            reserved.Add(("Entrance", options.Entrance.Port));
            reserved.AddRange(options.Entrance.AdditionalPorts.Select(p => ("Entrance.AdditionalPorts", p)));
        }
        if (options.GameProbe.Enabled) reserved.Add(("GameProbe", options.GameProbe.Port));
        if (options.Pvp.Enabled) reserved.Add(("Pvp", options.Pvp.Port));
        if (options.FallbackRelay.Enabled) reserved.Add(("FallbackRelay", options.FallbackRelay.TcpPort));
        if (options.Processes.Channel.Enabled) reserved.Add(("Processes.Channel.LinkPort", options.Processes.Channel.LinkPort));
        foreach (var pair in new[] { ("WebPanels.Gm", options.WebPanels.Gm), ("WebPanels.Player", options.WebPanels.Player) })
        {
            var (name, listener) = pair;
            if (!listener.Enabled) continue;
            if (!IPAddress.TryParse(listener.Host, out _)) throw new InvalidOperationException($"{name}.Host must be an IP address.");
            if (listener.AdditionalPorts.Length != 0) throw new InvalidOperationException($"{name}.AdditionalPorts is not supported; configure its Port.");
            var conflict = reserved.FirstOrDefault(r => r.Port == listener.Port);
            if (conflict.Name is not null) throw new InvalidOperationException($"{name} TCP port {listener.Port} conflicts with {conflict.Name}.");
            Add(name, listener.Port);
        }
    }

    public static void Listen(KestrelServerOptions kestrel, ServerOptions options)
    {
        kestrel.Listen(IPAddress.Parse(options.Admin.Host), options.Admin.Port);
        foreach (var listener in new[] { options.WebPanels.Gm, options.WebPanels.Player })
            if (listener.Enabled) kestrel.Listen(IPAddress.Parse(listener.Host), listener.Port);
    }

    public static void UseWebPanels(this WebApplication app, ServerOptions options)
    {
        app.Use(async (context, next) =>
        {
            var port = context.Connection.LocalPort;
            var path = context.Request.Path.Value ?? "/";
            var diagnosticsReceive = context.Request.Path.StartsWithSegments("/api/client-diagnostics", StringComparison.OrdinalIgnoreCase);
            var diagnosticsAdmin = context.Request.Path.StartsWithSegments("/api/admin/diagnostics", StringComparison.OrdinalIgnoreCase);
            var diagnosticsGm = options.WebPanels.Gm.Enabled && port == options.WebPanels.Gm.Port;
            if (diagnosticsReceive && (port != options.Admin.Port || !options.ClientDiagnostics.Enabled
                    || !HttpMethods.IsPost(context.Request.Method) || !path.TrimEnd('/').Equals("/api/client-diagnostics/reports", StringComparison.OrdinalIgnoreCase))
                || diagnosticsAdmin && !diagnosticsGm
                || path.Equals("/admin/admin-diagnostics.js", StringComparison.OrdinalIgnoreCase) && !diagnosticsGm)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (port != options.Admin.Port && context.Request.Path.StartsWithSegments("/internal/manager", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var player = options.WebPanels.Player.Enabled && port == options.WebPanels.Player.Port;
            var gm = options.WebPanels.Gm.Enabled && port == options.WebPanels.Gm.Port;
            if (!player && !gm) { await next(context); return; }
            if (path == "/")
            {
                context.Request.Path = player ? "/admin/player.html" : "/admin/index.html";
                await next(context);
                return;
            }
            var page = player ? "/admin/player.html" : "/admin/index.html";
            var allowed = path.Equals(page, StringComparison.OrdinalIgnoreCase)
                || path.Equals("/admin/admin.css", StringComparison.OrdinalIgnoreCase)
                || gm && (path.Equals("/admin/admin.js", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/admin/admin-rewards.js", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/admin/admin-diagnostics.js", StringComparison.OrdinalIgnoreCase))
                || path.Equals("/health", StringComparison.OrdinalIgnoreCase);
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                allowed = gm || AdminAccessPolicy.Resolve(context.Request.Method, path, options.EnableTestCeraTopUp).Access != AdminAccess.Admin;
            if (!allowed) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            await next(context);
        });
    }
}
