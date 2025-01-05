using System;
using System.Management.Automation;
using System.Reflection;

namespace Pog.Utils;

public static class AssemblyVersions {
    public static string GetPogVersion() {
        var versionAttr = typeof(AssemblyVersions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (versionAttr != null) {
            return versionAttr.InformationalVersion;
        } else {
            throw new InternalError("Could not find Pog version from the assembly.");
        }
    }

    /// <returns>A pair of (isCore, version), where isCore indicates whether this is pwsh.exe (or powershell.exe).</returns>
    public static (bool, string?) GetPowerShellVersion() {
        var pwshAssembly = typeof(PowerShell).Assembly;
        var versionStr = pwshAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (versionStr != null && versionStr.Contains(" SHA: ")) {
            // pwsh.exe versions have the format `X.Y.Z-rc0 SHA: ...`
            return (true, versionStr.Substring(0, versionStr.IndexOf(" SHA: ", StringComparison.Ordinal)));
        }

        // old powershell.exe, dig the version out of the then private (now public) PSVersionInfo
        var prop = typeof(PowerShell).Assembly
                .GetType("System.Management.Automation.PSVersionInfo")
                .GetProperty("PSVersion", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var pwshVersion = prop?.GetValue(prop) as Version;
        return (false, pwshVersion == null ? null : pwshVersion.Major + "." + pwshVersion.Minor + "." + pwshVersion.Build);
    }
}
