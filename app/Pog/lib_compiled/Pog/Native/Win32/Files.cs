using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Pog;

public static partial class Win32 {
    [DllImport("Kernel32.dll", SetLastError = true)]
    public static extern unsafe bool SetFileInformationByHandle(SafeFileHandle fileHandle, int fileInformationClass,
            FILE_RENAME_INFO* fileInformation, int fileInformationSize);

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Flags]
    public enum FILE_FLAG {
        // must be passed to open a directory handle (otherwise you can only create file handles)
        BACKUP_SEMANTICS = 0x02000000,
        DELETE_ON_CLOSE = 0x04000000,
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string filename, uint desiredAccess, FileShare sharedMode,
            IntPtr securityAttributes, FileMode creationDisposition, FILE_FLAG flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockFileEx(IntPtr hFile, LockFileFlags dwFlags, uint dwReserved,
            uint nNumberOfBytesToLockLow, uint nNumberOfBytesToLockHigh, [In] ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnlockFileEx(IntPtr hFile, uint dwReserved,
            uint nNumberOfBytesToLockLow, uint nNumberOfBytesToLockHigh, [In] ref NativeOverlapped lpOverlapped);

    [Flags]
    public enum LockFileFlags {
        // ReSharper disable once InconsistentNaming
        EXCLUSIVE_LOCK = 0x00000002,
        // ReSharper disable once InconsistentNaming
        FAIL_IMMEDIATELY = 0x00000001,
        // ReSharper disable once InconsistentNaming
        WAIT = 0x00000000,
    }
}
