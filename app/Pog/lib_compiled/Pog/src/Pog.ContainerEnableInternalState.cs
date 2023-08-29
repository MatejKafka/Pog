using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

/// Record representing internal state of the Env_Enable environment.
[PublicAPI]
public class ContainerEnableInternalState {
    private const string StateVariableName = "global:_PogContainerState";

    /// Set of all shortcuts that were not "refreshed" during this `Enable-Pog` call.
    /// Starts with all shortcuts found in package, and each time `Export-Shortcut` is called, it is removed from the set.
    /// before end of Enable, all shortcuts still in this set are deleted.
    public readonly HashSet<string> StaleShortcuts;
    /// <see cref="StaleShortcuts"/>
    public readonly HashSet<string> StaleCommands;
    /// <see cref="StaleShortcuts"/>
    public readonly HashSet<string> StaleShortcutStubs;

    private ContainerEnableInternalState(IEnumerable<string> shortcuts, IEnumerable<string> commands,
            IEnumerable<string> shortcutStubs) {
        StaleShortcuts = new HashSet<string>(shortcuts);
        StaleCommands = new HashSet<string>(commands);
        StaleShortcutStubs = new HashSet<string>(shortcutStubs);
    }

    /// Creates a new `ContainerEnableInternalState` instance and stores it as a global variable in the current container instance.
    public static ContainerEnableInternalState InitCurrent(PSCmdlet callingCmdlet, ImportedPackage enabledPackage) {
        var state = new ContainerEnableInternalState(
                enabledPackage.EnumerateExportedShortcuts().Select(f => f.FullName),
                enabledPackage.EnumerateExportedCommands().Select(f => f.FullName),
                enabledPackage.EnumerateShortcutStubs().Select(f => f.FullName));
        SetCurrent(callingCmdlet, state);
        return state;
    }

    private static void SetCurrent(PSCmdlet callingCmdlet, ContainerEnableInternalState state) {
        callingCmdlet.SessionState.PSVariable.Set(new PSVariable(StateVariableName, state, ScopedItemOptions.Constant));
    }

    /// Retrieves the instance of `ContainerEnableInternalState` associated with this container (PowerShell runspace).
    /// <exception cref="InvalidOperationException">No valid `ContainerEnableInternalState` instance is associated with this container.</exception>
    public static ContainerEnableInternalState GetCurrent(PSCmdlet callingCmdlet) {
        var containerStateVar = callingCmdlet.SessionState.PSVariable.Get(StateVariableName);
        if (containerStateVar == null) {
            throw new InvalidOperationException(
                    $"${StateVariableName} variable is missing, Pog package manifests must be executed inside the Pog environment.");
        }
        if (containerStateVar.Value is not ContainerEnableInternalState containerState) {
            throw new InvalidOperationException(
                    $"${StateVariableName} is not of type {nameof(ContainerEnableInternalState)}");
        }
        return containerState;
    }
}
