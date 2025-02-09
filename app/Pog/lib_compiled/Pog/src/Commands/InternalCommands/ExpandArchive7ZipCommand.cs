using System;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;

namespace Pog.Commands.InternalCommands;

public class Failed7ZipArchiveExtractionException(string message) : Exception(message);

/// <summary>Extracts files from a specified archive file using 7Zip.</summary>
[PublicAPI]
[Cmdlet(VerbsData.Expand, "Archive7Zip")]
public sealed class ExpandArchive7ZipCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string ArchivePath = null!;
    [Parameter(Mandatory = true, Position = 1)] public string TargetPath = null!;
    /// If passed, only paths inside the archive matching at least one of the filters are extracted.
    [Parameter] public string[]? Filter;
    [Parameter] public ProgressActivity ProgressActivity = new();

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
