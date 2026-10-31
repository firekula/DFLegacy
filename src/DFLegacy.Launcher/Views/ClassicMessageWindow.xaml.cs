using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using DFLegacy.Launcher.Controls;

namespace DFLegacy.Launcher.Views;

public partial class ClassicMessageWindow : Window
{
    private SkinButton? laterButton;
    internal bool Accepted { get; private set; }
    internal ClassicMessageWindow(string message, bool confirmRestart, string cancelText = "稍后") : this(message)
    {
        if(!confirmRestart) return;
        ConfirmButton.Margin=new Thickness(48,137,0,0); ConfirmButton.IsCancel=false;
        laterButton=new SkinButton { Content=cancelText,Width=80,Height=29,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(158,137,0,0),Style=(Style)FindResource("ClassicButton"),IsCancel=true };
        laterButton.Click+=(_,_)=>Close(); ((Grid)Content).Children.Add(laterButton);
    }
    public ClassicMessageWindow(string message)
    {
        InitializeComponent();
        // PixelText bounds each raster input to 4096 UTF-16 units. Keep that shared
        // safeguard while allowing this scrollable diagnostic to retain its full tail.
        for (var offset = 0; offset < message.Length;)
        {
            var length = Math.Min(4096, message.Length - offset);
            if (offset + length < message.Length && char.IsHighSurrogate(message[offset + length - 1])) length--;
            var block = offset == 0 ? MessageText : new PixelText
            {
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = MessageText.Foreground,
                FontSize = MessageText.FontSize,
                OutlineThickness = MessageText.OutlineThickness
            };
            block.Text = message.Substring(offset, length);
            if (offset != 0) MessageBlocks.Children.Add(block);
            offset += length;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) { Accepted=true; Close(); }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        e.Handled = true;
        Accepted=e.Key==Key.Enter && laterButton?.IsKeyboardFocusWithin!=true;
        Close();
    }
}
