using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.PowerShell;
using Pog.Utils;

namespace Pog;

/// Internal host UI implementation that blocks all output streams other than Write-Host logs
/// but lets direct host interactions through to the real host UI.
internal sealed class ContainerHostUserInterface(PSHostUserInterface ui) : PSHostUserInterface {
    public override PSHostRawUserInterface RawUI => ui.RawUI;
    public override bool SupportsVirtualTerminal => ui.SupportsVirtualTerminal;

    public override Dictionary<string, PSObject> Prompt(string caption, string message,
            Collection<FieldDescription> descriptions)
        => ui.Prompt(caption, message, descriptions);

    public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices,
            int defaultChoice)
        => ui.PromptForChoice(caption, message, choices, defaultChoice);

    public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName,
            PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        => ui.PromptForCredential(caption, message, userName, targetName, allowedCredentialTypes, options);

    public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
        => ui.PromptForCredential(caption, message, userName, targetName);

    public override string ReadLine() => ui.ReadLine();
    public override SecureString ReadLineAsSecureString() => ui.ReadLineAsSecureString();

    public override void Write(string value) => ui.Write(value);

    public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        => ui.Write(foregroundColor, backgroundColor, value);

    public override void WriteLine(string value) {
        // see `WriteInformation` for context
        if (value != _nextBlockedInformationMessage) {
            ui.WriteLine(value);
        }
        _nextBlockedInformationMessage = null;
    }

    public override void WriteLine() => ui.WriteLine();

    public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        => ui.WriteLine(foregroundColor, backgroundColor, value);

    public override void WriteProgress(long sourceId, ProgressRecord record) => ui.WriteProgress(sourceId, record);

    public override void WriteDebugLine(string message) {}
    public override void WriteErrorLine(string value) {}
    public override void WriteVerboseLine(string message) {}
    public override void WriteWarningLine(string message) {}

    private string? _nextBlockedInformationMessage = null;

    public override void WriteInformation(InformationRecord record) {
        // unfortunately, pwsh calls both WriteInformation and WriteLine (in this order) and the built-in hosts
        //  ignore `WriteInformation` calls so prefixing the record here doesn't really do anything
        // since we want to pass Write-Host calls unaltered and intercept Write-Information, we need to leave the WriteLine
        //  call as-is for Write-Host but block it for Write-Information; to achieve that, we'll set a flag here and check
        //  it in `WriteLine`
        if (record.IsHostRecord()) {
            // Write-Host log
            _nextBlockedInformationMessage = null;
            // this log is not blocked, forward it to the actual host UI
            ui.WriteInformation(record);
        } else {
            // this is an information log, not a Write-Host log, block it
            // note that this may falsely block a message if `$InformationPreference` is set to hide information logs
            //  and the user manually calls `$Host.UI.PrintLine` with the same message; I don't see any way to work around
            //  that, other than inspecting the caller through a stack trace, which is both fragile and slow
            _nextBlockedInformationMessage = record.ToString();
        }
    }
}

/// Internal host implementation to inject the custom host UI above.
internal sealed class ContainerHost(PSHost host) : PSHost {
    public override Guid InstanceId {get;} = Guid.NewGuid();
    public override PSHostUserInterface UI {get;} = new ContainerHostUserInterface(host.UI);

    // ignore exit requests from the container
    public override void SetShouldExit(int exitCode) {}

    public override void EnterNestedPrompt() => host.EnterNestedPrompt();
    public override void ExitNestedPrompt() => host.ExitNestedPrompt();
    public override void NotifyBeginApplication() => host.NotifyBeginApplication();
    public override void NotifyEndApplication() => host.NotifyEndApplication();

    public override CultureInfo CurrentCulture => host.CurrentCulture;
    public override CultureInfo CurrentUICulture => host.CurrentUICulture;
    public override string Name => host.Name;
    public override Version Version => host.Version;
}

public record struct ContainerOutput(bool PipelineOutput, object Output);

// Some notes
//  - according to https://github.com/PowerShell/PowerShell/issues/17617#issuecomment-1173169928,
//    it's not possible to run the container in the same thread as the main runspace runs in
//  - architecturally, it's not possible to accept pipeline input and do live output at the same time (for live output,
//    the main thread must be blocked waiting for output from the container, therefore any previous cmdlets cannot run,
//    so no input can be supplied); fortunately, we don't need any pipeline input to the container, so it works ok
public sealed class Container : IDisposable {
    private readonly string[] _modules;
    private readonly SessionStateVariableEntry[] _variables;
    private readonly string? _workingDirectory;
    private readonly object? _environmentContext;
    private readonly Runspace _runspace;

    /// <param name="host">
    /// The <see cref="PSHost"/> instance used for the runspace. If null, DefaultHost instance is created.
    /// </param>
    /// <param name="streamConfig">Configuration for output stream preference variables.</param>
    /// <param name="modules"></param>
    /// <param name="variables"></param>
    /// <param name="workingDirectory"></param>
    /// <param name="context"></param>
    public Container(PSHost? host = null, OutputStreamConfig streamConfig = new(), string[]? modules = null,
            SessionStateVariableEntry[]? variables = null, string? workingDirectory = null, object? context = null) {
        _modules = modules ?? [];
        _variables = variables ?? [];
        _workingDirectory = workingDirectory;
        _environmentContext = context;
        // reuse a single runspace for all invocations
        _runspace = GetInitializedRunspace(host == null ? null : new ContainerHost(host), streamConfig);
    }

    public void Dispose() => _runspace.Dispose();

    /// Invokes <paramref name="cmd"/> while yielding combined live output from all output streams.
    /// <remarks>
    /// Note that this method can be called multiple times on a single <see cref="Container"/> but it must not be called
    /// concurrently.
    /// </remarks>
    /// <exception cref="PipelineStoppedException">Thrown when the execution is canceled.</exception>
    public IEnumerable<ContainerOutput> Invoke(PSCommand cmd, CancellationToken token = default) {
        using var ps = PowerShell.Create();
        ps.Runspace = _runspace;
        ps.Commands = cmd;

        using var outputCollection = new PSDataCollection<PSObject>();
        using var mergedOutput = new BlockingCollection<ContainerOutput>();

        outputCollection.DataAdded += GetOutputCallback<PSObject>(mergedOutput, r => new(true, r));
        ps.Streams.Error.DataAdded += GetOutputCallback<ErrorRecord>(mergedOutput, r => new(false, r));
        ps.Streams.Warning.DataAdded += GetOutputCallback<WarningRecord>(mergedOutput, r => new(false, r));
        ps.Streams.Verbose.DataAdded += GetOutputCallback<VerboseRecord>(mergedOutput, r => new(false, r));
        ps.Streams.Debug.DataAdded += GetOutputCallback<DebugRecord>(mergedOutput, r => new(false, r));
        ps.Streams.Information.DataAdded += GetOutputCallback<InformationRecord>(mergedOutput,
                record => record.IsHostRecord() ? null : new(false, record));

        // don't provide any input, write output to `outputCollection`
        var asyncResult = ps.BeginInvoke<PSObject, PSObject>(null, outputCollection);

        // enable cancellation after `BeginInvoke` is called, otherwise it might arrive before execution starts
        using var cancellation = token.Register(() => {
            // gracefully stop the invocation; use .BeginStop instead of .Stop, because .Stop causes a deadlock when the
            //  container is currently reading user input (https://github.com/PowerShell/PowerShell/issues/17633)
            ps.BeginStop(ps.EndStop, null);
        });

        // ReSharper disable once MethodSupportsCancellation (the queue needs to be closed even if cancelled)
        var runTask = Task.Factory.FromAsync(asyncResult, ps.EndInvoke).ContinueWith(t => {
            mergedOutput.CompleteAdding();
            t.GetAwaiter().GetResult(); // propagate exceptions
        });

        // FIXME: currently, calling `Invoke` doesn't do anything until the caller starts iterating; it would be a bit nicer
        //  if `.BeginInvoke` was called immediately so that, e.g., canceling after calling `Invoke` works nicer
        foreach (var o in mergedOutput.GetConsumingEnumerable()) {
            yield return o;
        }

        // propagate any exceptions from the invocation
        runTask.GetAwaiter().GetResult();
    }

    private static EventHandler<DataAddedEventArgs> GetOutputCallback<T>(
            BlockingCollection<ContainerOutput> output, Func<T, ContainerOutput?> map) {
        return (sender, e) => {
            var collection = (PSDataCollection<T>) sender;
            var record = collection[e.Index];
            collection.RemoveAt(e.Index);
            if (map(record) is {} o) {
                output.Add(o);
            }
        };
    }

    /// Create a new runspace for the container, configure it and open it.
    private Runspace GetInitializedRunspace(PSHost? host, OutputStreamConfig streamConfig) {
        var iss = CreateInitialSessionState();
        var runspace = host == null ? RunspaceFactory.CreateRunspace(iss) : RunspaceFactory.CreateRunspace(host, iss);

        // if the environment needs a custom working directory, set it
        var workingDir = _workingDirectory;
        if (workingDir != null) {
            // this is a hack, but unfortunately a necessary one: https://github.com/PowerShell/PowerShell/issues/17603
            // run runspace init (module import, variable setup,...) with a changed working directory
            // the runspace keeps the changed working directory even after it's reverted back on the process level
            RunWithWorkingDir(workingDir, runspace.Open);
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
        iss.ThreadOptions = PSThreadOptions.ReuseThread;
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
