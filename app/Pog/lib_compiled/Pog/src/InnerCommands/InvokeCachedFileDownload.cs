using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

/// Internal struct used to simplify passing all HTTP request parameters between cmdlets.
internal record struct DownloadParameters(UserAgentType UserAgent = default);

public class IncorrectFileHashException(string message) : Exception(message);

internal class InvokeCachedFileDownload(PogCmdlet cmdlet) : ScalarCommand<SharedFileCache.IFileLock>(cmdlet) {
    [Parameter] public required string SourceUrl;
    [Parameter] public required DownloadParameters DownloadParameters;
    [Parameter] public required Package Package;
    [Parameter] public string? ExpectedHash = null;
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
            var file = InvokePogCommand(new InvokeFileDownload(Cmdlet) {
                SourceUrl = SourceUrl,
                DownloadParameters = DownloadParameters,
                DestinationDirPath = downloadDirPath,
                ProgressActivity = ProgressActivity,
                ComputeHash = ExpectedHash != null || StoreInCache,
            });

            if (file.Hash == null) {
                WriteVerbose("Returning the downloaded file directly.");
                return new TmpFileLock(file.Path!, downloadDirPath, File.OpenRead(file.Path!));
            }

            if (ExpectedHash != null && file.Hash != ExpectedHash) {
                throw new IncorrectFileHashException(
                        $"File downloaded from '{SourceUrl}' has an incorrect checksum, seems that the file was changed " +
                        $"since the package was created (expected checksum: '{ExpectedHash}', actual: '{file.Hash}').");
            }

            WriteVerbose($"Adding the downloaded file to the local cache under the key '{file.Hash}'.");
            return AddEntryToCache(file.Hash, InternalState.DownloadCache.PrepareNewEntry(downloadDirPath, Package));
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

    /// Utility class to clean up a downloaded file from the download directory when it's no longer needed.
    /// Instances of this class are returned for files without a known hash.
    [PublicAPI]
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
