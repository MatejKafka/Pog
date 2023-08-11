// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Polyfills.System.IO
{
    public static partial class Path
    {
        // private static bool ExistsCore(string fullPath, out bool isDirectory)
        // {
        //     Interop.Kernel32.WIN32_FILE_ATTRIBUTE_DATA data = default;
        //     int errorCode = FileSystem.FillAttributeInfo(fullPath, ref data, returnErrorOnNotFound: true);
        //     bool result = (errorCode == Interop.Errors.ERROR_SUCCESS) && (data.dwFileAttributes != -1);
        //     isDirectory = result && (data.dwFileAttributes & Interop.Kernel32.FileAttributes.FILE_ATTRIBUTE_DIRECTORY) != 0;
        //
        //     return result;
        // }
    }
}