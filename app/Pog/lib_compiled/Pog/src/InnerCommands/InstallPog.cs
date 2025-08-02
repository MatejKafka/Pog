using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;
using PPaths = Pog.PathConfig.PackagePaths;

namespace Pog.InnerCommands;

internal sealed class InstallPog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet), IDisposable {
    private string _appDirPath = null!;
    private string _newAppDirPath = null!;
    private string _oldAppDirPath = null!;
    private string _extractionDirPath = null!;
    private string _tmpDeletePath = null!;

    private bool _lockFileListShown = false;
    private ProgressActivity _progressActivity = new();

    public override void Invoke() {
        if (Package.Manifest.Install == null) {
            WriteInformation($"Package '{Package.PackageName}' does not have an Install block.");
            return;
        }
        WriteInformation($"Installing {Package.GetDescriptionString()}...");

        _appDirPath = $@"{Package.Path}\{PPaths.AppDirName}";
        _newAppDirPath = $@"{Package.Path}\{PPaths.NewAppDirName}";
        _oldAppDirPath = $@"{Package.Path}\{PPaths.AppBackupDirName}";
        _extractionDirPath = $@"{Package.Path}\{PPaths.TmpExtractionDirName}";
        _tmpDeletePath = $@"{Package.Path}\{PPaths.TmpDeleteDirName}";

        _progressActivity = new() {Activity = $"Installing '{Package.PackageName}'"};

        try {
            CleanPreviousInstallation();

            foreach (var source in Package.Manifest.EvaluateInstallUrls(Package)) {
                InstallSingleSource(source);
            }

            ReplaceAppDirectory(_newAppDirPath, _appDirPath, _oldAppDirPath);
        } catch (Exception e) {
            throw new Exception($"Failed to install package '{Package.PackageName}': {e.Message}", e);
        }
    }

    private void CleanPreviousInstallation() {
        if (new[] {_extractionDirPath, _newAppDirPath, _tmpDeletePath}.Any(FsUtils.EnsureDeleteDirectory)) {
            WriteWarning("Removed orphaned tmp installer directories, probably from an interrupted previous install...");
        }

        if (Directory.Exists(_oldAppDirPath)) {
            // the installation has been interrupted before it cleaned up; to be safe, always revert to the previous version,
            //  in case we add some post-install steps after the ./app directory is moved in place, because otherwise if we
            //  would keep the new version, we'd have to check that the follow-up steps all finished
            if (Directory.Exists(_appDirPath)) {
                WriteWarning("Clearing an incomplete app directory from a previous interrupted install...");
                // remove atomically, so that the user doesn't see a partially deleted app directory in case this is interrupted again
                FsUtils.DeleteDirectoryAtomically(_appDirPath, _tmpDeletePath);
            }
            WriteWarning("Restoring the previous app directory to recover from an interrupted install...");
            FsUtils.MoveAtomically(_oldAppDirPath, _appDirPath);
        }

        if (Directory.Exists(_appDirPath)) {
            // check if we can move/delete the current ./app directory
            // e.g. maybe the packaged program is running and holding a lock over a file inside
            if (FsUtils.IsDirectoryLocked(_appDirPath)) {
                // show information about the locked files to the user, so that they can close the program
                //  while the new version is downloading & extracting to save time
                ShowLockedFileInfo(_appDirPath);
            }

            // do not remove the current ./app directory just yet; first, we'll download & extract the new version,
            //  and after all checks pass and we know we managed to set it up correctly, we'll delete the old version
        }
    }

    /// Retrieve and extract the source file into <see cref="_extractionDirPath"/> and move the selected subdirectory
    /// to the target path under <see cref="_newAppDirPath"/>.
    private void InstallSingleSource(PackageSource source) {
        // should be resolved by the caller
        var url = (string) source.Url;
        var target = source switch {
            PackageSourceNoArchive pna => pna.Target,
            PackageSourceArchive pa => pa.Target,
            _ => throw new UnreachableException(),
        };
        var targetPath = target == null ? _newAppDirPath : FsUtils.JoinValidateSubPath(_newAppDirPath, target);

        if (targetPath == null) {
            // the target path escapes from the app directory
            ThrowArgumentError(target, "TargetEscapesRoot",
                    $"Argument passed to the -Target parameter must be a relative path that does not escape " +
                    $"the app directory, got '{target}'.");
            throw new UnreachableException();
        }

        if (source is PackageSourceNoArchive) {
            if (targetPath == _newAppDirPath) {
                ThrowArgumentError(target, "TargetResolvesToRoot",
                        $"Argument passed to the -Target parameter must contain the target file name, got '{target}'");
            }
        }

        using var downloadedFile = InvokePogCommand(new InvokeCachedFileDownload(Cmdlet) {
            SourceUrl = url,
            ExpectedHash = source.ExpectedHash,
            DownloadParameters = new DownloadParameters(source.UserAgent),
            Package = Package,
            ProgressActivity = _progressActivity,
        });

        switch (source) {
            case PackageSourceNoArchive:
                InstallNoArchive(downloadedFile, targetPath);
                break;
            case PackageSourceArchive a:
                InstallArchive(a, downloadedFile, targetPath);
                break;
            default:
                throw new UnreachableException();
        }
    }

    public void Dispose() {
        FsUtils.EnsureDeleteDirectory(_tmpDeletePath);
        FsUtils.EnsureDeleteDirectory(_extractionDirPath);
        FsUtils.EnsureDeleteDirectory(_newAppDirPath);
        // do not attempt to delete AppBackupDirName here, it should be already cleaned up
        //  (and if it isn't, it probably also won't work here)
    }

    private void InstallNoArchive(SharedFileCache.IFileLock downloadedFile, string targetPath) {
        // ensure that the parent directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        // copy the file directly to the target path
        // for NoArchive, Target contains even the file name, not just the directory name
        File.Copy(downloadedFile.Path, targetPath, true);
    }

    private void InstallArchive(PackageSourceArchive param,
            SharedFileCache.IFileLock downloadedFile, string targetPath) {
        using (downloadedFile) {
            // extract the archive to a temporary directory
            InvokePogCommand(new ExpandArchive7Zip(Cmdlet) {
                ArchivePath = downloadedFile.Path,
                TargetPath = _extractionDirPath,
                Filter = param.Subdirectory == null ? null : [param.Subdirectory],
                ProgressActivity = _progressActivity,
            });
        }

        // find and prepare the used subdirectory
        var usedDir = GetExtractedSubdirectory(_extractionDirPath, param.Subdirectory);
        WriteDebug($"Resolved source directory: {usedDir}");
        PrepareExtractedSubdirectory(usedDir.FullName, param.SetupScript, param.NsisInstaller);

        // move `usedDir` to the new app directory
        if (!Directory.Exists(targetPath)) {
            var parentPath = Path.GetDirectoryName(targetPath)!;
            // ensure parent directory exists
            Directory.CreateDirectory(parentPath);
            usedDir.MoveToWithRetries(targetPath);
        } else {
            // move just the contents and merge a single level of targetPath and usedDir
            // we don't try to do a recursive merge, it wasn't needed so far, and it has less obvious failure modes
            usedDir.MoveContentToWithRetries(targetPath);
        }

        // remove any unused files from the extraction dir
        FsUtils.EnsureDeleteDirectory(_extractionDirPath);
    }

    private DirectoryInfo GetExtractedSubdirectory(string extractedRootPath, string? subdirectory) {
        if (subdirectory == null) {
            var root = new DirectoryInfo(extractedRootPath);
            // this only loads metadata for the first 2 files without iterating over the whole directory
            var entries = root.EnumerateFileSystemInfos().Take(2).ToArray();
            if (entries.Length == 1 && entries[0] is DirectoryInfo newRoot) {
                // single directory in archive root (as is common for Linux-style archives)
                WriteDebug($"Archive root contains a single directory '{newRoot.Name}', using it instead of the root.");
                return newRoot;
            } else {
                // no single subdirectory, multiple (or no) files in root (Windows-style archive)
                WriteDebug("Archive root contains multiple items, using the archive root directly.");
                return root;
            }
        } else {
            // `subdirectory` may contain wildcards, resolve them
            var resolvedPaths = GetResolvedProviderPathFromPSPath(Path.Combine(extractedRootPath, subdirectory), out _);
            switch (resolvedPaths.Count) {
                case > 1:
                    // multiple matches
                    var resolvedPathStr = string.Join(", ",
                            resolvedPaths.Select(p => p.Substring(extractedRootPath.Length + 1)));
                    ThrowArgumentError(subdirectory, "ArchiveSubdirectoryMultipleMatches",
                            $"Subdirectory '{subdirectory}' requested in the package manifest resolved to multiple " +
                            $"matching paths inside the archive: {resolvedPathStr}");
                    break;
                case 0:
                    // subdirectory does not exist
                    throw new DirectoryNotFoundException($"Subdirectory '{subdirectory}' requested in the package " +
                                                         $"manifest does not exist inside the archive.");
            }

            var subPath = resolvedPaths[0]!;

            if (FsUtils.EscapesDirectory(extractedRootPath, subPath)) {
                ThrowArgumentError(subdirectory, "SubdirectoryEscapesRoot",
                        $"Argument passed to the -Subdirectory parameter must be a relative path that does not escape " +
                        $"the archive directory, got '{subdirectory}'.");
            }

            // test if the path exists in the extracted directory
            var sub = new DirectoryInfo(subPath);
            if ((sub.Attributes & FileAttributes.Directory) == 0) {
                // it's actually a file
                // use the parent directory, 7zip should have only extracted the file we're interested in
                // FIXME: if the user specifies the -Target, it might be confusing that a directory with the file is placed
                //  there instead of just the file
                return sub.Parent!; // cannot be null, we're inside the extracted directory
            } else {
                // directory
                return sub;
            }
        }
    }

    private void PrepareExtractedSubdirectory(string subdirectoryPath, ScriptBlock? setupScript, bool nsisInstaller) {
        if (nsisInstaller) {
            // extracted NSIS installers contain this directory, typically it doesn't contain anything useful
            const string pluginDirName = "$PLUGINSDIR";
            var pluginDirPath = Path.Combine(subdirectoryPath, pluginDirName);
            try {
                Directory.Delete(pluginDirPath, true);
                WriteDebug($"Removed '{pluginDirName}' directory from the extracted NSIS installer archive.");
            } catch (DirectoryNotFoundException e) {
                throw new DirectoryNotFoundException(
                        $"'-NsisInstaller' flag was set in the package manifest, but the directory '{pluginDirName}' " +
                        "does not exist in the extracted path (NSIS self-extracting archive should contain it).", e);
            }
        }

        if (setupScript != null) {
            var cmd = new InvokeContainer(Cmdlet) {
                WorkingDirectory = subdirectoryPath,
                // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
                Variables = [
                    new("this", Package.Manifest.Raw, ""),
                    new("ErrorActionPreference", ActionPreference.Stop, ""),
                ],
                Run = ps => ps.AddScript("Set-StrictMode -Version 3; & $Args[0]").AddArgument(setupScript),
            };

            // setup script should not output anything, show a warning
            foreach (var o in InvokePogCommand(cmd)) {
                WriteWarning($"SETUP: {o}");
            }
        }
    }

    /// <summary>Moves <paramref name="srcDir"/> to the ./app directory. Ensures that there are no locked files
    /// in the previous ./app directory (if it exists), and moves it to <paramref name="backupDir"/>.</summary>
    private void ReplaceAppDirectory(string srcDir, string targetAppDir, string backupDir) {
        using var newAppDirHandle = FsUtils.OpenForMove(srcDir);
        try {
            FsUtils.MoveByHandle(newAppDirHandle, targetAppDir);
            // success, targetAppDir did not originally exist, the new app directory is in place
            return;
        } catch (COMException e) when (e.HResult == -2147024713) {
            // -2147024713 (0x800700B7) = target already exists, ignore
        }

        // the target app directory already exists, so we need to replace it
        // however, win32 does not give us an API to atomically swap/replace a directory, except for NTFS transactions,
        // which seem to be mostly deprecated; instead, we'll have to first move the current app directory away,
        // and then move the replacement app directory in; there's a short period between the moves where we'll
        // get an unusable app if the operation fails/crashes, but it seems we cannot do any better than that
        using (var oldAppDirHandle = MoveOutOldAppDirectory(targetAppDir, backupDir)) {
            try {
                FsUtils.MoveByHandle(newAppDirHandle, targetAppDir);
                // success, the new app directory is in place
            } catch {
                // at least attempt to move back the original app directory
                FsUtils.MoveByHandle(oldAppDirHandle, targetAppDir);
                throw;
            }
        }
        // delete the backup app directory
        FsUtils.DeleteDirectoryAtomically(backupDir, _tmpDeletePath);
    }

    // FIXME: if there's an executing binary from the dir, but no other open files (e.g. AutoHotkey v2), this function will
    //  succeed in moving out the app dir, but then calling `FsUtils.DeleteDirectoryAtomically` will fail
    //  at `FileSystem.RemoveDirectory`; investigate why and how to correctly detect the situation
    private SafeFileHandle MoveOutOldAppDirectory(string appDirPath, string backupPath) {
        var i = 0;
        SafeFileHandle oldAppDirHandle;
        for (;; i++) {
            try {
                oldAppDirHandle = FsUtils.OpenForMove(appDirPath);
                break;
            } catch (FileLoadException) {
                // used by another process, wait for unlock
                WaitForLockedFiles(appDirPath, i, checkDirItself: true);
                // retry
            }
        }
        using (oldAppDirHandle) {
            for (;; i++) {
                try {
                    FsUtils.MoveByHandle(oldAppDirHandle, backupPath);
                    return oldAppDirHandle;
                } catch (UnauthorizedAccessException) {
                    // a file inside the app directory is locked
                    WaitForLockedFiles(appDirPath, i, checkDirItself: false);
                    // retry
                }
            }
        }
    }

    private void WaitForLockedFiles(string dirPath, int attemptNumber, bool checkDirItself) {
        WriteDebug("The previous app directory seems to be used.");
        InvokePogCommand(new ShowLockedFileList(Cmdlet) {
            Path = _appDirPath,
            MessagePrefix = "Cannot overwrite an existing package installation,",
            NoList = _lockFileListShown,
            Wait = true,
        });
        // if this gets called a second time, the user did not close everything, print the up-to-date list again
        _lockFileListShown = false;
    }

    private void ShowLockedFileInfo(string dirPath) {
        InvokePogCommand(new ShowLockedFileList(Cmdlet) {
            Path = _appDirPath,
            MessagePrefix = "Cannot overwrite an existing package installation,",
        });
        _lockFileListShown = true;
    }
}
