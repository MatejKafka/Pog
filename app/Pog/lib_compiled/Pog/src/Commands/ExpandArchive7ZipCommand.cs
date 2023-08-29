using System;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Commands.Internal;

namespace Pog.Commands;

public class Failed7ZipArchiveExtractionException : Exception {
    public Failed7ZipArchiveExtractionException(string message) : base(message) {}
}

/// <summary>
/// <para type="synopsis">Extracts files from a specified archive file using 7Zip.</para>
/// </summary>
[PublicAPI]
[Cmdlet(VerbsData.Expand, "Archive7Zip")]
public class ExpandArchive7ZipCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string ArchivePath = null!;
    [Parameter(Mandatory = true, Position = 1)] public string TargetPath = null!;
    /// <summary><para type="description">
    /// If passed, only paths inside the archive matching at least one of the filters are extracted.
    /// </para></summary>
    [Parameter] public string[]? Filter;
    [Parameter] public CmdletProgressBar.ProgressActivity ProgressActivity = new();

    protected override void BeginProcessing() {
        base.BeginProcessing();

        InvokePogCommand(new ExpandArchive7Zip(this) {
            ArchivePath = GetUnresolvedProviderPathFromPSPath(ArchivePath),
            TargetPath = GetUnresolvedProviderPathFromPSPath(TargetPath),
            RawTargetPath = TargetPath,
            Filter = Filter,
            ProgressActivity = ProgressActivity,
        });
    }
}
