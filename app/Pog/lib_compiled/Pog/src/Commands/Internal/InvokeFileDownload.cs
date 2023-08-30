using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Utils.Http;

namespace Pog.Commands.Internal;

[PublicAPI]
public record DownloadParameters(
        DownloadParameters.UserAgentType UserAgent = DownloadParameters.UserAgentType.PowerShell,
        bool LowPriorityDownload = false) {
    [PublicAPI]
    public enum UserAgentType {
        PowerShell, Browser, Wget,
    }

    internal string? GetUserAgentHeaderString() {
        return UserAgent switch {
            UserAgentType.PowerShell => null,
            UserAgentType.Browser =>
                    "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:105.0) Gecko/20100101 Firefox/105.0",
            UserAgentType.Wget => "User-Agent: Wget/1.20.3 (linux-gnu)",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

public class InvokeFileDownload : ScalarCommand<SharedFileCache.IFileLock>, IDisposable {
    [Parameter(Mandatory = true)] public string SourceUrl = null!;
    [Parameter(Mandatory = true)] public string? ExpectedHash;
    [Parameter(Mandatory = true)] public DownloadParameters DownloadParameters = null!;
    [Parameter(Mandatory = true)] public Package Package = null!;
    [Parameter] public bool StoreInCache = false;

    private readonly CancellationTokenSource _stopping = new();

    public InvokeFileDownload(PogCmdlet cmdlet) : base(cmdlet) {}

    public void Dispose() {
        _stopping.Dispose();
    }

    public override void StopProcessing() {
        base.StopProcessing();
        _stopping.Cancel();
    }

    // TODO: handle `InvalidCacheEntryException` everywhere
    public override SharedFileCache.IFileLock Invoke() {
        Debug.Assert(ExpectedHash == null || !StoreInCache);
        // hash must be uppercase
        Debug.Assert(ExpectedHash == null || ExpectedHash == ExpectedHash.ToUpperInvariant());

        WriteVerbose($"Retrieving file from '{SourceUrl}' (or local cache)...");

        if (ExpectedHash != null) {
            WriteDebug($"Checking if we have a cached copy for '{ExpectedHash}'...");
            var entryLock = GetEntryLockedWithCleanup(ExpectedHash, Package);
            if (entryLock != null) {
                WriteInformation($"File retrieved from the local cache: '{SourceUrl}'");
                // do not validate the hash; it was already validated once when the entry was first downloaded;
                //  most archives and binaries already have a checksum that's verified during extraction/execution,
                //  which should be enough to detect accidental corruption of the stored file
                return entryLock;
            }
            // the entry does not exist yet, download it and then add it into the cache
            WriteVerbose("File not found in the local cache.");
        } else {
            WriteVerbose("No hash provided, cannot use the local cache.");
        }

        WriteInformation($"Downloading file from '{SourceUrl}'.");

        // TODO: hold a handle to the tmp directory during download, so that another process can safely delete stale entries
        //  (typically after a crash) without accidentally deleting a live entry
        var downloadDirPath = InternalState.TmpDownloadDirectory.GetTemporaryPath();
        Directory.CreateDirectory(downloadDirPath);
        WriteDebug($"Using temporary directory '{downloadDirPath}'.");
        try {
            var downloadedFilePath = DownloadFile(new Uri(SourceUrl), downloadDirPath, DownloadParameters);

            if (ExpectedHash == null && !StoreInCache) {
                WriteVerbose("Returning the downloaded file directly.");
                return new TmpFileLock(downloadedFilePath, downloadDirPath, File.OpenRead(downloadedFilePath));
            }

            var hash = InvokePogCommand(new GetFileHash7Zip(Cmdlet) {
                Path = downloadedFilePath,
            });

            if (ExpectedHash != null && hash != ExpectedHash) {
                // incorrect hash
                ThrowTerminatingError(new ErrorRecord(
                        new IncorrectFileHashException($"Incorrect hash for the file downloaded from '{SourceUrl}'"
                                                       + $" (expected: '{ExpectedHash}', real: '{hash}')."),
                        "IncorrectHash", ErrorCategory.InvalidResult, SourceUrl));
            }

            WriteVerbose($"Adding the downloaded file to the local cache under the key '{hash}'.");
            return AddEntryToCache(hash, InternalState.DownloadCache.PrepareNewEntry(downloadDirPath, Package));
        } catch {
            FsUtils.EnsureDeleteDirectory(downloadDirPath);
            throw;
        }
    }

    private SharedFileCache.CacheEntryLock AddEntryToCache(string hash, SharedFileCache.NewCacheEntry entry) {
        // try to add the entry; if it already exists (was added before the download finished), try to retrieve it
        while (true) {
            try {
                return InternalState.DownloadCache.AddEntryLocked(hash, entry);
            } catch (CacheEntryAlreadyExistsException) {
                WriteVerbose("File is already cached.");
                var entryLock = GetEntryLockedWithCleanup(hash, Package);
                if (entryLock == null) {
                    continue; // retry
                }

                // we have an existing cache entry, delete the downloaded file
                try {
                    WriteDebug("Deleting downloaded file, already exists in the local cache.");
                    Directory.Delete(entry.DirPath, true);
                } catch {
                    entryLock.Unlock();
                    throw;
                }
                return entryLock;
            }
        }
    }

    private SharedFileCache.CacheEntryLock? GetEntryLockedWithCleanup(string hash, Package package) {
        try {
            return InternalState.DownloadCache.GetEntryLocked(hash, package);
        } catch (InvalidCacheEntryException) {
            WriteWarning($"Found an invalid download cache entry '{hash}', replacing...");
            // TODO: figure out how to handle the failure more gracefully (maybe skip the cache all-together
            //  if we don't manage to delete the entry even after the download completes?)
            try {
                InternalState.DownloadCache.DeleteEntry(hash);
                return null;
            } catch {
                // deletion failed, fall-through, rethrow the original exception
            }
            throw;
        }
    }

    /// <summary>Downloads the file from $SrcUrl to $TargetDir.</summary>
    /// <returns>Full path of the downloaded file.</returns>
    /// <remarks>
    /// BITS transfer (https://docs.microsoft.com/en-us/windows/win32/bits/about-bits) is used for download. Originally,
    /// Invoke-WebRequest was used in some cases, and it was potentially faster for small files for cold downloads (BITS
    /// service takes some time to initialize when not used for a while), but the added complexity does not seem to be worth it.
    /// </remarks>>
    /// <remarks>
    /// I experimented with different download tools: iwr, curl, BITS, aria2
    ///  - iwr is quite slow; internally, it uses HttpClient, so I'm assuming using it directly will also be slow
    ///  - BITS, curl and aria2 are roughly the same speed, with curl being the fastest by ~10%
    ///    (download test with 'VS Code' followed by SHA-256 hash validation, aria2 11.9s, curl 10.6s, BITS 11.3s)
    ///  - BITS should in theory be a bit better mannered on heavily loaded systems, but I haven't experimented with it,
    ///    except for the `-Priority Low` parameter
    ///  - BITS does not support Content-Disposition, and it takes the filename from the original source URL,
    ///    but messes up if it contains a query string and fails while trying to create an invalid file name;
    ///    if the .NET module was used directly instead of the PowerShell cmdlets, this might not be an issue
    ///  - aria2 supports validating the checksum, but either it does so after the file is downloaded or it's
    ///    just slow in general, since curl followed by a separate hash check is faster
    /// </remarks>
    private string DownloadFile(Uri sourceUrl, string destinationDirPath, DownloadParameters downloadParameters) {
        // BITS powershell module has issues with resolving URLs with query strings (apparently, it assumes everything after
        //  the last / is a file name) and it uses just the source URL instead of the final URL or Content-Disposition,
        //  so we resolve the file name ourselves

        // originally, filename and final URL was resolved before BITS was invoked, but that causes extra latency,
        //  because BITS has to reopen a new connection for the download, so we now do the resolution and download
        //  in parallel and reconcile them at the end; that's non-ideal, since we're duplicating work, but works
        //  well-enough for now

        var origUrlFileName = HttpFileNameParser.GetDownloadedFileName(sourceUrl, null);
        var tmpDownloadTarget = $"{destinationDirPath}\\{origUrlFileName}";

        Task<ResolveDownloadTarget.DownloadTarget>? resolverTask = null;
        if (Path.GetExtension(origUrlFileName) is ".zip" or ".7z" or ".exe" or ".tgz" ||
            origUrlFileName.EndsWith(".tar.gz")) {
            // original URL seems to have a sensible filename extension, assume it's correct and do not try
            //  to resolve the final filename
        } else {
            // run the resolver in parallel to the download
            resolverTask = ResolveDownloadTarget.ResolveFinalDownloadTargetAsync(_stopping.Token, sourceUrl, downloadParameters);
        }

        var bitsParams = new Hashtable {
            {"Source", sourceUrl.OriginalString},
            {"Destination", tmpDownloadTarget},
            {"DisplayName", $"Installing '{Package.PackageName}'"},
            {"Description", $"Downloading '{sourceUrl.OriginalString}'..."},
            // passing -Dynamic allows BITS to communicate with badly-mannered servers that don't support HEAD requests,
            //  Content-Length headers,...; see https://docs.microsoft.com/en-us/windows/win32/api/bits5_0/ne-bits5_0-bits_job_property_id),
            //  section BITS_JOB_PROPERTY_DYNAMIC_CONTENT
            {"Dynamic", true},
            {"Priority", downloadParameters.LowPriorityDownload ? "Low" : "Foreground"},
        };

        if (downloadParameters.GetUserAgentHeaderString() is {} userAgentStr) {
            WriteDebug($"Using a spoofed user agent: {userAgentStr}");
            bitsParams["CustomHeaders"] = "User-Agent: " + userAgentStr;
        }

        // invoke BITS; it's possible to invoke it directly using the .NET API, but that seems overly complex for now
        StartBitsTransferSb.InvokeReturnAsIs(bitsParams);

        Debug.Assert(File.Exists(tmpDownloadTarget));

        if (resolverTask == null) {
            // original filename is "good enough", use it
            return tmpDownloadTarget;
        } else {
            // retrieve the resolved target
            ResolveDownloadTarget.DownloadTarget target;
            try {
                // this should be ok (no deadlocks), PowerShell cmdlets internally do it the same way
                target = resolverTask.GetAwaiter().GetResult();
            } catch (TaskCanceledException) {
                throw new PipelineStoppedException();
            }

            var (httpHeadSupported, resolvedUrl, contentDisposition) = target;
            var resolvedFileName = HttpFileNameParser.GetDownloadedFileName(resolvedUrl, contentDisposition);
            var resolvedTarget = $"{destinationDirPath}\\{resolvedFileName}";

            if (!httpHeadSupported) {
                WriteDebug("Server seems to not support HEAD requests.");
            }
            WriteDebug($"Resolved target: {resolvedTarget}");

            // rename the downloaded file
            File.Move(tmpDownloadTarget, resolvedTarget);
            return resolvedTarget;
        }
    }

    private static readonly ScriptBlock StartBitsTransferSb =
            ScriptBlock.Create(@"param($p) Import-Module BitsTransfer; BitsTransfer\Start-BitsTransfer @p");

    /// Utility class to cleanup a downloaded file from the download directory when it's no longer needed.
    /// Instances of this class are returned for files without a known hash.
    public class TmpFileLock : SharedFileCache.IFileLock {
        public string Path {get;}
        public FileStream ReadStream {get;}
        private readonly string _dirPath;
        private bool _locked = true;

        internal TmpFileLock(string filePath, string containerDirPath, FileStream readStream) {
            Path = filePath;
            ReadStream = readStream;
            _dirPath = containerDirPath;
        }

        public void Unlock() {
            if (!_locked) return;
            ReadStream.Close();
            Directory.Delete(_dirPath, true);
            _locked = false;
        }

        public void Dispose() {
            Unlock();
        }
    }

    public class IncorrectFileHashException : Exception {
        public IncorrectFileHashException(string message) : base(message) {}
    }
}
