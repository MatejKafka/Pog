using System;
using System.Management.Automation;

namespace Pog.Commands.Common;

public sealed class CmdletProgressBar : IDisposable {
    // when progress print activity ID is not set explicitly, use an auto-incrementing ID
    //  to show multiple progress bars when multiple cmdlets are ran in parallel
    private static int _nextActivityId = 0;

    private readonly Action<ProgressRecord> _writeProgressFn;
    private readonly ProgressRecord _progressRecord;

    public CmdletProgressBar(Action<ProgressRecord> writeProgressFn, ProgressActivity metadata) {
        _writeProgressFn = writeProgressFn;
        _progressRecord = new ProgressRecord(metadata.Id ?? _nextActivityId++, metadata.Activity, metadata.Description);
        _writeProgressFn(_progressRecord);
    }

    public CmdletProgressBar(Cmdlet cmdlet, ProgressActivity metadata) : this(cmdlet.WriteProgress, metadata) {}

    public void Update(double ratioComplete, string? description = null) {
        if (description != null) {
            _progressRecord.StatusDescription = description;
        }
        UpdatePercent((int) (ratioComplete * 100));
    }

    public void UpdatePercent(int percentComplete) {
        _progressRecord.PercentComplete = percentComplete;
        _writeProgressFn(_progressRecord);
    }

    public void Dispose() {
        _progressRecord.PercentComplete = 100;
        _progressRecord.RecordType = ProgressRecordType.Completed;
        _writeProgressFn(_progressRecord);
    }

    public record struct ProgressActivity(string? Activity = null, string? Description = null, int? Id = null);
}
