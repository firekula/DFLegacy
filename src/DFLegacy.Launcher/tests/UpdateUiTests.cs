using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DFLegacy.Launcher.Controls;
using DFLegacy.Launcher.Views;
using DFLegacy.Launcher.Updates;
internal static class UpdateUiTests
{
    internal static void Run(Action<bool,string> check)
    {
        using var session=new UpdateSession(AppContext.BaseDirectory,new DFLegacy.Launcher.LauncherOptions());
        using var controller=new UpdateController(session,AppContext.BaseDirectory,()=>{});
        var window=new UpdateWindow(controller) { ShowInTaskbar=false,ShowActivated=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual };
        try
        {
            window.Show(); window.Render(new(UpdatePhase.Downloading,"正在下载", "dev-test",new(50,200,100))); window.UpdateLayout();
            check(window.ActualWidth==567 && window.ActualHeight==512,"update page keeps original native 567x512 geometry");
            var back=window.FindName("BackButton") as SkinButton; var retry=window.FindName("RetryButton") as SkinButton; var install=window.FindName("InstallButton") as SkinButton;
            check(back!=null && retry!=null && install!=null,"update page has functional back retry and explicit restart controls");
            bool Within(DependencyObject? hit,DependencyObject target)
            {
                for(var current=hit;current!=null;current=VisualTreeHelper.GetParent(current)) if(ReferenceEquals(current,target)) return true;
                return false;
            }
            window.Render(new(UpdatePhase.Error,"命中测试")); window.UpdateLayout();
            check(retry!=null && !Within(window.InputHitTest(new Point(285,96)) as DependencyObject,retry),"logo artwork hit area is not covered by the retry button");
            var close=window.FindName("CloseButton") as SkinButton;
            check(close!=null && Within(window.InputHitTest(new Point(538,69)) as DependencyObject,close),"native painted X has a full live close-button hit area");
            window.Render(new(UpdatePhase.Downloading,"正在下载", "dev-test",new(50,200,100))); window.UpdateLayout();
            var strip=(OriginalProgressStrip)window.FindName("DownloadProgress");
            check(Math.Abs(strip.FilledWidth-125)<0.1,"original progress texture fill follows actual received/total ratio");
            var amount=window.FindName("AmountText") as PixelText;
            check(amount?.Text.Contains("50 B / 200 B")==true && amount.Text.Contains("25%"),"actual WPF labels display measured bytes and percentage");
            window.Render(new(UpdatePhase.Error,string.Join("\n",Enumerable.Repeat(new string('长',80),40)))); window.UpdateLayout();
            var scroll=window.FindName("StatusScroll") as ScrollViewer;
            check(scroll?.ScrollableHeight>0,"long update errors scroll within fixed panel");
            if(back!=null && retry!=null && install!=null)
            {
                check(back.IsEnabled && retry.IsEnabled && !install.IsEnabled,"error state keeps back and retry enabled, installation disabled");
                check(back.TranslatePoint(new Point(back.ActualWidth,back.ActualHeight),window).Y<window.ActualHeight,"error content cannot cover back button");
                window.Render(new(UpdatePhase.Ready,"下载校验完成", "dev-test")); window.UpdateLayout(); check(install.IsEnabled,"verified ready state enables explicit restart confirmation");
            }
            bool Confirm(Action<ClassicMessageWindow> action)
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>action(Application.Current.Windows.OfType<ClassicMessageWindow>().Single())));
                return UpdateWindow.ConfirmRestart(window);
            }
            check(Confirm(dialog=>((SkinButton)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))),"native confirmation requires explicit yes and returns acceptance");
            check(!Confirm(dialog=>((Grid)dialog.Content).Children.OfType<SkinButton>().Single(b=>Equals(b.Content,"稍后")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))),"native confirmation later action defers installation");
            check(Confirm(dialog=>dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dialog),0,Key.Enter) { RoutedEvent=Keyboard.PreviewKeyDownEvent })),"native restart confirmation supports explicit Enter acceptance");
            foreach(var later in new[]{true,false})
            foreach(var key in new[]{Key.Enter,Key.Escape})
            {
                var accepted=Confirm(dialog=>
                {
                    var button=later?((Grid)dialog.Content).Children.OfType<SkinButton>().Single(b=>Equals(b.Content,"稍后")):(SkinButton)dialog.FindName("ConfirmButton");
                    dialog.Activate(); button.Focus(); Keyboard.Focus(button);
                    check(button.IsKeyboardFocused,"owned confirmation button receives actual keyboard focus");
                    button.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(button),0,key) { RoutedEvent=Keyboard.PreviewKeyDownEvent });
                });
                check(accepted==(!later && key==Key.Enter),$"focused {(later?"Later":"Confirm")} {key} preserves restart consent");
            }
            window.Render(new(UpdatePhase.Downloading,"正在下载更新包…", "dev-test",new(6220000,10000000,1200000))); window.UpdateLayout();
            var evidence=Environment.GetEnvironmentVariable("DFLEGACY_UPDATE_UI_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)) { var bitmap=new RenderTargetBitmap(567,512,96,96,PixelFormats.Pbgra32); bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(evidence); encoder.Save(file); }
            if(close!=null)
            {
                close.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                check(!window.IsVisible && controller.State.Phase==UpdatePhase.Cancelled,"native X click closes update window and cancels through the back-to-login path");
            }
        }
        finally { window.Close(); }
        var compact=new UpdateInstallWindow { ShowInTaskbar=false,ShowActivated=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual };
        try
        {
            compact.Show(); compact.UpdateLayout(); check(compact.ActualWidth==286 && compact.ActualHeight==104,"install helper uses native 286x104 geometry"); check(compact.FindName("InstallStatus") is PixelText,"helper displays truthful phase without invented percentage");
            var evidence=Environment.GetEnvironmentVariable("DFLEGACY_UPDATE_UI_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)) { var bitmap=new RenderTargetBitmap(286,104,96,96,PixelFormats.Pbgra32); bitmap.Render(compact); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(Path.ChangeExtension(evidence,"install.png")); encoder.Save(file); }
        }
        finally { compact.Finish(); }
        var outcomeRoot=Path.Combine(Path.GetTempPath(),"dflegacy-outcome-ui-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outcomeRoot);
        try
        {
            var prepared=Task.Run(()=>UpdateInstallerTests.Prepare(outcomeRoot)).GetAwaiter().GetResult();
            foreach(var mode in new[]{"rollback","restart-failure","stage-failure"})
            {
                var target=UpdateInstallerTests.Installation(outcomeRoot);
                if(mode=="stage-failure") File.AppendAllText(Path.Combine(prepared.Directory,"ijl15.dll"),"tampered");
                using var second=mode=="rollback"?new FileStream(Path.Combine(target,"ijl15.dll"),FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite):null;
                var result=UpdateTransaction.Apply(target,prepared,UpdateInstaller.Restart);
                var helper=new UpdateInstallWindow { ShowInTaskbar=false,ShowActivated=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual };
                helper.Show(); bool displayed=false;
                var closer=new DispatcherTimer(TimeSpan.FromMilliseconds(10),DispatcherPriority.Background,(_,_)=>
                {
                    foreach(var dialog in helper.OwnedWindows.OfType<ClassicMessageWindow>().ToArray())
                    {
                        displayed=true;
                        var blocks=(StackPanel)dialog.FindName("MessageBlocks");
                        var text=string.Concat(blocks.Children.OfType<PixelText>().Select(b=>b.Text));
                        check(text.Contains(result.Message) && text.Contains(target) && text.Contains(result.BackupDirectory??".dflegacy-backups"),"immediate helper feedback retains actual outcome, installation and recovery path");
                        check(text.Contains("手动") && text.Contains("登录器"),"immediate helper feedback gives manual next action");
                        check(helper.IsVisible,"helper stays visible until outcome is acknowledged");
                        ((SkinButton)dialog.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                },Dispatcher.CurrentDispatcher);
                try
                {
                    helper.Finish(result,target);
                    check(displayed,$"owned helper shows {mode} before closing");
                    check(!helper.IsVisible,"acknowledging helper outcome allows owned helper to close");
                }
                finally { closer.Stop(); helper.Finish(); }
            }
        }
        finally { Directory.Delete(outcomeRoot,true); }
        Console.WriteLine("Update WPF page and helper layout checks executed");
    }
}
