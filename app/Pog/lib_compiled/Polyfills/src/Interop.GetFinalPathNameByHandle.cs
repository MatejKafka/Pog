// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Polyfills;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        internal const uint FILE_NAME_NORMALIZED = 0x0;

        // https://docs.microsoft.com/windows/desktop/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew (kernel32)
        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
        internal static extern unsafe uint GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            char* lpszFilePath,
            uint cchFilePath,
            uint dwFlags);
    }
}