using System;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands.ContainerCommands;

/// <summary>Sets the specified user-level environment variable to a new value.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Set, "EnvVar")]
public sealed class SetEnvVarCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string VariableName = null!;
    [Parameter(Mandatory = true, Position = 1)] public string Value = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        if (Value == Environment.GetEnvironmentVariable(VariableName, EnvironmentVariableTarget.User)) {
            WriteVerbose($"'env:{VariableName}' is already set to '{Value}' for the current user.");
        } else {
            Environment.SetEnvironmentVariable(VariableName, Value, EnvironmentVariableTarget.User);
            WriteWarning($"Set 'env:{VariableName}' to '{Value}' for the current user.");
        }

        // the var might be set for the user, but our process might have the old/no value
        // this ensures that after this call, value of $env:VarName is up-to-date
        Environment.SetEnvironmentVariable(VariableName, Value, EnvironmentVariableTarget.Process);
    }
}
