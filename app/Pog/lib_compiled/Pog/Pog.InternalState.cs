using System.IO;
using System.Reflection;
using JetBrains.Annotations;

namespace Pog;

public static class InternalState {
    [PublicAPI]
    public static readonly string RootDirPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, @"..\..\..")); // app/Pog/lib_compiled/Pog.dll

    private static PathConfig? _pathConfig;
    [PublicAPI] public static PathConfig PathConfig => _pathConfig ??= new PathConfig(RootDirPath);

    private static Repository? _repository;
    [PublicAPI] public static Repository Repository => _repository ??= new Repository(PathConfig.ManifestRepositoryDir);

    private static PackageRootManager? _packageRootManager;
    [PublicAPI]
    public static PackageRootManager PackageRootManager =>
            _packageRootManager ??= new PackageRootManager(PathConfig.PackageRoots);
}