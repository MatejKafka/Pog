using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Pog.Utils;

namespace Pog;

[PublicAPI]
public class ImportedPackageManager(PackageRootConfig packageRootConfig) {
    public readonly PackageRootConfig PackageRoots = packageRootConfig;

    public string DefaultPackageRoot => PackageRoots.ValidPackageRoots[0];

    // FIXME: we should resolve the package root path to the correct casing
    public string ResolveValidPackageRoot(string path) {
        if (PackageRoots.ValidPackageRoots.Contains(path)) {
            return path;
        }
        if (PackageRoots.MissingPackageRoots.Contains(path)) {
            throw new InvalidPackageRootException(
                    $"The passed package root is registered, but the directory is missing: {path}");
        } else {
            throw new InvalidPackageRootException($"The passed package root is not registered: {path}");
        }
    }

    /// Same as `GetPackage`, but if the package is not found, a non-existent package in the default package root is returned.
    public ImportedPackage GetPackageDefault(string packageName, bool resolveName, bool loadManifest) {
        Verify.PackageName(packageName);

        var roots = PackageRoots.ValidPackageRoots;
        var selectedRoot = roots.FirstOrDefault(r => Directory.Exists(Path.Combine(r, packageName))) ?? roots[0];
        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(selectedRoot, packageName);
        }
        return new ImportedPackage(packageName, Path.Combine(selectedRoot, packageName), loadManifest);
    }

    /// <exception cref="ImportedPackageNotFoundException"></exception>
    public ImportedPackage GetPackage(string packageName, bool resolveName, bool loadManifest) {
        Verify.PackageName(packageName);

        var searchedPaths = new List<string>();
        foreach (var root in PackageRoots.ValidPackageRoots) {
            var path = Path.Combine(root, packageName);
            if (!Directory.Exists(path)) {
                searchedPaths.Add(path);
                continue;
            }
            if (resolveName) {
                packageName = FsUtils.GetResolvedChildName(root, packageName);
            }
            return new ImportedPackage(packageName, Path.Combine(root, packageName), loadManifest);
        }
        throw new ImportedPackageNotFoundException($"Could not find package '{packageName}' in known package directories."
                                                   + " Searched paths:\n    " + string.Join("\n    ", searchedPaths));
    }

    /// Assumes that the package root is valid, if not null.
    /// <exception cref="ImportedPackageNotFoundException"></exception>
    public ImportedPackage GetPackage(string packageName, string? packageRoot, bool resolveName, bool loadManifest) {
        if (packageRoot == null) {
            return GetPackage(packageName, resolveName, loadManifest);
        } else {
            return GetPackage(packageName, packageRoot, resolveName, loadManifest, true);
        }
    }

    /// Assumes that the package root is valid.
    /// <exception cref="ImportedPackageNotFoundException"></exception>
    public ImportedPackage GetPackage(string packageName, string packageRoot,
            bool resolveName, bool loadManifest, bool mustExist) {
        Verify.PackageName(packageName);
        Debug.Assert(ResolveValidPackageRoot(packageRoot) == packageRoot);

        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(packageRoot, packageName);
        }

        var p = new ImportedPackage(packageName, Path.Combine(packageRoot, packageName), false);

        if (mustExist && !p.Exists) {
            throw new ImportedPackageNotFoundException(
                    $"Could not find package '{p.PackageName}' at package root '{packageRoot}'. Searched path: {p.Path}");
        }

        if (loadManifest && p.Exists) {
            p.ReloadManifest();
        }
        return p;
    }

    // FIXME: this should probably return a set (or at least filter the packages to skip collisions)
    public IEnumerable<string> EnumeratePackageNames(string namePattern = "*") {
        return PackageRoots.ValidPackageRoots.SelectMany(r => FsUtils.EnumerateNonHiddenDirectoryNames(r, namePattern));
    }

    public IEnumerable<ImportedPackage> Enumerate(bool loadManifest, string namePattern = "*") {
        return PackageRoots.ValidPackageRoots.SelectMany(r => DoEnumerate(r, loadManifest, namePattern));
    }

    /// Assumes that the package root is valid.
    public IEnumerable<ImportedPackage> Enumerate(string? packageRoot, bool loadManifest, string namePattern = "*") {
        if (packageRoot == null) {
            return Enumerate(loadManifest, namePattern);
        }

        Debug.Assert(ResolveValidPackageRoot(packageRoot) == packageRoot);
        return DoEnumerate(packageRoot, loadManifest, namePattern);
    }

    private IEnumerable<ImportedPackage> DoEnumerate(string packageRoot, bool loadManifest, string namePattern = "*") {
        return FsUtils.EnumerateNonHiddenDirectoryNames(packageRoot, namePattern)
                // do not resolve name, it already has the correct casing
                .Select(p => GetPackage(p, packageRoot, false, loadManifest, false));
    }
}
