using System.Collections.Generic;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.Utils;
using Pog.Utils.GitHub;

namespace Pog.Commands.ContainerCommands;

/// <summary>Lists all GitHub releases for the passed repository.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "GitHubRelease")]
[OutputType(typeof(GitHubRelease), typeof(GitHubTag))]
public sealed class GetGitHubReleaseCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)]
    [ValidatePattern(@"^[^/\s]+/[^/\s]+$")]
    public string Repository = null!;

    /// ScriptBlock to filter valid releases.
    [Parameter] public ScriptBlock? Filter;

    /// ScriptBlock that parses the raw tag name into a version string. Typically, this is not necessary.
    [Parameter] public ScriptBlock? Version;

    /// Tag name prefix to remove to get the raw version. By default, "v" prefix or no prefix is accepted.
    [Parameter] public string? TagPrefix;

    /// Retrieve tags instead of releases.
    [Parameter] public SwitchParameter Tags;

    private readonly GitHubApiClient _client = new(InternalState.HttpClient);
    private string? _apiToken;

    protected override void BeginProcessing() {
        base.BeginProcessing();
        var secureApiToken = ManifestGeneratorContainerContext.GetCurrent(this).GitHubToken;
        _apiToken = secureApiToken == null ? null : UnprotectSecureString(secureApiToken);
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();
        WriteObjectEnumerable(Tags ? EnumerateTags() : EnumerateReleases());
    }

    private string? ParseVersion(GitHubObject obj) {
        if (Version != null) {
            var rawResult = Version.InvokeWithContext(null, [new("_", obj)]);
            var result = LanguagePrimitives.ConvertTo<string?>(rawResult);
            return string.IsNullOrEmpty(result) ? null : result;
        } else {
            var tag = obj.GetTagName();
            // if TagPrefix is not explicitly set, also allow versions with no prefix (starting with a number)
            tag = tag.StripPrefix(TagPrefix ?? "v") ?? (TagPrefix == null ? tag : null);
            // if the prefix is missing or the resulting version does not start with a number, ignore it
            return string.IsNullOrEmpty(tag) || !char.IsDigit(tag![0]) ? null : tag;
        }
    }

    private IEnumerable<T> FilterEnumerable<T>(IAsyncEnumerable<T> enumerable) where T : GitHubObject {
        foreach (var obj in enumerable.ToBlockingEnumerable(CancellationToken)) {
            // parse tag name to generate version
            obj.SetVersion(ParseVersion(obj));
            // ignore releases with unparseable versions
            if (obj.VersionStr != null) {
                yield return obj;
            }
        }
    }

    private IEnumerable<GitHubRelease> EnumerateReleases() {
        return FilterEnumerable(_client.EnumerateReleasesAsync(Repository, _apiToken, CancellationToken));
    }

    private IEnumerable<GitHubTag> EnumerateTags() {
        return FilterEnumerable(_client.EnumerateTagsAsync(Repository, _apiToken, CancellationToken));
    }
}
