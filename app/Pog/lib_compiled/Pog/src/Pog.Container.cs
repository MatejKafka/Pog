using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using JetBrains.Annotations;
using Microsoft.PowerShell;

namespace Pog;

// Some notes
//  - according to https://github.com/PowerShell/PowerShell/issues/17617#issuecomment-1173169928,
//    it's not possible to run the container in the same thread as the main runspace runs in
//  - architecturally, it's not possible to accept pipeline input and do live output at the same time (for live output,
//    the main thread must be blocked waiting for output from the container, therefore any previous cmdlets cannot run,
//    so no input can be supplied); fortunately, we don't need any pipeline input to the container, so it works ok
public sealed class Container : IDisposable {
    /// Output streams from the container runspace.
    public PSDataStreams Streams => _ps.Streams;

    private readonly string[] _modules;
    private readonly SessionStateVariableEntry[] _variables;
    private readonly string? _workingDirectory;
    private readonly object? _environmentContext;
    private readonly PowerShell _ps = PowerShell.Create();

    /// <param name="host">
    /// The <see cref="PSHost"/> instance used for the runspace. If null, DefaultHost instance is created.
    /// To read the output streams, use the <see cref="Streams"/> property after <see cref="Invoke"/> was called.
    /// </param>
    /// <param name="streamConfig">Configuration for output stream preference variables.</param>
    /// <param name="modules"></param>
    /// <param name="variables"></param>
    /// <param name="workingDirectory"></param>
    /// <param name="context"></param>
    public Container(PSHost? host, OutputStreamConfig streamConfig, string[]? modules = null,
            SessionStateVariableEntry[]? variables = null, string? workingDirectory = null, object? context = null) {
        _modules = modules ?? [];
        _variables = variables ?? [];
        _workingDirectory = workingDirectory;
        _environmentContext = context;
        _ps.Runspace = GetInitializedRunspace(host, streamConfig);
    }

    /// Invokes <paramref name="setupCommandFn"/> to initialize the invoked command and then invokes it while yielding live
    /// output from the output stream and handling cancellation through <see cref="Stop()"/>.
    /// <remarks>Note that this method can be called multiple times on a single <see cref="Container"/>.</remarks>
    public IEnumerable<PSObject> Invoke(Action<PowerShell> setupCommandFn) {
        // setupCommandFn should set up the script which is then executed by BeginInvoke
        setupCommandFn(_ps);

        try {
            // don't accept any input, write output to `outputCollection`
            using var outputCollection = new PSDataCollection<PSObject>();
            var asyncResult = _ps.BeginInvoke(new PSDataCollection<PSObject>(), outputCollection);
            try {
                foreach (var o in outputCollection) {
                    yield return o;
                }
            } finally {
                _ps.EndInvoke(asyncResult);
            }
        } finally {
            // ensure that the command buffer is clear for repeated invocation
            _ps.Commands.Clear();
        }
    }

    public void Stop() {
        // stop the runspace on Ctrl-C; this works gracefully with `finally` blocks
        // we use .BeginStop instead of .Stop, because .Stop causes a deadlock when the container
        //  is currently reading user input (https://github.com/PowerShell/PowerShell/issues/17633)
        _ps.BeginStop(ar => _ps.EndStop(ar), null);
    }

    public void Dispose() {
        _ps.Runspace?.Dispose();
        _ps.Dispose();
    }

    /// Create a new runspace for the container, configure it and open it.
    private Runspace GetInitializedRunspace(PSHost? host, OutputStreamConfig streamConfig) {
        var iss = CreateInitialSessionState();
        var runspace = host == null ? RunspaceFactory.CreateRunspace(iss) : RunspaceFactory.CreateRunspace(host, iss);

        // if the environment needs a custom working directory, set it
        var workingDir = _workingDirectory;
        if (workingDir != null) {
            // this is a hack, but unfortunately a necessary one: https://github.com/PowerShell/PowerShell/issues/17603
            RunWithWorkingDir(workingDir, () => {
                // run runspace init (module import, variable setup,...)
                // the runspace keeps the changed working directory even after it's reverted back on the process level
                runspace.Open();
            });
        } else {
            // run runspace init (module import, variable setup,...)
            runspace.Open();
        }

        // preference variables must be copied AFTER imports, otherwise we would get a slew
        //  of verbose messages from Import-Module
        CopyPreferenceVariablesToRunspace(runspace, streamConfig);

        return runspace;
    }

    private static readonly object WorkingDirMonitor = new();

    private static void RunWithWorkingDir(string workingDir, Action fn) {
        // mutex, because only single thread at a time can safely change the directory
        lock (WorkingDirMonitor) {
            var originalWorkingDirectory = Environment.CurrentDirectory;
            try {
                // temporarily override the working directory
                Environment.CurrentDirectory = workingDir;

                fn();
            } finally {
                Environment.CurrentDirectory = originalWorkingDirectory;
            }
        }
    }

    private InitialSessionState CreateInitialSessionState() {
        var iss = InitialSessionState.CreateDefault2();
        iss.ThreadOptions = PSThreadOptions.UseNewThread;
        iss.ThrowOnRunspaceOpenError = true;
        iss.ExecutionPolicy = ExecutionPolicy.Bypass;

        iss.Variables.Add(new SessionStateVariableEntry[] {
            // block automatic module loading to isolate the configuration script from system packages
            // this allows for a more consistent environment between different machines
            new("PSModuleAutoLoadingPreference", "None", "", ScopedItemOptions.AllScope),
            // stop on any (even non-terminating) error, override the preference variable defined above
            new("ErrorActionPreference", "Stop", ""),
        });

        iss.ImportPSModule([
            // these two imports contain basic stuff needed for printing output, errors, FS traversal,...
            "Microsoft.PowerShell.Management",
            "Microsoft.PowerShell.Utility",
        ]);

        // TODO: figure out if we can define this without having to write inline PowerShell function
        // override Import-Module to hide the default verbose prints when -Verbose is set for the container environment
        // $VerbosePreference is set globally, so we'd need to overwrite it, and then set it back,
        //  as it interacts weirdly with -Verbose:$false, which apparently doesn't work here for some reason;
        //  it seems as the cleanest solution to do `4>$null`, which just hides the Verbose stream altogether
        iss.Commands.Add(new SessionStateFunctionEntry("Import-Module",
                @"Microsoft.PowerShell.Core\Import-Module @Args 4>$null"));

        // setup environment-specific modules and variables
        iss.ImportPSModule(_modules);
        iss.Variables.Add(_variables);

        // if the environment uses an internal context, set it
        if (_environmentContext != null) {
            iss.Variables.Add(new SessionStateVariableEntry(EnvContextVarName, _environmentContext,
                    "Internal context used by the Pog container environment", ScopedItemOptions.Constant));
        }

        return iss;
    }

    private static void CopyPreferenceVariablesToRunspace(Runspace rs, OutputStreamConfig streamConfig) {
        void Set<T>(string name, T? value) {
            if (value == null) return;
            rs.SessionStateProxy.SetVariable(name, value);
        }

        Set("ProgressPreference", streamConfig.Progress);
        Set("WarningPreference", streamConfig.Warning);
        Set("InformationPreference", streamConfig.Information);
        Set("VerbosePreference", streamConfig.Verbose);
        Set("DebugPreference", streamConfig.Debug);
        Set("ConfirmPreference", streamConfig.Confirm);
    }

    private const string EnvContextVarName = "_PogInternalContext";

    public class EnvironmentContext<TDerived> where TDerived : EnvironmentContext<TDerived> {
        /// Retrieves the instance of <see cref="EnvironmentContext{TDerived}"/> associated with this container (PowerShell runspace).
        /// <exception cref="InvalidOperationException">No valid <see cref="EnvironmentContext{TDerived}"/> instance is associated with this container.</exception>
        public static TDerived GetCurrent(PSCmdlet callingCmdlet) {
            var containerContextVar = callingCmdlet.SessionState.PSVariable.Get("global:" + EnvContextVarName);
            if (containerContextVar == null) {
                throw new InvalidOperationException(
                        $"${EnvContextVarName} variable is missing, Pog package manifests must be executed inside the Pog environment.");
            }
            if (containerContextVar.Value is not TDerived containerContext) {
                throw new InvalidOperationException($"${EnvContextVarName} is not of type {nameof(TDerived)}");
            }
            return containerContext;
        }
    }

    [PublicAPI]
    public record struct OutputStreamConfig(
            ActionPreference? Progress,
            ActionPreference? Warning,
            ActionPreference? Information,
            ActionPreference? Verbose,
            ActionPreference? Debug,
            ConfirmImpact? Confirm) {
        // PowerShell does not give us any simple way to just ask for the effective value of a preference variable
        // instead, we have to effectively reimplement the algorithm PowerShell uses internally; fun
        private static T? GetPreferenceVariableValue<T>(PSCmdlet cmdlet, string varName, string? paramName,
                Func<object, T>? mapParam) where T : struct {
            if (paramName != null && mapParam != null &&
                cmdlet.MyInvocation.BoundParameters.TryGetValue(paramName, out var obj)) {
                return mapParam(obj);
            } else {
                var parentVar = cmdlet.SessionState.PSVariable.Get(varName); // get var from parent scope
                return parentVar?.Value switch {
                    null => null,
                    T v => v,
                    // https://github.com/PowerShell/PowerShell/issues/3483
                    var v => Enum.TryParse(v.ToString(), out T pref) ? pref : null,
                };
            }
        }

        public static OutputStreamConfig FromCmdletPreferenceVariables(PSCmdlet cmdlet) {
            var config = new OutputStreamConfig(
                    GetPreferenceVariableValue<ActionPreference>(cmdlet, "ProgressPreference", null, null),
                    GetPreferenceVariableValue(cmdlet, "WarningPreference", "WarningAction",
                            param => (ActionPreference) param),
                    GetPreferenceVariableValue(cmdlet, "InformationPreference", "InformationAction",
                            param => (ActionPreference) param),
                    GetPreferenceVariableValue(cmdlet, "VerbosePreference", "Verbose",
                            param => (SwitchParameter) param
                                    ? ActionPreference.Continue
                                    : ActionPreference.SilentlyContinue),
                    GetPreferenceVariableValue(cmdlet, "DebugPreference", "Debug",
                            param => (SwitchParameter) param
                                    ? ActionPreference.Continue
                                    : ActionPreference.SilentlyContinue),
                    GetPreferenceVariableValue(cmdlet, "ConfirmPreference", "Confirm",
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
}
