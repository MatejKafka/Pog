using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Pog.Native;

internal sealed class FileLock : IDisposable {
    private readonly SafeFileHandle _handle;
    private NativeOverlapped _overlapped;
    private readonly uint _lengthLow;
    private readonly uint _lengthHigh;
    private bool _locked = true;

    private FileLock(SafeFileHandle handle, ulong position, ulong length) {
        _handle = handle;
        _overlapped = new NativeOverlapped {
            OffsetLow = (int) (position & 0xFFFFFFFF),
            OffsetHigh = (int) (position >> 32),
        };
        _lengthLow = (uint) (length & 0xFFFFFFFF);
        _lengthHigh = (uint) (length >> 32);
    }

    public static FileLock Lock(SafeFileHandle handle, Win32.LockFileFlags flags,
            ulong position = 0, ulong length = ulong.MaxValue) {
        var regionLock = new FileLock(handle, position, length);
        var success = Win32.LockFileEx(regionLock._handle, flags, 0, regionLock._lengthLow, regionLock._lengthHigh,
                ref regionLock._overlapped);
        if (!success) {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }
        return regionLock;
    }

    private void Unlock() {
        if (!_locked) return;
        var success = Win32.UnlockFileEx(_handle, 0, _lengthLow, _lengthHigh, ref _overlapped);
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
