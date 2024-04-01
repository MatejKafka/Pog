using System;
using System.Diagnostics;

namespace Pog.Utils;

/** Variant of Lazy where the value expires after a given interval and is re-created on next access. */
internal sealed class TimedLazy<T>(TimeSpan timeout, Func<T> generator) where T : class {
    private T? _value = null;
    private readonly Stopwatch _timer = new();
    private readonly long _timeoutTicks = (long) (Stopwatch.Frequency * timeout.TotalSeconds);

    public T Value {
        get {
            if (_timer.ElapsedTicks >= _timeoutTicks || _value == null) {
                _value = generator();
                _timer.Restart();
            }
            return _value!;
        }
    }

    public void Invalidate() {
        _value = null;
    }
}
