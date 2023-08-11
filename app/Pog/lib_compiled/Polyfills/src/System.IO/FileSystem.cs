// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Polyfills.System.IO
{
    internal static partial class FileSystem
    {
        internal static void VerifyValidPath(string path, string argName)
        {
            if (string.IsNullOrEmpty(path)) {
                throw new ArgumentException("Path is null or empty.", argName);
            }
            if (path.Contains('\0')) {
                throw new ArgumentException("Path contains invalid characters.", argName);
            }
        }
    }
}