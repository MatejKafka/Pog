﻿using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;

namespace Pog.Commands.InternalCommands;

/// <summary>Downloads a file using BITS with the server-provided file name.</summary>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "FileDownload")]
[OutputType(typeof(string))]
public sealed class InvokeFileDownloadCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string SourceUrl = null!;
    [Parameter(Mandatory = true, Position = 1)] public string DestinationDirPath = null!;
    [Parameter] public DownloadParameters DownloadParameters = new();
    [Parameter] public ProgressActivity ProgressActivity = new();

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var destinationDir = GetUnresolvedProviderPathFromPSPath(DestinationDirPath);

        // ensure the destination dir exists
        Directory.CreateDirectory(destinationDir);

        WriteObject(InvokePogCommand(new InvokeFileDownload(this) {
            SourceUrl = SourceUrl,
            DestinationDirPath = destinationDir,
            DownloadParameters = DownloadParameters,
            ProgressActivity = ProgressActivity,
        }));
    }
}
