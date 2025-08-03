using System;
using System.Threading;
using Pog.Utils;
using Xunit;

namespace Pog.Tests.Utils;

public class TimedLazyTests {
    [Fact]
    public void TestCaching() {
        TestClass.Reset();
        var lazy = new TimedLazy<TestClass>(TimeSpan.FromMilliseconds(100), () => new TestClass());

        Assert.Equal(0, lazy.Value.Index);
        Assert.Equal(0, lazy.Value.Index);
        Assert.Equal(0, lazy.Value.Index);

        Thread.Sleep(105);

        Assert.Equal(1, lazy.Value.Index);
        Assert.Equal(1, lazy.Value.Index);
        Assert.Equal(1, lazy.Value.Index);

        lazy.Invalidate();
        Assert.Equal(2, lazy.Value.Index);
    }

    // in case TimedLazy is ever switched to a struct, this test validates that it's not copied where it shouldn't be
    [Fact]
    public void TestCachingWithWrapper() {
        TestClass.Reset();
        var wrapper = new Wrapper();

        Assert.Equal(0, wrapper.Value.Index);
        Assert.Equal(0, wrapper.Value.Index);
        Assert.Equal(0, wrapper.Value.Index);

        Thread.Sleep(105);

        Assert.Equal(1, wrapper.Value.Index);
        Assert.Equal(1, wrapper.Value.Index);
        Assert.Equal(1, wrapper.Value.Index);
    }

    private class Wrapper {
        private readonly TimedLazy<TestClass> _lazy = new(TimeSpan.FromMilliseconds(100), () => new TestClass());
        internal TestClass Value => _lazy.Value;
    }

    private class TestClass {
        private static int _counter = 0;
        public readonly int Index = _counter++;

        internal static void Reset() {
            _counter = 0;
        }
    }
}