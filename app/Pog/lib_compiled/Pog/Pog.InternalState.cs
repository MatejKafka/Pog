using System.IO;
using System.Reflection;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public static class InternalState {
    public static readonly string RootDirPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, @"..\..\..")); // app/Pog/lib_compiled/Pog.dll

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