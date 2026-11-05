using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

internal class InvokeContainer(PogCmdlet cmdlet) : EnumerableCommand<PSObject>(cmdlet), IDisposable {
    [Parameter] public required PSCommand Run;
    [Parameter] public string[]? Modules = null;
    [Parameter] public SessionStateVariableEntry[]? Variables = null;
    [Parameter] public string? WorkingDirectory = null;
    [Parameter] public object? Context = null;

    private Container? _container;

    public void Dispose() => _container?.Dispose();

    public override IEnumerable<PSObject> Invoke() {
        var streamConfig = Container.OutputStreamConfig.FromCmdletPreferenceVariables(Cmdlet);
        _container = new Container(Host, streamConfig, Modules, Variables, WorkingDirectory, Context);

        foreach (var output in _container.Invoke(Run, CancellationToken)) {
            if (output.PipelineOutput) {
                yield return (PSObject) output.Output;
            } else {
                switch (output.Output) {
                    case ErrorRecord e:
                        WriteError(e);
                        break;
                    case WarningRecord w:
                        WriteWarning(w.Message);
                        break;
                    case VerboseRecord v:
                        WriteVerbose(v.Message);
                        break;
                    case DebugRecord d:
                        WriteDebug(d.Message);
                        break;
                    case InformationRecord i:
                        WriteInformation(i.MessageData, i.Tags.ToArray());
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
    }
}
