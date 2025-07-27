using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

internal class InvokeContainer(PogCmdlet cmdlet) : EnumerableCommand<PSObject>(cmdlet), IDisposable {
    [Parameter] public required Action<PowerShell> Run;
    [Parameter] public string[]? Modules = null;
    [Parameter] public SessionStateVariableEntry[]? Variables = null;
    [Parameter] public string? WorkingDirectory = null;
    [Parameter] public object? Context = null;

    private Container? _container;

    public override IEnumerable<PSObject> Invoke() {
        var streamConfig = Container.OutputStreamConfig.FromCmdletPreferenceVariables(Cmdlet);
        _container = new Container(Host, streamConfig, Modules, Variables, WorkingDirectory, Context);
        return _container.Invoke(Run, CancellationToken);
    }

    public void Dispose() {
        _container?.Dispose();
    }
}
