using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Internal;
using Pog.Utils;

namespace Pog.Commands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Disable, "Pog", DefaultParameterSetName = DefaultPS)]
public class DisablePogCommand : ImportedPackageCommand {
    protected override void ProcessPackage(ImportedPackage package) {
        package.EnsureManifestIsLoaded();

        // enumerate exported items and delete them
        RemoveExportedItems(package);

        // if app has an optional Disable block, run it
        DisablePackage(package);
    }

    private void DisablePackage(ImportedPackage package) {
        if (package.Manifest.Disable == null) {
            WriteVerbose($"Package '{package.PackageName}' does not have a Disable block.");
            return;
        }

        WriteInformation($"Disabling '{package.GetDescriptionString()}'...");

        // FIXME: probably discard container output, it breaks -PassThru
        WriteObjectEnumerable(InvokePogCommand(new InvokeContainer(this) {
            ContainerType = Container.ContainerType.Disable,
            Package = package,
        }));
    }

    // TODO: figure out how to sensibly execute this on orphaned shortcuts/commands during Enable-Pog
    private void RemoveExportedItems(ImportedPackage package) {
        RemoveExportedCommands(package);
        RemoveExportedShortcuts(package);
    }

    private void RemoveExportedShortcuts(ImportedPackage p) {
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

    private void RemoveExportedCommands(ImportedPackage p) {
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
