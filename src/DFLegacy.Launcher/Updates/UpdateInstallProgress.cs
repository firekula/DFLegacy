namespace DFLegacy.Launcher.Updates;

internal sealed record UpdateInstallProgress(double Fraction, string Message, bool ReplacingLauncher = false)
{
    internal static void Report(IProgress<UpdateInstallProgress>? progress, double fraction, string message, string? launcherFile = null)
    {
        // UI reporting cannot fail or roll back a disk transaction.
        try { progress?.Report(new(fraction, message, IsLauncher(launcherFile))); } catch { }
    }

    internal static UpdateInstallProgress FromCore(DFLegacy.UpdateCore.UpdateProgress progress)
    {
        var fraction = progress.TotalFiles > 0
            ? Math.Clamp((double)progress.CompletedFiles / progress.TotalFiles, 0, 1)
            : 0;
        return new(fraction, UpdateProgressText.Format(progress),
            progress.Phase is "backup" or "replace" or "restore" && IsLauncher(progress.Path));
    }

    private static bool IsLauncher(string? path) =>
        string.Equals(path, BootstrapLayout.EntryName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, BootstrapLayout.AppName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, BootstrapLayout.UpdaterName, StringComparison.OrdinalIgnoreCase);
}
