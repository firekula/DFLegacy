using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DFLegacy.AccountClient;
using DFLegacy.Launcher.Views;

internal static class RegistrationUiTests
{
    internal static void Run(Action<bool, string> check)
    {
        string? payload = null;
        var requestCount = 0;
        using var http = new HttpClient(new UpdateTransportTests.Handler(async (request, token) =>
        {
            requestCount++;
            check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/accounts",
                "registration submits to the public account endpoint");
            payload = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""
                    {"id":"11111111-1111-1111-1111-111111111111","userName":"player01","accountUid":1,"cash":0,"kind":0}
                    """)
            };
        })) { BaseAddress = new Uri("https://registration.example/") };
        var registered = false;
        var window = new RegisterWindow(new AccountServiceClient(http), (profile, _) =>
        {
            registered = profile.Account == "player01";
        });
        var messageCloser = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        messageCloser.Tick += (_, _) =>
        {
            foreach (var message in Application.Current.Windows.OfType<ClassicMessageWindow>().ToArray())
                message.Close();
        };
        try
        {
            var grid = (Grid)window.Content;
            var accountOptions = grid.Children.OfType<CheckBox>().ToArray();
            check(accountOptions.Length == 0, "public registration does not offer account type selection");
            var occupiedRows = grid.Children.OfType<FrameworkElement>().Select(Grid.GetRow).Distinct().Order().ToArray();
            check(occupiedRows.SequenceEqual(Enumerable.Range(0, grid.RowDefinitions.Count)),
                "registration form has no empty row after removing account type selection");
            // Selecting any legacy option must never let this public form request a test account.
            foreach (var option in accountOptions) option.IsChecked = true;
            ((TextBox)window.FindName("UserNameBox")).Text = "player01";
            ((PasswordBox)window.FindName("PasswordBox")).Password = "secret01";
            ((PasswordBox)window.FindName("ConfirmBox")).Password = "secret01";
            ((TextBox)window.FindName("OicqBox")).Text = "123456";
            messageCloser.Start();
            ((Button)window.FindName("SubmitBtn")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(requestCount == 1 && payload is not null, "valid registration emits exactly one request");
            if (payload is not null)
            {
                using var json = JsonDocument.Parse(payload);
                check(!json.RootElement.GetProperty("testAccount").GetBoolean(),
                    "public registration always sends the ordinary account flag");
            }
            check(registered, "successful registration still returns the created local account profile");
        }
        finally
        {
            messageCloser.Stop();
            window.Close();
        }
    }
}
