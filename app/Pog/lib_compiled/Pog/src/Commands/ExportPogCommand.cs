using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Utils;

namespace Pog.Commands;

/// <summary>Exports shortcuts and commands from the package.</summary>
/// <para>
/// Exports shortcuts from the package to the start menu, and commands to an internal Pog directory that's available on $env:PATH.
/// </para>
[PublicAPI]
[Cmdlet(VerbsData.Export, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class ExportPogCommand() : ImportedPackageCommand(false) {
    protected override void ProcessPackage(ImportedPackage package) {
        ExportShortcuts(package);
        ExportCommands(package);
    }

    private void ExportShortcuts(ImportedPackage p) {
        foreach (var shortcut in p.EnumerateExportedShortcuts()) {
            var shortcutName = shortcut.GetBaseName();
            var target = GloballyExportedShortcut.FromLocal(shortcut.FullName);

            if (target.IsFromPackage(p)) {
                if (!target.UpdateFrom(shortcut)) {
                    WriteVerbose($"Shortcut '{shortcutName}' is already exported from this package.");
                    continue;
                }
            } else {
                var ownerPath = target.SourcePackagePath;
                if (target.OverwriteWith(shortcut)) {
                    WriteWarning($"Overwritten an existing shortcut '{shortcutName}' from package " +
                                 $"'{Path.GetFileName(ownerPath)}'.");
                } else {
                    // created new shortcut
                }
            }

            WriteInformation($"Exported shortcut '{shortcutName}' from '{p.PackageName}'.");
        }
    }

    private void ExportCommands(ImportedPackage p) {
        // ensure the export dir exists
        Directory.CreateDirectory(InternalState.PathConfig.ExportedCommandDir);

        foreach (var command in p.EnumerateExportedCommands()) {
            var cmdName = command.GetBaseName();
            var target = GloballyExportedCommand.FromLocal(command.FullName);

            if (command.FullName == target.Target) {
                WriteVerbose($"Command '{cmdName}' is already globally exported from this package.");
                continue;
            }

            var matchingCommands = target.EnumerateConflictingCommands().ToArray();
            if (matchingCommands.Length != 0) {
                WriteDebug($"Found {matchingCommands.Length} conflicting commands: {matchingCommands}");

                if (matchingCommands.Length > 1) {
                    WriteWarning($"Pog developers fucked something up, and there are multiple colliding commands for " +
                                 $"'{cmdName}', please send a bug report.");
                }

                var ownerPath = matchingCommands[0].SourcePackagePath;
                foreach (var collidingCmdPath in matchingCommands) {
                    collidingCmdPath.Delete();
                }

                WriteWarning($"Overwritten an existing command '{cmdName}' from package '{Path.GetFileName(ownerPath)}'.");
            }

            try {
                target.UpdateFrom(command);
            } catch (Exception e) {
                var category = e is UnauthorizedAccessException
                        ? ErrorCategory.PermissionDenied
                        : ErrorCategory.NotSpecified;
                ThrowTerminatingError(new CommandExportException(
                                $"Could not export command '{cmdName}' from '{p.PackageName}', symbolic link creation " +
                                $"failed: {e.Message}", e),
                        "CommandExportFailed", category, cmdName);
            }
            WriteInformation($"Exported command '{cmdName}' from '{p.PackageName}'.", null);
        }
    }

    public class CommandExportException(string message, Exception innerException) : Exception(message, innerException);
}
