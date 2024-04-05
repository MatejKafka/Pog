using System;
using System.Collections;
using System.Diagnostics;
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

    private readonly PowerShell _ps = PowerShell.Create();
    private readonly ContainerType _containerType;
    private readonly Package _package;
    private readonly Hashtable _internalArguments;
    private readonly Hashtable _packageArguments;

    /// <param name="containerType">Which environment to set up in the container.</param>
    /// <param name="package"></param>
    /// <param name="internalArguments"></param>
    /// <param name="packageArguments"></param>
    /// <param name="host">
    /// The <see cref="PSHost"/> instance used for the runspace. If null, DefaultHost instance is created.
    /// To read the output streams, use the <see cref="Streams"/> property after <see cref="BeginInvoke"/> was called.
    /// </param>
    /// <param name="streamConfig">Configuration for output stream preference variables.</param>
    public Container(ContainerType containerType, Package package,
            Hashtable? internalArguments, Hashtable? packageArguments,
            PSHost? host, OutputStreamConfig streamConfig) {
        _containerType = containerType;
        _package = package;
        // TODO: validate that internalArguments match containerType
        _internalArguments = internalArguments ?? new Hashtable();
        _packageArguments = packageArguments ?? new Hashtable();

        _ps.Runspace = GetInitializedRunspace(host, streamConfig);
    }

    // __main and __cleanup should be exported by each container environment
    // the `finally` block is called even on exit
    private const string ContainerInvokeSbStr =
            """
            try {
                __main @Args
            } finally {
                Write-Debug 'Cleaning up...'
                try {
                    __cleanup
                    Write-Debug 'Cleanup finished.'
                } catch {
                    # don't throw, we'd lose the original exception
                    Write-Warning ('Cleanup failed: ' + $_)
                }
            }
            """;

    public IAsyncResult BeginInvoke(PSDataCollection<PSObject> outputCollection) {
        _ps.AddScript(ContainerInvokeSbStr).AddArgument(_package.Manifest).AddArgument(_packageArguments);
        // don't accept any input, write output to `outputCollection`
        return _ps.BeginInvoke(new PSDataCollection<PSObject>(), outputCollection);
    }

    public void EndInvoke(IAsyncResult asyncResult) {
        _ps.EndInvoke(asyncResult);
    }

    public void Stop() {
        // stop the runspace on Ctrl-C; this works gracefully with `finally` blocks
        // we use .BeginStop instead of .Stop, because .Stop causes a deadlock when the container
        //  is currently reading user input (not sure why)
        // ideally, we should probably call .EndStop somewhere, but we don't really need to know about the completion
        _ps.BeginStop(null, null);
    }

    public void Dispose() {
        _ps.Runspace?.Dispose();
        _ps.Dispose();
    }

    /// Create a new runspace for the container, configure it and open it.
    private Runspace GetInitializedRunspace(PSHost? host, OutputStreamConfig streamConfig) {
        var iss = CreateInitialSessionState();
        var runspace = host == null ? RunspaceFactory.CreateRunspace(iss) : RunspaceFactory.CreateRunspace(host, iss);

        if (_package is ImportedPackage ip) {
            // set the working directory
            // this is a hack, but unfortunately a necessary one: https://github.com/PowerShell/PowerShell/issues/17603
            // TODO: add a mutex here in case multiple containers are started in parallel
            var originalWorkingDirectory = Environment.CurrentDirectory;
            try {
                // temporarily override the working directory
                Environment.CurrentDirectory = ip.Path;
                // run runspace init (module import, variable setup,...)
                // the runspace keeps the changed working directory even after it's reverted back on the process level
                runspace.Open();
            } finally {
                Environment.CurrentDirectory = originalWorkingDirectory;
            }
        } else {
            // not an imported package, do not change working directory

            // only GetInstallHash container should be created for repository packages
            Debug.Assert(_containerType == ContainerType.GetInstallHash);

            // run runspace init (module import, variable setup,...)
            runspace.Open();
        }

        // preference variables must be copied AFTER imports, otherwise we would get a slew
        //  of verbose messages from Import-Module
        CopyPreferenceVariablesToRunspace(runspace, streamConfig);

        return runspace;
    }

    private static void CopyPreferenceVariablesToRunspace(Runspace rs, OutputStreamConfig streamConfig) {
        rs.SessionStateProxy.SetVariable("ProgressPreference", streamConfig.Progress);
        rs.SessionStateProxy.SetVariable("WarningPreference", streamConfig.Warning);
        rs.SessionStateProxy.SetVariable("InformationPreference", streamConfig.Information);
        rs.SessionStateProxy.SetVariable("VerbosePreference", streamConfig.Verbose);
        rs.SessionStateProxy.SetVariable("DebugPreference", streamConfig.Debug);
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
            new("this", _package.Manifest.Raw, "Loaded manifest of the processed package"),
            // store internal Pog data in the _Pog variable inside the container, used by the environments imported below
            new("_Pog", new ContainerInternalInfo(_package, _internalArguments),
                    "Internal data used by the Pog container environment", ScopedItemOptions.Constant),
        });

        var containerDir = InternalState.PathConfig.ContainerDir;
        iss.ImportPSModule([
            // these two imports contain basic stuff needed for printing output, errors, FS traversal,...
            "Microsoft.PowerShell.Management",
            "Microsoft.PowerShell.Utility",
            // setup environment for package manifest script
            // each environment module must provide functions `__main` and `__cleanup`
            _containerType switch {
                ContainerType.Enable => $@"{containerDir}\Enable\Env_Enable.psm1",
                ContainerType.Disable => $@"{containerDir}\Enable\Env_Disable.psm1",
                ContainerType.Install => $@"{containerDir}\Install\Env_Install.psm1",
                ContainerType.GetInstallHash => $@"{containerDir}\Install\Env_GetInstallHash.psm1",
                _ => throw new ArgumentOutOfRangeException(nameof(_containerType), _containerType, null),
            },
        ]);

        // TODO: figure out if we can define this without having to write inline PowerShell function
        // override Import-Module to hide the default verbose prints when -Verbose is set for the container environment
        // $VerbosePreference is set globally, so we'd need to overwrite it, and then set it back,
        //  as it interacts weirdly with -Verbose:$false, which apparently doesn't work here for some reason;
        //  it seems as the cleanest solution to do `4>$null`, which just hides the Verbose stream altogether
        iss.Commands.Add(new SessionStateFunctionEntry("Import-Module",
                @"Microsoft.PowerShell.Core\Import-Module @Args 4>$null"));

        return iss;
    }

    /// Enum of supported container environments.
    public enum ContainerType { Install, GetInstallHash, Enable, Disable }

    [PublicAPI]
    public record OutputStreamConfig(
            ActionPreference Progress,
            ActionPreference Warning,
            ActionPreference Information,
            ActionPreference Verbose,
            ActionPreference Debug) {
        public ActionPreference Progress = Progress;
        public ActionPreference Warning = Warning;
        public ActionPreference Information = Information;
        public ActionPreference Verbose = Verbose;
        public ActionPreference Debug = Debug;
    }

    [PublicAPI]
    public record ContainerInternalInfo(Package Package, Hashtable InternalArguments) {
        private const string StateVariableName = "global:_Pog";

        /// Retrieves the instance of `ContainerInternalInfo` associated with this container (PowerShell runspace).
        /// <exception cref="InvalidOperationException">No valid `ContainerInternalInfo` instance is associated with this container.</exception>
        public static ContainerInternalInfo GetCurrent(PSCmdlet callingCmdlet) {
            var containerStateVar = callingCmdlet.SessionState.PSVariable.Get(StateVariableName);
            if (containerStateVar == null) {
                throw new InvalidOperationException(
                        $"${StateVariableName} variable is missing, Pog package manifests must be executed inside the Pog environment.");
            }
            if (containerStateVar.Value is not ContainerInternalInfo containerState) {
                throw new InvalidOperationException(
                        $"${StateVariableName} is not of type {nameof(ContainerInternalInfo)}");
            }
            return containerState;
        }
    }
}
