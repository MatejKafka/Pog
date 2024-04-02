using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;

namespace Pog.Native;

// copied from PowerShell: src/Microsoft.PowerShell.Commands.Management/commands/management/Clipboard.cs
[PublicAPI]
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
public static class Clipboard {
    private const uint CF_TEXT = 1;
    private const uint CF_UNICODETEXT = 13;

    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;
    private const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

    public static void SetText(string text) {
        ExecuteOnStaThread(() => SetClipboardData(Tuple.Create(text, CF_UNICODETEXT)));
    }

    private static void ExecuteOnStaThread(Func<bool> action) {
        const int RetryCount = 5;
        int tries = 0;

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) {
            while (tries++ < RetryCount && !action()) {
                // wait until RetryCount or action
            }

            return;
        }

        Exception? exception = null;
        var thread = new Thread(() => {
            try {
                while (tries++ < RetryCount && !action()) {
                    // wait until RetryCount or action
                }
            } catch (Exception e) {
                exception = e;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) {
            throw exception;
        }
    }

    private static bool SetClipboardData(params Tuple<string, uint>[] data) {
        try {
            if (!Win32.OpenClipboard(IntPtr.Zero)) {
                return false;
            }

            Win32.EmptyClipboard();

            foreach (var d in data) {
                if (!SetSingleClipboardData(d.Item1, d.Item2)) {
                    return false;
                }
            }
        } finally {
            Win32.CloseClipboard();
        }

        return true;
    }

    private static bool SetSingleClipboardData(string text, uint format) {
        IntPtr hGlobal = IntPtr.Zero;
        IntPtr data = IntPtr.Zero;

        try {
            uint bytes;
            if (format == CF_TEXT) {
                bytes = (uint) (text.Length + 1);
                data = Marshal.StringToHGlobalAnsi(text);
            } else if (format == CF_UNICODETEXT) {
                bytes = (uint) ((text.Length + 1) * 2);
                data = Marshal.StringToHGlobalUni(text);
            } else {
                // Not yet supported format.
                return false;
            }

            if (data == IntPtr.Zero) {
                return false;
            }

            hGlobal = Win32.GlobalAlloc(GHND, (UIntPtr) bytes);
            if (hGlobal == IntPtr.Zero) {
                return false;
            }

            IntPtr dataCopy = Win32.GlobalLock(hGlobal);
            if (dataCopy == IntPtr.Zero) {
                return false;
            }

            Win32.CopyMemory(dataCopy, data, bytes);
            Win32.GlobalUnlock(hGlobal);

            if (Win32.SetClipboardData(format, hGlobal) != IntPtr.Zero) {
                // The clipboard owns this memory now, so don't free it.
                hGlobal = IntPtr.Zero;
            }
        } catch {
            // Ignore failures
        } finally {
            if (data != IntPtr.Zero) {
                Marshal.FreeHGlobal(data);
            }

            if (hGlobal != IntPtr.Zero) {
                Win32.GlobalFree(hGlobal);
            }
        }

        return true;
    }
}
