using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace DFLegacy.Launcher.Controls;
public sealed class OriginalProgressStrip : FrameworkElement
{
    private readonly ImageSource texture=new BitmapImage(new Uri("pack://application:,,,/DFLegacy.Launcher;component/Assets/Skin/classic-update-progress.png"));
    private readonly DispatcherTimer timer;
    private double fraction,position;
    private bool indeterminate;
    public OriginalProgressStrip()
    {
        IsHitTestVisible=false;
        timer=new DispatcherTimer(TimeSpan.FromMilliseconds(60),DispatcherPriority.Background,(_,_)=> { position=(position+0.035)%1; InvalidateVisual(); },Dispatcher);
        timer.Stop(); Loaded+=(_,_)=> { if(indeterminate) timer.Start(); }; Unloaded+=(_,_)=>timer.Stop();
    }
    internal void Set(double value,bool pending)
    {
        fraction=Math.Clamp(value,0,1); indeterminate=pending;
        if(pending && IsLoaded) timer.Start(); else timer.Stop(); InvalidateVisual();
    }
    internal double FilledWidth=>ActualWidth*fraction;
    protected override void OnRender(DrawingContext dc)
    {
        var width=indeterminate?ActualWidth*.25:FilledWidth;
        var left=indeterminate?(ActualWidth-width)*position:0;
        if(width>0) dc.DrawImage(texture,new Rect(left,0,width,ActualHeight));
    }
}
