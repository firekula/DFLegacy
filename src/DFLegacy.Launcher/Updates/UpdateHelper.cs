using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace DFLegacy.Launcher.Updates;

public sealed class UpdateHandoff
{
    internal UpdateHandoff(UpdatePlan plan) => Plan=plan;
    internal UpdatePlan Plan { get; }
    public string HelperPath => Plan.HelperPath;
}
internal sealed record UpdatePlan(string Target, PreparedUpdate Update, int ParentId, long ParentStartTicks, string ParentPath, string HelperPath, string ExecutableHash);
public sealed record UpdateHelperHandle(int ProcessId);
internal sealed record UpdateHelperOutcome(int ExitCode, UpdateApplyResult? Result=null, string? Target=null);
public sealed partial class UpdateInstaller
{
    public Task<UpdateHandoff> PrepareAsync(PreparedUpdate update,CancellationToken cancellationToken=default)
        => Task.Run(()=>Prepare(update,cancellationToken),cancellationToken);
    private UpdateHandoff Prepare(PreparedUpdate update,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var targetPin=UpdateSafety.PinDirectory(target);
        using var stagePin=UpdateSafety.PinDirectory(update.Directory);
        UpdateTransaction.ValidateStage(update);
        using var parent=Process.GetCurrentProcess();
        var executable=Path.GetFullPath(parent.MainModule!.FileName);
        if(!executable.Equals(Path.Combine(target,"DFLegacy.Launcher.exe"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("只能更新当前登录器所在目录。");
        var state=ReadState();
        if(state!=null && update.Release.Id<=state.ReleaseId) throw new InvalidDataException("不能安装旧版本。");
        var hash=UpdateSafety.Hash(executable);
        var helper=CreateHelperCopy(executable,hash);
        try
        {
            // Preparation owns the copy until a complete handoff is returned to the caller.
            using var helperPin=UpdateSafety.PinDirectory(Path.GetDirectoryName(helper)!);
            var snapshot=UpdateSafety.ReadJson<PreparedUpdate>(JsonSerializer.SerializeToUtf8Bytes(update,UpdateSafety.Json));
            return new UpdateHandoff(new(target,snapshot,parent.Id,parent.StartTime.ToUniversalTime().Ticks,executable,helper,hash));
        }
        catch { UpdateArtifacts.DeleteUnstartedHelper(helper); throw; }
    }
    // Internal allocation boundary shared with real-file acquisition regression tests.
    internal static string CreateHelperCopy(string executable,string expectedHash)
    {
        var directory=Path.Combine(Path.GetTempPath(),"dflegacy-helper-"+Guid.NewGuid().ToString("N"));
        UpdateSafety.NoLinks(directory); Directory.CreateDirectory(directory);
        var helper=Path.Combine(directory,"DFLegacy.Launcher.exe");
        try
        {
            using var pin=UpdateSafety.PinDirectory(directory);
            File.Copy(executable,helper,false);
            if(!UpdateSafety.Hash(helper).Equals(expectedHash,StringComparison.Ordinal)) throw new IOException("临时安装助手复制校验失败。");
            return helper;
        }
        catch { UpdateArtifacts.DeleteUnstartedHelper(helper); throw; }
    }
    public async Task<UpdateHelperHandle> StartHelperAsync(UpdateHandoff handoff,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan=handoff.Plan;
        if(!plan.Target.Equals(target,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新目标不匹配。");
        using var parent=Process.GetCurrentProcess();
        ValidateParent(plan,parent.Id);
        using var helperPin=UpdateSafety.PinDirectory(Path.GetDirectoryName(plan.HelperPath)!);
        using var helperFile=new FileStream(plan.HelperPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        ValidateHelperPath(plan);
        UpdateTransaction.ValidateStage(plan.Update);
        var pipeName="dflegacy-update-"+Guid.NewGuid().ToString("N");
        using var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var info=new ProcessStartInfo(plan.HelperPath) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(plan.HelperPath)! };
        info.ArgumentList.Add("--dflegacy-apply-update"); info.ArgumentList.Add(pipeName);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var child=Process.Start(info) ?? throw new IOException("无法启动更新助手。");
        await server.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
        if(!GetNamedPipeClientProcessId(server.SafePipeHandle,out var clientId) || clientId!=(uint)child.Id) throw new InvalidDataException("更新助手身份不匹配。");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(plan,UpdateSafety.Json);
        if(bytes.Length>65536) throw new InvalidDataException("更新交接过大。");
        await server.WriteAsync(BitConverter.GetBytes(bytes.Length),timeout.Token).ConfigureAwait(false);
        await server.WriteAsync(bytes,timeout.Token).ConfigureAwait(false); await server.FlushAsync(timeout.Token).ConfigureAwait(false);
        var ready=new byte[1]; await server.ReadExactlyAsync(ready,timeout.Token).ConfigureAwait(false);
        if(ready[0]!=1) throw new InvalidDataException("更新助手拒绝交接。");
        cancellationToken.ThrowIfCancellationRequested();
        await server.WriteAsync(new byte[]{1},timeout.Token).ConfigureAwait(false); await server.FlushAsync(timeout.Token).ConfigureAwait(false);
        // Caller must close the launcher only after this readiness acknowledgement.
        return new(child.Id);
    }
    private static void ValidateParent(UpdatePlan plan,int pipeServerId)
    {
        if(plan.ParentId!=pipeServerId) throw new InvalidDataException("更新父进程不匹配。");
        using var parent=Process.GetProcessById(plan.ParentId);
        if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStartTicks || parent.MainModule?.FileName is not { } actual ||
           !Path.GetFullPath(actual).Equals(plan.ParentPath,StringComparison.OrdinalIgnoreCase) ||
           !plan.ParentPath.Equals(Path.Combine(Path.GetFullPath(plan.Target),"DFLegacy.Launcher.exe"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新父进程身份无效。");
        UpdateSafety.NoLinks(plan.ParentPath);
        if(!UpdateSafety.Hash(plan.ParentPath).Equals(plan.ExecutableHash,StringComparison.Ordinal)) throw new InvalidDataException("原登录器文件已改变。");
    }
    private static void ValidateHelperPath(UpdatePlan plan)
    {
        var directory=Path.GetDirectoryName(plan.HelperPath)!;
        var name=Path.GetFileName(directory);
        if(!name.StartsWith("dflegacy-helper-",StringComparison.Ordinal) || !Guid.TryParseExact(name[16..],"N",out _) ||
           !Path.GetDirectoryName(directory)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) ||
           Path.GetFileName(plan.HelperPath)!="DFLegacy.Launcher.exe") throw new InvalidDataException("临时助手路径无效。");
        UpdateSafety.NoLinks(plan.HelperPath);
        if(!UpdateSafety.Hash(plan.HelperPath).Equals(plan.ExecutableHash,StringComparison.Ordinal)) throw new InvalidDataException("临时助手文件已改变。");
    }
    public static async Task<int> RunHelperAsync(string[] args)
        => (await RunHelperWithOutcomeAsync(args).ConfigureAwait(false)).ExitCode;
    internal static async Task<UpdateHelperOutcome> RunHelperWithOutcomeAsync(string[] args)
    {
        if(args.Length!=2 || args[0]!="--dflegacy-apply-update" || !args[1].StartsWith("dflegacy-update-",StringComparison.Ordinal) || !Guid.TryParseExact(args[1][16..],"N",out _)) return new(2);
        UpdatePlan? plan=null; bool parentExited=false;
        try
        {
            using var pipe=new NamedPipeClientStream(".",args[1],PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var parentId)) return new(2);
            var length=new byte[4]; await pipe.ReadExactlyAsync(length,timeout.Token).ConfigureAwait(false);
            var size=BitConverter.ToInt32(length); if(size<=0 || size>65536) return new(2);
            var bytes=new byte[size]; await pipe.ReadExactlyAsync(bytes,timeout.Token).ConfigureAwait(false);
            plan=UpdateSafety.ReadJson<UpdatePlan>(bytes);
            if(string.IsNullOrWhiteSpace(plan.Target) || string.IsNullOrWhiteSpace(plan.ParentPath) || string.IsNullOrWhiteSpace(plan.HelperPath) ||
               plan.Update is null || plan.Update.Release is null || string.IsNullOrWhiteSpace(plan.Update.Directory) || !UpdateSafety.IsHash(plan.ExecutableHash))
                throw new InvalidDataException("更新交接缺少字段。");
            ValidateParent(plan,checked((int)parentId));
            if(!Path.GetFullPath(Environment.ProcessPath!).Equals(plan.HelperPath,StringComparison.OrdinalIgnoreCase)) return new(2);
            ValidateHelperPath(plan);
            using var targetPin=UpdateSafety.PinDirectory(plan.Target);
            using var stagePin=UpdateSafety.PinDirectory(plan.Update.Directory);
            UpdateTransaction.ValidateStage(plan.Update);
            using var parent=Process.GetProcessById(plan.ParentId);
            // Hold the actual parent process handle, preventing PID reuse after readiness.
            _=parent.SafeHandle;
            await pipe.WriteAsync(new byte[]{1},timeout.Token).ConfigureAwait(false); await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            var commit=new byte[1]; await pipe.ReadExactlyAsync(commit,timeout.Token).ConfigureAwait(false); if(commit[0]!=1) return new(2);
            using var exitTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await parent.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
            parentExited=true;
            if(!UpdateSafety.Hash(plan.ParentPath).Equals(plan.ExecutableHash,StringComparison.Ordinal)) throw new InvalidDataException("安装目标在交接后已改变。");
            var result=UpdateTransaction.Apply(plan.Target,plan.Update,Restart);
            SaveResult(plan.Target,result);
            return new(result.Success && result.Restarted?0:1,result,plan.Target);
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Only an authenticated, committed handoff whose parent has exited
            // owns the user's next action. Rejected/abandoned handoffs must not
            // leave an orphan modal while the original launcher is still alive.
            if(parentExited && plan!=null)
            {
                var result=new UpdateApplyResult(false,false,"更新未完成，安装助手未能完成替换。请重新检查更新。\n"+ex.Message);
                SaveResult(plan.Target,result);
                return new(1,result,plan.Target);
            }
            return new(1);
        }
    }
    private static void SaveResult(string target,UpdateApplyResult result)
    {
        try
        {
            using var pin=UpdateSafety.PinDirectory(target);
            var directory=Path.Combine(target,".dflegacy"); UpdateSafety.NoLinks(directory); Directory.CreateDirectory(directory);
            using var statePin=UpdateSafety.PinDirectory(directory);
            UpdateTransaction.WriteJson(Path.Combine(directory,"last-result.json"),result);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException) {}
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint id);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint id);
}
