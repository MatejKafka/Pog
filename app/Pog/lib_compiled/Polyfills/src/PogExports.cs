using Polyfills.System.IO;

namespace Polyfills;

// Private Pog class used to expose useful internals of the polyfills to Pog.
public static class PogExports {
    public static string? GetSymbolicLinkTarget(string linkPath) {
        return FileSystem.GetLinkTarget(linkPath);
    }
}