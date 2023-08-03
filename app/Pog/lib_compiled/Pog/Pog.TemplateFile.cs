using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using System.Text;
using System.Text.RegularExpressions;

namespace Pog;

internal static class TemplateFile {
    private static readonly Regex TemplateSubstitutionRegex =
            new(@"^{{TEMPLATE:(\p{L}[\p{L}0-9]*)}}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void Substitute(string templatePath, string templateDataPath, string outputPath) {
        File.WriteAllText(outputPath, Substitute(templatePath, templateDataPath), Encoding.UTF8);
    }

    public static string Substitute(string templatePath, string templateDataPath) {
        var substitutionTable = ParseSubstitutionFile(templateDataPath);

        var ast = Parser.ParseFile(templatePath, out var tokens, out var errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(templatePath, errors);
        }

        var strings = tokens.OfType<StringToken>();
        var templateStr = ast.ToString();

        // build the output manifest string by inserting stringified AST nodes into the template code
        var lastEndI = 0;
        var sb = new StringBuilder();
        // iterate over all string literals in the template, find the ones matching the template format (`{{TEMPLATE:...}}`)
        foreach (var s in strings) {
            var m = TemplateSubstitutionRegex.Match(s.Value);
            if (!m.Success) {
                continue; // not a template substitution pattern
            }
            sb.Append(templateStr, lastEndI, s.Extent.StartOffset - lastEndI);
            lastEndI = s.Extent.EndOffset;
            sb.Append(substitutionTable[m.Groups[1].Value]);
        }
        sb.Append(templateStr, lastEndI, templateStr.Length - lastEndI);

        return sb.ToString();
    }

    /// Parses the substitution file and returns a dictionary mapping from string keys (referenced in the template)
    /// to the AST node representing the value, which is inserted into the template.
    private static Dictionary<string, StatementAst> ParseSubstitutionFile(string templateDataPath) {
        var ast = Parser.ParseFile(templateDataPath, out _, out var errors);
        if (errors.Length > 0) {
            throw new PackageManifestParseException(templateDataPath, errors);
        }

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
        }).ToDictionary(p => p.Item1, p => p.Item2);
    }
}
