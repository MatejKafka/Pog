using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;

namespace Pog.Commands.ContainerCommands;

/// <summary>Retrieves a file from the passed URL and calculates the SHA-256 hash.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "UrlHash")]
[OutputType(typeof(string))]
public sealed class GetUrlHashCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] SourceUrl = null!;

    [Parameter(ValueFromPipeline = true)]
    public UserAgentType UserAgent = default;

    protected override void ProcessRecord() {
        base.ProcessRecord();

        var downloadParams = new DownloadParameters(UserAgent);
        foreach (var url in SourceUrl) {
            WriteObject(InvokePogCommand(new InvokeFileDownload(this) {
                SourceUrl = url,
                DownloadParameters = downloadParams,
                ProgressActivity = new ProgressActivity("Retrieving file hash"),
                ComputeHash = true,
            }).Hash);
        }
    }
}
