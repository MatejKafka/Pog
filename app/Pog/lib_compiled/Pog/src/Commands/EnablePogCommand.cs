using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

public class EnableScriptFailedException(string message, Exception innerException) : Exception(message, innerException);

public class DuplicateManifestArgumentException(string message) : ArgumentException(message);

/// <summary>Enables an installed package to allow external usage.</summary>
/// <para>Enables an installed package, setting up required files and exporting public commands and shortcuts.</para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Enable, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class EnablePogCommand() : ImportedPackageCommand(true), IDynamicParameters {
    /// Extra parameters to pass to the Enable script in the package manifest. For interactive usage, prefer to use the
    /// automatically generated parameters on this command (e.g. instead of passing `@{Arg = Value}` to this parameter,
    /// pass `-_Arg Value` as a standard parameter to this cmdlet), which gives you autocomplete and early name/type checking.
    [Parameter(Position = 1)]
    public Hashtable? PackageArguments = null;

    private DynamicCommandParameters? _proxiedParams = null;

    public object? GetDynamicParameters() {
        // remove possible leftover from previous invocation
        _proxiedParams = null;

        ImportedPackage? package;
        try {
            package = GetDynamicParamPackage();
            // no package passed
            if (package == null) return null;
        } catch {
            // loading the passed package failed; it will be loaded again in the main body, so just ignore it for now
            //  to get consistent errors
            return null;
        }

        if (package.Manifest.Enable == null) {
            // behave as if the scriptblock had no parameters
            return _proxiedParams = new DynamicCommandParameters();
        } else {
            var paramBuilder = new DynamicCommandParameters.Builder(
                    "_", DynamicCommandParameters.ParameterCopyFlags.NoPosition, HandleUnknownAttribute);
            return _proxiedParams = paramBuilder.CopyParameters(package.Manifest.Enable);
        }
    }

    private Attribute? HandleUnknownAttribute(string paramName, Attribute unknownAttr) {
        WriteWarning($"Manifest ScriptBlock parameter forwarding doesn't handle the dynamic parameter attribute" +
                     $" '{unknownAttr.GetType()}', defined for parameter '{paramName}' of the manifest block.\nIf this" +
                     $" is something that you think you need as a package author, open a new issue and we'll see" +
                     $" what we can do.");
        return null;
    }

    private ImportedPackage? GetDynamicParamPackage() {
        if (Package == null && PackageName == null) {
            return null;
        }

        // TODO: make this work for multiple packages (probably by prefixing the parameter name with package name?)
        if ((Package?.Length ?? 0) + (PackageName?.Length ?? 0) > 1) {
            // more than one package, ignore package parameters
            return null;
        }

        // parameters are null-coalesced, because mandatory parameters are not enforced in dynamicparam
        // also, we cannot use EnumerateParameterPackages here, because it uses WriteError, which is not allowed in dynamicparam
        //  (a dumb design limitation, if you ask me)
        if (ParameterSetName == PackagePS) {
            if (Package == null || Package.Length == 0) return null;
            var package = Package[0];
            package.EnsureManifestIsLoaded();
            return package;
        } else {
            var pn = PackageName?.FirstOrDefault();
            if (pn == null) return null;
            return InternalState.ImportedPackageManager.GetPackage(pn, true, true);
        }
    }

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (PackageArguments != null) {
            if (MyInvocation.ExpectingInput) {
                throw new ArgumentException(
                        "-PackageArguments must not be passed when packages are passed through pipeline.");
            }
            if (PackageName?.Length > 1) {
                throw new ArgumentException(
                        "-PackageArguments must not be passed when -PackageName contains multiple package names.");
            }
            if (Package?.Length > 1) {
                throw new ArgumentException(
                        "-PackageArguments must not be passed when -Package contains multiple packages.");
            }
        }

        if (_proxiedParams != null) {
            var forwardedParams = _proxiedParams.Extract();
            if (PackageArguments == null) {
                PackageArguments = forwardedParams;
            } else {
                foreach (DictionaryEntry e in forwardedParams) {
                    if (!PackageArguments.ContainsKey(e.Key)) {
                        PackageArguments[e.Key] = e.Value;
                    } else {
                        throw new DuplicateManifestArgumentException(
                                $"The parameter '{e.Key}' was passed to '{MyInvocation.MyCommand.Name}' both using" +
                                " '-PackageParameters' and forwarded dynamic parameter. Each parameter must be present" +
                                " in at most one of these.");
                    }
                }
            }
        } else {
            // either the package manifest is invalid, or multiple packages were passed, or pipeline input is used
        }
    }

    protected override void ProcessPackage(ImportedPackage package) {
        if (package.Manifest.Enable == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Enable block.");
            return;
        }

        // TODO: consider whether to remove this print and instead prefix all logs from Env_Enable with the name of the package
        //  currently, when enabling many packages, the noise from this line makes it harder to see what actually changed
        WriteInformation($"Enabling {package.GetDescriptionString()}...");

        var ctx = new EnableContainerContext(package);
        var cmd = new InvokeContainer(this) {
            WorkingDirectory = package.Path,
            Context = ctx,
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_Enable.psm1"],
            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            Variables = [new("this", package.Manifest.Raw, "Loaded manifest of the processed package")],
            Run = ps => ps.AddCommand("__main").AddArgument(package.Manifest).AddArgument(PackageArguments ?? new()),
        };

        try {
            // Enable container should not output anything, show a warning
            foreach (var o in InvokePogCommand(cmd)) {
                WriteWarning($"ENABLE: {o}");
            }
        } catch (RuntimeException e) {
            // something failed inside the container

            // we need to run post-enable actions before the `ThrowTerminatingError` call below, because we might write
            //  something to the pipeline, which we shouldn't do after terminating it
            try {
                RemoveStaleExports(package, ctx);
            } catch (Exception cleanupException) {
                // don't throw, we'd lose the original exception
                WriteWarning($"Internal clean up after invoking Enable script failed: {cleanupException}");
            }

            ThrowTerminatingError(WrapContainerError(package, e), "EnableFailed", ErrorCategory.NotSpecified, package);
        }

        // here, we can safely let any exceptions bubble, unlike above
        RemoveStaleExports(package, ctx);
    }

    private EnableScriptFailedException WrapContainerError(ImportedPackage package, RuntimeException e) {
        var ii = e.ErrorRecord.InvocationInfo;
        // replace the position info with a custom listing, since the script path is missing
        var graphic = ii.PositionMessage.Substring(ii.PositionMessage.IndexOf('\n') + 1);
        var positionMsg = $"At {package.ManifestPath}, Enable:{ii.ScriptLineNumber}\n" + graphic;
        return new EnableScriptFailedException(
                $"Enable script for package '{package.PackageName}' failed. Please fix the package manifest or " +
                $"report the issue to the package maintainer:\n" +
                $"    {e.Message.Replace("\n", "\n    ")}\n\n" +
                $"    {positionMsg.Replace("\n", "\n    ")}\n", e);
    }

    /// Remove all exports that were not re-exported during the Enable invocation. This ensures that the resulting set
    /// of exports matches what the current manifest specifies.
    private void RemoveStaleExports(ImportedPackage package, EnableContainerContext ctx) {
        if (ctx.StaleShortcuts.Count > 0) RemoveStaleShortcuts(ctx.StaleShortcuts, package);
        if (ctx.StaleShortcutShims.Count > 0) RemoveStaleShortcutShims(ctx.StaleShortcutShims);
        if (ctx.StaleCommands.Count > 0) RemoveStaleCommands(ctx.StaleCommands);
    }

    private void RemoveStaleShortcuts(IEnumerable<string> staleShortcutPaths, ImportedPackage package) {
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
            if (globalShortcut.IsFromPackage(package)) {
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
            var globalCmd = GlobalExportUtils.GetCommandExportPath(path);
            if (FsUtils.GetSymbolicLinkTarget(globalCmd) == path) {
                File.Delete(globalCmd);
                WriteDebug("Removed globally exported command.");
            }

            WriteInformation($"Removed stale command '{Path.GetFileNameWithoutExtension(path)}'.");
        }
    }
}
