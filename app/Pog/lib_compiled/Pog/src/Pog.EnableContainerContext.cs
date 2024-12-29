using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public class EnableContainerContext : Container.EnvironmentContext<EnableContainerContext> {
    public readonly ImportedPackage Package;
    /// Set of all shortcuts that were not "refreshed" during this `Enable-Pog` call.
    /// Starts with all shortcuts found in package, and each time `Export-Shortcut` is called, it is removed from the set.
    /// before end of Enable, all shortcuts still in this set are deleted.
    ///
    /// <remarks>Note that the set is case-sensitive, while the actual paths are case-insensitive. This is because we need
    /// special handling for shortcuts/commands that change the casing of the name. Essentially, we want to treat them as
    /// a new, completely independent command, but we actually need to delete the previous export immediately, as opposed
    /// to the cleanup phase of `Export-Pog`, because otherwise we could not create the new export.</remarks>
    public readonly HashSet<string> StaleShortcuts;
    /// <see cref="StaleShortcuts"/>
    public readonly HashSet<string> StaleCommands;
    /// <see cref="StaleShortcuts"/>
    public readonly HashSet<string> StaleShortcutShims;

    internal EnableContainerContext(ImportedPackage enabledPackage) {
        Package = enabledPackage;
        StaleShortcuts = [..enabledPackage.EnumerateExportedShortcuts().Select(f => f.FullName)];
        StaleCommands = [..enabledPackage.EnumerateExportedCommands().Select(f => f.FullName)];
        StaleShortcutShims = [..enabledPackage.EnumerateShortcutShims().Select(f => f.FullName)];
    }
}
