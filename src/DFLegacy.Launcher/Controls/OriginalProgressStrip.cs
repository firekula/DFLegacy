using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DFLegacy.Launcher.Controls;
public sealed class OriginalProgressStrip : FrameworkElement
{
    private readonly ImageSource texture=new BitmapImage(new Uri("pack://application:,,,/DFLegacy.Launcher;component/Assets/Skin/classic-update-progress.png"));
    private double fraction;
    public OriginalProgressStrip()
    {
        IsHitTestVisible=false;
    }
    internal void Set(double value,bool pending)
    {
        // Unknown-duration work holds its measured position; it never becomes a marquee.
        fraction=double.IsFinite(value)?Math.Clamp(value,0,1):0;
        InvalidateVisual();
    }
    internal double FilledWidth=>ActualWidth*fraction;
    protected override void OnRender(DrawingContext dc)
    {
        if(FilledWidth>0) dc.DrawImage(texture,new Rect(0,0,FilledWidth,ActualHeight));
    }
}
