using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;

namespace Pog;

public static class FileLockUtils {
    // ReSharper disable once MemberCanBePrivate.Global
    public static IEnumerable<string> EnumerateLockedFiles(string directory) {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .AsParallel()
                .AsUnordered()
                .Where(f => {
                    try {
                        // this is not much slower than calling raw `CreateFile` (~4% in my testing)
                        File.Open(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Close();
                        return false;
                    } catch (SystemException e) {
                        switch (e.HResult) {
                            case -2147024864: // 0x80070020 = ERROR_SHARING_VIOLATION
                                return true;
                            case -2147024891: // 0x80070005 = ERROR_ACCESS_DENIED
                                return false; // ignore
                            case -2147022976: // 0x80070780 = ERROR_CANT_ACCESS_FILE
                                return false; // sometimes happens for special files, ignore
                            default:
                                throw;
                        }
                    }
                });
    }

    public static string[] GetLockedFiles(string directory) {
        return EnumerateLockedFiles(directory).ToArray();
    }

    [PublicAPI]
    public static bool ContainsLockedFiles(string directory) {
        return EnumerateLockedFiles(directory).Any();
    }

    /**
     * Attempts to atomically move the directory at `srcPath` to `destinationPath`. Returns `true on success,
     * `false` if the directory is locked, throws an exception for other error cases.
     *
     * <exception cref="SystemException"></exception>
     */
    [PublicAPI]
    public static bool MoveDirectoryUnlocked(string srcPath, string destinationPath) {
        using var handle = Win32.OpenDirectoryForMove(srcPath);
        try {
            Win32.MoveFileByHandle(handle, destinationPath);
            return true; // move succeeded, no locks
        } catch (SystemException e) {
            // 0x80070005 = ERROR_ACCESS_DENIED
            if (e.HResult == -2147024891) {
                return false; // something in the directory is locked
            }
            throw;
        }
    }

    [PublicAPI]
    public static bool IsDirectoryLocked(string directoryPath) {
        // move directory to itself; this returns false when the directory contains anything locked, and is a no-op otherwise
        return !MoveDirectoryUnlocked(directoryPath, directoryPath);
    }
}
