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
    [PublicAPI]
    public static class PackagePaths {
        public const string ManifestRelPath = "pog.psd1";
        public const string ManifestResourceRelPath = ".pog";
        public const string RepositoryTemplateDirName = ".template";

        public const string ShortcutDirRelPath = ".";
        public const string CommandDirRelPath = "./.commands";
        public const string ShortcutStubDirRelPath = "./.commands/shortcuts";

        internal const string AppDirName = "app";
        internal const string CacheDirName = "cache";
        internal const string LogDirName = "logs";
        internal const string DataDirName = "data";
        internal const string ConfigDirName = "config";

        /// Temporary directory where the previous ./app directory is moved when installing
        /// a new version to support rollback in case of a failed install.
        internal const string AppBackupDirName = ".POG_INTERNAL_app_old";
        /// Temporary directory used for archive extraction.
        internal const string TmpExtractionDirName = ".POG_INTERNAL_install_tmp";
        /// Temporary directory where the new app directory is composed for multi-source installs before moving it in place.
        internal const string NewAppDirName = ".POG_INTERNAL_app_new";
        /// Temporary path where a deleted directory is first moved so that the delete
        /// is an atomic operation with respect to the original location.
        internal const string TmpDeleteDirName = ".POG_INTERNAL_delete_tmp";
    }

    /// Directory where exported shortcuts from packages are copied (system-wide).
    public static readonly string StartMenuSystemExportDir =
            Path.Combine(GetFolderPath(SpecialFolder.CommonStartMenu), "Pog");
    /// Directory where exported shortcuts from packages are copied (per-user).
    public static readonly string StartMenuUserExportDir = Path.Combine(GetFolderPath(SpecialFolder.StartMenu), "Pog");

    private readonly string _appRootDir;
    private readonly string _dataRootDir;
    private readonly string _cacheRootDir;

    public readonly string ContainerDir;
    public readonly string CompiledLibDir;
    public readonly string ExecutableStubPath;

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

    /// Path to the exported 7-Zip binary, needed for package extraction during installation.
    public readonly string Path7Zip;

    public readonly PackageRootConfig PackageRoots;

    // if any new paths are added here, also add them to setup.ps1 in the root directory

    public PathConfig(string rootDirPath) :
            this(Path.Combine(rootDirPath, "app/Pog"), Path.Combine(rootDirPath, "data"), Path.Combine(rootDirPath, "cache")) {}

    public PathConfig(string appRootDirPath, string dataRootDirPath, string cacheRootDirPath) {
        _appRootDir = appRootDirPath;
        _dataRootDir = dataRootDirPath;
        _cacheRootDir = cacheRootDirPath;
        PackageRoots = new PackageRootConfig(Path.Combine(_dataRootDir, "package_roots.txt"));

        ContainerDir = Path.Combine(_appRootDir, "container");
        CompiledLibDir = Path.Combine(_appRootDir, "lib_compiled");
        ExecutableStubPath = Path.Combine(CompiledLibDir, "PogExecutableStubTemplate.exe");

        ExportedCommandDir = Path.Combine(_dataRootDir, "package_bin");
        ManifestRepositoryDir = Path.Combine(_dataRootDir, "manifests");
        ManifestGeneratorDir = Path.Combine(_dataRootDir, "manifest_generators");

        DownloadCacheDir = Path.Combine(_cacheRootDir, "download_cache");
        DownloadTmpDir = Path.Combine(_cacheRootDir, "download_tmp");

        Path7Zip = Path.Combine(ExportedCommandDir, "7z.exe");
    }
}
