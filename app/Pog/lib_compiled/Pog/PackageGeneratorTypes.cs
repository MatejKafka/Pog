using IOPath = System.IO.Path;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

public class PackageGeneratorNotFoundException : FileNotFoundException {
    public PackageGeneratorNotFoundException(string message) : base(message) {}
}

[PublicAPI]
public class GeneratorRepository {
    public readonly string Path;

    public GeneratorRepository(string generatorRepositoryDirPath) {
        Path = generatorRepositoryDirPath;
    }

    public IEnumerable<string> EnumerateGeneratorNames(string searchPattern = "*") {
        return FsUtils.EnumerateNonHiddenFileNames(Path, searchPattern + ".psd1")
                .Select(IOPath.GetFileNameWithoutExtension);
    }

    public IEnumerable<PackageGenerator> Enumerate(string searchPattern = "*") {
        var repo = this;
        return EnumerateGeneratorNames(searchPattern).Select(p => new PackageGenerator(repo, p));
    }

    public PackageGenerator GetPackage(string packageName, bool resolveName, bool mustExist) {
        Verify.PackageName(packageName);
        if (resolveName) {
            packageName = FsUtils.GetResolvedChildName(Path, packageName);
        }
        var package = new PackageGenerator(this, packageName);
        if (mustExist && !package.Exists) {
            throw new PackageGeneratorNotFoundException(
                    $"Package generator '{package.PackageName}' does not exist, expected path: {package.Path}");
        }
        return package;
    }
}

[PublicAPI]
public class PackageGenerator {
    public readonly string PackageName;
    [Hidden] public readonly string Path;
    [Hidden] public bool Exists => File.Exists(Path);

    private PackageGeneratorManifest? _manifest;
    [Hidden]
    public PackageGeneratorManifest Manifest {
        get {
            if (_manifest == null) ReloadManifest();
            return _manifest!;
        }
    }

    internal PackageGenerator(GeneratorRepository repository, string packageName) {
        Path = IOPath.Combine(repository.Path, $"{packageName}.psd1");
        PackageName = packageName;
    }

    /// <exception cref="PackageManifestNotFoundException">Thrown if the package generator does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package generator file is not a valid PowerShell data file (.psd1).</exception>
    public void ReloadManifest() {
        _manifest = new PackageGeneratorManifest(Path);
    }
}
