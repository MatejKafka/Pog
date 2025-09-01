using System;
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
        // TODO: in the PowerShell version, we used to run Confirm-PogRepository here;
        //  think through whether it's a good idea to add that back
        SourcePackage.EnsureManifestIsLoaded();

        if (!Force && SourcePackage.MatchesImportedManifest(Package)) {
            // not happy with this being a warning, but WriteHost is imo not the right one to use here,
            // and WriteInformation will not be visible for users with default $InformationPreference
            WriteWarning($"Skipping import of {SourcePackage.GetDescriptionString()} to '{Package.Path}', " +
                         "target already contains this package. Pass '-Force' to override.");
            return false;
        }

        var actionStr = $"Importing {SourcePackage.GetDescriptionString()} to '{Package.Path}'.";
        if (!ShouldProcess(actionStr, actionStr, null)) {
            return false;
        }

        if (!ConfirmManifestOverwrite(SourcePackage, Package, out var targetVersion)) {
            WriteInformation($"Skipping import of package '{SourcePackage.PackageName}'.");
            return false;
        }

        // import the package, replacing the previous manifest (and creating the directory if the package is new)
        SourcePackage.ImportTo(Package, Backup);

        var nameStr = Package.PackageName == SourcePackage.PackageName ? "" : $" (package '{SourcePackage.PackageName}')";
        WriteInformation(targetVersion != null && targetVersion != SourcePackage.Version
                ? $"Updated '{Package.Path}'{nameStr} from version '{targetVersion}' to '{SourcePackage.Version}'."
                : $"Imported {SourcePackage.GetDescriptionString()} to '{Package.Path}'.");
        return true;
    }

    private bool ConfirmManifestOverwrite(RepositoryPackage source, ImportedPackage target,
            out PackageVersion? targetVersion) {
        PackageManifest? targetManifest = null;
        targetVersion = null;
        try {
            // try to load the (possibly) existing manifest
            // TODO: maybe add a method to only load the name and version from the manifest and skip full validation?
            targetManifest = target.ReloadManifest();
        } catch (PackageNotFoundException) {
            // the package does not exist, no need to confirm
            return true;
        } catch (PackageManifestNotFoundException) {
            // the package exists, but the manifest is missing
            // either a random folder was erroneously created, or this is a package, but corrupted
            WriteWarning($"A package directory already exists at '{target.Path}', but it doesn't seem to contain " +
                         $"a package manifest. All directories in a package root should be packages with a valid manifest.");
            // overwrite without confirmation
            return true;
        } catch (Exception e) when (e is IPackageManifestException) {
            WriteWarning($"Found an existing package manifest at '{target.Path}', but it is not valid.");
        }

        targetVersion = targetManifest?.Version;

        if (Diff && targetManifest != null) {
            // TODO: also probably check if any supporting files in .pog changed
            // FIXME: in typical scenarios, the `package.Manifest` access is the only reason why the manifest is parsed;
            //  figure out how to either get a raw string, or copy over the repository manifest to the imported package
            //  (since the target manifest is typically immediately used, loading it here won't cause much overhead)
            var diff = DiffRenderer.RenderDiff(targetManifest.RawString, source.Manifest.RawString, ignoreMatching: true);
            if (diff != "") WriteHost(diff);
        }

        if (Force) {
            return true;
        }

        if (targetVersion != null && targetVersion < source.Version) {
            // target is older than the imported package, continue silently
            return true;
        }

        // prompt for confirmation
        var downgrading = targetVersion != null && targetVersion > source.Version;
        var title = $"{(downgrading ? "Downgrade" : "Overwrite")} an existing package manifest for '{target.PackageName}'?";
        var manifestDescription =
                targetManifest == null ? "" : $" (manifest '{targetManifest.Name}', version '{targetVersion}')";
        var message = $"There is already an imported package '{target.PackageName}' at '{target.Path}'" +
                      $"{manifestDescription}. Overwrite its manifest with version '{source.Version}'?";
        return ShouldContinue(message, title);
    }
}
