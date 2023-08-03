using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pog;

public static class FileUtils {
    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string dirPath, string searchPattern = "*") {
        return new DirectoryInfo(dirPath).EnumerateDirectories(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.Name);
    }

    public static IEnumerable<string> EnumerateNonHiddenFileNames(string dirPath, string searchPattern = "*") {
        return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => f.Name);
    }

    public static bool FileContentEqual(FileInfo f1, FileInfo f2) {
        // significantly faster than trying to do a streaming implementation
        return f1.Length == f2.Length && File.ReadAllBytes(f1.FullName).SequenceEqual(File.ReadAllBytes(f2.FullName));
    }

    /// Return `childName`, but with casing matching the name as stored in the filesystem, if it already exists.
    public static string GetResolvedChildName(string parent, string childName) {
        Verify.Assert.FileName(childName);
        try {
            return new DirectoryInfo(parent).EnumerateDirectories(childName).Single().Name;
        } catch (InvalidOperationException) {
            // the child does not exist yet, return the name as-is
            return childName;
        }
    }

    /// Delete the directory at `dirPath`, if it exists.
    public static bool EnsureDeleteDirectory(string dirPath) {
        try {
            Directory.Delete(dirPath, true);
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
        }
    }

    /// Assumes that <paramref name="targetDir"/> exists.
    public static void MoveDirectoryContents(DirectoryInfo srcDir, string targetDir) {
        foreach (var entry in srcDir.EnumerateFileSystemInfos()) {
            // shrug, not all .NET APIs are nice...
            if (entry is FileInfo file)
                file.MoveTo(Path.Combine(targetDir, entry.Name));
            else if (entry is DirectoryInfo dir) {
                dir.MoveTo(Path.Combine(targetDir, entry.Name));
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
}
