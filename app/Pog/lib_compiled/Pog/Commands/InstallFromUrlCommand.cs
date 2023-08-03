using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using Pog.Commands.Internal;
using Pog.Native;

namespace Pog.Commands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Install, "FromUrl", DefaultParameterSetName = "Archive")]
public class InstallFromUrlCommand : PSCmdlet, IDisposable {
    /// Source URL, from which the archive is downloaded. Redirects are supported.
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("Url")]
    public string SourceUrl = null!;
    /// SHA-256 hash that the downloaded archive should match. Validation is skipped if null, but a warning is printed.
    [Parameter(ValueFromPipelineByPropertyName = true)]
    [Alias("Hash")]
    [Verify.Sha256Hash]
    public string? ExpectedHash;
    /// If passed, only the subdirectory with passed name/path is extracted to ./app and the rest is ignored.
    [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "Archive")]
    [Verify.FilePath]
    public string? Subdirectory;
    /// If passed, the extracted directory is moved to "./app/$Target", instead of directly to ./app.
    [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "Archive")]
    // make Target mandatory when NoArchive is set, otherwise the name of the binary would be controlled by the server
    //  we're downloading from, making the resulting package no longer reproducible based on just the hash
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "NoArchive")]
    [Verify.FilePath]
    public string? Target;
    /// Some servers (e.g. Apache Lounge) dislike PowerShell user agent string for some reason.
    /// Set this to `Browser` to use a browser user agent string (currently Firefox).
    /// Set this to `Wget` to use wget user agent string.
    [Parameter(ValueFromPipelineByPropertyName = true)]
    public DownloadParameters.UserAgentType UserAgent = DownloadParameters.UserAgentType.PowerShell;
    /// If you need to modify the extracted archive (e.g. remove some files), pass a scriptblock, which receives
    /// a path to the extracted directory as its only argument. All modifications to the extracted files should be
    /// done in this scriptblock – this ensures that the ./app directory is not left in an inconsistent state
    /// in case of a crash during installation.
    [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "Archive")]
    [Alias("Setup")]
    public ScriptBlock? SetupScript;
    // TODO: auto-detect NSIS installers and remove the flag?
    /// Pass this if the retrieved file is an NSIS installer
    /// Currently, only thing this does is remove the `$PLUGINSDIR` output directory.
    /// NOTE: NSIS installers may do some initial config, which is not ran when extracted directly.
    [Parameter(ValueFromPipelineByPropertyName = true, ParameterSetName = "Archive")]
    public SwitchParameter NsisInstaller;
    /// If passed, the downloaded file is directly moved to the ./app directory, without being treated as an archive and extracted.
    // this parameter must be mandatory, otherwise it is ignored when piping input objects and the default "Archive"
    //  parameter set is used instead
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "NoArchive")]
    public SwitchParameter NoArchive;


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
    private DownloadParameters _downloadParameters = null!;
    private Package _package = null!;


    /// here, only parameter validation and setup is done
    protected override void BeginProcessing() {
        base.BeginProcessing();
        _packageDirPath = SessionState.Path.CurrentLocation.ProviderPath;
        _appDirPath = _p(AppDirName);
        _newAppDirPath = _p(NewAppDirName);

        // read download parameters from the global container info variable
        var internalInfo = Container.ContainerInternalInfo.GetCurrent(this);
        var lowPriorityDownload = (bool) internalInfo.InternalArguments["DownloadLowPriority"];
        _allowOverwrite = (bool) internalInfo.InternalArguments["AllowOverwrite"];
        _downloadParameters = new DownloadParameters(UserAgent, lowPriorityDownload);
        _package = internalInfo.Package;


        if (new[] {_p(TmpExtractionDirName), _newAppDirPath, _p(TmpDeleteDirName)}.Any(FileUtils.EnsureDeleteDirectory)) {
            WriteWarning("Removed orphaned tmp installer directories, probably from an interrupted previous install...");
        }

        if (Directory.Exists(_p(AppBackupDirName))) {
            // the installation has been interrupted before it cleaned up; to be safe, always revert to the previous version,
            //  in case we add some post-install steps after the ./app directory is moved in place, because otherwise if we
            //  would keep the new version, we'd have to check that the follow-up steps all finished
            if (Directory.Exists(_appDirPath)) {
                WriteWarning("Clearing an incomplete app directory from a previous interrupted install...");
                // remove atomically, so that the user doesn't see a partially deleted app directory in case this is interrupted again
                DirectoryUtils.DeleteDirectoryAtomically(_appDirPath, _p(TmpDeleteDirName));
            }
            WriteWarning("Restoring the previous app directory to recover from an interrupted install...");
            DirectoryUtils.MoveDirectoryAtomically(_p(AppBackupDirName), _appDirPath);
        }

        if (Directory.Exists(_appDirPath)) {
            var shouldContinue = ConfirmOverwrite("Overwrite existing package installation?",
                    "Package seems to be already installed. Do you want to overwrite the current installation" +
                    " (./app subdirectory)?\nConfiguration and other package data will be kept.");

            if (!shouldContinue) {
                var exception = new UserRefusedOverwriteException(
                        "Not installing, user refused to overwrite existing package installation." +
                        " Do not pass -Confirm to overwrite the existing installation without confirmation.");
                ThrowTerminatingError(new ErrorRecord(exception, "UserRefusedOverwrite", ErrorCategory.OperationStopped,
                        null));
            }

            // next, we check if we can move/delete the current ./app directory
            // e.g. maybe the packaged program is running and holding a lock over a file inside
            if (DirectoryUtils.IsDirectoryLocked(_appDirPath)) {
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

        // hash should always be uppercase
        ExpectedHash = ExpectedHash?.ToUpper();
        if (ExpectedHash == null) {
            WriteWarning($"Downloading a file from '{SourceUrl}', but no checksum was provided in the package." +
                         " This means that we cannot be sure if the downloaded file is the same one the package author intended." +
                         " This may or may not be a problem on its own, but it's a better style to include a checksum," +
                         " and it improves security and reproducibility.");
        }

        Debug.Assert(!NoArchive || Target != null);
        var targetPath = Target == null
                ? _newAppDirPath
                : Path.GetFullPath(_newAppDirPath + '\\' + Target.TrimEnd('/', '\\'));

        if (!targetPath.StartsWith(_newAppDirPath + '\\') && targetPath != _newAppDirPath) {
            // the target path escapes from the app directory
            ThrowTerminatingError(new ErrorRecord(
                    new ArgumentException(
                            $"Argument passed to the -Target parameter must be a relative path that does not escape " +
                            $"the app directory, got '{Target}'."),
                    "TargetEscapesRoot", ErrorCategory.InvalidArgument, Target));
        }

        if (NoArchive) {
            if (targetPath == _newAppDirPath) {
                ThrowTerminatingError(new ErrorRecord(new ArgumentException(
                                $"Argument passed to the -Target parameter must contain the target file name, got '{Target}'"),
                        "TargetResolvesToRoot", ErrorCategory.InvalidArgument, Target));
            }
        }

        using var downloadedFile = InvokeFileDownload.Invoke(
                this, SourceUrl, ExpectedHash, _downloadParameters, _package, false);

        if (NoArchive) {
            InstallNoArchive(downloadedFile, targetPath);
        } else {
            InstallArchive(downloadedFile, targetPath);
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
        FileUtils.EnsureDeleteDirectory(_p(TmpDeleteDirName));
        FileUtils.EnsureDeleteDirectory(_p(TmpExtractionDirName));
        FileUtils.EnsureDeleteDirectory(_newAppDirPath);
        // do not attempt to delete AppBackupDirName here, it should be already cleaned up
        //  (and if it isn't, it probably also won't work here)
    }

    /// Resolve a package-relative path to an absolute path.
    private string _p(string relPath) {
        return Path.Combine(_packageDirPath, relPath);
    }

    private void InstallNoArchive(SharedFileCache.IFileLock downloadedFile, string targetPath) {
        // ensure that the parent directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        // copy the file directly to the target path
        // for NoArchive, Target contains even the file name, not just the directory name
        File.Copy(downloadedFile.Path, targetPath, true);
    }

    private void InstallArchive(SharedFileCache.IFileLock downloadedFile, string targetPath) {
        // extract the archive to a temporary directory
        var extractionDir = _p(TmpExtractionDirName);
        var cmd = new ExpandArchive7Zip(this, downloadedFile.Path, extractionDir, Subdirectory);
        _currentRunningCmd = cmd;
        cmd.Invoke();
        _currentRunningCmd = null;
        downloadedFile.Dispose();

        // find and prepare the used subdirectory
        var usedDir = GetExtractedSubdirectory(extractionDir, Subdirectory);
        PrepareExtractedSubdirectory(usedDir.FullName, SetupScript, NsisInstaller);

        // move `usedDir` to the new app directory
        if (!Directory.Exists(targetPath)) {
            var parentPath = Path.GetDirectoryName(targetPath)!;
            // ensure parent directory exists
            Directory.CreateDirectory(parentPath);
            usedDir.MoveTo(targetPath);
        } else {
            // TODO: handle existing target (overwrite?)
            FileUtils.MoveDirectoryContents(usedDir, targetPath);
        }
    }

    private DirectoryInfo GetExtractedSubdirectory(string extractedRootPath, string? subdirectory) {
        if (subdirectory == null) {
            var root = new DirectoryInfo(extractedRootPath);
            // this only loads metadata for the first 2 files without iterating over the whole directory
            var entries = root.EnumerateFileSystemInfos().Take(2).ToArray();
            if (entries.Length == 1 && (entries[0].Attributes & FileAttributes.Directory) != 0) {
                // single directory in archive root (as is common for Linux-style archives)
                WriteDebug($"Archive root contains a single directory '{entries[0].Name}', using it for './app'.");
                return (DirectoryInfo) entries[0];
            } else {
                // no single subdirectory, multiple (or no) files in root (Windows-style archive)
                WriteDebug("Archive root contains multiple items, using archive root directly for './app'.");
                return root;
            }
        } else {
            var subPath = Path.Combine(extractedRootPath, subdirectory);
            WriteDebug($"Using passed path inside archive: {subPath}");

            // test if the path exists in the extracted directory
            DirectoryInfo sub = null!;
            try {
                sub = new(subPath);
            } catch (DirectoryNotFoundException e) {
                var dirStr = string.Join(", ",
                        Directory.EnumerateFileSystemEntries(extractedRootPath)
                                .Select(p => "'" + Path.GetFileName(p) + "'"));
                var exception = new DirectoryNotFoundException(
                        $"'-Subdirectory {subdirectory}' param was provided to 'Install-FromUrl' " +
                        "in package manifest, but the directory does not exist inside the archive. " +
                        $"Root of the archive contains the following items: {dirStr}", e);
                ThrowTerminatingError(new ErrorRecord(exception, "ArchiveSubdirectoryNotFound", ErrorCategory.InvalidData,
                        subdirectory));
            }

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
            var pluginDirPath = Path.Combine(subdirectoryPath, "$PLUGINDIR");
            try {
                Directory.Delete(pluginDirPath, true);
                WriteDebug("Removed $PLUGINSDIR directory from the extracted NSIS installer archive.");
            } catch (DirectoryNotFoundException e) {
                var exception = new DirectoryNotFoundException(
                        "'-NsisInstaller' flag was passed to 'Install-FromUrl' in package manifest, " +
                        "but the directory '`$PLUGINSDIR' does not exist in the extracted path (NSIS self-extracting " +
                        "archive should contain it).", e);
                ThrowTerminatingError(new ErrorRecord(exception, "NsisPluginDirNotFound", ErrorCategory.InvalidData,
                        pluginDirPath));
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
        using var newAppDirHandle = DirectoryUtils.OpenDirectoryForMove(srcDir);
        try {
            DirectoryUtils.MoveFileByHandle(newAppDirHandle, targetAppDir);
            // success, targetAppDir did not originally exist, the new app directory is in place
            return;
        } catch (COMException e) {
            // -2147024713 (0x800700B7) = target already exists
            if (e.HResult != -2147024713) {
                throw;
            }
        }

        // the target app directory already exists, so we need to replace it
        // however, win32 does not give us an API to atomically swap/replace a directory, except for NTFS transactions,
        // which seem to be mostly deprecated; instead, we'll have to first move the current app directory away,
        // and then move the replacement app directory in; there's a short moment between the move where we'll
        // get an unusable app if the operation fails/crashes, but it seems we cannot do any better than that
        using (var oldAppDirHandle = MoveOutOldAppDirectory(targetAppDir, backupDir)) {
            try {
                DirectoryUtils.MoveFileByHandle(newAppDirHandle, targetAppDir);
                // success, the new app directory is in place
            } catch {
                // at least attempt to move back the original app directory
                DirectoryUtils.MoveFileByHandle(oldAppDirHandle, targetAppDir);
                throw;
            }
        }
        // delete the backup app directory
        Directory.Delete(backupDir, true);
    }

    private SafeFileHandle MoveOutOldAppDirectory(string appDirPath, string backupPath) {
        var i = 0;
        SafeFileHandle oldAppDirHandle;
        for (;; i++) {
            try {
                oldAppDirHandle = DirectoryUtils.OpenDirectoryForMove(appDirPath);
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
                    DirectoryUtils.MoveFileByHandle(oldAppDirHandle, backupPath);
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
