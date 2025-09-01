using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>Imports a package manifest from the repository.</summary>
/// <para>
/// Imports a package from the repository by copying the package manifest to the target path.
/// To fully install a package (equivalent to e.g. `winget install`), use the `pog` command, or run the following stages
/// of installation manually by invoking `Install-Pog`, `Enable-Pog` and `Export-Pog`.
/// </para>
[PublicAPI]
[Cmdlet(VerbsData.Import, "Pog", DefaultParameterSetName = PackageName_PS, SupportsShouldProcess = true)]
[OutputType(typeof(ImportedPackage))]
public sealed class ImportPogCommand : ImportCommandBase {
    /// Return a [Pog.ImportedPackage] object with information about the imported package.
    [Parameter] public SwitchParameter PassThru;

    protected override void ProcessPackage(RepositoryPackage source, ImportedPackage target) {
        var imported = InvokePogCommand(new ImportPog(this) {
            SourcePackage = source,
            Package = target,
            Diff = Diff,
            Force = Force,
            Backup = false,
        });

        if (PassThru && imported) {
            WriteObject(target);
        }
    }
}
