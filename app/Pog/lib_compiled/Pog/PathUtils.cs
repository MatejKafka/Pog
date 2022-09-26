using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pog;

internal static class PathUtils {
    public static IEnumerable<string> EnumerateNonHiddenDirectoryNames(string path, string searchPattern = "*") {
        return new DirectoryInfo(path).EnumerateDirectories(searchPattern)
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(d => d.Name);
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
}