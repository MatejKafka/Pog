using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Pog.Native;
using Polyfills;
using PIO = Polyfills.System.IO;

namespace Pog;

public static class FsUtils {
    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string dirPath, string searchPattern = "*") {
        return new DirectoryInfo(dirPath).EnumerateDirectories(searchPattern)
                // ignore both Windows hidden files and Unix-style dot-files
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden) && d.Name[0] != '.')
                .Select(d => d.Name);
    }

    public static IEnumerable<string> EnumerateNonHiddenFileNames(string dirPath, string searchPattern = "*") {
        return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => f.Name);
    }

    // this block of forwarding methods is here to allow simple usage from PowerShell
    //  without having to load Polyfills.dll manually before usage

    public static string GetRelativePath(string from, string to) {
        return PIO.Path.GetRelativePath(from, to);
    }

    public static string? GetSymbolicLinkTarget(string linkPath) {
        return PogExports.GetSymbolicLinkTarget(linkPath);
    }

    public static FileSystemInfo CreateSymbolicLink(string path, string targetPath, bool isDirectory) {
        if (isDirectory) return PIO.Directory.CreateSymbolicLink(path, targetPath);
        else return PIO.File.CreateSymbolicLink(path, targetPath);
    }

    private static readonly Regex InvalidDosNameRegex = new(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..+)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? SanitizeFileName(string? fileName) {
        // sources for relevant functions in Chromium:
        // GetFileNameFromURL: https://github.com/chromium/chromium/blob/11a147047d7ed9d429b53e536fe1ead54fad5053/net/base/filename_util_internal.cc#L119
        // GenerateSafeFileName: https://github.com/chromium/chromium/blob/bf9e98c98e8d7e79befeb057fde42b0e320d9b19/net/base/filename_util.cc#L163
        // SanitizeGeneratedFileName: https://github.com/chromium/chromium/blob/11a147047d7ed9d429b53e536fe1ead54fad5053/net/base/filename_util_internal.cc#L79

        // list of invalid filenames on Windows: https://stackoverflow.com/a/62888

        if (fileName == null) {
            return null;
        }

        // Win32 does not like trailing '.' and ' ', remove it
        fileName = fileName.TrimEnd('.', ' ');
        // replace any invalid characters with _
        fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

        if (InvalidDosNameRegex.IsMatch(fileName)) {
            // is a DOS file name, prefix with _ to lose the special meaning
            fileName = "_" + fileName;
        }

        // if fileName is empty or only consists of invalid chars (or _), it is not valid
        if (fileName.All(c => c == '_')) {
            return null;
        }
        return fileName;
    }

    public static bool FileContentEqual(byte[] f1, FileInfo f2) {
        return f1.Length == f2.Length && f1.SequenceEqual(File.ReadAllBytes(f2.FullName));
    }

    public static bool FileContentEqual(ZipArchiveEntry f1, FileInfo f2) {
        if (f1.Length != f2.Length) return false;

        var f1Content = new byte[f1.Length];
        using var f1Stream = f1.Open();
        var bytesRead = f1Stream.Read(f1Content, 0, (int) f1.Length);
        Debug.Assert(bytesRead == f1.Length);
        return f1Content.SequenceEqual(File.ReadAllBytes(f2.FullName));
    }

    public static bool FileContentEqual(FileInfo f1, FileInfo f2) {
        // significantly faster than trying to do a streaming implementation
        return f1.Length == f2.Length && File.ReadAllBytes(f1.FullName).SequenceEqual(File.ReadAllBytes(f2.FullName));
    }

    public static bool DirectoryTreeEqual(string d1Path, string d2Path) {
        return DirectoryTreeEqual(new DirectoryInfo(d1Path), new DirectoryInfo(d2Path));
    }

    public static bool DirectoryTreeEqual(DirectoryInfo d1, DirectoryInfo d2) {
        if (d1.Exists != d2.Exists) return false;
        if (!d1.Exists && !d2.Exists) return true;

        var d1Entries = d1.GetFileSystemInfos();
        var d2Entries = d2.GetFileSystemInfos();
        if (d1Entries.Length != d2Entries.Length) {
            return false;
        }

        int Comparator(FileSystemInfo e1, FileSystemInfo e2) => string.Compare(e1.Name, e2.Name, StringComparison.Ordinal);
        Array.Sort(d1Entries, Comparator);
        Array.Sort(d2Entries, Comparator);

        for (var i = 0; i < d1Entries.Length; i++) {
            var (e1, e2) = (d1Entries[i], d2Entries[i]);
            if (e1.Name != e2.Name) return false;
            var equal = (e1, e2) switch {
                (FileInfo f1, FileInfo f2) => FileContentEqual(f1, f2),
                (DirectoryInfo cd1, DirectoryInfo cd2) => DirectoryTreeEqual(cd1, cd2),
                _ => false,
            };
            if (!equal) return false;
        }

        return true;
    }

    /// Return `childName`, but with casing matching the name as stored in the filesystem, if it already exists.
    public static string GetResolvedChildName(string parent, string childName) {
        Verify.Assert.FileName(childName);
        try {
            return new DirectoryInfo(parent).EnumerateFileSystemInfos(childName).Single().Name;
        } catch (InvalidOperationException) {
            // the child does not exist yet, return the name as-is
            return childName;
        }
    }

    private static bool RemoveReadOnlyAttributesInDirectory(string dirPath) {
        var removedAny = false;
        foreach (var filePath in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories)) {
            var attr = File.GetAttributes(filePath);
            if ((attr & FileAttributes.ReadOnly) != 0) {
                File.SetAttributes(filePath, attr & ~FileAttributes.ReadOnly);
                removedAny = true;
            }
        }
        return removedAny;
    }

    /// Recursively deletes a directory, even if it contains files with the read-only attribute.
    public static void ForceDeleteDirectory(string dirPath) {
        while (true) {
            try {
                Directory.Delete(dirPath, true);
                return;
            } catch (UnauthorizedAccessException) {
                if (!RemoveReadOnlyAttributesInDirectory(dirPath)) {
                    // did not find anything with a read-only attribute, there's probably another reason for the exception
                    throw;
                }
                // retry
            }
        }
    }

    /// Delete the directory at `dirPath`, if it exists.
    public static bool EnsureDeleteDirectory(string dirPath) {
        try {
            ForceDeleteDirectory(dirPath);
            return true;
        } catch (DirectoryNotFoundException) {
            return false;
        }
    }

    /// Delete the file at <paramref name="filePath"/>, if it exists.
    public static bool EnsureDeleteFile(string filePath) {
        try {
            File.Delete(filePath);
            return true;
        } catch (FileNotFoundException) {
            return false;
        } catch (DirectoryNotFoundException) {
            return false; // thrown when parent dir does not exist
        }
    }

    /// Assumes that <paramref name="targetDir"/> exists.
    public static void MoveDirectoryContents(DirectoryInfo srcDir, string targetDir) {
        foreach (var entry in srcDir.EnumerateFileSystemInfos()) {
            var targetPath = Path.Combine(targetDir, entry.Name);
            // shrug, not all .NET APIs are nice...
            if (entry is FileInfo file) {
                file.MoveTo(targetPath);
            } else if (entry is DirectoryInfo dir) {
                dir.MoveTo(targetPath);
            }
        }
    }

    /// Assumes that <paramref name="targetPath"/> does NOT exist.
    public static void CopyDirectory(DirectoryInfo srcDir, string targetPath) {
        Directory.CreateDirectory(targetPath);
        foreach (var entry in srcDir.EnumerateFileSystemInfos()) {
            var entryTargetPath = Path.Combine(targetPath, entry.Name);
            if (entry is FileInfo file)
                file.CopyTo(entryTargetPath);
            else if (entry is DirectoryInfo dir) {
                CopyDirectory(dir, entryTargetPath);
            }
        }
    }

    private static SafeFileHandle CreateFile(string filename, uint desiredAccess, FileShare sharedMode,
            FileMode creationDisposition, Win32.FILE_FLAG flagsAndAttributes) {
        var handle = Win32.CreateFile(filename, desiredAccess, sharedMode, IntPtr.Zero, creationDisposition,
                flagsAndAttributes, IntPtr.Zero);
        if (handle.IsInvalid) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return handle;
    }

    public static void MoveByHandle(SafeFileHandle handle, string destinationPath, bool replaceExistingFile = false) {
        if (!Win32.SetFileInformationByHandle_FileRenameInfo(handle, destinationPath, replaceExistingFile)) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
    }

    /// <summary>Open a filesystem object (file/directory) for later moving it using `MoveByHandle`.</summary>
    ///
    /// This method, together with <see cref="MoveByHandle"/>, is useful to distinguish between 2 possible causes
    /// for an Access Denied error, which are conflated together with `MoveFile`:
    /// 1) We don't have sufficient permissions to move the directory (in which case this method throws
    ///    an Access Denied exception).
    /// 2) There's a locked entry in the directory and we cannot move it (in which case the same exception
    ///    is thrown from <see cref="MoveByHandle"/>).
    public static SafeFileHandle OpenForMove(string directoryPath) {
        // ReSharper disable once InconsistentNaming
        const uint ACCESS_DELETE = 0x00010000;
        // using `FileShare.Read`, because e.g. Explorer likes to hold read handles to directories
        return CreateFile(directoryPath, ACCESS_DELETE, FileShare.Read,
                FileMode.Open, Win32.FILE_FLAG.BACKUP_SEMANTICS);
    }

    public static SafeFileHandle OpenDirectoryReadOnly(string directoryPath) {
        return CreateFile(directoryPath, (uint) FileAccess.Read, FileShare.Read,
                FileMode.Open, Win32.FILE_FLAG.BACKUP_SEMANTICS);
    }

    /// <remarks>
    /// Why not just use MoveFileEx? Because internally, if the move fails, it attempts to copy.
    /// See https://youtu.be/uhRWMGBjlO8?t=2162
    /// </remarks>
    public static void MoveAtomically(string srcDirPath, string targetPath) {
        using var handle = OpenForMove(srcDirPath);
        MoveByHandle(handle, targetPath);
    }

    /// It is not possible to atomically delete a directory. Instead, we use a temporary directory
    /// to first move it out of the way, and then delete it. Note that `tmpMovePath` must
    /// be on the same filesystem as `srcDirPath`.
    public static void DeleteDirectoryAtomically(string srcDirPath, string tmpMovePath) {
        MoveAtomically(srcDirPath, tmpMovePath);
        ForceDeleteDirectory(tmpMovePath);
    }

    /// <inheritdoc cref="DeleteDirectoryAtomically"/>
    public static bool EnsureDeleteDirectoryAtomically(string srcDirPath, string tmpMovePath) {
        try {
            MoveAtomically(srcDirPath, tmpMovePath);
        } catch (FileNotFoundException) {
            return false;
        } catch (DirectoryNotFoundException) {
            return false;
        }
        ForceDeleteDirectory(tmpMovePath);
        return true;
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
            dirHandle = OpenForMove(srcPath);
        } catch (FileLoadException) {
            // the directory at dirPath is locked by another process
            return false;
        }

        using (dirHandle) {
            try {
                MoveByHandle(dirHandle, destinationPath);
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

    /// <remarks>Assumes that both paths are resolved and cleaned.</remarks>
    public static bool EscapesDirectory(string basePath, string validatedPath) {
        var isSafe = validatedPath.StartsWith(basePath, StringComparison.InvariantCultureIgnoreCase) &&
                     (validatedPath.Length == basePath.Length || validatedPath[basePath.Length] == '\\');
        return !isSafe;
    }

    /// If <paramref name="childPath"/> stays inside <paramref name="basePath"/>, return the combined path, otherwise return null.
    /// Assumes that <paramref name="basePath"/> is resolved in canonical form.
    /// Use this function when resolving an untrusted relative path.
    public static string? JoinValidateSubPath(string basePath, string childPath) {
        var combined = Path.GetFullPath(basePath + '\\' + childPath.TrimEnd('/', '\\'));
        if (EscapesDirectory(basePath, combined)) {
            return null;
        }
        return combined;
    }
}
