using System;
using System.Management.Automation;

namespace Pog.Commands.Utils;

public class CmdletProgressBar : IDisposable {
    // when progress print activity ID is not set explicitly, use an auto-incrementing ID
    //  to show multiple progress bars when multiple cmdlets are ran in parallel
    private static int _nextActivityId = 0;

    private readonly Action<ProgressRecord> _writeProgressFn;
    private readonly ProgressRecord _progressRecord;

    public CmdletProgressBar(Action<ProgressRecord> writeProgressFn, int? activityId, string activity, string description) {
        _writeProgressFn = writeProgressFn;
        _progressRecord = new ProgressRecord(activityId ?? _nextActivityId++, activity, description);
        writeProgressFn(_progressRecord);
    }

    public CmdletProgressBar(Cmdlet cmdlet, int? activityId, string activity, string description)
            : this(cmdlet.WriteProgress, activityId, activity, description) {}

    public void Update(int percentComplete) {
        _progressRecord.PercentComplete = percentComplete;
        _writeProgressFn(_progressRecord);
    }

    public void Dispose() {
        _progressRecord.PercentComplete = 100;
        _progressRecord.RecordType = ProgressRecordType.Completed;
        _writeProgressFn(_progressRecord);
    }
}
