using System;
using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.Utils;
using PPaths = Pog.PathConfig.PackagePaths;

namespace Pog.Commands;

// TODO: make this similarly robust to Install-Pog; especially handle shims that are in use
/// <summary>Uninstalls a package.</summary>
/// <para>
/// Uninstalls a package by first disabling it (see `Disable-Pog`) and then deleting the package directory.
/// If -KeepData is passed, only the app, cache and logs directories are deleted and persistent data are left intact.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Uninstall, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class UninstallPogCommand() : ImportedPackageNoPassThruCommand(false) {
    /// Keep the package directory, only disable the package and delete the app directory.
    [Parameter] public SwitchParameter KeepData;

    // does not make sense to support -PassThru for this cmdlet
    protected override void ProcessPackageNoPassThru(ImportedPackage package) {
        if (package.PackageName == "Pog") {
            WriteWarning("Cannot uninstall Pog itself using Pog. To uninstall Pog, Run `Disable-Pog Pog`, close the" +
                         $" PowerShell session and then delete the Pog package directory ('{package.Path}') manually.");
            return;
        }

        // only disable a package if the manifest is available; otherwise delete it (this is mainly useful for recovering
        //  from failed uninstallations, where the manifest gets deleted but the directory still exists)
        if (package.ManifestAvailable) {
            if (EnsureManifestIsLoaded(package) == null) {
                return;
            }

            // disable the package
            InvokePogCommand(new DisablePog(this) {
                Package = package,
            });
        } else {
            WriteDebug($"Skipped Disable-Pog for '{package.PackageName}', package does not have a manifest.");
        }

        // uninstalling a package does not access .Manifest, no need to load it
        UninstallPackage(package);
    }

    private void UninstallPackage(ImportedPackage package) {
        var tmpDeletePath = $@"{package.Path}\{PPaths.TmpDeleteDirName}";
        if (FsUtils.EnsureDeleteDirectory(tmpDeletePath)) {
            WriteWarning("Removed orphaned tmp installer directories, probably from an interrupted previous install...");
        }

        // atomically delete the app directory
        DeleteSubdirectory(package, PPaths.AppDirName);
        // also delete the cache and logs directory
        DeleteSubdirectory(package, PPaths.CacheDirName);
        DeleteSubdirectory(package, PPaths.LogDirName);

        if (!KeepData) {
            // delete all subdirectories first; this way, the package remains valid even if something fails
            //  (e.g. due to an opened file); TODO: check for used files similarly to Install-Pog
            foreach (var e in Directory.EnumerateDirectories(package.Path)) {
                if (e == package.ManifestResourceDirPath || e == tmpDeletePath) {
                    continue;
                }
                DeleteDirectoryWithWait(e, tmpDeletePath);
            }

            // delete the whole package directory, including the manifest
            // TODO: it might be nice to make this atomic, and do the same with setup in Import-Pog
            FsUtils.EnsureDeleteDirectory(package.Path);

            WriteInformation($"Uninstalled package '{package.PackageName}'.");
        } else {
            WriteInformation($"Uninstalled package '{package.PackageName}', preserving app data.");
        }
    }

    private bool DeleteSubdirectory(ImportedPackage package, string dirName) {
        try {
            DeleteDirectoryWithWait($@"{package.Path}\{dirName}", $@"{package.Path}\{PPaths.TmpDeleteDirName}");
            WriteVerbose($"Deleted the {dirName} directory for '{package.PackageName}'.");
            return true;
        } catch (FileNotFoundException) {
            return false;
        } catch (DirectoryNotFoundException) {
            return false;
        }
    }

    // FIXME: if there's an executing binary from the dir, but no other open files (e.g. AutoHotkey v2), this function will
    //  succeed in moving out the app dir, but then calling `FsUtils.ForceDeleteDirectory` will fail
    //  at `FileSystem.RemoveDirectory`; investigate why and how to correctly detect the situation
    private void DeleteDirectoryWithWait(string dirPath, string tmpMovePath) {
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
        InvokePogCommand(new ShowLockedFileList(this) {
            Path = dirPath,
            MessagePrefix = "Cannot uninstall the package,",
            Wait = true,
        });
    }
}
