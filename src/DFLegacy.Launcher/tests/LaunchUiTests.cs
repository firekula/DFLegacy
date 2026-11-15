using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher.Views;

internal static class LaunchUiTests
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle,uint command);

    internal static void Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"dflegacy-launch-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target=Path.Combine(root,"ijl15.dll"); var candidate=Path.Combine(root,"ijl15.dll.bak");
        File.WriteAllText(target,"synthetic old patch"); File.WriteAllText(candidate,"synthetic original candidate");
        var before=Directory.GetFiles(root).ToDictionary(p=>p,File.ReadAllBytes);
        try
        {
            foreach(var scenario in new[]{"cancel","confirm","startup-error","no-migration"})
            {
                var main=new MainWindow(new LauncherAccountStore(Path.Combine(root,"launcher.json")),refreshStatus:false)
                    {WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowActivated=false};
                var closed=false; main.Closed+=(_,_)=>closed=true; main.Show();
                var consentCount=0; var errorCount=0;
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(15)};
                timer.Tick+=(_,_)=>
                {
                    var dialog=Application.Current.Windows.OfType<ClassicMessageWindow>().FirstOrDefault(w=>w.IsVisible);
                    if(dialog is null) return;
                    var confirm=(SkinButton)dialog.FindName("ConfirmButton");
                    if(!confirm.IsCancel)
                    {
                        consentCount++;
                        var ownerHandle=new WindowInteropHelper(main).Handle;
                        check(main.IsVisible && IsWindowVisible(ownerHandle),"migration keeps a visibly reachable launcher owner: "+scenario);
                        check(main.ShowInTaskbar,"visible launcher retains taskbar representation during consent");
                        check(dialog.Owner==main && GetWindow(new WindowInteropHelper(dialog).Handle,4)==ownerHandle,"migration has both WPF and native launcher ownership");
                        check(!dialog.Accepted && before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && Directory.GetFiles(root).Length==before.Count,"no consent or original-file writes occur before explicit action");
                        if(scenario=="cancel") dialog.Close();
                        else confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                    else
                    {
                        errorCount++;
                        check(main.IsVisible && dialog.Owner==main,"cancel/startup error is presented over visible login");
                        dialog.Close();
                    }
                };
                timer.Start();
                try
                {
                    var operation=main.RunLaunchUiAsync(async (confirm,warning)=>
                    {
                        await Task.Yield();
                        check(!main.IsVisible,"ordinary preflight hide behavior is preserved");
                        if(scenario!="no-migration")
                        {
                            var accepted=confirm(new(target,"synthetic",candidate,IjlMigrationState.RestoreRequired));
                            check(accepted==(scenario!="cancel"),"only explicit confirmation permits migration");
                            check(!Application.Current.Windows.OfType<ClassicMessageWindow>().Any(w=>w.IsVisible),"consent dialog closes before startup continues");
                            if(!accepted) throw new InvalidDataException("Synthetic cancellation: game not started.");
                            check(!main.IsVisible,"launcher hides after confirmed consent and before continued startup");
                        }
                        if(scenario=="startup-error") throw new IOException("Synthetic startup error.");
                    });
                    var frame=new DispatcherFrame();
                    operation.ContinueWith(_=>main.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
                    Dispatcher.PushFrame(frame); operation.GetAwaiter().GetResult();
                    var failed=scenario is "cancel" or "startup-error";
                    check(failed ? main.IsVisible && !closed && main.IsEnabled : closed,"launch completion/cancellation/error restores correct login lifetime: "+scenario);
                    check(consentCount==(scenario=="no-migration"?0:1) && errorCount==(failed?1:0),"one consent and expected error path only: "+scenario);
                    check(before.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)) && Directory.GetFiles(root).Length==before.Count,"UI lifecycle never writes original fixtures");
                }
                finally { timer.Stop(); if(!closed) main.Close(); }
            }
        }
        finally { Directory.Delete(root,true); }
        Console.WriteLine("Launch migration ownership/cancel/confirm/error lifecycle checks executed");
    }
}
