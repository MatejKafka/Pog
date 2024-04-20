using System;
using System.IO;
using System.Reflection;
using System.Threading;
using JetBrains.Annotations;
using Pog.Utils.Http;

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

    /// Debug method, used for testing. Not thread-safe.
    [PublicAPI]
    public static bool InitDataRoot(string dataRootDirPath) {
        if (_pathConfig == null) {
            _pathConfig = new PathConfig(GetRootDirPath(), dataRootDirPath);
            return true;
        }
        return false;
    }

    /// Configure the package repository used to retrieve package manifests. Not thread-safe.
    [PublicAPI]
    public static bool InitRepository(Func<IRepository> repositoryGenerator) {
        if (_repository == null) {
            _repository = repositoryGenerator();
            return true;
        }
        return false;
    }

    private static PathConfig? _pathConfig;
    public static PathConfig PathConfig => LazyInitializer.EnsureInitialized(ref _pathConfig,
            () => new PathConfig(GetRootDirPath()))!;

    private static IRepository? _repository;
    public static IRepository Repository => LazyInitializer.EnsureInitialized(ref _repository,
            () => new RemoteRepository(PathConfig.DefaultRemoteRepositoryUrl))!;

    private static GeneratorRepository? _generatorRepository;
    public static GeneratorRepository GeneratorRepository => LazyInitializer.EnsureInitialized(ref _generatorRepository,
            () => new GeneratorRepository(PathConfig.ManifestGeneratorDir))!;

    private static ImportedPackageManager? _importedPackageManager;
    public static ImportedPackageManager ImportedPackageManager => LazyInitializer.EnsureInitialized(
            ref _importedPackageManager,
            () => new ImportedPackageManager(PathConfig.PackageRoots))!;

    private static TmpDirectory? _tmpDownloadDirectory;
    public static TmpDirectory TmpDownloadDirectory => LazyInitializer.EnsureInitialized(ref _tmpDownloadDirectory,
            () => new TmpDirectory(PathConfig.DownloadTmpDir))!;

    private static SharedFileCache? _downloadCache;
    public static SharedFileCache DownloadCache => LazyInitializer.EnsureInitialized(ref _downloadCache,
            () => new SharedFileCache(PathConfig.DownloadCacheDir, TmpDownloadDirectory))!;

    private static PogHttpClient? _httpClient;
    /// Shared HttpClient singleton instance, used by all other classes.
    internal static PogHttpClient HttpClient => LazyInitializer.EnsureInitialized(ref _httpClient,
            () => new PogHttpClient())!;
}
