namespace Pog.Utils;

public static class StringExtensions {
    public static string? StripPrefix(this string str, string prefix) {
        return str.StartsWith(prefix) ? str.Substring(prefix.Length) : null;
    }
}
