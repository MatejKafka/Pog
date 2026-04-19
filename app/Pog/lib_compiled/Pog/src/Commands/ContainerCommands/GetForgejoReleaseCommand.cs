using System.Management.Automation;
using System.Security;
using JetBrains.Annotations;
using Pog.Utils.GitHub;

namespace Pog.Commands.ContainerCommands;

/// <summary>
/// Lists all Forgejo releases for the passed repository and instance. Note that this cmdlet returns
/// types compatible with <see cref="GetGitHubAssetCommand"/> and other cmdlets for GitHub.
/// </summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "ForgejoRelease", DefaultParameterSetName = DefaultPS)]
[OutputType(typeof(GitHubRelease), typeof(GitHubTag))]
public sealed class GetForgejoReleaseCommand : GetReleaseCommandBase {
    /// Base URL of the Forgejo instance to use.
    /// For example, to use Codeberg, pass `https://codeberg.org`.
    [Parameter(Mandatory = true)]
    [ValidatePattern("^(http|https)://.*$")]
    public string Instance = null!;

    /// API token to the selected instance to use. In the generator environment, this is set automatically through
    /// <c>$PSDefaultParameterValues</c>. Generators should not need to set this explicitly, this is primarily
    /// for interactive usage outside the container.
    [Parameter] public SecureString? AccessToken;

    internal override GitHubApiClient CreateClient() {
        var apiToken = AccessToken == null ? null : UnprotectSecureString(AccessToken);
        if (apiToken != null) {
            WriteDebug($"Using an API token for '{Instance}'.");
        }
        return new(InternalState.HttpClient, apiToken, $"{Instance}/api/v1");
    }
}
