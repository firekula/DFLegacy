using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DFLegacy.Manager;

public sealed record InitialGameAdmin(string UserName, string Password, string OicqCode);
public sealed record ServerSnapshot(string State, int? Pid = null, int? ExitCode = null, string? Error = null, DateTimeOffset? StartedAt = null, bool RestartRequired = false);

public sealed class ServerSupervisor : IAsyncDisposable
{
    private readonly ManagerPaths _paths;
    private readonly ConfigurationStore _config;
    private readonly ManagerLog _log;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _stateGate = new();
    private readonly HttpClient _client = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    private Process? _process;
    private string _controlSecret = "";
    private Uri? _controlAddress;
    private DeploymentConfig? _runningConfig;
    private ServerSnapshot _snapshot;
    private readonly PvfResourceStore _resources;

    public ServerSupervisor(ManagerPaths paths, ConfigurationStore config, ManagerLog log)
    {
        _paths = paths; _config = config; _log = log;
        _resources = new(paths);
        _snapshot = new(config.Read().Configured ? "stopped" : "not_configured");
    }
    public ServerSnapshot Snapshot { get { lock (_stateGate) return _snapshot; } }
    public PvfResourceStatus GetPvfStatus() => _resources.GetStatus(_config.Read());
    public async Task<PvfResourceStatus> UploadPvfAsync(Stream input, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("正在处理服务或上传任务，请稍后重试。");
        try
        {
            if (_process is { HasExited: false }) throw new InvalidOperationException("请先停止游戏服务再替换 Script.pvf。");
            if (GetPvfStatus().ExternallyControlled) throw new InvalidOperationException("PVF 由环境变量或容器挂载控制，请修改部署环境。");
            var stored = await _resources.StoreAsync(input, cancellationToken);
            try { cancellationToken.ThrowIfCancellationRequested(); _config.SetPvfPath(stored.Path); }
            catch { if (stored.Created) File.Delete(stored.Path); throw; }
            ConfigurationChanged(); _log.Write("manager", "Script.pvf 上传完成并已选择；请保存其他服务配置后启动。");
            return GetPvfStatus();
        }
        finally { _gate.Release(); }
    }
    private void SetSnapshot(ServerSnapshot snapshot) { lock (_stateGate) _snapshot = snapshot; }
    public void ConfigurationChanged()
    {
        lock (_stateGate)
        {
            if (_snapshot.State == "running") _snapshot = _snapshot with { RestartRequired = _runningConfig != _config.Read().Effective };
            else if (_snapshot.State == "not_configured" && _config.Read().Configured) _snapshot = new("stopped");
        }
    }
    public async Task<ConfigurationSnapshot> SaveConfigurationAsync(DeploymentConfig request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { var result = _config.Save(request); ConfigurationChanged(); return result; }
        finally { _gate.Release(); }
    }

    public async Task<ServerSnapshot> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await StartCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    public async Task<ServerSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await StopCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }
    public async Task<ServerSnapshot> RestartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await StopCoreAsync(cancellationToken); return await StartCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<ServerSnapshot> StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return Snapshot;
        if (_process is not null) { _process.Dispose(); _process = null; }
        var startupLogId = _log.Read().LastOrDefault()?.Id ?? 0;
        try
        {
            var configuration = _config.Read();
            if (!configuration.Configured) throw new InvalidOperationException("请先完成服务配置并保存。");
            var settings = configuration.Effective;
            ConfigurationStore.Validate(settings);
            var document = _config.ReadServerDocument();
            if (ConfigurationStore.Get(document, "DFLegacy:Processes:Channel:Enabled")?.GetValue<bool>() == true
                || Environment.GetEnvironmentVariable("DFLegacy__Processes__Channel__Enabled")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("管理器首版仅托管单体模式；请关闭 Processes.Channel.Enabled，或使用高级手动部署。");
            var pvfPath = Path.GetFullPath(settings.ScriptPvfPath, _paths.Root);
            if (!File.Exists(pvfPath)) throw new InvalidOperationException("找不到 Script.pvf，请指定或挂载客户端匹配的文件。");
            using (var pvf = File.OpenRead(pvfPath)) if (pvf.Length == 0) throw new InvalidOperationException("Script.pvf 文件为空。");
            foreach (var port in settings.ForwardedTcpPorts()) ProbePort(settings.BindHost, port, SocketType.Stream);
            ProbePort(settings.AdminHost, settings.AdminPort, SocketType.Stream);
            foreach (var port in settings.ForwardedUdpPorts()) ProbePort(settings.BindHost, port, SocketType.Dgram);
            var start = CreateStartInfo();
            _controlSecret = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            start.Environment["DFLEGACY_MANAGER_CONTROL_TOKEN"] = _controlSecret;
            _log.AddSecret(_controlSecret);
            var controlHost = IPAddress.Parse(settings.AdminHost);
            if (controlHost.Equals(IPAddress.Any)) controlHost = IPAddress.Loopback;
            if (controlHost.Equals(IPAddress.IPv6Any)) controlHost = IPAddress.IPv6Loopback;
            if (!IPAddress.IsLoopback(controlHost)) throw new InvalidOperationException("游戏 API 监听地址须为回环或通配地址，以允许本机管理通道。");
            _controlAddress = new UriBuilder("http", controlHost.ToString(), settings.AdminPort).Uri;
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => _log.Write("server", args.Data);
            process.ErrorDataReceived += (_, args) => _log.Write("server", args.Data);
            process.Exited += (_, _) =>
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_process, process) && _snapshot.State == "running")
                    {
                        _snapshot = _snapshot with { State = "exited", Pid = null, ExitCode = process.ExitCode, Error = "游戏服务意外退出，请检查日志。" };
                        _log.Write("manager", $"游戏进程已退出，退出码 {process.ExitCode}。");
                    }
                }
            };
            _process = process;
            SetSnapshot(new("starting"));
            if (!process.Start()) throw new InvalidOperationException("无法创建游戏服务进程。");
            process.StandardInput.Close();
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            SetSnapshot(new("starting", process.Id, StartedAt: DateTimeOffset.UtcNow));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            while (!deadline.IsCancellationRequested)
            {
                if (process.HasExited) throw new InvalidOperationException($"游戏服务在就绪前退出，退出码 {process.ExitCode}，请检查日志。");
                try
                {
                    using var request = ControlRequest(HttpMethod.Get, "/internal/manager/status");
                    using var response = await _client.SendAsync(request, deadline.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadFromJsonAsync<JsonObject>(deadline.Token);
                        if (body?["managed"]?.GetValue<bool>() == true && body?["ready"]?.GetValue<bool>() == true)
                        {
                            lock (_stateGate)
                            {
                                if (process.HasExited) throw new InvalidOperationException("游戏服务在就绪时退出，请检查日志。");
                                _runningConfig = settings;
                                _snapshot = _snapshot with { State = "running", RestartRequired = false };
                            }
                            _log.Write("manager", "游戏服务已通过专用管理通道的就绪检查。");
                            return Snapshot;
                        }
                    }
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException) { }
                await Task.Delay(150, deadline.Token);
            }
            throw new InvalidOperationException("游戏服务启动超时，请检查日志、PVF 和端口设置。");
        }
        catch (Exception exception)
        {
            var exitCode = HasStarted(_process) && _process!.HasExited ? _process.ExitCode : (int?)null;
            await KillOwnedAsync();
            _process?.Dispose(); _process = null; _controlSecret = ""; _runningConfig = null;
            var message = exception is OperationCanceledException ? "游戏服务启动超时或被取消，请检查日志。" : exception.Message;
            var failure = _log.Read(startupLogId).FirstOrDefault(line => line.Source == "server"
                && (line.Text.Contains("fail:", StringComparison.OrdinalIgnoreCase)
                    || line.Text.Contains("crit:", StringComparison.OrdinalIgnoreCase)
                    || line.Text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase)));
            if (failure is not null)
            {
                var detail = System.Text.RegularExpressions.Regex.Replace(failure.Text, @"\s+at\s+.*$", "", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                message += " 原因：" + (detail.Length > 600 ? detail[..600] + "…" : detail);
            }
            SetSnapshot(new("failed", ExitCode: exitCode, Error: message));
            _log.Write("manager", message);
            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<ServerSnapshot> StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            _process?.Dispose(); _process = null; _controlSecret = "";
            SetSnapshot(new(Snapshot.State == "not_configured" ? "not_configured" : "stopped")); return Snapshot;
        }
        SetSnapshot(Snapshot with { State = "stopping" });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = ControlRequest(HttpMethod.Post, "/internal/manager/stop");
            using var response = await _client.SendAsync(request, deadline.Token);
            response.EnsureSuccessStatusCode();
            await _process.WaitForExitAsync(deadline.Token);
            _process.WaitForExit();
            _log.Write("manager", $"游戏服务已正常停止，退出码 {_process.ExitCode}。");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            _log.Write("manager", "优雅停止未完成，终止当前管理器托管的游戏进程。");
            await KillOwnedAsync();
        }
        _process?.Dispose(); _process = null; _controlSecret = "";
        SetSnapshot(new("stopped")); return Snapshot;
    }

    public async Task InitializeAdminAsync(InitialGameAdmin account, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.UserName) || string.IsNullOrWhiteSpace(account.Password) || string.IsNullOrWhiteSpace(account.OicqCode)) throw new ArgumentException("请填写游戏管理员账号、密码和找回码。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false }) throw new InvalidOperationException("请先停止游戏服务再初始化管理员。");
            _log.AddSecret(account.Password); _log.AddSecret(account.OicqCode);
            var info = CreateStartInfo(); info.ArgumentList.Add("--initialize-admin");
            using var process = new Process { StartInfo = info };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { userName = account.UserName, password = account.Password, oicqCode = account.OicqCode }));
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
            }
            catch { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } throw; }
            var output = await stdout; var errors = await stderr;
            if (process.ExitCode != 0)
            {
                string message = "初始化游戏管理员失败，请检查游戏账号是否已存在。";
                try { message = JsonNode.Parse(output)?["error"]?.GetValue<string>() ?? message; } catch (JsonException) { }
                _log.Write("manager", message); throw new InvalidOperationException(message);
            }
            _log.Write("manager", "游戏管理员已通过离线存档业务初始化。");
        }
        finally { _gate.Release(); }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var executable = Path.Combine(_paths.ServerDirectory, OperatingSystem.IsWindows() ? "DFLegacy.Server.exe" : "DFLegacy.Server");
        var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            WorkingDirectory = _paths.ServerDirectory };
        if (File.Exists(executable)) info.FileName = executable;
        else
        {
            var assembly = Path.Combine(_paths.ServerDirectory, "DFLegacy.Server.dll");
            if (!File.Exists(assembly)) throw new InvalidOperationException("缺少配套游戏服务程序，请使用完整发行包。");
            info.FileName = "dotnet"; info.ArgumentList.Add(assembly);
        }
        info.ArgumentList.Add("--config"); info.ArgumentList.Add(_paths.ServerConfig);
        foreach (var key in info.Environment.Keys.Where(key => key.StartsWith("DFLEGACY_MANAGER_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        return info;
    }
    private HttpRequestMessage ControlRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_controlAddress!, path));
        request.Headers.Add("X-DFLegacy-Manager", _controlSecret); return request;
    }
    private static void ProbePort(string host, int port, SocketType type)
    {
        var address = IPAddress.Parse(host);
        using var socket = new Socket(address.AddressFamily, type, type == SocketType.Stream ? ProtocolType.Tcp : ProtocolType.Udp);
        if (address.Equals(IPAddress.IPv6Any)) socket.DualMode = true;
        socket.ExclusiveAddressUse = true;
        try { socket.Bind(new IPEndPoint(address, port)); if (type == SocketType.Stream) socket.Listen(1); }
        catch (SocketException exception)
        {
            var reason = exception.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => "已被其他程序占用，请停止对应程序或修改这个端口",
                SocketError.AccessDenied => "被操作系统拒绝绑定，可能属于系统保留端口或没有绑定权限；请更换此端口，Windows 可用 netsh interface ipv4 show excludedportrange 检查保留范围",
                _ => "无法绑定：" + exception.Message
            };
            throw new InvalidOperationException($"端口 {host}:{port}/{(type == SocketType.Stream ? "tcp" : "udp")} {reason}（错误 {exception.NativeErrorCode}）。", exception);
        }
    }
    private static bool HasStarted(Process? process)
    {
        if (process is null) return false;
        try { _ = process.Id; return true; } catch (InvalidOperationException) { return false; }
    }
    private async Task KillOwnedAsync()
    {
        if (HasStarted(_process) && _process is { HasExited: false } process) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); process.WaitForExit(); }
    }
    public async ValueTask DisposeAsync() { await StopAsync(); _client.Dispose(); _process?.Dispose(); _gate.Dispose(); }
}
