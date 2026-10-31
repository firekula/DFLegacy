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
            window.Render(new(UpdatePhase.Downloading,"正在下载更新包…", "dev-test",new(6220000,10000000,1200000))); window.UpdateLayout();
            var evidence=Environment.GetEnvironmentVariable("DFLEGACY_UPDATE_UI_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)) { var bitmap=new RenderTargetBitmap(567,512,96,96,PixelFormats.Pbgra32); bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(evidence); encoder.Save(file); }
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
        Console.WriteLine("Update WPF page and helper layout checks executed");
    }
}
