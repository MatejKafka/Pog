using System;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.Utils;
using Pog.Utils.GitHub;

namespace Pog.Commands.ContainerCommands;

/// <summary>Finds release assets for the passed repository matching the passed name patterns.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "GitHubAsset")]
public sealed class GetGitHubAssetCommand : PogCmdlet {
    // position intentionally not set to encourage pipelining
    /// Releases to find assets in.
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public GitHubRelease[] Release = null!;

    /// Wildcard pattern for the asset name.
    [Parameter(Mandatory = true, Position = 0)]
    public string[] Asset = null!;

    /// Wildcard pattern for the asset name. If not found, the resulting array contains a null.
    [Parameter]
    [Alias("Optional")]
    public string[]? OptionalAsset;

    /// Silently ignore releases that do not have all required assets. By default, a non-terminating error is returned.
    /// Prefer to exclude invalid releases manually before invoking this cmdlet.
    [Parameter] public SwitchParameter IgnoreMissing;

    protected override void ProcessRecord() {
        base.ProcessRecord();

        foreach (var r in Release) {
            if (r.Assets.Length == 0) {
                // ignore releases with no assets
                WriteVerbose($"Skipping release '{r.TagName}' with no assets.");
                continue;
            }

            var assets = Asset.Select(n => FindAsset(r, n, IgnoreMissing)).ToArray();
            if (assets.Any(a => a == null)) {
                // some asset is missing (FindAsset writes an error)
                WriteVerbose($"Skipping release '{r.TagName}' with missing assets.");
                continue;
            }

            WriteObject(new {
                Release = r,
                Version = r.Version,
                Asset = assets,
                OptionalAsset = OptionalAsset?.Select(n => FindAsset(r, n, true)).ToArray(),
            });
        }
    }

    private GitHubAsset? FindAsset(GitHubRelease release, string assetNamePattern, bool ignoreMissing) {
        var pattern = new WildcardPattern(assetNamePattern, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
        GitHubAsset? asset;
        try {
            asset = release.Assets.SingleOrDefault(a => pattern.IsMatch(a.Name));
        } catch (InvalidOperationException) {
            // ambiguous pattern, multiple assets matched
            var e = new Exception($"Ambiguous asset name, multiple assets matched '{assetNamePattern}' for release at " +
                                  $"'{release.HtmlUrl}'.");
            ThrowTerminatingError(e, "AmbiguousAssetPattern", ErrorCategory.InvalidData, release);
            throw new UnreachableException();
        }

        if (!ignoreMissing && asset == null) {
            var e = new Exception($"No asset matching '{assetNamePattern}' found for release at '{release.HtmlUrl}', " +
                                  $"ignoring the release.");
            WriteError(e, "AssetNotFound", ErrorCategory.ObjectNotFound, release);
        }
        return asset;
    }
}
