using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using Pog.Commands;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

internal sealed class EnablePog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet) {
    [Parameter] public Hashtable? PackageArguments = null;

    public override bool Invoke() {
        if (Package.Manifest.Enable == null) {
            WriteInformation($"Package '{Package.PackageName}' does not have an Enable block.");
            return false;
        }

        // TODO: consider whether to remove this print and instead prefix all logs from Env_Enable with the name of the package
        //  currently, when enabling many packages, the noise from this line makes it harder to see what actually changed
        WriteInformation($"Enabling {Package.GetDescriptionString()}...");

        var ctx = new EnableContainerContext(Package);
        var cmd = new InvokeContainer(Cmdlet) {
            WorkingDirectory = Package.Path,
            Context = ctx,
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_Enable.psm1"],
            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            Variables = [new("this", Package.Manifest.Raw, "Loaded manifest of the processed package")],
            Run = ps => ps.AddCommand("__main").AddArgument(Package.Manifest).AddArgument(PackageArguments ?? new()),
        };

        try {
            // Enable container should not output anything, show a warning
            foreach (var o in InvokePogCommand(cmd)) {
                WriteWarning($"ENABLE: {o}");
            }
        } catch (RuntimeException e) {
            // something failed inside the container

            // we need to run post-enable actions before the `throw` below, because we might write
            //  something to the pipeline, which we shouldn't do after terminating it
            try {
                RemoveStaleExports(ctx);
            } catch (Exception cleanupException) {
                // don't throw, we'd lose the original exception
                WriteWarning($"Internal clean up after invoking Enable script failed: {cleanupException}");
            }

            throw WrapContainerError(e);
        }

        // here, we can safely let any exceptions bubble, unlike above
        RemoveStaleExports(ctx);
        return true;
    }

    // FIXME: in "NormalView" error view, the error looks slightly confusing, as it's designed for "ConciseView"
    private EnableScriptFailedException WrapContainerError(RuntimeException e) {
        var ii = e.ErrorRecord.InvocationInfo;
        // replace the position info with a custom listing, since the script path is missing
        var graphic = ii.PositionMessage.Substring(ii.PositionMessage.IndexOf('\n') + 1);
        var positionMsg = $"At {Package.ManifestPath}, Enable:{ii.ScriptLineNumber}\n" + graphic;
        return new EnableScriptFailedException(
                $"Failed to setup package '{Package.PackageName}'. If it's a transient issue, re-run the setup, otherwise " +
                $"please fix the package manifest or report the issue to the package maintainer:\n" +
                $"    {e.Message.Replace("\n", "\n    ")}\n\n" +
                $"    {positionMsg.Replace("\n", "\n    ")}\n", e);
    }

    /// Remove all exports that were not re-exported during the Enable invocation. This ensures that the resulting set
    /// of exports matches what the current manifest specifies.
    private void RemoveStaleExports(EnableContainerContext ctx) {
        if (ctx.StaleShortcuts.Count > 0) RemoveStaleShortcuts(ctx.StaleShortcuts);
        if (ctx.StaleShortcutShims.Count > 0) RemoveStaleShortcutShims(ctx.StaleShortcutShims);
        if (ctx.StaleCommands.Count > 0) RemoveStaleCommands(ctx.StaleCommands);
    }

    private void RemoveStaleShortcuts(IEnumerable<string> staleShortcutPaths) {
        WriteDebug("Removing stale shortcuts...");
        foreach (var path in staleShortcutPaths) {
            if (FsUtils.FileExistsCaseSensitive(path)) {
                File.Delete(path);
            } else {
                // do not delete if the casing changed, but still print the message; this is pretty ugly and fragile,
                // but we want to keep exactly the same output as if the name of the command changed, not just the casing
            }

            // delete the globally exported shortcut, if there's any
            var globalShortcut = GloballyExportedShortcut.FromLocal(path);
            if (globalShortcut.IsFromPackage(Package)) {
                globalShortcut.Delete();
                WriteDebug("Removed globally exported shortcut.");
            }

            WriteInformation($"Removed stale shortcut '{Path.GetFileNameWithoutExtension(path)}'.");
        }
    }

    private void RemoveStaleShortcutShims(IEnumerable<string> staleShortcutShimPaths) {
        WriteDebug("Removing stale shortcut shims...");
        foreach (var path in staleShortcutShimPaths) {
            if (FsUtils.FileExistsCaseSensitive(path)) {
                // same as above
                File.Delete(path);
            }
            WriteDebug($"Removed stale shortcut shim '{Path.GetFileNameWithoutExtension(path)}'.");
        }
    }

    private void RemoveStaleCommands(IEnumerable<string> staleCommandPaths) {
        WriteDebug("Removing stale commands...");
        foreach (var path in staleCommandPaths) {
            if (FsUtils.FileExistsCaseSensitive(path)) {
                // same as above
                File.Delete(path);
            }

            // delete the globally exported command, if there's any
            var globalCommand = GloballyExportedCommand.FromLocal(path);
            if (globalCommand.IsFromPackage(Package)) {
                globalCommand.Delete();
                WriteDebug("Removed globally exported command.");
            }

            WriteInformation($"Removed stale command '{Path.GetFileNameWithoutExtension(path)}'.");
        }
    }
}
