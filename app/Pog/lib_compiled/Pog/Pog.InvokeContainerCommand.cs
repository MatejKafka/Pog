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
        string PackageName, string PackageDirectory, PackageManifest Manifest, Hashtable InternalArguments);

// Some notes
//  - according to https://github.com/PowerShell/PowerShell/issues/17617#issuecomment-1173169928,
//    it's not possible to run the container in the same thread as this cmdlet was ran in
//  - architecturally, it's not possible to accept pipeline input and do live output at the same time (for live output,
//    the main thread must be blocked waiting for output from the container, therefore any previous cmdlets cannot run,
//    so no input can be supplied); fortunately, we don't need any pipeline input to the container, so it works ok
[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "Container")]
public class InvokeContainerCommand : PSCmdlet, IDisposable {
    [Parameter(Mandatory = true, Position = 0)] public ContainerType ContainerType;
    [Parameter(Mandatory = true, Position = 1)] public Package Package = null!;
    [Parameter(Mandatory = true)] public PackageManifest Manifest = null!;
    [Parameter(Mandatory = true)] public Hashtable InternalArguments = null!;
    [Parameter] public Hashtable PackageArguments = new();

    private static readonly string ContainerDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, @"..\container"));

    private readonly PowerShell _ps = PowerShell.Create();

    protected override void BeginProcessing() {
        base.BeginProcessing();

        _ps.Runspace = GetInitializedRunspace();

        // __main and __cleanup should be exported by each container environment
        // the `finally` block is called even on exit
        _ps.AddScript(@"
            Set-StrictMode -Version Latest
            try {
                __main @Args
            } finally {
                Write-Debug 'Cleaning up...'
                __cleanup
                Write-Debug 'Cleanup finished.'
            }
        ").AddArgument(Manifest.Raw).AddArgument(PackageArguments);

        var outputCollection = new PSDataCollection<PSObject>();
        // don't accept any input, write output to `outputCollection`
        // FIXME: mandatory parameter prompt gets stuck on Ctrl-C
        var async = _ps.BeginInvoke(new PSDataCollection<PSObject>(), outputCollection);
        foreach (var o in outputCollection) {
            WriteObject(o);
        }
        _ps.EndInvoke(async);
    }

    /// Create a new runspace for the container, configure it and open it.
    private Runspace GetInitializedRunspace() {
        // reuse our Host ---------------------------\/
        var runspace = RunspaceFactory.CreateRunspace(Host, CreateInitialSessionState());
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
        return runspace;
    }

    private void CopyPreferenceVariablesToRunspace(Runspace rs) {
        foreach (var (varName, paramName, mapParam) in CopiedPreferenceVariables) {
            var parentVar = SessionState.PSVariable.Get(varName); // get var from parent scope
            var value = paramName != null && mapParam != null &&
                        MyInvocation.BoundParameters.TryGetValue(paramName, out var obj)
                    ? mapParam(obj) // map the passed parameter
                    : parentVar.Value; // use the preference variable value from the parent scope
            rs.SessionStateProxy.SetVariable(parentVar.Name, value);
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

            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            new("this", Manifest.Raw, "Loaded manifest of the processed package"),
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

    /// Relevant preference variables, which are copied to the container. See `man about_Preference_Variables`.
    private static readonly (string name, string? paramName, Func<object, object>? mapParam)[] CopiedPreferenceVariables = {
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
}