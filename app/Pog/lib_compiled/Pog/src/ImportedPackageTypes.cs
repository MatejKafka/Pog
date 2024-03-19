using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Pog.Utils;
using IOPath = System.IO.Path;

namespace Pog;

public class ImportedPackageNotFoundException(string message) : PackageNotFoundException(message);

public class InvalidPackageRootException(string message) : ArgumentException(message);

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
        var selectedRoot = roots.FirstOrDefault(r => Directory.Exists(IOPath.Combine(r, packageName))) ?? roots[0];
        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(selectedRoot, packageName);
        }
        return new ImportedPackage(packageName, IOPath.Combine(selectedRoot, packageName), loadManifest);
    }

    /// <exception cref="ImportedPackageNotFoundException"></exception>
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
                packageName = FsUtils.GetResolvedChildName(root, packageName);
            }
            return new ImportedPackage(packageName, IOPath.Combine(root, packageName), loadManifest);
        }
        throw new ImportedPackageNotFoundException($"Could not find package '{packageName}' in known package directories."
                                                   + " Searched paths:\n    " + string.Join("\n    ", searchedPaths));
    }

    /// Assumes that the package root is valid.
    public ImportedPackage GetPackage(string packageName, string packageRoot,
            bool resolveName, bool loadManifest, bool mustExist) {
        Verify.PackageName(packageName);
        Debug.Assert(ResolveValidPackageRoot(packageRoot) == packageRoot);

        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(packageRoot, packageName);
        }

        var p = new ImportedPackage(packageName, IOPath.Combine(packageRoot, packageName), false);

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

    public IEnumerable<ImportedPackage> EnumeratePackages(bool loadManifest, string namePattern = "*") {
        return PackageRoots.ValidPackageRoots.SelectMany(r => EnumeratePackages(r, loadManifest, namePattern));
    }

    /// Assumes that the package root is valid.
    public IEnumerable<ImportedPackage> EnumeratePackages(string packageRoot, bool loadManifest, string namePattern = "*") {
        Debug.Assert(ResolveValidPackageRoot(packageRoot) == packageRoot);
        return FsUtils.EnumerateNonHiddenDirectoryNames(packageRoot, namePattern)
                // do not resolve name, it already has the correct casing
                .Select(p => GetPackage(p, packageRoot, false, loadManifest, false));
    }
}

/// <summary>
/// A class representing an imported package.
///
/// By default, the package manifest is loaded during initialization - the package must exist and have a valid manifest,
/// otherwise an exception is thrown.
/// </summary>
[PublicAPI]
public sealed class ImportedPackage : Package {
    public PackageVersion? Version => Manifest.Version;
    public string? ManifestName => Manifest.Name;

    internal ImportedPackage(string packageName, string path, bool loadManifest = true) : base(packageName, path) {
        Verify.Assert.PackageName(packageName);
        if (loadManifest) {
            // load the manifest to validate it and ensure the getters won't throw
            ReloadManifest();
        }
    }

    // called while importing a new manifest
    internal void RemoveManifest() {
        FsUtils.EnsureDeleteFile(ManifestPath);
        FsUtils.EnsureDeleteDirectory(ManifestResourceDirPath);
        // invalidate the current loaded manifest
        InvalidateManifest();
    }

    public bool MatchesRepositoryManifest(RepositoryPackage p) {
        // compare resource dirs
        if (!FsUtils.DirectoryTreeEqual(ManifestResourceDirPath, p.ManifestResourceDirPath)) {
            return false;
        }

        // compare manifest
        var importedManifest = new FileInfo(ManifestPath);
        if (!importedManifest.Exists) {
            return false;
        } else if (p is DirectRepositoryPackage dp) {
            return importedManifest.Exists && FsUtils.FileContentEqual(new FileInfo(dp.ManifestPath), importedManifest);
        } else if (p is TemplatedRepositoryPackage tp) {
            var repoManifest = Encoding.UTF8.GetBytes(tp.GetManifestString());
            return FsUtils.FileContentEqual(repoManifest, importedManifest);
        } else {
            throw new UnreachableException();
        }
    }

    public bool RemoveExportedShortcuts() {
        // shortcut dir is the root of the package, delete the shortcuts one-by-one instead of deleting the whole directory
        var deleted = false;
        foreach (var shortcut in EnumerateExportedShortcuts()) {
            shortcut.Delete();
            deleted = true;
        }
        return deleted;
    }

    public bool RemoveExportedCommands() {
        return DeleteDirectoryRel(PathConfig.PackagePaths.CommandDirRelPath);
    }

    public bool RemoveShortcutStubs() {
        return DeleteDirectoryRel(PathConfig.PackagePaths.ShortcutStubDirRelPath);
    }

    private bool DeleteDirectoryRel(string relDirPath) {
        return FsUtils.EnsureDeleteDirectory(IOPath.Combine(Path, relDirPath));
    }

    /// Enumerates full paths of all exported shortcuts.
    public IEnumerable<FileInfo> EnumerateExportedShortcuts() {
        return EnumerateFilesRel(PathConfig.PackagePaths.ShortcutDirRelPath, "*.lnk");
    }

    /// Enumerates full paths of all exported commands.
    public IEnumerable<FileInfo> EnumerateExportedCommands() {
        return EnumerateFilesRel(PathConfig.PackagePaths.CommandDirRelPath);
    }

    /// Enumerates full paths of all internal shortcut stubs.
    internal IEnumerable<FileInfo> EnumerateShortcutStubs() {
        return EnumerateFilesRel(PathConfig.PackagePaths.ShortcutStubDirRelPath);
    }

    private IEnumerable<FileInfo> EnumerateFilesRel(string relDirPath, string searchPattern = "*") {
        var dirPath = IOPath.Combine(Path, relDirPath);
        try {
            return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden));
        } catch (DirectoryNotFoundException) {
            // if nothing was exported, the directory might not exist
            return Enumerable.Empty<FileInfo>();
        }
    }

    protected override PackageManifest LoadManifest() {
        return new PackageManifest(ManifestPath);
    }

    public string GetDescriptionString() {
        var versionStr = Manifest.Version != null ? $", version '{Manifest.Version}'" : "";
        if (Manifest.Private) {
            return $"private package '{PackageName}'{versionStr}";
        } else if (Manifest.Name == PackageName) {
            return $"package '{Manifest.Name}'{versionStr}";
        } else {
            return $"package '{Manifest.Name}' (installed as '{PackageName}'){versionStr}";
        }
    }
}
