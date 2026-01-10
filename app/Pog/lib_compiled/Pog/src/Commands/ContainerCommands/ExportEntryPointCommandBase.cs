using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Native;
using Pog.PSAttributes;
using Pog.Shim;
using Pog.Utils;

namespace Pog.Commands.ContainerCommands;

/// Base cmdlet to share code between Export-Shortcut and Export-Command.
[PublicAPI]
public class ExportEntryPointCommandBase : PogCmdlet {
    protected const string ShimPS = "Shim";

    /// Name of the exported entry point, without an extension.
    [Parameter(Mandatory = true, Position = 0)]
    [Verify.FileName]
    public string[] Name = null!;

    /// Working directory to set while invoking the target.
    [Parameter(ParameterSetName = ShimPS)]
    [ResolvePath("Working directory")]
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

    /// Path to an .exe to copy icons and similar PE resources from. If not passed, -TargetPath is used instead.
    /// This should only be used when the target is a batch file (.cmd/.bat) that wraps an actual binary, and you
    /// want to copy icons, elevation requirements etc. from the binary.
    [Parameter(ParameterSetName = ShimPS)]
    [ResolvePath("Metadata source")]
    public string? MetadataSource;

    /// If set, a directory containing an up-to-date version of the Microsoft Visual C++ redistributable libraries
    /// (vcruntime140.dll and similar) is added to PATH. The redistributable libraries are shipped with Pog.
    [Parameter(ParameterSetName = ShimPS)] public SwitchParameter VcRedist;

    // TODO: argument and env resolution tags
    protected bool CreateExportShim(string exportPath, string targetPath, bool replaceArgv0) {
        var args = ResolveArguments(ArgumentList);
        var envVars = ResolveEnvironmentVariables(EnvironmentVariables);

        if (VcRedist) {
            // add the Pog vcredist dir to PATH
            envVars ??= [];
            var pathI = envVars.FindIndex(e => e.Key == "PATH");
            if (pathI == -1) {
                envVars.Add(new("PATH", [InternalState.PathConfig.VcRedistDir, "%PATH%"]));
            } else {
                var path = envVars[pathI];
                envVars[pathI] = new(path.Key,
                        path.Value.Prepend(InternalState.PathConfig.VcRedistDir).ToArray());
            }
        }

        var shim = new ShimExecutable(targetPath, WorkingDirectory, args, envVars, MetadataSource, replaceArgv0);
        var (updated, targetInfo) = new ExportedShimCommand(shim).UpdateCommand(exportPath, WriteDebug);

        if (VcRedist && targetInfo?.Architecture == PeBinary.Architecture.I386) {
            // currently, Pog only provides x64 VC redistributable libraries, so a 32bit program will fail
            // TODO: better handling for this
            WriteWarning($"Exported binary '{targetPath}' is a 32-bit binary, -VcRedist is not supported yet.");
        }

        // TODO: add a check that warns when a package that declares, e.g., x86 exports something with a different arch

        return updated;
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

    private string[]? ResolveArguments(string[]? args) {
        return args?.Select(ResolvePotentialPath).ToArray();
    }

    private List<KeyValuePair<string, string[]>>? ResolveEnvironmentVariables(IDictionary? envVars) {
        if (envVars == null) return null;

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
