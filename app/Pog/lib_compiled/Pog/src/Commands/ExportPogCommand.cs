using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Utils;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Exports shortcuts and commands from the package.</para>
/// <para type="description">
/// Exports shortcuts from the package to the start menu, and commands to an internal Pog directory that's available on $env:PATH.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsData.Export, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public class ExportPogCommand : ImportedPackageCommand {
    /// <summary><para type="description">
    /// Export shortcuts to the system-wide start menu for all users, instead of the user-specific start menu.
    /// </para></summary>
    [Parameter] public SwitchParameter Systemwide;

    private string _startMenuDir = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        _startMenuDir = Systemwide ? PathConfig.StartMenuSystemExportDir : PathConfig.StartMenuUserExportDir;
        // ensure the dir exists
        Directory.CreateDirectory(_startMenuDir);
    }

    protected override void ProcessPackage(ImportedPackage package) {
        ExportShortcuts(package);
        ExportCommands(package);
    }

    private void ExportShortcuts(ImportedPackage p) {
        foreach (var shortcut in p.EnumerateExportedShortcuts()) {
            var shortcutName = shortcut.GetBaseName();
            var targetPath = Path.Combine(_startMenuDir, shortcut.Name);
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
        // TODO: check if $PATH_CONFIG.ExportedCommandDir is in PATH, and warn the user if it's not
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

            FsUtils.CreateSymbolicLink(targetPath, command.FullName, false);
            WriteInformation($"Exported command '{cmdName}' from '{p.PackageName}'.", null);
        }
    }
}
