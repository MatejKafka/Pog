using System;
using System.Management.Automation;

namespace Pog.Commands.Utils;

public class CmdletProgressBar : IDisposable {
    // when progress print activity ID is not set explicitly, use an auto-incrementing ID
    //  to show multiple progress bars when multiple cmdlets are ran in parallel
    private static int _nextActivityId = 0;

    private Cmdlet _cmdlet;
    private ProgressRecord _progressRecord;

    public CmdletProgressBar(Cmdlet cmdlet, int? activityId, string activity, string description) {
        _cmdlet = cmdlet;
        _progressRecord = new ProgressRecord(activityId ?? _nextActivityId++, activity, description);
        _cmdlet.WriteProgress(_progressRecord);
    }

    public void Update(int percentComplete) {
        _progressRecord.PercentComplete = percentComplete;
        _cmdlet.WriteProgress(_progressRecord);
    }

    public void Dispose() {
        _progressRecord.PercentComplete = 100;
        _progressRecord.RecordType = ProgressRecordType.Completed;
        _cmdlet.WriteProgress(_progressRecord);
    }
}