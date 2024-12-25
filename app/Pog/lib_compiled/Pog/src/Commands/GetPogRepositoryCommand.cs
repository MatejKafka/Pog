using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.Commands;

/// Returns the refs (paths/URLs) of all used package repositories.
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "PogRepository")]
[OutputType(typeof(string))]
public class GetPogRepositoryCommand : PogCmdlet {
    protected override void BeginProcessing() {
        base.BeginProcessing();

        var repo = InternalState.Repository;
        if (repo is RepositoryList rl) {
            WriteObjectEnumerable(rl.Repositories.Select(GetRepositoryRef));
        } else {
            WriteObject(GetRepositoryRef(repo));
        }
    }

    private string GetRepositoryRef(IRepository repo) {
        return repo switch {
            LocalRepository lr => lr.Path,
            RemoteRepository rr => rr.Url,
            _ => throw new UnreachableException(),
        };
    }
}
