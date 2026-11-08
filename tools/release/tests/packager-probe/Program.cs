using System.Reflection;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var type = Assembly.Load("DFLegacy.ClientPackager").GetType("DFLegacy.ClientPackager.V4ResourcesForm", throwOnError: true)!;
        using var form = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: [Path.GetTempPath()], culture: null)!;
        var provider = (ComboBox)type.GetField("provider", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var request = type.GetMethod("Request", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (provider.Items.Count != 3 || provider.SelectedIndex != 0) throw new Exception("Provider options/default differ");
        foreach (var (index, expectedProvider, expectedChannel) in new[] { (0, "gitea", "gitea-test"), (1, "gitee", "gitee-test"), (2, "github", "stable"), (0, "gitea", "gitea-test") })
        {
            provider.SelectedIndex = index;
            using var json = JsonDocument.Parse((string)request.Invoke(form, null)!);
            var root = json.RootElement;
            if (root.GetProperty("provider").GetString() != expectedProvider || root.GetProperty("channel").GetString() != expectedChannel
                || root.GetProperty("repository").GetString() != "firekula/DFLegacy") throw new Exception("Incorrect source request after provider selection");
        }
        Console.WriteLine("Packager source selection: Gitea default, Gitee/GitHub retained, source request verified.");
    }
}
