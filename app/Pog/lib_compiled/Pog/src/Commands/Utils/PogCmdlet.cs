using System;
using System.Management.Automation;

namespace Pog.Commands.Utils;

public class PogCmdlet : PSCmdlet {
    protected void WriteInformation(string message) {
        WriteInformation(message, null);
    }

    protected void ThrowTerminatingError(Exception exception, string errorId, ErrorCategory errorCategory,
            object? targetObject) {
        ThrowTerminatingError(new ErrorRecord(exception, errorId, errorCategory, targetObject));
    }

    protected void ThrowTerminatingArgumentError(object? argumentValue, string errorId, string message) {
        ThrowTerminatingError(new ArgumentException(message), errorId, ErrorCategory.InvalidArgument, argumentValue);
    }
}
