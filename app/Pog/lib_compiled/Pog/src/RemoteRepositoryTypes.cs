﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JetBrains.Annotations;
using Pog.Utils;

namespace Pog;

internal class RemotePackageDictionary : IEnumerable<KeyValuePair<string, PackageVersion[]>> {
    private readonly List<string> _packageNames = [];
    // also store the key as part of a value, so that we can resolve casing
    // (unfortunately, .NET does not give us access to the stored key inside the hashmap entry)
    private readonly Dictionary<string, (string, PackageVersion[])> _packageVersions =
            new(StringComparer.InvariantCultureIgnoreCase);
    public readonly Stopwatch RetrievalAge = Stopwatch.StartNew();

    /// Assumes that <paramref name="packageJson"/> is pre-sorted, both in package names and in versions.
    public RemotePackageDictionary(JsonElement packageJson) {
        // TODO: error handling
        foreach (var p in packageJson.EnumerateObject()) {
            var versions = p.Value.EnumerateArray().Select(e => new PackageVersion(e.GetString()!)).ToArray();
            _packageNames.Add(p.Name);
            _packageVersions.Add(p.Name, (p.Name, versions));

            Verify.Assert.PackageName(p.Name);
            // check that versions were sorted on server
            Debug.Assert(versions.SequenceEqual(versions.OrderByDescending(v => v)));
        }

        // check that package names were sorted on server
        Debug.Assert(_packageNames.SequenceEqual(_packageNames.OrderBy(pn => pn)));
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
public sealed class RemoteRepository : IRepository, IDisposable {
    private readonly HttpClient _client = new();

    private readonly TimedLazy<RemotePackageDictionary> _packagesLazy;
    internal RemotePackageDictionary Packages => _packagesLazy.Value;

    // each client will be at most 10 minutes out-of-date with the repository; since the package listing is less than 100 kB,
    //  re-downloading it every 10 minutes of active usage shouldn't be much of an issue (especially since users typically
    //  use package managers either for a single invocation, or for multiple invocations in a quick succession when running
    //  in a script)
    private static readonly TimeSpan PackageCacheExpiration = TimeSpan.FromMinutes(10);

    public string Url => _client.BaseAddress.AbsoluteUri;
    public bool Exists {
        get {
            try {
                _ = Packages;
                return true;
            } catch (RepositoryNotFoundException) {
                return false;
            }
        }
    }

    public RemoteRepository(string repositoryBaseUrl) {
        if (!repositoryBaseUrl.EndsWith("/")) {
            repositoryBaseUrl += "/";
        }
        var url = new Uri(repositoryBaseUrl);
        _client.BaseAddress = url;

        _packagesLazy = new(PackageCacheExpiration, () => {
            return new(RetrieveJson("") ??
                       throw new RepositoryNotFoundException($"Package repository does not seem to exist: {Url}"));
        });
    }

    public void Dispose() {
        _client.Dispose();
    }

    public IEnumerable<string> EnumeratePackageNames(string searchPattern = "*") {
        var packageNames = Packages.Keys;
        if (searchPattern == "*") {
            return packageNames;
        }

        // turn the glob pattern into a regex
        var regexStr = "^" + Regex.Escape(searchPattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        var patternRegex = new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return packageNames.Where(p => patternRegex.IsMatch(p));
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

    private async Task<HttpResponseMessage?> RetrieveAsync(string url) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _client.SendAsync(request);

        // don't like having to manually dispose, but don't see any better way
        if (response.StatusCode == HttpStatusCode.NotFound) {
            response.Dispose();
            return null;
        }
        try {
            response.EnsureSuccessStatusCode();
        } catch {
            response.Dispose();
            throw;
        }
        return response;
    }

    private async Task<JsonElement?> RetrieveJsonAsync(string url) {
        using var response = await RetrieveAsync(url);
        if (response == null) return null;
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<ZipArchive?> RetrieveZipArchiveAsync(string url) {
        // do not dispose, otherwise the returned stream would also get closed: https://github.com/dotnet/runtime/issues/28578
        var response = await RetrieveAsync(url);
        if (response == null) return null;
        return new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
    }

    // this should be ok (no deadlocks), PowerShell cmdlets internally do it the same way
    private JsonElement? RetrieveJson(string url) => RetrieveJsonAsync(url).GetAwaiter().GetResult();

    // this should be ok (no deadlocks), PowerShell cmdlets internally do it the same way
    internal ZipArchive? RetrieveZipArchive(string url) => RetrieveZipArchiveAsync(url).GetAwaiter().GetResult();
}

[PublicAPI]
public class RemoteRepositoryVersionedPackage : RepositoryVersionedPackage {
    private readonly RemoteRepository _repository;

    public override IRepository Repository => _repository;
    public string Url => $"{_repository.Url}{HttpUtility.UrlEncode(PackageName)}/";
    public override bool Exists => _repository.Packages.ContainsKey(PackageName);
    protected override string ExpectedPathStr => $"expected URL: {Url}";

    internal RemoteRepositoryVersionedPackage(RemoteRepository repository, string packageName) : base(packageName) {
        _repository = repository;
    }

    public override IEnumerable<PackageVersion> EnumerateVersions(string searchPattern = "*") {
        return _repository.Packages[PackageName] ?? Enumerable.Empty<PackageVersion>();
    }

    public override IEnumerable<string> EnumerateVersionStrings(string searchPattern = "*") {
        return EnumerateVersions(searchPattern).Select(v => v.ToString());
    }

    protected override RepositoryPackage GetPackageUnchecked(PackageVersion version) {
        return new RemoteRepositoryPackage(this, version);
    }
}

public class InvalidRepositoryPackageArchiveException(string message) : Exception(message);

// TODO: rewrite
[PublicAPI]
public sealed class RemoteRepositoryPackage(RemoteRepositoryVersionedPackage parent, PackageVersion version)
        : RepositoryPackage(parent, version), IRemotePackage, IDisposable {
    public string Url {get; init;} = $"{parent.Url}{HttpUtility.UrlEncode(version.ToString())}.zip";
    public override bool Exists => true; // TODO: ...
    private ZipArchive? _manifestArchive = null;

    public void Dispose() {
        _manifestArchive?.Dispose();
    }

    public override void ImportTo(ImportedPackage target) {
        // this also loads the archive
        EnsureManifestIsLoaded();

        target.RemoveManifest();
        // TODO: first, validate that the archive only contains pog.psd1 and the .pog subdirectory
        // TODO: handle exceptions
        _manifestArchive!.ExtractToDirectory(target.Path);

        Debug.Assert(MatchesImportedManifest(target));
    }

    private static Dictionary<string, ZipArchiveEntry?>? MapArchivePathsToDirectory(
            ZipArchive archive, string targetDirectoryPath) {
        var dict = new Dictionary<string, ZipArchiveEntry?>();
        foreach (var entry in archive.Entries) {
            var targetPath = FsUtils.JoinValidateSubdirectory(targetDirectoryPath, entry.FullName);
            if (targetPath == null) {
                return null;
            }

            // add all parent directories to dict for comparison
            var dirPath = Path.GetDirectoryName(targetPath);
            while (dirPath != targetDirectoryPath) {
                dict[dirPath] = null;
                dirPath = Path.GetDirectoryName(dirPath);
            }

            // set value for directories to null
            dict[targetPath] = entry.FullName.EndsWith("/") ? null : entry;
        }
        return dict;
    }

    public override bool MatchesImportedManifest(ImportedPackage p) {
        // this also loads the archive
        EnsureManifestIsLoaded();

        var mappedPathDict = MapArchivePathsToDirectory(_manifestArchive!, p.Path);
        if (mappedPathDict == null) {
            // malicious archive
            return false;
        }

        var manifestFile = new FileInfo(p.ManifestPath);
        if (!manifestFile.Exists) {
            return false;
        }

        IEnumerable<FileSystemInfo> targetEntries = [manifestFile];

        var resourceDir = new DirectoryInfo(p.ManifestResourceDirPath);
        if (resourceDir.Exists) {
            targetEntries = targetEntries.Append(resourceDir)
                    .Concat(resourceDir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories));
        }

        foreach (var entry in targetEntries) {
            if (!mappedPathDict.TryGetValue(entry.FullName, out var archiveEntry)) {
                return false;
            }
            mappedPathDict.Remove(entry.FullName);

            var equal = (entry, archiveEntry) switch {
                (DirectoryInfo, null) => true,
                (FileInfo f, not null) => FsUtils.FileContentEqual(archiveEntry, f),
                _ => false,
            };
            if (!equal) return false;
        }

        if (mappedPathDict.Count != 0) {
            // archive contains some extra files
            return false;
        }

        return true;
    }

    protected override PackageManifest LoadManifest() {
        _manifestArchive = ((RemoteRepository) Container.Repository).RetrieveZipArchive(Url);
        if (_manifestArchive == null) {
            throw new PackageNotFoundException($"Tried to read the package manifest of a non-existent package at '{Url}'.");
        }

        // TODO: handle exceptions
        var manifestEntry = _manifestArchive.GetEntry("pog.psd1") ?? throw new InvalidRepositoryPackageArchiveException(
                $"Repository package is missing a pog.psd1 manifest: {Url}");

        var manifestStr = ReadArchiveEntryAsString(manifestEntry, Encoding.UTF8);
        return new PackageManifest(manifestStr, Url, this);
    }

    private string ReadArchiveEntryAsString(ZipArchiveEntry entry, Encoding encoding) {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, encoding);
        return reader.ReadToEnd();
    }
}
