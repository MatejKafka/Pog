using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

public record DownloadParameters(UserAgentType UserAgent = default, bool LowPriorityDownload = false);

public enum UserAgentType {
    // Pog is `default(T)`
    Pog = 0, PowerShell, Browser, Wget,
}

public static class UserAgentTypeExtensions {
    public static string GetHeaderString(this UserAgentType userAgent) {
        // explicitly specify fixed User-Agent strings; that way, we can freely switch implementations without breaking compatibility
        return userAgent switch {
            UserAgentType.Pog => InternalState.HttpClient.UserAgent,
            UserAgentType.PowerShell => "Mozilla/5.0 (Windows NT 10.0; Win64; x64; en-US) PowerShell/5.1.0",
            UserAgentType.Browser => "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
            UserAgentType.Wget => "Wget/1.20.3 (linux-gnu)",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

public class IncorrectFileHashException(string message) : Exception(message);

internal class InvokeCachedFileDownload(PogCmdlet cmdlet) : ScalarCommand<SharedFileCache.IFileLock>(cmdlet) {
    [Parameter(Mandatory = true)] public string SourceUrl = null!;
    [Parameter(Mandatory = true)] public string? ExpectedHash;
    [Parameter(Mandatory = true)] public DownloadParameters DownloadParameters = null!;
    [Parameter(Mandatory = true)] public Package Package = null!;
    [Parameter] public bool StoreInCache = false;
    [Parameter] public ProgressActivity ProgressActivity = new();

    // TODO: handle `InvalidCacheEntryException` everywhere
    public override SharedFileCache.IFileLock Invoke() {
        Debug.Assert(ExpectedHash == null || !StoreInCache);
        // hash must be uppercase
        Debug.Assert(ExpectedHash == null || ExpectedHash == ExpectedHash.ToUpperInvariant());

        WriteVerbose($"Retrieving file from '{SourceUrl}' (or local cache)...");

        if (ExpectedHash != null) {
            WriteDebug($"Checking if we have a cached copy for '{ExpectedHash}'...");
            var entryLock = GetEntryLockedWithCleanup(ExpectedHash);
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
        WriteDebug($"Using temporary directory '{downloadDirPath}'.");

        Directory.CreateDirectory(downloadDirPath);
        try {
            var downloadedFilePath = InvokePogCommand(new InvokeFileDownload(Cmdlet) {
                SourceUrl = SourceUrl,
                DownloadParameters = DownloadParameters,
                DestinationDirPath = downloadDirPath,
                ProgressActivity = ProgressActivity,
            });

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
                var entryLock = GetEntryLockedWithCleanup(hash);
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

    private SharedFileCache.CacheEntryLock? GetEntryLockedWithCleanup(string hash) {
        try {
            return InternalState.DownloadCache.GetEntryLocked(hash, Package);
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

    /// Utility class to cleanup a downloaded file from the download directory when it's no longer needed.
    /// Instances of this class are returned for files without a known hash.
    public sealed class TmpFileLock : SharedFileCache.IFileLock {
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
}
