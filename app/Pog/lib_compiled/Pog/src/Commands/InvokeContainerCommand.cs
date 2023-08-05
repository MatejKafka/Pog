using System;
using System.Collections;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog.Commands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "Container")]
[OutputType(typeof(object))]
public class InvokeContainerCommand : PSCmdlet, IDisposable {
    [Parameter(Mandatory = true, Position = 0)] public Container.ContainerType ContainerType;
    [Parameter(Mandatory = true, Position = 1)] public Package Package = null!;
    [Parameter(Mandatory = true)] public Hashtable InternalArguments = null!;
    [Parameter] public Hashtable PackageArguments = new();

    private Container _container = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();
        _container = new Container(ContainerType, Package, InternalArguments, PackageArguments, Host,
                ReadStreamPreferenceVariables());
        var outputCollection = new PSDataCollection<PSObject>();
        var asyncResult = _container.BeginInvoke(outputCollection);
        foreach (var o in outputCollection) {
            WriteObject(o);
        }
        _container.EndInvoke(asyncResult);
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        // stop the container on Ctrl-C
        _container.Stop();
    }

    public void Dispose() {
        _container.Dispose();
    }

    private object GetPreferenceVariableValue(string varName, string? paramName, Func<object, object>? mapParam) {
        var parentVar = SessionState.PSVariable.Get(varName); // get var from parent scope
        return paramName != null && mapParam != null &&
               MyInvocation.BoundParameters.TryGetValue(paramName, out var obj)
                ? mapParam(obj) // map the passed parameter
                : parentVar.Value; // use the preference variable value from the parent scope
    }

    private Container.OutputStreamConfig ReadStreamPreferenceVariables() {
        var config = new Container.OutputStreamConfig(
                (ActionPreference) GetPreferenceVariableValue("ProgressPreference", null, null),
                (ActionPreference) GetPreferenceVariableValue("WarningPreference", "WarningAction", param => param),
                (ActionPreference) GetPreferenceVariableValue("InformationPreference", "InformationAction", param => param),
                (ActionPreference) GetPreferenceVariableValue("VerbosePreference", "Verbose",
                        param => ((SwitchParameter) param) ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
                (ActionPreference) GetPreferenceVariableValue("DebugPreference", "Debug",
                        param => ((SwitchParameter) param) ? ActionPreference.Continue : ActionPreference.SilentlyContinue)
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
