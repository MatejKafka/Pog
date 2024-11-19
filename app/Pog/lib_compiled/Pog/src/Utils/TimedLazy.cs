using System;
using System.Diagnostics;

namespace Pog.Utils;

/// Variant of Lazy where the value expires after a given interval and is re-created on next access.
internal sealed class TimedLazy<T>(TimeSpan timeout, Func<T> generator) where T : class {
    private T? _value = null;
    private readonly Stopwatch _timer = new();
    private readonly long _timeoutTicks = (long) (Stopwatch.Frequency * timeout.TotalSeconds);
    private readonly object _lock = new();

    public T Value {
        get {
            lock (_lock) {
                if (_value == null || _timer.ElapsedTicks >= _timeoutTicks) {
                    // if generator throws an exception, the old value is kept, so it will get invoked again on next access
                    _value = generator();
                    Debug.Assert(_value != null);
                    _timer.Restart();
                }
                return _value!;
            }
        }
    }

    public void Invalidate() {
        lock (_lock) {
            _value = null;
        }
    }
}
