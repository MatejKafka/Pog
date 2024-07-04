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
            // ensure the start menu dir exists
            Directory.CreateDirectory(PathConfig.StartMenuExportDir);

            var shortcutName = shortcut.GetBaseName();
            var targetPath = $"{PathConfig.StartMenuExportDir}\\{shortcut.Name}";
            var target = new FileInfo(targetPath);

            if (target.Exists) {
                if (FsUtils.FileContentEqual(shortcut, target)) {
                    WriteVerbose($"Shortcut '{shortcutName}' is already exported from this package.");
                    continue;
                } else {
                    WriteWarning($"Overwriting existing shortcut '{shortcutName}'...");
                }
            }

            shortcut.CopyTo(targetPath, true);
            WriteInformation($"Exported shortcut '{shortcutName}' from '{p.PackageName}'.", null);
        }
    }

    private void ExportCommands(ImportedPackage p) {
        foreach (var command in p.EnumerateExportedCommands()) {
            var cmdName = command.GetBaseName();
            var targetPath = Path.Combine(InternalState.PathConfig.ExportedCommandDir, command.Name);

            if (command.FullName == FsUtils.GetSymbolicLinkTarget(targetPath)) {
                WriteVerbose($"Command '{cmdName}' is already exported from this package.");
                continue;
            }

            var matchingCommands = Directory
                    .EnumerateFiles(InternalState.PathConfig.ExportedCommandDir, $"{cmdName}.*")
                    // filter out files with a dot before the extension (e.g. `arm-none-eabi-ld.bfd.exe`)
                    .Where(cmdPath => Path.GetFileNameWithoutExtension(cmdPath) == cmdName)
                    .ToArray();

            if (matchingCommands.Length != 0) {
                if (matchingCommands.Length > 1) {
                    WriteWarning("Pog developers fucked something up, and there are multiple colliding commands. " +
                                 "Plz send bug report.");
                }
                WriteWarning($"Overwriting existing command '{cmdName}'...");
                foreach (var collidingCmdPath in matchingCommands) File.Delete(collidingCmdPath);
            }

            try {
                FsUtils.CreateSymbolicLink(targetPath, command.FullName, false);
            } catch (Exception e) {
                var category = e is UnauthorizedAccessException
                        ? ErrorCategory.PermissionDenied
                        : ErrorCategory.NotSpecified;
                ThrowTerminatingError(new CommandExportException(
                                $"Could not export command '{cmdName}' from '{p.PackageName}', " +
                                $"symbolic link creation failed: {e.Message}", e),
                        "CommandExportFailed", category, cmdName);
            }
            WriteInformation($"Exported command '{cmdName}' from '{p.PackageName}'.", null);
        }
    }

    public class CommandExportException(string message, Exception innerException) : Exception(message, innerException);
}
