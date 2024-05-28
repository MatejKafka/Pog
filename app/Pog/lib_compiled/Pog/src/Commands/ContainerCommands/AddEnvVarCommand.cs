using System;
using System.Management.Automation;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
[Cmdlet(VerbsCommon.Add, "EnvVar")]
public class AddEnvVarCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string VariableName = null!;
    [Parameter(Mandatory = true, Position = 1)] public string Directory = null!;
    [Parameter] public SwitchParameter Prepend;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        // remove trailing slash
        var directoryStripped = Directory[Directory.Length - 1] is '/' or '\\'
                ? Directory.Substring(0, Directory.Length - 1)
                : Directory;

        var pattern = new Regex("(^|;)" + Regex.Escape(directoryStripped) + "[\\/]*($|;)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (AddEnvVar(pattern, EnvironmentVariableTarget.User)) {
            // use a warning, since this affects system state outside the Pog directory
            var verb = Prepend ? "Prepended" : "Appended";
            WriteWarning($"{verb} '{Directory}' to 'env:{VariableName}' for the current user.");
        } else {
            WriteVerbose($"Value '{Directory}' already present in 'env:{VariableName}' for the current user.");
        }

        // the var might be set for the user, but our process might have the old/no value
        // this ensures that after this call, value of $env:VarName is up-to-date
        if (AddEnvVar(pattern, EnvironmentVariableTarget.Process)) {
            WriteDebug($"Added '{Directory}' to the process-level env var.");
        } else {
            WriteDebug("Already added to the process-level env var.");
        }
    }

    private bool AddEnvVar(Regex searchPattern, EnvironmentVariableTarget scope) {
        var value = Environment.GetEnvironmentVariable(VariableName, scope);
        if (string.IsNullOrEmpty(value)) {
            // not set yet
            Environment.SetEnvironmentVariable(VariableName, Directory, scope);
            return true;
        }

        if (searchPattern.IsMatch(value)) {
            return false;
        }

        var newValue = Prepend
                ? Directory + (value[0] == ';' ? "" : ";") + value
                : value + (value[value.Length - 1] == ';' ? "" : ";") + Directory;
        Environment.SetEnvironmentVariable(VariableName, newValue, scope);
        return true;
    }
}
