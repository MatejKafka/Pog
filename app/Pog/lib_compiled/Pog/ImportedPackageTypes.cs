using System.Collections.Generic;
using IOPath = System.IO.Path;
using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

public class ImportedPackageNotFoundException : DirectoryNotFoundException {
    public ImportedPackageNotFoundException(string message) : base(message) {}
}

[PublicAPI]
public class PackageRoots {
    private PackageRootConfig _config;

    public PackageRoots(PackageRootConfig packageRootConfig) {
        _config = packageRootConfig;
    }

    public ImportedPackage GetPackage(string packageName, bool resolveName, bool loadManifest) {
        var searchedPaths = new List<string>();
        foreach (var root in _config.ValidPackageRoots) {
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