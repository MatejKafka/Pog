using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using System.Text.RegularExpressions;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

public class Failed7ZipHashCalculationException : Exception {
    public Failed7ZipHashCalculationException(string message) : base(message) {}
}

public class GetFileHash7Zip : ScalarCommand<string>, IDisposable {
    [Parameter(Mandatory = true)] public string Path = null!;
    [Parameter] public HashAlgorithm Algorithm = default;

    [Parameter] public CmdletProgressBar.ProgressActivity ProgressActivity = new();

    private string _algorithmStr = null!;
    private Process? _process;
    private bool _stopping = false;

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

    public GetFileHash7Zip(PogCmdlet cmdlet) : base(cmdlet) {}

    private static string QuoteArgument(string arg) {
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    public override string Invoke() {
        _algorithmStr = Algorithm.ToString().ToUpper();

        _process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = InternalState.PathConfig.Path7Zip,
                Arguments = $"h {QuoteArgument("-scrc" + _algorithmStr)} {QuoteArgument(Path)} {Args7Zip}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // we must capture stderr, otherwise it would fight with pwsh output
            },
        };

        ProgressActivity.Activity ??= "Calculating file hash";
        ProgressActivity.Description ??= $"Calculating {_algorithmStr} hash for '{System.IO.Path.GetFileName(Path)}'...";
        using var progressBar = new CmdletProgressBar(Cmdlet, ProgressActivity);

        string? hash = null;
        try {
            _process.Start();
            // forward progress prints and capture the checksum value
            while (_process.StandardOutput.ReadLine() is {} line) {
                var match = ProgressPrintRegex.Match(line);
                if (match.Success) {
                    progressBar.Update(int.Parse(match.Groups[1].Value));
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
            _process.WaitForExit();
        } catch (PipelineClosedException) {
            // ignore
            // this is often (but not always) triggered on Ctrl-c, when the loop above tries to write to a closed pipeline
        }

        if (_stopping) {
            // signal that we were stopped by the user
            throw new PipelineStoppedException();
        }

        if (_process.ExitCode != 0) {
            // FIXME: is this ok? aren't we risking a deadlock when the pipe buffer fills up?
            var stderr = _process.StandardError.ReadToEnd().Trim();
            throw new Failed7ZipHashCalculationException(
                    $"Could not calculate file hash, '7zip' returned exit code {_process.ExitCode}:\n{stderr}");
        }

        if (hash != null) {
            Debug.Assert(hash.Length == GetExpectedHashLength(Algorithm));
            return hash;
        } else {
            throw new InternalError($"7zip did not return the calculated hash for '{Path}'.");
        }
    }

    public override void StopProcessing() {
        base.StopProcessing();
        _stopping = true;
        // the 7z process also receives Ctrl-c, so we don't need to kill it manually, just wait for exit
        // ideally, we would cancel the ReadLine() call above, but the StreamReader API doesn't support
        //  cancellation until .NET 7; however, 7z seems to exit reliably on receiving Ctrl-c, so this works
        _process!.WaitForExit();
    }

    public void Dispose() {
        _process?.Dispose();
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum HashAlgorithm {
        // SHA256 is `default(T)`
        SHA256 = 0, CRC32, CRC64, SHA1,
    }

    private static int GetExpectedHashLength(HashAlgorithm algorithm) {
        return algorithm switch {
            HashAlgorithm.CRC32 => 8,
            HashAlgorithm.CRC64 => 16,
            HashAlgorithm.SHA1 => 40,
            HashAlgorithm.SHA256 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };
    }
}
