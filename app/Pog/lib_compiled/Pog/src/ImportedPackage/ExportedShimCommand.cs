using System;
using System.IO;
using Pog.Shim;
using Pog.Utils;

namespace Pog;

internal class ExportedShimCommand(ShimExecutable shim) {
    public bool UpdateCommand(string exportPath, Action<string> debugLogFn) {
        if (File.Exists(exportPath)) {
            if ((new FileInfo(exportPath).Attributes & FileAttributes.ReparsePoint) != 0) {
                debugLogFn("Overwriting symlink with a shim executable...");
                // reparse point, not an ordinary file, remove
                File.Delete(exportPath);
            } else if (!FsUtils.FileExistsCaseSensitive(exportPath)) {
                debugLogFn("Updating casing of an exported command...");
                File.Delete(exportPath);
            } else {
                try {
                    return shim.UpdateShim(exportPath);
                } catch (ShimExecutable.OutdatedShimException) {
                    debugLogFn("Old shim executable, replacing with an up-to-date one...");
                    File.Delete(exportPath);
                }
            }
        } else {
            // ensure the parent directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        }

        // copy empty shim to rLinkPath
        File.Copy(InternalState.PathConfig.ShimPath, exportPath);
        try {
            shim.WriteNewShim(exportPath);
        } catch {
            // clean up the empty shim
            FsUtils.EnsureDeleteFile(exportPath);
            throw;
        }
        return true;
    }
}
