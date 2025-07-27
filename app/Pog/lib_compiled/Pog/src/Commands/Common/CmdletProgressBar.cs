using System;
using System.Management.Automation;

namespace Pog.Commands.Common;

internal sealed class CmdletProgressBar : IProgress<double>, IDisposable {
    // when progress print activity ID is not set explicitly, use an auto-incrementing ID
    //  to show multiple progress bars when multiple cmdlets are ran in parallel
    private static int _nextActivityId = 0;

    private readonly Action<ProgressRecord> _writeProgressFn;
    private readonly ProgressRecord _progressRecord;

    public CmdletProgressBar(Action<ProgressRecord> writeProgressFn, ProgressActivity metadata) {
        _writeProgressFn = writeProgressFn;
        _progressRecord = new ProgressRecord(metadata.Id ?? _nextActivityId++, metadata.Activity, metadata.Description) {
            PercentComplete = 0,
        };
        _writeProgressFn(_progressRecord);
    }

    public void Dispose() {
        _progressRecord.PercentComplete = 100;
        _progressRecord.RecordType = ProgressRecordType.Completed;
        _writeProgressFn(_progressRecord);
    }

    public CmdletProgressBar(Cmdlet cmdlet, ProgressActivity metadata) : this(cmdlet.WriteProgress, metadata) {}

    public void Report(double ratioComplete) => ReportPercent((int) (ratioComplete * 100));

    public void Report(double ratioComplete, string description) {
        _progressRecord.StatusDescription = description;
        Report(ratioComplete);
    }

    /// Utility method for reporting file/download sizes.
    public void ReportSize(long completedBytes, long? totalBytes = null, string? description = null) {
        var (ratio, sizeStr) = totalBytes == null
                // cannot give progress position, we don't know the total size
                ? (0.0, ToHumanSize(completedBytes))
                : (completedBytes / (double) totalBytes, ToHumanSize(completedBytes, totalBytes.Value));
        Report(ratio, description == null ? sizeStr : $"{description} ({sizeStr})");
    }

    public void ReportPercent(int percentComplete) {
        _progressRecord.PercentComplete = percentComplete;
        _writeProgressFn(_progressRecord);
    }

    private static string ToHumanSize(long completedBytes, long totalBytes) {
        return $"{ToHumanSize(completedBytes)} / {ToHumanSize(totalBytes)}";
    }

    // adapted from PowerShell, `Utils.cs`
    private static string ToHumanSize(long bytes) {
        return bytes switch {
            < 1024 and >= 0 => $"{bytes} b",
            < 1048576 and >= 1024 => $"{bytes / 1024.0:0.0} kB",
            < 1073741824 and >= 1048576 => $"{bytes / 1048576.0:0.0} MB",
            < 1099511627776 and >= 1073741824 => $"{bytes / 1073741824.0:0.000} GB",
            < 1125899906842624 and >= 1099511627776 => $"{bytes / 1099511627776.0:0.00000} TB",
            < 1152921504606847000 and >= 1125899906842624 => $"{bytes / 1125899906842624.0:0.0000000} PB",
            >= 1152921504606847000 => $"{bytes / 1152921504606847000.0:0.000000000} EB",
            _ => "0 Bytes",
        };
    }
}

/// Metadata for rendering a PowerShell progress bar.
public record struct ProgressActivity(string? Activity = null, string? Description = null, int? Id = null);
