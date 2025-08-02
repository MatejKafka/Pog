using System;

namespace Pog;

/// Type of User-Agent to use when making HTTP requests through Pog.
/// The specific User-Agent strings may change between Pog releases to match a newer version of the emulated HTTP client.
public enum UserAgentType {
    // Pog is `default(T)`
    Pog = 0, PowerShell, Browser, Wget,
}

internal static class UserAgentTypeExtensions {
    public static string GetHeaderString(this UserAgentType userAgent) {
        // explicitly specify fixed User-Agent strings; that way, we can freely switch implementations without breaking compatibility
        return userAgent switch {
            UserAgentType.Pog => InternalState.HttpClient.UserAgent,
            UserAgentType.PowerShell => "Mozilla/5.0 (Windows NT 10.0; Win64; x64; en-US) PowerShell/5.1.0",
            UserAgentType.Browser => "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
            UserAgentType.Wget => "Wget/1.20.3 (linux-gnu)",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
