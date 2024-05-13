using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Uninstalls a package.</para>
/// <para type="description">
/// Uninstalls a package by first disabling it (see `Disable-Pog`) and then deleting the package directory.
/// If -KeepData is passed, only the app, cache and logs directories are deleted and persistent data are left intact.
/// </para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Uninstall, "Pog", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class UninstallPogCommand() : ImportedPackageCommand(true) {
    /// <summary><para type="description">
    /// Keep the package directory, only disable the package and delete the app directory.
    /// </para></summary>
    [Parameter] public SwitchParameter KeepData;

    protected override void ProcessPackage(ImportedPackage package) {
        // disable the package
        InvokePogCommand(new DisablePog(this) {
            Package = package,
        });

        // atomically delete the app directory, if it exists
        var appDirPath = $@"{package.Path}\{PathConfig.PackagePaths.AppDirName}";
        var tmpDeletePath = $@"{package.Path}\{PathConfig.PackagePaths.TmpDeleteDirName}";
        if (FsUtils.EnsureDeleteDirectoryAtomically(appDirPath, tmpDeletePath)) {
            WriteVerbose($"Deleted the app directory for '{package.PackageName}'.");
        }

        // also delete the cache and logs directory
        var deleted = false;
        deleted |= FsUtils.EnsureDeleteDirectory($@"{package.Path}\{PathConfig.PackagePaths.CacheDirName}");
        deleted |= FsUtils.EnsureDeleteDirectory($@"{package.Path}\{PathConfig.PackagePaths.LogDirName}");
        if (deleted) {
            WriteVerbose($"Deleted cache and logs for '{package.PackageName}'.");
        }

        if (!KeepData) {
            // delete all subdirectories first; this way, the package remains valid even if something fails
            //  (e.g. due to an opened file); TODO: check for used files similarly to Install-Pog
            foreach (var e in Directory.EnumerateDirectories(package.Path)) {
                if (e == package.ManifestResourceDirPath) {
                    continue;
                }
                FsUtils.EnsureDeleteDirectory(e);
            }

            // delete the whole package directory, including the manifest
            // TODO: it might be nice to make this atomic, and do the same with setup in Import-Pog
            FsUtils.EnsureDeleteDirectory(package.Path);
            WriteInformation($"Uninstalled package '{package.PackageName}'.");
        } else {
            WriteInformation($"Uninstalled package '{package.PackageName}', preserving app data.");
        }
    }
}
