using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using Microsoft.Win32.SafeHandles;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;
using PPaths = Pog.PathConfig.PackagePaths;

namespace Pog.InnerCommands;

// TODO: make this similarly robust to Install-Pog; especially handle shims that are in use
internal class UninstallPog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet) {
    [Parameter] public required bool KeepData;

    public override void Invoke() {
        if (Package.PackageName == "Pog") {
            WriteWarning("Cannot uninstall Pog itself using Pog. To uninstall Pog, Run `Disable-Pog Pog`, close the" +
                         $" PowerShell session and then delete the Pog package directory ('{Package.Path}') manually.");
            return;
        }

        InvokePogCommand(new UnexportPog(Cmdlet) {
            Package = Package,
        });

        DisablePackage();
        UninstallPackage();
    }

    private void DisablePackage() {
        if (!Package.Exists) {
            return; // calling the Disable block on a non-existent directory is potentially risky
        }

        // only disable a package if the manifest is available; otherwise just delete it (this is mainly useful for
        //  recovering from failed uninstallations, where the manifest gets deleted but the directory still exists)
        try {
            Package.EnsureManifestIsLoaded();
        } catch (Exception e) {
            WriteWarning($"Could not load manifest for package '{Package.PackageName}', " +
                         $"skipping Disable block call: {e.Message}");
            return;
        }

        // disable the package
        InvokePogCommand(new DisablePog(Cmdlet) {
            Package = Package,
        });
    }

    private void UninstallPackage() {
        var tmpDeletePath = $@"{Package.Path}\{PPaths.TmpDeleteDirName}";
        if (FsUtils.EnsureDeleteDirectory(tmpDeletePath)) {
            WriteWarning("Removed orphaned tmp installer directories, probably from an interrupted previous install...");
        }

        // atomically delete the app directory
        DeleteSubdirectory(Package, PPaths.AppDirName);
        // also delete the cache and logs directory
        DeleteSubdirectory(Package, PPaths.CacheDirName);
        DeleteSubdirectory(Package, PPaths.LogDirName);

        if (!KeepData) {
            // delete all subdirectories first; this way, the package remains valid even if something fails
            //  (e.g. due to an opened file); TODO: check for used files similarly to Install-Pog
            foreach (var e in Directory.EnumerateDirectories(Package.Path)) {
                if (e == Package.ManifestResourceDirPath || e == tmpDeletePath) {
                    continue;
                }
                DeleteDirectoryWithWait(e, tmpDeletePath);
            }

            // delete the whole package directory, including the manifest
            // TODO: it might be nice to make this atomic, and do the same with setup in Import-Pog
            FsUtils.EnsureDeleteDirectory(Package.Path);

            WriteInformation($"Uninstalled package '{Package.PackageName}'.");
        } else {
            WriteInformation($"Uninstalled package '{Package.PackageName}', preserving app data.");
        }
    }

    private void DeleteSubdirectory(ImportedPackage package, string dirName) {
        try {
            DeleteDirectoryWithWait($@"{package.Path}\{dirName}", $@"{package.Path}\{PPaths.TmpDeleteDirName}");
            WriteVerbose($"Deleted the {dirName} directory for '{package.PackageName}'.");
        } catch (FileNotFoundException) {} catch (DirectoryNotFoundException) {}
    }

    // FIXME: if there's an executing binary from the dir, but no other open files (e.g. AutoHotkey v2), this function will
    //  succeed in moving out the app dir, but then calling `FsUtils.ForceDeleteDirectory` will fail
    //  at `FileSystem.RemoveDirectory`; investigate why and how to correctly detect the situation
    private void DeleteDirectoryWithWait(string dirPath, string tmpMovePath) {
        Debug.Assert(Path.IsPathRooted(dirPath));
        Debug.Assert(Path.IsPathRooted(tmpMovePath));

        SafeFileHandle dirHandle;
        while (true) {
            try {
                dirHandle = FsUtils.OpenForMove(dirPath);
                break;
            } catch (FileLoadException) {
                // used by another process, wait for unlock
                WaitForLockedFiles(dirPath);
                // retry
            }
        }
        using (dirHandle) {
            while (true) {
                try {
                    FsUtils.MoveByHandle(dirHandle, tmpMovePath);
                    break;
                } catch (UnauthorizedAccessException) {
                    // a file inside the app directory is locked
                    WaitForLockedFiles(dirPath);
                    // retry
                }
            }
        }

        // delete the backup app directory
        FsUtils.ForceDeleteDirectory(tmpMovePath);
    }

    private void WaitForLockedFiles(string dirPath) {
        InvokePogCommand(new ShowLockedFileList(Cmdlet) {
            Path = dirPath,
            MessagePrefix = "Cannot uninstall the package,",
            Wait = true,
        });
    }
}
