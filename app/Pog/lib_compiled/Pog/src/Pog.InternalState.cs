using System;
using System.IO;
using System.Reflection;

namespace Pog;

public static class InternalState {
    private static string GetRootDirPath() {
        const string libDirName = @"\lib_compiled\";
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var i = assemblyPath.LastIndexOf(libDirName, StringComparison.InvariantCultureIgnoreCase);
        if (i < 0) {
            throw new Exception("Could not find the root module directory, cannot initialize Pog.dll.");
        }
        var pogModuleDir = assemblyPath.Substring(0,i);
        return Path.GetFullPath(Path.Combine(pogModuleDir, @"..\..")); // app/Pog/lib_compiled
    }

    private static string? _rootDirPath;
    public static string RootDirPath {
        get => _rootDirPath ??= GetRootDirPath();
        set {
            if (_rootDirPath != null) {
                throw new Exception("Cannot configure the Pog root directory, since there is already a previous " +
                                    "configured path. The path must be set before any Pog modules are used.");
            }
            _rootDirPath = value;
        }
    }

    private static PathConfig? _pathConfig;
    public static PathConfig PathConfig => _pathConfig ??= new PathConfig(RootDirPath);

    private static Repository? _repository;
    public static Repository Repository => _repository ??= new Repository(PathConfig.ManifestRepositoryDir);

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
