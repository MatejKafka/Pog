using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;

namespace Pog.Commands.ContainerCommands;

// TODO: maybe change Export-Pog to create a marker that "user wants this package exported",
// TODO: probably also remove exports of the stale commands/shortcuts
//  and then handle updates of the exported items in Enable-Pog?
[PublicAPI]
[Cmdlet(VerbsCommon.Remove, "StaleExports")]
public class RemoveStaleExportsCommand : PogCmdlet {
    protected override void BeginProcessing() {
        base.BeginProcessing();

        var ctx = EnableContainerContext.GetCurrent(this);

        if (ctx.StaleShortcuts.Count > 0 || ctx.StaleShortcutShims.Count > 0) {
            WriteDebug("Removing stale shortcuts...");
            foreach (var path in ctx.StaleShortcuts) {
                if (FsUtils.FileExistsCaseSensitive(path)) {
                    File.Delete(path);
                } else {
                    // do not delete if the casing changed, but still print the message; this is pretty ugly and fragile,
                    // but we want to keep exactly the same output as if the name of the command changed, not just the casing
                }

                // delete the globally exported shortcut, if there's any
                var globalShortcut = GloballyExportedShortcut.FromLocal(path);
                if (globalShortcut.IsFromPackage(ctx.Package)) {
                    globalShortcut.Delete();
                    WriteDebug("Removed globally exported shortcut.");
                }

                WriteInformation($"Removed stale shortcut '{Path.GetFileNameWithoutExtension(path)}'.");
            }
        }

        if (ctx.StaleShortcutShims.Count > 0) {
            WriteDebug("Removing stale shortcut shims...");
            foreach (var path in ctx.StaleShortcutShims) {
                if (FsUtils.FileExistsCaseSensitive(path)) {
                    // same as above
                    File.Delete(path);
                }
                WriteDebug($"Removed stale shortcut shim '{Path.GetFileNameWithoutExtension(path)}'.");
            }
        }

        // TODO: also remove global exports here
        if (ctx.StaleCommands.Count > 0) {
            WriteDebug("Removing stale commands...");
            foreach (var path in ctx.StaleCommands) {
                if (FsUtils.FileExistsCaseSensitive(path)) {
                    // same as above
                    File.Delete(path);
                }

                // delete the globally exported command, if there's any
                var globalCmd = GlobalExportUtils.GetCommandExportPath(path);
                if (FsUtils.GetSymbolicLinkTarget(globalCmd) == path) {
                    File.Delete(globalCmd);
                    WriteDebug("Removed globally exported command.");
                }

                WriteInformation($"Removed stale command '{Path.GetFileNameWithoutExtension(path)}'.");
            }
        }
    }
}
