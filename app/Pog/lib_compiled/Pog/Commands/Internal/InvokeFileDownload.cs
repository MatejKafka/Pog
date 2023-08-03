using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;

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

public class InvokeFileDownload : Command {
    private string _sourceUrl = null!;
    private string? _expectedHash;
    private DownloadParameters _downloadParameters = null!;
    private Package _package = null!;
    private bool _storeInCache = false;

    public static SharedFileCache.IFileLock Invoke(PSCmdlet cmdlet, string sourceUrl, string? expectedHash,
            DownloadParameters downloadParameters, Package package, bool storeInCache) {
        Debug.Assert(!(expectedHash != null && storeInCache));
        return new InvokeFileDownload(cmdlet) {
            _sourceUrl = sourceUrl,
            _expectedHash = expectedHash,
            _downloadParameters = downloadParameters,
            _package = package,
            _storeInCache = storeInCache,
        }.DoInvoke();
    }

    private InvokeFileDownload(PSCmdlet cmdlet) : base(cmdlet) {}

    // TODO: handle `InvalidCacheEntryException` everywhere
    private SharedFileCache.IFileLock DoInvoke() {
        WriteVerbose($"Retrieving file from '{_sourceUrl}' (or local cache)...");

        if (_expectedHash != null) {
            WriteDebug($"Checking if we have a cached copy for '{_expectedHash}'...");
            var entryLock = GetEntryLockedWithCleanup(_expectedHash, _package);
            if (entryLock != null) {
                WriteInformation($"File retrieved from the local cache: '{_sourceUrl}'");
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

        WriteInformation($"Downloading file from '{_sourceUrl}'.");

        // TODO: hold a handle to the tmp directory during download, so that another process can safely delete stale entries
        //  (typically after a crash) without accidentally deleting a live entry
        var downloadDirPath = InternalState.TmpDownloadDirectory.GetTemporaryPath();
        Directory.CreateDirectory(downloadDirPath);
        WriteDebug($"Using temporary directory '{downloadDirPath}'.");
        try {
            var downloadedFilePath = DownloadFile(_sourceUrl, downloadDirPath, _downloadParameters);

            if (_expectedHash == null && !_storeInCache) {
                WriteVerbose("Returning the downloaded file directly.");
                return new TmpFileLock(downloadedFilePath, downloadDirPath, File.OpenRead(downloadedFilePath));
            }

            var hash = GetFileHash7ZipCommand.Invoke(downloadedFilePath);
            if (_expectedHash != null && hash != _expectedHash) {
                // incorrect hash
                ThrowTerminatingError(new ErrorRecord(
                        new IncorrectFileHashException($"Incorrect hash for the file downloaded from '{_sourceUrl}'"
                                                       + $" (expected: '{_expectedHash}', real: '{hash}')."),
                        "IncorrectHash", ErrorCategory.InvalidResult, _sourceUrl));
            }

            WriteVerbose($"Adding the downloaded file to the local cache under the key '{hash}'.");
            return AddEntryToCache(hash, InternalState.DownloadCache.PrepareNewEntry(downloadDirPath, _package));
        } catch {
            FileUtils.EnsureDeleteDirectory(downloadDirPath);
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
                var entryLock = GetEntryLockedWithCleanup(hash, _package);
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
    private string DownloadFile(string sourceUrl, string destinationDirPath, DownloadParameters downloadParameters) {
        var bitsParams = new Hashtable {
            {"Source", sourceUrl},
            {"Destination", destinationDirPath},
            {"Description", $"Downloading '{sourceUrl}' to '{destinationDirPath}'."},
            // passing -Dynamic allows BITS to communicate with badly-mannered servers that don't support HEAD requests,
            //  Content-Length headers,...; see https://docs.microsoft.com/en-us/windows/win32/api/bits5_0/ne-bits5_0-bits_job_property_id),
            //  section BITS_JOB_PROPERTY_DYNAMIC_CONTENT
            {"Dynamic", true},
            {"Priority", downloadParameters.LowPriorityDownload ? "Low" : "Foreground"},
        };

        switch (downloadParameters.UserAgent) {
            case DownloadParameters.UserAgentType.PowerShell:
                break;
            case DownloadParameters.UserAgentType.Browser:
                WriteDebug("Using fake browser (Firefox) user agent.");
                bitsParams["CustomHeaders"] =
                        "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:105.0) Gecko/20100101 Firefox/105.0";
                break;
            case DownloadParameters.UserAgentType.Wget:
                WriteDebug("Using fake wget user agent.");
                bitsParams["CustomHeaders"] = "User-Agent: Wget/1.20.3 (linux-gnu)";
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // FIXME: BITS does not respect content-disposition HTTP headers, so the downloaded file name may be nonsense (like "stable" for VS Code);
        //  probably go back to using `iwr` to retrieve the final URL, file name,... and only use BITS for the final download

        // invoke BITS; it's possible to invoke it directly using the .NET API, but that seems overly complex for now
        StartBitsTransferSb.InvokeReturnAsIs(bitsParams);
        // download finished, find the file path
        var files = Directory.GetFiles(destinationDirPath);
        Debug.Assert(files.Length == 1);
        return files[0];
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
