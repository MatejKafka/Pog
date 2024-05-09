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
        // TODO: figure out how to sensibly execute this on orphaned shortcuts/commands during Enable-Pog
        RemoveGloballyExportedCommands(package);
        RemoveGloballyExportedShortcuts(package);

        package.RemoveExportedCommands();
        package.RemoveExportedShortcuts();
        package.RemoveShortcutShims();
    }

    private void RemoveGloballyExportedShortcuts(ImportedPackage p) {
        foreach (var startMenuDir in new[] {PathConfig.StartMenuUserExportDir, PathConfig.StartMenuSystemExportDir}) {
            if (!Directory.Exists(startMenuDir)) {
                continue;
            }

            foreach (var shortcut in p.EnumerateExportedShortcuts()) {
                var shortcutName = shortcut.GetBaseName();
                var targetPath = Path.Combine(startMenuDir, shortcut.Name);
                var target = new FileInfo(targetPath);

                if (!target.Exists || !FsUtils.FileContentEqual(shortcut, target)) {
                    WriteVerbose($"Shortcut '{shortcutName}' is not exported in '{startMenuDir}'.");
                } else {
                    // found a matching shortcut, delete it
                    target.Delete();
                    WriteInformation($"Removed an exported shortcut '{shortcutName}'.");
                }
            }
        }
    }

    private void RemoveGloballyExportedCommands(ImportedPackage p) {
        foreach (var command in p.EnumerateExportedCommands()) {
            var cmdName = command.GetBaseName();
            var targetPath = Path.Combine(InternalState.PathConfig.ExportedCommandDir, command.Name);

            if (command.FullName == FsUtils.GetSymbolicLinkTarget(targetPath)) {
                // found a matching command, delete it
                File.Delete(targetPath);
                WriteInformation($"Removed an exported command '{cmdName}'.");
            } else {
                WriteVerbose($"Command '{cmdName}' is not exported.");
            }
        }
    }
}
