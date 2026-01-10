using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Pog.Utils;

namespace Pog;

public class RepositoryNotFoundException(string message) : Exception(message);

public class RepositoryPackageNotFoundException(string message) : PackageNotFoundException(message);

public class RepositoryPackageVersionNotFoundException(string message) : PackageNotFoundException(message);

// it would be nice if we could express in the type system that we have 2 separate hierarchies
//  (e.g. RemoteRepository always returns RemoteRepositoryVersionedPackage), but since .netstandard2.0 does not support
//  covariant return types, we would have to express it with generics, which would prevent us from having a list of repositories
[PublicAPI]
public interface IRepository {
    public bool Exists {get;}
    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*");
    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*");
    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist);
}

/// Allows the user to access multiple repositories at the same time in a descending order of priority.
[PublicAPI]
public class RepositoryList(IRepository[] repositories) : IRepository {
    public readonly IRepository[] Repositories = repositories;
    public bool Exists => Repositories.All(r => r.Exists);

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        // deduplicate shadowed packages from multiple repositories
        return Repositories.SelectMany(repo => repo.EnumeratePackageNames(searchPattern))
                .Distinct()
                .OrderBy(pn => pn, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*") {
        // allow "duplicate" packages here, since they're actually distinct
        return Repositories.SelectMany(repo => repo.Enumerate(searchPattern))
                .OrderBy(p => p.PackageName, StringComparer.OrdinalIgnoreCase);
    }

    // FIXME: figure out how to support combining package versions for a single package from multiple repositories
    //  e.g. repo 1 has Firefox v100, repo 2 has Firefox v101; if you search for Firefox v101, the search will fail,
    //  because this method will return Firefox from repo 1, and Find-Pog will not enumerate repo 2 at all;
    // FIXME: related ^, `Find-Pog p` will only return a single `p` package, but `Find-Pog `*p*` will return both
    // TODO: what to do with same-named packages with different versions in different repos? we could have
    //  a RepositoryVersionedPackageList which combines multiple packages (the interface seems open enough), but I'm not sure
    //  it makes sense to implement this, maybe it's best to just keep the current behavior
    //  options:
    //  1) merge available package versions across all repos with RepositoryVersionedPackageList (imo it's too risky,
    //     but it's roughly what e.g. `apt` does, so users might be familiar with it; hardest to implement)
    //  2) take the first package found and ignore anything later (currently implemented, imo reasonable compromise)
    //  3) if a query is ambiguous (multiple matching packages from different repos), error out and force the user
    //     to explicitly specify the repo to use (most robust, easy to implement but have to always poll all repos,
    //     also need to add UI for specifying the repo, which probably makes the UI a bit more complex)
    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist) {
        foreach (var r in Repositories) {
            var p = r.GetPackage(packageName, resolveName, false);
            if (p.Exists) {
                return p;
            }
        }
        throw new RepositoryPackageNotFoundException(
                $"Package {packageName} does not exist in any of the configured repositories.");
    }
}

/// <summary>
/// Class representing a package with multiple available versions. Each version is represented by an `IRepositoryPackage`.
/// </summary>
/// The backing directory may not exist, in which case the enumeration methods behave as if there were no existing versions.
[PublicAPI]
public abstract class RepositoryVersionedPackage {
    public readonly string PackageName;
    public abstract IRepository Repository {get;}
    public abstract bool Exists {get;}

    protected RepositoryVersionedPackage(string packageName) {
        Verify.Assert.PackageName(packageName);
        PackageName = packageName;
    }

    internal abstract string ExpectedPathStr {get;}
    protected abstract RepositoryPackage GetPackageUnchecked(PackageVersion version);

    /// Enumerate UNORDERED versions of the package.
    public abstract IEnumerable<string> EnumerateVersionStrings(string searchPattern = "*");

    /// Enumerate parsed versions of the package, in a DESCENDING order.
    public virtual IEnumerable<PackageVersion> EnumerateVersions(string searchPattern = "*") {
        return EnumerateVersionStrings(searchPattern)
                .Select(v => new PackageVersion(v))
                .OrderByDescending(v => v);
    }

    /// Enumerate packages for versions matching the pattern, in a DESCENDING order according to the version.
    public IEnumerable<RepositoryPackage> Enumerate(string searchPattern = "*") {
        return EnumerateVersions(searchPattern).Select(GetPackageUnchecked);
    }

    public RepositoryPackage? GetLatestPackageOrNull() {
        var latestVersion = EnumerateVersionStrings().Select(v => new PackageVersion(v)).Max();
        return latestVersion == null ? null : GetPackageUnchecked(latestVersion);
    }

    public RepositoryPackage GetLatestPackage() {
        return GetLatestPackageOrNull() ?? throw new RepositoryPackageVersionNotFoundException(
                $"Package '{PackageName}' in the repository does not have any versions, {ExpectedPathStr}");
    }

    public RepositoryPackage GetVersionPackage(string version, bool mustExist) {
        return GetVersionPackage(new PackageVersion(version), mustExist);
    }

    public virtual RepositoryPackage GetVersionPackage(PackageVersion version, bool mustExist) {
        var package = GetPackageUnchecked(version);
        if (mustExist && !package.Exists) {
            throw new RepositoryPackageVersionNotFoundException(
                    $"Package '{PackageName}' in the repository does not have version '{version}', {package.ExpectedPathStr}");
        }
        return package;
    }
}

/// Class representing a package available for installation from a repository (either local or remote).
[PublicAPI]
public abstract class RepositoryPackage(RepositoryVersionedPackage parent, PackageVersion version)
        : Package(parent.PackageName, null) {
    public readonly RepositoryVersionedPackage Container = parent;
    public readonly PackageVersion Version = version;

    internal abstract string ExpectedPathStr {get;}

    public override string GetDescriptionString() => $"package '{PackageName}', version '{Version}'";
    public override string ToString() => $"{this.GetType().FullName}({PackageName} v{Version})";

    public bool MatchesImportedManifest(ImportedPackage p) {
        var importedManifest = new FileInfo(p.ManifestPath);
        if (!importedManifest.Exists) {
            return false;
        } else {
            var repoManifestBytes = Encoding.UTF8.GetBytes(Manifest.ToString());
            return FsUtils.FileContentEqual(repoManifestBytes, importedManifest);
        }
    }

    public void ImportTo(ImportedPackage target, bool backup = false) {
        // load manifest to ensure that it is valid, which also loads the archive for remote packages
        EnsureManifestIsLoaded();

        // ensure target directory exists
        Directory.CreateDirectory(target.Path);

        // remove any previous manifest
        target.RemoveManifest(true);

        try {
            // actually import the manifest
            // we're loading the manifest anyway for validation, so write it out directly without going back to the filesystem;
            //  this risks inconsistencies between the filesystem and the cached manifest, but since we expect the package
            //  objects to be short-lived, this shouldn't be an issue
            File.WriteAllText(target.ManifestPath, Manifest.ToString());
        } catch {
            target.RestoreManifestBackup();
            throw;
        }

        if (!backup) {
            // we successfully finished the import and caller does not want to preserve the backup, remove it
            target.RemoveManifestBackup();
        }

        Debug.Assert(MatchesImportedManifest(target));
    }
}
