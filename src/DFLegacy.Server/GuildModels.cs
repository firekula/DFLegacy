using System.Text;
using System.Text.RegularExpressions;
using DFLegacy.Protocol;

namespace DFLegacy.Server;

public sealed record GuildRecord(uint Id, string Name, string Address, Guid FounderCharacterId,
    DateTimeOffset CreatedAt, string? Announcement = null)
{
    public byte Level { get; init; } = 1;
    public uint Experience { get; init; }
    public ushort SkillPoints { get; init; }
    public byte StatusSkillLevel { get; init; }
    public byte ExperienceSkillLevel { get; init; }
    public byte[]? IconPixels { get; init; }
}
public sealed record GuildCreateResult(byte Error, CharacterRecord? Character = null, GuildRecord? Guild = null,
    bool Replayed = false, bool Unknown = false);
internal sealed record GuildDissolutionResult(CharacterRecord Founder, Guid[] OtherMembers);
internal sealed record GuildAnnouncementResult(byte Error, Guid[]? Members = null);

public static class GuildRules
{
    internal const int IconSize = 16;
    internal const byte IconMinimumGuildLevel = 2;
    private static readonly Encoding Encoding = CreateEncoding();
    public static bool TryReadName(byte[] bytes, out string name) =>
        TryDecode(bytes, 1, 14, out name) && name.Trim() == name;
    public static bool TryReadAddress(byte[] bytes, out string address) =>
        TryDecode(bytes, 3, 12, out address) && address[0] != '_'
        && address.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    internal static bool TryReadAnnouncement(byte[] bytes, out string announcement) =>
        TryDecode(bytes, 1, GameProtocolEngine.GuildAnnouncementMaxBytes, out announcement);

    public static bool IsName(string name) => TryEncode(name, out var bytes) && TryReadName(bytes, out _);
    public static bool IsAddress(string address) => TryEncode(address, out var bytes) && TryReadAddress(bytes, out _);
    public static byte[] Encode(string value) => Encoding.GetBytes(value);

    public static int CreationCost(ScriptFileSystem scripts)
    {
        if (!scripts.FileExists("etc/guild.etc")) return 300000;
        var text = scripts.ReadAllText("etc/guild.etc");
        var match = Regex.Match(text, @"\[guild create cost\]\s+(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var cost) && cost >= 0
            ? cost : 300000;
    }

    private static bool TryDecode(byte[] bytes, int minimum, int maximum, out string value)
    {
        value = "";
        if (bytes.Length < minimum || bytes.Length > maximum) return false;
        try { value = Encoding.GetString(bytes); }
        catch (DecoderFallbackException) { return false; }
        return value.Length > 0 && !value.Any(char.IsControl)
            && Encoding.GetBytes(value).AsSpan().SequenceEqual(bytes);
    }

    private static bool TryEncode(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value)) return false;
        try { bytes = Encoding.GetBytes(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    private static Encoding CreateEncoding()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
