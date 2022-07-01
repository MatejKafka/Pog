using System;
using System.Collections;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.PowerShell;

namespace Pog;

public enum ContainerType { Install, GetInstallHash, Enable }

[PublicAPI]
public record ContainerInternalInfo(
        string PackageName, string PackageDirectory, Hashtable Manifest, Hashtable InternalArguments);

[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "CompiledContainer")]
public class InvokeContainerCommand : PSCmdlet, IDisposable {
    private static readonly string ContainerDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, @"..\container"));

    [Parameter(Mandatory = true, Position = 0)] public ContainerType ContainerType;
    [Parameter(Mandatory = true, Position = 1)] public Package Package = null!;
    [Parameter(Mandatory = true)] public Hashtable Manifest = null!;
    [Parameter(Mandatory = true)] public Hashtable InternalArguments = null!;
    [Parameter(Mandatory = true)] public Hashtable PackageArguments = null!;

    private readonly PowerShell _ps = PowerShell.Create();

    protected override void BeginProcessing() {
        base.BeginProcessing();

        // Manifest["Enable"] = (Manifest["Enable"] as ScriptBlock)!.GetNewClosure();

        var outputCollection = new PSDataCollection<PSObject>();
        outputCollection.DataAdded += delegate {
            foreach (var o in outputCollection.ReadAll()) {
                WriteObject(o);
            }
        };

        var runspace = RunspaceFactory.CreateRunspace(Host, CreateInitialSessionState()); // reuse our Host
        // set the working directory
        // this is a hack, but unfortunately a necessary one: https://github.com/PowerShell/PowerShell/issues/17603
        var originalWorkingDirectory = Environment.CurrentDirectory;
        try {
            // temporarily override the working directory
            Environment.CurrentDirectory = Package.Path;
            // run runspace init (module import, variable setup,...)
            // the runspace keeps the changed working directory even after it's reverted back on the process level
            runspace.Open();
        } finally {
            Environment.CurrentDirectory = originalWorkingDirectory;
        }

        // preference variables must be copied AFTER imports, otherwise we would get a slew
        //  of verbose messages from Import-Module
        CopyPreferenceVariablesToRunspace(runspace);

        // if debug prints are active, also activate verbose prints
        // TODO: this is quite convenient, but it kinda goes against the original intended use of these variables, is it really a good idea?
        if ((ActionPreference) runspace.SessionStateProxy.GetVariable("DebugPreference") == ActionPreference.Continue) {
            runspace.SessionStateProxy.SetVariable("VerbosePreference", "Continue");
        }
        if ((ActionPreference) runspace.SessionStateProxy.GetVariable("VerbosePreference") == ActionPreference.Continue) {
            runspace.SessionStateProxy.SetVariable("InformationPreference", "Continue");
        }

        _ps.Runspace = runspace;
        // __main should be exported by each container environment
        _ps.AddScript(@"
            try {
                __main @Args
            } finally {
                Write-Debug 'Cleaning up...'
                __cleanup
                Write-Debug 'Cleanup finished.'
            }
        ").AddArgument(Manifest).AddArgument(PackageArguments);
        // don't accept any input, write output to `outputCollection`
        _ps.Invoke(null, outputCollection, new PSInvocationSettings());

        // _ps.AddCommand("__main").AddArgument(Manifest).AddArgument(PackageArguments);
        // try {
        //     // don't accept any input, write output to `outputCollection`
        //     _ps.Invoke(null, outputCollection, new PSInvocationSettings());
        // } finally {
        //     WriteDebug("Cleaning up...");
        //     _ps.Commands.Clear();
        //     _ps.AddCommand("__cleanup");
        //     _ps.Invoke(null, outputCollection, new PSInvocationSettings());
        //     WriteDebug("Cleanup finished.");
        // }
    }

    // relevant preference variables, which are copied to the container (see `man about_Preference_Variables`)
    private readonly (string name, string? paramName, Func<object, object>? mapParam)[] _copiedPreferenceVariables = {
        // ErrorAction is skipped, as we always set it to "Stop"
        // ("ErrorActionPreference", "ErrorAction", param => param),
        ("ProgressPreference", null, null),
        ("ConfirmPreference", "Confirm", param => ((SwitchParameter) param) ? ConfirmImpact.Low : ConfirmImpact.High),
        // keep Debug, Verbose and Information in this order, to allow overriding in CopyPreferenceVariablesToIss
        ("DebugPreference", "Debug",
                param => ((SwitchParameter) param) ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
        ("VerbosePreference", "Verbose",
                param => ((SwitchParameter) param) ? ActionPreference.Continue : ActionPreference.SilentlyContinue),
        ("InformationPreference", "InformationAction", param => param),
        ("WarningPreference", "WarningAction", param => param),
    };

    private void CopyPreferenceVariablesToRunspace(Runspace rs) {
        foreach (var (varName, paramName, mapParam) in _copiedPreferenceVariables) {
            var parentVar = SessionState.PSVariable.Get(varName); // get var from parent scope
            var value = paramName != null && mapParam != null &&
                        MyInvocation.BoundParameters.TryGetValue(paramName, out var obj)
                    ? mapParam(obj) // map the passed parameter
                    : parentVar.Value; // use the preference variable value from the parent scope
            rs.SessionStateProxy.SetVariable(parentVar.Name, value);
        }
    }

    // private void CopyPreferenceVariablesToIss(InitialSessionState iss) {
    //     // TODO: consider re-adding automatic enable for -Verbose when -Debug is set
    //     //  and for InformationPreference when -Verbose is set
    //     iss.Variables.Add(_copiedPreferenceVariables.Select(variable => {
    //         var p = SessionState.PSVariable.Get(variable.name); // get var from parent scope
    //         var value = variable.paramName != null && variable.mapParam != null &&
    //                     MyInvocation.BoundParameters.TryGetValue(variable.paramName, out var obj)
    //                 ? variable.mapParam(obj) // map the passed parameter
    //                 : p.Value; // use the preference variable value from the parent scope
    //         return new SessionStateVariableEntry(p.Name, value, p.Description, p.Options, p.Attributes);
    //     }));
    // }

    private InitialSessionState CreateInitialSessionState() {
        var iss = InitialSessionState.CreateDefault2();
        iss.ThreadOptions = PSThreadOptions.UseCurrentThread;
        iss.ThrowOnRunspaceOpenError = true;
        iss.ExecutionPolicy = ExecutionPolicy.Bypass;

        iss.Variables.Add(new SessionStateVariableEntry[] {
            // block automatic module loading to isolate the configuration script from system packages
            // this allows for a more consistent environment between different machines
            new("PSModuleAutoLoadingPreference", "None", "", ScopedItemOptions.AllScope),
            // stop on any (even non-terminating) error, override the preference variable defined above
            new("ErrorActionPreference", "Stop", ""),

            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            new("this", Manifest, "Loaded manifest of the processed package"),
            // store internal Pog data in the _Pog variable inside the container, used by the environments imported below
            new("_Pog", new ContainerInternalInfo(Package.PackageName, Package.Path, Manifest, InternalArguments),
                    "Internal data used by the Pog container environment", ScopedItemOptions.Constant),
        });

        iss.ImportPSModule(new[] {
            // these two imports contain basic stuff needed for printing output, errors, FS traversal,...
            "Microsoft.PowerShell.Management",
            "Microsoft.PowerShell.Utility",
            // setup environment for package manifest script
            // each environment module must provide functions `__main` and `__cleanup`
            ContainerType switch {
                ContainerType.Enable => Path.Combine(ContainerDir, @"Enable\Env_Enable.psm1"),
                ContainerType.Install => Path.Combine(ContainerDir, @"Install\Env_Install.psm1"),
                ContainerType.GetInstallHash => Path.Combine(ContainerDir, @"Install\Env_GetInstallHash.psm1"),
                _ => throw new ArgumentOutOfRangeException(nameof(ContainerType), ContainerType, null),
            }
        });

        // TODO: figure out if we can define this without having to write inline PowerShell function
        // override Import-Module to hide the default verbose prints when -Verbose is set for the container environment
        // $VerbosePreference is set globally, so we'd need to overwrite it, and then set it back,
        //  as it interacts weirdly with -Verbose:$false, which apparently doesn't work here for some reason;
        //  it seems as the cleanest solution to do `4>$null`, which just hides the Verbose stream altogether
        iss.Commands.Add(new SessionStateFunctionEntry("Import-Module", @"
            Microsoft.PowerShell.Core\Import-Module @Args 4>$null
        "));

        return iss;
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        // stop the runspace on Ctrl-C; this works gracefully with `finally` blocks
        _ps.Stop();
    }

    public void Dispose() {
        _ps.Runspace?.Dispose();
        _ps.Dispose();
    }
}