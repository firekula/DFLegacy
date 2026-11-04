namespace DFLegacy.Manager;

public sealed record ClientExportJobStatus(string Id, string State, ClientExportProgress Progress, string? Error, long Size);

// One active generation, bounded completed history. A disconnected browser must
// not cancel generation; the host lifetime owns cancellation instead.
public sealed class ClientExportJobs(ClientPackageExporter exporter, ManagerLog log, ClientUpdateStore? updates = null)
{
    private sealed class Job(string id)
    {
        public ClientExportJobStatus Status = new(id, "running", new("preparing", 0, 0, "等待生成客户端包…"), null, 0);
        public string? Path;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new();

    public ClientExportJobStatus Start(DeploymentConfig config, CancellationToken stopping = default, bool testBuild = false)
    {
        ConfigurationStore.Validate(config);
        lock (_gate)
        {
            if (_jobs.Values.Any(job => job.Status.State == "running"))
                throw new InvalidOperationException("正在生成其他客户端包，请稍后重试。");
            while (_jobs.Count >= 16) _jobs.Remove(_jobs.Keys.First());
            var job = new Job(Guid.NewGuid().ToString("N"));
            _jobs.Add(job.Status.Id, job);
            log.Write("export", "开始生成客户端包。");
            _ = Task.Run(async () =>
            {
                try
                {
                    var path = await exporter.ExportPairAsync(config, testBuild, updates, stopping, value =>
                    {
                        lock (_gate)
                        {
                            if (value.Stage != job.Status.Progress.Stage) log.Write("export", value.Message);
                            job.Status = job.Status with { Progress = value };
                        }
                    });
                    lock (_gate)
                    {
                        job.Path = path;
                        job.Status = job.Status with { State = "ready", Size = new FileInfo(path).Length };
                    }
                    log.Write("export", $"客户端包生成完成，共 {new FileInfo(path).Length} 字节，可下载。");
                }
                catch (Exception error)
                {
                    // No exception text from arbitrary IO paths is exposed to a browser.
                    var message = error is OperationCanceledException ? "管理器正在停止，客户端生成已取消。"
                        : error is InvalidDataException or ArgumentException or InvalidOperationException ? error.Message
                        : "客户端生成失败，请检查资源、磁盘空间和目录权限。";
                    lock (_gate) job.Status = job.Status with { State = "failed", Error = message };
                    log.Write("export", message);
                }
            }, CancellationToken.None);
            return job.Status;
        }
    }

    public ClientExportJobStatus? Read(string id) { lock (_gate) return _jobs.GetValueOrDefault(id)?.Status; }
    public string? Download(string id)
    {
        lock (_gate)
        {
            var job = _jobs.GetValueOrDefault(id);
            return job?.Status.State == "ready" && File.Exists(job.Path) ? job.Path : null;
        }
    }
}
