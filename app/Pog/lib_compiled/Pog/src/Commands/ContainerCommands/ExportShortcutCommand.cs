using System;
using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.PSAttributes;
using Pog.Shim;

namespace Pog.Commands.ContainerCommands;

/// <summary>Exports a shortcut (.lnk) entry point to the package and places the created shortcut in the root
/// of the package directory. The user can invoke the shortcut to run the packaged application.</summary>
[PublicAPI]
// do not set ShimPS as default parameter set, we need to distinguish it from the case where only -TargetPath is passed
[Cmdlet(VerbsData.Export, "Shortcut")]
public sealed class ExportShortcutCommand : ExportEntryPointCommandBase {
    /// Path to the invoked target. Note that it must either be an executable (.exe), a batch file (.cmd/.bat), a directory
    /// or a special shell folder using the '::{guid}' syntax.
    [Parameter(Mandatory = true, Position = 1)]
    [ResolveShellLinkTarget]
    public string TargetPath = null!;

    /// Path to the icon file used to set the shortcut icon. The path should refer to an .ico file,
    /// or an executable with an embedded icon.
    [Parameter]
    [ResolvePath("Icon source")]
    [Alias("Icon")]
    public string? IconPath;

    /// Description of the shortcut. By default, the file name of the target without the extension is used.
    [Parameter] public string? Description;

    private TargetType _targetType;

    private enum TargetType { Executable, ShellFolder, Directory, NonExecutableFile }

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var ctx = EnableContainerContext.GetCurrent(this);
        var package = ctx.Package;

        if (TargetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
            // SLDF_ALLOW_LINK_TO_LINK seems like it should allow us to link to .lnk, but it does not work
            //  (and the Shell source code has an explicit comment saying that this won't work)
            throw new ArgumentException(
                    $"Exporting shortcuts pointing to another .lnk file is not supported. If you need to export a shortcut " +
                    $"pointing to a shell folder target, pass the folder CLSID directly as '-TargetPath': {TargetPath}");
        }

        _targetType = ShimExecutable.IsTargetSupported(TargetPath) ? TargetType.Executable :
                TargetPath.StartsWith("::{") && TargetPath.EndsWith("}") ? TargetType.ShellFolder :
                Directory.Exists(TargetPath) ? TargetType.Directory :
                TargetType.NonExecutableFile;

        // use target as the default icon path for executables
        var iconPath = IconPath ?? (_targetType == TargetType.Executable ? TargetPath : null);
        // use the first icon (index 0)
        (string, int)? icon = iconPath == null ? null : (iconPath, 0);

        // fallback to target name as description
        // TODO: copy description from versioninfo resource of the target
        var description = !string.IsNullOrEmpty(Description) ? Description :
                _targetType == TargetType.ShellFolder ? "" : Path.GetFileNameWithoutExtension(TargetPath);
        // append package name so that the user can easily check where the shortcut comes from
        description += $" ({package.PackageName})";

        var shortcut = new ExportedShortcut(TargetPath, icon, description, package.Path);
        foreach (var name in Name) {
            var shortcutPath = package.GetExportedShortcutPath(name);
            var shimPath = package.GetExportedShortcutShimPath(name);

            var changed = ExportShortcut(ctx, shortcutPath, shimPath, shortcut);

            // ensure any globally exported copy of the shortcut is also correct
            // run this unconditionally – this way, if something caused the two shortcuts to desync previously,
            //  just re-running `Enable-Pog` should be enough to fix the exported copy
            var globalShortcutChanged = UpdateGloballyExportedShortcut(shortcutPath, package);
            if (globalShortcutChanged) {
                if (changed) {
                    WriteDebug("Updated globally exported shortcut.");
                } else {
                    WriteWarning("Fixed an outdated exported shortcut " +
                                 "(this should only ever happen after a previous installation failed).");
                }
            }

            if (changed || globalShortcutChanged) {
                WriteInformation($"Exported shortcut '{name}'.");
            } else {
                WriteVerbose($"Shortcut '{name}' is already exported.");
            }
        }
    }

    private bool ExportShortcut(EnableContainerContext ctx, string exportPath, string shimPath, ExportedShortcut shortcut) {
        ctx.StaleShortcuts.Remove(exportPath);

        if (_targetType == TargetType.Executable) {
            // shortcut to an executable, invoked through a shim
            ctx.StaleShortcutShims.Remove(shimPath);
            return ExportShimShortcut(exportPath, shimPath, shortcut);
        }

        if (ParameterSetName == ShimPS) {
            throw new ParameterBindingException($"Cannot set shortcut target properties, the target is not " +
                                                $"an executable or a batch file: {shortcut.Target}");
        }

        // shortcut to a directory or to a shell folder (shouldn't really be used in public packages)
        return shortcut.UpdateShortcut(exportPath, WriteDebug);
    }

    // for each executable shortcut, we create a hidden shim that sets all actual options; originally, the shim was only
    //  created when -EnvironmentVariables was passed, since we cannot set env vars on a shortcut; over time, it was also
    //  used for -ArgumentList and -WorkingDirectory, because if someone creates a file association by selecting
    //  the shortcut, the command line is lost (yeah, Windows are kinda stupid sometimes)
    //
    // however, only creating the shim for some executable shortcuts is a bit problematic, because if an older version
    //  of a package invokes the target directly, the system copies the target path somewhere (e.g. a file association)
    //  and the next version adds an argument/env var, it will not be picked up
    //
    // therefore, we now always create the shim; the shim invocation overhead is ~6 ms on my pretty average laptop,
    //  which is imo acceptable since .lnk shortcuts are typically not invoked on hot code paths, unlike commands
    private bool ExportShimShortcut(string exportPath, string shimPath, ExportedShortcut shortcut) {
        var shimChanged = CreateExportShim(shimPath, shortcut.Target, true);
        // ensure that the shortcut is correct
        var shortcutChanged = (shortcut with {Target = shimPath}).UpdateShortcut(exportPath, WriteDebug);
        return shortcutChanged || shimChanged;
    }

    private static bool UpdateGloballyExportedShortcut(string localExportPath, ImportedPackage sourcePackage) {
        var globalShortcut = GloballyExportedShortcut.FromLocal(localExportPath);
        if (globalShortcut.IsFromPackage(sourcePackage)) {
            return globalShortcut.UpdateFrom(new(localExportPath));
        } else {
            // not our shortcut
            return false;
        }
    }
}
