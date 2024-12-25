using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using IOPath = System.IO.Path;

namespace Pog;

public class ImportedPackageNotFoundException(string message) : PackageNotFoundException(message);

public class InvalidPackageRootException(string message) : ArgumentException(message);

/// <summary>
/// A class representing an imported package.
///
/// By default, the package manifest is loaded during initialization - the package must exist and have a valid manifest,
/// otherwise an exception is thrown.
/// </summary>
[PublicAPI]
public sealed class ImportedPackage : Package, ILocalPackage {
    public PackageVersion? Version => Manifest.Version;
    public string? ManifestName => Manifest.Name;

    public string Path {get;}
    public string ManifestPath => $"{Path}\\{PathConfig.PackagePaths.ManifestFileName}";
    public string ManifestResourceDirPath => $"{Path}\\{PathConfig.PackagePaths.ManifestResourceDirName}";

    public override bool Exists => Directory.Exists(Path);

    public FileInfo[] ExportedCommands => EnumerateExportedCommands().ToArray();
    public FileInfo[] ExportedShortcuts => EnumerateExportedShortcuts().ToArray();

    internal ImportedPackage(string packageName, string path, bool loadManifest = true) : base(packageName, null) {
        Verify.Assert.FilePath(path);
        Verify.Assert.PackageName(packageName);
        Path = path;
        if (loadManifest) {
            // load the manifest to validate it and ensure the getters won't throw
            ReloadManifest();
        }
    }

    protected override PackageManifest LoadManifest() {
        if (!Exists) {
            throw new PackageNotFoundException($"Tried to read the package manifest of a non-existent package at '{Path}'.");
        }
        return new PackageManifest(ManifestPath);
    }

    // called while importing a new manifest
    internal void RemoveManifest() {
        FsUtils.EnsureDeleteFile(ManifestPath);
        FsUtils.EnsureDeleteDirectory(ManifestResourceDirPath);
        // invalidate the current loaded manifest
        InvalidateManifest();
    }

    internal bool RemoveExportedShortcuts() {
        // shortcut dir is the root of the package, delete the shortcuts one-by-one instead of deleting the whole directory
        var deleted = false;
        foreach (var shortcut in EnumerateExportedShortcuts()) {
            shortcut.Delete();
            deleted = true;
        }
        return deleted;
    }

    internal bool RemoveExportedCommands() {
        return DeleteDirectoryRel(PathConfig.PackagePaths.CommandDirRelPath);
    }

    internal bool RemoveShortcutShims() {
        return DeleteDirectoryRel(PathConfig.PackagePaths.ShortcutShimDirRelPath);
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

    /// Enumerates full paths of all internal shortcut shims.
    internal IEnumerable<FileInfo> EnumerateShortcutShims() {
        return EnumerateFilesRel(PathConfig.PackagePaths.ShortcutShimDirRelPath);
    }

    private IEnumerable<FileInfo> EnumerateFilesRel(string relDirPath, string searchPattern = "*") {
        var dirPath = IOPath.Combine(Path, relDirPath);
        try {
            return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden));
        } catch (DirectoryNotFoundException) {
            // if nothing was exported, the directory might not exist
            return [];
        }
    }

    internal bool RemoveGloballyExportedShortcut(FileInfo shortcut) {
        var targetPath = $"{PathConfig.StartMenuExportDir}\\{shortcut.Name}";
        var target = new FileInfo(targetPath);
        if (!target.Exists || !FsUtils.FileContentEqual(shortcut, target)) {
            return false;
        } else {
            // found a matching shortcut, delete it
            target.Delete();
            return true;
        }
    }

    internal bool RemoveGloballyExportedCommand(FileInfo command) {
        var targetPath = IOPath.Combine(InternalState.PathConfig.ExportedCommandDir, command.Name);
        if (command.FullName == FsUtils.GetSymbolicLinkTarget(targetPath)) {
            // found a matching command, delete it
            File.Delete(targetPath);
            return true;
        } else {
            return false;
        }
    }

    public override string GetDescriptionString() {
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
