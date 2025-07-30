using System;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

/// Removes both local and global exports of the package.
internal class UnexportPog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet) {
    public override void Invoke() {
        RemoveGloballyExportedCommands(Package);
        RemoveGloballyExportedShortcuts(Package);

        // remove the internal exports, prompting the user to stop the program if some of them are in use
        // FIXME: since these are binaries, OpenedFilesView does not see the handles, so we get just a generic error message,
        //  not sure why; RestartManager correctly detects them, maybe use it instead of OFV for exports specifically?
        var exportPath = Package.ExportedCommandDirPath;
        while (true) {
            try {
                RemoveExportedCommands(Package);
                RemoveExportedShortcuts(Package);
                RemoveShortcutShims(Package);
                break;
            } catch (UnauthorizedAccessException) {
                InvokePogCommand(new ShowLockedFileList(Cmdlet) {
                    Path = exportPath,
                    MessagePrefix = "Cannot remove exported entry points,",
                    Wait = true,
                });
            }
        }
    }

    private static void RemoveExportedShortcuts(ImportedPackage p) {
        // shortcut dir is the root of the package, delete the shortcuts one-by-one instead of deleting the whole directory
        foreach (var shortcut in p.EnumerateExportedShortcuts()) {
            shortcut.Delete();
        }
    }

    private static void RemoveExportedCommands(ImportedPackage p) {
        FsUtils.EnsureDeleteDirectory(p.ExportedCommandDirPath);
    }

    private static void RemoveShortcutShims(ImportedPackage p) {
        FsUtils.EnsureDeleteDirectory(p.ExportedShortcutShimDirPath);
    }

    private void RemoveGloballyExportedShortcuts(ImportedPackage p) {
        foreach (var shortcut in p.EnumerateExportedShortcuts()) {
            var globalShortcut = GloballyExportedShortcut.FromLocal(shortcut.FullName);
            if (globalShortcut.IsFromPackage(p)) {
                globalShortcut.Delete();
                WriteInformation($"Removed an exported shortcut '{shortcut.GetBaseName()}' from the Start menu.");
            } else {
                WriteVerbose($"Shortcut '{shortcut.GetBaseName()}' is not exported to the Start menu.");
            }
        }
    }

    private void RemoveGloballyExportedCommands(ImportedPackage p) {
        foreach (var command in p.EnumerateExportedCommands()) {
            var globalCommand = GloballyExportedCommand.FromLocal(command.FullName);
            if (globalCommand.IsFromPackage(p)) {
                globalCommand.Delete();
                WriteInformation($"Removed an exported command '{command.GetBaseName()}' from PATH.");
            } else {
                WriteVerbose($"Command '{command.GetBaseName()}' is not exported to PATH.");
            }
        }
    }
}
