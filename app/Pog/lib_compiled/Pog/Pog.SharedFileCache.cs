using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using IOPath = System.IO.Path;

namespace Pog;

[PublicAPI]
public class InvalidCacheEntryException : Exception {
    public readonly string EntryKey;

    public InvalidCacheEntryException(string entryKey) : base($"Invalid cache entry: '{entryKey}'") {
        EntryKey = entryKey;
    }

    public InvalidCacheEntryException(string entryKey, string message) : base(message) {
        EntryKey = entryKey;
    }
}

[PublicAPI]
public class CacheEntryAlreadyExistsException : Exception {
    public readonly string EntryKey;

    public CacheEntryAlreadyExistsException(string entryKey) :
            base($"Cache entry already exists: '{entryKey}'") {
        EntryKey = entryKey;
    }
}

public class InvalidAddedCacheEntryException : Exception {
    public InvalidAddedCacheEntryException(string message) : base(message) {}
}

[PublicAPI]
public class CacheEntryInUseException : Exception {
    public readonly string EntryKey;

    public CacheEntryInUseException(string entryKey) : base(
            $"Cannot delete the cache entry, it is currently in use: '{entryKey}'") {
        EntryKey = entryKey;
    }
}

// cache structure:
//  - each subdirectory in the main directory is a cache entry
//  - each cache entry contains exactly 2 files:
//     - one is called `referencingPackages.json-list`, it contains a list of packages that accessed this entry
//     - second one has any other name, and is the actual cache entry; during insertion, if the entry file is also called
//       `referencingPackages.json-list`, it is prefixed with `_`
[PublicAPI]
public class SharedFileCache {
    private const string MetadataFileName = "referencingPackages.json-list";

    public readonly string Path;
    /// Directory for temporary files on the same volume as `.Path`, used for adding and removing entries.
    private readonly TmpDirectory _tmpDirectory;

    public SharedFileCache(string cacheDirPath, TmpDirectory tmpDir) {
        Path = cacheDirPath;
        _tmpDirectory = tmpDir;
    }

    public delegate void InvalidCacheEntryCb(InvalidCacheEntryException exception);

    public IEnumerable<CacheEntryInfo> EnumerateEntries(InvalidCacheEntryCb? invalidEntryCb = null) {
        // the last .Where is necessary to avoid race conditions (cache entry exists when entries are enumerated,
        //  but is deleted before `GetEntryInfoInner(...)` finishes)
        return PathUtils.EnumerateNonHiddenDirectoryNames(Path)
                .Select(entryKey => {
                    try {
                        return GetEntryInfoInner(entryKey);
                    } catch (InvalidCacheEntryException e) {
                        invalidEntryCb?.Invoke(e);
                        return null;
                    }
                })
                .Where(e => e != null)!;
    }

    // NOTE: this enumerable must never be publicly returned, we need to hold the read lock during the whole read
    private static IEnumerable<SourcePackageMetadata> EnumerateMetadataFileStream(FileStream stream) {
        var reader = new StreamReader(stream);
        while (reader.ReadLine() is {} line) {
            if (line == "") {
                continue;
            }
            var parsed = SourcePackageMetadata.ParseFromJson(line);
            if (parsed != null) {
                yield return parsed;
            }
        }
    }

    private static SourcePackageMetadata[]? ReadMetadataFile(string metadataPath) {
        try {
            using var stream = File.Open(metadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // lock the whole file in a shared read-only mode
            using var regionLock = Win32.LockFile(stream, Win32.LockFileFlags.WAIT, 0, ulong.MaxValue);
            return EnumerateMetadataFileStream(stream).ToArray();
        } catch (FileNotFoundException) {
            return null;
        }
    }

    private void AddPackageMetadata(string metadataPath, SourcePackageMetadata packageInfo) {
        using var stream = File.Open(metadataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        // lock the whole file in RW mode
        using var regionLock = Win32.LockFile(stream, Win32.LockFileFlags.EXCLUSIVE_LOCK | Win32.LockFileFlags.WAIT,
                0, ulong.MaxValue);

        // ensure that we're not adding a duplicate
        if (EnumerateMetadataFileStream(stream).Any(pm => pm == packageInfo)) {
            // already included in the list
            // we used this cache entry, refresh last write time, even though the file did not change
            File.SetLastWriteTime(metadataPath, DateTime.Now);
            return;
        }

        // ensure that we have a new line
        if (stream.Length > 0) {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != (byte) '\n') {
                stream.WriteByte((byte) '\n');
            }
        }

        // write the serialized metadata into the file
        // NOTE: since we use \n as a record separator, we must not pretty-print the JSON
        JsonSerializer.Serialize(stream, packageInfo);
    }

    /// Lock the entry directory, allowing read/write, but not deletion.
    /// <returns>`null` in case the entry does not exist, a disposable read handle to the directory otherwise.</returns>
    /// <exception cref="FileLoadException">The directory is locked by someone else with an incompatible mode.</exception>
    private SafeFileHandle? LockEntryDirectory(string entryDirPath) {
        for (var i = 0;; i++) {
            try {
                return Win32.OpenDirectoryReadOnly(entryDirPath);
            } catch (FileNotFoundException) {
                // yes, really, it's not DirectoryNotFoundException
                // the entry does not exist
                return null;
            } catch (FileLoadException) {
                // cannot open the directory, it is already open with an incompatible sharing mode
                // this typically means that some other cache instance has opened the entry for deletion
                if (i < 30) {
                    // we'll try spinning for a bit; Sleep(1) doesn't use up too much CPU and also gives us
                    //  a negligible latency from the user's point of view
                    // you might think that spinning is dumb and we could just open the directory with FileShare.Delete;
                    //  that looks like a good plan, but that would let the deleting cache instance move the directory right
                    //  from under our hands, thus not really locking it
                    Thread.Sleep(1);
                } else {
                    // let the exception bubble; ~30ms is a suspiciously long duration and we don't want
                    //  to hang indefinitely in case something unexpected is going on
                    throw;
                }
            }
        }
    }

    private readonly record struct EntryContentInfo(FileInfo Metadata, FileInfo Entry);

    // Expects the entry to be already locked.
    private EntryContentInfo? GetEntryContentInfo(string entryDirPath) {
        var files = new DirectoryInfo(entryDirPath).GetFiles();
        if (files.Length != 2) {
            // too many files
            return null;
        }

        if (files[0].Name == MetadataFileName) return new EntryContentInfo(files[0], files[1]);
        if (files[1].Name == MetadataFileName) return new EntryContentInfo(files[1], files[0]);
        return null; // missing metadata file;
    }

    /// <exception cref="InvalidCacheEntryException"></exception>
    private CacheEntryInfo? GetEntryInfoInner(string entryKey) {
        var entryDirPath = IOPath.Combine(Path, entryKey);
        // lock the entry
        using var directoryHandle = LockEntryDirectory(entryDirPath);
        if (directoryHandle == null) {
            // entry does not exist
            return null;
        }

        var contentInfo = GetEntryContentInfo(entryDirPath);
        if (contentInfo == null) {
            throw new InvalidCacheEntryException(entryKey);
        }
        var (metadataInfo, entryInfo) = contentInfo.Value;

        var metadata = ReadMetadataFile(metadataInfo.FullName);
        if (metadata == null) {
            // metadata file has gone missing since the listing above?
            throw new InvalidCacheEntryException(entryKey);
        }

        return new CacheEntryInfo(entryKey, entryInfo.FullName,
                entryInfo.Length, metadataInfo.LastWriteTime, metadata);
    }

    /// <summary>
    /// Retrieves a single entry by its key and locks it for reading.
    /// The lock should be held during the whole time that you're reading from the entry. To unlock it, call Unlock or Dispose.
    /// Information about the requesting package is recorded into the entry metadata, if it's passed.
    /// </summary>
    /// <exception cref="InvalidCacheEntryException"></exception>
    public CacheEntryLock? GetEntryLocked(string entryKey, Package package) {
        Verify.FileName(entryKey);

        var entryDirPath = IOPath.Combine(Path, entryKey);
        // lock the entry; we can release this on return, because we'll have an open handle
        //  directly to the entry file, which will prevent deletion of this entry
        using var directoryHandle = LockEntryDirectory(entryDirPath);
        if (directoryHandle == null) {
            // entry does not exist
            return null;
        }

        var contentInfo = GetEntryContentInfo(entryDirPath);
        if (contentInfo == null) {
            throw new InvalidCacheEntryException(entryKey);
        }
        var (metadataInfo, entryInfo) = contentInfo.Value;

        FileStream readStream;
        try {
            readStream = File.Open(entryInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        } catch (FileNotFoundException) {
            // the file has gone missing since the call to GetEntryContentInfo above, invalid entry
            throw new InvalidCacheEntryException(entryKey);
        }

        try {
            AddPackageMetadata(metadataInfo.FullName, SourcePackageMetadata.CreateFromPackage(package));
            return new CacheEntryLock(entryKey, entryInfo.FullName, readStream);
        } catch (FileNotFoundException) {
            readStream.Dispose();
            // the metadata file has gone missing, invalid entry
            throw new InvalidCacheEntryException(entryKey);
        } catch {
            readStream.Dispose();
            throw;
        }
    }

    /// <exception cref="CacheEntryInUseException"></exception>
    public void DeleteEntry(CacheEntryInfo entry) {
        DeleteEntry(entry.EntryKey);
    }

    /// <exception cref="CacheEntryInUseException"></exception>
    public void DeleteEntry(string entryKey) {
        Verify.FileName(entryKey);
        var srcPath = IOPath.Combine(Path, entryKey);
        var destinationPath = _tmpDirectory.GetTemporaryPath();

        SafeFileHandle h;
        try {
            // it's not possible to atomically delete a directory; instead, we move it
            //  to a temporary directory and delete it there
            h = Win32.OpenDirectoryForMove(srcPath);
        } catch (FileNotFoundException) {
            // entry does not exist
            return;
        } catch (FileLoadException) {
            // entry is currently in use
            throw new CacheEntryInUseException(entryKey);
        }


        using (h) {
            try {
                // this atomically moves the directory
                Win32.MoveFileByHandle(h, destinationPath);
            } catch (UnauthorizedAccessException) {
                // entry is currently in use
                throw new CacheEntryInUseException(entryKey);
            }
        }
        // there doesn't seem to be any way to delete a directory using a handle, so we'll close it and delete it separately
        Directory.Delete(destinationPath, true);
    }

    public class NewCacheEntry {
        internal readonly string DirPath;
        internal readonly string EntryFileName;

        internal NewCacheEntry(string entrySrcDir, string entryFileName) {
            DirPath = entrySrcDir;
            EntryFileName = entryFileName;
        }
    }

    public NewCacheEntry PrepareNewEntry(string entrySrcDir, Package package) {
        if (!Directory.Exists(entrySrcDir)) {
            throw new InvalidAddedCacheEntryException("Attempted to add a cache entry from a non-existent directory.");
        }

        if (Directory.EnumerateDirectories(entrySrcDir).Count() != 0) {
            throw new InvalidAddedCacheEntryException("Attempted to add a cache entry containing a subdirectory.");
        }

        var files = Directory.GetFiles(entrySrcDir);
        if (files.Length != 1) {
            throw new InvalidAddedCacheEntryException("Attempted to add a cache entry containing multiple files.");
        }
        // .GetFiles returns full paths
        var entryFileName = IOPath.GetFileName(files[0]);

        // prepend `_` to the file name in case it's the same as the metadata file
        if (entryFileName == MetadataFileName) {
            entryFileName = "_" + entryFileName;
            File.Move(files[0], IOPath.Combine(entrySrcDir, entryFileName));
        }

        // add a metadata file
        using var metadataStream = File.Open(IOPath.Combine(entrySrcDir, MetadataFileName), FileMode.CreateNew);
        // write the serialized metadata into the file
        // NOTE: since we use \n as a record separator, we must not pretty-print the JSON
        JsonSerializer.Serialize(metadataStream, SourcePackageMetadata.CreateFromPackage(package));

        // the entry setup is finished
        return new NewCacheEntry(entrySrcDir, entryFileName);
    }

    /// Atomically add an entry to the cache and get a lock.
    /// Expected usage: File is downloaded to a temporary directory, then moved into the cache using this method.
    /// <exception cref="CacheEntryAlreadyExistsException"></exception>
    public CacheEntryLock AddEntryLocked(string entryKey, NewCacheEntry newEntry) {
        Verify.FileName(entryKey);

        var targetPath = IOPath.Combine(Path, entryKey);
        // we close this handle after the method is done, similarly to GetEntryLocked
        using var handle = Win32.OpenDirectoryForMove(newEntry.DirPath);

        // move the entry into place
        try {
            Win32.MoveFileByHandle(handle, targetPath);
        } catch (COMException e) {
            // -2147024713 (0x800700B7) = target already exists
            if (e.HResult == -2147024713) {
                throw new CacheEntryAlreadyExistsException(entryKey);
            }
            throw;
        }

        var entryFilePath = IOPath.Combine(targetPath, newEntry.EntryFileName);
        FileStream readStream;
        try {
            readStream = File.Open(entryFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        } catch (FileNotFoundException) {
            // the file has gone missing (wtf?), invalid entry
            throw new InvalidCacheEntryException(entryKey);
        }
        return new CacheEntryLock(entryKey, entryFilePath, readStream);
    }

    [PublicAPI]
    public class CacheEntryInfo {
        public readonly string EntryKey;
        public readonly string Path;
        /// Size of the cache entry, in bytes.
        public readonly long Size;
        public readonly DateTime LastUseTime;
        /// List of metadata about packages that used this cache entry.
        public readonly SourcePackageMetadata[] SourcePackages;

        internal CacheEntryInfo(string entryKey, string path, long size, DateTime lastUseTime,
                SourcePackageMetadata[] sourcePackages) {
            Verify.Assert.FileName(entryKey);
            EntryKey = entryKey;
            Path = path;
            Size = size;
            LastUseTime = lastUseTime;
            SourcePackages = sourcePackages;
        }
    }

    [PublicAPI]
    public interface IFileLock : IDisposable {
        public string Path {get;}
        public FileStream ReadStream {get;}

        public void Unlock();
    }

    [PublicAPI]
    public class CacheEntryLock : IFileLock {
        public readonly string EntryKey;
        public string Path {get;}
        /// The file stream used to lock the cache entry for reading, and also available for use.
        /// Do NOT close this stream manually, it will be closed automatically on Dispose.
        public FileStream ReadStream {get;}

        internal CacheEntryLock(string entryKey, string path, FileStream readStream) {
            Verify.Assert.FileName(entryKey);
            EntryKey = entryKey;
            Path = path;
            ReadStream = readStream;
        }

        public void Unlock() {
            ReadStream.Close();
        }

        public void Dispose() {
            // this closes the file handle
            ReadStream.Dispose();
        }
    }

    [PublicAPI]
    public record SourcePackageMetadata(string PackageName, string? ManifestName, string? ManifestVersion) {
        // we cannot use a secondary constructor, because that would confuse JsonSerializer.Deserialize,
        //  and we cannot annotate the primary constructor in a `record` with `[JsonConstructor]`
        public static SourcePackageMetadata CreateFromPackage(Package p) {
            return new SourcePackageMetadata(p.PackageName, p.Manifest.Name, p.Manifest.Version?.ToString());
        }

        // TODO: if an invalid record is encountered during enumeration, remove it from the file
        /// <returns>The parsed `SourcePackageMetadata` object, or `null` if the JSON input is not valid.</returns>
        internal static SourcePackageMetadata? ParseFromJson(string json) {
            try {
                var parsed = JsonSerializer.Deserialize<SourcePackageMetadata>(json);
                switch (parsed) {
                    case null:
                    case {PackageName: null}:
                        return null;
                    default:
                        return parsed;
                }
            } catch (JsonException) {
                return null;
            }
        }
    }
}
