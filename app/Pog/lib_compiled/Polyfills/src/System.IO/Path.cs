// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Polyfills.System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Polyfills.System.Text;
using OPath = System.IO.Path;

namespace Polyfills.System.IO
{
    // Provides methods for processing file system strings in a cross-platform manner.
    // Most of the methods don't do a complete parsing (such as examining a UNC hostname),
    // but they will handle most string operations.
    [PublicAPI]
    public static partial class Path
    {
        // Public static readonly variant of the separators. The Path implementation itself is using
        // internal const variant of the separators for better performance.
        public static readonly char DirectorySeparatorChar = PathInternal.DirectorySeparatorChar;
        public static readonly char AltDirectorySeparatorChar = PathInternal.AltDirectorySeparatorChar;
        public static readonly char VolumeSeparatorChar = PathInternal.VolumeSeparatorChar;
        public static readonly char PathSeparator = PathInternal.PathSeparator;

        // For generating random file names
        // 8 random bytes provides 12 chars in our encoding for the 8.3 name.
        private const int KeyLength = 8;

        [Obsolete("Path.InvalidPathChars has been deprecated. Use GetInvalidPathChars or GetInvalidFileNameChars instead.")]
        public static readonly char[] InvalidPathChars = OPath.GetInvalidPathChars();

        // Changes the extension of a file path. The path parameter
        // specifies a file path, and the extension parameter
        // specifies a file extension (with a leading period, such as
        // ".exe" or ".cs").
        //
        // The function returns a file path with the same root, directory, and base
        // name parts as path, but with the file extension changed to
        // the specified extension. If path is null, the function
        // returns null. If path does not contain a file extension,
        // the new file extension is appended to the path. If extension
        // is null, any existing extension is removed from path.
        [return: NotNullIfNotNull(nameof(path))]
        public static string? ChangeExtension(string? path, string? extension)
        {
            if (path == null)
                return null;

            int subLength = path.Length;
            if (subLength == 0)
                return string.Empty;

            for (int i = path.Length - 1; i >= 0; i--)
            {
                char ch = path[i];

                if (ch == '.')
                {
                    subLength = i;
                    break;
                }

                if (PathInternal.IsDirectorySeparator(ch))
                {
                    break;
                }
            }

            if (extension == null)
            {
                return path.Substring(0, subLength);
            }

            var subpath = path.Substring(0, subLength);
            return extension.StartsWith(".") ?
                string.Concat(subpath, extension) :
                string.Concat(subpath, ".", extension);
        }

        // /// <summary>
        // /// Determines whether the specified file or directory exists.
        // /// </summary>
        // /// <remarks>
        // /// Unlike <see cref="File.Exists(string?)"/> it returns true for existing, non-regular files like pipes.
        // /// If the path targets an existing link, but the target of the link does not exist, it returns true.
        // /// </remarks>
        // /// <param name="path">The path to check</param>
        // /// <returns>
        // /// <see langword="true" /> if the caller has the required permissions and <paramref name="path" /> contains
        // /// the name of an existing file or directory; otherwise, <see langword="false" />.
        // /// This method also returns <see langword="false" /> if <paramref name="path" /> is <see langword="null" />,
        // /// an invalid path, or a zero-length string. If the caller does not have sufficient permissions to read the specified path,
        // /// no exception is thrown and the method returns <see langword="false" /> regardless of the existence of <paramref name="path" />.
        // /// </returns>
        // public static bool Exists([NotNullWhen(true)] string? path)
        // {
        //     if (string.IsNullOrEmpty(path))
        //     {
        //         return false;
        //     }
        //
        //     string? fullPath;
        //     try
        //     {
        //         fullPath = OPath.GetFullPath(path);
        //     }
        //     catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        //     {
        //         return false;
        //     }
        //
        //     bool result = ExistsCore(fullPath, out bool isDirectory);
        //     if (result && PathInternal.IsDirectorySeparator(fullPath[fullPath.Length - 1]))
        //     {
        //         // Some sys-calls remove all trailing slashes and may give false positives for existing files.
        //         // We want to make sure that if the path ends in a trailing slash, it's truly a directory.
        //         return isDirectory;
        //     }
        //
        //     return result;
        // }

        /// <summary>
        /// Returns true if the path is fixed to a specific drive or UNC path. This method does no
        /// validation of the path (URIs will be returned as relative as a result).
        /// Returns false if the path specified is relative to the current drive or working directory.
        /// </summary>
        /// <remarks>
        /// Handles paths that use the alternate directory separator.  It is a frequent mistake to
        /// assume that rooted paths <see cref="Path.IsPathRooted(string)"/> are not relative.  This isn't the case.
        /// "C:a" is drive relative- meaning that it will be resolved against the current directory
        /// for C: (rooted, but relative). "C:\a" is rooted and not relative (the current directory
        /// will not be used to modify the path).
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="path"/> is null.
        /// </exception>
        public static bool IsPathFullyQualified(string path)
        {
            return IsPathFullyQualified(path.AsSpan());
        }

        public static bool IsPathFullyQualified(ReadOnlySpan<char> path)
        {
            return !PathInternal.IsPartiallyQualified(path);
        }

        /// <summary>
        /// Create a relative path from one path to another. Paths will be resolved before calculating the difference.
        /// Default path comparison for the active platform will be used (OrdinalIgnoreCase for Windows or Mac, Ordinal for Unix).
        /// </summary>
        /// <param name="relativeTo">The source path the output should be relative to. This path is always considered to be a directory.</param>
        /// <param name="path">The destination path.</param>
        /// <returns>The relative path or <paramref name="path"/> if the paths don't share the same root.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="relativeTo"/> or <paramref name="path"/> is <c>null</c> or an empty string.</exception>
        public static string GetRelativePath(string relativeTo, string path)
        {
            return GetRelativePath(relativeTo, path, PathInternal.StringComparison);
        }

        private static string GetRelativePath(string relativeTo, string path, StringComparison comparisonType)
        {
            if (PathInternal.IsEffectivelyEmpty(relativeTo.AsSpan()))
                throw new ArgumentException("The path is empty.", nameof(relativeTo));
            if (PathInternal.IsEffectivelyEmpty(path.AsSpan()))
                throw new ArgumentException("The path is empty.", nameof(path));

            Debug.Assert(comparisonType == StringComparison.Ordinal || comparisonType == StringComparison.OrdinalIgnoreCase);

            relativeTo = OPath.GetFullPath(relativeTo);
            path = OPath.GetFullPath(path);

            // Need to check if the roots are different- if they are we need to return the "to" path.
            if (!PathInternal.AreRootsEqual(relativeTo, path, comparisonType))
                return path;

            int commonLength = PathInternal.GetCommonPathLength(relativeTo, path, ignoreCase: comparisonType == StringComparison.OrdinalIgnoreCase);

            // If there is nothing in common they can't share the same root, return the "to" path as is.
            if (commonLength == 0)
                return path;

            // Trailing separators aren't significant for comparison
            int relativeToLength = relativeTo.Length;
            if (EndsInDirectorySeparator(relativeTo.AsSpan()))
                relativeToLength--;

            bool pathEndsInSeparator = EndsInDirectorySeparator(path.AsSpan());
            int pathLength = path.Length;
            if (pathEndsInSeparator)
                pathLength--;

            // If we have effectively the same path, return "."
            if (relativeToLength == pathLength && commonLength >= relativeToLength) return ".";

            // We have the same root, we need to calculate the difference now using the
            // common Length and Segment count past the length.
            //
            // Some examples:
            //
            //  C:\Foo C:\Bar L3, S1 -> ..\Bar
            //  C:\Foo C:\Foo\Bar L6, S0 -> Bar
            //  C:\Foo\Bar C:\Bar\Bar L3, S2 -> ..\..\Bar\Bar
            //  C:\Foo\Foo C:\Foo\Bar L7, S1 -> ..\Bar

            var sb = new ValueStringBuilder(stackalloc char[260]);
            sb.EnsureCapacity(Math.Max(relativeTo.Length, path.Length));

            // Add parent segments for segments past the common on the "from" path
            if (commonLength < relativeToLength)
            {
                sb.Append("..");

                for (int i = commonLength + 1; i < relativeToLength; i++)
                {
                    if (PathInternal.IsDirectorySeparator(relativeTo[i]))
                    {
                        sb.Append(DirectorySeparatorChar);
                        sb.Append("..");
                    }
                }
            }
            else if (PathInternal.IsDirectorySeparator(path[commonLength]))
            {
                // No parent segments and we need to eat the initial separator
                //  (C:\Foo C:\Foo\Bar case)
                commonLength++;
            }

            // Now add the rest of the "to" path, adding back the trailing separator
            int differenceLength = pathLength - commonLength;
            if (pathEndsInSeparator)
                differenceLength++;

            if (differenceLength > 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(DirectorySeparatorChar);
                }

                sb.Append(path.AsSpan(commonLength, differenceLength));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Trims one trailing directory separator beyond the root of the path.
        /// </summary>
        public static string TrimEndingDirectorySeparator(string path) => PathInternal.TrimEndingDirectorySeparator(path);

        /// <summary>
        /// Trims one trailing directory separator beyond the root of the path.
        /// </summary>
        public static ReadOnlySpan<char> TrimEndingDirectorySeparator(ReadOnlySpan<char> path) => PathInternal.TrimEndingDirectorySeparator(path);

        /// <summary>
        /// Returns true if the path ends in a directory separator.
        /// </summary>
        public static bool EndsInDirectorySeparator(ReadOnlySpan<char> path) => PathInternal.EndsInDirectorySeparator(path);

        /// <summary>
        /// Returns true if the path ends in a directory separator.
        /// </summary>
        public static bool EndsInDirectorySeparator(string path) => PathInternal.EndsInDirectorySeparator(path);
    }
}