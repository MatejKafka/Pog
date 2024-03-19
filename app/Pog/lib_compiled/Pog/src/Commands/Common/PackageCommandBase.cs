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

    protected IEnumerable<ImportedPackage> GetImportedPackage(IEnumerable<string> packageName, bool loadManifest) {
        return GetImportedPackage(packageName, null, loadManifest);
    }

    protected IEnumerable<ImportedPackage> GetImportedPackage(IEnumerable<string> packageName, string? packageRoot,
            bool loadManifest) {
        return packageName.SelectOptional(pn => GetImportedPackage(pn, packageRoot, loadManifest));
    }

    protected ImportedPackage? GetImportedPackage(string packageName, bool loadManifest) {
        return GetImportedPackage(packageName, null, loadManifest);
    }

    protected ImportedPackage? GetImportedPackage(string packageName, string? packageRoot, bool loadManifest) {
        try {
            if (packageRoot != null) {
                return InternalState.ImportedPackageManager.GetPackage(packageName, packageRoot, true, loadManifest, true);
            } else {
                return InternalState.ImportedPackageManager.GetPackage(packageName, true, loadManifest);
            }
        } catch (ImportedPackageNotFoundException e) {
            WriteError(new ErrorRecord(e, "PackageNotFound", ErrorCategory.InvalidArgument, packageName));
            return null;
        } catch (Exception e) when (e is IPackageManifestException) {
            WriteError(new ErrorRecord(e, "InvalidPackageManifest", ErrorCategory.InvalidData, packageName));
            return null;
        }
    }

    protected IEnumerable<ImportedPackage> EnsureManifestIsLoaded(IEnumerable<ImportedPackage> packages) {
        return packages.SelectOptional(EnsureManifestIsLoaded);
    }

    protected ImportedPackage? EnsureManifestIsLoaded(ImportedPackage p) {
        try {
            p.EnsureManifestIsLoaded();
            return p;
        } catch (Exception e) when (e is IPackageManifestException) {
            WriteError(new ErrorRecord(e, "InvalidPackageManifest", ErrorCategory.InvalidData, p));
            return null;
        }
    }

    protected IEnumerable<RepositoryPackage> GetRepositoryPackage(IEnumerable<string> packageName,
            PackageVersion? version = null) {
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
