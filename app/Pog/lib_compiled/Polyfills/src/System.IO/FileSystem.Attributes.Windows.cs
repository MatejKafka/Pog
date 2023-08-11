// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Polyfills.System.IO
{
    internal static partial class FileSystem
    {
        internal static bool IsPathUnreachableError(int errorCode)
        {
            switch (errorCode)
            {
                case Interop.Errors.ERROR_FILE_NOT_FOUND:
                case Interop.Errors.ERROR_PATH_NOT_FOUND:
                case Interop.Errors.ERROR_NOT_READY:
                case Interop.Errors.ERROR_INVALID_NAME:
                case Interop.Errors.ERROR_BAD_PATHNAME:
                case Interop.Errors.ERROR_BAD_NETPATH:
                case Interop.Errors.ERROR_BAD_NET_NAME:
                case Interop.Errors.ERROR_INVALID_PARAMETER:
                case Interop.Errors.ERROR_NETWORK_UNREACHABLE:
                case Interop.Errors.ERROR_NETWORK_ACCESS_DENIED:
                case Interop.Errors.ERROR_INVALID_HANDLE:           // eg from \\.\CON
                case Interop.Errors.ERROR_FILENAME_EXCED_RANGE:     // Path is too long
                    return true;
                default:
                    return false;
            }
        }
    }
}