using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Native;
using Pog.PSAttributes;

namespace Pog.Commands.ContainerCommands;

/// <summary>Exports a shortcut (.lnk) entry point to the package and places the created shortcut in the root
/// of the package directory. The user can invoke the shortcut to run the packaged application.</summary>
[PublicAPI]
// parameter set necessary for compatibility with Export-Command
[Cmdlet(VerbsData.Export, "Shortcut", DefaultParameterSetName = ShimPS)]
public sealed class ExportShortcutCommand : ExportEntryPointCommandBase {
    /// Path to the icon file used to set the shortcut icon. The path should refer to an .ico file,
    /// or an executable with an embedded icon.
    [Parameter]
    [ResolvePath("Icon source")]
    [Alias("Icon")]
    public string? IconPath;

    /// Description of the shortcut. By default, the file name of the target without the extension is used.
    [Parameter] public string? Description;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var ctx = EnableContainerContext.GetCurrent(this);

        // use target as the default icon path
        var iconPath = IconPath ?? TargetPath;

        // support copying icon from another .lnk, otherwise default to the first icon (index 0) in the target
        var icon = Path.GetExtension(iconPath) == ".lnk" ? new Shortcut(iconPath).IconLocation : (iconPath, 0);

        // fallback to target name as description
        // TODO: copy description from versioninfo resource of the target
        var description = !string.IsNullOrEmpty(Description) ? Description : Path.GetFileNameWithoutExtension(TargetPath);
        // append package name so that the user can easily check where the shortcut comes from
        description += $" ({ctx.Package.PackageName})";

        // ensure shortcut and shim dirs exists
        Directory.CreateDirectory(ctx.Package.ExportedShortcutDirPath);
        Directory.CreateDirectory(ctx.Package.ExportedShortcutShimDirPath);

        foreach (var name in Name) {
            var exportPath = ctx.Package.GetExportedShortcutPath(name);
            var shimExportPath = ctx.Package.GetExportedShortcutShimPath(name);

            if (ExportShortcut(exportPath, shimExportPath, icon, description)) {
                WriteInformation($"Exported shortcut '{name}'.");
            } else {
                WriteVerbose($"Shortcut '{name}' is already exported.");
            }

            // mark this shortcut as not stale
            //  (stale = e.g. leftover shortcut from previous version that was removed for this version)
            ctx.StaleShortcutShims.Remove(shimExportPath);
            ctx.StaleShortcuts.Remove(exportPath);

            if (PassThru) {
                WriteObject(exportPath);
            }
        }
    }

    internal bool ExportShortcut(string exportPath, string shimExportPath, (string, int) icon, string description) {
        // for each shortcut, we create a hidden shim that sets all actual options; originally, the shim was only created
        //  when -EnvironmentVariables was passed, since we cannot set env vars on a shortcut; over time, it was also
        //  used for -ArgumentList and -WorkingDirectory, because if someone creates a file association by selecting
        //  the shortcut, the command line is lost (yeah, Windows are kinda stupid sometimes)
        //
        // however, only creating the shim for some shortcuts causes issues:
        //  1) if an older version of a package invokes the target directly, the system copies the target path somewhere
        //     (e.g. a file association) and the next version adds an argument/env var, it will not be picked up
        //  2) without the shim, checking which package an exported shortcut belongs to would be a bit more complex
        //     (apparently, .lnk shortcuts support some form of custom properties, but I don't know much about it)
        //
        // therefore, we now always create the shim; the shim invocation overhead is ~6 ms on my pretty average laptop,
        //  which is imo acceptable since .lnk shortcuts are typically not invoked on hot code paths, unlike commands
        var shimChanged = CreateExportShim(shimExportPath, true);

        var s = new Shortcut();
        if (File.Exists(exportPath)) {
            if (!FsUtils.FileExistsCaseSensitive(exportPath)) {
                WriteDebug("Updating casing of an exported shortcut...");
                // casing mismatch, behave as if we're creating a new shortcut
                File.Delete(exportPath);
            } else {
                WriteDebug($"Shortcut at '{exportPath}' already exists, reusing it...");
                s.LoadFrom(exportPath);
            }
        }

        // ensure that the shortcut is correct
        var shortcutChanged = UpdateShortcutContent(s, exportPath, shimExportPath, icon, description);

        // ensure any globally exported copy of the shortcut is also correct
        // run this unconditionally – this way, if something caused the two shortcuts to desync previously, just re-running
        //  `Enable-Pog` should be enough to fix the exported copy
        var globalShortcutChanged = UpdateGloballyExportedShortcut(exportPath, shimExportPath);

        if (globalShortcutChanged) {
            if (shortcutChanged) {
                WriteDebug("Updated globally exported shortcut.");
            } else {
                // this should not happen unless a previous invocation failed halfway through
                WriteWarning("Fixed an outdated exported shortcut.");
            }
        }

        return shortcutChanged || shimChanged || globalShortcutChanged;
    }

    private bool UpdateShortcutContent(Shortcut s, string exportPath, string shimExportPath, (string, int) icon,
            string description) {
        if (s.Loaded && s.Target == shimExportPath && s.IconLocation == icon && s.Description == description
            && s.WorkingDirectory == "" && s.Arguments == "") {
            // shortcut is up to date
            return false;
        }

        // shortcut does not match, update it
        s.Target = shimExportPath;
        s.IconLocation = icon;
        s.Description = description;
        s.Arguments = "";
        s.WorkingDirectory = "";

        s.SaveTo(exportPath);
        return true;
    }

    private bool UpdateGloballyExportedShortcut(string localExportPath, string targetShimPath) {
        var globalShortcut = GloballyExportedShortcut.FromLocal(localExportPath);
        if (globalShortcut.IsFromPackage(targetShimPath)) {
            return globalShortcut.UpdateFrom(new(localExportPath));
        } else {
            // not our shortcut
            return false;
        }
    }
}
