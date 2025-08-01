﻿using System;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

/// <summary>Removes all cached package archives in the local download cache matching the search criteria.</summary>
/// <para>
/// The `Clear-PogDownloadCache` cmdlet lists all package archives stored in the local download cache that are older than
/// the specified date. After confirmation, the archives are deleted. If a deleted archive is currently in use (the package is
/// currently being installed), a non-terminating error is raised and the entry is left intact.
/// </para>
[PublicAPI]
[Cmdlet(VerbsCommon.Clear, "PogDownloadCache", DefaultParameterSetName = DaysPS)]
public sealed class ClearPogDownloadCacheCommand : PogCmdlet {
    private const string DatePS = "Date";
    private const string DaysPS = "Days";

    /// Sets a date used to filter the removed archives. Only archives which were last used before the passed date are removed.
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = DatePS)]
    public DateTime? DateBefore;

    /// Archives that were not used in the last <see cref="DaysBefore"/> days are removed.
    [Parameter(Position = 0, ParameterSetName = DaysPS)]
    public ulong DaysBefore = 0;

    /// Do not prompt for confirmation and delete the matching cache entries immediately.
    [Parameter]
    public SwitchParameter Force;

    private readonly SharedFileCache _cache = InternalState.DownloadCache;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var limitDate = ParameterSetName == DatePS ? DateBefore : DateTime.Now.AddDays(-(double) DaysBefore);

        var entries = _cache.EnumerateEntries(OnInvalidCacheEntry)
                // skip recently used entries
                .Where(e => e.LastUseTime < limitDate)
                .ToArray();

        if (entries.Length == 0) {
            WriteInformation($"No package archives older than '{limitDate.ToString()}' found, nothing to remove.");
            return;
        }

        if (Force) {
            var totalSize = 0.0;
            var deletedCount = 0ul;
            foreach (var entry in entries.Where(DeleteEntry)) {
                totalSize += entry.Size;
                deletedCount++;
            }
            WriteInformation($"Removed {deletedCount} package archive{(deletedCount == 1 ? "" : "s")}, " +
                             $"freeing ~{totalSize / Gigabyte:F2} GB of space.");
        } else {
            var totalSize = 0ul;
            foreach (var entry in entries.OrderByDescending(e => e.Size)) {
                totalSize += entry.Size;
                WriteHost($"{entry.Size / Megabyte,10:F2} MB - {GetEntryOwnerStr(entry)}");
            }

            var title = $"Remove the listed package archives, freeing ~{totalSize / Gigabyte:F2} GB of space?";
            var message = "This will not affect already installed packages. Reinstalling a package will take longer, " +
                          "as it will have to be downloaded again.";
            if (ShouldContinue(message, title)) {
                // delete the entries
                foreach (var entry in entries) {
                    DeleteEntry(entry);
                }
            } else {
                WriteHost("No package archives were removed.");
            }
        }
    }

    private const double Megabyte = 1024 * 1024;
    private const double Gigabyte = Megabyte * 1024;

    private bool DeleteEntry(SharedFileCache.CacheEntryInfo entry) {
        try {
            _cache.DeleteEntry(entry);
            return true;
        } catch (CacheEntryInUseException e) {
            var newE = new CacheEntryInUseException(
                    $"Cannot clear a cache entry for '{GetEntryOwnerStr(entry)}', it is currently in use by " +
                    $"another Pog instance. Please wait until the installation finishes and try again.", e);
            WriteError(newE, "EntryInUse", ErrorCategory.ResourceBusy, entry);
            return false;
        }
    }

    private static string GetEntryOwnerStr(SharedFileCache.CacheEntryInfo entry) {
        return string.Join(", ", entry.SourcePackages.Select(s =>
                s.PackageName + (s.ManifestVersion == null ? "" : $" v{s.ManifestVersion}")));
    }

    private void OnInvalidCacheEntry(InvalidCacheEntryException e) {
        WriteWarning($"Invalid cache entry encountered, deleting...: {e.EntryKey}");
        try {
            _cache.DeleteEntry(e.EntryKey);
        } catch (CacheEntryInUseException) {
            WriteWarning("Cannot delete the invalid entry, it is currently in use.");
        }
    }
}
