using System;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;
using IOPath = System.IO.Path;

namespace Pog;

[PublicAPI]
public class TmpDirectory {
    public readonly string Path;

    public TmpDirectory(string tmpDirPath) {
        Path = tmpDirPath;
    }

    public string GetTemporaryPath() {
        var path = IOPath.Combine(Path, Guid.NewGuid().ToString());
        // the chance is very small...
        Debug.Assert(!Directory.Exists(path) && !File.Exists(path));
        return path;
    }
}