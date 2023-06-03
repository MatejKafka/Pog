using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pog;

public static class PathUtils {
    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string dirPath, string searchPattern = "*") {
        return new DirectoryInfo(dirPath).EnumerateDirectories(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.Name);
    }

    public static IEnumerable<FileInfo> EnumerateNonHiddenFiles(string dirPath, string searchPattern = "*") {
        return new DirectoryInfo(dirPath).EnumerateFiles(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden));
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
    public static void EnsureDeleteDirectory(string dirPath) {
        try {
            Directory.Delete(dirPath, true);
        } catch (DirectoryNotFoundException) {}
    }
}
