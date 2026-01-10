using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using JetBrains.Annotations;
using Pog.Utils;

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

[PublicAPI]
public record PackageManifest {
    public readonly bool Private;
    public readonly string? Name;
    public readonly PackageVersion? Version;
    public readonly PackageArchitecture[]? Architecture;
    // public readonly PackageChannel? Channel;

    /// List of paths that the program accesses outside its package directory.
    /// <remarks>
    /// If this property is non-null, it indicates that the package is non-portable.
    /// An empty list indicates that the package is non-portable, with unspecified paths.
    /// </remarks>
    public readonly string[]? NonPortablePaths;

    public readonly string? Description;
    public readonly string? Website;

    public readonly PackageSource[]? Install;
    public readonly ScriptBlock? Enable;
    public readonly ScriptBlock? Disable;

    public readonly Hashtable Raw;

    private readonly string _rawManifestStr;
    public override string ToString() => _rawManifestStr;

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
    /// <exception cref="InvalidPackageManifestStructureException">The package manifest was correctly parsed but has an invalid structure.</exception>
    private PackageManifest((string, Hashtable) manifest, string manifestSource, RepositoryPackage? owningPackage = null) {
        InstrumentationCounter.ManifestLoads.Increment();

        (_rawManifestStr, Raw) = manifest;
        // parse the raw manifest into properties on this object
        var parser = new HashtableParser(Raw);

        Private = parser.ParseScalar<bool>(nameof(Private), false) ?? false;
        if (Private) {
            if (owningPackage != null) {
                parser.AddIssue("Property 'Private' is not allowed in manifests in a package repository.");
            } else {
                parser.IgnoreRequired = true;
            }
        }

        Name = parser.ParseScalar<string>(nameof(Name), true);
        // check that the manifest name matches package name, if passed
        if (owningPackage != null && Name != owningPackage.PackageName) {
            parser.AddValidityIssue(nameof(Name), Name, $"expected '{owningPackage.PackageName}'");
        }

        var versionStr = parser.ParseScalar<string>(nameof(Version), true);
        Version = versionStr == null ? null : ParseVersion(parser, versionStr);
        // check that the manifest version matches the package version, if passed
        if (owningPackage != null && versionStr != owningPackage.Version.ToString()) {
            parser.AddValidityIssue(nameof(Version), versionStr, $"expected '{owningPackage.Version}'");
        }

        var archRaw = parser.ParseList<string>(nameof(Architecture), true);
        Architecture = archRaw == null ? null : ParseArchitecture(parser, archRaw);

        NonPortablePaths = parser.ParseList<string>(nameof(NonPortablePaths), false);

        Description = parser.ParseScalar<string>(nameof(Description), false);
        Website = parser.ParseScalar<string>(nameof(Website), false);

        var installRaw = parser.ParseList<Hashtable>(nameof(Install), true);
        Install = installRaw == null ? null : ParseInstallBlock(parser, installRaw, ref _unknownPropertyNames);
        Enable = parser.ParseScalar<ScriptBlock>(nameof(Enable), true);
        Disable = parser.ParseScalar<ScriptBlock>(nameof(Disable), false);

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
            parser.AddValidityIssue(nameof(Version), versionStr, e.Message);
            return null;
        }
    }

    private static PackageSource[]? ParseInstallBlock(HashtableParser parentParser, Hashtable[] raw,
            ref List<string>? extraPropNames) {
        if (raw.Length == 0) {
            parentParser.AddIssue($"Value of '{nameof(Install)}' must not be an empty array.");
        }

        var parsed = new PackageSource[raw.Length];
        for (var i = 0; i < raw.Length; i++) {
            var p = ParseInstallHashtable(new HashtableParser(raw[i], $"{nameof(Install)}[{i}].", parentParser.Issues),
                    ref extraPropNames);
            if (p == null) return null;
            else parsed[i] = p;
        }
        return parsed;
    }

    public enum PackageArchitecture { Any, X64, X86, Arm64 }

    private static Dictionary<string, PackageArchitecture> _architectureMap = new(StringComparer.InvariantCultureIgnoreCase) {
        {"*", PackageArchitecture.Any},
        {"x86", PackageArchitecture.X86},
        {"x64", PackageArchitecture.X64},
        {"arm64", PackageArchitecture.Arm64},
    };

    private static PackageArchitecture[]? ParseArchitecture(HashtableParser parser, string[] raw) {
        var parsed = new PackageArchitecture[raw.Length];
        for (var i = 0; i < raw.Length; i++) {
            if (!_architectureMap.TryGetValue(raw[i], out parsed[i])) {
                parser.AddIssue($"Invalid '{nameof(Architecture)}' value: {raw[i]} " +
                                $"(supported values: {string.Join(", ", _architectureMap.Keys)})");
                return null;
            }
        }
        return parsed;
    }

    private static PackageSource? ParseInstallHashtable(HashtableParser parser, ref List<string>? extraPropNames) {
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

        PackageSource parsed;
        if (noArchive) {
            parsed = new PackageSourceNoArchive {
                Url = sourceUrl!,
                ExpectedHash = expectedHash,
                UserAgent = userAgent,
                // if null, we will throw an exception anyway
                Target = target!,
            };
        } else {
            parsed = new PackageSourceArchive {
                Url = sourceUrl!,
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

    /// <summary>
    /// Resolves URLs of all installation sources (if the URL is defined using a scriptblock, it is invoked to get the actual
    /// URL) and returns a copy of <see cref="PackageSource"/> with the resolved URL.
    /// </summary>
    ///
    /// <remarks>
    /// NOTE: This method executes potentially untrusted code from the manifest.<br/>
    /// NOTE: This method assumes that there's a usable PowerShell runspace set up for the calling thread.<br/>
    /// NOTE: This command can be safely invoked outside a container.
    /// </remarks>
    ///
    /// <exception cref="InvalidPackageManifestUrlScriptBlockException"></exception>
    public IEnumerable<PackageSource> EvaluateInstallUrls(Package owningPackage) {
        return EvaluateInstallUrls(owningPackage is ILocalPackage p ? p.ManifestPath : owningPackage.PackageName);
    }

    /// <inheritdoc cref="EvaluateInstallUrls(Pog.Package)"/>
    public IEnumerable<PackageSource> EvaluateInstallUrls(string manifestSourceStr = "<unknown>") {
        return Install == null ? [] : Install.Select(s => s with {Url = s.EvaluateUrl(Raw, manifestSourceStr)});
    }
}

public record PackageSourceNoArchive : PackageSource {
    // mandatory when NoArchive is set, otherwise the name of the binary would be controlled by the server
    //  we're downloading from, making the resulting package no longer reproducible based on just the hash
    /// The downloaded file is moved to `./app/$Target`. The path must include the file name.
    public string Target = null!;
}

public record PackageSourceArchive : PackageSource {
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

[PublicAPI]
public abstract record PackageSource {
    /// Source URL, from which the archive is downloaded. Redirects are supported. This is either a plain string,
    /// or a ScriptBlock that returns the URL on invocation (<seealso cref="EvaluateUrl"/>).
    public object Url = null!;

    /// SHA-256 hash that the downloaded archive should match. Validation is skipped if null, but a warning is printed.
    public string? ExpectedHash;

    /// Which User-Agent header to use. By default, Pog uses a custom user agent string containing the version of Pog,
    /// PowerShell and Windows. Unless the server dislikes the default user agent, prefer to keep it.
    /// Set this to `PowerShell` to use the default PowerShell user agent string.
    /// Set this to `Browser` to use a browser user agent string (currently Firefox).
    /// Set this to `Wget` to use wget user agent string.
    public UserAgentType UserAgent;

    /// Prefer not to use directly, use <see cref="PackageManifest.EvaluateInstallUrls(string)"/> instead.
    internal string EvaluateUrl(Hashtable rawManifest, string manifestSourceStr) {
        if (Url is string s) {
            return s; // static string, just return
        }

        var sb = (ScriptBlock) Url;
        ValidateUrlScriptBlock(sb);

        var resolvedUrlObj = InvokeUrlSb(sb, rawManifest, manifestSourceStr);
        if (resolvedUrlObj.Count != 1) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    $"must return a single string, got {resolvedUrlObj.Count} values: {string.Join(", ", resolvedUrlObj)}");
        }

        var obj = resolvedUrlObj[0]?.BaseObject;
        if (obj is not string resolvedUrl) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    $"must return a string, got '{obj?.GetType().ToString() ?? "null"}'");
        }
        return resolvedUrl;
    }

    private Collection<PSObject> InvokeUrlSb(ScriptBlock sb, Hashtable rawManifest, string manifestSourceStr) {
        // there doesn't seem to be any easier way to set strict mode for a scope
        // this does not leave any leftovers in the caller's scope
        var wrapperSb = ScriptBlock.Create("Set-StrictMode -Version 3; & $Args[0]");
        var variables = new List<PSVariable>
                {new("this", rawManifest), new("ErrorActionPreference", ActionPreference.Stop)};

        try {
            return wrapperSb.InvokeWithContext(null, variables, sb);
        } catch (RuntimeException e) {
            // something failed inside the scriptblock
            var ii = e.ErrorRecord.InvocationInfo;
            // replace the position info with a custom listing, since the script path is missing
            var graphic = ii.PositionMessage.Substring(ii.PositionMessage.IndexOf('\n') + 1);
            var positionMsg = $"At {manifestSourceStr}, Install.Url:{ii.ScriptLineNumber}\n" + graphic;
            // FIXME: in "NormalView" error view, the error looks slightly confusing, as it's designed for "ConciseView"
            throw new InvalidPackageManifestUrlScriptBlockException(
                    $"failed. Please fix the package manifest or report the issue to the package maintainer:\n" +
                    $"    {e.Message.Replace("\n", "\n    ")}\n\n" +
                    $"    {positionMsg.Replace("\n", "\n    ")}\n", e);
        }
    }

    /// Validates that the passed source URL generator scriptblock does not invoke any cmdlets, does not use any variables
    /// that it does not itself define and does not assign to any non-local variables.
    ///
    /// <remarks>The goal is not to be 100% robust, but serve mostly as a lint. We're just attempting to prevent a manifest
    /// author from using cmdlets or variables that could be locally overriden. The alternative that was originally used was
    /// to run the scriptblock in a container, but that has non-trivial setup overhead.</remarks>
    private static void ValidateUrlScriptBlock(ScriptBlock sb) {
        var cmdletCalls = sb.Ast.FindAll(node => node is CommandAst, true).ToArray();
        if (cmdletCalls.Length != 0) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "must not invoke any commands, since the user may have aliased them in their PowerShell profile. " +
                    $"Found the following command invocations: {string.Join(", ", cmdletCalls.Select(c => $"`{c}`"))}");
        }

        var assignments = sb.Ast.FindAllByType<VariableExpressionAst>(true)
                .Split(v => v.Parent is AssignmentStatementAst, out var usages)
                .ToDictionary(v => v.VariablePath.UserPath, StringComparer.OrdinalIgnoreCase);

        var nonLocalAssignments = assignments.Values.Where(a => !a.VariablePath.IsUnqualified).ToArray();
        if (nonLocalAssignments.Length > 0) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "must not assign to any non-local variables. Found the following non-local variable assignments: " +
                    $"{string.Join(", ", nonLocalAssignments.Select(c => $"`{c}`"))}");
        }

        var invalidUsages = usages
                .Where(u => !assignments.ContainsKey(u.VariablePath.UserPath))
                // $this is the only allowed external variable
                .Where(u => u.VariablePath.UserPath != "this")
                .ToArray();

        if (invalidUsages.Length > 0) {
            throw new InvalidPackageManifestUrlScriptBlockException(
                    "must not use any variables that were not previously defined in the scriptblock, except for `$this`. " +
                    $"Found the following variable usages: {string.Join(", ", invalidUsages.Select(c => $"`{c}`"))}");
        }
    }
}

/// <summary>Package manifest had a ScriptBlock as the 'Install.Url' property, but it did not return a valid URL.</summary>
public class InvalidPackageManifestUrlScriptBlockException : Exception, IPackageManifestException {
    private const string Prefix = "ScriptBlock for the source URL ('Install.Url' property in the package manifest) ";

    internal InvalidPackageManifestUrlScriptBlockException(string message) : base(Prefix + message) {}
    internal InvalidPackageManifestUrlScriptBlockException(string message, Exception e) : base(Prefix + message, e) {}
}
