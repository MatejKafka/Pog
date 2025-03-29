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
);

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
