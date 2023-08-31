﻿using System;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Removes all cached package archives in the local download cache matching the search criteria.</para>
/// <para type="description">
/// The `Clear-PogDownloadCache` cmdlet lists all package archives stored in the local download cache, which are older than the specified date.
/// After confirmation, the archives are deleted. If an archive is currently used (the package is currently being installed), a warning
/// is printed, but the matching remaining entries are deleted.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Clear, "PogDownloadCache", DefaultParameterSetName = DaysPS)]
public class ClearPogDownloadCacheCommand : PogCmdlet {
    private const string DatePS = "Date";
    private const string DaysPS = "Days";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = DatePS)]
    public DateTime? DateBefore;
    [Parameter(Position = 0, ParameterSetName = DaysPS)]
    public ulong DaysBefore = 0;
    /// <summary><para type="description">
    /// Do not prompt for confirmation and delete the cache entries immediately.
    /// </para></summary>
    [Parameter]
    public SwitchParameter Force;

    private SharedFileCache Cache => InternalState.DownloadCache;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var limitDate = ParameterSetName == DatePS ? DateBefore : DateTime.Now.AddDays(-(double) DaysBefore);

        var entries = Cache.EnumerateEntries(OnInvalidCacheEntry)
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
                var sourceStr = string.Join(", ", entry.SourcePackages.Select(s =>
                        s.PackageName + (s.ManifestVersion == null ? "" : $" v{s.ManifestVersion}")));
                WriteHost($"{entry.Size / Megabyte,10:F2} MB - {sourceStr}");
            }

            var title = $"Remove the listed package archives, freeing ~{totalSize / Gigabyte:F2} GB of space?";
            var message = "This will not affect already installed applications. Reinstalling an application " +
                          "will take longer, as it will have to be downloaded again.";
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
            Cache.DeleteEntry(entry);
            return true;
        } catch (CacheEntryInUseException e) {
            WriteError(e, "EntryInUse", ErrorCategory.ResourceBusy, entry);
            return false;
        }
    }

    private void OnInvalidCacheEntry(InvalidCacheEntryException e) {
        WriteWarning($"Invalid cache entry encountered, deleting...: {e.EntryKey}");
        try {
            Cache.DeleteEntry(e.EntryKey);
        } catch (CacheEntryInUseException) {
            WriteWarning("Cannot delete the invalid entry, it is currently in use.");
        }
    }
}