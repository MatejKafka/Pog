using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Pog;

internal static class PackageManifestParser {
    /// Loads the manifest the same way as `Import-PowerShellDataFile` would, while providing better error messages and
    /// unwrapping any script-blocks (see the other methods).
    public static Hashtable LoadManifest(string manifestPath, string? manifestStr = null) {
        if (manifestStr == null && !File.Exists(manifestPath)) {
            throw new PackageManifestNotFoundException($"Package manifest file is missing, expected path: {manifestPath}",
                    manifestPath);
        }

        // NOTE: how this is loaded is important; the resulting scriptblocks must NOT be bound to a single runspace;
        //  this should not be an issue when loading the manifest in C#, but in PowerShell, it happens semi-often
        //  (see e.g. https://github.com/PowerShell/PowerShell/issues/11658#issuecomment-577304407)
        var ast = manifestStr == null
                ? Parser.ParseFile(manifestPath, out _, out var errors)
                : Parser.ParseInput(manifestStr, out _, out errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(manifestPath, errors);
        }

        var hashtableNode = (HashtableAst) ast.Find(static a => a is HashtableAst, false);
        if (hashtableNode == null) {
            throw new PackageManifestParseException(manifestPath,
                    "The manifest is not a valid PowerShell data file, must be a single Hashtable literal.");
        }

        var manifest = (Hashtable) hashtableNode.SafeGetValue();
        UnwrapHashtableScriptBlocks(manifest);
        return manifest;
    }

    /// Apparently, HashtableAst.SafeGetValue() incorrectly converts scriptblock value ASTs, loading the whole statement
    /// instead of the scriptblock itself, so it's double wrapped. Before this is fixed upstream, we have to go through
    /// the manifest and unwrap any scriptblock we find.
    /// TODO: report these findings upstream
    private static void UnwrapHashtableScriptBlocks(Hashtable hashtable) {
        // use .Keys to avoid exception from modifying an iterated-over Hashtable
        foreach (var key in hashtable.Keys.Cast<object>().ToList()) {
            switch (hashtable[key]) {
                case Hashtable childHashtable:
                    UnwrapHashtableScriptBlocks(childHashtable);
                    break;
                case Array arr:
                    UnwrapArrayScriptBlocks(arr);
                    break;
                case ScriptBlock sb:
                    hashtable[key] = UnwrapScriptBlock(sb);
                    break;
            }
        }
    }

    private static void UnwrapArrayScriptBlocks(Array arr) {
        for (long i = 0; i < arr.Length; i++) {
            switch (arr.GetValue(i)) {
                case Hashtable ht:
                    UnwrapHashtableScriptBlocks(ht);
                    break;
                case Array childArr:
                    UnwrapArrayScriptBlocks(childArr);
                    break;
                case ScriptBlock sb:
                    arr.SetValue(UnwrapScriptBlock(sb), i);
                    break;
            }
        }
    }

    private static ScriptBlock UnwrapScriptBlock(ScriptBlock sb) {
        // this is just going through the nested members, read through the parse tree to understand what it does
        var sbAst = sb.Ast as ScriptBlockAst;
        var pipelineAst = sbAst?.EndBlock.Statements[0] as PipelineAst;
        var realSbAst = (pipelineAst?.PipelineElements[0] as CommandExpressionAst)?.Expression as ScriptBlockExpressionAst;
        var realSb = realSbAst?.ScriptBlock.GetScriptBlock();
        // ReSharper disable once JoinNullCheckWithUsage
        if (realSb == null) {
            throw new InternalError("Could not unwrap package manifest script-block.");
        }
        return realSb;
    }
}
