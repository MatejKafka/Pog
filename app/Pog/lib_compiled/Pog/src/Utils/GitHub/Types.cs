using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Pog.Utils.GitHub;

[PublicAPI]
public record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")]
        string Url,
        string ContentType,
        ulong Size,
        ulong DownloadCount
) {
    private string? _hash;

    [JsonPropertyName("digest")]
    public string? Hash {
        get => _hash;
        // serialized as a string containing `sha256:...hash...`, only available for releases after 2025-06-03
        set => _hash = value switch {
            null => _hash = null,
            _ when value.StartsWith("sha256:") => value.Substring("sha256:".Length).ToUpperInvariant(),
            _ => null,
        };
    }
}

/// Internal object to unify Git tag parsing.
public abstract record GitHubObject {
    /// Cleaned version string from `TagName` by removing the tag prefix ("v" by default).
    public string? VersionStr {get; private set;}

    /// Parsed release version.
    public PackageVersion? Version {get; private set;}

    internal GitHubObject() {}

    // we need to do this instead of declaring TagName as a property here, because each subclass needs different json attributes
    internal abstract string GetTagName();

    // version parsing is done in GitHubCommandBase, because it depends on cmdlet parameters
    internal void SetVersion(string? version) {
        VersionStr = version;
        Version = version is null ? null : new(version);
    }
}

[PublicAPI]
public record GitHubRelease(
        string Name,
        string TagName,
        bool Draft,
        bool Prerelease,
        string HtmlUrl,
        string Body,
        GitHubAsset[] Assets) : GitHubObject {
    internal override string GetTagName() => TagName;
}

[PublicAPI]
public record GitHubTag([property: JsonPropertyName("name")] string TagName) : GitHubObject {
    internal override string GetTagName() => TagName;
}
