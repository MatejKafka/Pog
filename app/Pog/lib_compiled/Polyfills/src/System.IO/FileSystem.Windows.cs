// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Buffers;
using Polyfills.Microsoft.Win32.SafeHandles;
using OPath = System.IO.Path;

namespace Polyfills.System.IO
{
    internal static partial class FileSystem
    {
        private static void GetFindData(string fullPath, bool isDirectory, bool ignoreAccessDenied, ref Interop.Kernel32.WIN32_FIND_DATA findData)
        {
            using SafeFindHandle handle = Interop.Kernel32.FindFirstFile(Path.TrimEndingDirectorySeparator(fullPath), ref findData);
            if (handle.IsInvalid)
            {
                int errorCode = Marshal.GetLastWin32Error();
                // File not found doesn't make much sense coming from a directory.
                if (isDirectory && errorCode == Interop.Errors.ERROR_FILE_NOT_FOUND)
                    errorCode = Interop.Errors.ERROR_PATH_NOT_FOUND;
                if (ignoreAccessDenied && errorCode == Interop.Errors.ERROR_ACCESS_DENIED)
                    return;
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }

        private static bool IsNameSurrogateReparsePoint(ref Interop.Kernel32.WIN32_FIND_DATA data)
        {
            // Name surrogates are reparse points that point to other named entities local to the file system.
            // Reparse points can be used for other types of files, notably OneDrive placeholder files. We
            // should treat reparse points that are not name surrogates as any other directory, e.g. recurse
            // into them. Surrogates should just be detached.
            //
            // See
            // https://github.com/dotnet/runtime/issues/23646
            // https://msdn.microsoft.com/en-us/library/windows/desktop/aa365511.aspx
            // https://msdn.microsoft.com/en-us/library/windows/desktop/aa365197.aspx

            return ((FileAttributes)data.dwFileAttributes & FileAttributes.ReparsePoint) != 0
                && (data.dwReserved0 & 0x20000000) != 0; // IsReparseTagNameSurrogate
        }

        internal static void CreateSymbolicLink(string path, string pathToTarget, bool isDirectory)
        {
            Interop.Kernel32.CreateSymbolicLink(path, pathToTarget, isDirectory);
        }

        internal static FileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget, bool isDirectory)
        {
            var targetPath = returnFinalTarget ?
                    GetFinalLinkTarget(linkPath, isDirectory) :
                    GetImmediateLinkTarget(linkPath, throwOnError: true, returnFullPath: true);

            return targetPath == null ? null :
                isDirectory ? new DirectoryInfo(targetPath) : new FileInfo(targetPath);
        }

        internal static string? GetLinkTarget(string linkPath)
            => GetImmediateLinkTarget(linkPath, throwOnError: false, returnFullPath: false);

        /// <summary>
        /// Gets reparse point information associated to <paramref name="linkPath"/>.
        /// </summary>
        /// <returns>The immediate link target, absolute or relative or null if the file is not a supported link.</returns>
        internal static unsafe string? GetImmediateLinkTarget(string linkPath, bool throwOnError, bool returnFullPath)
        {
            using SafeFileHandle handle = OpenSafeFileHandle(linkPath,
                    Interop.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS |
                    Interop.Kernel32.FileOperations.FILE_FLAG_OPEN_REPARSE_POINT);

            if (handle.IsInvalid)
            {
                if (!throwOnError)
                {
                    return null;
                }

                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(Interop.Kernel32.MAXIMUM_REPARSE_DATA_BUFFER_SIZE);
            try
            {
                bool success;

                fixed (byte* pBuffer = buffer)
                {
                    success = Interop.Kernel32.DeviceIoControl(
                        handle,
                        dwIoControlCode: Interop.Kernel32.FSCTL_GET_REPARSE_POINT,
                        lpInBuffer: null,
                        nInBufferSize: 0,
                        lpOutBuffer: pBuffer,
                        nOutBufferSize: Interop.Kernel32.MAXIMUM_REPARSE_DATA_BUFFER_SIZE,
                        out _,
                        IntPtr.Zero);
                }

                if (!success)
                {
                    if (!throwOnError)
                    {
                        return null;
                    }

                    int error = Marshal.GetLastWin32Error();
                    // The file or directory is not a reparse point.
                    if (error == Interop.Errors.ERROR_NOT_A_REPARSE_POINT)
                    {
                        return null;
                    }

                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }

                Span<byte> bufferSpan = new(buffer);
                success = MemoryMarshal.TryRead(bufferSpan, out Interop.Kernel32.SymbolicLinkReparseBuffer rbSymlink);
                Debug.Assert(success);

                // We always use SubstituteName(Offset|Length) instead of PrintName(Offset|Length),
                // the latter is just the display name of the reparse point and it can show something completely unrelated to the target.

                if (rbSymlink.ReparseTag == Interop.Kernel32.IOReparseOptions.IO_REPARSE_TAG_SYMLINK)
                {
                    int offset = sizeof(Interop.Kernel32.SymbolicLinkReparseBuffer) + rbSymlink.SubstituteNameOffset;
                    int length = rbSymlink.SubstituteNameLength;

                    Span<char> targetPath = MemoryMarshal.Cast<byte, char>(bufferSpan.Slice(offset, length));

                    bool isRelative = (rbSymlink.Flags & Interop.Kernel32.SYMLINK_FLAG_RELATIVE) != 0;
                    if (!isRelative)
                    {
                        // Absolute target is in NT format and we need to clean it up before return it to the user.
                        if (targetPath.StartsWith(PathInternal.UncNTPathPrefix.AsSpan()))
                        {
                            // We need to prepend the Win32 equivalent of UNC NT prefix.
                            return OPath.Combine(PathInternal.UncPathPrefix, targetPath.Slice(PathInternal.UncNTPathPrefix.Length).ToString());
                        }

                        return GetTargetPathWithoutNTPrefix(targetPath);
                    }
                    else if (returnFullPath)
                    {
                        return OPath.Combine(OPath.GetDirectoryName(linkPath)!, targetPath.ToString());
                    }
                    else
                    {
                        return targetPath.ToString();
                    }
                }
                else if (rbSymlink.ReparseTag == Interop.Kernel32.IOReparseOptions.IO_REPARSE_TAG_MOUNT_POINT)
                {
                    success = MemoryMarshal.TryRead(bufferSpan, out Interop.Kernel32.MountPointReparseBuffer rbMountPoint);
                    Debug.Assert(success);

                    int offset = sizeof(Interop.Kernel32.MountPointReparseBuffer) + rbMountPoint.SubstituteNameOffset;
                    int length = rbMountPoint.SubstituteNameLength;

                    Span<char> targetPath = MemoryMarshal.Cast<byte, char>(bufferSpan.Slice(offset, length));

                    // Unlike symbolic links, mount point paths cannot be relative.
                    Debug.Assert(!PathInternal.IsPartiallyQualified(targetPath));
                    // Mount points cannot point to a remote location.
                    Debug.Assert(!targetPath.StartsWith(PathInternal.UncNTPathPrefix.AsSpan()));
                    return GetTargetPathWithoutNTPrefix(targetPath);
                }

                return null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            static string GetTargetPathWithoutNTPrefix(ReadOnlySpan<char> targetPath)
            {
                Debug.Assert(targetPath.StartsWith(PathInternal.NTPathPrefix.AsSpan()));
                return targetPath.Slice(PathInternal.NTPathPrefix.Length).ToString();
            }
        }

        private static unsafe string? GetFinalLinkTarget(string linkPath, bool isDirectory)
        {
            Interop.Kernel32.WIN32_FIND_DATA data = default;
            GetFindData(linkPath, isDirectory, ignoreAccessDenied: false, ref data);

            // The file or directory is not a reparse point.
            if ((data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) == 0 ||
                // Only symbolic links and mount points are supported at the moment.
                (data.dwReserved0 != Interop.Kernel32.IOReparseOptions.IO_REPARSE_TAG_SYMLINK &&
                 data.dwReserved0 != Interop.Kernel32.IOReparseOptions.IO_REPARSE_TAG_MOUNT_POINT))
            {
                return null;
            }

            // We try to open the final file since they asked for the final target.
            using SafeFileHandle handle = OpenSafeFileHandle(linkPath,
                    Interop.Kernel32.FileOperations.OPEN_EXISTING |
                    Interop.Kernel32.FileOperations.FILE_FLAG_BACKUP_SEMANTICS);

            if (handle.IsInvalid)
            {
                // If the handle fails because it is unreachable, is because the link was broken.
                // We need to fallback to manually traverse the links and return the target of the last resolved link.
                int error = Marshal.GetLastWin32Error();
                if (IsPathUnreachableError(error))
                {
                    return GetFinalLinkTargetSlow(linkPath);
                }
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            const int InitialBufferSize = 4096;
            char[] buffer = ArrayPool<char>.Shared.Rent(InitialBufferSize);
            try
            {
                uint result = GetFinalPathNameByHandle(handle, buffer);

                // If the function fails because lpszFilePath is too small to hold the string plus the terminating null character,
                // the return value is the required buffer size, in TCHARs. This value includes the size of the terminating null character.
                if (result > buffer.Length)
                {
                    char[] toReturn = buffer;
                    buffer = ArrayPool<char>.Shared.Rent((int)result);
                    ArrayPool<char>.Shared.Return(toReturn);

                    result = GetFinalPathNameByHandle(handle, buffer);
                }

                // If the function fails for any other reason, the return value is zero.
                if (result == 0)
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }

                Debug.Assert(PathInternal.IsExtended(new string(buffer, 0, (int)result).AsSpan()));
                // GetFinalPathNameByHandle always returns with extended DOS prefix even if the link target was created without one.
                // While this does not interfere with correct behavior, it might be unexpected.
                // Hence we trim it if the passed-in path to the link wasn't extended.
                int start = PathInternal.IsExtended(linkPath.AsSpan()) ? 0 : 4;
                return new string(buffer, start, (int)result - start);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            uint GetFinalPathNameByHandle(SafeFileHandle handle, char[] buffer)
            {
                fixed (char* bufPtr = buffer)
                {
                    return Interop.Kernel32.GetFinalPathNameByHandle(handle, bufPtr, (uint)buffer.Length, Interop.Kernel32.FILE_NAME_NORMALIZED);
                }
            }

            string? GetFinalLinkTargetSlow(string linkPath)
            {
                // Since all these paths will be passed to CreateFile, which takes a string anyway, it is pointless to use span.
                // I am not sure if it's possible to change CreateFile's param to ROS<char> and avoid all these allocations.

                // We don't throw on error since we already did all the proper validations before.
                string? current = GetImmediateLinkTarget(linkPath, throwOnError: false, returnFullPath: true);
                string? prev = null;

                while (current != null)
                {
                    prev = current;
                    current = GetImmediateLinkTarget(current, throwOnError: false, returnFullPath: true);
                }

                return prev;
            }
        }

        private static unsafe SafeFileHandle OpenSafeFileHandle(string path, int flags)
        {
            SafeFileHandle handle = Interop.Kernel32.CreateFile(
                path,
                dwDesiredAccess: 0,
                FileShare.ReadWrite | FileShare.Delete,
                lpSecurityAttributes: (Interop.Kernel32.SECURITY_ATTRIBUTES*)IntPtr.Zero,
                FileMode.Open,
                dwFlagsAndAttributes: flags,
                hTemplateFile: IntPtr.Zero);

            return handle;
        }
    }
}