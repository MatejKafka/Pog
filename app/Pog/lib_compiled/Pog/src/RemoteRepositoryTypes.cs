using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using JetBrains.Annotations;
using Pog.Utils;

namespace Pog;

/// <summary>Thrown when a remote repository has an incorrect structure or metadata.</summary>
public class InvalidRemoteRepositoryException : Exception {
    public InvalidRemoteRepositoryException(string message) : base(message) {}
    public InvalidRemoteRepositoryException(string message, Exception innerException) : base(message, innerException) {}
}

internal class RemotePackageDictionary : IEnumerable<KeyValuePair<string, PackageVersion[]>> {
    private readonly List<string> _packageNames = [];
    // also store the key as part of a value, so that we can resolve casing
    // (unfortunately, .NET does not give us access to the stored key inside the hashmap entry)
    private readonly Dictionary<string, (string, PackageVersion[])> _packageVersions =
            new(StringComparer.InvariantCultureIgnoreCase);

    /// Assumes that <paramref name="packageJson"/> is pre-sorted, both in package names and in versions.
    public RemotePackageDictionary(JsonElement packageJson, string url) {
        try {
            foreach (var p in packageJson.EnumerateObject()) {
                var versions = p.Value.EnumerateArray()
                        .Select(e => new PackageVersion(e.GetString() ?? throw new InvalidDataException()))
                        .ToArray();
                _packageNames.Add(p.Name);
                _packageVersions.Add(p.Name, (p.Name, versions));

                Verify.Assert.PackageName(p.Name);
                // check that versions were sorted on server
                Debug.Assert(versions.SequenceEqual(versions.OrderByDescending(v => v)));
            }
        } catch (InvalidDataException e) {
            throw new InvalidRemoteRepositoryException(
                    $"Remote repository at '{url}' is invalid, has incorrect structure of the package index JSON file. " +
                    $"This is likely the result of an incorrectly generated repository. Please, notify the maintainer of " +
                    $"the repository.", e);
        }

        // check that package names were sorted on server
        Debug.Assert(_packageNames.SequenceEqual(_packageNames.OrderBy(pn => pn, StringComparer.OrdinalIgnoreCase)));
    }

    public PackageVersion[]? this[string key] => _packageVersions.TryGetValue(key, out var val) ? val.Item2 : null;
    public IEnumerable<string> Keys => _packageNames;
    public bool ContainsKey(string key) => _packageVersions.ContainsKey(key);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public string ResolvePackageName(string packageName) {
        return _packageVersions.TryGetValue(packageName, out var val) ? val.Item1 : packageName;
    }

    public IEnumerator<KeyValuePair<string, PackageVersion[]>> GetEnumerator() {
        return _packageNames.Select(pn => new KeyValuePair<string, PackageVersion[]>(pn, this[pn]!))
                .GetEnumerator();
    }
}

[PublicAPI]
public sealed class RemoteRepository : IRepository {
    private readonly TimedLazy<RemotePackageDictionary> _packagesLazy;
    internal RemotePackageDictionary Packages => _packagesLazy.Value;

    // each client will be at most 10 minutes out-of-date with the repository; since the package index is less than 100 kB,
    //  re-downloading it every 10 minutes of active usage shouldn't be much of an issue (especially since users typically
    //  use package managers either for a single invocation or for multiple invocations in a quick succession when running
    //  in a script)
    private static readonly TimeSpan PackageCacheExpiration = TimeSpan.FromMinutes(10);

    /// Resolved URL of the remote repository, including the version.
    public readonly string Url;

    public bool Exists {
        get {
            try {
                _ = Packages;
                return true;
            } catch (RepositoryNotFoundException) {
                return false;
            } catch (HttpRequestException) {
                return false;
            }
        }
    }

    public RemoteRepository(string repositoryBaseUrl) {
        if (!repositoryBaseUrl.EndsWith("/", StringComparison.Ordinal)) {
            repositoryBaseUrl += "/";
        }

        var url = new Uri(repositoryBaseUrl);
        var urlRepoVersion = ParseUrlVersionSegment(url.Segments.LastOrDefault());
        if (urlRepoVersion == null) {
            // if we do not have a version suffix, append v2/ so that the base unversioned URL can be used for any version
            url = new(url, "v2/");
        } else if (urlRepoVersion != 2) {
            throw new InvalidRemoteRepositoryException($"Unsupported repository URL version 'v{urlRepoVersion}', " +
                                                       $"only 'v2' is supported by this Pog version: {url}");
        }

        Url = url.ToString();
        // FIXME: we're doing a network request while holding a mutex over the cache, other threads will be stuck waiting
        //  until this one finishes the request
        _packagesLazy = new(PackageCacheExpiration, DownloadRepositoryRoot);
    }

    private static int? ParseUrlVersionSegment(string? segment) {
        if (string.IsNullOrEmpty(segment)) {
            return null;
        }
        if (segment![0] == 'v' && int.TryParse(segment.Substring(1, segment.Length - 2), out var version)) {
            return version;
        } else {
            return null;
        }
    }

    private RemotePackageDictionary DownloadRepositoryRoot() {
        try {
            // FIXME: blocking without possible cancellation
            var result = InternalState.HttpClient.RetrieveJsonAsync(new(Url)).GetAwaiter().GetResult();
            if (result == null) {
                throw new RepositoryNotFoundException($"Package repository does not seem to exist: {Url}");
            }
            return new(result.Value, Url);
        } catch (HttpRequestException e) {
            throw new HttpRequestException($"Failed to fetch a remote package repository at '{Url}': {e.Message}", e);
        }
    }

    /// List of available packages and their versions is cached locally for a short duration.
    /// This method invalidates the cache, causing it to be retrieved again on the next access.
    public void InvalidateCache() {
        _packagesLazy.Invalidate();
    }

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        var packageNames = Packages.Keys;
        if (searchPattern == "*") {
            return packageNames;
        }

        var pattern = new WildcardPattern(searchPattern, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
        return packageNames.Where(p => pattern.IsMatch(p));
    }

    public IEnumerable<RepositoryVersionedPackage> Enumerate(string searchPattern = "*") {
        var repo = this;
        return EnumeratePackageNames(searchPattern).Select(p => new RemoteRepositoryVersionedPackage(repo, p));
    }

    public RepositoryVersionedPackage GetPackage(string packageName, bool resolveName, bool mustExist) {
        Verify.PackageName(packageName);
        if (resolveName) {
            packageName = Packages.ResolvePackageName(packageName);
        }

        var package = new RemoteRepositoryVersionedPackage(this, packageName);
        if (mustExist && !package.Exists) {
            throw new RepositoryPackageNotFoundException(
                    $"Package '{package.PackageName}' does not exist in the repository, expected URL: {package.Url}");
        }
        return package;
    }
}

[PublicAPI]
public class RemoteRepositoryVersionedPackage : RepositoryVersionedPackage {
    private readonly RemoteRepository _repository;

    public override IRepository Repository => _repository;
    public string Url => $"{_repository.Url}{HttpUtility.UrlPathEncode(PackageName)}/";
    public override bool Exists => _repository.Packages.ContainsKey(PackageName);
    internal override string ExpectedPathStr => $"expected URL: {Url}";

    internal RemoteRepositoryVersionedPackage(RemoteRepository repository, string packageName) : base(packageName) {
        _repository = repository;
    }

    public override IEnumerable<PackageVersion> EnumerateVersions(string searchPattern = "*") {
        var versions = _repository.Packages[PackageName];
        if (versions == null) {
            return [];
        }
        if (searchPattern == "*") {
            return versions;
        }

        var pattern = new WildcardPattern(searchPattern, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
        return versions.Where(v => pattern.IsMatch(v.ToString()));
    }

    public override IEnumerable<string> EnumerateVersionStrings(string searchPattern = "*") {
        return EnumerateVersions(searchPattern).Select(v => v.ToString());
    }

    protected override RepositoryPackage GetPackageUnchecked(PackageVersion version) {
        return new RemoteRepositoryPackage(this, version);
    }
}

[PublicAPI]
public sealed class RemoteRepositoryPackage(RemoteRepositoryVersionedPackage parent, PackageVersion version)
        : RepositoryPackage(parent, version), IRemotePackage {
    public string Url {get; init;} = $"{parent.Url}{HttpUtility.UrlPathEncode(version.ToString())}.psd1";
    public override bool Exists => ManifestLoaded || ExistsInPackageList();
    internal override string ExpectedPathStr => $"expected URL: {Url}";

    private bool ExistsInPackageList() {
        var repo = (RemoteRepository) ((RemoteRepositoryVersionedPackage) Container).Repository;
        var versions = repo.Packages[PackageName];
        // version list is sorted in descending order, use a descending comparer
        return versions != null && Array.BinarySearch(versions, Version, new PackageVersion.DescendingComparer()) >= 0;
    }

    protected override PackageManifest LoadManifest() {
        // FIXME: cancellation
        return LoadManifestAsync().GetAwaiter().GetResult();
    }

    // originally, the remote package manifest was a zip archive that contained the manifest and an optional .pog dir;
    //  since the .pog dir was almost unused, and it complicated some aspects of package installation, it is no longer
    //  supported; in the `v2` format, the manifest is provided directly
    protected override async Task<PackageManifest> LoadManifestAsync(CancellationToken token = default) {
        var manifestStr = await InternalState.HttpClient.RetrieveTextAsync(new(Url), token).ConfigureAwait(false);
        if (manifestStr == null) {
            if (Exists) {
                throw new InvalidRemoteRepositoryException(
                        $"Package '{PackageName}' is listed in the remote repository index, but was not found at '{Url}'. " +
                        $"This indicates either a malformed repository or an incompatible repository version.");
            } else {
                throw new PackageNotFoundException(
                        $"Package '{PackageName}' does not exist in remote repository, {ExpectedPathStr}");
            }
        }
        return new PackageManifest(manifestStr, Url, owningPackage: this);
    }
}
