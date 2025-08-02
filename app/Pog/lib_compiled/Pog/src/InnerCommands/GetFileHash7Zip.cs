using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Pog.Commands.Common;
using Pog.Commands.InternalCommands;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

public class Failed7ZipHashCalculationException(string message) : Exception(message);

internal sealed class GetFileHash7Zip(PogCmdlet cmdlet) : ScalarCommand<string>(cmdlet) {
    [Parameter] public required string Path;
    [Parameter] public HashAlgorithm7Zip Algorithm = default;
    [Parameter] public ProgressActivity ProgressActivity = new();

    // -bsp1  = enable progress reports
    // -spd   = disable wildcard matching for file names
    // -ba    = an undocumented flag which hides the printed header and only prints the resulting hash and file info
    private const string Args7Zip = "-bsp1 -spd -ba";

    // 7z.exe progress print pattern
    // e.g. ' 34% glib-2.dll'
    private static readonly Regex ProgressPrintRegex = new(@"^\s*(\d{1,3})%.*$");
    // 7z.exe hash print pattern
    // e.g. '466d43edd41763e4c9f03e6ac79fe9bea1ad6e5afa32779884ae50216aa22ae4    1067143478  CLion-2022.2.4.win.zip'
    private static readonly Regex HashPrintRegex = new(@"^([0-9a-fA-F]+)    .*$");

    private static string QuoteArgument(string arg) {
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    public override string Invoke() {
        ProgressActivity.Activity ??= "Calculating file hash";
        ProgressActivity.Description ??= $"Calculating {Algorithm} hash for '{System.IO.Path.GetFileName(Path)}'...";
        using var progressBar = new CmdletProgressBar(Cmdlet, ProgressActivity);

        using var process = new Process();
        process.StartInfo = SetupProcessStartInfo();
        process.Start();

        // ideally, we should cancel the ReadLine() call below, but the StreamReader API doesn't support
        //  cancellation until .NET 7 and killing 7z interrupts the loop as well
        // ReSharper disable once AccessToDisposedClosure
        using var cancellation = CancellationToken.Register(() => process.Kill());

        string? hash = null;
        // forward progress prints and capture the checksum value
        while (process.StandardOutput.ReadLine() is {} line) {
            var match = ProgressPrintRegex.Match(line);
            if (match.Success) {
                progressBar.ReportPercent(int.Parse(match.Groups[1].Value));
            } else if (string.IsNullOrWhiteSpace(line) || line.StartsWith("  0M Scan") || line == "  0%") {
                // ignore these prints, they are expected
            } else if ((match = HashPrintRegex.Match(line)).Success) {
                // this line contains the hash
                hash = match.Groups[1].Value.ToUpper();
            } else {
                // during normal operation, no additional output should be printed;
                //  however, for some reason, some error messages seem to be duplicated
                //  at both stdout and stderr, so we ignore these messages and hope that
                //  all the error information will be also printed on stderr
            }
        }

        process.WaitForExit();

        if (process.ExitCode != 0) {
            // FIXME: is this ok? aren't we risking a deadlock when the pipe buffer fills up?
            var stderr = process.StandardError.ReadToEnd().Trim();
            throw new Failed7ZipHashCalculationException(
                    $"Could not calculate file hash, '7zip' returned exit code {process.ExitCode}:\n{stderr}");
        }

        if (hash == null) {
            throw new InternalError($"7zip did not return the calculated hash for '{Path}'.");
        }

        if (CancellationToken.IsCancellationRequested) {
            // signal that we were stopped by the user
            throw new PipelineStoppedException();
        }

        // * 2 for hex
        Debug.Assert(hash.Length == 2 * GetHashLength(Algorithm));
        return hash;
    }

    private ProcessStartInfo SetupProcessStartInfo() {
        return new ProcessStartInfo {
            FileName = InternalState.PathConfig.Path7Zip,
            Arguments = $"h {QuoteArgument("-scrc" + Algorithm)} {QuoteArgument(Path)} {Args7Zip}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true, // we must capture stderr, otherwise it would fight with pwsh output
        };
    }

    private static int GetHashLength(HashAlgorithm7Zip algorithm) {
        return algorithm switch {
            HashAlgorithm7Zip.SHA256 => 32,
            HashAlgorithm7Zip.SHA512 => 64,
            HashAlgorithm7Zip.SHA1 => 20,
            HashAlgorithm7Zip.CRC32 => 4,
            HashAlgorithm7Zip.CRC64 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };
    }
}
