using System.Collections.Generic;
using System.Text;

namespace Pog.Native;

internal static class Win32Args {
    /// Escapes and combines a list of arguments into a command line string. The invoked command itself must be added separately.
    /// It is an opposite of Win32 `CommandLineToArgv`, except for the command path (since `CommandLineToArgv` has special
    /// handling for the first argument).
    public static string EscapeArguments(IEnumerable<string> arguments) {
        var sb = new StringBuilder();

        var isFirst = true;
        foreach (var arg in arguments) {
            if (!isFirst) sb.Append(' ');
            isFirst = false;

            var (shouldQuote, escapedArg) = EscapeArgumentInner(arg);
            if (shouldQuote) {
                sb.Append('"');
                sb.Append(escapedArg);
                sb.Append('"');
            } else {
                sb.Append(escapedArg);
            }
        }

        return sb.ToString();
    }

    // https://learn.microsoft.com/en-us/archive/blogs/twistylittlepassagesallalike/everyone-quotes-command-line-arguments-the-wrong-way
    // note that there's special handling for the first argument, which is assumed to be a valid filesystem path,
    // but we don't care about that here
    public static string EscapeArgument(string arg) {
        var (shouldQuote, sb) = EscapeArgumentInner(arg);
        if (shouldQuote) {
            return '"' + sb.ToString() + '"';
        } else {
            return sb.ToString();
        }
    }

    /// <returns>Pair of bool, indicating if arg should be quoted, and the escaped arg as a buffer.</returns>
    private static (bool, StringBuilder) EscapeArgumentInner(string arg) {
        var sb = new StringBuilder();

        if (arg == "") {
            // special case, we must quote an empty string
            return (true, sb);
        }

        var shouldQuote = false;
        var backslashCount = 0;
        foreach (var c in arg) {
            switch (c) {
                case '\\':
                    backslashCount++;
                    sb.Append(c);
                    break;

                case ' ':
                case '\t':
                case '\n':
                case '\v':
                    backslashCount = 0;
                    shouldQuote = true;
                    sb.Append(c);
                    break;

                case '"':
                    if (backslashCount > 0) {
                        // escape preceding backslashes
                        for (var i = 0; i < backslashCount; i++) {
                            sb.Append('\\');
                        }
                        backslashCount = 0;
                    }
                    // escape the quote
                    sb.Append('\\');
                    sb.Append(c);
                    break;

                default:
                    backslashCount = 0;
                    sb.Append(c);
                    break;
            }
        }

        if (shouldQuote && backslashCount > 0) {
            // escape backslashes at the end of the argument, so that they don't escape the ending quote
            for (var i = 0; i < backslashCount; i++) {
                sb.Append('\\');
            }
        }

        return (shouldQuote, sb);
    }
}
