using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pog;

public class PackageNotFoundException(string message) : DirectoryNotFoundException(message);

public abstract class Package {
    public readonly string PackageName;
    public abstract bool Exists {get;}

    private PackageManifest? _manifest;
    public PackageManifest Manifest => EnsureManifestIsLoaded();

    internal bool ManifestLoaded => _manifest != null;

    protected Package(string packageName, PackageManifest? manifest) {
        Verify.Assert.PackageName(packageName);
        PackageName = packageName;
        _manifest = manifest;
    }

    protected void InvalidateManifest() {
        _manifest = null;
    }

    /// <inheritdoc cref="LoadManifest"/>
    public PackageManifest EnsureManifestIsLoaded() {
        return _manifest ?? ReloadManifest();
    }

    /// <inheritdoc cref="LoadManifest"/>
    public PackageManifest ReloadManifest() {
        return _manifest = LoadManifest();
    }

    // for now, keep the async stuff internal; expose it later when I have some coherent idea
    //  on if and how to provide an async public API
    /// <inheritdoc cref="LoadManifest"/>
    internal async Task<PackageManifest> ReloadManifestAsync(CancellationToken token = default) {
        return _manifest = await LoadManifestAsync(token).ConfigureAwait(false);
    }

    /// <exception cref="PackageNotFoundException">The package directory does not exist.</exception>
    /// <exception cref="PackageManifestNotFoundException">The package manifest file does not exist.</exception>
    /// <exception cref="PackageManifestParseException">The package manifest file is not a valid PowerShell data file (.psd1).</exception>
    /// <exception cref="InvalidPackageManifestStructureException">The package manifest is a valid data file, but the structure is not valid.</exception>
    protected abstract PackageManifest LoadManifest();

    protected virtual Task<PackageManifest> LoadManifestAsync(CancellationToken token = default) {
        return Task.Run(LoadManifest, token);
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
