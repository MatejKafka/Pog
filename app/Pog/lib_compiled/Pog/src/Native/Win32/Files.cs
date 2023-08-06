using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Pog.Native;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static partial class Win32 {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern unsafe bool SetFileInformationByHandle(SafeFileHandle fileHandle,
            FILE_INFO_BY_HANDLE_CLASS fileInformationClass, void* fileInformation, int fileInformationSize);

    public static unsafe bool SetFileInformationByHandle_FileRenameInfo(SafeFileHandle handle, string destinationPath,
            bool replaceExistingFile = false) {
        // source: https://github.com/microsoft/BuildXL/blob/d09e1c45d68a81ccabff3f32e0e31c855ee246f8/Public/Src/Utilities/Native/IO/Windows/FileSystem.Win.cs#L1605=

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

            return SetFileInformationByHandle(handle, FILE_INFO_BY_HANDLE_CLASS.FileRenameInfo, renameInfo,
                    structSizeIncludingDestination);
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum FILE_INFO_BY_HANDLE_CLASS {
        FileBasicInfo,
        FileStandardInfo,
        FileNameInfo,
        FileRenameInfo,
        FileDispositionInfo,
        FileAllocationInfo,
        FileEndOfFileInfo,
        FileStreamInfo,
        FileCompressionInfo,
        FileAttributeTagInfo,
        FileIdBothDirectoryInfo,
        FileIdBothDirectoryRestartInfo,
        FileIoPriorityHintInfo,
        FileRemoteProtocolInfo,
        FileFullDirectoryInfo,
        FileFullDirectoryRestartInfo,
        FileStorageInfo,
        FileAlignmentInfo,
        FileIdInfo,
        FileIdExtdDirectoryInfo,
        FileIdExtdDirectoryRestartInfo,
        FileDispositionInfoEx,
        FileRenameInfoEx,
        FileCaseSensitiveInfo,
        FileNormalizedNameInfo,
        MaximumFileInfoByHandleClass
    }

    [Flags]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum FILE_FLAG : uint {
        ATTRIBUTE_READONLY = 0x1,
        ATTRIBUTE_HIDDEN = 0x2,
        ATTRIBUTE_SYSTEM = 0x4,
        ATTRIBUTE_ARCHIVE = 0x20,
        ATTRIBUTE_NORMAL = 0x80,
        ATTRIBUTE_TEMPORARY = 0x100,
        ATTRIBUTE_OFFLINE = 0x1000,
        ATTRIBUTE_ENCRYPTED = 0x4000,

        // file is deleted when this handle is closed
        OPEN_NO_RECALL = 0x00100000,
        OPEN_REPARSE_POINT = 0x00200000,
        SESSION_AWARE = 0x00800000,
        POSIX_SEMANTICS = 0x01000000,
        // must be passed to open a directory handle (otherwise you can only create file handles)
        BACKUP_SEMANTICS = 0x02000000,
        DELETE_ON_CLOSE = 0x04000000,
        SEQUENTIAL_SCAN = 0x08000000,
        RANDOM_ACCESS = 0x10000000,
        NO_BUFFERING = 0x20000000,
        OVERLAPPED = 0x40000000,
        WRITE_THROUGH = 0x80000000,
    }

    // ReSharper disable once InconsistentNaming
    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_RENAME_INFO {
        public bool ReplaceIfExists;
        public IntPtr RootDirectory;
        /// Length of the string starting at <see cref="FileName"/> in *bytes* (not characters).
        public int FileNameLengthInBytes;
        /// First character of filename; this is a variable length array as determined by FileNameLength.
        public readonly char FileName;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
            string filename,
            uint desiredAccess = (uint) FileAccess.ReadWrite,
            FileShare sharedMode = FileShare.None,
            IntPtr securityAttributes = default,
            FileMode creationDisposition = FileMode.OpenOrCreate,
            FILE_FLAG flagsAndAttributes = FILE_FLAG.ATTRIBUTE_NORMAL,
            IntPtr templateFile = default);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockFileEx(SafeFileHandle hFile, LockFileFlags dwFlags, uint dwReserved,
            uint nNumberOfBytesToLockLow, uint nNumberOfBytesToLockHigh, [In] ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnlockFileEx(SafeFileHandle hFile, uint dwReserved,
            uint nNumberOfBytesToLockLow, uint nNumberOfBytesToLockHigh, [In] ref NativeOverlapped lpOverlapped);

    [Flags]
    public enum LockFileFlags {
        EXCLUSIVE_LOCK = 0x00000002,
        FAIL_IMMEDIATELY = 0x00000001,
        WAIT = 0x00000000,
    }
}
