using System.IO;

namespace Pog;

/// Class with utility functions for working with global exports (i.e. commands visible in PATH and Start menu shortcuts).
/// In this future, this will probably turn into a non-static class instantiated per export location (e.g. `ShortcutExportManager`).
internal static class GlobalExportUtils {
    public static string GetShortcutExportPath(string exportedShortcutPath) {
        return $"{PathConfig.StartMenuExportDir}\\{Path.GetFileName(exportedShortcutPath)}";
    }

    public static string GetShortcutExportPath(FileInfo exportedShortcut) {
        return $"{PathConfig.StartMenuExportDir}\\{exportedShortcut.Name}";
    }

    public static string GetCommandExportPath(string exportedCommandPath) {
        return $"{InternalState.PathConfig.ExportedCommandDir}\\{Path.GetFileName(exportedCommandPath)}";
    }

    public static string GetCommandExportPath(FileInfo exportedCommand) {
        return $"{InternalState.PathConfig.ExportedCommandDir}\\{exportedCommand.Name}";
    }
}
