using System;
using System.Collections;
using System.Management.Automation;
using JetBrains.Annotations;

namespace Pog;

public class InvalidGeneratorManifestException(string message) : Exception(message);

[PublicAPI]
public class PackageGeneratorManifest {
    public readonly Hashtable Raw;
    public readonly string Path;

    public readonly ScriptBlock ListVersionsSb;
    public readonly ScriptBlock? GenerateSb;

    internal PackageGeneratorManifest(string generatorPath) {
        Path = generatorPath;
        Raw = PackageManifestParser.LoadManifest(generatorPath);

        var listVersions = Raw["ListVersions"] ?? throw new InvalidGeneratorManifestException(
                $"Package generator is missing the required 'ListVersions' ScriptBlock, at '{Path}'.");
        // optional
        var generate = Raw["Generate"];

        if (listVersions is ScriptBlock listVersionsSb) {
            ListVersionsSb = listVersionsSb;
        } else {
            throw new InvalidGeneratorManifestException(
                    "Package generator property 'ListVersions' must be a ScriptBlock, " +
                    $"got '{listVersions.GetType()}', at '{Path}'.");
        }

        if (generate is ScriptBlock generateSb) {
            GenerateSb = generateSb;
        } else if (generate != null) {
            throw new InvalidGeneratorManifestException(
                    "Package generator property 'Generate' must be a ScriptBlock if present, " +
                    $"got '{listVersions.GetType()}', at '{Path}'.");
        }
    }
}
