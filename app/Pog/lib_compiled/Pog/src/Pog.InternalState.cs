using System;
using System.IO;
using System.Reflection;
using JetBrains.Annotations;

namespace Pog;

public static class InternalState {
#if DEBUG
    static InternalState() {
        // ensure POG_DEBUG is set when importing the debug build of Pog.dll to avoid mode inconsistency
        //  between the container and the main runspace
        if (Environment.GetEnvironmentVariable("POG_DEBUG") == null) {
            Environment.SetEnvironmentVariable("POG_DEBUG", "1");
        }
    }
#endif

#if DEBUG
    public const bool DebugBuild = true;
#else
    public const bool DebugBuild = false;
#endif

    private static string GetRootDirPath() {
        const string libDirName = @"\lib_compiled\";
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var i = assemblyPath.LastIndexOf(libDirName, StringComparison.InvariantCultureIgnoreCase);
        if (i < 0) {
            throw new Exception("Could not find the root module directory, cannot initialize Pog.dll.");
        }
        var pogModuleDir = assemblyPath.Substring(0, i);
        return Path.GetFullPath(Path.Combine(pogModuleDir, @"..\..")); // app/Pog/lib_compiled
    }

    /// Debug method, used for testing.
    [UsedImplicitly]
    public static void OverrideDataRoot(string dataRootDirPath) {
        if (_pathConfig != null) {
            throw new Exception("Cannot override Pog paths, since PathConfig was already configured, " +
                                "probably due to auto-configuration on the first access.");
        }
        _pathConfig = new PathConfig(GetRootDirPath(), dataRootDirPath);
    }

    private static PathConfig? _pathConfig;
    public static PathConfig PathConfig => _pathConfig ??= new PathConfig(GetRootDirPath());

    private static IRepository? _repository;
    public static IRepository Repository => _repository ??= new LocalRepository(PathConfig.ManifestRepositoryDir);

    private static GeneratorRepository? _generatorRepository;
    public static GeneratorRepository GeneratorRepository =>
            _generatorRepository ??= new GeneratorRepository(PathConfig.ManifestGeneratorDir);

    private static ImportedPackageManager? _importedPackageManager;
    public static ImportedPackageManager ImportedPackageManager =>
            _importedPackageManager ??= new ImportedPackageManager(PathConfig.PackageRoots);

    private static TmpDirectory? _tmpDownloadDirectory;
    public static TmpDirectory TmpDownloadDirectory => _tmpDownloadDirectory ??= new TmpDirectory(PathConfig.DownloadTmpDir);

    private static SharedFileCache? _downloadCache;
    public static SharedFileCache DownloadCache =>
            _downloadCache ??= new SharedFileCache(PathConfig.DownloadCacheDir, TmpDownloadDirectory);
}
