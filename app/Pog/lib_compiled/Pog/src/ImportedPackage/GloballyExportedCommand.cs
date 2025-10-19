using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pog.Utils;
using IOPath = System.IO.Path;

namespace Pog;

internal class GloballyExportedCommand(string path) {
    public readonly string Path = path;

    public bool Exists => FsUtils.FileExistsCaseSensitive(Path);
    // take the target path and go ../.. (from `<package>/.commands/name.exe`)
    public string? SourcePackagePath => Target is {} t ? IOPath.GetDirectoryName(IOPath.GetDirectoryName(t)) : null;
    public string? Target => FsUtils.GetSymbolicLinkTarget(Path);

    public static GloballyExportedCommand FromLocal(string localExportPath) {
        var path = $"{InternalState.PathConfig.ExportedCommandDir}\\{IOPath.GetFileName(localExportPath)}";
        return new(path);
    }

    public bool IsFromPackage(ImportedPackage p) {
        return string.Equals(SourcePackagePath, p.Path, StringComparison.OrdinalIgnoreCase);
    }

    public void Delete() => File.Delete(Path);

    public void UpdateFrom(FileInfo localCmd) {
        FsUtils.CreateSymbolicLink(Path, localCmd.FullName, false);
    }

    public IEnumerable<GloballyExportedCommand> EnumerateConflictingCommands() {
        var name = IOPath.GetFileNameWithoutExtension(Path);
        // filter out files with a dot before the extension (e.g. `arm-none-eabi-ld.bfd.exe`)
        return Directory.EnumerateFiles(InternalState.PathConfig.ExportedCommandDir, $"{name}.*")
                .Where(cmdPath => string.Equals(
                        IOPath.GetFileNameWithoutExtension(cmdPath), name, StringComparison.OrdinalIgnoreCase))
                .Select(p => new GloballyExportedCommand(p));
    }
}
