using System;
using System.Collections.Generic;
using System.Diagnostics;
using IOPath = System.IO.Path;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

public class ImportedPackageNotFoundException : DirectoryNotFoundException {
    public ImportedPackageNotFoundException(string message) : base(message) {}
}

public class PackageRootNotValidException : ArgumentException {
    public PackageRootNotValidException(string message) : base(message) {}
}

[PublicAPI]
public class ImportedPackageManager {
    public readonly PackageRootConfig PackageRoots;

    public string DefaultPackageRoot => PackageRoots.ValidPackageRoots[0];

    public ImportedPackageManager(PackageRootConfig packageRootConfig) {
        PackageRoots = packageRootConfig;
    }

    // FIXME: we should resolve the package root path to the correct casing
    public string ResolveValidPackageRoot(string path) {
        var normalized = Path.GetFullPath(path);
        if (PackageRoots.ValidPackageRoots.Contains(normalized)) {
            return normalized;
        }
        if (PackageRoots.MissingPackageRoots.Contains(normalized)) {
            throw new PackageRootNotValidException(
                    $"The passed package root is registered, but the directory is missing: {path}");
        } else {
            throw new PackageRootNotValidException($"The passed package root is not registered: {path}");
        }
    }

    public ImportedPackage GetPackage(string packageName, bool resolveName, bool loadManifest) {
        Verify.PackageName(packageName);

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
        throw new ImportedPackageNotFoundException($"Could not find package '{packageName}' in known package directories."
                                                   + " Searched paths:\n    " + string.Join("\n    ", searchedPaths));
    }

    /// Assumes that the package root is valid.
    public ImportedPackage GetPackage(string packageName, string packageRoot, bool resolveName, bool loadManifest) {
        Verify.PackageName(packageName);
        Debug.Assert(ResolveValidPackageRoot(packageRoot) == packageRoot);

        if (resolveName) {
            packageName = PathUtils.GetResolvedChildName(packageRoot, packageName);
        }

        var p = new ImportedPackage(packageName, IOPath.Combine(packageRoot, packageName), false);

        if (loadManifest) {
            if (!p.Exists) {
                throw new ImportedPackageNotFoundException($"Package '{p.PackageName}' at '{p.Path}' does not exist");
            }
            p.ReloadManifest();
        }
        return p;
    }

    // FIXME: this should probably return a set (or at least filter the packages to skip collisions)
    public IEnumerable<string> EnumeratePackageNames(string namePattern = "*") {
        return PackageRoots.ValidPackageRoots.SelectMany(r => PathUtils.EnumerateNonHiddenDirectoryNames(r, namePattern));
    }

    public IEnumerable<ImportedPackage> EnumeratePackages(bool loadManifest, string namePattern = "*") {
        return PackageRoots.ValidPackageRoots.SelectMany(r => EnumeratePackages(r, loadManifest, namePattern));
    }

    /// Assumes that the package root is valid.
    public IEnumerable<ImportedPackage> EnumeratePackages(string packageRoot, bool loadManifest, string namePattern = "*") {
        Debug.Assert(ResolveValidPackageRoot(packageRoot) == packageRoot);
        return PathUtils.EnumerateNonHiddenDirectoryNames(packageRoot, namePattern)
                // do not resolve name, it already has the correct casing
                .Select(p => GetPackage(p, packageRoot, false, loadManifest));
    }
}

/// <summary>
/// A class representing an imported package.
///
/// By default, the package manifest is loaded during initialization - the package must exist and have a valid manifest,
/// otherwise an exception is thrown.
/// </summary>
[PublicAPI]
public class ImportedPackage : Package {
    public PackageVersion? Version => Manifest.Version;
    [Hidden] public string? ManifestName => Manifest.Name;

    internal ImportedPackage(string packageName, string path, bool loadManifest = true) : base(packageName, path) {
        Verify.Assert.PackageName(packageName);
        if (loadManifest) {
            // load the manifest to (partially) validate it and ensure the getters won't throw
            ReloadManifest();
        }
    }

    /// Enumerates full paths of all exported shortcuts.
    public IEnumerable<FileInfo> EnumerateExportedShortcuts() {
        return EnumerateFiles(IOPath.Combine(Path, PathConfig.PackagePaths.ShortcutDirRelPath), "*.lnk");
    }

    /// Enumerates full paths of all exported commands.
    public IEnumerable<FileInfo> EnumerateExportedCommands() {
        return EnumerateFiles(IOPath.Combine(Path, PathConfig.PackagePaths.CommandDirRelPath));
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string dirPath, string searchPattern = "*") {
        try {
            return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden));
        } catch (DirectoryNotFoundException) {
            // if nothing was exported, the directory might not exist
            return Enumerable.Empty<FileInfo>();
        }
    }

    public string GetDescriptionString() {
        var versionStr = Manifest.Version != null ? $", version '{Manifest.Version}'" : "";
        if (Manifest.IsPrivate) {
            return $"private package '{PackageName}'{versionStr}";
        } else if (Manifest.Name == PackageName) {
            return $"package '{Manifest.Name}'{versionStr}";
        } else {
            return $"package '{Manifest.Name}' (installed as '{PackageName}'){versionStr}";
        }
    }
}
