using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Commands.Internal;

namespace Pog.Commands;

[UsedImplicitly]
[Cmdlet(VerbsCommon.Get, "FileHash7Zip")]
[OutputType(typeof(string))]
public class GetFileHash7ZipCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string LiteralPath = null!;
    [Parameter(Position = 1)] public GetFileHash7Zip.HashAlgorithm Algorithm = GetFileHash7Zip.HashAlgorithm.SHA256;
    [Parameter] public CmdletProgressBar.ProgressActivity ProgressActivity = new();

    protected override void BeginProcessing() {
        base.BeginProcessing();

        WriteObject(InvokePogCommand(new GetFileHash7Zip(this) {
            Path = GetUnresolvedProviderPathFromPSPath(LiteralPath),
            Algorithm = Algorithm,
            ProgressActivity = ProgressActivity,
        }));
    }
}
