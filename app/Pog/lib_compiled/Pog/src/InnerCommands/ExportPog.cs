using System;
using System.IO;
using System.Linq;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

internal sealed class ExportPog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet) {
    public override bool Invoke() {
        ExportShortcuts();
        ExportCommands();
        return true;
    }

    private void ExportShortcuts() {
        foreach (var shortcut in Package.EnumerateExportedShortcuts()) {
            var shortcutName = shortcut.GetBaseName();
            var target = GloballyExportedShortcut.FromLocal(shortcut.FullName);

            if (target.IsFromPackage(Package)) {
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

            WriteInformation($"Exported shortcut '{shortcutName}' from '{Package.PackageName}'.");
        }
    }

    private void ExportCommands() {
        // ensure the export dir exists
        Directory.CreateDirectory(InternalState.PathConfig.ExportedCommandDir);

        foreach (var command in Package.EnumerateExportedCommands()) {
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
                throw new CommandExportException($"Could not export command '{cmdName}' from '{Package.PackageName}', " +
                                                 $"symbolic link creation failed: {e.Message}", e);
            }
            WriteInformation($"Exported command '{cmdName}' from '{Package.PackageName}'.");
        }
    }

    public class CommandExportException(string message, Exception innerException) : Exception(message, innerException);
}
