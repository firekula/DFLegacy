using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace DFLegacy.Launcher.Controls;

public sealed class PixelText : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(PixelText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OutlineThicknessProperty = DependencyProperty.Register(nameof(OutlineThickness), typeof(int), typeof(PixelText), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(PixelText), new FrameworkPropertyMetadata(TextWrapping.Wrap, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(PixelText), new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Text { get => (string?)GetValue(TextProperty) ?? ""; set => SetValue(TextProperty, value); }
    public int OutlineThickness { get => (int)GetValue(OutlineThicknessProperty); set => SetValue(OutlineThicknessProperty, value); }
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    private bool _fallback;
    public PixelText()
    {
        Focusable = false; ClipToBounds = true; VerticalContentAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }
    private int SizePixels => Math.Clamp((int)Math.Round(FontSize * VisualTreeHelper.GetDpi(this).DpiScaleY), 6, 96);
    private double LineHeight => Math.Max(FontSize + 5, 18);
    private string[] Lines(double width)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var available = double.IsFinite(width) ? Math.Max(1, (int)Math.Floor(width * dpi.DpiScaleX) - 2 * Math.Clamp(OutlineThickness, 0, 2)) : 4096;
        return PixelRasterizer.Lines(Text, SizePixels, available, TextWrapping != TextWrapping.NoWrap);
    }
    protected override Size MeasureOverride(Size constraint)
    {
        if (Text.Length == 0) return new Size(0, 0);
        _fallback = false;
        try
        {
            var inner = Math.Max(0, constraint.Width - Padding.Left - Padding.Right); var lines = Lines(inner);
            var dpi = VisualTreeHelper.GetDpi(this); var width = 0d;
            foreach (var line in lines)
            {
                var raster = PixelRasterizer.Render(line, SizePixels, Colors.White, OutlineThickness);
                width = Math.Max(width, Math.Max(raster.Advance, raster.Image.PixelWidth) / dpi.DpiScaleX);
            }
            return new Size(Math.Min(constraint.Width, width + Padding.Left + Padding.Right),
                Math.Min(constraint.Height, lines.Length * LineHeight + Padding.Top + Padding.Bottom));
        }
        catch (Win32Exception) { _fallback = true; return Fallback(constraint.Width).BuildGeometry(new Point()).Bounds.Size; }
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Text.Length == 0) return;
        if (_fallback) { drawingContext.DrawText(Fallback(ActualWidth), new Point(Padding.Left, Padding.Top)); return; }
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this); var innerWidth = Math.Max(0, ActualWidth - Padding.Left - Padding.Right);
            var lines = Lines(innerWidth); var total = lines.Length * LineHeight;
            var availableHeight = Math.Max(0, ActualHeight - Padding.Top - Padding.Bottom);
            var y = Padding.Top + (VerticalContentAlignment == VerticalAlignment.Center ? (availableHeight - total) / 2 : 0);
            var color = (Foreground as SolidColorBrush)?.Color ?? Colors.White;
            foreach (var line in lines)
            {
                var raster = PixelRasterizer.Render(line, SizePixels, color, OutlineThickness);
                if (raster.GlyphHeight > 0)
                {
                    var width = raster.Image.PixelWidth / dpi.DpiScaleX; var height = raster.Image.PixelHeight / dpi.DpiScaleY;
                    var x = Padding.Left + (TextAlignment == TextAlignment.Center ? (innerWidth - width) / 2 : TextAlignment == TextAlignment.Right ? innerWidth - width : 0);
                    var top = y + (LineHeight - height) / 2;
                    x = Math.Floor(x * dpi.DpiScaleX) / dpi.DpiScaleX; top = Math.Floor(top * dpi.DpiScaleY) / dpi.DpiScaleY;
                    drawingContext.DrawImage(raster.Image, new Rect(x, top, width, height));
                }
                y += LineHeight;
            }
        }
        catch (Win32Exception) { _fallback = true; drawingContext.DrawText(Fallback(ActualWidth), new Point(Padding.Left, Padding.Top)); }
    }
    private FormattedText Fallback(double width) => new(Text.Length > 4096 ? Text[..4096] + "…" : Text, CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight, new Typeface("宋体"), FontSize, Foreground ?? Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = double.IsFinite(width) ? Math.Max(1, width - Padding.Left - Padding.Right) : 4096, TextAlignment = TextAlignment };
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); InvalidateMeasure(); }
    protected override AutomationPeer OnCreateAutomationPeer() => new TextPeer(this);
    private sealed class TextPeer(PixelText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(PixelText);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => AutomationProperties.GetName(Owner) is { Length: > 0 } name ? name : ((PixelText)Owner).Text;
    }
}
