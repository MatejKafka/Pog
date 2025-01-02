using System;
using System.Linq;
using System.Management.Automation;
using System.Security;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;
using Pog.PSAttributes;

namespace Pog.Commands;

// TODO: some parts of this cmdlet are still quite hacky
/// <summary>Generate new manifests in a local package repository for the selected package manifest generator.</summary>
[PublicAPI]
[Cmdlet(VerbsData.Update, "PogRepository")]
[OutputType(typeof(TemplatedLocalRepositoryPackage))]
public class UpdatePogRepositoryCommand : PogCmdlet {
    /// Name of the manifest generator for which to generate new manifests.
    /// If not passed, all existing generators are invoked.
    [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [ArgumentCompleter(typeof(RepositoryPackageGeneratorNameCompleter))]
    public string[]? PackageName = null;

    // we only use -Version to match against retrieved versions, no need to parse
    /// List of versions to generate/update manifests for.
    [Parameter(Position = 1, ValueFromPipelineByPropertyName = true)]
    public string[]? Version = null;

    /// Regenerate even existing manifests. By default, only manifests for versions that
    /// do not currently exist in the repository are generated.
    [Parameter] public SwitchParameter Force;

    /// Only retrieve and list versions, do not generate manifests.
    [Parameter] public SwitchParameter ListOnly;

    /// <summary>GitHuh access token, automatically used by the provided cmdlets communicating with GitHub.</summary>
    /// <remarks>One possible use case is increasing the API rate limit, which is quite low for unauthenticated callers.</remarks>
    [Parameter] public SecureString? GitHubToken = null;

    private LocalRepository _repo = null!;
    private bool _updateAll;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        // place this check here; if we throw an exception in the constructor, XmlDoc2CmdletDoc fails,
        //  because it needs to create instances of all commands to get default parameter values
        if (InternalState.Repository is LocalRepository r) {
            _repo = r;
        } else {
            throw new RuntimeException("Updating repository packages is only supported for local repositories, " +
                                       "not remote. Please explicitly set a local repository.");
        }

        // TODO: this is duplicated in Find-Pog and Confirm-PogRepository
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
        }

        _updateAll = !MyInvocation.ExpectingInput && PackageName == null;
        if (_updateAll) {
            UpdateAll();
        }
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();
        if (_updateAll) return;
        if (PackageName == null) return;

        foreach (var pn in PackageName) {
            var generator = InternalState.GeneratorRepository.GetPackage(pn, true, true);
            // ensure manifest is loaded
            generator.ReloadManifest();

            // if -Version was passed, overwrite even existing manifests
            ProcessPackage(generator, Force || Version != null);
        }
    }

    private void UpdateAll() {
        var generators = InternalState.GeneratorRepository.Enumerate().ToArray();
        if (generators.Length == 0) {
            return;
        }

        using var progressBar = new CmdletProgressBar(this, new() {
            Activity = "Updating repository",
            // this duplicates the code below in the loop, but we have to do that due to
            //  https://github.com/PowerShell/PowerShell/issues/24728
            Description = $"Updating '{generators[0].PackageName}'...",
        });

        var i = 0;
        foreach (var generator in generators) {
            progressBar.Update((double) i++ / generators.Length, $"Updating '{generator.PackageName}'...");
            ProcessPackage(generator, Force);
        }
    }

    private void ProcessPackage(PackageGenerator generator, bool force) {
        var package = (LocalRepositoryVersionedPackage) _repo.GetPackage(generator.PackageName, true, true);
        if (!package.IsTemplated) {
            var e = new ArgumentException(
                    $"Package '{package.PackageName}' {(package.Exists ? "is not templated" : "does not exist yet")}, " +
                    "generators are only supported for existing templated packages.");
            WriteError(e, "NonTemplatedPackage", ErrorCategory.InvalidArgument, generator.PackageName);
            return;
        }

        var it = InvokePogCommand(new InvokeContainer(this) {
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_ManifestGenerator.psm1"],
            Run = ps => ps.AddCommand("__main").AddParameters(
                    new object?[] {generator, package, Version, force, ListOnly, GitHubToken}),
        });

        foreach (var o in it) {
            WriteObject(o);
        }
    }
}
