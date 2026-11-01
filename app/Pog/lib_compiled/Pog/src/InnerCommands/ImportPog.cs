using System;
using System.Diagnostics;
using System.Management.Automation;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

internal sealed class ImportPog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet) {
    [Parameter] public required RepositoryPackage SourcePackage;
    [Parameter] public required bool Diff;
    [Parameter] public required bool Force;
    [Parameter] public required bool Backup;

    public override bool Invoke() {
        // if a manifest backup exists, restore it; this may happen when package installation in Invoke-Pog crashes;
        //  more generally, the existence of an undeleted manifest backup means that the content of the package does
        //  not match its current manifest, so we should behave as if the backup is the actual manifest
        if (Package.RestoreManifestBackup()) {
            WriteWarning($"Restored manifest backup for package '{Package.PackageName}' from an interrupted installation.");
        }

        // TODO: in the PowerShell version, we used to run Confirm-PogRepository here;
        //  think through whether it's a good idea to add that back
        SourcePackage.EnsureManifestIsLoaded();

        if (!Force && SourcePackage.MatchesImportedManifest(Package)) {
            // not happy with this being a warning, but WriteHost is imo not the right one to use here,
            // and WriteInformation will not be visible for users with default $InformationPreference
            WriteWarning($"Skipping import of {SourcePackage.GetDescriptionString()} to '{Package.Path}', " +
                         "target already contains this package version. Pass '-Force' to override.");
            return false;
        }

        var actionStr = $"Importing {SourcePackage.GetDescriptionString()} to '{Package.Path}'.";
        if (!ShouldProcess(actionStr, actionStr, null)) {
            return false;
        }

        LoadPackageManifest(Package, out var targetManifest, out var rawTargetManifest);
        if (Diff && rawTargetManifest != null) {
            // `source` manifest is already loaded above
            var rawSourceManifest = SourcePackage.RawManifest!;
            var diff = DiffRenderer.RenderDiff(rawTargetManifest, rawSourceManifest, ignoreMatching: true);
            if (diff != "") WriteHost(diff);
        }

        if (!ConfirmImport(SourcePackage, Package, targetManifest)) {
            WriteInformation($"Skipping import of package '{SourcePackage.PackageName}'.");
            return false;
        }

        if (SourcePackage.Manifest.NonPortablePaths is {} paths) {
            if (paths.Length > 0) {
                var s = paths.Length == 1 ? "" : "s";
                WriteWarning($"Package '{SourcePackage.PackageName}' is non-portable, " +
                             $"it uses the following path{s}: {string.Join(", ", paths)}");
            } else {
                WriteWarning($"Package '{SourcePackage.PackageName}' is non-portable.");
            }
        }

        // import the package, replacing the previous manifest (and creating the directory if the package is new)
        SourcePackage.ImportTo(Package, Backup);
        return true;
    }

    private void LoadPackageManifest(ImportedPackage target, out PackageManifest? manifest, out string? rawManifest) {
        try {
            // try to load the (possibly) existing manifest
            // TODO: maybe add a method to only load the name and version from the manifest and skip full validation?
            manifest = target.ReloadManifest();
            rawManifest = target.RawManifest;
        } catch (PackageNotFoundException) {
            // the package does not exist, no need to confirm
            manifest = null;
            rawManifest = null;
        } catch (PackageManifestNotFoundException) {
            // the package exists, but the manifest is missing
            // either a random folder was erroneously created, or this is a package, but corrupted
            WriteWarning($"A package directory already exists at '{target.Path}', but it doesn't seem to contain " +
                         $"a package manifest. All directories in a package root should be packages with a valid manifest.");
            // overwrite without confirmation
            manifest = null;
            rawManifest = null;
        } catch (Exception e) when (e is IPackageManifestException) {
            WriteWarning($"Found an existing package manifest at '{target.Path}', but it is not valid.");
            manifest = null;
            // this should be populated here, needed to show the diff
            rawManifest = target.RawManifest;
        }
    }

    private bool ShouldContinueForce(string query, string caption, string successLog) {
        var confirmed = Force || ShouldContinue(query, caption);
        if (confirmed) WriteInformation(successLog);
        return confirmed;
    }

    private bool ConfirmImport(RepositoryPackage source, ImportedPackage target,
            PackageManifest? targetManifest) {
        // this is a fresh import (no preexisting package) or the existing target is invalid
        if (targetManifest == null) {
            WriteInformation($"Importing {source.GetDescriptionString()} to '{target.Path}'...");
            return true;
        }

        if (targetManifest.Private) {
            // not a repository package, make sure the user really intends to overwrite it
            var title = $"Replace private package at '{target.Path}' with repository {source.GetDescriptionString()}?";
            var message = $"This seems to be a local-only package since it is marked as private. " +
                          $"Are you sure you want to replace it?";
            return ShouldContinueForce(message, title,
                    $"Replacing private package '{target.Path}' with {source.GetDescriptionString()}...");
        }

        // checking for .Private above ensures that the manifest has a name and version (otherwise it would fail to parse)
        Debug.Assert(targetManifest.Name != null);
        Debug.Assert(targetManifest.Version != null);

        if (targetManifest.Name != source.PackageName) {
            // we're changing the package, not just the version, this is quite unusual, prompt for confirmation
            var title = $"Replace package '{targetManifest.Name}' at '{target.Path}' " +
                        $"with repository {source.GetDescriptionString()}?";
            var message = "The currently used manifest is for a different package. Are you sure you want to replace it?";
            return ShouldContinueForce(message, title,
                    $"Replacing package '{targetManifest.Name}' at '{target.Path}' with {source.GetDescriptionString()}...");
        }

        if (source.Version < targetManifest.Version) {
            // downgrading, show a warning but continue without confirmation;
            //  the user specified an explicit version, so they likely intend to downgrade
            WriteWarning($"Downgrading '{target.Path}' from '{targetManifest.Version}' to '{source.Version}'...");
        } else if (source.Version == targetManifest.Version) {
            WriteInformation($"Importing updated manifest for {source.GetDescriptionString()} to '{Package.Path}'...");
        } else {
            WriteInformation($"Updating '{Package.Path}' from version '{targetManifest.Version}' to '{source.Version}'...");
        }
        return true;
    }
}
