using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands;

namespace Pog.Commands.Common;

[PublicAPI]
public abstract class PogSourceHashCommandBase : RepositoryPackageCommand {
    private const string ImportedPS = "ImportedPackage";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ImportedPS, ValueFromPipeline = true)]
    public ImportedPackage[] ImportedPackage = null!; // accept even imported packages

    protected override void ProcessRecord() {
        if (ParameterSetName != ImportedPS) {
            base.ProcessRecord();
            return;
        }

        // TODO: do this in parallel (even for packages passed as array)
        foreach (var package in ImportedPackage) {
            ProcessPackage(package);
        }
    }

    protected sealed override void ProcessPackage(RepositoryPackage package) => ProcessPackage(package);
    protected abstract void ProcessPackage(Package package);

    protected string RetrieveSourceHash(Package package, PackageSource source, string url) {
        using var fileLock = InvokePogCommand(new InvokeCachedFileDownload(this) {
            SourceUrl = url,
            Package = package,
            DownloadParameters = new DownloadParameters(source.UserAgent),
            ProgressActivity = new() {Activity = "Retrieving file"},
            StoreInCache = true,
        });

        // we know it's a cache entry lock, since `StoreInCache` is true
        return ((SharedFileCache.CacheEntryLock) fileLock).EntryKey;
        // fileLock is unlocked here
    }
}
