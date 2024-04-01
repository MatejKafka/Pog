using System.IO;

namespace Pog.Utils;

internal static class FileSystemInfoExtensions {
    public static string GetBaseName(this FileSystemInfo info) {
        return Path.GetFileNameWithoutExtension(info.Name);
    }
}
