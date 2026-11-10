using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher.Updates;
using DFLegacy.Launcher.Views;

internal static class UpdateStartupFlowTests
{
    internal static void RunOnSta(Action<bool,string> check, bool presentationOnly=false)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                if(presentationOnly) CheckProgressPresentation(check); else Run(check);
            }
            catch(Exception ex) { failure=ex; }
            finally { app.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) throw new Exception("Compact startup tests failed",failure);
    }
    private static void Wait(Task task)
    {
        var frame=new DispatcherFrame();
        var dispatcher=Dispatcher.CurrentDispatcher;
        var timeout=new DispatcherTimer(TimeSpan.FromSeconds(15),DispatcherPriority.Send,(_,_)=>frame.Continue=false,dispatcher);
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
        Dispatcher.PushFrame(frame); timeout.Stop();
        if(!task.IsCompleted) throw new TimeoutException("Automatic update flow did not finish.");
        task.GetAwaiter().GetResult();
        dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    }

    internal static void Run(Action<bool,string> check)
    {
        CheckProgressPresentation(check);
        var strip=new OriginalProgressStrip {Width=258,Height=8};
        strip.Measure(new Size(258,8)); strip.Arrange(new Rect(0,0,258,8)); strip.Set(.5,true); strip.UpdateLayout();
        var fill=new RenderTargetBitmap(258,8,96,96,PixelFormats.Pbgra32); fill.Render(strip);
        var pixels=new byte[258*8*4]; fill.CopyPixels(pixels,258*4,0);
        var columns=Enumerable.Range(0,258).Where(x=>Enumerable.Range(0,8).Any(y=>pixels[(y*258+x)*4+3]!=0)).ToArray();
        check(columns.Length>0 && columns[0]==0 && columns[^1] is >=127 and <=129,"pending progress remains a measured left-to-right half fill rather than a moving quarter strip");
        var root=Path.Combine(Path.GetTempPath(),"dflegacy-startup-flow-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var package=Task.Run(()=>UpdateInstallerTests.Prepare(root)).GetAwaiter().GetResult();
        var release=package.Release;
        Window Owner()=>new() { Width=100,Height=100,ShowInTaskbar=false,ShowActivated=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual };
        try
        {
            Wait(BootstrapUpdateTests.RunWindowPolicyAsync(root,check));
            var validatedRoot=Path.Combine(root,"validated-background");Directory.CreateDirectory(validatedRoot);
            var bytes="validated resource"u8.ToArray();var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            var source=new DFLegacy.UpdateCore.ReleaseSource("gitee","firekula/DFLegacy","gitee-test","x64","normal");
            var manifest=new DFLegacy.UpdateCore.ClientManifest(4,"v4-test",new string('a',40),"x64","normal","gitee-test",1,0,
                [new("resource.bin",bytes.Length,hash,[new(hash,bytes.Length,"v4-test",hash+".bin")])],[],[]);
            var raw=DFLegacy.UpdateCore.ManifestCodec.Serialize(manifest);
            var verified=new DFLegacy.UpdateCore.VerifiedRelease(source,manifest.Version,Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(raw)),raw);
            var cache=new DFLegacy.UpdateCore.ContentCache(Path.Combine(validatedRoot,".dflegacy/v4/cache"));File.WriteAllBytes(cache.ObjectPath(hash,bytes.Length),bytes);
            DFLegacy.UpdateCore.InstallTransaction.Apply(validatedRoot,new(verified,cache.DirectoryPath));
            CheckBootstrapHandoff(validatedRoot,package,cache.ObjectPath(hash,bytes.Length),Owner,check);
            {
                var owner=Owner();var notices=new List<string>();
                var feed=new TaskCompletionSource<UpdateRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var session=new UpdateControllerTests.Session {Feed=_=>feed.Task};
                using var flow=new UpdateCoordinator(owner,validatedRoot,session,()=>check(false,"background current check cannot shut down login"),notices.Add);
                var pending=flow.StartAsync();
                check(owner.IsVisible,"completed receipt opens login while release discovery is still pending");
                check(!Application.Current.Windows.OfType<UpdateInstallWindow>().Any(w=>w.IsVisible),"background discovery does not open a verification window");
                feed.SetException(new IOException("freshness unavailable"));Wait(pending);
                check(owner.IsVisible&&notices.Count==0,"background freshness failure is nonmodal and leaves login open");
                owner.Close();
            }
            foreach(var error in new[]{false,true})
            {
                var owner=Owner(); var session=new UpdateControllerTests.Session(); var warnings=new List<string>();
                var feed=new TaskCompletionSource<UpdateRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
                session.Feed=_=>feed.Task;
                using var flow=new UpdateCoordinator(owner,root,session,()=>check(false,"no update/error cannot shut down login"),warnings.Add);
                var pending=flow.StartAsync();
                check(!owner.IsVisible,"login stays hidden until startup check completes");
                check(!Application.Current.Windows.OfType<UpdateWindow>().Any(w=>w.IsVisible),"startup never opens the full update page");
                if(error) feed.SetException(new IOException("offline fixture")); else feed.SetResult(null);
                Wait(pending);
                check(owner.IsVisible && !Application.Current.Windows.OfType<UpdateInstallWindow>().Any(w=>w.IsVisible),"no update or failed check goes directly to login without a second window");
                check(warnings.Count==(error?1:0),"network failure is explained once; current version is silent");
                owner.Close();
            }
            foreach(var mode in new[]{"success","occupied","cancel","recovery-decline","recovery-accept"})
            {
                var owner=Owner(); owner.Show();
                var session=new UpdateControllerTests.Session { Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(release) };
                var warnings=new List<string>(); var shutdowns=0;
                var helperDirectory=Path.Combine(Path.GetTempPath(),"dflegacy-helper-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(helperDirectory);
                var helper=Path.Combine(helperDirectory,"DFLegacy.Launcher.exe"); File.WriteAllText(helper,"owned fixture");
                session.Prepare=(p,_)=> mode=="occupied"?throw new IOException("file in use"):Task.FromResult(new UpdateHandoff(new UpdatePlan(root,p,0,0,"",helper,"")));
                session.Start=(_,_)=>Task.FromResult(new UpdateHelperHandle(int.MaxValue));
                session.Download=async (stage,progress,token)=>
                {
                    progress.Report(new(50,100,10));
                    await owner.Dispatcher.InvokeAsync(()=>
                    {
                        check(!owner.IsVisible,"login is hidden while automatic download is active");
                        var compact=Application.Current.Windows.OfType<UpdateInstallWindow>().Where(w=>w.IsVisible).ToArray();
                        check(compact.Length==1 && !Application.Current.Windows.OfType<UpdateWindow>().Any(w=>w.IsVisible),"available update uses exactly the native compact window");
                        if(compact.Length==1)
                        {
                            compact[0].UpdateLayout();
                            check(Math.Abs(((OriginalProgressStrip)compact[0].FindName("InstallProgress")).FilledWidth-258*.4)<1,"compact download fill follows actual received bytes");
                            if(mode=="cancel") compact[0].Close();
                        }
                    },DispatcherPriority.Background);
                    await Task.Delay(10,token);
                    var destination=Path.Combine(stage,"dflegacy-update-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(destination);
                    foreach(var file in Directory.GetFiles(package.Directory)) File.Copy(file,Path.Combine(destination,Path.GetFileName(file)));
                    return package with {Directory=destination};
                };
                if(mode.StartsWith("recovery")) session.Recovery=new("fixture","hash","old",new("DFLegacy.Launcher.exe","hash",1),new("DFLegacy.Launcher.exe","hash",1));
                session.PrepareRecovery=(_,_)=>Task.FromResult(new UpdateHandoff(new UpdatePlan(root,null,0,0,"",helper,"",session.Recovery)));
                using(var flow=new UpdateCoordinator(owner,root,session,()=>shutdowns++,warnings.Add,_=>mode=="recovery-accept"))
                {
                    Wait(flow.StartAsync());
                    var accepted=mode is "success" or "recovery-accept";
                    check(shutdowns==(accepted?1:0) && session.Starts==(accepted?1:0),"automatic handoff starts once only when allowed: "+mode);
                    if(!accepted) check(owner.IsVisible,"failed/deferred update restores login: "+mode);
                    check(warnings.Count==(mode=="occupied"?1:0),"occupied file is reported without claiming install success: "+mode);
                    if(mode.StartsWith("recovery")) check(session.Downloads==0,"pending recovery is resolved before network/download");
                }
                owner.Close();
                UpdateArtifacts.Sweep(root);
                if(Directory.Exists(helperDirectory)) Directory.Delete(helperDirectory,true);
            }
        }
        finally { Directory.Delete(root,true); }
        Console.WriteLine("Automatic compact startup flow checks executed");
    }

    private static void CheckProgressPresentation(Action<bool,string> check)
    {
        var window=new UpdateInstallWindow();
        try
        {
            foreach(var (phase,label) in new[]{("verify","完整校验"),("download","下载"),("retry","等待重试"),
                ("prepared","准备安装"),("install","安装"),("recover","恢复"),("commit","保存更新记录"),("future-phase","处理更新")})
            {
                UpdateProgress? forwarded=null;
                V4UpdateSession.Forward(new CaptureProgress(p=>forwarded=p)).Report(new(phase,"DFLegacy.Client.dll",1,8,Detail:"请勿退出。"));
                check(forwarded?.Detail?.StartsWith(label+" 1/8",StringComparison.Ordinal)==true,"update phase has a Chinese display prefix: "+phase);
                check(forwarded?.Phase==phase,"translated display keeps the engine phase intact: "+phase);
            }
            UpdateProgress? empty=null;
            V4UpdateSession.Forward(new CaptureProgress(p=>empty=p)).Report(new("verify","",0,0));
            check(empty?.Detail=="完整校验","unknown file totals do not display 0/0 or an empty colon");
            var original="安装 0/8：正在提交修复；此阶段请勿退出。\r\n"+new string('长',180)+".npk";
            window.Render(new UpdateInstallProgress(.85,original));
            var status=window.FindName("InstallStatus") as TextBlock;
            check(status is not null,"compact update status supports single-line ellipsis");
            if(status is not null)
            {
                check(status.TextWrapping==TextWrapping.NoWrap&&status.TextTrimming==TextTrimming.CharacterEllipsis,
                    "long update status remains one line with visible truncation");
                check(!status.Text.Contains('\r')&&!status.Text.Contains('\n'),"embedded newlines cannot create an orphaned second line");
                check((string?)status.ToolTip==original,"full installation details remain available without truncation");
                var panel=(FrameworkElement)window.Content;
                panel.Measure(new Size(286,104)); panel.Arrange(new Rect(0,0,286,104)); panel.UpdateLayout();
                check(status.ActualWidth==249&&status.ActualHeight<=24,"long file names stay within the compact status area");
                window.Render(new UpdateViewState(UpdatePhase.Downloading,original,
                    Progress:new UpdateProgress(50,100,10,"download",Detail:original)));
                check(!status.Text.Contains('\n')&&(string?)status.ToolTip==original,"download progress uses the same single-line treatment");
                var images=Path.Combine(AppContext.BaseDirectory,"update-text-preview"); Directory.CreateDirectory(images);
                foreach(var (name,progress) in new[]{
                    ("install",new DFLegacy.UpdateCore.UpdateProgress("install","",0,8,Detail:"请勿退出。")),
                    ("download-long",new DFLegacy.UpdateCore.UpdateProgress("download","ImagePacks2/"+new string('长',80)+".npk",2,8)),
                    ("recover",new DFLegacy.UpdateCore.UpdateProgress("recover","DFLegacy.Client.dll",3,8))})
                {
                    window.Render(new UpdateInstallProgress(.85,UpdateProgressText.Format(progress)));
                    panel.UpdateLayout();
                    var bitmap=new RenderTargetBitmap(286,104,96,96,PixelFormats.Pbgra32); bitmap.Render(panel);
                    var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output=File.Create(Path.Combine(images,name+".png")); encoder.Save(output);
                }
            }
        }
        finally { window.Finish(); }
    }

    private sealed class CaptureProgress(Action<UpdateProgress> capture):IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value)=>capture(value);
    }

    private static void CheckBootstrapHandoff(string root,PreparedUpdate package,string cachedObject,Func<Window> createOwner,Action<bool,string> check)
    {
        foreach(var mode in new[]{"ready","current","cancel-download","locks-declined","locks-error","cancel-inspection","close-inspection","restart-fails","recovery-decline","recovery-accept","unverified"})
        {
            var owner=createOwner();var notices=new List<string>();var handoffs=0;var inspections=0;var confirmations=0;var prepares=0;
            var session=new UpdateControllerTests.Session {Package=package,Feed=_=>Task.FromResult<UpdateRelease?>(mode=="current"?null:package.Release)};
            session.Prepare=(_,_)=>{prepares++;throw new InvalidDataException("the App process is not the legacy Launcher.exe installer");};
            session.PrepareRecovery=(_,_)=>{prepares++;throw new InvalidDataException("the App process is not the legacy recovery installer");};
            session.InspectLocks=async _=>
            {
                inspections++;
                if(mode=="locks-error")throw new IOException("inspection failed");
                if(mode is "cancel-inspection" or "close-inspection")
                    await owner.Dispatcher.InvokeAsync(()=>
                    {
                        if(mode=="close-inspection")owner.Close();
                        else Application.Current.Windows.OfType<UpdateInstallWindow>().Single(w=>w.IsVisible).Close();
                    });
                return mode=="locks-declined"?[new FileLockOwner(1,1,"unrelated fixture",false)]:[];
            };
            if(mode=="cancel-download")session.Download=async(_,_,token)=>
            {
                await owner.Dispatcher.InvokeAsync(()=>Application.Current.Windows.OfType<UpdateInstallWindow>().Single(w=>w.IsVisible).Close(),DispatcherPriority.Background);
                token.ThrowIfCancellationRequested();throw new InvalidOperationException("cancel must stop download");
            };
            if(mode.StartsWith("recovery"))session.Recovery=new("fixture","hash","old",new("DFLegacy.Launcher.exe","hash",1),new("DFLegacy.Launcher.exe","hash",1),true);
            using(var flow=new UpdateCoordinator(owner,root,session,()=>check(false,"bootstrap App must not start a legacy helper"),notices.Add,
                _=>mode=="recovery-accept",()=>mode!="unverified",()=>
                {
                    handoffs++;
                    check(inspections==1,"automatic bootstrap handoff occurs only after file-lock inspection: "+mode);
                    if(mode=="restart-fails")throw new IOException("fixed entry restart failed");
                },(_,_)=>{confirmations++;return false;}))
            {
                Wait(flow.StartAsync());
                var handedOff=mode is "ready" or "recovery-accept";
                check(handoffs==(handedOff||mode=="restart-fails"?1:0),"verified App automatically hands Ready updates to the fixed entry only when permitted: "+mode);
                check(prepares==(mode=="unverified"?1:0)&&session.Starts==0,"bootstrap handoff never enters the Launcher.exe-only installer: "+mode);
                check(File.Exists(cachedObject),"bootstrap handoff preserves the installation's downloaded V4 cache: "+mode);
                if(handedOff)
                {
                    flow.Check();Wait(flow.StartAsync());
                    check(handoffs==1&&inspections==1&&session.Checks==(mode=="recovery-accept"?0:1),"repeat coordinator entry cannot launch a second fixed bootstrap: "+mode);
                    check(!owner.IsVisible,"successful bootstrap handoff does not reopen the old login: "+mode);
                }
                else if(mode!="close-inspection")check(owner.IsVisible,"deferred or failed bootstrap handoff returns to login: "+mode);
                if(mode=="locks-declined")check(confirmations==1&&prepares==0,"declining occupied files prevents both helper and bootstrap startup");
                if(mode=="restart-fails")check(notices.Count==1&&notices[0].Contains("fixed entry restart failed"),"bootstrap startup failure is reported once without a fallback installer");
            }
            if(mode!="close-inspection")owner.Close();
            UpdateArtifacts.Sweep(root);
        }
    }
}
