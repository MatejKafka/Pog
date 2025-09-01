using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Pog.Utils;

namespace Pog;

public class ImportedPackageNotFoundException(string message) : PackageNotFoundException(message);

public class InvalidPackageRootException(string message) : ArgumentException(message);

/// <summary>Class representing an installed package.</summary>
/// <remarks>
/// By default, the package manifest is loaded during initialization - the package must exist and have a valid manifest,
/// otherwise an exception is thrown.
/// </remarks>
[PublicAPI]
public sealed class ImportedPackage : Package, ILocalPackage {
    public PackageVersion? Version => Manifest.Version;
    public string? ManifestName => Manifest.Name;

    public string Path {get;}
    public string ManifestPath => $"{Path}\\{PathConfig.PackagePaths.ManifestFileName}";
    public string ManifestResourceDirPath => $"{Path}\\{PathConfig.PackagePaths.ManifestResourceDirName}";
    public string UserManifestPath => $"{Path}\\{PathConfig.PackagePaths.UserManifestFileName}";

    private string ManifestBackupPath => $"{Path}\\{PathConfig.PackagePaths.ManifestBackupFileName}";
    private string ManifestResourceDirBackupPath => $"{Path}\\{PathConfig.PackagePaths.ManifestResourceBackupFileName}";

    internal string ExportedCommandDirPath => $"{Path}{PathConfig.PackagePaths.CommandDirRelSuffix}";
    internal string ExportedShortcutDirPath => $"{Path}{PathConfig.PackagePaths.ShortcutDirRelSuffix}";
    internal string ExportedShortcutShimDirPath => $"{Path}{PathConfig.PackagePaths.ShortcutShimDirRelSuffix}";

    public override bool Exists => Directory.Exists(Path);

    private PackageUserManifest? _userManifest;
    public PackageUserManifest UserManifest => EnsureUserManifestIsLoaded();

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
    internal void RemoveManifest(bool backup) {
        if (backup) {
            FsUtils.MoveAtomicallyIfExists(ManifestPath, ManifestBackupPath);
            FsUtils.MoveAtomicallyIfExists(ManifestResourceDirPath, ManifestResourceDirBackupPath);
        } else {
            FsUtils.EnsureDeleteFile(ManifestPath);
            FsUtils.EnsureDeleteDirectory(ManifestResourceDirPath);
        }
        // invalidate the current loaded manifest
        InvalidateManifest();
    }

    internal bool RestoreManifestBackup() {
        RemoveManifest(false);
        return FsUtils.MoveAtomicallyIfExists(ManifestResourceDirBackupPath, ManifestResourceDirPath)
               || FsUtils.MoveAtomicallyIfExists(ManifestBackupPath, ManifestPath);
    }

    internal void RemoveManifestBackup() {
        FsUtils.EnsureDeleteFile(ManifestBackupPath);
        FsUtils.EnsureDeleteDirectory(ManifestResourceDirBackupPath);
    }

    public PackageUserManifest EnsureUserManifestIsLoaded() => _userManifest ?? ReloadUserManifest();
    public PackageUserManifest ReloadUserManifest() => _userManifest = LoadUserManifest();

    private PackageUserManifest LoadUserManifest() {
        if (!Exists) {
            throw new PackageNotFoundException(
                    $"Tried to read the user manifest of a non-existent package at '{Path}'.");
        }
        return new PackageUserManifest(UserManifestPath);
    }

    public IEnumerable<FileInfo> EnumerateExportedCommands() => EnumerateExportDir(ExportedCommandDirPath);
    public IEnumerable<FileInfo> EnumerateExportedShortcuts() => EnumerateExportDir(ExportedShortcutDirPath, "*.lnk");
    internal IEnumerable<FileInfo> EnumerateShortcutShims() => EnumerateExportDir(ExportedShortcutShimDirPath);

    private static IEnumerable<FileInfo> EnumerateExportDir(string dirPath, string searchPattern = "*") {
        try {
            return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden));
        } catch (DirectoryNotFoundException) {
            // if nothing was exported, the directory might not exist
            return [];
        }
    }

    internal string GetExportedCommandPath(string cmdName, string cmdExt) => @$"{ExportedCommandDirPath}\{cmdName}{cmdExt}";
    internal string GetExportedShortcutPath(string shortcutName) => @$"{ExportedShortcutDirPath}\{shortcutName}.lnk";
    internal string GetExportedShortcutShimPath(string shortcutName) => @$"{ExportedShortcutShimDirPath}\{shortcutName}.exe";

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

    public override string ToString() => $"{this.GetType().FullName}({PackageName} v{Version}, {Path})";
}
