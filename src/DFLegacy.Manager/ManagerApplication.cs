using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;

namespace DFLegacy.Manager;

public static class ManagerApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ManagerHostOptions.Parse(args);
            var paths = new ManagerPaths(options.Root, options.DataDirectory);
            using var lease = new FileStream(Path.Combine(paths.Data, ".manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var config = new ConfigurationStore(paths);
            var identity = new ManagerIdentity(paths);
            var log = new ManagerLog(paths);
            log.AddSecret(identity.SetupToken);
            var supervisor = new ServerSupervisor(paths, config, log);
            var exporter = new ClientPackageExporter(paths);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [], ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(console => { console.SingleLine = true; console.TimestampFormat = "HH:mm:ss "; });
            builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
            builder.WebHost.ConfigureKestrel(server =>
            {
                server.Listen(options.Listen, options.Port);
                server.Limits.MaxRequestBodySize = 64 * 1024;
            });
            var keyDirectory = Directory.CreateDirectory(Path.Combine(paths.Management, "keys"));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyDirectory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(keyDirectory).SetApplicationName("DFLegacy.Manager");
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookie =>
            {
                cookie.Cookie.Name = "dflegacy.manager";
                cookie.Cookie.HttpOnly = true; cookie.Cookie.SameSite = SameSiteMode.Strict; cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8); cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
                cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            });
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(paths); builder.Services.AddSingleton(config); builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(log); builder.Services.AddSingleton(supervisor); builder.Services.AddSingleton(exporter);
            builder.Services.AddHostedService<ManagerLifetime>();
            builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(30));
            await using var app = builder.Build();
            var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
            foreach (var address in (Environment.GetEnvironmentVariable("DFLEGACY_MANAGER_TRUSTED_PROXIES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                forwarded.KnownProxies.Add(IPAddress.Parse(address));
            app.UseForwardedHeaders(forwarded);
            app.Use(async (context, next) =>
            {
                if (!options.AllowsHost(context.Request.Host.Host)) { context.Response.StatusCode = 400; return; }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                {
                    var origin = context.Request.Headers.Origin.ToString();
                    if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var supplied)
                        || !supplied.GetLeftPart(UriPartial.Authority).Equals($"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase)))
                    { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "请求来源不匹配。" }); return; }
                    var binaryUpload = context.Request.Path == "/api/resources/pvf" && HttpMethods.IsPost(context.Request.Method);
                    if (context.Request.Path.StartsWithSegments("/api") && context.Request.ContentLength > 0 && !context.Request.HasJsonContentType() && !binaryUpload)
                    { context.Response.StatusCode = 415; return; }
                }
                try { await next(context); }
                catch (Exception exception) when (!context.Response.HasStarted && exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { error = exception.Message });
                }
            });
            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated == true && context.Request.Path.StartsWithSegments("/api")
                    && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                    && context.Request.Path != "/api/login" && context.Request.Path != "/api/setup"
                    && !ManagerIdentity.SameSecret(context.Request.Headers["X-CSRF-Token"].ToString(), context.User.FindFirstValue("csrf") ?? ""))
                { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "缺少有效的 CSRF 凭据，请刷新页面。" }); return; }
                await next(context);
            });
            app.UseAuthorization();
            app.UseDefaultFiles(); app.UseStaticFiles();
            app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "dflegacy-manager" }));
            app.MapGet("/ready", () => supervisor.Snapshot.State == "running" ? Results.Ok(new { ready = true }) : Results.Json(new { ready = false }, statusCode: 503));
            app.MapGet("/api/setup/status", () => new { required = !identity.IsConfigured });
            app.MapPost("/api/setup", async (SetupRequest request, HttpContext context) =>
            {
                identity.CompleteSetup(request.SetupToken, request.UserName, request.Password);
                log.Write("manager", "部署管理账号初始化完成。");
                await SignIn(context, identity.UserName); return Results.Ok(new { configured = true });
            });
            app.MapPost("/api/login", async (LoginRequest request, HttpContext context) =>
            {
                if (!identity.VerifyLogin(request.UserName, request.Password, context.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
                    return Results.Json(new { error = "账号、密码错误或尝试过多，请稍后重试。" }, statusCode: 401);
                await SignIn(context, identity.UserName); return Results.Ok(new { loggedIn = true });
            });
            var api = app.MapGroup("/api").RequireAuthorization();
            api.MapGet("/session", (HttpContext context) => new { userName = context.User.Identity!.Name, csrf = context.User.FindFirstValue("csrf") });
            api.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.Ok(); });
            api.MapGet("/config", () => config.Read());
            api.MapGet("/resources/pvf", () => supervisor.GetPvfStatus());
            api.MapPost("/resources/pvf", async (HttpContext context) =>
            {
                if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/octet-stream", StringComparison.OrdinalIgnoreCase))
                    return Results.Json(new { error = "请上传 Script.pvf 二进制文件。" }, statusCode: 415);
                if (context.Request.ContentLength > PvfResourceStore.MaximumBytes)
                    return Results.Json(new { error = "Script.pvf 超过 1 GiB 上限。" }, statusCode: 413);
                var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = PvfResourceStore.MaximumBytes;
                return Results.Ok(await supervisor.UploadPvfAsync(context.Request.Body, context.RequestAborted));
            });
            api.MapPut("/config", async (DeploymentConfig request, HttpContext context) =>
            {
                var result = await supervisor.SaveConfigurationAsync(request, context.RequestAborted);
                log.Write("manager", "服务配置已保存；运行中的服务需重启后应用。"); return result;
            });
            api.MapGet("/status", () =>
            {
                var configuration = config.Read();
                return new { server = supervisor.Snapshot, configuration.Configured, dataDirectory = paths.Data, version = ReadVersion(paths), assetsAvailable = File.Exists(Path.Combine(paths.AssetsDirectory, "manifest.json")),
                    accountServiceUrl = configuration.Effective.AccountServiceUrl, entranceHost = configuration.Effective.AdvertisedHost,
                    entrancePort = configuration.Effective.EntrancePort, gameplayAdvertisedHost = configuration.Effective.GameplayAdvertisedHost };
            });
            api.MapGet("/logs", (long? after) => log.Read(after ?? 0));
            api.MapPost("/server/start", async (CancellationToken token) => Results.Ok(await supervisor.StartAsync(token)));
            api.MapPost("/server/stop", async (CancellationToken token) => Results.Ok(await supervisor.StopAsync(token)));
            api.MapPost("/server/restart", async (CancellationToken token) => Results.Ok(await supervisor.RestartAsync(token)));
            api.MapPost("/game-admin", async (InitialGameAdmin request, HttpContext context) => { await supervisor.InitializeAdminAsync(request, context.RequestAborted); return Results.Ok(new { created = true }); });
            api.MapPost("/export/{kind}", async (string kind, HttpContext context) =>
            {
                var path = await exporter.ExportAsync(kind, config.Read().Effective, context.RequestAborted);
                return Results.File(path, "application/zip", Path.GetFileName(path));
            });
            var browserHost = options.Listen.Equals(IPAddress.Any) ? IPAddress.Loopback : options.Listen.Equals(IPAddress.IPv6Any) ? IPAddress.IPv6Loopback : options.Listen;
            var browserUrl = new UriBuilder("http", browserHost.ToString(), options.Port).Uri.ToString();
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                Console.WriteLine("DFLegacy 部署管理：" + browserUrl);
                if (!identity.IsConfigured) Console.WriteLine("首次设置凭据保存在：" + paths.SetupTokenFile + "（只在初始化时使用）");
                if (!options.NoBrowser && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))))
                {
                    try { Process.Start(new ProcessStartInfo(browserUrl + (!identity.IsConfigured ? "#setup=" + identity.SetupToken : "")) { UseShellExecute = true }); }
                    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.WriteLine("请在浏览器中打开管理地址。"); }
                }
            });
            await app.RunAsync();
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        { Console.Error.WriteLine("无法启动部署管理器：" + exception.Message); return 2; }
    }

    private static async Task SignIn(HttpContext context, string name)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, name), new Claim("csrf", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))) };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = false });
    }
    private static string ReadVersion(ManagerPaths paths)
    {
        try { return System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(paths.Root, "release-manifest.json")))?["version"]?.GetValue<string>() ?? "开发构建"; }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException) { return "开发构建"; }
    }
    private sealed record SetupRequest(string SetupToken, string UserName, string Password);
    private sealed record LoginRequest(string UserName, string Password);
}

internal sealed class ManagerLifetime(ServerSupervisor supervisor, ConfigurationStore config, ManagerIdentity identity, ManagerLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        if (identity.IsConfigured && config.Read().Configured && config.Read().Effective.Autostart)
        { try { await supervisor.StartAsync(stoppingToken); } catch (InvalidOperationException error) { log.Write("manager", error.Message); } }
        try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    { await base.StopAsync(cancellationToken); await supervisor.StopAsync(cancellationToken); }
}
