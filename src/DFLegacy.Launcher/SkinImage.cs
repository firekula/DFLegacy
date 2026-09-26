using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DFLegacy.Launcher;

/// <summary>
/// 原版皮肤的三态图片按钮：把一张 <see cref="Image"/> 变成 常态/悬停/按下 三态切换的按钮。
/// 原版 QQLogin.xml 的 Button 元素就是这种模型（Background / BackgroundHover / BackgroundPressed），
/// 中文标签已经烙在图里，所以直接换图就等于标签与原版完全一致。
/// </summary>
internal sealed class SkinImage
{
    private readonly Image _image;
    private readonly ImageSource _normal;
    private readonly ImageSource _hover;
    private readonly ImageSource _pressed;
    private readonly Action? _onClick;

    public SkinImage(Image image, string normal, string hover, string pressed, Action? onClick = null)
    {
        _image = image;
        _normal = Load(normal);
        _hover = Load(hover);
        _pressed = Load(pressed);
        _onClick = onClick;

        image.Source = _normal;
        image.Stretch = Stretch.Fill;
        if (onClick is not null)
        {
            image.Cursor = Cursors.Hand;
        }

        image.MouseEnter += (_, _) => image.Source = _hover;
        image.MouseLeave += (_, _) => image.Source = _normal;
        image.MouseLeftButtonDown += (_, e) =>
        {
            image.Source = _pressed;
            _ = image.CaptureMouse();
            // 阻断冒泡：标题栏区域（CaptionHeight=100）由窗口统一处理拖动，
            // 关闭按钮等图元在按下时不能被当成"拖窗口"。
            e.Handled = true;
        };
        image.MouseLeftButtonUp += (_, _) => OnMouseUp();
    }

    private void OnMouseUp()
    {
        if (_image.IsMouseCaptured)
        {
            _image.ReleaseMouseCapture();
        }

        var stillOver = _image.IsMouseOver;
        _image.Source = stillOver ? _hover : _normal;
        if (stillOver)
        {
            _onClick?.Invoke();
        }
    }

    private static ImageSource Load(string fileName) =>
        new BitmapImage(new Uri($"pack://application:,,,/Assets/Skin/{fileName}", UriKind.Absolute));
}
