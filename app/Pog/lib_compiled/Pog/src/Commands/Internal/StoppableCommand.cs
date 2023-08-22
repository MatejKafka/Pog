using System;
using System.Management.Automation;
using System.Threading;

namespace Pog.Commands.Internal;

public abstract class StoppableCommand : Command, IDisposable {
    private readonly CancellationTokenSource _stopping = new();
    protected CancellationToken CancellationToken => _stopping.Token;

    protected StoppableCommand(PSCmdlet cmdlet) : base(cmdlet) {}

    public override void StopProcessing() {
        base.StopProcessing();
        _stopping.Cancel();
    }

    public void Dispose() => _stopping.Dispose();
}
