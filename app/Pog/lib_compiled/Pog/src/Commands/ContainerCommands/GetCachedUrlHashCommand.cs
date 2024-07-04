using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;

namespace Pog.Commands.ContainerCommands;

/// <summary>Retrieves a file from the passed URL and calculates the SHA-256 hash, storing the file in the download cache.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "CachedUrlHash")]
[OutputType(typeof(string))]
public class GetCachedUrlHashCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] SourceUrl = null!;

    [Parameter(ValueFromPipeline = true)]
    public UserAgentType UserAgent = default;

    private Package _package = null!;
    private bool _lowPriorityDownload;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var internalInfo = DownloadContainerContext.GetCurrent(this);
        _lowPriorityDownload = internalInfo.LowPriorityDownload;
        _package = internalInfo.Package;
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        var downloadParameters = new DownloadParameters(UserAgent, _lowPriorityDownload);

        foreach (var url in SourceUrl) {
            using var fileLock = InvokePogCommand(new InvokeCachedFileDownload(this) {
                SourceUrl = url,
                StoreInCache = true,
                DownloadParameters = downloadParameters,
                Package = _package,
                ProgressActivity = new() {Activity = "Retrieving file"},
            });

            // we know it's a cache entry lock, since StoreInCache is true
            var cacheLock = (SharedFileCache.CacheEntryLock) fileLock;
            // we don't need the lock, we're only interested in the hash
            cacheLock.Unlock();

            WriteObject(cacheLock.EntryKey);
        }
    }
}
