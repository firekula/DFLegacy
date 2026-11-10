namespace DFLegacy.Launcher.Updates;

// Translate only presentation text; engine phase identifiers remain unchanged.
internal static class UpdateProgressText
{
    internal static string Format(DFLegacy.UpdateCore.UpdateProgress progress)
    {
        var label = progress.Phase switch
        {
            "verify" => "完整校验",
            "download" => "下载",
            "retry" => "等待重试",
            "prepared" => "准备安装",
            "install" => "安装",
            "recover" => "恢复",
            "commit" => "保存更新记录",
            _ => "处理更新"
        };
        if (progress.TotalFiles > 0) label += $" {progress.CompletedFiles}/{progress.TotalFiles}";
        if (!string.IsNullOrWhiteSpace(progress.Path)) label += "：" + progress.Path;
        if (!string.IsNullOrWhiteSpace(progress.Detail))
            label += (string.IsNullOrWhiteSpace(progress.Path) ? "：" : "；") + progress.Detail;
        return label;
    }
}
