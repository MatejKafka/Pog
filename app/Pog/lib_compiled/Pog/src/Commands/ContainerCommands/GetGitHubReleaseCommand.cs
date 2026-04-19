using System.Management.Automation;
using System.Security;
using JetBrains.Annotations;
using Pog.Utils.GitHub;

namespace Pog.Commands.ContainerCommands;

/// <summary>Lists all GitHub releases for the passed repository.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "GitHubRelease", DefaultParameterSetName = DefaultPS)]
[OutputType(typeof(GitHubRelease), typeof(GitHubTag))]
public sealed class GetGitHubReleaseCommand : GetReleaseCommandBase {
    /// GitHub API token to use. In the generator environment, this is set automatically through <c>$PSDefaultParameterValues</c>.
    /// Generators should not need to set this explicitly, this is primarily for interactive usage outside the container.
    [Parameter] public SecureString? AccessToken;

    internal override GitHubApiClient CreateClient() {
        var apiToken = AccessToken == null ? null : UnprotectSecureString(AccessToken);
        if (apiToken != null) {
            WriteDebug("Using a GitHub API token.");
        }
        return new(InternalState.HttpClient, apiToken);
    }
}
