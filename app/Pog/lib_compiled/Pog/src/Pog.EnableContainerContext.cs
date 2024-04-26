using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public class EnableContainerContext : Container.EnvironmentContext<EnableContainerContext> {
    /// Set of all shortcuts that were not "refreshed" during this `Enable-Pog` call.
    /// Starts with all shortcuts found in package, and each time `Export-Shortcut` is called, it is removed from the set.
    /// before end of Enable, all shortcuts still in this set are deleted.
    public readonly HashSet<string> StaleShortcuts;
    /// <see cref="StaleShortcuts"/>
    public readonly HashSet<string> StaleCommands;
    /// <see cref="StaleShortcuts"/>
    public readonly HashSet<string> StaleShortcutStubs;

    internal EnableContainerContext(ImportedPackage enabledPackage) {
        StaleShortcuts = [..enabledPackage.EnumerateExportedShortcuts().Select(f => f.FullName)];
        StaleCommands = [..enabledPackage.EnumerateExportedCommands().Select(f => f.FullName)];
        StaleShortcutStubs = [..enabledPackage.EnumerateShortcutStubs().Select(f => f.FullName)];
    }
}
