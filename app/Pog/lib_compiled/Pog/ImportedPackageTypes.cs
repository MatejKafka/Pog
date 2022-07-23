using System.Collections.Generic;
using IOPath = System.IO.Path;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

public class ImportedPackageNotFoundException : DirectoryNotFoundException {
    public ImportedPackageNotFoundException(string message) : base(message) {}
}

[PublicAPI]
public class PackageRootManager {
    public readonly PackageRootConfig PackageRoots;

    public string DefaultPackageRoot => PackageRoots.ValidPackageRoots[0];

    public PackageRootManager(PackageRootConfig packageRootConfig) {
        PackageRoots = packageRootConfig;
    }

    public ImportedPackage GetPackage(string packageName, bool resolveName, bool loadManifest) {
        var searchedPaths = new List<string>();
        foreach (var root in PackageRoots.ValidPackageRoots) {
            var path = IOPath.Combine(root, packageName);
            if (!Directory.Exists(path)) {
                searchedPaths.Add(path);
                continue;
            }
            if (resolveName) {
                packageName = PathUtils.GetResolvedChildName(root, packageName);
            }
            return new ImportedPackage(packageName, IOPath.Combine(root, packageName), loadManifest);
        }
        throw new ImportedPackageNotFoundException($"Could not find package '${packageName}' in known package directories."
                                                   + " Searched paths:`n" + string.Join("\n", searchedPaths));
    }

    public ImportedPackage GetPackage(string packageName, string packageRoot, bool resolveName, bool loadManifest) {
        if (resolveName) {
            packageName = PathUtils.GetResolvedChildName(packageRoot, packageName);
        }
        return new ImportedPackage(packageName, IOPath.Combine(packageRoot, packageName), loadManifest);
    }

    // FIXME: this should probably return a set (or at least filter the packages to skip collisions)
    public IEnumerable<string> EnumeratePackageNames() {
        return PackageRoots.ValidPackageRoots.SelectMany(PathUtils.EnumerateNonHiddenDirectoryNames);
    }

    public IEnumerable<ImportedPackage> EnumeratePackages(bool loadManifest) {
        return PackageRoots.ValidPackageRoots.SelectMany(r => PathUtils.EnumerateNonHiddenDirectoryNames(r)
                // do not resolve name, it already has the correct casing
                .Select(p => GetPackage(p, r, false, loadManifest)));
    }
}

/// <summary>
/// A class representing an imported package.
/// By default, the package is validated during initialization - it must exist and have a valid manifest, otherwise an exception is thrown.
/// </summary>
[PublicAPI]
public class ImportedPackage : Package {
    public PackageVersion? Version => Manifest.Version;
    [Hidden] public string? ManifestName => Manifest.Name;

    internal ImportedPackage(string packageName, string path, bool loadManifest = true) : base(packageName, path) {
        if (loadManifest) {
            // load the manifest to (partially) validate it and ensure the getters above won't throw
            ReloadManifest();
        }
    }
}