using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Pog.Native;

/// Lifted from FileSystem.Windows.cs, .NET 7
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class Symlink {
    private const int MAXIMUM_REPARSE_DATA_BUFFER_SIZE = 16384;
    private const int FSCTL_GET_REPARSE_POINT = 0x000900a8;
    private const uint SYMLINK_FLAG_RELATIVE = 1;
    private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
    private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

    /// <summary>
    /// Gets reparse point information associated to <paramref name="linkPath"/>.
    /// </summary>
    /// <returns>The immediate link target, absolute or relative or null if the file is not a supported link.</returns>
    public static unsafe string? GetLinkTarget(string linkPath, bool throwOnError = true, bool returnFullPath = false) {
        using var handle = Win32.CreateFile(linkPath, 0, FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero, FileMode.Open, Win32.FILE_FLAG.BACKUP_SEMANTICS | Win32.FILE_FLAG.OPEN_REPARSE_POINT,
                IntPtr.Zero);

        if (handle.IsInvalid) {
            if (!throwOnError) {
                return null;
            }
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        var buffer = ArrayPool<byte>.Shared.Rent(MAXIMUM_REPARSE_DATA_BUFFER_SIZE);
        try {
            bool success;
            uint bytesReturned = 0;
            fixed (byte* pBuffer = buffer) {
                success = 0 != DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, null, 0, pBuffer,
                        (uint) buffer.Length, &bytesReturned, IntPtr.Zero);
            }

            if (!success) {
                if (!throwOnError) {
                    return null;
                }

                var error = Marshal.GetLastWin32Error();
                // The file or directory is not a reparse point.
                if (error == Win32.Errors.ERROR_NOT_A_REPARSE_POINT) {
                    return null;
                }
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            Span<byte> bufferSpan = new(buffer);
            success = MemoryMarshal.TryRead(bufferSpan, out SymbolicLinkReparseBuffer rbSymlink);
            Debug.Assert(success);

            // We always use SubstituteName(Offset|Length) instead of PrintName(Offset|Length),
            // the latter is just the display name of the reparse point and it can show something completely unrelated to the target.

            if (rbSymlink.ReparseTag == IO_REPARSE_TAG_SYMLINK) {
                int offset = sizeof(SymbolicLinkReparseBuffer) + rbSymlink.SubstituteNameOffset;
                int length = rbSymlink.SubstituteNameLength;

                Span<char> targetPath = MemoryMarshal.Cast<byte, char>(bufferSpan.Slice(offset, length));

                var isRelative = (rbSymlink.Flags & SYMLINK_FLAG_RELATIVE) != 0;
                if (!isRelative) {
                    // Absolute target is in NT format and we need to clean it up before return it to the user.
                    if (targetPath.StartsWith(PathInternal.UncNTPathPrefix.AsSpan())) {
                        // We need to prepend the Win32 equivalent of UNC NT prefix.
                        return PathInternal.UncPathPrefix +
                               targetPath.Slice(PathInternal.UncNTPathPrefix.Length).ToString();
                    }

                    return GetTargetPathWithoutNTPrefix(targetPath);
                } else if (returnFullPath) {
                    return Path.GetDirectoryName(linkPath) + "\\" + targetPath.ToString();
                } else {
                    return targetPath.ToString();
                }
            } else if (rbSymlink.ReparseTag == IO_REPARSE_TAG_MOUNT_POINT) {
                success = MemoryMarshal.TryRead(bufferSpan, out MountPointReparseBuffer rbMountPoint);
                Debug.Assert(success);

                int offset = sizeof(MountPointReparseBuffer) + rbMountPoint.SubstituteNameOffset;
                int length = rbMountPoint.SubstituteNameLength;

                Span<char> targetPath = MemoryMarshal.Cast<byte, char>(bufferSpan.Slice(offset, length));
                // Unlike symbolic links, mount point paths cannot be relative.
                return GetTargetPathWithoutNTPrefix(targetPath);
            }

            return null;
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        static string GetTargetPathWithoutNTPrefix(ReadOnlySpan<char> targetPath) {
            Debug.Assert(targetPath.StartsWith(PathInternal.NTPathPrefix.AsSpan()));
            return targetPath.Slice(PathInternal.NTPathPrefix.Length).ToString();
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", ExactSpelling = true)]
    private static extern unsafe int DeviceIoControl(SafeHandle hDevice, uint dwIoControlCode, void* lpInBuffer,
            uint nInBufferSize, void* lpOutBuffer, uint nOutBufferSize, uint* lpBytesReturned, nint lpOverlapped);

    // https://msdn.microsoft.com/library/windows/hardware/ff552012.aspx
    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    private struct SymbolicLinkReparseBuffer {
        public readonly uint ReparseTag;
        public readonly ushort ReparseDataLength;
        public readonly ushort Reserved;
        public readonly ushort SubstituteNameOffset;
        public readonly ushort SubstituteNameLength;
        public readonly ushort PrintNameOffset;
        public readonly ushort PrintNameLength;
        public readonly uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    private struct MountPointReparseBuffer {
        public readonly uint ReparseTag;
        public readonly ushort ReparseDataLength;
        public readonly ushort Reserved;
        public readonly ushort SubstituteNameOffset;
        public readonly ushort SubstituteNameLength;
        public readonly ushort PrintNameOffset;
        public readonly ushort PrintNameLength;
    }

    /// <summary>
    /// The link target is a directory.
    /// </summary>
    private const int SYMBOLIC_LINK_FLAG_DIRECTORY = 0x1;

    /// <summary>
    /// Allows creation of symbolic links from a process that is not elevated. Requires Windows 10 Insiders build 14972 or later.
    /// Developer Mode must first be enabled on the machine before this option will function.
    /// </summary>
    private const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;

    [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CreateSymbolicLinkPrivate(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

    /// <summary>
    /// Creates a symbolic link.
    /// </summary>
    /// <param name="symlinkFileName">The symbolic link to be created.</param>
    /// <param name="targetFileName">The name of the target for the symbolic link to be created.
    /// If it has a device name associated with it, the link is treated as an absolute link; otherwise, the link is treated as a relative link.</param>
    /// <param name="isDirectory"><see langword="true" /> if the link target is a directory; <see langword="false" /> otherwise.</param>
    public static void CreateSymbolicLink(string symlinkFileName, string targetFileName, bool isDirectory) {
        symlinkFileName = PathInternal.EnsureExtendedPrefixIfNeeded(symlinkFileName);
        targetFileName = PathInternal.EnsureExtendedPrefixIfNeeded(targetFileName);

        var flags = 0;

        var osVersion = Environment.OSVersion.Version;
        var isAtLeastWin10Build14972 = osVersion.Major >= 11 || osVersion is {Major: 10, Build: >= 14972};

        if (isAtLeastWin10Build14972) {
            flags = SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE;
        }

        if (isDirectory) {
            flags |= SYMBOLIC_LINK_FLAG_DIRECTORY;
        }

        if (!CreateSymbolicLinkPrivate(symlinkFileName, targetFileName, flags)) {
            if (Marshal.GetLastWin32Error() == Win32.Errors.ERROR_FILE_EXISTS) {
                throw new FileAlreadyExistsException();
            } else {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }
    }

    public class FileAlreadyExistsException : Exception {}
}
