using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DFLegacy.Launcher.Controls;

public sealed class ClassicDialogFrame : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ClassicDialogFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CanCloseProperty = DependencyProperty.Register(
        nameof(CanClose), typeof(bool), typeof(ClassicDialogFrame), new PropertyMetadata(true));

    private SkinButton? closeButton;

    public ClassicDialogFrame()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DFLegacy.Launcher;component/Controls/ClassicDialogFrame.xaml")
        });
        Style = (Style)FindResource(typeof(ClassicDialogFrame));
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool CanClose { get => (bool)GetValue(CanCloseProperty); set => SetValue(CanCloseProperty, value); }
    public event RoutedEventHandler? CloseRequested;

    public override void OnApplyTemplate()
    {
        if (closeButton != null) closeButton.Click -= CloseClicked;
        base.OnApplyTemplate();
        closeButton = GetTemplateChild("PART_CloseButton") as SkinButton;
        if (closeButton != null) closeButton.Click += CloseClicked;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!e.Handled && e.GetPosition(this).Y < 82 && Window.GetWindow(this) is { IsVisible: true } window)
        {
            e.Handled = true;
            window.DragMove();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && CanClose)
        {
            e.Handled = true;
            RequestClose();
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => RequestClose();

    private void RequestClose()
    {
        if (!CanClose) return;
        if (CloseRequested is { } close) close(this, new RoutedEventArgs());
        else Window.GetWindow(this)?.Close();
    }
}
