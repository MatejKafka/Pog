using System;
using System.Runtime.InteropServices;

namespace Pog.Stub;

// resource manipulation is significantly easier in C++ compared to C#, so we're using a native DLL
internal static class PogNative {
    public static unsafe void PrepareStubExecutableResources(string stubPath, string targetPath, Span<byte> stubData) {
        fixed (byte* stubDataPtr = stubData) {
            var errorMsg = prepare_stub_executable_resources(stubPath, targetPath, stubDataPtr, (UIntPtr) stubData.Length);
            if (errorMsg != null) {
                throw new InternalError("Stub executable setup failed: " + errorMsg);
            }
        }
    }

    /// `stubLibraryHandle` MUST be closed after you are finished working with stub data by calling `.FreeStubHandle(...)`.
    public static unsafe Span<byte> ReadStubData(string stubPath, out IntPtr stubLibraryHandle) {
        var errorMsg = read_stub_data(stubPath, out stubLibraryHandle, out var stubDataPtr, out var stubDataSize);
        if (errorMsg != null) {
            throw new InternalError("Could not read stub data: " + errorMsg);
        }
        return new Span<byte>(stubDataPtr, (int)stubDataSize);
    }

    public static void FreeStubHandle(IntPtr stubLibraryHandle) {
        close_stub_data(stubLibraryHandle);
    }

    [DllImport("PogNative.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.BStr)]
    private static extern unsafe string prepare_stub_executable_resources(
            in string stubPath, in string targetPath, in byte* stubData, UIntPtr stubDataSize);

    [DllImport("PogNative.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.BStr)]
    private static extern unsafe string read_stub_data(
            in string stubPath, out IntPtr stubLibraryHandle, out byte* stubData, out UIntPtr stubDataSize);

    [DllImport("PogNative.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.BStr)]
    private static extern string close_stub_data(in IntPtr stubLibraryHandle);
}
