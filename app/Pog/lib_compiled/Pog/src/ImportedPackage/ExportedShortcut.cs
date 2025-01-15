using System;
using System.IO;
using Pog.Native;
using Pog.Utils;

namespace Pog;

internal record ExportedShortcut(string Target, (string, int)? IconLocation, string Description, string SourcePackage) {
    private static Shortcut LoadShortcut(string shortcutPath, Action<string> debugLogFn) {
        var s = new Shortcut();
        if (File.Exists(shortcutPath)) {
            if (!FsUtils.FileExistsCaseSensitive(shortcutPath)) {
                debugLogFn("Updating casing of an exported shortcut...");
                // casing mismatch, behave as if we're creating a new shortcut
                File.Delete(shortcutPath);
            } else {
                debugLogFn($"Shortcut at '{shortcutPath}' already exists, reusing it...");
                s.LoadFrom(shortcutPath);
            }
        }
        return s;
    }

    public bool UpdateShortcut(string shortcutPath, Action<string> debugLogFn) {
        var s = LoadShortcut(shortcutPath, debugLogFn);
        var icon = IconLocation ?? ("", 0);

        // use .TargetID instead of .Target, because we also support shell folder targets
        // if this proves to be too slow, we can conditionally use s.Target as long as our .Target looks like a path
        if (s.Loaded && s.TargetID == Target && s.IconLocation == icon && s.Description == Description
            && s.WorkingDirectory == "" && s.Arguments == "" && GetShortcutSource(s) == SourcePackage) {
            // shortcut is up to date
            return false;
        }

        // shortcut does not match, update it
        s.TargetID = Target;
        s.IconLocation = icon;
        s.Description = Description;
        s.Arguments = "";
        s.WorkingDirectory = "";
        SetShortcutSource(s, SourcePackage);

        s.SaveTo(shortcutPath);
        return true;
    }

    /// GUID used for Pog-specific properties in IPropertyStore of the shortcut.
    private static readonly Guid PogPropertyGuid = new("db0f883d-39c0-465f-bc9a-86ab0ba11cfd");

    // we store the path of the owning package as a property, which allows Pog to correctly track globally exported shortcuts
    //  that have a target that's not an executable that can be wrapped with a shim

    internal static string? GetShortcutSource(Shortcut shortcut) {
        return shortcut.TryGetStringProperty(PogPropertyGuid, 2, out var sourceStr) ? sourceStr : null;
    }

    private static void SetShortcutSource(Shortcut shortcut, string sourceStr) {
        shortcut.SetStringProperty(PogPropertyGuid, 2, sourceStr);
    }
}
