using System.Diagnostics;
using System.Management.Automation;
using Xunit;

namespace Pog.Tests;

public class ContainerTests {
    [Fact]
    public void TestCancellation() {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);
        var timer = Stopwatch.StartNew();

        using var container = new Container();
        var output = container.Invoke(new PSCommand().AddCommand("Start-Sleep").AddParameter("Seconds", 5), cts.Token);

        Assert.Throws<PipelineStoppedException>(() => {
            foreach (var _ in output) {}
        });
        Assert.True(timer.ElapsedMilliseconds < 2500);
    }

    [Fact]
    public void TestCancellationDuringOutput() {
        using var cts = new CancellationTokenSource();
        var timer = Stopwatch.StartNew();

        using var container = new Container();
        var output = container.Invoke(new PSCommand().AddScript("echo x; sleep 5; echo y"), cts.Token);

        using var it = output.GetEnumerator();
        Assert.True(it.MoveNext());
        Assert.Equal("x", LanguagePrimitives.ConvertTo<string>(it.Current.Output));

        cts.Cancel();
        Assert.Throws<PipelineStoppedException>(() => it.MoveNext());

        Assert.True(timer.ElapsedMilliseconds < 2500);
    }

    [Fact]
    public void TestErrorPropagation() {
        using var container = new Container();
        var output = container.Invoke(new PSCommand().AddScript("throw 'failed'"));

        var e = Assert.Throws<RuntimeException>(() => {
            foreach (var _ in output) {}
        });
        Assert.Equal("failed", e.Message);
    }
}