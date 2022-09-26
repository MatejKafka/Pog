using System.Management.Automation;

namespace Pog;

public class InternalError : RuntimeException {
    public InternalError(string message) : base(
            $"INTERNAL ERROR: {message} Seems like Pog developers fucked something up, plz send a bug report.") {}
}