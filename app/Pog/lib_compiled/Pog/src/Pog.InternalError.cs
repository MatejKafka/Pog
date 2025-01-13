using System;

namespace Pog;

public class InternalError(string message)
        : Exception($"INTERNAL ERROR: {message} Seems like Pog developers fucked something up, please send a bug report.");
