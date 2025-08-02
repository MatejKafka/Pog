using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands.InternalCommands;

/// Hash algorithms supported by 7zip.
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum HashAlgorithm7Zip {
    // SHA256 is `default(T)`
    SHA256 = 0, SHA512, SHA1, CRC32, CRC64,
}

/// <summary>Calculates a checksum of the specified file using 7zip.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "FileHash7Zip")]
[OutputType(typeof(string))]
public sealed class GetFileHash7ZipCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public string LiteralPath = null!;
    [Parameter(Position = 1)] public HashAlgorithm7Zip Algorithm = default;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        WriteObject(InvokePogCommand(new GetFileHash7Zip(this) {
            Path = GetUnresolvedProviderPathFromPSPath(LiteralPath),
            Algorithm = Algorithm,
        }));
    }
}
