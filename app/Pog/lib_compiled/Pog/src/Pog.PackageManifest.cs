﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Language;
using JetBrains.Annotations;
using Pog.InnerCommands;

namespace Pog;

public interface IPackageManifestException;

public class PackageManifestNotFoundException(string message, string fileName)
        : FileNotFoundException(message, fileName), IPackageManifestException;

[PublicAPI]
public class PackageManifestParseException : Exception, IPackageManifestException {
    // when subclassing ParseException, the default ConciseView error view gets confused and strips all position
    //  information and context from the error message, resulting in very confusing error messages like
    //  `Import-Pog: Missing closing ')' in expression.`
    // instead, hide the inheritance and store it as a private field instead
    private readonly ParseException _parseException;
    public readonly string ManifestSource;

    internal PackageManifestParseException(string manifestSource, string message) {
        _parseException = new(message);
        ManifestSource = manifestSource;
    }

    internal PackageManifestParseException(string manifestSource, ParseError[] errors) {
        _parseException = new(errors);
        ManifestSource = manifestSource;
    }

    public override string Message =>
            $"Could not {(_parseException.Errors == null ? "load" : "parse")} the package manifest at '{ManifestSource}':\n" +
            _parseException.Message;
}

[PublicAPI]
public class InvalidPackageManifestStructureException : Exception, IPackageManifestException {
    public readonly string ManifestSource;
    public readonly List<string> Issues;

    internal InvalidPackageManifestStructureException(string manifestSource, List<string> issues) {
        ManifestSource = manifestSource;
        Issues = issues;
    }

    public override string Message =>
            $"Package manifest at '{ManifestSource}' has invalid structure:\n\t" + string.Join("\n\t", Issues);
}

/// <summary>Package manifest had a ScriptBlock as the 'Install.Url' property, but it did not return a valid URL.</summary>
public class InvalidPackageManifestUrlScriptBlockException : Exception, IPackageManifestException {
    internal InvalidPackageManifestUrlScriptBlockException(string message) : base(message) {}
}

[PublicAPI]
public record PackageManifest {
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

    public readonly Hashtable Raw;
    public readonly string RawString;

    /// List of unknown non-underscored properties on the manifest, should be almost always empty.
    private readonly List<string>? _unknownPropertyNames = null;

    /// <inheritdoc cref="PackageManifest(string, RepositoryPackage?)"/>
    public PackageManifest(string manifestPath) : this(manifestPath, null) {}

    /// <param name="manifestPath">Path to the manifest file.</param>
    /// <param name="owningPackage">If parsing a repository manifest, this should be the package that owns the manifest.</param>
    /// <inheritdoc cref="PackageManifest(ValueTuple{string, Hashtable}, string, RepositoryPackage?)"/>
    internal PackageManifest(string manifestPath, RepositoryPackage? owningPackage = null)
            : this(PackageManifestParser.LoadManifest(manifestPath), manifestPath, owningPackage) {}

    /// <param name="manifestStr">String from which the manifest is parsed.</param>
    /// <param name="manifestSource">Source describing the origin of the manifest string, used for better error reporting.</param>
    /// <param name="owningPackage">If parsing a repository manifest, this should be the package that owns the manifest.</param>
    /// <inheritdoc cref="PackageManifest(ValueTuple{string, Hashtable}, string, RepositoryPackage?)"/>
    internal PackageManifest(string manifestStr, string manifestSource, RepositoryPackage? owningPackage = null)
            : this(PackageManifestParser.LoadManifest(manifestStr, manifestSource), manifestSource, owningPackage) {}

    /// <exception cref="PackageManifestNotFoundException">The package manifest file does not exist.</exception>
    /// <exception cref="PackageManifestParseException">The package manifest file is not a valid PowerShell data file (.psd1).</exception>
    /// <exception cref="InvalidPackageManifestStructureException">The package manifest was correctly parsed, but has invalid structure.</exception>
    private PackageManifest((string, Hashtable) manifest, string manifestSource, RepositoryPackage? owningPackage = null) {
        InstrumentationCounter.ManifestLoads.Increment();

        (RawString, Raw) = manifest;
        // parse the raw manifest into properties on this object
        var parser = new HashtableParser(Raw);

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
            parser.AddValidityIssue("Name", Name, $"expected '{owningPackage.PackageName}'");
        }

        var versionStr = parser.ParseScalar<string>("Version", true);
        Version = versionStr == null ? null : ParseVersion(parser, versionStr);
        // check that the manifest version matches package version, if passed
        if (owningPackage != null && versionStr != owningPackage.Version.ToString()) {
            parser.AddValidityIssue("Version", versionStr, $"expected '{owningPackage.Version}'");
        }

        var archRaw = parser.ParseList<string>("Architecture", true);
        Architecture = archRaw == null ? null : ParseArchitecture(parser, archRaw);

        Description = parser.ParseScalar<string>("Description", false);
        Website = parser.ParseScalar<string>("Website", false);

        var installRaw = parser.ParseList<Hashtable>("Install", true);
        Install = installRaw == null ? null : ParseInstallBlock(parser, installRaw, ref _unknownPropertyNames);
        Enable = parser.ParseScalar<ScriptBlock>("Enable", true);
        Disable = parser.ParseScalar<ScriptBlock>("Disable", false);

        // check for extra unknown keys
        foreach (var extraKey in parser.ExtraNonPrivateKeys) {
            // only lint this, do not throw a hard error, since that would prevent us from extending the manifest
            //  in the future while still keeping compatibility with older Pog versions
            (_unknownPropertyNames ??= []).Add(parser.ObjectPath + extraKey);
        }

        if (parser.HasIssues) {
            throw new InvalidPackageManifestStructureException(manifestSource, parser.Issues);
        }
    }

    private static PackageVersion? ParseVersion(HashtableParser parser, string versionStr) {
        try {
            return new PackageVersion(versionStr);
        } catch (InvalidPackageVersionException e) {
            parser.AddValidityIssue("Version", versionStr, e.Message);
            return null;
        }
    }

    private static PackageInstallParameters[]? ParseInstallBlock(HashtableParser parentParser, Hashtable[] raw,
            ref List<string>? extraPropNames) {
        if (raw.Length == 0) {
            parentParser.AddIssue("Value of 'Install' must not be an empty array.");
        }

        var parsed = new PackageInstallParameters[raw.Length];
        for (var i = 0; i < raw.Length; i++) {
            var p = ParseInstallHashtable(new HashtableParser(raw[i], $"Install[{i}].", parentParser.Issues),
                    ref extraPropNames);
            if (p == null) return null;
            else parsed[i] = p;
        }
        return parsed;
    }

    public enum PackageArchitecture { Any, X64, X86, Arm64 }

    private static PackageArchitecture[]? ParseArchitecture(HashtableParser parser, string[] raw) {
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

    private static PackageInstallParameters? ParseInstallHashtable(HashtableParser parser,
            ref List<string>? extraPropNames) {
        // If set, the downloaded file is directly moved to the ./app directory, without being treated as an archive and extracted.
        var noArchive = parser.ParseScalar<bool>("NoArchive", false) ?? false;

        var sourceUrl = parser.GetProperty("Url", true, "string | ScriptBlock");
        if (sourceUrl is not (null or string or ScriptBlock)) {
            parser.AddIssue($"Required property '{parser.ObjectPath}Url' is present, " +
                            $"but has an incorrect type '{sourceUrl.GetType()}', expected 'string | ScriptBlock'.");
        }

        var expectedHash = parser.ParseScalar<string>("Hash", false);
        if (expectedHash == "") {
            // allow empty hash, otherwise Show-PogSourceHash would be less ergonomic
            expectedHash = null;
        }
        if (expectedHash != null && !Verify.Is.Sha256Hash(expectedHash)) {
            parser.AddValidityIssue("Hash", expectedHash, "expected a SHA-256 hash (64 character hex string)");
        }
        // hash should always be uppercase
        expectedHash = expectedHash?.ToUpperInvariant();

        var userAgentStr = parser.ParseScalar<string>("UserAgent", false);
        UserAgentType userAgent;
        if (userAgentStr == null) {
            userAgent = default;
        } else if (!Enum.TryParse(userAgentStr, true, out userAgent)) {
            parser.AddValidityIssue("UserAgent", userAgentStr,
                    $"supported values: {string.Join(", ", Enum.GetNames(typeof(UserAgentType)))}");
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
        foreach (var extraKey in parser.ExtraNonPrivateKeys) {
            (extraPropNames ??= []).Add(parser.ObjectPath + extraKey);
        }

        return parser.HasIssues ? null : parsed;
    }


    /// Lints the manifest for non-critical errors, calling <paramref name="addIssueCb"/> for each one.
    internal void Lint(Action<string> addIssueCb, string packageInfoStr, bool ignoreMissingHash) {
        if (_unknownPropertyNames != null) {
            foreach (var name in _unknownPropertyNames) {
                addIssueCb($"Found an unknown property '{name}' in the manifest for {packageInfoStr}. Private properties " +
                           $"must be prefixed with underscore (e.g. '_PrivateProperty'). It is possible that this property " +
                           $"was added in a newer version of Pog than you are using.");
            }
        }

        if (Install != null) {
            foreach (var ip in Install) {
                if (ip.ExpectedHash == null && !ignoreMissingHash) {
                    addIssueCb($"Missing checksum in the manifest for {packageInfoStr}. " +
                               "This means that during installation, Pog cannot verify if the downloaded file is the same" +
                               "one that the package author intended. This may or may not be a problem on its own, but " +
                               "it's a better style to include a checksum, and it improves security and reproducibility. " +
                               "Additionally, Pog can cache downloaded files if the checksum is provided.");
                }
            }
        }
    }
}

public abstract record PackageInstallParameters {
    /// Source URL, from which the archive is downloaded. Redirects are supported.
    public object SourceUrl = null!;

    /// SHA-256 hash that the downloaded archive should match. Validation is skipped if null, but a warning is printed.
    public string? ExpectedHash;

    /// Which User-Agent header to use. By default, Pog uses a custom user agent string containing the version of Pog,
    /// PowerShell and Windows. Unless the server dislikes the default user agent, prefer to keep it.
    /// Set this to `PowerShell` to use the default PowerShell user agent string.
    /// Set this to `Browser` to use a browser user agent string (currently Firefox).
    /// Set this to `Wget` to use wget user agent string.
    public UserAgentType UserAgent;

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

        // TODO: use something like New-ContainerModule here
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
