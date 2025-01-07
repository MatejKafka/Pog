using System.IO;
using Pog.Native;
using Pog.Utils;
using IOPath = System.IO.Path;

namespace Pog;

/// Represents a globally exported shortcut (in Start menu).
internal class GloballyExportedShortcut(string path) {
    public readonly string Path = path;

    public static GloballyExportedShortcut FromLocal(string localExportPath) {
        var path = $"{InternalState.PathConfig.ExportedShortcutDir}\\{IOPath.GetFileName(localExportPath)}";
        return new(path);
    }

    public bool IsFromPackage(ImportedPackage p) {
        return IsFromPackage(p.GetExportedShortcutShimPath(IOPath.GetFileNameWithoutExtension(Path)));
    }

    public bool IsFromPackage(string targetShimPath) {
        // we consider the shortcut matching if it exists and its target (the hidden shim) matches the local shortcut;
        //  this ensures that if something caused the two shortcuts to desync previously, we can recover
        return FsUtils.FileExistsCaseSensitive(Path) && new Shortcut(Path).Target == targetShimPath;
    }

    public bool UpdateFrom(FileInfo localShortcut) {
        if (FsUtils.FileContentEqual(localShortcut, new(Path))) {
            // already up to date
            return false;
        } else {
            localShortcut.CopyTo(Path, true);
            return true;
        }
    }

    public bool OverwriteWith(FileInfo localShortcut) {
        var exists = false;
        if (File.Exists(Path)) {
            // delete conflicting shortcut
            File.Delete(Path);
            exists = true;
        } else {
            // ensure parent dir exists
            Directory.CreateDirectory(IOPath.GetDirectoryName(Path)!);
        }

        localShortcut.CopyTo(Path);
        return exists;
    }

    public void Delete() {
        File.Delete(Path);
    }
}
