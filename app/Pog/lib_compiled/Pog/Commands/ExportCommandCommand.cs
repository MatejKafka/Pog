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

    [Parameter(Mandatory = true, Position = 0)] public string CommandName = null!;
    [Parameter(Mandatory = true, Position = 1)] public string TargetPath = null!;

    [Parameter(ParameterSetName = SymlinkPS)] public SwitchParameter Symlink;

    [Parameter(ParameterSetName = StubPS)] public string? WorkingDirectory;

    [Parameter(ParameterSetName = StubPS)]
    [Alias("Arguments")]
    public string[]? ArgumentList;

    [Parameter(ParameterSetName = StubPS)]
    [Alias("Environment")]
    public Hashtable? EnvironmentVariables;

    [Parameter(ParameterSetName = StubPS)] public string? MetadataSource;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var internalState = ContainerEnableInternalState.GetCurrent(this);
        var useSymlink = ParameterSetName == SymlinkPS;
        var rTargetPath = GetUnresolvedProviderPathFromPSPath(TargetPath)!;
        var linkExtension = useSymlink ? Path.GetExtension(rTargetPath) : ".exe";

        var commandDirPath = GetUnresolvedProviderPathFromPSPath(PathConfig.PackagePaths.CommandDirRelPath);
        var rLinkPath = Path.Combine(commandDirPath, CommandName + linkExtension);

        if (!File.Exists(rTargetPath)) {
            ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"Command target '{TargetPath}' does not exist, or it is not a file."),
                    "TargetNotFound", ErrorCategory.InvalidArgument, TargetPath));
        }

        // ensure command dir exists
        Directory.CreateDirectory(commandDirPath);

        if (useSymlink) {
            ExportCommandSymlink(rLinkPath, rTargetPath);
        } else {
            ExportCommandStubExecutable(rLinkPath, rTargetPath);
        }

        // mark this command as not stale (e.g. leftover command from previous version)
        internalState.StaleCommands.Remove(rLinkPath);
    }

    private void ExportCommandSymlink(string rLinkPath, string rTargetPath) {
        if (File.Exists(rLinkPath)) {
            // .GetLinkTarget(...) returns null if argument is not a symlink
            if (rTargetPath == Native.Symlink.GetLinkTarget(rLinkPath)) {
                WriteVerbose($"Command {CommandName} is already exported as a symlink.");
                return;
            }
            File.Delete(rLinkPath);
        }
        // TODO: add back support for relative paths
        Native.Symlink.CreateSymbolicLink(rLinkPath, rTargetPath, false);
        WriteInformation($"Registered command '{CommandName}' using a symlink.", null);
    }

    private void ExportCommandStubExecutable(string rLinkPath, string rTargetPath) {
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

        // stub executable
        if (File.Exists(rLinkPath)) {
            if ((new FileInfo(rLinkPath).Attributes & FileAttributes.ReparsePoint) != 0) {
                // reparse point, not an ordinary file, remove
                File.Delete(rLinkPath);
            } else if (stub.UpdateStub(rLinkPath, rMetadataSource)) {
                // stub was changed
                WriteInformation($"Registered command '{CommandName}' using a stub executable.", null);
            } else {
                WriteVerbose($"Command {CommandName} is already exported as a stub executable.");
            }
        } else {
            // copy empty stub to rLinkPath
            File.Copy(InternalState.PathConfig.ExecutableStubPath, rLinkPath);
            stub.WriteNewStub(rLinkPath, rMetadataSource);
            WriteInformation($"Registered command '{CommandName}' using a stub executable.", null);
        }
    }

    private static Dictionary<string, string> ResolveEnvironmentVariables(Hashtable envVars) {
        return envVars.Cast<DictionaryEntry>()
                .ToDictionary(entry => entry.Key!.ToString(), entry => entry.Value?.ToString() ?? "");
    }
}
