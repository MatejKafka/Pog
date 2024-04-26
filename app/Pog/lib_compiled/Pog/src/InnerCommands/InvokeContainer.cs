using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

internal class InvokeContainer(PogCmdlet cmdlet) : EnumerableCommand<PSObject>(cmdlet) {
    [Parameter] public string? WorkingDirectory = null;
    [Parameter] public object? Context = null;

    [Parameter] public string[]? Modules = null;
    [Parameter] public SessionStateVariableEntry[]? Variables = null;
    [Parameter(Mandatory = true)] public Action<PowerShell> Run = null!;

    private Container? _container;

    public override IEnumerable<PSObject> Invoke() {
        _container = new Container(Host, ReadStreamPreferenceVariables(),
                Run, Modules, Variables, WorkingDirectory, Context);

        var outputCollection = new PSDataCollection<PSObject>();
        var asyncResult = _container.BeginInvoke(outputCollection);
        foreach (var o in outputCollection) {
            yield return o;
        }
        _container.EndInvoke(asyncResult);
    }

    public override void StopProcessing() {
        base.StopProcessing();
        // stop the container on Ctrl-C
        _container?.Stop();
    }

    private object GetPreferenceVariableValue(string varName, string? paramName, Func<object, object>? mapParam) {
        var parentVar = Cmdlet.SessionState.PSVariable.Get(varName); // get var from parent scope
        return paramName != null && mapParam != null &&
               Cmdlet.MyInvocation.BoundParameters.TryGetValue(paramName, out var obj)
                ? mapParam(obj) // map the passed parameter
                : parentVar.Value; // use the preference variable value from the parent scope
    }

    private Container.OutputStreamConfig ReadStreamPreferenceVariables() {
        var config = new Container.OutputStreamConfig(
                (ActionPreference) GetPreferenceVariableValue("ProgressPreference", null, null),
                (ActionPreference) GetPreferenceVariableValue("WarningPreference", "WarningAction", param => param),
                (ActionPreference) GetPreferenceVariableValue("InformationPreference", "InformationAction", param => param),
                (ActionPreference) GetPreferenceVariableValue("VerbosePreference", "Verbose",
                        param => (SwitchParameter) param ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
                (ActionPreference) GetPreferenceVariableValue("DebugPreference", "Debug",
                        param => (SwitchParameter) param ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
                (ConfirmImpact) GetPreferenceVariableValue("ConfirmPreference", "Confirm",
                        param => (SwitchParameter) param ? ConfirmImpact.Low : ConfirmImpact.None)
        );

        // other preference variables:
        //   ErrorAction is skipped, as we always set it to "Stop"
        //     ("ErrorActionPreference", "ErrorAction", param => param),
        //   -Confirm is currently not used in the container
        //     ("ConfirmPreference", "Confirm", param => ((SwitchParameter) param) ? ConfirmImpact.Low : ConfirmImpact.High),

        // if debug prints are active, also activate verbose prints
        // TODO: this is quite convenient, but it kinda goes against the original intended use of these variables, is it really a good idea?
        if (config.Debug == ActionPreference.Continue) {
            config.Verbose = ActionPreference.Continue;
        }
        // if verbose prints are active, also activate information prints
        if (config.Verbose == ActionPreference.Continue) {
            config.Information = ActionPreference.Continue;
        }

        return config;
    }
}
