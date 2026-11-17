using System.Windows;
using Core = DFLegacy.UpdateCore;
using Updates = DFLegacy.Launcher.Updates;

namespace DFLegacy.Launcher.Views;

// The 2008 launcher's CProgressDlg layout: a 233x108 panel whose progress control
// occupies (18,72)-(216,81), i.e. a 198x9 green-gradient bar, with no percentage text.
public partial class LoadingWindow : Window
{
    internal const double NativeProgressWidth = 198;
    internal double ProgressFraction { get; private set; }
    internal LoadingWindow() => InitializeComponent();
    // Startup validation reports the engine's core progress; the background check reports
    // the launcher view-state progress. Both map onto the same native bar.
    internal void Report(Core.UpdateProgress progress) =>
        Set(progress.CompletedFiles, progress.TotalFiles, progress.BytesReceived, progress.TotalBytes);
    internal void Report(Updates.UpdateProgress progress) =>
        Set(progress.CompletedFiles, progress.TotalFiles, progress.BytesReceived, progress.TotalBytes);
    private void Set(int completedFiles, int totalFiles, long bytesReceived, long totalBytes)
    {
        ProgressFraction = totalFiles > 0 ? Math.Clamp((double)completedFiles / totalFiles, 0, 1)
            : totalBytes > 0 ? Math.Clamp((double)bytesReceived / totalBytes, 0, 1) : 0;
        LoadingProgress.Width = NativeProgressWidth * ProgressFraction;
    }
}
