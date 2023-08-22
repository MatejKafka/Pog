﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using Pog.Commands.Internal;
using Pog.Commands.Utils;
using Pog.Utils;

namespace Pog.Commands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Install, "FromUrl")]
public class InstallFromUrlCommand : PogCmdlet, IDisposable {
    // created while parsing the package manifest
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public PackageInstallParameters Params = null!;

    private const string AppDirName = "app";
    /// Temporary directory where the previous ./app directory is moved when installing
    /// a new version to support rollback in case of a failed install.
    private const string AppBackupDirName = ".POG_INTERNAL_app_old";
    /// Temporary directory used for archive extraction.
    private const string TmpExtractionDirName = ".POG_INTERNAL_install_tmp";
    /// Temporary directory where the new app directory is composed for multi-source installs before moving it in place.
    private const string NewAppDirName = ".POG_INTERNAL_app_new";
    /// Temporary directory where a deleted directory is first moved so that the delete
    /// is an atomic operation with respect to the original location.
    private const string TmpDeleteDirName = ".POG_INTERNAL_delete_tmp";


    private Command? _currentRunningCmd;
    private string _packageDirPath = null!;
    private string _appDirPath = null!;
    private string _newAppDirPath = null!;
    private bool _allowOverwrite = false;
    private bool _lowPriorityDownload;
    private Package _package = null!;


    /// here, only parameter validation and setup is done
    protected override void BeginProcessing() {
        base.BeginProcessing();
        _packageDirPath = SessionState.Path.CurrentLocation.ProviderPath;
        _appDirPath = _p(AppDirName);
        _newAppDirPath = _p(NewAppDirName);

        // read download parameters from the global container info variable
        var internalInfo = Container.ContainerInternalInfo.GetCurrent(this);
        _lowPriorityDownload = (bool) internalInfo.InternalArguments["DownloadLowPriority"];
        _allowOverwrite = (bool) internalInfo.InternalArguments["AllowOverwrite"];
        _package = internalInfo.Package;


        if (new[] {_p(TmpExtractionDirName), _newAppDirPath, _p(TmpDeleteDirName)}.Any(FsUtils.EnsureDeleteDirectory)) {
            WriteWarning("Removed orphaned tmp installer directories, probably from an interrupted previous install...");
        }

        if (Directory.Exists(_p(AppBackupDirName))) {
            // the installation has been interrupted before it cleaned up; to be safe, always revert to the previous version,
            //  in case we add some post-install steps after the ./app directory is moved in place, because otherwise if we
            //  would keep the new version, we'd have to check that the follow-up steps all finished
            if (Directory.Exists(_appDirPath)) {
                WriteWarning("Clearing an incomplete app directory from a previous interrupted install...");
                // remove atomically, so that the user doesn't see a partially deleted app directory in case this is interrupted again
                FsUtils.DeleteDirectoryAtomically(_appDirPath, _p(TmpDeleteDirName));
            }
            WriteWarning("Restoring the previous app directory to recover from an interrupted install...");
            FsUtils.MoveAtomically(_p(AppBackupDirName), _appDirPath);
        }

        if (Directory.Exists(_appDirPath)) {
            var shouldContinue = ConfirmOverwrite("Overwrite existing package installation?",
                    "Package seems to be already installed. Do you want to overwrite the current installation" +
                    " (./app subdirectory)?\nConfiguration and other package data will be kept.");

            if (!shouldContinue) {
                var exception = new UserRefusedOverwriteException(
                        "Not installing, user refused to overwrite existing package installation." +
                        " Do not pass -Confirm to overwrite the existing installation without confirmation.");
                ThrowTerminatingError(exception, "UserRefusedOverwrite", ErrorCategory.OperationStopped, null);
            }

            // next, we check if we can move/delete the current ./app directory
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

    /// Here, the actual download &amp; extraction is done.
    /// Each retrieved archive is extracted into <see cref="TmpExtractionDirName"/> and the relevant subdirectory is then
    /// moved into its target name at <see cref="NewAppDirName"/>.
    protected override void ProcessRecord() {
        base.ProcessRecord();

        var url = Params.ResolveUrl();

        if (Params.ExpectedHash == null) {
            WriteWarning($"Downloading a file from '{url}', but no checksum was provided in the package." +
                         " This means that we cannot be sure if the downloaded file is the same one the package author intended." +
                         " This may or may not be a problem on its own, but it's a better style to include a checksum," +
                         " and it improves security and reproducibility.");
        }

        var target = Params switch {
            PackageInstallParametersNoArchive pna => pna.Target,
            PackageInstallParametersArchive pa => pa.Target,
            _ => throw new UnreachableException(),
        };
        var targetPath = target == null ? _newAppDirPath : JoinValidateSubdirectory(_newAppDirPath, target);

        if (targetPath == null) {
            // the target path escapes from the app directory
            ThrowTerminatingArgumentError(target, "TargetEscapesRoot",
                    $"Argument passed to the -Target parameter must be a relative path that does not escape " +
                    $"the app directory, got '{target}'.");
            throw new UnreachableException();
        }

        if (Params is PackageInstallParametersNoArchive) {
            if (targetPath == _newAppDirPath) {
                ThrowTerminatingArgumentError(target, "TargetResolvesToRoot",
                        $"Argument passed to the -Target parameter must contain the target file name, got '{target}'");
            }
        }

        var downloadParameters = new DownloadParameters(Params.UserAgent, _lowPriorityDownload);
        using var downloadedFile = InvokeFileDownload.Invoke(
                this, url, Params.ExpectedHash, downloadParameters, _package, false);

        switch (Params) {
            case PackageInstallParametersNoArchive:
                InstallNoArchive(downloadedFile, targetPath);
                break;
            case PackageInstallParametersArchive a:
                InstallArchive(a, downloadedFile, targetPath);
                break;
            default:
                throw new UnreachableException();
        }
    }

    /// here, we install the new ./app directory created during extraction
    protected override void EndProcessing() {
        base.EndProcessing();
        ReplaceAppDirectory(_newAppDirPath, _appDirPath, _p(AppBackupDirName));
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        _currentRunningCmd?.StopProcessing();
    }

    public void Dispose() {
        FsUtils.EnsureDeleteDirectory(_p(TmpDeleteDirName));
        FsUtils.EnsureDeleteDirectory(_p(TmpExtractionDirName));
        FsUtils.EnsureDeleteDirectory(_newAppDirPath);
        // do not attempt to delete AppBackupDirName here, it should be already cleaned up
        //  (and if it isn't, it probably also won't work here)
    }

    /// Resolve a package-relative path to an absolute path.
    private string _p(string relPath) {
        return Path.Combine(_packageDirPath, relPath);
    }

    /// <remarks>Assumes that both paths are resolved and cleaned.</remarks>
    private static bool EscapesDirectory(string basePath, string validatedPath) {
        return !validatedPath.StartsWith(basePath + '\\') && validatedPath != basePath;
    }

    private static string? JoinValidateSubdirectory(string basePath, string subdirectoryPath) {
        var combined = Path.GetFullPath(basePath + '\\' + subdirectoryPath.TrimEnd('/', '\\'));
        if (EscapesDirectory(basePath, combined)) {
            return null;
        }
        return combined;
    }

    private void InstallNoArchive(SharedFileCache.IFileLock downloadedFile, string targetPath) {
        // ensure that the parent directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        // copy the file directly to the target path
        // for NoArchive, Target contains even the file name, not just the directory name
        File.Copy(downloadedFile.Path, targetPath, true);
    }

    private void InstallArchive(PackageInstallParametersArchive param,
            SharedFileCache.IFileLock downloadedFile, string targetPath) {
        // extract the archive to a temporary directory
        var extractionDir = _p(TmpExtractionDirName);
        var cmd = new ExpandArchive7Zip(this, downloadedFile.Path, extractionDir,
                param.Subdirectory == null ? null : new[] {param.Subdirectory});
        _currentRunningCmd = cmd;
        cmd.Invoke();
        _currentRunningCmd = null;
        downloadedFile.Dispose();

        // find and prepare the used subdirectory
        var usedDir = GetExtractedSubdirectory(extractionDir, param.Subdirectory);
        PrepareExtractedSubdirectory(usedDir.FullName, param.SetupScript, param.NsisInstaller);

        // move `usedDir` to the new app directory
        if (!Directory.Exists(targetPath)) {
            var parentPath = Path.GetDirectoryName(targetPath)!;
            // ensure parent directory exists
            Directory.CreateDirectory(parentPath);
            usedDir.MoveTo(targetPath);
        } else {
            // TODO: handle existing target (overwrite?)
            FsUtils.MoveDirectoryContents(usedDir, targetPath);
        }

        // remove any unused files from the extraction dir
        FsUtils.EnsureDeleteDirectory(extractionDir);
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
                    ThrowTerminatingArgumentError(subdirectory, "ArchiveSubdirectoryMultipleMatches",
                            $"Subdirectory '{subdirectory}' requested in the package manifest resolved to multiple " +
                            $"matching paths inside the archive: {resolvedPathStr}");
                    break;
                case 0:
                    // subdirectory does not exist
                    var exception = new DirectoryNotFoundException(
                            $"Subdirectory '{subdirectory}' requested in the package manifest does not exist inside the archive.");
                    ThrowTerminatingError(exception, "ArchiveSubdirectoryNotFound", ErrorCategory.InvalidData, subdirectory);
                    break;
            }

            var subPath = resolvedPaths[0]!;
            WriteDebug($"Using passed path inside archive: {subPath}");

            if (EscapesDirectory(extractedRootPath, subPath)) {
                ThrowTerminatingArgumentError(subdirectory, "SubdirectoryEscapesRoot",
                        $"Argument passed to the -Subdirectory parameter must be a relative path that does not escape " +
                        $"the archive directory, got '{subdirectory}'.");
            }

            // test if the path exists in the extracted directory
            var sub = new DirectoryInfo(subPath);
            if ((int) sub.Attributes == -1) {}

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
                var exception = new DirectoryNotFoundException(
                        $"'-NsisInstaller' flag was set in the package manifest, but the directory '{pluginDirName}' " +
                        "does not exist in the extracted path (NSIS self-extracting archive should contain it).", e);
                ThrowTerminatingError(exception, "NsisPluginDirNotFound", ErrorCategory.InvalidData, pluginDirPath);
            }
        }

        if (setupScript != null) {
            // run the setup script with a changed directory
            SessionState.Path.PushCurrentLocation("pog");
            SessionState.Path.SetLocation(subdirectoryPath);
            try {
                setupScript.InvokeReturnAsIs();
            } finally {
                SessionState.Path.PopLocation("pog");
            }
        }
    }

    /// <summary>Moves srcDir to the ./app directory. Ensures that there are no locked files in the previous
    /// ./app directory (if it exists), and moves it to backupDir.</summary>
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
        // and then move the replacement app directory in; there's a short moment between the move where we'll
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
        FsUtils.DeleteDirectoryAtomically(backupDir, _p(TmpDeleteDirName));
    }

    private SafeFileHandle MoveOutOldAppDirectory(string appDirPath, string backupPath) {
        var i = 0;
        SafeFileHandle oldAppDirHandle;
        for (;; i++) {
            try {
                oldAppDirHandle = FsUtils.OpenForMove(appDirPath);
                break;
            } catch (FileLoadException) {
                // used by another process, wait for unlock
                WaitForLockedFiles(appDirPath, i, true);
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
        // FIXME: better message
        Host.UI.WriteLine("The package seems to be in use, trying to find the offending processes...");

        // FIXME: wait instead of throwing, port to C#
        InvokeCommand.InvokeScript("ThrowLockedFileList");
    }

    /// <returns>True if there are some locked files/directories, false otherwise.</returns>
    private bool ShowLockedFileInfo(string dirPath) {
        // FIXME: port to C#
        InvokeCommand.InvokeScript("ThrowLockedFileList");
        return false;
    }

    private bool ConfirmOverwrite(string title, string message) {
        return _allowOverwrite || Confirm(title, message);
    }

    private bool Confirm(string title, string message) {
        // TODO: when the Confirmations module is ported to C#, use it here
        var options = new Collection<ChoiceDescription> {new("&Yes"), new("&No")};
        return Host.UI.PromptForChoice(title, message, options, 0) switch {
            0 => true,
            1 => false,
            _ => throw new InvalidDataException(),
        };
    }

    public class UserRefusedOverwriteException : Exception {
        public UserRefusedOverwriteException(string message) : base(message) {}
    }
}
