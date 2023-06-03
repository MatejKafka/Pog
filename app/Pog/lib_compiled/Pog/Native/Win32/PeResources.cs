// Generated using CsWin32, with custom modifications

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Pog.Native;

/// <content>
/// Contains extern methods from "KERNEL32.dll".
/// </content>
public static partial class Win32 {
    /// <summary>Frees the loaded dynamic-link library (DLL) module and, if necessary, decrements its reference count.</summary>
    /// <param name="hLibModule">
    /// <para>A handle to the loaded library module. The <a href="https://docs.microsoft.com/windows/desktop/api/libloaderapi/nf-libloaderapi-loadlibrarya">LoadLibrary</a>, <a href="https://docs.microsoft.com/windows/desktop/api/libloaderapi/nf-libloaderapi-loadlibraryexa">LoadLibraryEx</a>, <a href="https://docs.microsoft.com/windows/desktop/api/libloaderapi/nf-libloaderapi-getmodulehandlea">GetModuleHandle</a>, or <a href="https://docs.microsoft.com/windows/desktop/api/libloaderapi/nf-libloaderapi-getmodulehandleexa">GetModuleHandleEx</a> function returns this handle.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-freelibrary#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero. To get extended error information, call the <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a> function.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-freelibrary">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern BOOL FreeLibrary(HMODULE hLibModule);

    /// <summary>Loads the specified module into the address space of the calling process.</summary>
    /// <param name="lpLibFileName">
    /// <para>The name of the module. This can be either a library module (a .dll file) or an executable module (an .exe file). The name specified is the file name of the module and is not related to the name stored in the library module itself, as specified by the <b>LIBRARY</b> keyword in the module-definition (.def) file. If the string specifies a full path, the function searches only that path for the module. If the string specifies a relative path or a module name without a path, the function uses a standard search strategy to find the module; for more information, see the Remarks. If the function cannot find the  module, the function fails. When specifying a path, be sure to use backslashes (\\), not forward slashes (/). For more information about paths, see <a href="https://docs.microsoft.com/windows/desktop/FileIO/naming-a-file">Naming a File or Directory</a>. If the string specifies a module name without a path and the file name extension is omitted, the function appends the default library extension .dll to the module name. To prevent the function from appending .dll to the module name, include a trailing point character (.) in the module name string.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-loadlibraryw#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>If the function succeeds, the return value is a handle to the module. If the function fails, the return value is NULL. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-loadlibraryw">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "LoadLibraryW", SetLastError = true,
            CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern FreeLibrarySafeHandle LoadLibrary(string lpLibFileName);

    [PublicAPI]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "IdentifierTypo")]
    [Flags]
    public enum LoadLibraryFlags : uint {
        None = 0,
        DONT_RESOLVE_DLL_REFERENCES = 0x00000001,
        LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x00000010,
        LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
        LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040,
        LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020,
        LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200,
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100,
        LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800,
        LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400,
        LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008,
        LOAD_LIBRARY_REQUIRE_SIGNED_TARGET = 0x00000080,
        LOAD_LIBRARY_SAFE_CURRENT_DIRS = 0x00002000,
    }

    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "LoadLibraryExW", SetLastError = true,
            CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern FreeLibrarySafeHandle LoadLibraryEx(string lpLibFileName, IntPtr hFile, LoadLibraryFlags dwFlags);

    /// <summary>Determines the location of a resource with the specified type and name in the specified module.</summary>
    /// <param name="hModule">
    /// <para>Type: <b>HMODULE</b> A handle to the module whose portable executable file or an accompanying MUI file contains the resource. If this parameter is <b>NULL</b>, the function searches the module used to create the current process.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-findresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpName">
    /// <para>Type: <b>LPCTSTR</b> The name of the resource. Alternately, rather than a pointer, this parameter can be <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-makeintresourcea">MAKEINTRESOURCE</a>(ID), where ID is the integer identifier of the resource. For more information, see the Remarks section below.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-findresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpType">
    /// <para>Type: <b>LPCTSTR</b> The resource type. Alternately, rather than a pointer, this parameter can be <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-makeintresourcew">MAKEINTRESOURCE</a>(ID), where ID is the integer identifier of the given resource type. For standard resource types, see <a href="https://docs.microsoft.com/windows/desktop/menurc/resource-types">Resource Types</a>. For more information, see the Remarks section below.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-findresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>HRSRC</b> If the function succeeds, the return value is a handle to the specified resource's information block. To obtain a handle to the resource, pass this handle to the <a href="/windows/desktop/api/libloaderapi/nf-libloaderapi-loadresource">LoadResource</a> function. If the function fails, the return value is <b>NULL</b>. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-findresourcew">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "FindResourceW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern HRSRC FindResource(FreeLibrarySafeHandle hModule, ResourceAtom lpName, ResourceAtom lpType);

    /// <inheritdoc cref="LoadResource(HMODULE, HRSRC)"/>
    public static HGLOBAL LoadResource(SafeHandle? hModule, HRSRC hResInfo) {
        bool hModuleAddRef = false;
        try {
            HMODULE hModuleLocal;
            if (hModule != null) {
                hModule.DangerousAddRef(ref hModuleAddRef);
                hModuleLocal = (HMODULE) hModule.DangerousGetHandle();
            } else
                hModuleLocal = (HMODULE) new IntPtr(0L);
            return LoadResource(hModuleLocal, hResInfo);
        } finally {
            if (hModuleAddRef)
                hModule!.DangerousRelease();
        }
    }

    /// <summary>Retrieves a handle that can be used to obtain a pointer to the first byte of the specified resource in memory.</summary>
    /// <param name="hModule">
    /// <para>Type: <b>HMODULE</b> A handle to the module whose executable file contains the resource. If <i>hModule</i> is <b>NULL</b>, the system loads the resource from the module that was used to create the current process.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-loadresource#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="hResInfo">
    /// <para>Type: <b>HRSRC</b> A handle to the resource to be loaded. This handle is returned by the <a href="https://docs.microsoft.com/windows/win32/api/winbase/nf-winbase-findresourcea">FindResource</a> or <a href="https://docs.microsoft.com/windows/win32/api/winbase/nf-winbase-findresourceexa">FindResourceEx</a> function.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-loadresource#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>HGLOBAL</b> If the function succeeds, the return value is a handle to the data associated with the resource. If the function fails, the return value is <b>NULL</b>. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-loadresource">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern HGLOBAL LoadResource(HMODULE hModule, HRSRC hResInfo);

    /// <summary>Retrieves a pointer to the specified resource in memory.</summary>
    /// <param name="hResData">
    /// <para>Type: **HGLOBAL** A handle to the resource to be accessed. The [LoadResource function](nf-libloaderapi-loadresource.md) returns this handle. Note that this parameter is listed as an **HGLOBAL** variable only for backward compatibility. Do not pass any value as a parameter other than a successful return value from the **LoadResource** function.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-lockresource#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: **LPVOID** If the loaded resource is available, the return value is a pointer to the first byte of the resource; otherwise, it is **NULL**.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-lockresource">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern unsafe void* LockResource(HGLOBAL hResData);

    /// <inheritdoc cref="SizeofResource(HMODULE, HRSRC)"/>
    public static uint SizeofResource(SafeHandle? hModule, HRSRC hResInfo) {
        bool hModuleAddRef = false;
        try {
            HMODULE hModuleLocal;
            if (hModule != null) {
                hModule.DangerousAddRef(ref hModuleAddRef);
                hModuleLocal = (HMODULE) hModule.DangerousGetHandle();
            } else
                hModuleLocal = (HMODULE) new IntPtr(0L);
            return SizeofResource(hModuleLocal, hResInfo);
        } finally {
            if (hModuleAddRef)
                hModule!.DangerousRelease();
        }
    }

    /// <summary>Retrieves the size, in bytes, of the specified resource.</summary>
    /// <param name="hModule">
    /// <para>Type: <b>HMODULE</b> A handle to the module whose executable file contains the resource.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-sizeofresource#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="hResInfo">
    /// <para>Type: <b>HRSRC</b> A handle to the resource. This handle must be created by using the <a href="https://docs.microsoft.com/windows/win32/api/winbase/nf-winbase-findresourcea">FindResource</a> or <a href="https://docs.microsoft.com/windows/win32/api/winbase/nf-winbase-findresourceexa">FindResourceEx</a> function.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-sizeofresource#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>DWORD</b> If the function succeeds, the return value is the number of bytes in the resource. If the function fails, the return value is zero. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-sizeofresource">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint SizeofResource(HMODULE hModule, HRSRC hResInfo);

    /// <inheritdoc cref="EnumResourceNames(HMODULE, ResourceAtom, ENUMRESNAMEPROCW, nint)"/>
    public static bool EnumResourceNames(SafeHandle? hModule, ResourceAtom lpType,
            ENUMRESNAMEPROCW lpEnumFunc, nint lParam) {
        var hModuleAddRef = false;
        try {
            HMODULE hModuleLocal;
            if (hModule != null) {
                hModule.DangerousAddRef(ref hModuleAddRef);
                hModuleLocal = (HMODULE) hModule.DangerousGetHandle();
            } else
                hModuleLocal = (HMODULE) new IntPtr(0L);
            return EnumResourceNames(hModuleLocal, lpType, lpEnumFunc, lParam);
        } finally {
            if (hModuleAddRef)
                hModule!.DangerousRelease();
        }
    }

    /// <summary>Enumerates resources of a specified type within a binary module.</summary>
    /// <param name="hModule">
    /// <para>Type: **HMODULE** A handle to a module to be searched. Starting with Windows Vista, if this is an LN file, then appropriate .mui files (if any exist) are included in the search. If this parameter is **NULL**, that is equivalent to passing in a handle to the module used to create the current process.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-enumresourcenamesw#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpType">
    /// <para>Type: **LPCTSTR** The type of the resource for which the name is being enumerated. Alternately, rather than a pointer, this parameter can be [MAKEINTRESOURCE](/windows/desktop/api/winuser/nf-winuser-makeintresourcea)(ID), where ID is an integer value representing a predefined resource type. For a list of predefined resource types, see [Resource Types](/windows/win32/menurc/resource-types). For more information, see the [Remarks](#remarks) section below.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-enumresourcenamesw#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpEnumFunc">
    /// <para>Type: **ENUMRESNAMEPROC** A pointer to the callback function to be called for each enumerated resource name or ID. For more information, see [ENUMRESNAMEPROC](/windows/win32/api/libloaderapi/nc-libloaderapi-enumresnameprocw).</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-enumresourcenamesw#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lParam">
    /// <para>Type: **LONG_PTR** An application-defined value passed to the callback function. This parameter can be used in error checking.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-enumresourcenamesw#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: **BOOL** The return value is **TRUE** if the function succeeds or **FALSE** if the function does not find a resource of the type specified, or if the function fails for another reason. To get extended error information, call [GetLastError](/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror).</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//libloaderapi/nf-libloaderapi-enumresourcenamesw">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern BOOL EnumResourceNames(HMODULE hModule, ResourceAtom lpType,
            ENUMRESNAMEPROCW lpEnumFunc, nint lParam);

    /// <inheritdoc cref="EnumResourceTypes(HMODULE, ENUMRESTYPEPROCW, nint)"/>
    public static bool EnumResourceTypes(SafeHandle? hModule, ENUMRESTYPEPROCW lpEnumFunc, nint lParam) {
        var hModuleAddRef = false;
        try {
            HMODULE hModuleLocal;
            if (hModule != null) {
                hModule.DangerousAddRef(ref hModuleAddRef);
                hModuleLocal = (HMODULE) hModule.DangerousGetHandle();
            } else
                hModuleLocal = (HMODULE) new IntPtr(0L);
            return EnumResourceTypes(hModuleLocal, lpEnumFunc, lParam);
        } finally {
            if (hModuleAddRef)
                hModule!.DangerousRelease();
        }
    }

    /// <see href="https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-enumresourcetypesw"/>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "EnumResourceTypesW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern BOOL EnumResourceTypes(HMODULE hModule, ENUMRESTYPEPROCW lpEnumFunc, nint lParam);

    /// <summary>Commits or discards changes made prior to a call to UpdateResource.</summary>
    /// <param name="hUpdate">
    /// <para>Type: <b>HANDLE</b> A module handle returned by the <a href="https://docs.microsoft.com/windows/desktop/api/winbase/nf-winbase-beginupdateresourcea">BeginUpdateResource</a> function, and used by <a href="https://docs.microsoft.com/windows/desktop/api/winbase/nf-winbase-updateresourcea">UpdateResource</a>, referencing the file to be updated.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-endupdateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="fDiscard">
    /// <para>Type: <b>BOOL</b> Indicates whether to write the resource updates to the file. If this parameter is <b>TRUE</b>, no changes are made. If it is <b>FALSE</b>, the changes are made: the resource updates will take effect.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-endupdateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>BOOL</b> Returns <b>TRUE</b> if the function succeeds; <b>FALSE</b> otherwise. If the function succeeds and <i>fDiscard</i> is <b>TRUE</b>, then no resource updates are made to the file; otherwise all successful resource updates are made to the file. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-endupdateresourcew">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "EndUpdateResourceW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern BOOL EndUpdateResource(IntPtr hUpdate, BOOL fDiscard);

    /// <summary>Retrieves a handle that can be used by the UpdateResource function to add, delete, or replace resources in a binary module.</summary>
    /// <param name="pFileName">
    /// <para>Type: <b>LPCTSTR</b> The binary file in which to update resources. An application must be able to obtain write-access to this file; the file referenced by <i>pFileName</i> cannot be currently executing. If <i>pFileName</i> does not specify a full path, the system searches for the file in the current directory.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-beginupdateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="bDeleteExistingResources">
    /// <para>Type: <b>BOOL</b> Indicates whether to delete the <i>pFileName</i> parameter's existing resources. If this parameter is <b>TRUE</b>, existing resources are deleted and the updated file includes only resources added with the <a href="https://docs.microsoft.com/windows/desktop/api/winbase/nf-winbase-updateresourcea">UpdateResource</a> function. If this parameter is <b>FALSE</b>, the updated file includes existing resources unless they are explicitly deleted or replaced by using <b>UpdateResource</b>.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-beginupdateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>HANDLE</b> If the function succeeds, the return value is a handle that can be used by the <a href="/windows/desktop/api/winbase/nf-winbase-updateresourcea">UpdateResource</a> and <a href="/windows/desktop/api/winbase/nf-winbase-endupdateresourcea">EndUpdateResource</a> functions. The return value is <b>NULL</b> if the specified file is not a PE, the file does not exist, or the file cannot be opened for writing. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-beginupdateresourcew">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "BeginUpdateResourceW", SetLastError = true,
            CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern ResourceUpdateSafeHandle BeginUpdateResource(string pFileName, BOOL bDeleteExistingResources);

    /// <summary>Adds, deletes, or replaces a resource in a portable executable (PE) file.</summary>
    /// <param name="hUpdate">
    /// <para>Type: <b>HANDLE</b> A module handle returned by the <a href="https://docs.microsoft.com/windows/desktop/api/winbase/nf-winbase-beginupdateresourcea">BeginUpdateResource</a> function, referencing the file to be updated.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpType">
    /// <para>Type: <b>LPCTSTR</b> The resource type to be updated. Alternatively, rather than a pointer, this parameter can be <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-makeintresourcea">MAKEINTRESOURCE</a>(ID), where ID is an integer value representing a predefined resource type. If the first character of the string is a pound sign (#), then the remaining characters represent a decimal number that specifies the integer identifier of the resource type. For example, the string "#258" represents the identifier 258. For a list of predefined resource types, see <a href="https://docs.microsoft.com/windows/desktop/direct3d10/d3d10-graphics-programming-guide-resources-types">Resource Types</a>.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpName">
    /// <para>Type: <b>LPCTSTR</b> The name of the resource to be updated. Alternatively, rather than a pointer, this parameter can be <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-makeintresourcea">MAKEINTRESOURCE</a>(ID), where ID is a resource ID. When creating a new resource do not use a string that begins with a '#' character for this parameter.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="wLanguage">
    /// <para>Type: <b>WORD</b> The <a href="https://docs.microsoft.com/windows/desktop/Intl/language-identifiers">language identifier</a> of the resource to be updated. For a list of the primary language identifiers and sublanguage identifiers that make up a language identifier, see the <a href="https://docs.microsoft.com/windows/desktop/api/winnt/nf-winnt-makelangid">MAKELANGID</a>  macro.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="lpData">
    /// <para>Type: <b>LPVOID</b> The resource data to be inserted into the file indicated by <i>hUpdate</i>. If the resource is one of the predefined types, the data must be valid and properly aligned. Note that this is the raw binary data to be stored in the file indicated by <i>hUpdate</i>, not the data provided by <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-loadicona">LoadIcon</a>, <a href="https://docs.microsoft.com/windows/desktop/api/winuser/nf-winuser-loadstringa">LoadString</a>, or other resource-specific load functions. All data containing strings or text must be in Unicode format. <i>lpData</i> must not point to ANSI data.</para>
    /// <para>If <i>lpData</i> is <b>NULL</b> and <i>cbData</i> is 0, the specified resource is deleted from the file indicated by <i>hUpdate</i>.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <param name="cb">
    /// <para>Type: <b>DWORD</b> The size, in bytes, of the resource data at <i>lpData</i>.</para>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew#parameters">Read more on docs.microsoft.com</see>.</para>
    /// </param>
    /// <returns>
    /// <para>Type: <b>BOOL</b> Returns <b>TRUE</b> if successful or <b>FALSE</b> otherwise. To get extended error information, call <a href="/windows/desktop/api/errhandlingapi/nf-errhandlingapi-getlasterror">GetLastError</a>.</para>
    /// </returns>
    /// <remarks>
    /// <para><see href="https://docs.microsoft.com/windows/win32/api//winbase/nf-winbase-updateresourcew">Learn more about this API from docs.microsoft.com</see>.</para>
    /// </remarks>
    [DllImport("KERNEL32.dll", ExactSpelling = true, EntryPoint = "UpdateResourceW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern unsafe BOOL UpdateResource(ResourceUpdateSafeHandle hUpdate, ResourceAtom lpType,
            ResourceAtom lpName, ushort wLanguage, [Optional] void* lpData, uint cb);

    /// <summary>
    /// Represents a Win32 handle that can be closed with <see cref="FreeLibrary"/>.
    /// </summary>
    public class FreeLibrarySafeHandle
            : SafeHandle {
        private static readonly IntPtr InvalidHandleValue = new IntPtr(0L);
        public FreeLibrarySafeHandle() : base(InvalidHandleValue, true) {}

        public FreeLibrarySafeHandle(IntPtr preexistingHandle, bool ownsHandle = true) :
                base(InvalidHandleValue, ownsHandle) {
            this.SetHandle(preexistingHandle);
        }

        public override bool IsInvalid => this.handle.ToInt64() == 0L;

        protected override bool ReleaseHandle() => FreeLibrary((HMODULE) this.handle);
    }

    public sealed class ResourceUpdateSafeHandle : SafeHandle {
        private static readonly IntPtr InvalidHandleValue = default;

        public ResourceUpdateSafeHandle() : base(InvalidHandleValue, true) {}

        public ResourceUpdateSafeHandle(IntPtr handle, bool ownsHandle = true) : base(InvalidHandleValue, ownsHandle) {
            SetHandle(handle);
        }

        public override bool IsInvalid => handle == InvalidHandleValue;

        protected override bool ReleaseHandle() {
            return EndUpdateResource(handle, true);
        }

        public void CommitChanges() {
            if (!EndUpdateResource(handle, false)) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
            SetHandleAsInvalid();
        }
    }

    [DebuggerDisplay("{Value}")]
    public readonly struct BOOL : IEquatable<BOOL> {
        public readonly int Value;
        public BOOL(int value) => this.Value = value;
        public static implicit operator int(BOOL value) => value.Value;
        public static explicit operator BOOL(int value) => new BOOL(value);
        public static bool operator ==(BOOL left, BOOL right) => left.Value == right.Value;
        public static bool operator !=(BOOL left, BOOL right) => !(left == right);

        public bool Equals(BOOL other) => this.Value == other.Value;

        public override bool Equals(object obj) => obj is BOOL other && this.Equals(other);

        public override int GetHashCode() => this.Value.GetHashCode();
        public BOOL(bool value) => this.Value = value ? 1 : 0;
        public static implicit operator bool(BOOL value) => value.Value != 0;
        public static implicit operator BOOL(bool value) => new BOOL(value);
    }

    [DebuggerDisplay("{Value}")]
    public readonly struct HMODULE : IEquatable<HMODULE> {
        public readonly IntPtr Value;
        public HMODULE(IntPtr value) => this.Value = value;

        public static HMODULE Null => default;

        public bool IsNull => Value == default;
        public static implicit operator IntPtr(HMODULE value) => value.Value;
        public static explicit operator HMODULE(IntPtr value) => new HMODULE(value);
        public static bool operator ==(HMODULE left, HMODULE right) => left.Value == right.Value;
        public static bool operator !=(HMODULE left, HMODULE right) => !(left == right);

        public bool Equals(HMODULE other) => this.Value == other.Value;

        public override bool Equals(object obj) => obj is HMODULE other && this.Equals(other);

        public override int GetHashCode() => this.Value.GetHashCode();
    }

    [DebuggerDisplay("{Value}")]
    public readonly struct HRSRC : IEquatable<HRSRC> {
        public readonly nint Value;
        public HRSRC(nint value) => this.Value = value;
        public static implicit operator nint(HRSRC value) => value.Value;
        public static explicit operator HRSRC(nint value) => new HRSRC(value);
        public static bool operator ==(HRSRC left, HRSRC right) => left.Value == right.Value;
        public static bool operator !=(HRSRC left, HRSRC right) => !(left == right);

        public bool Equals(HRSRC other) => this.Value == other.Value;

        public override bool Equals(object obj) => obj is HRSRC other && this.Equals(other);

        public override int GetHashCode() => this.Value.GetHashCode();
    }

    [DebuggerDisplay("{Value}")]
    public readonly struct HGLOBAL : IEquatable<HGLOBAL> {
        public readonly IntPtr Value;
        public HGLOBAL(IntPtr value) => this.Value = value;

        public static HGLOBAL Null => default;

        public bool IsNull => Value == default;
        public static implicit operator IntPtr(HGLOBAL value) => value.Value;
        public static explicit operator HGLOBAL(IntPtr value) => new HGLOBAL(value);
        public static bool operator ==(HGLOBAL left, HGLOBAL right) => left.Value == right.Value;
        public static bool operator !=(HGLOBAL left, HGLOBAL right) => !(left == right);

        public bool Equals(HGLOBAL other) => this.Value == other.Value;

        public override bool Equals(object obj) => obj is HGLOBAL other && this.Equals(other);

        public override int GetHashCode() => this.Value.GetHashCode();
    }

// https://devblogs.microsoft.com/oldnewthing/20110217-00/?p=11463
// NOTE: string resource names stop being valid when the originating Module is disposed, do NOT try to access them
    public readonly unsafe struct ResourceAtom {
        private readonly char* _name;

        public ResourceAtom(ushort id) {
            _name = (char*) id;
        }

        public ResourceAtom(char* name) {
            _name = name;
        }

        public static implicit operator ResourceAtom(ushort id) {
            return new ResourceAtom(id);
        }

        public bool IsId() {
            return (nuint) _name <= ushort.MaxValue;
        }

        /// Note that this copies the pointed-to string.
        public string GetAsString() {
            if (IsId()) {
                throw new InvalidOperationException("The atom is a numeric value, not a string");
            }
            return new string(_name);
        }

        public ushort GetAsId() {
            return (ushort) _name;
        }

        public override string ToString() {
            return IsId() ? $"#{GetAsId()}" : GetAsString();
        }

        private static bool StringEquals(char* p1, char* p2) {
            // never thought I'd write this in C#
            for (; *p1 == *p2 && *p1 != 0; p1++, p2++) {}
            return *p1 == *p2;
        }

        public static bool operator ==(ResourceAtom a, ResourceAtom b) {
            if (a.IsId() != b.IsId()) return false;
            if (a.IsId()) return a.GetAsId() == b.GetAsId();
            else return StringEquals(a._name, b._name);
        }

        public static bool operator !=(ResourceAtom a, ResourceAtom b) => !(a == b);
        public bool Equals(ResourceAtom other) => this == other;
        public override bool Equals(object? obj) => obj is ResourceAtom other && this == other;
        public override int GetHashCode() => (int) (nuint) _name;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    // ReSharper disable once InconsistentNaming, IdentifierTypo
    public delegate BOOL ENUMRESNAMEPROCW(HMODULE hModule, ResourceAtom lpType, ResourceAtom lpName, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    // ReSharper disable once InconsistentNaming, IdentifierTypo
    public delegate BOOL ENUMRESTYPEPROCW(HMODULE hModule, ResourceAtom lpType, nint lParam);
}
