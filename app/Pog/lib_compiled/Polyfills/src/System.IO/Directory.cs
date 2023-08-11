// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using OPath = System.IO.Path;

namespace Polyfills.System.IO
{
    public static class Directory
    {
        /// <summary>
        /// Creates a directory symbolic link identified by <paramref name="path"/> that points to <paramref name="pathToTarget"/>.
        /// </summary>
        /// <param name="path">The absolute path where the symbolic link should be created.</param>
        /// <param name="pathToTarget">The target directory of the symbolic link.</param>
        /// <returns>A <see cref="DirectoryInfo"/> instance that wraps the newly created directory symbolic link.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="pathToTarget"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="pathToTarget"/> is empty.
        /// -or-
        /// <paramref name="path"/> is not an absolute path.
        /// -or-
        /// <paramref name="path"/> or <paramref name="pathToTarget"/> contains invalid path characters.</exception>
        /// <exception cref="IOException">A file or directory already exists in the location of <paramref name="path"/>.
        /// -or-
        /// An I/O error occurred.</exception>
        public static FileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
        {
            FileSystem.VerifyValidPath(pathToTarget, nameof(pathToTarget));

            FileSystem.CreateSymbolicLink(path, pathToTarget, isDirectory: true);
            return new DirectoryInfo(path);
        }

        /// <summary>
        /// Gets the target of the specified directory link.
        /// </summary>
        /// <param name="linkPath">The path of the directory link.</param>
        /// <param name="returnFinalTarget"><see langword="true"/> to follow links to the final target; <see langword="false"/> to return the immediate next link.</param>
        /// <returns>A <see cref="DirectoryInfo"/> instance if <paramref name="linkPath"/> exists, independently if the target exists or not. <see langword="null"/> if <paramref name="linkPath"/> is not a link.</returns>
        /// <exception cref="IOException">The directory on <paramref name="linkPath"/> does not exist.
        /// -or-
        /// The link's file system entry type is inconsistent with that of its target.
        /// -or-
        /// Too many levels of symbolic links.</exception>
        /// <remarks>When <paramref name="returnFinalTarget"/> is <see langword="true"/>, the maximum number of symbolic links that are followed are 40 on Unix and 63 on Windows.</remarks>
        public static FileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget)
        {
            FileSystem.VerifyValidPath(linkPath, nameof(linkPath));
            return FileSystem.ResolveLinkTarget(linkPath, returnFinalTarget, isDirectory: true);
        }
    }
}