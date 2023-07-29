using System;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace Pog.Native;

// This class exists because .NET API (and win32 MoveItem) does not allow us to differ between
//  "we don't have permission to rename a directory" and "there's a locked entry in the directory".
[PublicAPI]
public static class DirectoryUtils {
    // source: https://github.com/microsoft/BuildXL/blob/d09e1c45d68a81ccabff3f32e0e31c855ee246f8/Public/Src/Utilities/Native/IO/Windows/FileSystem.Win.cs#L1605=
    public static unsafe void MoveFileByHandle(SafeFileHandle handle, string destinationPath,
            bool replaceExistingFile = false) {
        // we cannot use normal marshalling, because the FILE_RENAME_INFO struct has a variable-length string as the last member,
        // and C# P/Invoke doesn't really support that, so we have to build the whole struct ourselves as a buffer.

        // FILE_RENAME_INFO as we've defined it contains one character which is enough for a terminating null byte.
        // Then, we need room for the actual characters.
        int fileNameLengthInBytesExcludingNull = destinationPath.Length * sizeof(char);
        int structSizeIncludingDestination = sizeof(Win32.FILE_RENAME_INFO) + fileNameLengthInBytesExcludingNull;

        fixed (byte* b = new byte[structSizeIncludingDestination]) {
            // fill out the struct members
            var renameInfo = (Win32.FILE_RENAME_INFO*) b;
            renameInfo->ReplaceIfExists = replaceExistingFile;
            renameInfo->RootDirectory = IntPtr.Zero;
            renameInfo->FileNameLengthInBytes = fileNameLengthInBytesExcludingNull + sizeof(char);

            // copy the filename string to the struct
            char* filenameBuffer = &renameInfo->FileName;
            for (int i = 0; i < destinationPath.Length; i++) {
                filenameBuffer[i] = destinationPath[i];
            }
            filenameBuffer[destinationPath.Length] = (char) 0;

            const int fileRenameInformationClass = 3;
            if (!Win32.SetFileInformationByHandle(handle, fileRenameInformationClass, renameInfo,
                        structSizeIncludingDestination)) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }
    }

    private static SafeFileHandle CreateFileWrapped(string filename, uint desiredAccess, FileShare sharedMode,
            FileMode creationDisposition, Win32.FILE_FLAG flagsAndAttributes) {
        var handle = Win32.CreateFile(filename, desiredAccess, sharedMode, IntPtr.Zero, creationDisposition,
                flagsAndAttributes, IntPtr.Zero);
        if (handle.IsInvalid) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return handle;
    }

    /// This method, together with `MoveFileByHandle`, is useful to distinguish between 2 possible causes
    /// for an Access Denied error, which are conflated together with `MoveFile`:
    /// 1) We don't have sufficient permissions to move the directory (in which case this method throws
    ///    an Access Denied exception).
    /// 2) There's a locked entry in the directory and we cannot move it (in which case the same exception
    ///    is thrown from `MoveFileByHandle`).
    public static SafeFileHandle OpenDirectoryForMove(string directoryPath) {
        // ReSharper disable once InconsistentNaming
        const uint ACCESS_DELETE = 0x00010000;
        // using `FileShare.Read`, because e.g. Explorer likes to hold read handles to directories
        return CreateFileWrapped(directoryPath, ACCESS_DELETE, FileShare.Read,
                FileMode.Open, Win32.FILE_FLAG.BACKUP_SEMANTICS);
    }

    public static SafeFileHandle OpenDirectoryReadOnly(string directoryPath) {
        return CreateFileWrapped(directoryPath, (uint) FileAccess.Read, FileShare.Read,
                FileMode.Open, Win32.FILE_FLAG.BACKUP_SEMANTICS);
    }

    /// This method is very similar to PowerShell `Rename-Item`, which iirc internally calls `MoveFile`.
    /// TODO: why did I write this? iirc there's some issue with MoveFile, cannot remember what it is;
    ///       maybe something with it trying to move directories file-by-file?
    public static void MoveDirectoryAtomically(string srcDirPath, string targetPath) {
        using var handle = OpenDirectoryForMove(srcDirPath);
        MoveFileByHandle(handle, targetPath);
    }

    /// It is not possible to atomically delete a directory. Instead, we use a temporary directory
    /// to first move it out of the way, and then delete it. Note that `tmpMovePath` must
    /// be at same filesystem as `srcDirPath`.
    public static void DeleteDirectoryAtomically(string srcDirPath, string tmpMovePath, bool ignoreNotFound = false) {
        try {
            MoveDirectoryAtomically(srcDirPath, tmpMovePath);
        } catch (FileNotFoundException) {
            if (ignoreNotFound) return;
            throw;
        }
        Directory.Delete(tmpMovePath, true);
    }

    /// Attempts to atomically move the directory at `srcPath` to `destinationPath`. Returns `true on success,
    /// `false` if the directory is locked, throws an exception for other error cases.
    ///
    /// <remarks>If `destinationPath` is locked, this function erroneously returns true.</remarks>
    ///
    /// <exception cref="SystemException"></exception>
    public static bool MoveDirectoryUnlocked(string srcPath, string destinationPath) {
        SafeFileHandle dirHandle;
        try {
            dirHandle = OpenDirectoryForMove(srcPath);
        } catch (FileLoadException) {
            // the directory at dirPath is locked by another process
            return false;
        }

        using (dirHandle) {
            try {
                MoveFileByHandle(dirHandle, destinationPath);
            } catch (UnauthorizedAccessException) {
                // 2 possibilities:
                // 1) an entry inside srcPath is locked by another process
                // 2) an entry inside destinationPath is locked by another process
                // FIXME: it doesn't seem there's an easy way to distinguish between these two;
                //  currently, we always return true
                return false;
            }
        }

        return true;
    }

    /// Returns true if any entry inside the directory or the directory is locked.
    public static bool IsDirectoryLocked(string directoryPath) {
        // move directory to itself; this returns false when the directory contains anything locked, and is a no-op otherwise
        return !MoveDirectoryUnlocked(directoryPath, directoryPath);
    }
}
