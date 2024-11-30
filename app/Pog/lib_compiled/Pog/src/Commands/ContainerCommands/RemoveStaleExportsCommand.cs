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

        var internalState = EnableContainerContext.GetCurrent(this);

        if (internalState.StaleShortcuts.Count > 0 || internalState.StaleShortcutShims.Count > 0) {
            WriteDebug("Removing stale shortcuts...");
            foreach (var path in internalState.StaleShortcuts) {
                if (FsUtils.FileExistsCaseSensitive(path)) {
                    // do not delete if the casing changed, but still print the message;
                    // this is pretty ugly and fragile, but we want to keep exactly the same output as if the name
                    //  of the command changed, not just the casing
                    File.Delete(path);
                }
                WriteInformation($"Removed stale shortcut '{Path.GetFileNameWithoutExtension(path)}'.");
            }
        }

        if (internalState.StaleShortcutShims.Count > 0) {
            WriteDebug("Removing stale shortcut shims...");
            foreach (var path in internalState.StaleShortcutShims) {
                if (FsUtils.FileExistsCaseSensitive(path)) {
                    // same as above
                    File.Delete(path);
                }
                WriteDebug($"Removed stale shortcut shim '{Path.GetFileNameWithoutExtension(path)}'.");
            }
        }

        if (internalState.StaleCommands.Count > 0) {
            WriteDebug("Removing stale commands...");
            foreach (var path in internalState.StaleCommands) {
                if (FsUtils.FileExistsCaseSensitive(path)) {
                    // same as above
                    File.Delete(path);
                }
                WriteInformation($"Removed stale command '{Path.GetFileNameWithoutExtension(path)}'.");
            }
        }
    }
}
