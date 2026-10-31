using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DFLegacy.Launcher.Controls;

public sealed class SkinFrame : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(SkinFrame), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InsetsProperty = DependencyProperty.Register(nameof(Insets), typeof(Thickness), typeof(SkinFrame), new FrameworkPropertyMetadata(new Thickness(8), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SkinFrame), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Thickness Insets { get => (Thickness)GetValue(InsetsProperty); set => SetValue(InsetsProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    private BitmapSource? _previous;
    private Thickness _insets;
    private readonly CroppedBitmap?[] _parts = new CroppedBitmap?[9];
    public SkinFrame() { IsHitTestVisible = false; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); }
    protected override void OnRender(DrawingContext dc)
    {
        if (Source is not { } source || ActualWidth <= 0 || ActualHeight <= 0) return;
        var insets = Insets; var w = source.PixelWidth; var h = source.PixelHeight;
        var l = Math.Clamp((int)insets.Left, 0, w / 2); var r = Math.Clamp((int)insets.Right, 0, w / 2);
        var t = Math.Clamp((int)insets.Top, 0, h / 2); var b = Math.Clamp((int)insets.Bottom, 0, h / 2);
        if (_previous != source || _insets != insets)
        {
            var xs = new[] { 0, l, w - r, w }; var ys = new[] { 0, t, h - b, h };
            for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++)
            {
                var rect = new Int32Rect(xs[col], ys[row], xs[col + 1] - xs[col], ys[row + 1] - ys[row]);
                _parts[row * 3 + col] = rect.Width > 0 && rect.Height > 0 ? new CroppedBitmap(source, rect) : null;
                _parts[row * 3 + col]?.Freeze();
            }
            _previous = source; _insets = insets;
        }
        var left = Math.Min(l, ActualWidth / 2); var right = Math.Min(r, ActualWidth / 2);
        var top = Math.Min(t, ActualHeight / 2); var bottom = Math.Min(b, ActualHeight / 2);
        var dx = new[] { 0d, left, ActualWidth - right, ActualWidth }; var dy = new[] { 0d, top, ActualHeight - bottom, ActualHeight };
        if (Fill is not null) dc.DrawRectangle(Fill, null, new Rect(left, top, Math.Max(0, ActualWidth - left - right), Math.Max(0, ActualHeight - top - bottom)));
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++)
            if (!(row == 1 && col == 1) && _parts[row * 3 + col] is { } image)
                dc.DrawImage(image, new Rect(dx[col], dy[row], Math.Max(0, dx[col + 1] - dx[col]), Math.Max(0, dy[row + 1] - dy[row])));
    }
}
