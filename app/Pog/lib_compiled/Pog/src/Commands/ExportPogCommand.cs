using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

// TODO: it probably makes sense to keep Enable-Pog and Export-Pog separate, as you might want to invoke each without the other;
//  however, we still want to have some form of automatic export in Enable-Pog; couldn't we use a heuristic like "if we're
//  creating a new export, there's nothing under that name yet, and our package already had something exported, export it"?

/// <summary>Exports shortcuts and commands from the package.</summary>
/// <para>
/// Exports shortcuts from the package to the start menu, and commands to an internal Pog directory that's available on $env:PATH.
/// </para>
[PublicAPI]
[Cmdlet(VerbsData.Export, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class ExportPogCommand() : ImportedPackageCommand(false) {
    protected override void ProcessPackage(ImportedPackage package) {
        InvokePogCommand(new ExportPog(this) {
            Package = package,
        });
    }
}
