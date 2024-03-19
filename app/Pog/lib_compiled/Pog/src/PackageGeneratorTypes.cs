using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using IOPath = System.IO.Path;

namespace Pog;

public class PackageGeneratorNotFoundException(string message) : FileNotFoundException(message);

[PublicAPI]
public class GeneratorRepository {
    public readonly string Path;

    public GeneratorRepository(string generatorRepositoryDirPath) {
        Path = generatorRepositoryDirPath;
    }

    public IEnumerable<string> EnumerateGeneratorFileNames(string searchPattern = "*") {
        return FsUtils.EnumerateNonHiddenFileNames(Path, searchPattern + ".psd1");
    }

    public IEnumerable<string> EnumerateGeneratorNames(string searchPattern = "*") {
        return EnumerateGeneratorFileNames(searchPattern).Select(IOPath.GetFileNameWithoutExtension);
    }

    public IEnumerable<PackageGenerator> Enumerate(string searchPattern = "*") {
        return EnumerateGeneratorFileNames(searchPattern)
                .Select(mn => new PackageGenerator(IOPath.Combine(Path, mn), IOPath.GetFileNameWithoutExtension(mn)));
    }

    public PackageGenerator GetPackage(string packageName, bool resolveName, bool mustExist) {
        Verify.PackageName(packageName);

        var manifestName = $"{packageName}.psd1";
        if (resolveName) {
            manifestName = FsUtils.GetResolvedChildName(Path, manifestName);
            packageName = IOPath.GetFileNameWithoutExtension(manifestName);
        }

        var package = new PackageGenerator(IOPath.Combine(Path, manifestName), packageName);
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
    public readonly string Path;
    public bool Exists => File.Exists(Path);

    private PackageGeneratorManifest? _manifest;
    public PackageGeneratorManifest Manifest => _manifest ?? ReloadManifest();

    internal PackageGenerator(string generatorPath, string packageName) {
        Path = generatorPath;
        PackageName = packageName;
    }

    /// <exception cref="PackageManifestNotFoundException">Thrown if the package generator does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package generator file is not a valid PowerShell data file (.psd1).</exception>
    public PackageGeneratorManifest ReloadManifest() {
        return _manifest = new PackageGeneratorManifest(Path);
    }
}
