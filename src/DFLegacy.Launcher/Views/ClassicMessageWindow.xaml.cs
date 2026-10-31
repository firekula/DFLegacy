using System.Windows;
using System.Windows.Input;
using DFLegacy.Launcher.Controls;

namespace DFLegacy.Launcher.Views;

public partial class ClassicMessageWindow : Window
{
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

    private void Confirm_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        e.Handled = true;
        Close();
    }
}
