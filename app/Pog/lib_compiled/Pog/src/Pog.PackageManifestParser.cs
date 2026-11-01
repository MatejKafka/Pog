using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Pog;

/// Internal helper used to override the source file on an extent.
internal class FileScriptExtent(IScriptExtent baseExtent, string source) : IScriptExtent {
    public string File => source;
    public string Text => baseExtent.Text;

    public IScriptPosition StartScriptPosition => baseExtent.StartScriptPosition;
    public int StartColumnNumber => baseExtent.StartColumnNumber;
    public int StartLineNumber => baseExtent.StartLineNumber;
    public int StartOffset => baseExtent.StartOffset;

    public IScriptPosition EndScriptPosition => baseExtent.EndScriptPosition;
    public int EndColumnNumber => baseExtent.EndColumnNumber;
    public int EndLineNumber => baseExtent.EndLineNumber;
    public int EndOffset => baseExtent.EndOffset;
}

// NOTE: how the manifest is loaded is important; the resulting scriptblocks must NOT be bound to a single runspace;
//  this should not be an issue when loading the manifest in C#, but in PowerShell, it happens semi-often
//  (see e.g. https://github.com/PowerShell/PowerShell/issues/11658#issuecomment-577304407)
internal static class PackageManifestParser {
    public static string ReadManifestString(string manifestPath) {
        try {
            return File.ReadAllText(manifestPath);
        } catch (FileNotFoundException) {
            throw new PackageManifestNotFoundException($"Package manifest file is missing: {manifestPath}", manifestPath);
        }
    }

    /// Loads the manifest the same way as `Import-PowerShellDataFile` would, while providing better error messages and
    /// unwrapping any script-blocks (see the other methods).
    public static (string RawStr, Hashtable Parsed) LoadManifest(string manifestPath) {
        return ParseManifest(ReadManifestString(manifestPath), manifestPath);
    }

    /// Loads the manifest the same way as `Import-PowerShellDataFile` would, while providing better error messages and
    /// unwrapping any script-blocks (see the other methods).
    public static (string RawStr, Hashtable Parsed) ParseManifest(string manifestStr, string manifestSource) {
        var ast = Parser.ParseInput(manifestStr, out _, out var errors);
        if (errors.Length > 0) {
            // since we're loading the manifest from string, the errors do not include the source file/URL
            // replace extents on the parse errors to properly show the source
            throw new PackageManifestParseException(manifestSource, errors
                    .Select(e => new ParseError(new FileScriptExtent(e.Extent, manifestSource), e.ErrorId, e.Message))
                    .ToArray());
        }
        return (manifestStr, ProcessManifest(ast, manifestSource));
    }

    private static Hashtable ProcessManifest(Ast ast, string manifestSource) {
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
