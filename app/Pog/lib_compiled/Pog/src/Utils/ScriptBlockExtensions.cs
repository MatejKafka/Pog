using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Pog.Utils;

internal class ImpureScriptBlockException(string message) : Exception(message);

internal static class ScriptBlockExtensions {
    /// Validates that the passed scriptblock does not invoke any cmdlets, does not use any variables
    /// that it does not itself define and does not assign to any non-local variables.
    ///
    /// <remarks>The goal is not to be 100% robust, but serve mostly as a lint. We're just attempting to prevent a manifest
    /// author from using cmdlets or variables that could be locally overriden. The alternative that was originally used was
    /// to run the scriptblock in a container, but that has non-trivial setup overhead.</remarks>
    public static void ValidatePurity(this ScriptBlock sb) {
        var cmdletCalls = sb.Ast.FindAll(node => node is CommandAst, true).ToArray();
        if (cmdletCalls.Length != 0) {
            throw new ImpureScriptBlockException(
                    "must not invoke any commands, since the user may have aliased them in their PowerShell profile. " +
                    $"Found the following command invocations: {string.Join(", ", cmdletCalls.Select(c => $"`{c}`"))}");
        }

        var assignments = sb.Ast.FindAllByType<VariableExpressionAst>(true)
                .Split(v => v.Parent is AssignmentStatementAst, out var usages)
                .ToDictionary(v => v.VariablePath.UserPath, StringComparer.OrdinalIgnoreCase);

        var nonLocalAssignments = assignments.Values.Where(a => !a.VariablePath.IsUnqualified).ToArray();
        if (nonLocalAssignments.Length > 0) {
            throw new ImpureScriptBlockException(
                    "must not assign to any non-local variables. Found the following non-local variable assignments: " +
                    $"{string.Join(", ", nonLocalAssignments.Select(c => $"`{c}`"))}");
        }

        var invalidUsages = usages
                .Where(u => !assignments.ContainsKey(u.VariablePath.UserPath))
                // $this is the only allowed external variable
                .Where(u => u.VariablePath.UserPath != "this")
                .ToArray();

        if (invalidUsages.Length > 0) {
            throw new ImpureScriptBlockException(
                    "must not use any variables that were not previously defined in the scriptblock, except for `$this`. " +
                    $"Found the following variable usages: {string.Join(", ", invalidUsages.Select(c => $"`{c}`"))}");
        }
    }
}
