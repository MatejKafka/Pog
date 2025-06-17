using System;
using System.Collections.Generic;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.Native;

namespace Pog.Commands;

/// <summary>Downloads all resources needed to install the given package and shows the SHA-256 hashes.</summary>
/// <para>
/// Download all resources specified in the package manifest, store them in the download cache and show the SHA-256 hash.
/// This cmdlet is useful for retrieving the hashes when writing a package manifest.
/// </para>
[PublicAPI]
[Cmdlet(VerbsCommon.Show, "PogSourceHash", DefaultParameterSetName = DefaultPS, SupportsShouldProcess = true)]
public sealed class ShowPogSourceHashCommand : PogSourceHashCommandBase {
    private bool _first = true;
    private List<string> _hashes = [];

    protected override void ProcessPackage(Package package) {
        package.EnsureManifestIsLoaded();
        if (package.Manifest.Install == null) {
            WriteInformation($"Package '{package.PackageName}' does not have an Install block.");
            return;
        }

        foreach (var source in package.Manifest.EvaluateInstallUrls(package)) {
            if (!_first) WriteHost("");
            _first = false;

            var url = (string) source.Url;
            var hash = RetrieveSourceHash(package, source, url);

            _hashes.Add(hash);
            // setting the clipboard here ensures that even if something fails during the download of a subsequent source,
            //  we will still store the previous sources
            Clipboard.SetText(string.Join("\n", _hashes));

            WriteHost($"Hash for the file at '{url}' (copied to clipboard):");
            WriteHost(hash, foregroundColor: ConsoleColor.White);

            if (source.ExpectedHash != null) {
                if (source.ExpectedHash == hash) {
                    WriteHost("Matches the expected hash specified in the manifest.", foregroundColor: ConsoleColor.Green);
                } else {
                    var errorMsg = $"The retrieved hash does not match the expected hash specified in the manifest " +
                                   $"(expected: '{source.ExpectedHash}').";
                    WriteError(new IncorrectFileHashException(errorMsg), "IncorrectHash", ErrorCategory.InvalidResult, url);
                }
            }
        }
    }
}
