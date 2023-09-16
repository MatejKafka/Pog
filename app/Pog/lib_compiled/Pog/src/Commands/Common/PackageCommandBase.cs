using System;
using System.Collections.Generic;
using System.Management.Automation;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.Commands.Common;

public abstract class PackageCommandBase : PogCmdlet {
    protected PackageCommandBase() {}

#if DEBUG
    // validate that all inheriting cmdlets set DefaultParameterSetName and SupportsShouldProcess
    protected PackageCommandBase(string defaultPSName) {
        var cmdletAttributes = this.GetType().GetCustomAttributes(typeof(CmdletAttribute), true);
        if (cmdletAttributes.Length != 1) {
            throw new InvalidOperationException($"Missing/repeated [Cmdlet] attribute on '{this.GetType()}'");
        }
        var attr = (CmdletAttribute) cmdletAttributes[0];
        if (attr.DefaultParameterSetName != defaultPSName) {
            throw new InvalidOperationException($"Incorrect 'DefaultParameterSetName' on '{this.GetType()}'");
        }
        if (!attr.SupportsShouldProcess) {
            throw new InvalidOperationException($"Missing 'SupportsShouldProcess' on '{this.GetType()}'");
        }
    }
#endif

    protected IEnumerable<ImportedPackage> GetImportedPackage(string[] packageName, string? packageRoot = null) {
        return packageName.SelectOptional(pn => GetImportedPackage(pn, packageRoot));
    }

    protected ImportedPackage? GetImportedPackage(string packageName, string? packageRoot = null) {
        try {
            if (packageRoot != null) {
                return InternalState.ImportedPackageManager.GetPackage(packageName, packageRoot, true, true, true);
            } else {
                return InternalState.ImportedPackageManager.GetPackage(packageName, true, true);
            }
        } catch (ImportedPackageNotFoundException e) {
            WriteError(new ErrorRecord(e, "PackageNotFound", ErrorCategory.InvalidArgument, packageName));
            return null;
        }
    }

    protected IEnumerable<RepositoryPackage> GetRepositoryPackage(string[] packageName, PackageVersion? version = null) {
        return packageName.SelectOptional(pn => GetRepositoryPackage(pn, version));
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
