using System.Collections.Generic;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.PSAttributes;
using Pog.Utils;

namespace Pog.Commands;

[OutputType(typeof(ImportedPackage))]
public abstract class ImportedPackageCommand : PogCmdlet {
    [UsedImplicitly] protected const string PackagePS = "Package";
    [UsedImplicitly] protected const string PackageNamePS = "PackageName";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Package", ValueFromPipeline = true)]
    public ImportedPackage[] Package = null!;

    /// Name of the package. This is the target name, not necessarily the manifest app name.
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PackageName", ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[] PackageName = null!;

    /// Return a [Pog.ImportedPackage] object with information about the package.
    [Parameter] public SwitchParameter PassThru;

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

        var packages = ParameterSetName == PackageNamePS ? EnumerateResolvedPackages(PackageName) : Package;
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
