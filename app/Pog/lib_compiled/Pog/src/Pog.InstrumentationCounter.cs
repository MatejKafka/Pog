using System.Threading;
using JetBrains.Annotations;

namespace Pog;

/// Static class for tracking statistics relevant for optimization.
public static class InstrumentationCounter {
    public static Counter UserManifestLoads = new();
    public static Counter ManifestLoads = new();
    public static Counter PackageRootFileReads = new();
    public static Counter ManifestTemplateSubstitutions = new();

    public struct Counter {
        private long _value = 0;
        [PublicAPI]
        public ulong Value => (ulong) Interlocked.Read(ref _value);

        public Counter() {}

        internal void Increment() {
            Interlocked.Increment(ref _value);
        }
    }
}
