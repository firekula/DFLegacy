using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DFLegacy.Launcher;

internal static class ClientBootstrapLauncher
{
    internal static Task<Process> StartAsync(string clientPath,string launcherData,ClientPackageManifest manifest,CancellationToken cancellationToken,string? paymentUrl=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClientFileIdentity.RequireSameDirectory(AppContext.BaseDirectory,Path.GetDirectoryName(clientPath)!);
        ClientFileIdentity.RequireSameDirectory(manifest.DirectoryPath,Path.GetDirectoryName(clientPath)!);
        var helper=StandaloneClientInstaller.EnsureHelper(manifest.DirectoryPath);
        return StartCoreAsync(clientPath,launcherData,manifest,cancellationToken,helper,TimeSpan.FromSeconds(25),verifyEmbedded:true,paymentUrl:paymentUrl);
    }
    // Internal seam for isolated process fixtures. Production always uses the embedded helper and one installation root.
    internal static async Task<Process> StartCoreAsync(string clientPath,string launcherData,ClientPackageManifest manifest,CancellationToken cancellationToken,
        string helperPath,TimeSpan timeout,Action<string>? readyPath=null,Func<string,ProcessStartInfo>? fixtureStart=null,bool verifyEmbedded=false,string? paymentUrl=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locks=new List<FileStream>(); Process? helper=null; Process? child=null; var transferred=false; var childValidated=false;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout);
        try
        {
            FileStream Hold(string path) { var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read); locks.Add(stream); return stream; }
            var exe=Hold(clientPath); var patch=Hold(Path.Combine(manifest.DirectoryPath,"DFLegacy.Client.dll"));
            Hold(Path.Combine(manifest.DirectoryPath,"client-package.json"));
            foreach(var file in manifest.Files) Hold(Path.Combine(manifest.DirectoryPath,file.Path));
            Hold(helperPath); if(verifyEmbedded) StandaloneClientInstaller.ValidateHelper(helperPath);
            var current=ClientPackageManifest.LoadAndValidate(manifest.DirectoryPath);
            if(current.Version!=manifest.Version || current.Commit!=manifest.Commit || current.Flavor!=manifest.Flavor || !current.Files.SequenceEqual(manifest.Files)) throw Failure("hash_mismatch");
            var nonce=Guid.NewGuid().ToString("N"); var patchPath=ClientFileIdentity.Canonical(patch.SafeFileHandle);
            var start=fixtureStart?.Invoke(nonce) ?? new ProcessStartInfo(helperPath);
            start.UseShellExecute=false; start.CreateNoWindow=true; start.RedirectStandardInput=true; start.RedirectStandardOutput=true;
            start.StandardInputEncoding=new UTF8Encoding(false,true); start.StandardOutputEncoding=new UTF8Encoding(false,true);
            start.WorkingDirectory=Path.GetDirectoryName(clientPath)!;
            if(paymentUrl is not null) start.Environment["DFLEGACY_LOCAL_PAYMENT_URL"]=paymentUrl;
            if(fixtureStart is null)
                foreach(var argument in new[]{"--client",ClientFileIdentity.Canonical(exe.SafeFileHandle),"--patch",patchPath,"--sha256",manifest.Files.Single(f=>f.Path=="DFLegacy.Client.dll").Sha256,"--mode",manifest.Flavor,"--nonce",nonce,"--",launcherData}) start.ArgumentList.Add(argument);
            helper=Process.Start(start) ?? throw Failure("create_failed");
            var line=await ReadLineAsync(helper.StandardOutput.BaseStream,deadline.Token);
            if (line is null)
            {
                // EOF can occur before the native helper's entry point runs.
                // Read the process status under the same startup deadline.
                await helper.WaitForExitAsync(deadline.Token);
                throw Failure("bootstrap_no_reply",helper.ExitCode);
            }
            using var json=JsonDocument.Parse(new UTF8Encoding(false,true).GetString(line),new JsonDocumentOptions{MaxDepth=3});
            var root=json.RootElement;
            if(root.GetProperty("event").GetString()=="error")
            {
                ClientPackageManifest.ExactKeys(root,"schemaVersion","event","nonce","code","message");
                if(root.GetProperty("schemaVersion").GetInt32()!=1 || root.GetProperty("nonce").GetString()!=nonce) throw Failure("invalid_protocol");
                throw Failure(root.GetProperty("code").GetString());
            }
            ClientPackageManifest.ExactKeys(root,"schemaVersion","event","nonce","pid","createdFileTime","patchPath");
            if(root.GetProperty("schemaVersion").GetInt32()!=1 || root.GetProperty("event").GetString()!="ready" || root.GetProperty("nonce").GetString()!=nonce) throw Failure("invalid_protocol");
            var pid=root.GetProperty("pid").GetInt32(); var returnedPath=root.GetProperty("patchPath").GetString();
            if(pid<=0 || !ulong.TryParse(root.GetProperty("createdFileTime").GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out var created)
                || returnedPath is null || !Path.IsPathFullyQualified(returnedPath)) throw Failure("child_identity");
            // Reject an unexpected returned path before opening it (including UNC/device paths).
            if(!string.Equals(returnedPath,patchPath,StringComparison.OrdinalIgnoreCase)) throw Failure("module_identity");
            using(var loaded=File.Open(returnedPath,FileMode.Open,FileAccess.Read,FileShare.Read))
                if(ClientFileIdentity.Of(loaded.SafeFileHandle)!=ClientFileIdentity.Of(patch.SafeFileHandle) || !string.Equals(ClientFileIdentity.Canonical(loaded.SafeFileHandle),patchPath,StringComparison.OrdinalIgnoreCase)) throw Failure("module_identity");
            // Retain the actual process handle before inspecting it; later PID reuse cannot redirect operations.
            child=Process.GetProcessById(pid); _=child.SafeHandle;
            if(!ClientFileIdentity.GetProcessTimes(child.SafeHandle,out var actualCreated,out _,out _,out _) || (ulong)actualCreated!=created || child.HasExited) throw Failure("child_identity");
            using(var image=File.Open(child.MainModule!.FileName,FileMode.Open,FileAccess.Read,FileShare.Read))
                if(ClientFileIdentity.Of(image.SafeFileHandle)!=ClientFileIdentity.Of(exe.SafeFileHandle)) throw Failure("child_identity");
            childValidated=true;
            readyPath?.Invoke(returnedPath);
            await helper.StandardInput.WriteAsync(("commit "+nonce+"\n").AsMemory(),deadline.Token); await helper.StandardInput.FlushAsync(deadline.Token);
            await helper.WaitForExitAsync(deadline.Token);
            if(helper.ExitCode!=0 || (await helper.StandardOutput.BaseStream.ReadAsync(new byte[1],deadline.Token))!=0 || child.HasExited) throw Failure("handshake_failed");
            cancellationToken.ThrowIfCancellationRequested(); transferred=true; return child;
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested) { throw Failure("initialization_timeout"); }
        catch(InvalidDataException ex) when(!ex.Data.Contains("BootstrapCode")) { throw Failure("invalid_protocol"); }
        catch(Exception ex) when(ex is JsonException or DecoderFallbackException or KeyNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or FormatException or OverflowException)
        { throw Failure("invalid_protocol"); }
        finally
        {
            if(helper is not null)
            {
                if(!transferred)
                {
                    try { helper.StandardInput.Close(); if(!helper.HasExited) helper.Kill(); await helper.WaitForExitAsync(CancellationToken.None); } catch(InvalidOperationException) { }
                }
                helper.Dispose();
            }
            // Before commit, only the helper's retained job owns termination. Never kill an unverified PID from a bad reply.
            if(!transferred && child is not null)
            {
                if(childValidated) { try { if(!child.HasExited) { child.Kill(); await child.WaitForExitAsync(CancellationToken.None); } } catch(InvalidOperationException) { } }
                child.Dispose();
            }
            foreach(var stream in locks) stream.Dispose();
        }
    }
    private static async Task<byte[]?> ReadLineAsync(Stream stream,CancellationToken token)
    {
        using var line=new MemoryStream(); var one=new byte[1];
        for(var i=0;i<8192;i++)
        {
            if (await stream.ReadAsync(one,token) != 1)
            {
                if (line.Length == 0) return null;
                throw Failure("invalid_protocol");
            }
            if(one[0]==10) return line.ToArray(); if(one[0]==13) throw Failure("invalid_protocol"); line.WriteByte(one[0]);
        }
        throw Failure("invalid_protocol");
    }
    internal static InvalidDataException Failure(string? code, int? helperExitCode = null)
    {
        var error = new InvalidDataException(code switch
        {
        "bootstrap_no_reply" when helperExitCode is int exitCode => $"独立补丁引导程序在回报启动结果前退出（退出码 0x{unchecked((uint)exitCode):X8}）。",
        "initialization_timeout" => "独立补丁初始化超时，游戏尚未启动。请检查客户端版本及系统依赖后重试。",
        "initialization_failed" or "invalid_image" => "独立补丁不支持此客户端映像。请使用同版本兼容展开的 32 位客户端（"+code+"）。",
        "hash_mismatch" or "module_identity" or "file_identity" => "独立补丁文件校验失败。请重新解压同一版本的完整工具包。",
        _ => "独立补丁启动握手失败，游戏尚未交接。请检查客户端文件后重试（" + (KnownCode(code)?code:"invalid_protocol") + "）。"
        });
        error.Data["BootstrapCode"] = code;
        if (helperExitCode.HasValue) error.Data["BootstrapExitCode"] = helperExitCode.Value;
        return error;
    }
    private static bool KnownCode(string? code) => code is "invalid_arguments" or "invalid_protocol" or "file_unavailable" or "patch_location" or "handshake_failed" or "ownership_failed" or "create_failed" or "injection_failed" or "parent_disconnected" or "child_identity" or "bootstrap_failed";
}
