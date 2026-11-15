using System.Reflection;
using System.Text;
using DFLegacy.Launcher;

internal static class ChapterTwoGameSettingsTests
{
    internal static void Run(string root, Action<bool,string> check)
    {
        var prepare=typeof(LauncherOptions).Assembly.GetType("DFLegacy.Launcher.ChapterTwoGameSettings")
            ?.GetMethod("Prepare",BindingFlags.Static|BindingFlags.NonPublic);
        check(prepare is not null,"game settings have a persistent installation-scoped owner");
        if(prepare is null)return;
        string Prepare(string dir)=>(string)prepare.Invoke(null,[dir])!;
        var client=Path.Combine(root,"settings-client");Directory.CreateDirectory(client);
        var original=Encoding.ASCII.GetBytes("[DNF COMMON]\r\nCONFIG VERSION=1\r\n[ID probe]\r\nSOUND VOLUME=32\r\nMUSIC VOLUME=17\r\nZOOM EFFECT=0\r\n[KEYBOARD]\r\nCUSTOM KEY=90\r\n");
        var session=Path.Combine(client,"Logs","DFLegacy","Sessions","A-20261113-221942-071-2","Temp");Directory.CreateDirectory(session);
        File.WriteAllBytes(Path.Combine(session,"DNF_CHINA.cfg"),original);
        var path=Prepare(client);
        check(Path.GetFullPath(path).StartsWith(Path.GetFullPath(client)+Path.DirectorySeparatorChar),"settings stay in the selected client installation");
        check(File.ReadAllBytes(path).SequenceEqual(original),"first launch imports the last local session's complete sound and key settings");
        File.AppendAllText(path,"NEW OPTION=preserved\r\n");var changed=File.ReadAllBytes(path);
        check(Prepare(client)==path&&File.ReadAllBytes(path).SequenceEqual(changed),"a later launch never overwrites saved settings with an old session");
        check(File.ReadAllBytes(Path.Combine(session,"DNF_CHINA.cfg")).SequenceEqual(original),"migration retains its evidence source");
        var other=Path.Combine(root,"other-client");Directory.CreateDirectory(other);
        check(Prepare(other)!=path,"different isolated clients never share game settings");
        check(!File.ReadAllText(Prepare(other)).Contains("ID probe"),"new installations do not import another client's accounts");
    }
}
