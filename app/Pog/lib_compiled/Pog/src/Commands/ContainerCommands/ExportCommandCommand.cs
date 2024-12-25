using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog.Commands.ContainerCommands;

/// <summary>Exports a command line entry point to the package, which the user can invoke to run the packaged application.</summary>
[PublicAPI]
[Cmdlet(VerbsData.Export, "Command", DefaultParameterSetName = ShimPS)]
public class ExportCommandCommand : ExportEntryPointCommandBase {
    private const string SymlinkPS = "Symlink";

    /// If set, the target is exported using a symbolic link instead of a shim executable.
    /// Note that if the target depends on dynamic libraries (.dll) stored in the same directory,
    /// using a symbolic link will likely result in errors about missing dynamic libraries when invoked.
    [Parameter(ParameterSetName = SymlinkPS)] public SwitchParameter Symlink;

    /// If set, argv[0] passed to the target is the absolute path to target, otherwise argv[0] is preserved from the shim.
    /// This switch is typically not necessary, but sometimes having a different `argv[0]` breaks the target.
    [Parameter(ParameterSetName = ShimPS)] public SwitchParameter ReplaceArgv0;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var ctx = EnableContainerContext.GetCurrent(this);
        var exportDir = ctx.Package.ExportedCommandDirPath;

        // ensure export dir exists
        Directory.CreateDirectory(exportDir);

        var useSymlink = ParameterSetName == SymlinkPS;
        var linkExtension = useSymlink ? Path.GetExtension(TargetPath) : ".exe";

        foreach (var name in Name) {
            var exportPath = Path.Combine(exportDir, name + linkExtension);
            if (useSymlink) {
                if (CreateExportSymlink(exportPath)) {
                    WriteInformation($"Exported command '{name}' using a symlink.");
                } else {
                    WriteVerbose($"Command {name} is already exported as a symlink.");
                }
            } else {
                if (CreateExportShim(exportPath, ReplaceArgv0)) {
                    WriteInformation($"Exported command '{name}' using a shim executable.");
                } else {
                    WriteVerbose($"Command {name} is already exported as a shim executable.");
                }
            }

            // mark this command as not stale
            //  (stale = e.g. leftover command from previous version that was removed for this version)
            ctx.StaleCommands.Remove(exportPath);

            if (PassThru) {
                WriteObject(exportPath);
            }
        }
    }

    protected bool CreateExportSymlink(string exportPath) {
        // use relative symlink target when the target is inside the package directory to make the package easier to move
        var relTargetPath = GetRelativeTargetPathForLocalPath(exportPath, TargetPath);

        if (File.Exists(exportPath)) {
            // ensure we have the correct casing
            if (FsUtils.FileExistsCaseSensitive(exportPath) && relTargetPath == FsUtils.GetSymbolicLinkTarget(exportPath)) {
                return false;
            }
            File.Delete(exportPath);
            WriteDebug($"Replacing existing file at '{exportPath}' with a symlink.");
        }
        FsUtils.CreateSymbolicLink(exportPath, relTargetPath, isDirectory: false);
        return true;
    }

    /// If <c>target</c> is inside the package directory, return a relative path from <c>linkPath</c>, otherwise return
    /// <c>targetPath</c> as-is.
    private string GetRelativeTargetPathForLocalPath(string linkPath, string targetPath) {
        if (FsUtils.EscapesDirectory(SessionState.Path.CurrentFileSystemLocation.ProviderPath, targetPath)) {
            // points outside the package dir, should not be too common
            return targetPath;
        }
        return FsUtils.GetRelativePath(Path.GetDirectoryName(linkPath)!, targetPath);
    }
}
