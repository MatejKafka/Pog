using System.Security;

namespace Pog;

internal class ManifestGeneratorContainerContext : Container.EnvironmentContext<ManifestGeneratorContainerContext> {
    public readonly SecureString? GitHubToken;

    internal ManifestGeneratorContainerContext(SecureString? githubToken) {
        GitHubToken = githubToken;
    }
}
