using System.Collections.Generic;
using JetBrains.Annotations;

namespace Pog;

// TODO: finish
[PublicAPI]
public class GeneratorRepository {
    public readonly string Path;

    public GeneratorRepository(string generatorRepositoryDirPath) {
        Path = generatorRepositoryDirPath;
    }

    public IEnumerable<string> EnumerateGeneratorNames(string searchPattern = "*") {
        return PathUtils.EnumerateNonHiddenDirectoryNames(Path, searchPattern);
    }
}