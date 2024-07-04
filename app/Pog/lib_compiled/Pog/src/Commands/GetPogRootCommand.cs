using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;

namespace Pog.Commands;

/// <summary>Returns a list of absolute paths to registered package roots.</summary>
/// <para>
/// Returns a list of absolute paths to registered package roots. To change the list of package roots,
/// use the `Edit-PogRootList` cmdlet.
/// </para>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "PogRoot", DefaultParameterSetName = ValidPS)]
[OutputType(typeof(string))]
public sealed class GetPogRootCommand : PogCmdlet {
    private const string MissingPS = "Missing";
    private const string ValidPS = "Valid";

    /// Only list missing (invalid) package roots, which are registered, but do not exist in the filesystem.
    [Parameter(ParameterSetName = MissingPS)] public SwitchParameter Missing;

    /// Only list package roots that exist in the filesystem.
    [Parameter(ParameterSetName = ValidPS)] public SwitchParameter Valid;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var pr = InternalState.ImportedPackageManager.PackageRoots;
        WriteObjectEnumerable(Missing ? pr.MissingPackageRoots : Valid ? pr.ValidPackageRoots : pr.AllPackageRoots);
    }
}
