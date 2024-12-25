using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Pog.Utils;

public static class DiffRenderer {
    private const string AnsiReset = "\e[0m";
    private const string AnsiRed = "\e[31m";
    private const string AnsiGreen = "\e[32m";

    public static string RenderDiff(string origText, string newText,
            bool ignoreMatching = false, bool ignoreWhitespace = false) {
        var sb = new StringBuilder();
        foreach (var d in InlineDiffBuilder.Diff(origText, newText, ignoreWhitespace).Lines) {
            switch (d.Type) {
                case ChangeType.Inserted: sb.Append(AnsiGreen).Append(d.Text).Append(AnsiReset).AppendLine(); break;
                case ChangeType.Deleted: sb.Append(AnsiRed).Append(d.Text).Append(AnsiReset).AppendLine(); break;
                default:
                    if (!ignoreMatching) {
                        sb.Append(d.Text).AppendLine();
                    }
                    break;
            }
        }

        // remove trailing newline, if any
        if (sb.Length > 0) {
            sb.Remove(sb.Length - 1, 1);
        }
        return sb.ToString();
    }
}
