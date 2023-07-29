﻿using System;
using System.Management.Automation;
using JetBrains.Annotations;
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
public class ExpandArchive7ZipCommand : PSCmdlet, IDisposable {
    [Parameter(Mandatory = true, Position = 0)] public string ArchivePath = null!;
    [Parameter(Mandatory = true, Position = 1)] public string TargetPath = null!;
    /// <summary>
    /// <para type="description">
    /// Subdirectory of the archive to extract instead of the whole archive. Note that this only acts as a filter,
    /// so `TargetPath` still corresponds to the root directory of the archive.
    /// </para>
    /// </summary>
    [Parameter]
    public string? Subdirectory;

    [Parameter] public int? ProgressActivityId;
    [Parameter] public string? ProgressActivity;
    [Parameter] public string? ProgressDescription;

    private ExpandArchive7Zip? _command = null;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        _command = new ExpandArchive7Zip(this, ArchivePath, TargetPath, Subdirectory,
                ProgressActivityId, ProgressActivity, ProgressDescription);
        _command.Invoke();
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        _command?.StopProcessing();
    }

    public void Dispose() {
        _command?.Dispose();
    }
}
