using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Stub;
using Pog.Utils;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
[Cmdlet(VerbsData.Export, "Command", DefaultParameterSetName = StubPS)]
public class ExportCommandCommand : PSCmdlet {
    private const string SymlinkPS = "Symlink";
    private const string StubPS = "Stub";

    [Parameter(Mandatory = true, Position = 0)]
    [Verify.FileName]
    public string[] CommandName = null!;

    [Parameter(Mandatory = true, Position = 1)]
    [Verify.FilePath]
    public string TargetPath = null!;

    [Parameter(ParameterSetName = StubPS)]
    [Verify.FilePath]
    public string? WorkingDirectory;

    [Parameter(ParameterSetName = StubPS)]
    [Alias("Arguments")]
    public string[]? ArgumentList;

    // use IDictionary so that caller can use [ordered] if order is important
    [Parameter(ParameterSetName = StubPS)]
    [Alias("Environment")]
    public IDictionary? EnvironmentVariables;

    [Parameter(ParameterSetName = StubPS)]
    [Verify.FilePath]
    public string? MetadataSource;

    // useful when the manifest wants to invoke the binary during Enable (e.g. initial config generation in Syncthing)
    [Parameter] public SwitchParameter PassThru;
    [Parameter(ParameterSetName = SymlinkPS)] public SwitchParameter Symlink;
    [Parameter(ParameterSetName = StubPS)] public SwitchParameter VcRedist;
    /// <summary><para type="description">
    /// If set, argv[0] passed to the target is the absolute path to target, otherwise argv[0] is preserved from the stub.
    /// This switch is typically not necessary, but sometimes having a different `argv[0]` breaks the target.
    /// </para></summary>
    [Parameter(ParameterSetName = StubPS)] public SwitchParameter ReplaceArgv0;

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
                if (ExportCommandSymlink(cmdName, rLinkPath, rTargetPath)) {
                    WriteInformation($"Exported command '{cmdName}' using a symlink.", null);
                } else {
                    WriteVerbose($"Command {cmdName} is already exported as a symlink.");
                }
            } else {
                if (ExportCommandStubExecutable(cmdName, rLinkPath, rTargetPath)) {
                    WriteInformation($"Exported command '{cmdName}' using a stub executable.", null);
                } else {
                    WriteVerbose($"Command {cmdName} is already exported as a stub executable.");
                }
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

    private bool ExportCommandSymlink(string cmdName, string rLinkPath, string rTargetPath) {
        if (File.Exists(rLinkPath)) {
            if (rTargetPath == FsUtils.GetSymbolicLinkTarget(rLinkPath)) {
                return false;
            }
            File.Delete(rLinkPath);
        }
        // TODO: add back support for relative paths
        FsUtils.CreateSymbolicLink(rLinkPath, rTargetPath, isDirectory: false);
        return true;
    }

    private bool ExportCommandStubExecutable(string cmdName, string rLinkPath, string rTargetPath) {
        var rWorkingDirectory = WorkingDirectory == null ? null : GetUnresolvedProviderPathFromPSPath(WorkingDirectory)!;
        var rMetadataSource = MetadataSource == null ? null : GetUnresolvedProviderPathFromPSPath(MetadataSource);

        if (rMetadataSource != null && !File.Exists(rMetadataSource)) {
            ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"Metadata source '{MetadataSource}' does not exist, or it is not a file."),
                    "MetadataSourceNotFound", ErrorCategory.InvalidArgument, MetadataSource));
        }

        var resolvedArgs = ArgumentList == null ? null : ResolveArguments(ArgumentList);
        var resolvedEnvVars = EnvironmentVariables == null ? null : ResolveEnvironmentVariables(EnvironmentVariables);
        if (VcRedist) {
            // add the Pog vcredist dir to PATH
            resolvedEnvVars ??= new();
            var pathI = resolvedEnvVars.FindIndex(e => e.Key == "PATH");
            if (pathI == -1) {
                resolvedEnvVars.Add(new("PATH", new[] {InternalState.PathConfig.VcRedistDir, "%PATH%"}));
            } else {
                var path = resolvedEnvVars[pathI];
                resolvedEnvVars[pathI] = new(path.Key, path.Value.Prepend(InternalState.PathConfig.VcRedistDir).ToArray());
            }
        }

        // TODO: argument and env resolution
        var stub = new StubExecutable(rTargetPath, rWorkingDirectory, resolvedArgs, resolvedEnvVars, ReplaceArgv0);

        if (File.Exists(rLinkPath)) {
            if ((new FileInfo(rLinkPath).Attributes & FileAttributes.ReparsePoint) != 0) {
                // reparse point, not an ordinary file, remove
                File.Delete(rLinkPath);
                WriteDebug("Overwriting symlink with a stub executable...");
            } else {
                try {
                    return stub.UpdateStub(rLinkPath, rMetadataSource);
                } catch (StubExecutable.OutdatedStubException) {
                    // outdated stub exe, overwrite the stub with a new version
                    File.Delete(rLinkPath);
                    WriteDebug("Old stub executable, replacing with an up-to-date one...");
                }
            }
        }

        // copy empty stub to rLinkPath
        File.Copy(InternalState.PathConfig.ExecutableStubPath, rLinkPath);
        stub.WriteNewStub(rLinkPath, rMetadataSource);
        return true;
    }

    private string ResolvePotentialPath(string value) {
        // if value looks like a relative path, resolve it
        // TODO: add more controls using annotations
        return value.StartsWith("./") || value.StartsWith(".\\") ? GetUnresolvedProviderPathFromPSPath(value) : value;
    }

    private string? ResolveEnvironmentVariableValue(object? valueObj) {
        if (valueObj == null) {
            return null;
        }
        return ResolvePotentialPath(valueObj.ToString());
    }

    private string[] ResolveArguments(string[] args) {
        return args.Select(ResolvePotentialPath).ToArray();
    }

    private List<KeyValuePair<string, string[]>> ResolveEnvironmentVariables(IDictionary envVars) {
        // if `envVars` is not a SortedDictionary, sort entries alphabetically, so that we get a consistent order
        // this is important, because iteration order of dictionaries apparently differs between .NET Framework
        //  and .NET Core, and we want the stubs to be consistent between powershell.exe and pwsh.exe
        var envEnumerable = envVars is IOrderedDictionary
                ? envVars.Cast<DictionaryEntry>()
                : envVars.Cast<DictionaryEntry>().OrderBy(e => e.Key);

        return envEnumerable.Select(e => new KeyValuePair<string, string[]>(e.Key!.ToString(), e.Value switch {
            // this does not match strings (char is not an object)
            IEnumerable<object> enumerable => enumerable.SelectOptional(ResolveEnvironmentVariableValue)
                    .ToArray(),
            _ => new[] {ResolveEnvironmentVariableValue(e.Value) ?? ""},
        })).ToList();
    }
}
