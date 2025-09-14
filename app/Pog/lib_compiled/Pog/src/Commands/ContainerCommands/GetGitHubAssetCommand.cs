using System;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Pog.Commands.Common;
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

    /// Treat patterns in `-Asset` and `-OptionalAsset` as regular expressions instead of wildcard patterns. This might be
    /// useful when asset file name changes between releases, and you need to match multiple different name formats.
    [Parameter] public SwitchParameter Regex;

    private IPattern[] _requiredPatterns = null!;
    private IPattern[]? _optionalPatterns;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        _requiredPatterns = Asset.Select(ToPattern).ToArray();
        _optionalPatterns = OptionalAsset?.Select(ToPattern).ToArray();
    }

    private IPattern ToPattern(string pattern) => Regex ? new RegexPattern(pattern) : new GlobPattern(pattern);

    protected override void ProcessRecord() {
        base.ProcessRecord();

        foreach (var r in Release) {
            if (r.Assets.Length == 0) {
                // ignore releases with no assets
                WriteVerbose($"Skipping release '{r.TagName}' with no assets.");
                continue;
            }

            var assets = _requiredPatterns.Select(n => FindAsset(r, n, IgnoreMissing)).ToArray();
            if (assets.Any(a => a == null)) {
                // some asset is missing (FindAsset writes an error)
                WriteVerbose($"Skipping release '{r.TagName}' with missing assets.");
                continue;
            }

            WriteObject(new {
                Release = r,
                Version = r.Version,
                Asset = assets,
                OptionalAsset = _optionalPatterns?.Select(n => FindAsset(r, n, true)).ToArray(),
            });
        }
    }

    private GitHubAsset? FindAsset(GitHubRelease release, IPattern pattern, bool ignoreMissing) {
        GitHubAsset? asset;
        try {
            asset = release.Assets.SingleOrDefault(a => pattern.IsMatch(a.Name));
        } catch (InvalidOperationException) {
            // ambiguous pattern, multiple assets matched
            var e = new Exception($"Ambiguous asset name, multiple assets matched '{pattern}' for release at " +
                                  $"'{release.HtmlUrl}'.");
            ThrowTerminatingError(e, "AmbiguousAssetPattern", ErrorCategory.InvalidData, release);
            throw new UnreachableException();
        }

        if (!ignoreMissing && asset == null) {
            var e = new Exception($"No asset matching '{pattern}' found for release at '{release.HtmlUrl}'.");
            WriteError(e, "AssetNotFound", ErrorCategory.ObjectNotFound, release);
        }
        return asset;
    }

    private interface IPattern {
        public bool IsMatch(string input);
    }

    private class RegexPattern(string pattern) : IPattern {
        private readonly Regex _pattern = new(AddAnchors(pattern), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        public bool IsMatch(string input) => _pattern.IsMatch(input);
        public override string ToString() => _pattern.ToString();

        /// Ensure that the pattern matches the whole string by adding ^ and $ if not already present.
        private static string AddAnchors(string pattern) {
            if (!pattern.StartsWith("^", StringComparison.Ordinal)) pattern = "^" + pattern;
            if (!pattern.EndsWith("$", StringComparison.Ordinal)) pattern += "$";
            return pattern;
        }
    }

    private class GlobPattern(string pattern) : IPattern {
        private readonly WildcardPattern _pattern = new(pattern,
                WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
        public bool IsMatch(string input) => _pattern.IsMatch(input);
        public override string ToString() => pattern;
    }
}
