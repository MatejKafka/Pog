using System;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace Pog.Native;

[PublicAPI]
public class FileLock : IDisposable {
    internal SafeFileHandle Handle;
    internal NativeOverlapped Overlapped;
    internal uint LengthLow;
    internal uint LengthHigh;
    private bool _locked = true;

    internal FileLock(SafeFileHandle handle, ulong position, ulong length) {
        Handle = handle;
        Overlapped = new NativeOverlapped {
            OffsetLow = (int) (position & 0xFFFFFFFF),
            OffsetHigh = (int) (position >> 32),
        };
        LengthLow = (uint) (length & 0xFFFFFFFF);
        LengthHigh = (uint) (length >> 32);
    }

    /// Locks the whole file.
    public static FileLock Lock(SafeFileHandle handle, Win32.LockFileFlags flags) {
        return Lock(handle, flags, 0, ulong.MaxValue);
    }

    public static FileLock Lock(SafeFileHandle handle, Win32.LockFileFlags flags, ulong position, ulong length) {
        var regionLock = new FileLock(handle, position, length);
        var success = Win32.LockFileEx(regionLock.Handle, flags, 0, regionLock.LengthLow, regionLock.LengthHigh,
                ref regionLock.Overlapped);
        if (!success) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return regionLock;
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
