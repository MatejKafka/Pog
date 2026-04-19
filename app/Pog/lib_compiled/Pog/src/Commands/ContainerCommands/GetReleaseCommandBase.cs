using System.Collections.Generic;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Utils;
using Pog.Utils.GitHub;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
public abstract class GetReleaseCommandBase : PogCmdlet {
    protected const string DefaultPS = VersionPS;
    private const string VersionPS = "Version";
    private const string TagPrefixPS = "TagPrefix";

    [Parameter(Mandatory = true, Position = 0)]
    [ValidatePattern(@"^[^/\s]+/[^/\s]+$")]
    public string Repository = null!;

    /// ScriptBlock that parses the raw tag name into a version string. Typically, this is not necessary.
    [Parameter(ParameterSetName = VersionPS)]
    public ScriptBlock? Version;

    /// Tag name prefix to remove to get the raw version. By default, "v" prefix or no prefix is accepted.
    [Parameter(ParameterSetName = TagPrefixPS)]
    [Parameter] public string? TagPrefix;

    /// Retrieve tags instead of releases.
    [Parameter] public SwitchParameter Tags;

    private GitHubApiClient _client = null!;

    internal abstract GitHubApiClient CreateClient();

    protected override void BeginProcessing() {
        base.BeginProcessing();
        _client = CreateClient();
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
            } else {
                WriteVerbose($"Skipping release '{obj.GetTagName()}' in repository '{Repository}', " +
                             $"could not parse tag as a version. To change how the tag is parsed, " +
                             $"pass either `-Version` or `-TagPrefix`.");
            }
        }
    }

    private IEnumerable<GitHubRelease> EnumerateReleases() {
        return FilterEnumerable(_client.EnumerateReleasesAsync(Repository, CancellationToken));
    }

    private IEnumerable<GitHubTag> EnumerateTags() {
        return FilterEnumerable(_client.EnumerateTagsAsync(Repository, CancellationToken));
    }
}
