using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Pog;

// NOTE: how the manifest is loaded is important; the resulting scriptblocks must NOT be bound to a single runspace;
//  this should not be an issue when loading the manifest in C#, but in PowerShell, it happens semi-often
//  (see e.g. https://github.com/PowerShell/PowerShell/issues/11658#issuecomment-577304407)
internal static class PackageManifestParser {
    /// Loads the manifest the same way as `Import-PowerShellDataFile` would, while providing better error messages and
    /// unwrapping any script-blocks (see the other methods).
    public static (string RawStr, Hashtable Parsed) LoadManifest(string manifestPath) {
        if (!File.Exists(manifestPath)) {
            throw new PackageManifestNotFoundException($"Package manifest file is missing, expected path: {manifestPath}",
                    manifestPath);
        }

        var ast = Parser.ParseFile(manifestPath, out _, out var errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(manifestPath, errors);
        }

        var manifestText = ast.Extent.Text;
        return (manifestText, ParseLoadedManifest(ast, manifestPath));
    }

    /// Loads the manifest the same way as `Import-PowerShellDataFile` would, while providing better error messages and
    /// unwrapping any script-blocks (see the other methods).
    public static (string, Hashtable) LoadManifest(string manifestStr, string manifestSource) {
        var ast = Parser.ParseInput(manifestStr, out _, out var errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(manifestSource, errors);
        }

        return (manifestStr, ParseLoadedManifest(ast, manifestSource));
    }

    private static Hashtable ParseLoadedManifest(Ast ast, string manifestSource) {
        var hashtableNode = (HashtableAst) ast.Find(static a => a is HashtableAst, false);
        if (hashtableNode == null) {
            throw new PackageManifestParseException(manifestSource,
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
    // ReSharper disable once SuggestBaseTypeForParameter
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
