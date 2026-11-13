using Core=DFLegacy.UpdateCore;

namespace DFLegacy.Launcher.Updates;

internal static class ChapterDisplayMigration
{
    internal static Core.ChapterDisplayPreference? Read(string root)
    {
        var client=Core.PathPolicy.Resolve(root,"DNF.exe");
        var config=Path.Combine(root,"Config.ini");
        Core.PathPolicy.NoLinks(config);
        if(!File.Exists(client)||!File.Exists(config))return null;
        try
        {
            var display=ClientDisplayConfiguration.Load(client);
            if(display.PresentationFilter is not ("sharp" or "point" or "linear") ||
                !ClientDisplayConfiguration.Presets.Any(p=>p.Width==display.Width&&p.Height==display.Height))
                return null;
            return new(display.Width,display.Height,display.PresentationFilter);
        }
        catch(InvalidDataException) { return null; }
    }
}
