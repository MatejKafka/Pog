using System;
using System.Collections;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

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

        WriteInformation($"Enabling {package.GetDescriptionString()}...");

        var it = InvokePogCommand(new InvokeContainer(this) {
            WorkingDirectory = package.Path,
            Context = new EnableContainerContext(package),
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_Enable.psm1"],
            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            Variables = [new("this", package.Manifest.Raw, "Loaded manifest of the processed package")],
            Run = ps => ps.AddCommand("__main").AddArgument(package.Manifest).AddArgument(PackageArguments ?? new()),
        });

        try {
            // Enable container should not output anything, show a warning
            foreach (var o in it) {
                WriteWarning($"ENABLE: {o}");
            }
        } catch (RuntimeException e) {
            // something failed inside the container

            var ii = e.ErrorRecord.InvocationInfo;
            // replace the position info with a custom listing, since the script path is missing
            var graphic = ii.PositionMessage.Substring(ii.PositionMessage.IndexOf('\n') + 1);
            var positionMsg = $"At {package.ManifestPath}, Enable:{ii.ScriptLineNumber}\n" + graphic;

            var ee = new EnableScriptFailedException(
                    $"Enable script for package '{package.PackageName}' failed. Please fix the package manifest or " +
                    $"report the issue to the package maintainer:\n" +
                    $"    {e.Message.Replace("\n", "\n    ")}\n\n" +
                    $"    {positionMsg.Replace("\n", "\n    ")}\n", e);
            ThrowTerminatingError(ee, "EnableFailed", ErrorCategory.NotSpecified, package);
        }
    }

    private Attribute? HandleUnknownAttribute(string paramName, Attribute unknownAttr) {
        WriteWarning($"Manifest ScriptBlock parameter forwarding doesn't handle the dynamic parameter attribute" +
                     $" '{unknownAttr.GetType()}', defined for parameter '{paramName}' of the manifest block.\nIf this" +
                     $" is something that you think you need as a package author, open a new issue and we'll see" +
                     $" what we can do.");
        return null;
    }

    public class EnableScriptFailedException(string message, Exception innerException) : Exception(message, innerException);

    public class DuplicateManifestArgumentException(string message) : ArgumentException(message);
}
