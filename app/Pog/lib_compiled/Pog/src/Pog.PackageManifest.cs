using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using JetBrains.Annotations;
using Pog.InnerCommands;

namespace Pog;

public interface IPackageManifestException {}

public class PackageManifestNotFoundException : FileNotFoundException, IPackageManifestException {
    internal PackageManifestNotFoundException(string message, string fileName) : base(message, fileName) {}
}

[PublicAPI]
public class PackageManifestParseException : ParseException, IPackageManifestException {
    public readonly string ManifestPath;

    internal PackageManifestParseException(string manifestPath, string message) : base(message) {
        ManifestPath = manifestPath;
    }

    internal PackageManifestParseException(string manifestPath, ParseError[] errors) : base(errors) {
        ManifestPath = manifestPath;
    }

    public override string Message =>
            $"Could not {(Errors == null ? "load" : "parse")} the package manifest at '{ManifestPath}':\n" + base.Message;
}

[PublicAPI]
public class InvalidPackageManifestStructureException : Exception, IPackageManifestException {
    public readonly string ManifestPath;
    public readonly List<string> Issues;

    public InvalidPackageManifestStructureException(string manifestPath, List<string> issues) {
        ManifestPath = manifestPath;
        Issues = issues;
    }

    public override string Message =>
            $"Package manifest at '{ManifestPath}' has invalid structure:\n\t" + string.Join("\n\t", Issues);
}

/// <summary>Package manifest had a ScriptBlock as the 'Install.Url' property, but it did not return a valid URL.</summary>
public class InvalidPackageManifestUrlScriptBlockException : Exception, IPackageManifestException {
    public InvalidPackageManifestUrlScriptBlockException(string message) : base(message) {}
}

[PublicAPI]
public record PackageManifest {
    public readonly string Path;

    public readonly bool Private;
    public readonly string? Name;
    public readonly PackageVersion? Version;
    public readonly PackageArchitecture[]? Architecture;
    // public readonly PackageChannel? Channel;

    public readonly string? Description;
    public readonly string? Website;

    public readonly PackageInstallParameters[]? Install;
    public readonly ScriptBlock? Enable;
    public readonly ScriptBlock? Disable;

    [Hidden]
    public readonly Hashtable Raw;

    /// <param name="manifestPath">Path to the manifest file.</param>
    /// <param name="manifestStr">
    /// If passed, the manifest is parsed from the string and <paramref name="manifestPath"/> is only used to improve error reporting.
    /// </param>
    /// <param name="owningPackage">If parsing a repository manifest, this should be the package that owns the manifest.</param>
    ///
    /// <exception cref="PackageManifestNotFoundException">Thrown if the package manifest file does not exist.</exception>
    /// <exception cref="PackageManifestParseException">Thrown if the package manifest file is not a valid PowerShell data file (.psd1).</exception>
    internal PackageManifest(string manifestPath, string? manifestStr = null, RepositoryPackage? owningPackage = null) {
        Path = manifestPath;
        Raw = PackageManifestParser.LoadManifest(manifestPath, manifestStr);

        // parse the raw manifest into properties on this object
        HashtableParser parser = new(Raw);

        Private = parser.ParseScalar<bool>("Private", false) ?? false;
        if (Private) {
            if (owningPackage != null) {
                parser.AddIssue("Property 'Private' is not allowed in manifests in a package repository.");
            } else {
                parser.IgnoreRequired = true;
            }
        }

        Name = parser.ParseScalar<string>("Name", true);
        // check that the manifest name matches package name, if passed
        if (owningPackage != null && Name != owningPackage.PackageName) {
            parser.AddIssue($"Incorrect 'Name' property value - got '{Name}', expected '{owningPackage.PackageName}'.");
        }

        var versionStr = parser.ParseScalar<string>("Version", true);
        Version = versionStr == null ? null : ParseVersion(parser, versionStr);
        // check that the manifest version matches package version, if passed
        if (owningPackage != null && versionStr != owningPackage.Version.ToString()) {
            parser.AddIssue($"Incorrect 'Version' property value - got '{versionStr}', expected '{owningPackage.Version}'.");
        }

        var archRaw = parser.ParseList<string>("Architecture", true);
        Architecture = archRaw == null ? null : ParseArchitecture(parser, archRaw);

        Description = parser.ParseScalar<string>("Description", false);
        Website = parser.ParseScalar<string>("Website", false);

        var installRaw = parser.ParseList<Hashtable>("Install", true);
        Install = installRaw == null ? null : ParseInstallBlock(parser, installRaw);
        Enable = parser.ParseScalar<ScriptBlock>("Enable", true);
        Disable = parser.ParseScalar<ScriptBlock>("Disable", false);

        // check for extra unknown keys
        foreach (var extraKey in parser.ExtraKeys.Where(k => k is not string s || !s.StartsWith("_"))) {
            parser.AddIssue($"Found unknown property '{extraKey}' - private properties must be prefixed with underscore " +
                            "(e.g. '_PrivateProperty').");
        }

        if (parser.HasIssues) {
            throw new InvalidPackageManifestStructureException(manifestPath, parser.Issues);
        }
    }

    private PackageVersion? ParseVersion(HashtableParser parser, string versionStr) {
        try {
            return new PackageVersion(versionStr);
        } catch (InvalidPackageVersionException e) {
            parser.AddIssue($"Invalid 'Version' value '{versionStr}': {e.Message}");
            return null;
        }
    }

    private PackageInstallParameters[]? ParseInstallBlock(HashtableParser parentParser, Hashtable[] raw) {
        var parsed = new PackageInstallParameters[raw.Length];
        for (var i = 0; i < raw.Length; i++) {
            var p = ParseInstallHashtable(new HashtableParser(raw[i], $"Install[{i}].", parentParser.Issues));
            if (p == null) return null;
            else parsed[i] = p;
        }
        return parsed;
    }

    public enum PackageArchitecture { Any, X64, X86, Arm64 }

    private PackageArchitecture[]? ParseArchitecture(HashtableParser parser, string[] raw) {
        var parsed = new PackageArchitecture[raw.Length];
        for (var i = 0; i < raw.Length; i++) {
            if (!_architectureMap.TryGetValue(raw[i], out parsed[i])) {
                parser.AddIssue($"Invalid 'Architecture' value: {raw[i]} " +
                                $"(supported values: {string.Join(", ", _architectureMap.Keys)})");
                return null;
            }
        }
        return parsed;
    }

    private static Dictionary<string, PackageArchitecture> _architectureMap = new(StringComparer.InvariantCultureIgnoreCase) {
        {"*", PackageArchitecture.Any},
        {"x86", PackageArchitecture.X86},
        {"x64", PackageArchitecture.X64},
        {"arm64", PackageArchitecture.Arm64},
    };

    private PackageInstallParameters? ParseInstallHashtable(HashtableParser parser) {
        // If set, the downloaded file is directly moved to the ./app directory, without being treated as an archive and extracted.
        var noArchive = parser.ParseScalar<bool>("NoArchive", false) ?? false;

        var sourceUrl = parser.GetProperty("Url", true, "string | ScriptBlock");
        if (sourceUrl is not (null or string or ScriptBlock)) {
            parser.AddIssue($"Required property '{parser.ObjectPath}Url' is present, " +
                            $"but has an incorrect type '{sourceUrl.GetType()}', expected 'string | ScriptBlock'.");
        }

        var expectedHash = parser.ParseScalar<string>("Hash", false);
        if (expectedHash == "") {
            // allow empty hash, otherwise Show-PogManifestHash would be less ergonomic
            expectedHash = null;
        }
        if (expectedHash != null && !Verify.Is.Sha256Hash(expectedHash)) {
            parser.AddValidityIssue("Hash", expectedHash, "expected a SHA-256 hash (64 character hex string)");
        }
        // hash should always be uppercase
        expectedHash = expectedHash?.ToUpperInvariant();

        var userAgentStr = parser.ParseScalar<string>("UserAgent", false);
        DownloadParameters.UserAgentType userAgent;
        if (userAgentStr == null) {
            userAgent = default;
        } else if (!_userAgentMap.TryGetValue(userAgentStr, out userAgent)) {
            parser.AddValidityIssue("UserAgent", userAgentStr, $"supported values: {string.Join(", ", _userAgentMap.Keys)}");
        }

        var target = parser.ParseScalar<string>("Target", noArchive);
        if (target != null && !Verify.Is.FilePath(target)) {
            parser.AddValidityIssue("Target", target, "expected a valid file path");
        }

        var subdirectory = noArchive ? null : parser.ParseScalar<string>("Subdirectory", false);
        if (subdirectory != null && !Verify.Is.FilePath(subdirectory)) {
            parser.AddValidityIssue("Subdirectory", subdirectory, "expected a valid file path");
        }

        PackageInstallParameters parsed;
        if (noArchive) {
            parsed = new PackageInstallParametersNoArchive {
                SourceUrl = sourceUrl!,
                ExpectedHash = expectedHash,
                UserAgent = userAgent,
                // if null, we will throw an exception anyway
                Target = target!,
            };
        } else {
            parsed = new PackageInstallParametersArchive {
                SourceUrl = sourceUrl!,
                ExpectedHash = expectedHash,
                UserAgent = userAgent,
                Target = target,
                Subdirectory = subdirectory,
                NsisInstaller = parser.ParseScalar<bool>("NsisInstaller", false) ?? false,
                SetupScript = parser.ParseScalar<ScriptBlock>("SetupScript", false),
            };
        }

        // check for extra unknown keys
        foreach (var extraKey in parser.ExtraKeys) {
            parser.AddIssue($"Found unknown property '{parser.ObjectPath}{extraKey}'.");
        }

        return parser.HasIssues ? null : parsed;
    }

    private static Dictionary<string, DownloadParameters.UserAgentType> _userAgentMap =
            new(StringComparer.InvariantCultureIgnoreCase) {
                {"Browser", DownloadParameters.UserAgentType.Browser},
                {"Wget", DownloadParameters.UserAgentType.Wget},
                {"PowerShell", DownloadParameters.UserAgentType.PowerShell},
            };
}

public abstract record PackageInstallParameters {
    /// Source URL, from which the archive is downloaded. Redirects are supported.
    public object SourceUrl = null!;

    /// SHA-256 hash that the downloaded archive should match. Validation is skipped if null, but a warning is printed.
    public string? ExpectedHash;

    /// Some servers (e.g. Apache Lounge) dislike the PowerShell/BITS user agent string.
    /// Set this to `Browser` to use a browser user agent string (currently Firefox).
    /// Set this to `Wget` to use wget user agent string.
    public DownloadParameters.UserAgentType UserAgent;

    /// <summary>
    /// If <see cref="SourceUrl"/> is a ScriptBlock, this method invokes it and returns the resulting URL,
    /// otherwise returns the static URL.
    /// </summary>
    ///
    /// <remarks>
    /// NOTE: This method executes potentially untrusted code from the manifest.
    /// NOTE: This method must be executed inside a container environment.
    /// </remarks>
    ///
    /// <exception cref="InvalidPackageManifestUrlScriptBlockException"></exception>
    public string ResolveUrl() {
        if (SourceUrl is string s) {
            return s; // static string, just return
        }

        Debug.Assert(SourceUrl is ScriptBlock);
        var sb = (ScriptBlock) SourceUrl;

        // TODO: shouldn't we use .GetNewClosure() here?
        var resolvedUrlObj = sb.InvokeReturnAsIs();

        if (resolvedUrlObj is PSObject pso) {
            resolvedUrlObj = pso.BaseObject;
        }
        if (resolvedUrlObj is not string resolvedUrl) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "ScriptBlock for the source URL ('Install.Url' property in the package manifest) must " +
                    $"return a string, got '{resolvedUrlObj?.GetType().ToString() ?? "null"}'");
        }
        return resolvedUrl;
    }
}

public record PackageInstallParametersNoArchive : PackageInstallParameters {
    // mandatory when NoArchive is set, otherwise the name of the binary would be controlled by the server
    //  we're downloading from, making the resulting package no longer reproducible based on just the hash
    /// The downloaded file is moved to `./app/$Target`. The path must include the file name.
    public string Target = null!;
}

public record PackageInstallParametersArchive : PackageInstallParameters {
    /// If passed, only the subdirectory with passed name/path is extracted to ./app and the rest is ignored.
    public string? Subdirectory;

    /// If passed, the extracted directory is moved to `./app/$Target`, instead of directly to `./app`.
    public string? Target;

    /// If you need to modify the extracted archive (e.g. remove some files), pass a scriptblock, which receives
    /// a path to the extracted directory as its only argument. All modifications to the extracted files should be
    /// done in this scriptblock – this ensures that the ./app directory is not left in an inconsistent state
    /// in case of a crash during installation.
    public ScriptBlock? SetupScript;

    // TODO: auto-detect NSIS installers and remove the flag?
    /// Pass this if the retrieved file is an NSIS installer
    /// Currently, only thing this does is remove the `$PLUGINSDIR` output directory.
    /// NOTE: NSIS installers may do some initial config, which is not ran when extracted directly.
    public bool NsisInstaller;
}
