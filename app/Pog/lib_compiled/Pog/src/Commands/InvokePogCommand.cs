using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

// FIXME: when XmlDoc2CmdletDoc syntax is changed to something saner, format the list accordingly
/// <summary>Install a package from the package repository.</summary>
/// <para>Pog installs packages in four discrete steps:</para>
/// <para>1) The package manifest is downloaded from the package repository and placed into the package directory.
/// This step can be invoked separately using the `Import-Pog` cmdlet.</para>
/// <para>2) All package sources are downloaded and extracted to the `app` subdirectory inside the package (`Install-Pog` cmdlet).</para>
/// <para>3) The package setup script is executed. After this step, the package is usable by directly invoking the shortcuts
/// in the top-level package directory or the exported commands in the `.commands` subdirectory. (`Enable-Pog` cmdlet)</para>
/// <para>4) The shortcuts and commands are exported to the Start menu and to a directory on PATH, respectively. (`Export-Pog` cmdlet)</para>
///
/// <para>
/// The `Invoke-Pog` (typically invoked using the alias `pog`) installs a package from the package repository by running
/// all four installation stages in order, accepting the same arguments as <c>Import-Pog</c>.
/// This cmdlet is roughly equivalent to `Invoke-Pog @Args -PassThru | Install-Pog -PassThru | Enable-Pog -PassThru | Export-Pog`.
/// </para>
[PublicAPI]
[Alias("pog")]
[Cmdlet(VerbsLifecycle.Invoke, "Pog", DefaultParameterSetName = DefaultPS)]
[OutputType(typeof(ImportedPackage))]
public sealed class InvokePogCommand : ImportCommandBase {
    /// Import and install the package, do not enable and export.
    [Parameter] public SwitchParameter Install;

    /// Import, install and enable the package, do not export it.
    [Parameter] public SwitchParameter Enable;

    /// Return a [Pog.ImportedPackage] object with information about the installed package.
    [Parameter] public SwitchParameter PassThru;

    // TODO: add an `-Imported` parameter set to allow installing+enabling+exporting an imported package

    protected override void ProcessPackage(RepositoryPackage source, ImportedPackage target) {
        var imported = InvokePogCommand(new ImportPog(this) {
            SourcePackage = source,
            Package = target,
            Diff = Diff,
            Force = Force,
            Backup = true,
        });

        if (!imported) {
            // safe to return, manifest backup was not created
            return;
        }

        try {
            InvokePogCommand(new InstallPog(this) {Package = target});
        } catch {
            WriteVerbose("Install-Pog failed, rolling back previous manifest.");
            target.RestoreManifestBackup();
            throw;
        }

        // it doesn't make sense to keep the manifest backup for Enable & Export, as we don't know what state the package
        //  is left in case of an error, so rolling back could make the situation worse
        target.RemoveManifestBackup();

        SetupPackage(target);

        if (PassThru) {
            WriteObject(target);
        }
    }

    private void SetupPackage(ImportedPackage target) {
        if (Install) return;
        InvokePogCommand(new EnablePog(this) {Package = target}); // TODO: package arguments
        if (Enable) return;
        InvokePogCommand(new ExportPog(this) {Package = target});
    }
}
