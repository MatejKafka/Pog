﻿using System.Collections;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Internal;

namespace Pog.Commands;

/// <summary>
/// <para type="synopsis">Downloads and extracts package files.</para>
/// <para type="description">
/// Downloads and extracts package files, populating the ./app directory of the package. Downloaded files
/// are cached, so repeated installs only require internet connection for the initial download.
/// </para>
/// </summary>
[UsedImplicitly]
[Cmdlet(VerbsLifecycle.Install, "Pog", DefaultParameterSetName = PackageNamePS)]
public class InstallPogCommand : ImportedPackageCommand {
    /// <summary><para type="description">
    /// If some version of the package is already installed, prompt before overwriting
    /// with the current version according to the manifest.
    /// </para></summary>
    [Parameter] public SwitchParameter Confirm;
    /// <summary><para type="description">
    /// Download files with low priority, which results in better network responsiveness
    /// for other programs, but possibly slower download speed.
    /// </para></summary>
    [Parameter] public SwitchParameter LowPriority;

    protected override void ProcessPackage(ImportedPackage package) {
        package.EnsureManifestIsLoaded();
        if (package.Manifest.Install == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Install block.", null);
            return;
        }

        WriteInformation($"Installing {package.GetDescriptionString()}...", null);

        var it = InvokePogCommand(new InvokeContainer(this) {
            ContainerType = Container.ContainerType.Install,
            Package = package,
            InternalArguments = new Hashtable {
                {"AllowOverwrite", !Confirm},
                {"DownloadLowPriority", (bool)LowPriority},
            },
        });

        // FIXME: probably discard container output, it breaks -PassThru
        foreach (var o in it) {
            WriteObject(o);
        }
    }
}
