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
//
// TODO: think through whether it would be a good idea to support relative paths in the package root file
//  if yes, which directory should they be relative from?
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
        InstrumentationCounter.PackageRootFileReads++;
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
        public const string ManifestFileName = "pog.psd1";
        public const string ManifestResourceDirName = ".pog";
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

    public const string DefaultRemoteRepositoryUrl = "https://matejkafka.github.io/PogPackages/";

    /// Directory where exported shortcuts from packages are copied (system-wide).
    public static readonly string StartMenuSystemExportDir =
            Path.Combine(GetFolderPath(SpecialFolder.CommonStartMenu), "Pog");
    /// Directory where exported shortcuts from packages are copied (per-user).
    public static readonly string StartMenuUserExportDir = Path.Combine(GetFolderPath(SpecialFolder.StartMenu), "Pog");

    public readonly string ContainerDir;
    public readonly string CompiledLibDir;
    public readonly string ExecutableStubPath;
    public readonly string VcRedistDir;

    public readonly string ExportedCommandDir;
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
    /// Path to the exported OpenedFilesView binary, needed to list package processes locking files during installation.
    public readonly string PathOpenedFilesView;

    public readonly PackageRootConfig PackageRoots;

    public PathConfig(string rootDirPath) : this(rootDirPath, rootDirPath) {}

    public PathConfig(string appRootDirPath, string dataRootDirPath) :
            this($"{appRootDirPath}\\app\\Pog", $"{dataRootDirPath}\\data", $"{dataRootDirPath}\\cache") {}

    // if any new paths are added here, also add them to setup.ps1 in the root directory
    public PathConfig(string appRootDirPath, string dataRootDirPath, string cacheRootDirPath) {
        PackageRoots = new PackageRootConfig($"{dataRootDirPath}\\package_roots.txt");

        ContainerDir = $"{appRootDirPath}\\container";
        CompiledLibDir = $"{appRootDirPath}\\lib_compiled";
        ExecutableStubPath = $"{CompiledLibDir}\\PogExecutableStubTemplate.exe";
        VcRedistDir = $"{CompiledLibDir}\\vc_redist";

        ExportedCommandDir = $"{dataRootDirPath}\\package_bin";
        ManifestGeneratorDir = $"{dataRootDirPath}\\manifest_generators";

        DownloadCacheDir = $"{cacheRootDirPath}\\download_cache";
        DownloadTmpDir = $"{cacheRootDirPath}\\download_tmp";

        Path7Zip = $"{ExportedCommandDir}\\7z.exe";
        PathOpenedFilesView = $"{ExportedCommandDir}\\OpenedFilesView.exe";
    }
}
