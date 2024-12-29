using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

internal class DisablePog(PogCmdlet cmdlet) : VoidCommand(cmdlet) {
    [Parameter(Mandatory = true)] public ImportedPackage Package = null!;

    public override void Invoke() {
        Debug.Assert(Package.ManifestLoaded);

        // enumerate exported items and delete them
        RemoveExportedItems(Package);

        // if app has an optional Disable block, run it
        DisablePackage(Package);
    }

    private void DisablePackage(ImportedPackage package) {
        if (package.Manifest.Disable == null) {
            WriteVerbose($"Package '{package.PackageName}' does not have a Disable block.");
            return;
        }

        WriteInformation($"Disabling '{package.GetDescriptionString()}'...");

        var it = InvokePogCommand(new InvokeContainer(Cmdlet) {
            WorkingDirectory = package.Path,
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_Disable.psm1"],
            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            Variables = [new("this", package.Manifest.Raw, "Loaded manifest of the processed package")],
            Run = ps => ps.AddCommand("__main").AddArgument(package.Manifest),
        });

        // Disable scriptblock should not output anything, show a warning
        foreach (var o in it) {
            WriteWarning($"DISABLE: {o}");
        }
    }

    private void RemoveExportedItems(ImportedPackage package) {
        RemoveGloballyExportedCommands(package);
        RemoveGloballyExportedShortcuts(package);

        // remove the internal exports, prompting the user to stop the program if some of them are in use
        // FIXME: since these are binaries, OpenedFilesView does not see the handles, so we get just a generic error message,
        //  not sure why; RestartManager correctly detects them, maybe use it instead of OFV for exports specifically?
        var exportPath = package.ExportedCommandDirPath;
        while (true) {
            try {
                RemoveExportedCommands(package);
                RemoveExportedShortcuts(package);
                RemoveShortcutShims(package);
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
                WriteInformation($"Removed an exported shortcut '{shortcut.GetBaseName()}'.");
            } else {
                WriteVerbose($"Shortcut '{shortcut.GetBaseName()}' is not exported to the Start menu.");
            }
        }
    }

    private void RemoveGloballyExportedCommands(ImportedPackage p) {
        foreach (var command in p.EnumerateExportedCommands()) {
            var targetPath = GlobalExportUtils.GetCommandExportPath(command);
            if (command.FullName == FsUtils.GetSymbolicLinkTarget(targetPath)) {
                // found a matching command, delete it
                File.Delete(targetPath);
                WriteInformation($"Removed an exported command '{command.GetBaseName()}'.");
            } else {
                WriteVerbose($"Command '{command.GetBaseName()}' is not exported.");
            }
        }
    }
}
