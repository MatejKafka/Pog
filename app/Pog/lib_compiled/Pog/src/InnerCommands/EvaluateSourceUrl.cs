using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

/// <summary>
/// If <see cref="Source"/>.SourceUrl is a ScriptBlock, this command invokes it and returns the resulting URL,
/// otherwise returns the static URL.
/// </summary>
///
/// <remarks>
/// NOTE: This command executes potentially untrusted code from the manifest.
/// NOTE: This command can be safely invoked outside a container.
/// </remarks>
///
/// <exception cref="InvalidPackageManifestUrlScriptBlockException"></exception>
internal sealed class EvaluateSourceUrl(PogCmdlet cmdlet) : ScalarCommand<string>(cmdlet) {
    [Parameter(Mandatory = true)] public Package Package = null!;
    [Parameter(Mandatory = true)] public PackageSource Source = null!;

    public override string Invoke() {
        if (Source.Url is string s) {
            return s; // static string, just return
        }

        var sb = (ScriptBlock) Source.Url;
        ValidateUrlScriptBlock(sb);

        var resolvedUrlObj = InvokeUrlSb(sb);
        if (resolvedUrlObj.Count != 1) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    $"must return a single string, got {resolvedUrlObj.Count} values: {string.Join(", ", resolvedUrlObj)}");
        }

        var obj = resolvedUrlObj[0]?.BaseObject;
        if (obj is not string resolvedUrl) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    $"must return a string, got '{obj?.GetType().ToString() ?? "null"}'");
        }
        return resolvedUrl;
    }

    private Collection<PSObject> InvokeUrlSb(ScriptBlock sb) {
        // there doesn't seem to be any easier way to set strict mode for a scope
        // this does not leave any leftovers in the caller's scope
        var wrapperSb = ScriptBlock.Create("Set-StrictMode -Version 3; & $Args[0]");
        var variables = new List<PSVariable>
                {new("this", Package.Manifest.Raw), new("ErrorActionPreference", ActionPreference.Stop)};

        try {
            return wrapperSb.InvokeWithContext(null, variables, sb);
        } catch (RuntimeException e) {
            // something failed inside the scriptblock
            var ii = e.ErrorRecord.InvocationInfo;
            var path = Package is ILocalPackage p ? p.ManifestPath : Package.PackageName;
            // replace the position info with a custom listing, since the script path is missing
            var graphic = ii.PositionMessage.Substring(ii.PositionMessage.IndexOf('\n') + 1);
            var positionMsg = $"At {path}, Install.Url:{ii.ScriptLineNumber}\n" + graphic;
            // FIXME: in "NormalView" error view, the error looks slightly confusing, as it's designed for "ConciseView"
            throw new InvalidPackageManifestUrlScriptBlockException(
                    $"failed. Please fix the package manifest or report the issue to the package maintainer:\n" +
                    $"    {e.Message.Replace("\n", "\n    ")}\n\n" +
                    $"    {positionMsg.Replace("\n", "\n    ")}\n", e);
        }
    }

    /// Validates that the passed source URL generator scriptblock does not invoke any cmdlets, does not use any variables
    /// that it does not itself define and does not assign to any non-local variables.
    ///
    /// <remarks>The goal is not to be 100% robust, but serve mostly as a lint. We're just attempting to prevent a manifest
    /// author from using cmdlets or variables that could be locally overriden. The alternative that was originally used was
    /// to run the scriptblock in a container, but that has non-trivial setup overhead.</remarks>
    private static void ValidateUrlScriptBlock(ScriptBlock sb) {
        var cmdletCalls = sb.Ast.FindAll(node => node is CommandAst, true).ToArray();
        if (cmdletCalls.Length != 0) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "must not invoke any commands, since the user may have aliased them in their PowerShell profile. " +
                    $"Found the following command invocations: {string.Join(", ", cmdletCalls.Select(c => $"`{c}`"))}");
        }

        var assignments = sb.Ast.FindAllByType<VariableExpressionAst>(true)
                .Split(v => v.Parent is AssignmentStatementAst, out var usages)
                .ToDictionary(v => v.VariablePath.UserPath, StringComparer.OrdinalIgnoreCase);

        var nonLocalAssignments = assignments.Values.Where(a => !a.VariablePath.IsUnqualified).ToArray();
        if (nonLocalAssignments.Length > 0) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "must not assign to any non-local variables. Found the following non-local variable assignments: " +
                    $"{string.Join(", ", nonLocalAssignments.Select(c => $"`{c}`"))}");
        }

        var invalidUsages = usages
                .Where(u => !assignments.ContainsKey(u.VariablePath.UserPath))
                // $this is the only allowed external variable
                .Where(u => u.VariablePath.UserPath != "this")
                .ToArray();

        if (invalidUsages.Length > 0) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "must not use any variables that were not previously defined in the scriptblock, except for `$this`. " +
                    $"Found the following variable usages: {string.Join(", ", invalidUsages.Select(c => $"`{c}`"))}");
        }
    }
}

/// <summary>Package manifest had a ScriptBlock as the 'Install.Url' property, but it did not return a valid URL.</summary>
public class InvalidPackageManifestUrlScriptBlockException : Exception, IPackageManifestException {
    private const string Prefix = "ScriptBlock for the source URL ('Install.Url' property in the package manifest) ";

    internal InvalidPackageManifestUrlScriptBlockException(string message) : base(Prefix + message) {}
    internal InvalidPackageManifestUrlScriptBlockException(string message, Exception e) : base(Prefix + message, e) {}
}
