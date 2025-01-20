using System.Collections;
using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public record PackageUserManifest {
    public readonly string Path;

    /// Indicates whether this package should be updated by `Update-Pog`.
    public readonly bool Frozen = false;

    public PackageUserManifest(string userManifestPath) {
        InstrumentationCounter.UserManifestLoads.Increment();

        Path = userManifestPath;

        Hashtable raw;
        try {
            raw = PackageManifestParser.LoadManifest(userManifestPath).Parsed;
        } catch (PackageManifestNotFoundException) {
            // user manifest will typically not exist, we're ok with it, use the defaults
            return;
        }

        // parse the raw manifest into properties on this object
        var parser = new HashtableParser(raw);

        if (parser.ParseScalar<bool>("Frozen", false) is {} frozen) {
            Frozen = frozen;
        }

        if (parser.HasIssues) {
            throw new InvalidPackageManifestStructureException(userManifestPath, parser.Issues);
        }
    }
}
