using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Commands.Internal;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
public record UrlHashInfo(GetFileHash7Zip.HashAlgorithm Algorithm, string Hash, string Url);

[PublicAPI]
[Cmdlet(VerbsCommon.Get, "UrlHash")]
public class GetUrlHashCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] SourceUrl = null!;

    [Parameter(ValueFromPipeline = true)]
    public DownloadParameters.UserAgentType UserAgent = default;

    private Package _package = null!;
    private bool _lowPriorityDownload;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var internalInfo = Container.ContainerInternalInfo.GetCurrent(this);
        _lowPriorityDownload = (bool) internalInfo.InternalArguments["DownloadLowPriority"];
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

            WriteObject(new UrlHashInfo(default, cacheLock.EntryKey, url));
        }
    }
}
