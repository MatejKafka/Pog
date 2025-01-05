using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace Pog;

public static class ManifestTemplateFile {
    /// Returns a list of all templated keys in the template file.
    [PublicAPI]
    public static string[] GetTemplateKeys(string templatePath) {
        LoadFile(templatePath, out var tokens);

        return EnumerateTemplateKeys(tokens).Select(k => k.Item1).ToArray();
    }

    public static string Substitute(string templatePath, string templateDataPath) {
        InstrumentationCounter.ManifestTemplateSubstitutions.Increment();

        var substitutionTable = ParseSubstitutionFile(templateDataPath);

        var ast = LoadFile(templatePath, out var tokens);
        var templateStr = ast.ToString();

        // build the output manifest string by inserting stringified AST nodes into the template code
        var lastEndI = 0;
        var sb = new StringBuilder();
        foreach (var (key, token) in EnumerateTemplateKeys(tokens)) {
            if (!substitutionTable.TryGetValue(key, out var substituteValue)) {
                throw new PackageManifestParseException(templateDataPath,
                        $"The manifest template data file is missing '{key}' key, expected by the manifest template.");
            }

            sb.Append(templateStr, lastEndI, token.Extent.StartOffset - lastEndI);
            sb.Append(substituteValue);
            lastEndI = token.Extent.EndOffset;
        }
        sb.Append(templateStr, lastEndI, templateStr.Length - lastEndI);

        return sb.ToString();
    }

    private static ScriptBlockAst LoadFile(string filePath, out Token[] tokens) {
        var ast = Parser.ParseFile(filePath, out tokens, out var errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(filePath, errors);
        }

        return ast;
    }

    private static readonly Regex TemplateSubstitutionRegex = new(@"^{{TEMPLATE:(\p{L}[\p{L}0-9]*)}}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<(string, StringToken)> EnumerateTemplateKeys(IEnumerable<Token> tokens) {
        foreach (var token in tokens.OfType<StringToken>()) {
            var m = TemplateSubstitutionRegex.Match(token.Value);
            if (m.Success) {
                yield return (m.Groups[1].Value, token);
            }
        }
    }

    /// Parses the substitution file and returns a dictionary mapping from string keys (referenced in the template)
    /// to the AST node representing the value, which is inserted into the template.
    private static Dictionary<string, StatementAst> ParseSubstitutionFile(string templateDataPath) {
        var ast = LoadFile(templateDataPath, out _);

        var hashtableNode = (HashtableAst) ast.Find(static n => n is HashtableAst, false);
        if (hashtableNode == null) {
            throw new PackageManifestParseException(templateDataPath,
                    "The manifest template data file is not a valid PowerShell data file, must be a single Hashtable literal.");
        }

        return hashtableNode.KeyValuePairs.Select(p => {
            if (p.Item1 is not StringConstantExpressionAst key) {
                throw new PackageManifestParseException(templateDataPath,
                        $"The key '{p.Item1}' is not a string constant, only constant keys are allowed in the manifest data file.");
            }

            return (key.Value, p.Item2);
        }).ToDictionary(p => p.Item1, p => p.Item2, StringComparer.InvariantCultureIgnoreCase);
    }

    /// Serializes the passed dictionary into a valid PowerShell data file (.psd1).
    public static void SerializeSubstitutionFile(string outputPath, IDictionary content) {
        File.WriteAllText(outputPath, SerializeSubstitutionFile(content), Encoding.UTF8);
    }

    /// Serializes the passed dictionary into a valid PowerShell Hashtable literal and returns the string.
    public static string SerializeSubstitutionFile(IDictionary content) {
        var serializer = new SubstitutionFileSerializer();
        serializer.Serialize(content);

        return serializer.ToString();
    }

    private class SubstitutionFileSerializer {
        private readonly StringBuilder _sb = new();
        private int _indent = 0;

        public override string ToString() {
            return _sb.ToString();
        }

        public void Serialize(object? value) {
            if (value == null) Write("$null");
            else if (value is PackageVersion v) SerializeString(v.ToString());
            else if (value is string s) SerializeString(s);
            else if (value is bool b) Write(b ? "$true" : "$false");
            else if (IsNumeric(value)) Write(value.ToString());

            else if (value is PSObject o) SerializePSObject(o);
            else if (value is IDictionary d) SerializeDictionary(d);
            else if (value is IEnumerable e) SerializeEnumerable(e);

            else if (value is ScriptBlock sb) SerializeScriptBlock(sb);
            else if (value is SwitchParameter p) Write(p ? "$true" : $"$false");
            else if (value is Guid or Uri or Version) SerializeString(value.ToString());

            else {
                throw new ArgumentException($"Manifest serializer does not support values of type '{value.GetType()}'.");
            }
        }

        private void Write(string s) {
            _sb.Append(s);
        }

        private void WriteNewline() {
            Write("\n");
            Write(GetIndent());
        }

        private string GetIndent() {
            return new string(' ', 4 * _indent);
        }

        private static bool IsNumeric(object o) {
            if (o is byte or sbyte) return true;
            if (o is short or int or long) return true;
            if (o is ushort or uint or ulong) return true;
            if (o is decimal or float or double) return true;

            return false;
        }

        private void SerializeString(string str) {
            Write("'");
            Write(str.Replace("'", "''")); // escape single quotes
            Write("'");
        }

        private void SerializeEnumerable(IEnumerable e) {
            Write("@(");
            var first = true;
            foreach (var item in e) {
                if (first) first = false;
                else Write(", ");
                Serialize(item);
            }
            Write(")");
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private void SerializeDictionary(IDictionary d) {
            SerializeDictionary(d.Cast<DictionaryEntry>());
        }

        private static readonly Regex UnquotedHashtableKeyRegex = new(@"^\p{L}[\p{L}0-9]*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private void SerializeDictionary(IEnumerable<DictionaryEntry> e) {
            Write("@{");
            _indent++;

            var empty = true;
            foreach (var entry in e) {
                empty = false;
                var key = entry.Key;
                var value = entry.Value;

                WriteNewline();
                if (key is string || IsNumeric(key)) {
                    var keyStr = key.ToString();
                    if (UnquotedHashtableKeyRegex.IsMatch(keyStr)) {
                        Write(keyStr); // no need to quote
                    } else {
                        SerializeString(keyStr);
                    }
                } else {
                    throw new ArgumentException(
                            "Only string or numeric keys are supported while serializing a hashtable or other " +
                            $"dictionaries, got '{key.GetType()}'.");
                }
                Write(" = ");
                Serialize(value);
            }

            _indent--;
            if (!empty) {
                WriteNewline();
            }
            Write("}");
        }

        private void SerializeScriptBlock(ScriptBlock sb) {
            Write("{");
            Write(sb.ToString().Replace("\n", "\n" + GetIndent()));
            Write("}");
        }

        private void SerializePSObject(PSObject o) {
            if (o.BaseObject is PSCustomObject) {
                // serialize as a dictionary
                SerializeDictionary(o.Properties.Select(p => new DictionaryEntry(p.Name, p.Value)));
            } else {
                // extract the base object and serialize it directly
                Serialize(o.BaseObject);
            }
        }
    }
}
