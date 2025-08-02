using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

[PublicAPI]
public record PackageSourceHash(string Url, string Hash, string? ExpectedHash, Package Package) {
    public bool? Matches => ExpectedHash == null ? null : Hash == ExpectedHash;
}

/// <summary>Downloads all resources needed to install the given package and returns the SHA-256 hash for each resource.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "PogSourceHash", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
[OutputType(typeof(PackageSourceHash))]
public class GetPogSourceHashCommand : PogSourceHashCommandBase {
    protected sealed override void ProcessPackage(Package package) {
        package.EnsureManifestIsLoaded();
        if (package.Manifest.Install == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Install block.");
            return;
        }

        foreach (var source in package.Manifest.EvaluateInstallUrls(package)) {
            var url = (string) source.Url;
            var hash = RetrieveSourceHash(package, source, url);

            WriteObject(new PackageSourceHash(url, hash, source.ExpectedHash, package));
        }
    }
}
