using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.PSAttributes;
using Pog.Shim;
using Pog.Utils;

namespace Pog.Commands.ContainerCommands;

/// <summary>Exports a command line entry point to the package, which the user can invoke to run the packaged application.</summary>
[PublicAPI]
[Cmdlet(VerbsData.Export, "Command", DefaultParameterSetName = ShimPS)]
public class ExportCommandCommand : PSCmdlet {
    private const string SymlinkPS = "Symlink";
    private const string ShimPS = "Shim";

    /// Name of the exported command, without an extension.
    [Parameter(Mandatory = true, Position = 0)]
    [Verify.FileName]
    public string[] CommandName = null!;

    /// Path to the invoked target. Note that it must either be an executable (.exe) or a batch file (.cmd/.bat).
    [Parameter(Mandatory = true, Position = 1)]
    [ResolvePath("Command target")]
    public string TargetPath = null!;

    /// Working directory to set while invoking the target.
    [Parameter(ParameterSetName = ShimPS)]
    [ResolvePath("Command working directory")]
    public string? WorkingDirectory;

    /// An argv-like array of arguments which are prepended to the command line that the target is invoked with.
    /// All arguments that start with `./` or `.\` are resolved into absolute paths.
    [Parameter(ParameterSetName = ShimPS)]
    [Alias("Arguments")]
    public string[]? ArgumentList;

    // use IDictionary so that caller can use [ordered] if order is important
    /// A dictionary of environment variables to set before invoking the target. The key must be a string, the value
    /// must either be a string, or an array of strings, which is combined using the path separator (;).
    /// All variable values that start with `./` or `.\` are resolved into absolute paths. Environment variable
    /// substitution is supported using the `%NAME%` syntax and expanded when the shortcut is invoked
    /// (e.g. in `KEY = "%VAR%\..."`, `%VAR%` is replaced at runtime with the actual value of the `VAR` environment variable).
    [Parameter(ParameterSetName = ShimPS)]
    [Alias("Environment")]
    public IDictionary? EnvironmentVariables;

    /// Path to an .exe to copy icons and similar PE resources from. If not passed, <see cref="TargetPath"/> is used instead.
    [Parameter(ParameterSetName = ShimPS)]
    [ResolvePath("Command metadata source")]
    public string? MetadataSource;

    // useful when the manifest wants to invoke the binary during Enable (e.g. initial config generation in Syncthing)
    [Parameter] public SwitchParameter PassThru;


    /// If set, the target is exported using a symbolic link instead of a shim executable.
    /// Note that if the target depends on dynamic libraries (.dll) stored in the same directory,
    /// using a symbolic link will likely result in errors about missing dynamic libraries when invoked.
    [Parameter(ParameterSetName = SymlinkPS)] public SwitchParameter Symlink;

    /// If set, a directory containing an up-to-date version of the Microsoft Visual C++ redistributable libraries
    /// (vcruntime140.dll and similar) is added to PATH. The redistributable libraries are shipped with Pog.
    [Parameter(ParameterSetName = ShimPS)] public SwitchParameter VcRedist;

    /// If set, argv[0] passed to the target is the absolute path to target, otherwise argv[0] is preserved from the shim.
    /// This switch is typically not necessary, but sometimes having a different `argv[0]` breaks the target.
    [Parameter(ParameterSetName = ShimPS)] public SwitchParameter ReplaceArgv0;

    // ReSharper disable once InconsistentNaming
    // TODO: figure out some way to avoid this parameter without duplicating this cmdlet
    /// Internal parameter, do not use.
    [Parameter(DontShow = true)]
    public SwitchParameter _InternalDoNotUse_Shortcut;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var ctx = EnableContainerContext.GetCurrent(this);
        var commandDirPath = _InternalDoNotUse_Shortcut
                ? ctx.Package.ExportedShortcutShimDirPath
                : ctx.Package.ExportedCommandDirPath;

        WriteDebug("Resolved command target: " + TargetPath);

        // ensure command dir exists
        Directory.CreateDirectory(commandDirPath);

        var useSymlink = ParameterSetName == SymlinkPS;
        var linkExtension = useSymlink ? Path.GetExtension(TargetPath) : ".exe";

        foreach (var cmdName in CommandName) {
            var rLinkPath = Path.Combine(commandDirPath, cmdName + linkExtension);
            if (useSymlink) {
                if (ExportCommandSymlink(cmdName, rLinkPath, TargetPath)) {
                    WriteInformation($"Exported command '{cmdName}' using a symlink.", null);
                } else {
                    WriteVerbose($"Command {cmdName} is already exported as a symlink.");
                }
            } else {
                if (ExportCommandShimExecutable(cmdName, rLinkPath, TargetPath)) {
                    WriteInformation($"Exported command '{cmdName}' using a shim executable.", null);
                } else {
                    WriteVerbose($"Command {cmdName} is already exported as a shim executable.");
                }
            }

            // mark this command as not stale
            //  (stale = e.g. leftover command from previous version that was removed for this version)
            if (_InternalDoNotUse_Shortcut) {
                ctx.StaleShortcutShims.Remove(rLinkPath);
            } else {
                ctx.StaleCommands.Remove(rLinkPath);
            }

            if (PassThru) {
                WriteObject(rLinkPath);
            }
        }
    }

    private bool ExportCommandSymlink(string cmdName, string rLinkPath, string rTargetPath) {
        // use relative symlink target when the target is inside the package directory to make the package easier to move
        rTargetPath = GetRelativeTargetPathForLocalPath(rLinkPath, rTargetPath);

        if (File.Exists(rLinkPath)) {
            // ensure we have the correct casing
            if (FsUtils.FileExistsCaseSensitive(rLinkPath) && rTargetPath == FsUtils.GetSymbolicLinkTarget(rLinkPath)) {
                return false;
            }
            File.Delete(rLinkPath);
        }
        FsUtils.CreateSymbolicLink(rLinkPath, rTargetPath, isDirectory: false);
        return true;
    }

    /// If <c>target</c> is inside the package directory, return a relative path from <c>linkPath</c>, otherwise return <c>target</c> as-is.
    private string GetRelativeTargetPathForLocalPath(string rLinkPath, string rTargetPath) {
        if (FsUtils.EscapesDirectory(SessionState.Path.CurrentFileSystemLocation.ProviderPath, rTargetPath)) {
            // points outside the package dir, should not be too common
            return rTargetPath;
        }
        return FsUtils.GetRelativePath(Path.GetDirectoryName(rLinkPath)!, rTargetPath);
    }

    private bool ExportCommandShimExecutable(string cmdName, string rLinkPath, string rTargetPath) {
        var resolvedArgs = ArgumentList == null ? null : ResolveArguments(ArgumentList);
        var resolvedEnvVars = EnvironmentVariables == null ? null : ResolveEnvironmentVariables(EnvironmentVariables);
        if (VcRedist) {
            // add the Pog vcredist dir to PATH
            resolvedEnvVars ??= [];
            var pathI = resolvedEnvVars.FindIndex(e => e.Key == "PATH");
            if (pathI == -1) {
                resolvedEnvVars.Add(new("PATH", [InternalState.PathConfig.VcRedistDir, "%PATH%"]));
            } else {
                var path = resolvedEnvVars[pathI];
                resolvedEnvVars[pathI] = new(path.Key, path.Value.Prepend(InternalState.PathConfig.VcRedistDir).ToArray());
            }
        }

        // TODO: argument and env resolution
        var shim = new ShimExecutable(rTargetPath, WorkingDirectory, resolvedArgs, resolvedEnvVars, MetadataSource,
                ReplaceArgv0);

        if (File.Exists(rLinkPath)) {
            if ((new FileInfo(rLinkPath).Attributes & FileAttributes.ReparsePoint) != 0) {
                WriteDebug("Overwriting symlink with a shim executable...");
                // reparse point, not an ordinary file, remove
                File.Delete(rLinkPath);
            } else if (!FsUtils.FileExistsCaseSensitive(rLinkPath)) {
                WriteDebug("Updating casing of an exported command...");
                File.Delete(rLinkPath);
            } else {
                try {
                    return shim.UpdateShim(rLinkPath);
                } catch (ShimExecutable.OutdatedShimException) {
                    WriteDebug("Old shim executable, replacing with an up-to-date one...");
                    File.Delete(rLinkPath);
                }
            }
        }

        // copy empty shim to rLinkPath
        File.Copy(InternalState.PathConfig.ShimPath, rLinkPath);
        try {
            shim.WriteNewShim(rLinkPath);
        } catch {
            // clean up the empty shim
            FsUtils.EnsureDeleteFile(rLinkPath);
            throw;
        }
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
        //  and .NET Core, and we want the shims to be consistent between powershell.exe and pwsh.exe
        var envEnumerable = envVars is IOrderedDictionary
                ? envVars.Cast<DictionaryEntry>()
                : envVars.Cast<DictionaryEntry>().OrderBy(e => e.Key);

        return envEnumerable.Select(e => new KeyValuePair<string, string[]>(e.Key!.ToString(), e.Value switch {
            // this does not match strings (char is not an object)
            IEnumerable<object> enumerable => enumerable.SelectOptional(ResolveEnvironmentVariableValue)
                    .ToArray(),
            _ => [ResolveEnvironmentVariableValue(e.Value) ?? ""],
        })).ToList();
    }
}
