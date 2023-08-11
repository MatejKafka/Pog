using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Stub;

namespace Pog.Commands;

[PublicAPI]
[Cmdlet(VerbsData.Export, "Command", DefaultParameterSetName = StubPS)]
public class ExportCommandCommand : PSCmdlet {
    private const string SymlinkPS = "Symlink";
    private const string StubPS = "Stub";

    [Parameter(Mandatory = true, Position = 0)] public string[] CommandName = null!;
    [Parameter(Mandatory = true, Position = 1)] public string TargetPath = null!;

    /// Useful when the manifest wants to invoke the binary during Enable (e.g. initial config generation in Syncthing).
    [Parameter]
    public SwitchParameter PassThru;

    [Parameter(ParameterSetName = SymlinkPS)] public SwitchParameter Symlink;

    [Parameter(ParameterSetName = StubPS)] public string? WorkingDirectory;

    [Parameter(ParameterSetName = StubPS)]
    [Alias("Arguments")]
    public string[]? ArgumentList;

    [Parameter(ParameterSetName = StubPS)]
    [Alias("Environment")]
    public Hashtable? EnvironmentVariables;

    [Parameter(ParameterSetName = StubPS)] public string? MetadataSource;

    // ReSharper disable once InconsistentNaming
    // TODO: figure out some way to avoid this parameter without duplicating this cmdlet
    /// Internal parameter, do not use.
    [Parameter(DontShow = true)]
    public SwitchParameter _InternalDoNotUse_Shortcut;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        foreach (var cmdName in CommandName) {
            Verify.FileName(cmdName);
        }
        Verify.FilePath(TargetPath);
        if (WorkingDirectory != null) Verify.FilePath(WorkingDirectory);
        if (MetadataSource != null) Verify.FilePath(MetadataSource);

        var internalState = ContainerEnableInternalState.GetCurrent(this);
        var rTargetPath = GetUnresolvedProviderPathFromPSPath(TargetPath)!;
        var commandDirRelPath = _InternalDoNotUse_Shortcut
                ? PathConfig.PackagePaths.ShortcutStubDirRelPath
                : PathConfig.PackagePaths.CommandDirRelPath;
        var commandDirPath = GetUnresolvedProviderPathFromPSPath(commandDirRelPath);

        WriteDebug("Resolved command target: " + rTargetPath);

        if (!File.Exists(rTargetPath)) {
            ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"Command target '{TargetPath}' does not exist, or it is not a file."),
                    "TargetNotFound", ErrorCategory.InvalidArgument, TargetPath));
        }

        // ensure command dir exists
        Directory.CreateDirectory(commandDirPath);

        var useSymlink = ParameterSetName == SymlinkPS;
        var linkExtension = useSymlink ? Path.GetExtension(rTargetPath) : ".exe";

        foreach (var cmdName in CommandName) {
            var rLinkPath = Path.Combine(commandDirPath, cmdName + linkExtension);
            if (useSymlink) {
                ExportCommandSymlink(cmdName, rLinkPath, rTargetPath);
            } else {
                ExportCommandStubExecutable(cmdName, rLinkPath, rTargetPath);
            }

            // mark this command as not stale
            //  (stale = e.g. leftover command from previous version that was removed for this version)
            if (_InternalDoNotUse_Shortcut) {
                internalState.StaleShortcutStubs.Remove(rLinkPath);
            } else {
                internalState.StaleCommands.Remove(rLinkPath);
            }

            if (PassThru) {
                WriteObject(rLinkPath);
            }
        }
    }

    private void ExportCommandSymlink(string cmdName, string rLinkPath, string rTargetPath) {
        if (File.Exists(rLinkPath)) {
            if (rTargetPath == FsUtils.GetSymbolicLinkTarget(rLinkPath)) {
                WriteVerbose($"Command {cmdName} is already exported as a symlink.");
                return;
            }
            File.Delete(rLinkPath);
        }
        // TODO: add back support for relative paths
        FsUtils.CreateSymbolicLink(rLinkPath, rTargetPath, isDirectory: false);
        WriteInformation($"Registered command '{cmdName}' using a symlink.", null);
    }

    private void ExportCommandStubExecutable(string cmdName, string rLinkPath, string rTargetPath) {
        var rWorkingDirectory = WorkingDirectory == null ? null : GetUnresolvedProviderPathFromPSPath(WorkingDirectory)!;
        var rMetadataSource = MetadataSource == null ? null : GetUnresolvedProviderPathFromPSPath(MetadataSource);

        if (rMetadataSource != null && !File.Exists(rMetadataSource)) {
            ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"Metadata source '{MetadataSource}' does not exist, or it is not a file."),
                    "MetadataSourceNotFound", ErrorCategory.InvalidArgument, MetadataSource));
        }

        // TODO: argument and env resolution
        var stub = new StubExecutable(rTargetPath, rWorkingDirectory, ArgumentList,
                EnvironmentVariables == null ? null : ResolveEnvironmentVariables(EnvironmentVariables));

        if (File.Exists(rLinkPath)) {
            if ((new FileInfo(rLinkPath).Attributes & FileAttributes.ReparsePoint) != 0) {
                // reparse point, not an ordinary file, remove
                File.Delete(rLinkPath);
            } else if (stub.UpdateStub(rLinkPath, rMetadataSource)) {
                // stub was changed
                WriteInformation($"Registered command '{cmdName}' using a stub executable.", null);
            } else {
                WriteVerbose($"Command {cmdName} is already exported as a stub executable.");
            }
        } else {
            // copy empty stub to rLinkPath
            File.Copy(InternalState.PathConfig.ExecutableStubPath, rLinkPath);
            stub.WriteNewStub(rLinkPath, rMetadataSource);
            WriteInformation($"Registered command '{cmdName}' using a stub executable.", null);
        }
    }

    private Dictionary<string, string> ResolveEnvironmentVariables(Hashtable envVars) {
        return envVars.Cast<DictionaryEntry>().ToDictionary(entry => entry.Key!.ToString(), entry => {
            var value = entry.Value?.ToString() ?? "";
            // if value looks like a relative path, resolve it
            // TODO: add more controls using annotations
            if (value.StartsWith("./") || value.StartsWith(".\\")) {
                value = GetUnresolvedProviderPathFromPSPath(value);
            }
            return value;
        });
    }
}
