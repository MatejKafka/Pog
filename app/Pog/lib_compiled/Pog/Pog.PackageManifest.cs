using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using JetBrains.Annotations;

namespace Pog;

public class PackageManifestNotFoundException : FileNotFoundException {
    internal PackageManifestNotFoundException(string message, string fileName) : base(message, fileName) {}
}

public class PackageManifestParseException : ParseException {
    internal PackageManifestParseException(string message) : base(message) {}
    internal PackageManifestParseException(ParseError[] errors) : base(errors) {}
}

[PublicAPI]
public class PackageManifest {
    public Hashtable Raw;
    public bool IsPrivate;
    public string? Name;
    public PackageVersion? Version;
    public string Path;

    /// <exception cref="PackageManifestNotFoundException">Thrown if the package manifest file does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package manifest file is not a valid PowerShell data file (.psd1).</exception>
    public PackageManifest(string manifestPath) {
        Path = manifestPath;
        Raw = LoadManifest(manifestPath);
        IsPrivate = (bool) (Raw["Private"] ?? false);
        Name = (string?) Raw["Name"];
        var v = Raw["Version"];
        Version = v == null ? null : new PackageVersion((string) v);
    }

    private Hashtable LoadManifest(string manifestPath) {
        if (!File.Exists(manifestPath)) {
            throw new PackageManifestNotFoundException($"Package manifest file at '${manifestPath}' does not exist.",
                    manifestPath);
        }

        // NOTE: how this is loaded is important; the resulting scriptblocks must NOT be bound to a single runspace;
        //  this should not be an issue when loading the manifest in C#, but in PowerShell, it happens semi-often
        var ast = Parser.ParseFile(manifestPath, out _, out var errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(errors);
        }

        var data = ast.Find(static a => a is HashtableAst, false);
        if (data == null) {
            throw new PackageManifestParseException("Could not load package manifest, it is not a valid PowerShell" +
                                                    " data file, must be a single Hashtable literal: " + manifestPath);
        }

        var manifest = (data.SafeGetValue() as Hashtable)!;
        UnwrapHashtableScriptBlocks(manifest);
        return manifest;
    }

    /// Apparently, HashtableAst.SafeGetValue() incorrectly converts scriptblock value ASTs, loading the whole statement
    /// instead of the scriptblock itself, so it's double wrapped. Before this is fixed upstream, we have to go through
    /// the manifest and unwrap any scriptblock we find.
    /// TODO: report these findings upstream
    private void UnwrapHashtableScriptBlocks(Hashtable hashtable) {
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

    private void UnwrapArrayScriptBlocks(Array arr) {
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

    private ScriptBlock UnwrapScriptBlock(ScriptBlock sb) {
        // this is just going through the nested members, read through the parse tree to understand what it does
        var sbAst = sb.Ast as ScriptBlockAst;
        var pipelineAst = sbAst?.EndBlock.Statements[0] as PipelineAst;
        var realSbAst = (pipelineAst?.PipelineElements[0] as CommandExpressionAst)?.Expression as ScriptBlockExpressionAst;
        var realSb = realSbAst?.ScriptBlock.GetScriptBlock();
        // ReSharper disable once JoinNullCheckWithUsage
        if (realSb == null) {
            throw new Exception("INTERNAL ERROR: Could not unwrap package manifest script-block." +
                                " Seems like Pog developers fucked something up, plz send bug report.");
        }
        return realSb;
    }
}