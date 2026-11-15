using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace DFLegacy.UpdateCore;

public sealed record ReleaseSource(string Provider, string Repository, string Channel, string Architecture, string Flavor)
{
    public const string GiteaOrigin = "http://firekula.site:3000";
    [JsonIgnore] public string Identity => $"{Provider}:{Repository}:{Channel}:{Architecture}:{Flavor}";
    [JsonIgnore] public string ManifestAssetName => $"DFLegacy-client-update-v4-{Channel}-{Flavor}-win-{Architecture}.json";
    public void Validate()
    {
        if ((Provider == "local" ? !IsLocalRepository(Repository)
                : Provider is not ("github" or "gitee" or "gitea" or "gitcode") || Repository != "firekula/DFLegacy") || !ManifestCodec.IsName(Channel)
            || Provider == "gitea" && Channel != "gitea-test"
            || Provider == "gitcode" && Channel != "chapter-two"
            || Architecture is not ("x86" or "x64") || Flavor is not ("normal" or "test")) throw new InvalidDataException("更新源配置无效。");
    }

    private static bool IsLocalRepository(string repository) =>
        repository is { Length: < 1024 }
        && System.Text.RegularExpressions.Regex.IsMatch(repository,
            @"\Ahttp://127\.0\.0\.2:[1-9][0-9]{0,4}(?:/[A-Za-z0-9_-]+)*\z")
        && Uri.TryCreate(repository, UriKind.Absolute, out var uri) && uri.Port is > 0 and <= 65535;
}
public sealed record VerifiedRelease(ReleaseSource Source, string Tag, string ManifestSha256, byte[] ManifestBytes)
{
    [JsonIgnore] public ClientManifest Manifest => ManifestCodec.Parse(ManifestBytes);
    public void Validate(ReleaseSource expected)
    {
        Source.Validate(); expected.Validate();
        if (Source != expected || !ManifestCodec.IsName(Tag) || ManifestBytes == null || ManifestBytes.Length > ManifestCodec.MaxBytes
            || !ManifestCodec.IsHash(ManifestSha256) || Convert.ToHexStringLower(SHA256.HashData(ManifestBytes)) != ManifestSha256)
            throw new InvalidDataException("更新目标来源或清单身份不符。");
        var manifest = Manifest;
        if (manifest.Version != Tag || manifest.Channel != Source.Channel || manifest.Architecture != Source.Architecture || manifest.Flavor != Source.Flavor)
            throw new InvalidDataException("更新目标的版本/渠道/架构与配置不一致。");
        _ = ConfigurationMigration.ValidateReleaseBindings(manifest.Migrations, Source);
    }
}
public interface IReleaseProvider : IResourceTransport
{
    ReleaseSource Source { get; }
    Task<VerifiedRelease?> FindAsync(CancellationToken token = default);
}
