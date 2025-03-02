using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.PSAttributes;
using Pog.Utils;

namespace Pog.Commands;

/// <summary>>Validates that an installed package is well-formed.</summary>
/// <para>
/// Supported parameter modes:
/// 1) no arguments, no pipeline input -> Validates structure of all installed packages and package roots.
/// 2) PackageName / Package -> Validates the selected installed package.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Confirm, "Pog", DefaultParameterSetName = DefaultPS)]
public sealed class ConfirmPogCommand : PogCmdlet {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public ImportedPackage[] Package = null!;

    /// Names of installed packages to validate. If not passed, all installed packages are validated.
    [Parameter(Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[]? PackageName;

    /// If set, do not warn about missing checksums in packages.
    [Parameter]
    public SwitchParameter IgnoreMissingHash;

    private readonly ImportedPackageManager _packages = InternalState.ImportedPackageManager;
    private bool _noIssues = true;

    private static readonly Regex QuoteHighlightRegex = new(@"'([^']+)'",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private void AddIssue(string message) {
        _noIssues = false;
        var aligned = message.Replace("\n", "\n         ");
        // highlight everything in quotes by turning off the bold format (which is the default for warnings)
        var highlighted = QuoteHighlightRegex.Replace(aligned, $"'\x1b[22m$1\x1b[1m'");
        WriteWarning(highlighted);
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        if (ParameterSetName == PackagePS) {
            foreach (var p in Package) {
                WriteVerbose($"Validating installed package '{p.PackageName}'...");
                ValidatePackage(p);
            }
        } else if (PackageName != null) {
            foreach (var p in PackageName.SelectOptional(ResolvePackage)) {
                WriteVerbose($"Validating installed package '{p.PackageName}'...");
                ValidatePackage(p);
            }
        } else {
            WriteVerbose("Validating all installed packages...");
            ValidateAll();
        }
    }

    private ImportedPackage? ResolvePackage(string packageName) {
        try {
            return InternalState.ImportedPackageManager.GetPackage(packageName, true, false);
        } catch (ImportedPackageNotFoundException e) {
            WriteError(new ErrorRecord(e, "PackageNotFound", ErrorCategory.InvalidArgument, packageName));
            return null;
        }
    }

    protected override void EndProcessing() {
        base.EndProcessing();
        WriteObject(_noIssues);
    }

    private void ValidateAll() {
        var foundPackageNames = new HashSet<string>();
        var validPackageRootExists = false;
        foreach (var packageRoot in _packages.PackageRoots.AllPackageRoots) {
            if (!Directory.Exists(packageRoot)) {
                AddIssue($"Package root '{packageRoot}' is registered, but the directory does not exist. " +
                         $"Remove the package root using 'Edit-PogRoot'.");
                continue;
            }

            validPackageRootExists = true;
            var extraFiles = GetFileList(Directory.EnumerateFiles(packageRoot));
            if (extraFiles != "") {
                AddIssue($"Package root '{packageRoot}' contains extra files, " +
                         $"only package directories expected: {extraFiles}");
            }

            // validate all packages in the package root
            foreach (var package in _packages.Enumerate(packageRoot, false)) {
                if (!foundPackageNames.Add(package.PackageName)) {
                    AddIssue($"Duplicate package '{package.PackageName}' in different package roots.");
                }
                ValidatePackage(package);
            }
        }

        if (!validPackageRootExists) {
            AddIssue("No valid package roots are registered, packages cannot be installed.");
        }
    }

    private void ValidatePackage(ImportedPackage p) {
        try {
            p.ReloadManifest();
        } catch (Exception e) when (e is IPackageManifestException) {
            AddIssue(e.Message);
            return;
        }

        p.Manifest.Lint(AddIssue, p.GetDescriptionString(), IgnoreMissingHash);

        // skip directory structure checks for private packages
        if (p.Manifest.Private) {
            return;
        }

        // validate that root only contains pog.psd1 and shortcuts as files
        var extraFiles = GetFileList(Directory.EnumerateFiles(p.Path)
                .Where(path => !path.EndsWith("\\pog.psd1") && !path.EndsWith("\\pog.user.psd1") &&
                               !path.EndsWith("\\Desktop.ini") && !path.EndsWith(".lnk")));
        if (extraFiles != "") {
            AddIssue($"Package '{p.PackageName}' contains extra files, only the 'pog.psd1' manifest file, " +
                     $"an optional 'pog.user.psd1' config file, 'Desktop.ini' and exported shortcuts expected, " +
                     $"at '{p.Path}': {extraFiles}");
        }

        // validate that root only contains directories in an allow-list
        var extraDirs = GetFileList(Directory.EnumerateDirectories(p.Path)
                .Select(Path.GetFileName).Where(n => !AllowedDirs.Contains(n)));
        if (extraDirs != "") {
            AddIssue($"Package '{p.PackageName}' contains unknown directories, at '{p.Path}': {extraDirs} " +
                     $"(allowed directories: {string.Join(", ", AllowedDirs)})");
        }
    }

    private static readonly HashSet<string> AllowedDirs = new() {
        "app", "cache", "logs", "data", "config",
        ".pog", ".commands", // internal Pog dirs
        ".user_private", // dir where users can put their custom files, scripts,...
    };

    private string GetFileList(IEnumerable<string> paths) {
        return string.Join(", ", paths.Select(Path.GetFileName).Select(name => $"'{name}'"));
    }
}
