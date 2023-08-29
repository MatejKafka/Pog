using System;
using System.Collections.Generic;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.PSAttributes;
using Pog.Utils;

namespace Pog.Commands;

[PublicAPI]
[OutputType(typeof(ImportedPackage))]
public abstract class ImportedPackageCommand : PogCmdlet {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public ImportedPackage[] Package = null!;

    /// <summary><para type="description">
    /// Name of the package. This is the target name, not necessarily the manifest app name.
    /// </para></summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[] PackageName = null!;

    /// <summary><para type="description">
    /// Return a [Pog.ImportedPackage] object with information about the package.
    /// </para></summary>
    [Parameter] public SwitchParameter PassThru;

    #if DEBUG
    protected ImportedPackageCommand() {
        // validate that all inheriting cmdlets set DefaultParameterSetName
        var cmdletAttributes = this.GetType().GetCustomAttributes(typeof(CmdletAttribute), true);
        if (cmdletAttributes.Length != 1) {
            throw new InvalidOperationException($"Missing/repeated [Cmdlet] attribute on '{this.GetType()}'");
        }
        var attr = (CmdletAttribute)cmdletAttributes[0];
        if (attr.DefaultParameterSetName != DefaultPS) {
            throw new InvalidOperationException($"Incorrect 'DefaultParameterSetName' on '{this.GetType()}'");
        }
    }
    #endif

    private IEnumerable<ImportedPackage> EnumerateResolvedPackages(IEnumerable<string> packageNames) {
        return packageNames.SelectOptional(pn => {
            try {
                return InternalState.ImportedPackageManager.GetPackage(pn, true, true);
            } catch (ImportedPackageNotFoundException e) {
                WriteError(new ErrorRecord(e, "PackageNotFound", ErrorCategory.InvalidArgument, pn));
                return null;
            }
        });
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        var packages = ParameterSetName == PackagePS ? Package : EnumerateResolvedPackages(PackageName);
        // TODO: do this in parallel (even for packages passed as array)
        foreach (var package in packages) {
            ProcessPackage(package);

            if (PassThru) {
                WriteObject(package);
            }
        }
    }

    protected abstract void ProcessPackage(ImportedPackage package);
}
