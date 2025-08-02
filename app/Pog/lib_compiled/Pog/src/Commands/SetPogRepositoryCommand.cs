using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

/// <summary>Sets the package repository used by other commands.</summary>
/// <remarks>
/// Not thread-safe. Do not call this concurrently with other Pog cmdlets from different runspaces, since some cmdlets
/// internally repeatedly access the repository and assume it won't change between accesses.
/// </remarks>
[PublicAPI]
[Cmdlet(VerbsCommon.Set, "PogRepository")]
public sealed class SetPogRepositoryCommand : PogCmdlet {
    // TODO: would be more elegant to pass uri or string, but if we made these into separate arguments, the user could
    //  not specify the priority; maybe we could take IRepository and provide an ArgumentTransformationAttribute?

    /// Ref (local path or remote URL) to repositories to use. The type of repository to use
    /// is distinguished based on a `http://` or `https://` prefix for remote repositories,
    /// anything else is treated as filesystem path to a local repository.
    [Parameter(Mandatory = true, Position = 0)]
    public string[] RepositoryReference = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var repos = RepositoryReference.Select(ParseRepositoryRef).ToArray();
        // if only a single repo is set, do not wrap it in RepositoryList to improve performance a bit for lookups
        // also, this is currently necessary for Update-PogRepository and similar cmdlets that can only operate on local repos
        InternalState.Repository = repos.Length > 1 ? new RepositoryList(repos) : repos[0];
    }

    private IRepository ParseRepositoryRef(string repoRef) {
        if (repoRef.StartsWith("http://") || repoRef.StartsWith("https://")) {
            var repo = new RemoteRepository(repoRef);
            WriteInformation($"Using a remote repository: {repo.Url}");
            return repo;
        } else {
            var repo = new LocalRepository(GetUnresolvedProviderPathFromPSPath(repoRef));
            WriteInformation($"Using a local repository: {repo.Path}");
            return repo;
        }
    }
}
