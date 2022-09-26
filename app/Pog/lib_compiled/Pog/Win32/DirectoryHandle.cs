using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pog;

// This class exists because .NET API (and win32 MoveItem) does not allow us to differ between
//  "we don't have permission to rename a directory" and "there's a locked entry in the directory".
public static partial class Win32 {
    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern unsafe bool SetFileInformationByHandle(SafeFileHandle fileHandle, int fileInformationClass,
            FILE_RENAME_INFO* fileInformation, int fileInformationSize);

    // source: https://github.com/microsoft/BuildXL/blob/d09e1c45d68a81ccabff3f32e0e31c855ee246f8/Public/Src/Utilities/Native/IO/Windows/FileSystem.Win.cs#L1605=
    public static unsafe void MoveFileByHandle(SafeFileHandle handle, string destinationPath,
            bool replaceExistingFile = false) {
        // we cannot use normal marshalling, because the FILE_RENAME_INFO struct has a variable-length string as the last member,
        // and C# P/Invoke doesn't really support that, so we have to build the whole struct ourselves as a buffer.

        // FILE_RENAME_INFO as we've defined it contains one character which is enough for a terminating null byte.
        // Then, we need room for the actual characters.
        int fileNameLengthInBytesExcludingNull = destinationPath.Length * sizeof(char);
        int structSizeIncludingDestination = sizeof(FILE_RENAME_INFO) + fileNameLengthInBytesExcludingNull;

        fixed (byte* b = new byte[structSizeIncludingDestination]) {
            // fill out the struct members
            var renameInfo = (FILE_RENAME_INFO*) b;
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
            if (!SetFileInformationByHandle(handle, fileRenameInformationClass, renameInfo,
                        structSizeIncludingDestination)) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string filename, uint desiredAccess, FileShare sharedMode,
            IntPtr securityAttributes, FileMode creationDisposition, FILE_FLAG flagsAndAttributes, IntPtr templateFile);

    private static SafeFileHandle CreateFileWrapped(string filename, uint desiredAccess, FileShare sharedMode,
            FileMode creationDisposition, FILE_FLAG flagsAndAttributes) {
        var handle = CreateFile(filename, desiredAccess, sharedMode, IntPtr.Zero, creationDisposition, flagsAndAttributes,
                IntPtr.Zero);
        if (handle.IsInvalid) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return handle;
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Flags]
    private enum FILE_FLAG {
        // must be passed to open a directory handle (otherwise you can only create file handles)
        BACKUP_SEMANTICS = 0x02000000,
        DELETE_ON_CLOSE = 0x04000000,
    }

    /// Don't forget to call Dispose() on the returned handle when you're done
    /// FIXME: createIfNotExists creates a file, not a directory!!!
    public static SafeFileHandle OpenDirectoryForMove(string directoryPath) {
        // ReSharper disable once InconsistentNaming
        const uint ACCESS_DELETE = 0x00010000;
        return CreateFileWrapped(directoryPath, ACCESS_DELETE, FileShare.Read | FileShare.Delete,
                FileMode.Open, FILE_FLAG.BACKUP_SEMANTICS);
    }

    public static SafeFileHandle OpenDirectoryReadOnly(string directoryPath) {
        return CreateFileWrapped(directoryPath, (uint) FileAccess.Read, FileShare.Read | FileShare.Delete,
                FileMode.Open, FILE_FLAG.BACKUP_SEMANTICS);
    }

    // ReSharper disable once InconsistentNaming
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_RENAME_INFO {
        public bool ReplaceIfExists;
        public IntPtr RootDirectory;
        /// Length of the string starting at <see cref="FileName"/> in *bytes* (not characters).
        public int FileNameLengthInBytes;
        /// First character of filename; this is a variable length array as determined by FileNameLength.
        public readonly char FileName;
    }
}