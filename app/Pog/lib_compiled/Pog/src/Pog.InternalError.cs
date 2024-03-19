using System;

namespace Pog;

public class InternalError(string message)
        : Exception($"INTERNAL ERROR: {message} Seems like Pog developers fucked something up, plz send a bug report.");
