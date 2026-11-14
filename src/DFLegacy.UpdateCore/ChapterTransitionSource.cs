namespace DFLegacy.UpdateCore;

public static class ChapterTransitionSource
{
    private const string OfficialRepository = "firekula/DFLegacy";
    private const string FirstChannel = "stable";
    private const string SecondChannel = "chapter-two";

    public static ReleaseSource? OfficialTarget(ReleaseSource baseline)
    {
        baseline.Validate();
        return baseline is { Provider: "github", Repository: OfficialRepository, Channel: FirstChannel }
            ? baseline with { Channel = SecondChannel }
            : null;
    }

    public static bool IsOfficialChapterTwo(ReleaseSource source) =>
        source is { Provider: "github", Repository: OfficialRepository, Channel: SecondChannel };

    public static void RequireUpgrade(ReleaseSource baseline, ReleaseSource target)
    {
        target.Validate();
        var official = OfficialTarget(baseline);
        if (target == baseline || target.Architecture != baseline.Architecture
            || target.Provider != "local" && target != official)
            throw new InvalidDataException("第一章安装与所选跨章更新来源不匹配。");
    }
}
