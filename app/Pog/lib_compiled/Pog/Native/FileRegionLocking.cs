using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public static partial class Native {
    public static FileRegionLock LockFile(FileStream stream, Win32.LockFileFlags flags, ulong position, ulong length) {
        var regionLock = new FileRegionLock(stream.SafeFileHandle!.DangerousGetHandle(), position, length);
        var success = Win32.LockFileEx(regionLock.Handle, flags, 0, regionLock.LengthLow, regionLock.LengthHigh,
                ref regionLock.Overlapped);
        if (!success) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return regionLock;
    }

    [PublicAPI]
    public class FileRegionLock : IDisposable {
        internal IntPtr Handle;
        internal NativeOverlapped Overlapped;
        internal uint LengthLow;
        internal uint LengthHigh;
        private bool _locked = true;

        internal FileRegionLock(IntPtr handle, ulong position, ulong length) {
            Handle = handle;
            Overlapped = new NativeOverlapped {
                OffsetLow = (int) (position & 0xFFFFFFFF),
                OffsetHigh = (int) (position >> 32)
            };
            LengthLow = (uint) (length & 0xFFFFFFFF);
            LengthHigh = (uint) (length >> 32);
        }

        public void Unlock() {
            if (!_locked) return;
            var success = Win32.UnlockFileEx(Handle, 0, LengthLow, LengthHigh, ref Overlapped);
            if (success) {
                _locked = false;
            } else {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }

        public void Dispose() {
            Unlock();
        }
    }
}
