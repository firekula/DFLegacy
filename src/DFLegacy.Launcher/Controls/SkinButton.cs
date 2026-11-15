using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DFLegacy.Launcher.Controls;

public sealed class SkinButton : Button
{
    public static readonly DependencyProperty NormalSourceProperty = DependencyProperty.Register(nameof(NormalSource), typeof(ImageSource), typeof(SkinButton));
    public static readonly DependencyProperty HoverSourceProperty = DependencyProperty.Register(nameof(HoverSource), typeof(ImageSource), typeof(SkinButton));
    public static readonly DependencyProperty PressedSourceProperty = DependencyProperty.Register(nameof(PressedSource), typeof(ImageSource), typeof(SkinButton));
    public ImageSource? NormalSource { get => (ImageSource?)GetValue(NormalSourceProperty); set => SetValue(NormalSourceProperty, value); }
    public ImageSource? HoverSource { get => (ImageSource?)GetValue(HoverSourceProperty); set => SetValue(HoverSourceProperty, value); }
    public ImageSource? PressedSource { get => (ImageSource?)GetValue(PressedSourceProperty); set => SetValue(PressedSourceProperty, value); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && IsEnabled) { e.Handled = true; OnClick(); return; }
        base.OnKeyDown(e);
    }
}
