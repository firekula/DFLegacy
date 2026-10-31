using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DFLegacy.Launcher;
using DFLegacy.Launcher.Controls;

internal static class LoginInputTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Only synthetic in-memory text; this store must never be written.
        var path = Path.Combine(Path.GetTempPath(), "dflegacy-input-" + Guid.NewGuid().ToString("N"), "launcher.json");
        var window = new MainWindow(new LauncherAccountStore(path), refreshStatus: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            window.Show(); window.UpdateLayout();
            var account = (TextBox)window.FindName("AccountText");
            var password = (PasswordBox)window.FindName("PasswordText");
            var accountHint = (PixelText)window.FindName("AccountHint");
            var passwordHint = (PixelText)window.FindName("PasswordHint");
            var outside = (CheckBox)window.FindName("AgreeBox");
            foreach (var control in new Control[] { account, password })
                foreach (var item in Descendants(control).OfType<FrameworkElement>())
                    Console.WriteLine($"Input tree: {control.Name} {item.GetType().Name}, origin={item.TranslatePoint(new Point(), window)}, size={item.RenderSize}, margin={item.Margin}, padding={(item is Control c ? c.Padding : new Thickness())}");
            void Focus(Control control)
            {
                window.Activate(); control.Focus(); Keyboard.Focus(control);
                check(control.IsKeyboardFocusWithin, $"{control.Name} receives real keyboard focus");
            }
            void Hint(PixelText hint, bool visible, string state)
            {
                Console.WriteLine($"Login hint: {hint.Name} {state}={hint.Visibility}");
                check(hint.Visibility == (visible ? Visibility.Visible : Visibility.Collapsed), $"{hint.Name} {state}");
            }
            Focus(outside);
            Hint(accountHint, true, "empty blurred"); Hint(passwordHint, true, "empty blurred");
            Focus(account);
            Hint(accountHint, false, "empty focused");
            CheckChildFocus(account, accountHint);
            Focus(account);
            var emptyCaret = account.TranslatePoint(account.GetRectFromCharacterIndex(0).TopLeft, window);
            var expectedAccount = accountHint.TranslatePoint(new Point(), window).X;
            Console.WriteLine($"Login geometry: account empty caret={emptyCaret}, placeholder X={expectedAccount}");
            check(Math.Abs(emptyCaret.X - expectedAccount) < 0.1, "account empty insertion caret aligns with placeholder left edge");
            check(Math.Abs(emptyCaret.Y - 370) < 1, "account insertion preserves original vertical alignment within subpixel rounding");
            account.Text = "InputFixture"; account.CaretIndex = 0; window.UpdateLayout();
            Hint(accountHint, false, "nonempty focused");
            var typedCaret = account.TranslatePoint(account.GetRectFromCharacterIndex(0).TopLeft, window);
            Console.WriteLine($"Login geometry: account typed caret X={typedCaret.X}");
            check(Math.Abs(typedCaret.X - expectedAccount) < 0.1, "account typed text insertion origin aligns with placeholder");
            CheckRenderedText(account, expectedAccount, false);
            Focus(outside); Hint(accountHint, false, "nonempty blurred");
            account.Clear(); Hint(accountHint, true, "cleared blurred");
            Focus(account); account.Text = "InputFixture"; account.Clear(); Hint(accountHint, false, "cleared focused");
            Focus(password); Hint(accountHint, true, "empty after focus leaves"); Hint(passwordHint, false, "empty focused");
            CheckChildFocus(password, passwordHint);
            Focus(password);
            var expectedPassword = passwordHint.TranslatePoint(new Point(), window).X;
            CheckPasswordCaret("empty");
            password.Password = "SyntheticOnly"; window.UpdateLayout();
            Hint(passwordHint, false, "nonempty focused"); CheckPasswordCaret("typed");
            CheckRenderedText(password, expectedPassword, true);
            Focus(outside); Hint(passwordHint, false, "nonempty blurred");
            password.Clear(); Hint(passwordHint, true, "cleared blurred");
            Focus(password); password.Password = "SyntheticOnly"; password.Clear(); Hint(passwordHint, false, "cleared focused");
            Focus(outside); Hint(passwordHint, true, "empty after focus leaves");
            Focus(account);
            account.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            check(password.IsKeyboardFocused, "Tab advances from account to password");
            password.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            check(account.IsKeyboardFocused, "Shift-Tab returns from password to account");
            Focus(outside);
            check(account.MaxLength == 32 && password.MaxLength == 128, "login input length limits remain intact");
            check(window.ActualWidth == 567 && window.ActualHeight == 512, "login artwork dimensions remain intact");
            foreach (var control in new Control[] { account, password })
            {
                check(control.ActualWidth == 164 && control.ActualHeight == 17, "login input full dimensions remain intact");
                foreach (var point in new[] { new Point(1, 8), new Point(163, 8) })
                {
                    var hit = window.InputHitTest(control.TranslatePoint(point, window)) as DependencyObject;
                    while (hit is not null && hit != control) hit = VisualTreeHelper.GetParent(hit);
                    check(hit == control, $"{control.Name} retains full input hit area at X={point.X}");
                }
            }
            check(!File.Exists(path), "input interactions never persist credentials");

            void CheckChildFocus(Control control, PixelText hint)
            {
                var host = (ScrollViewer)control.Template.FindName("PART_ContentHost", control);
                host.Focusable = true;
                Focus(host);
                check(!control.IsKeyboardFocused && control.IsKeyboardFocusWithin, $"{control.Name} tracks keyboard focus inside its child");
                Hint(hint, false, "empty with child focused");
                host.Focusable = false;
            }

            void CheckRenderedText(Control control, double expected, bool masked)
            {
                var glyphs = Descendants(control).OfType<Visual>()
                    .SelectMany(visual => Glyphs(VisualTreeHelper.GetDrawing(visual), p => visual.TransformToAncestor(window).Transform(p)))
                    .ToArray();
                check(glyphs.Length > 0, $"{control.Name} renders actual text glyphs");
                if (glyphs.Length == 0) return;
                var x = glyphs.Min(g => g.X);
                Console.WriteLine($"Login geometry: {control.Name} rendered glyph origin X={x}");
                check(Math.Abs(x - expected) < 0.1, $"{control.Name} rendered glyph origin aligns with placeholder");
                if (masked) check(glyphs.All(g => g.Masked), "password renders only masking characters");

                IEnumerable<(double X, bool Masked)> Glyphs(Drawing? drawing, Func<Point, Point> transform)
                {
                    if (drawing is GlyphRunDrawing glyph)
                        yield return (transform(glyph.GlyphRun.BaselineOrigin).X,
                            glyph.GlyphRun.Characters is { Count: > 0 } chars && chars.All(c => c == password.PasswordChar));
                    if (drawing is DrawingGroup group)
                        foreach (var child in group.Children)
                            foreach (var result in Glyphs(child, p => transform(group.Transform?.Transform(p) ?? p)))
                                yield return result;
                }
            }

            void CheckPasswordCaret(string state)
            {
                // PasswordBox has no public caret geometry API. Query the real WPF
                // text pointer without reading or logging its protected contents.
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var container = typeof(PasswordBox).GetProperty("TextContainer", flags)!.GetValue(password)!;
                var containerInterface = typeof(TextBox).Assembly.GetType("System.Windows.Documents.ITextContainer")!;
                var pointer = containerInterface.GetProperty("Start")!.GetValue(container)!;
                var pointerInterface = typeof(TextBox).Assembly.GetType("System.Windows.Documents.ITextPointer")!;
                var rect = (Rect)pointerInterface.GetMethod("GetCharacterRect")!.Invoke(pointer, new object[] { LogicalDirection.Forward })!;
                var view = Descendants(password).OfType<FrameworkElement>().Single(v => v.GetType().Name == "TextBoxView");
                var caret = password.TranslatePoint(rect.TopLeft, window);
                Console.WriteLine($"Login geometry: password {state} insertion={caret}, placeholder X={expectedPassword}, view X={view.TranslatePoint(new Point(), window).X}");
                check(!rect.IsEmpty && Math.Abs(caret.X - expectedPassword) < 0.1, $"password {state} insertion origin aligns with placeholder");
                check(Math.Abs(caret.Y - 393) < 1, $"password {state} insertion preserves original vertical alignment within subpixel rounding");
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
