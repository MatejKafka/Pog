using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pog.Tests.TestUtils;

internal static class Win32 {
    // copied from https://pinvoke.net/default.aspx/shell32/CommandLineToArgvW.html
    public static string[] CommandLineToArgv(string commandLine) {
        var ptrToSplitArgs = CommandLineToArgvW(commandLine, out var numberOfArgs);

        // CommandLineToArgvW returns NULL upon failure.
        if (ptrToSplitArgs == IntPtr.Zero) {
            throw new ArgumentException("Unable to split argument.", new Win32Exception());
        }

        // Make sure the memory ptrToSplitArgs to is freed, even upon failure.
        try {
            var splitArgs = new string[numberOfArgs];

            // ptrToSplitArgs is an array of pointers to null terminated Unicode strings.
            // Copy each of these strings into our split argument array.
            for (var i = 0; i < numberOfArgs; i++) {
                splitArgs[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptrToSplitArgs, i * IntPtr.Size))!;
            }
            return splitArgs;
        } finally {
            // Free memory obtained by CommandLineToArgW.
            LocalFree(ptrToSplitArgs);
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}