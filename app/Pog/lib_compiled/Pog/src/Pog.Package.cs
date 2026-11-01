using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pog;

public class PackageNotFoundException(string message) : DirectoryNotFoundException(message);

/// Base class for all kinds of packages, both installed (<see cref="ImportedPackage"/>) or available from
/// a repository (<see cref="RepositoryPackage"/>).
public abstract class Package {
    public readonly string PackageName;
    public abstract bool Exists {get;}

    internal string? RawManifest {get; private set;}

    private PackageManifest? _manifest;
    public PackageManifest Manifest => EnsureManifestIsLoaded();

    internal bool ManifestLoaded => _manifest != null;

    protected Package(string packageName) {
        Verify.Assert.PackageName(packageName);
        PackageName = packageName;
    }

    protected void InvalidateManifest() {
        RawManifest = null;
        _manifest = null;
    }

    /// <inheritdoc cref="ReloadManifest"/>
    public PackageManifest EnsureManifestIsLoaded() => _manifest ?? ReloadManifest();

    /// <inheritdoc cref="ReadManifest"/>
    /// <inheritdoc cref="ParseManifest"/>
    public PackageManifest ReloadManifest() {
        (RawManifest, var source) = ReadManifest();
        return _manifest = ParseManifest(RawManifest, source);
    }

    // for now, keep the async stuff internal; expose it later when I have some coherent idea
    //  on if and how to provide an async public API
    /// <inheritdoc cref="ReadManifest"/>
    /// <inheritdoc cref="ParseManifest"/>
    internal async Task<PackageManifest> ReloadManifestAsync(CancellationToken token = default) {
        (RawManifest, var source) = await ReadManifestAsync(token).ConfigureAwait(false);
        return _manifest = ParseManifest(RawManifest, source);
    }

    /// <exception cref="PackageManifestParseException">The package manifest file is not a valid PowerShell data file (.psd1).</exception>
    /// <exception cref="InvalidPackageManifestStructureException">The package manifest is a valid data file, but the structure is not valid.</exception>
    private PackageManifest ParseManifest(string manifestStr, string source) {
        return new(manifestStr, source, this as RepositoryPackage);
    }

    protected record ManifestText(string Manifest, string Source);

    /// <exception cref="PackageNotFoundException">The package directory does not exist.</exception>
    /// <exception cref="PackageManifestNotFoundException">The package manifest file does not exist.</exception>
    protected abstract ManifestText ReadManifest();

    protected virtual Task<ManifestText> ReadManifestAsync(CancellationToken token = default) {
        return Task.Run(ReadManifest, token);
    }

    public abstract string GetDescriptionString();
}

public interface ILocalPackage {
    string Path {get;}
    string ManifestPath {get;}
}

public interface IRemotePackage {
    string Url {get;}
}
