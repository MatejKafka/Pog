using System;
using System.Management.Automation;
using Pog.InnerCommands.Common;

namespace Pog.Commands.Common;

public class PackageCommandBase : PogCmdlet {
#if DEBUG
    protected PackageCommandBase(string defaultPSName) {
        // validate that all inheriting cmdlets set DefaultParameterSetName
        var cmdletAttributes = this.GetType().GetCustomAttributes(typeof(CmdletAttribute), true);
        if (cmdletAttributes.Length != 1) {
            throw new InvalidOperationException($"Missing/repeated [Cmdlet] attribute on '{this.GetType()}'");
        }
        var attr = (CmdletAttribute) cmdletAttributes[0];
        if (attr.DefaultParameterSetName != defaultPSName) {
            throw new InvalidOperationException($"Incorrect 'DefaultParameterSetName' on '{this.GetType()}'");
        }
    }
#endif

    protected ImportedPackage? GetImportedPackage(string packageName) {
        try {
            return InternalState.ImportedPackageManager.GetPackage(packageName, true, true);
        } catch (ImportedPackageNotFoundException e) {
            WriteError(new ErrorRecord(e, "PackageNotFound", ErrorCategory.InvalidArgument, packageName));
            return null;
        }
    }

    protected RepositoryPackage? GetRepositoryPackage(string packageName, PackageVersion? version = null) {
        try {
            var vp = InternalState.Repository.GetPackage(packageName, true, true);
            if (version != null) {
                return vp.GetVersionPackage(version, true);
            } else {
                return vp.GetLatestPackage();
            }
        } catch (RepositoryPackageNotFoundException e) {
            WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, packageName);
        } catch (RepositoryPackageVersionNotFoundException e) {
            WriteError(e, "PackageVersionNotFound", ErrorCategory.ObjectNotFound, packageName);
        } catch (InvalidPackageNameException e) {
            WriteError(e, "InvalidPackageName", ErrorCategory.InvalidArgument, packageName);
        }
        return null;
    }
}
