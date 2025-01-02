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

// TODO: allow passing the local repository as an argument?
/// <summary>Validates that a package from a local repository is well-formed.</summary>
/// <para>
/// Supported parameter modes:
/// 1) no arguments, no pipeline input -> Validates structure of the whole local repository, including all packages.
/// 2) PackageName, no Version -> Validates root of the package and all versions.
/// 3) PackageName + Version / Package -> Validates the selected version of the package.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Confirm, "PogRepository", DefaultParameterSetName = DefaultPS)]
public sealed class ConfirmPogRepositoryCommand : PogCmdlet {
    protected const string PackagePS = "Package";
    protected const string PackageNamePS = "PackageName";
    protected const string DefaultPS = PackageNamePS;

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public LocalRepositoryPackage[] Package = null!;

    /// Name of the repository package.
    [Parameter(Position = 0, ParameterSetName = PackageNamePS, ValueFromPipeline = true)]
    [ArgumentCompleter(typeof(RepositoryPackageNameCompleter))]
    public string[]? PackageName = null;

    /// Version of the repository package to validate.
    [Parameter(Position = 1, ParameterSetName = PackageNamePS)]
    [ArgumentCompleter(typeof(RepositoryPackageVersionCompleter))]
    public PackageVersion? Version;

    // we have some packages that do not provide versioned binaries in the repository,
    //  so skipping this specific check is useful
    /// If set, do not warn about missing checksums in packages.
    [Parameter]
    public SwitchParameter IgnoreMissingHash;

    private LocalRepository _repo = null!;
    private bool _noIssues = true;

    private static readonly Regex QuoteHighlightRegex = new("'([^']+)'",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private void AddIssue(string message) {
        _noIssues = false;
        var aligned = message.Replace("\n", "\n         ");
        // highlight everything in quotes by turning off the bold format (which is the default for warnings)
        var highlighted = QuoteHighlightRegex.Replace(aligned, $"'\x1b[22m$1\x1b[1m'");
        WriteWarning(highlighted);
    }

    protected override void BeginProcessing() {
        base.BeginProcessing();

        // place this check here; if we throw an exception in the constructor, XmlDoc2CmdletDoc fails,
        //  because it needs to create instances of all commands to get default parameter values
        if (InternalState.Repository is LocalRepository r) {
            _repo = r;
        } else {
            throw new RuntimeException("Validation of remote repositories and repository lists is not supported. " +
                                       "Please explicitly set a local repository.");
        }

        if (Version != null) {
            if (MyInvocation.ExpectingInput) {
                ThrowArgumentError(Version, "VersionWithPipelineInput",
                        "-Version must not be passed together with pipeline input.");
            } else if (PackageName == null) {
                ThrowArgumentError(Version, "VersionWithoutPackage",
                        "-Version must not be passed without also passing -PackageName.");
            } else if (PackageName.Length > 1) {
                ThrowArgumentError(Version, "VersionWithMultiplePackages",
                        "-Version must not be passed when -PackageName contains multiple package names.");
            }

            LocalRepositoryPackage? package;
            try {
                package = (LocalRepositoryPackage) _repo.GetPackage(PackageName![0], true, true)
                        .GetVersionPackage(Version, true);
            } catch (RepositoryPackageNotFoundException e) {
                WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, PackageName![0]);
                return;
            } catch (RepositoryPackageVersionNotFoundException e) {
                WriteError(e, "PackageVersionNotFound", ErrorCategory.ObjectNotFound, Version);
                return;
            }

            ValidatePackageVersion(package);
        }
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        if (Version != null) {
            return; // already processed above
        }

        if (ParameterSetName == PackagePS) {
            foreach (var p in Package) {
                WriteVerbose($"Validating repository package '{p.PackageName}', version '{p.Version}'...");
                ValidatePackageVersion(p);
            }
        } else if (PackageName != null) {
            foreach (var vp in PackageName.SelectOptional(ResolvePackage)) {
                WriteVerbose($"Validating all versions of the repository package '{vp.PackageName}'...");
                ValidatePackage(vp);
            }
        } else {
            WriteVerbose("Validating the whole package repository...");
            ValidateAll();
        }
    }

    protected override void EndProcessing() {
        base.EndProcessing();
        WriteObject(_noIssues);
    }

    private LocalRepositoryVersionedPackage? ResolvePackage(string packageName) {
        try {
            return (LocalRepositoryVersionedPackage) _repo.GetPackage(packageName, true, true);
        } catch (RepositoryPackageNotFoundException e) {
            WriteError(e, "PackageNotFound", ErrorCategory.ObjectNotFound, packageName);
            return null;
        }
    }

    private void ValidateAll() {
        if (!Directory.Exists(_repo.Path)) {
            AddIssue($"Repository directory does not exist, expected path: {_repo.Path}");
            return;
        }

        var extraFiles = GetFileList(Directory.EnumerateFiles(_repo.Path));
        if (extraFiles != "") {
            AddIssue($"Repository directory contains unexpected files, " +
                     $"only directories expected at '{_repo.Path}': {extraFiles}");
        }

        // validate all packages
        foreach (var vp in _repo.Enumerate()) {
            ValidatePackage((LocalRepositoryVersionedPackage) vp);
        }
    }

    private void ValidatePackage(LocalRepositoryVersionedPackage vp) {
        if (vp.IsTemplated) ValidateTemplatedPackage(vp);
        else ValidateDirectPackage(vp);
    }

    private void ValidateTemplatedPackage(LocalRepositoryVersionedPackage vp) {
        if (File.Exists($"{vp.Path}\\.template.psd1")) {
            AddIssue($"Package '{vp.PackageName}' contains an invalid version '.template'. " +
                     $"This version is not allowed, as it leads to ambiguity for direct packages.");
        }

        var templateDirPath = vp.TemplateDirPath;
        var templatePath = vp.TemplatePath;

        var extraFiles = GetFileList(Directory.EnumerateFiles(vp.Path).Where(p => !p.EndsWith(".psd1")));
        if (extraFiles != "") {
            AddIssue($"Package '{vp.PackageName}' has an incorrect file structure, contains extra files, " +
                     $"only .psd1 manifest files expected at '{vp.Path}': {extraFiles}");
        }

        var extraDirs = GetFileList(Directory.EnumerateDirectories(vp.Path).Where(p => p != templateDirPath));
        if (extraDirs != "") {
            AddIssue($"Package '{vp.PackageName}' has an incorrect file structure, contains extra directories, " +
                     $"only the '.template' is allowed at '{vp.Path}': {extraDirs}");
        }

        // validate .template dir
        ValidateManifestDirectory($"package '{vp.PackageName}'", templateDirPath, true);

        // validate that manifest template exists
        if (!File.Exists(templatePath)) {
            AddIssue($"Template file is missing for package '{vp.PackageName}', expected path: {templatePath}");
            return; // does not make sense to continue, since each package would error out
        }

        ValidatePackageVersions(vp, false); // manifest dir already validated
    }

    private void ValidateDirectPackage(LocalRepositoryVersionedPackage vp) {
        var extraFiles = GetFileList(Directory.EnumerateFiles(vp.Path));
        if (extraFiles != "") {
            AddIssue($"Package '{vp.PackageName}' has an incorrect file structure, contains extra files, " +
                     $"only sub-directories should be present at '{vp.Path}': {extraFiles}");
        }

        ValidatePackageVersions(vp, true);
    }

    private void ValidatePackageVersions(LocalRepositoryVersionedPackage vp, bool validateManifestDir) {
        var hasVersion = false;
        foreach (var p in vp.Enumerate()) {
            hasVersion = true;
            ValidatePackageVersion((LocalRepositoryPackage) p, validateManifestDir);
        }

        if (!hasVersion) {
            AddIssue($"Package '{vp.PackageName}' does not have any version. " +
                     $"Each package should have at least one version.");
        }
    }

    // FIXME: when there's an issue in the template, this will print a warning a for each version; maybe add a heuristic
    //  where a warning is collapsed if it's relevant for all versions?
    private void ValidatePackageVersion(LocalRepositoryPackage p, bool validateManifestDir = true) {
        if (validateManifestDir) {
            var path = p is TemplatedLocalRepositoryPackage tp ? tp.TemplateDirPath : p.Path;
            ValidateManifestDirectory(p.GetDescriptionString(), path, false);
        }

        // validate the manifest
        try {
            p.ReloadManifest();
        } catch (Exception e) when (e is IPackageManifestException) {
            AddIssue($"Invalid manifest for package '{p.PackageName}', version '{p.Version}': {e.Message}");
            return;
        }

        if (p.PackageName != p.Manifest.Name) {
            AddIssue($"Non-matching name '{p.Manifest.Name}' in the package manifest for " +
                     $"package '{p.PackageName}', version '{p.Version}.");
        }

        if (p.Version != p.Manifest.Version) {
            AddIssue($"Non-matching version '{p.Manifest.Version}' in the package manifest for " +
                     $"package '{p.PackageName}', version '{p.Version}.");
        }

        if (p.Manifest.Install is {} installParams) {
            foreach (var ip in installParams) {
                if (ip.ExpectedHash == null && !IgnoreMissingHash) {
                    AddIssue($"Missing checksum in package '{p.PackageName}', version '{p.Version}'. " +
                             "This means that during installation, Pog cannot verify if the downloaded file is the same" +
                             "one that the package author intended. This may or may not be a problem on its own, but " +
                             "it's a better style to include a checksum, and it improves security and reproducibility. " +
                             "Additionally, Pog can cache downloaded files if the checksum is provided.");
                }
            }
        }
    }

    private void ValidateManifestDirectory(string packageInfoStr, string manifestDirPath, bool isTemplate) {
        var extraEntries = Directory.EnumerateFileSystemEntries(manifestDirPath)
                .Where(p => !p.EndsWith(@"\pog.psd1") && !p.EndsWith(@"\.pog"));

        if (isTemplate) {
            // allow the generator manifest for template dir
            extraEntries = extraEntries.Where(p => !p.EndsWith(@"\generator.psd1"));
        }

        var extraEntriesStr = GetFileList(extraEntries);
        if (extraEntriesStr != "") {
            AddIssue($"Manifest directory for {packageInfoStr} at '{manifestDirPath}' contains extra entries: " +
                     $"{extraEntriesStr}. Only a 'pog.psd1' manifest file{(isTemplate ? ", a 'generator.psd1' generator file" : "")} " +
                     $"and an optional '.pog' directory for extra files is allowed.");
        }

        if (File.Exists($"{manifestDirPath}\\.pog")) {
            AddIssue($"Extra resource directory for {packageInfoStr} must be a directory, " +
                     $"found a file at '{manifestDirPath}\\.pog'.");
        }

        // pog.psd1 manifest is validated separately
    }

    private string GetFileList(IEnumerable<string> paths) {
        return string.Join(", ", paths.Select(Path.GetFileName).Select(name => $"'{name}'"));
    }
}
