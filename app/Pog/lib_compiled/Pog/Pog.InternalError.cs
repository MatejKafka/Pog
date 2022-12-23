using System;

namespace Pog;

public class InternalError : Exception {
    public InternalError(string message) : base(
            $"INTERNAL ERROR: {message} Seems like Pog developers fucked something up, plz send a bug report.") {}
}
