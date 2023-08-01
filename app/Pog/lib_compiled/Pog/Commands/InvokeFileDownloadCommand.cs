using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Internal;

namespace Pog.Commands;

/// Internal container command for downloading files during installation.
/// supported use cases:
//  - download a file with a known hash, for installation
//    = pass -ExpectedHash
//  - download a file with an unknown hash, cannot be cached
//    = do not pass -ExpectedHash, do not pass -StoreInCache
//  - retrieve a file to find its hash, then cache it
//    = do not pass -ExpectedHash, pass -StoreInCache
[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "FileDownload", DefaultParameterSetName = "Hash")]
[OutputType(typeof(InvokeFileDownload.TmpFileLock), typeof(SharedFileCache.CacheEntryLock))]
public class InvokeFileDownloadCommand : PSCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string SourceUrl = null!;

    [Parameter(ParameterSetName = "Hash")]
    [Alias("Hash")]
    [Verify.Sha256Hash]
    public string? ExpectedHash;

    [Parameter] public DownloadParameters DownloadParameters = new();
    [Parameter(Mandatory = true)] public Package Package = null!;
    [Parameter(ParameterSetName = "NoHash")] public SwitchParameter StoreInCache;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        ExpectedHash = ExpectedHash?.ToUpper();

        WriteObject(InvokeFileDownload.Invoke(this, SourceUrl, ExpectedHash, DownloadParameters, Package, StoreInCache));
    }
}
