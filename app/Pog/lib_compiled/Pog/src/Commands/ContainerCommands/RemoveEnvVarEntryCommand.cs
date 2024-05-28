using System;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
[Cmdlet(VerbsCommon.Remove, "EnvVarEntry")]
public class RemoveEnvVarEntryCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string VariableName = null!;
    [Parameter(Mandatory = true, Position = 1)] public string Directory = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        // remove trailing slash
        if (Directory[Directory.Length - 1] is '/' or '\\') {
            Directory = Directory.Substring(0, Directory.Length - 1);
        }

        var pattern = new Regex("^" + Regex.Escape(Directory) + "[\\/]*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // remove from user-level env var
        if (RemoveEnvVarPathEntry(EnvironmentVariableTarget.User, pattern, out var deleted)) {
            var removingStr = deleted ? " and removed the empty variable" : "";
            WriteInformation($"Removed '{Directory}' from 'env:{VariableName}'{removingStr} for the current user.");

            // removed a user-level entry, remove the process-level copy silently
            RemoveEnvVarPathEntry(EnvironmentVariableTarget.Process, pattern, out _);
        } else {
            // also check if the value is set in process-level var
            if (RemoveEnvVarPathEntry(EnvironmentVariableTarget.Process, pattern, out deleted)) {
                var removingStr = deleted ? " and removed the empty variable" : "";
                WriteInformation($"Removed '{Directory}' from 'env:{VariableName}'{removingStr}. Note that the entry was " +
                                 "only set in the process-level environment variable, not for the current user. If you're " +
                                 "adding the value somewhere in your shell profile, please remove it manually.");
            }
        }
    }

    private bool RemoveEnvVarPathEntry(EnvironmentVariableTarget scope, Regex pattern, out bool deleted) {
        deleted = false;
        var varValue = Environment.GetEnvironmentVariable(VariableName, scope);
        if (varValue == null) {
            return false;
        }

        var result = string.Join(";", varValue.Split(';').Where(s => !pattern.IsMatch(s)));
        if (result.Length == varValue.Length) {
            return false;
        }

        if (result == "") {
            result = null; // delete the variable
            deleted = true;
        }

        Environment.SetEnvironmentVariable(VariableName, result, scope);
        return true;
    }
}
