using System;
using System.Diagnostics;

namespace Pog.Utils;

internal class DebouncedProgress<T>(TimeSpan interval, Action<T> progressCb) : IProgress<T> {
    private readonly Stopwatch _timer = new();
    private readonly long _intervalTicks = interval.Ticks;

    public DebouncedProgress(IProgress<T> progress, TimeSpan interval) : this(interval, progress.Report) {}

    public void Report(T value) {
        if (ShouldReport()) progressCb(value);
    }

    private bool ShouldReport() {
        if (_timer.IsRunning && _timer.ElapsedTicks < _intervalTicks)
            return false;
        else {
            _timer.Restart();
            return true;
        }
    }
}
