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

    // PowerShell does not give us any simple way to just ask for the effective value of a preference variable
    // instead, we have to effectively reimplement the algorithm PowerShell uses internally; fun
    private T? GetPreferenceVariableValue<T>(string varName, string? paramName, Func<object, T>? mapParam) where T : struct {
        if (paramName != null && mapParam != null &&
            Cmdlet.MyInvocation.BoundParameters.TryGetValue(paramName, out var obj)) {
            return mapParam(obj);
        } else {
            var parentVar = Cmdlet.SessionState.PSVariable.Get(varName); // get var from parent scope
            return parentVar?.Value switch {
                null => null,
                T v => v,
                // https://github.com/PowerShell/PowerShell/issues/3483
                var v => Enum.TryParse(v.ToString(), out T pref) ? pref : null,
            };
        }
    }

    private Container.OutputStreamConfig ReadStreamPreferenceVariables() {
        var config = new Container.OutputStreamConfig(
                GetPreferenceVariableValue<ActionPreference>("ProgressPreference", null, null),
                GetPreferenceVariableValue("WarningPreference", "WarningAction", param => (ActionPreference) param),
                GetPreferenceVariableValue("InformationPreference", "InformationAction", param => (ActionPreference) param),
                GetPreferenceVariableValue("VerbosePreference", "Verbose",
                        param => (SwitchParameter) param ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
                GetPreferenceVariableValue("DebugPreference", "Debug",
                        param => (SwitchParameter) param ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
                GetPreferenceVariableValue("ConfirmPreference", "Confirm",
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
