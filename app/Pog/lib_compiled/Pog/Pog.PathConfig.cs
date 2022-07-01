using System;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using static System.Environment;

namespace Pog;

// TODO: add support for adding and removing package roots, and cache the loaded package root list
//  it is quite complex to do it in a robust way that supports simultaneous changes from multiple instances of Pog,
//  and even harder to make it performant; the provided package roots should always reflect current state of the
//  config file (so for caching, we'll need a file watcher thread to update the cache)
[PublicAPI]
public class PackageRootConfig {
    public readonly string PackageRootFile;

    public string[] ValidPackageRoots => ReadPackageRoots(Directory.Exists);
    public string[] MissingPackageRoots => ReadPackageRoots(r => !Directory.Exists(r));
    public string[] AllPackageRoots => ReadPackageRoots(_ => true);

    internal PackageRootConfig(string packageRootFilePath) {
        PackageRootFile = packageRootFilePath;
    }

    private string[] ReadPackageRoots(Func<string, bool> pathPredicate) {
        return File.ReadLines(PackageRootFile, Encoding.UTF8)
            .Select(Path.GetFullPath)
            .Where(pathPredicate)
            .ToArray();
    }
}

[PublicAPI]
public class PathConfig {
    // TODO: change repo manifest structure to match this (move pog.psd1 outside .pog)
    public const string PackageManifestRelPath = "pog.psd1";
    public static readonly string[] PackageManifestCleanupPaths = {"pog.psd1", ".pog"};
    
    /// Directory where exported shortcuts from packages are copied (system-wide).
    public static readonly string StartMenuSystemExportDir =
        Path.Combine(GetFolderPath(SpecialFolder.CommonStartMenu), "Pog");
    /// Directory where exported shortcuts from packages are copied (per-user).
    public static readonly string StartMenuUserExportDir = Path.Combine(GetFolderPath(SpecialFolder.StartMenu), "Pog");

    private readonly string _dataRootDir;
    private readonly string _cacheRootDir;

    public readonly string ExportedCommandDir;
    public readonly string ManifestRepositoryDir;
    public readonly string ManifestGeneratorDir;

    /// Directory where package files with known hash are cached.
    public readonly string DownloadCacheDir;
    /// Directory where package files without known hash are downloaded and stored during installation.
    /// A custom directory is used over system $env:TMP directory, because sometimes we move files
    /// from this dir to download cache, and if the system directory was on a different partition,
    /// this move could be needlessly expensive.
    public readonly string DownloadTmpDir;

    public readonly PackageRootConfig PackageRoots;

    public PathConfig(string rootDirPath) :
        this(Path.Combine(rootDirPath, "data"), Path.Combine(rootDirPath, "cache")) {}

    public PathConfig(string dataRootDirPath, string cacheRootDirPath) {
        this._dataRootDir = dataRootDirPath;
        this._cacheRootDir = cacheRootDirPath;
        this.PackageRoots = new PackageRootConfig(Path.Combine(this._dataRootDir, "package_roots.txt"));

        this.ExportedCommandDir = Path.Combine(this._dataRootDir, "package_bin");
        this.ManifestRepositoryDir = Path.Combine(this._dataRootDir, "manifests");
        this.ManifestGeneratorDir = Path.Combine(this._dataRootDir, "manifest_generators");

        this.DownloadCacheDir = Path.Combine(this._cacheRootDir, "download_cache");
        this.DownloadTmpDir = Path.Combine(this._cacheRootDir, "download_tmp");
    }
}