using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace Pog.Commands;

public class Failed7ZipHashCalculationException : RuntimeException {
    public Failed7ZipHashCalculationException(string message) : base(message) {}
}

[PublicAPI]
[Cmdlet(VerbsCommon.Get, "FileHash7Zip")]
public class GetFileHash7ZipCommand : PSCmdlet, IDisposable {
    [PublicAPI]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum HashAlgorithm {
        CRC32, CRC64, SHA1, SHA256,
    }

    private int GetExpectedHashLength(HashAlgorithm algorithm) {
        return algorithm switch {
            HashAlgorithm.CRC32 => 8,
            HashAlgorithm.CRC64 => 16,
            HashAlgorithm.SHA1 => 40,
            HashAlgorithm.SHA256 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };
    }

    [Parameter(Mandatory = true, Position = 0)] public string LiteralPath = null!;
    [Parameter] public HashAlgorithm Algorithm = HashAlgorithm.SHA256;
    [Parameter] public string? ProgressActivity;
    [Parameter] public string? ProgressDescription;
    private string _algorithmStr = null!;
    private string _fullPath = null!;

    private Process? _process;

    private string QuoteArgument(string arg) {
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    // -bsp1  = enable progress reports
    // -spd   = disable wildcard matching for file names
    private const string Args7Zip = "-bsp1 -spd";

    // 7z.exe progress print pattern
    // e.g. ' 34% glib-2.dll'
    private static readonly Regex ProgressPrintRegex = new(@"^\s*(\d{1,3})%.*$");

    private void ShowProgress(bool completed, int percentageComplete) {
        WriteProgress(new ProgressRecord(1,
                ProgressActivity ?? "Calculating file hash",
                ProgressDescription ?? $"Calculating {_algorithmStr} hash for '{LiteralPath}'...") {
            PercentComplete = percentageComplete,
            RecordType = completed ? ProgressRecordType.Completed : ProgressRecordType.Processing
        });
    }

    protected override void BeginProcessing() {
        base.BeginProcessing();
        _algorithmStr = Algorithm.ToString().ToUpper();
        _fullPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(LiteralPath);

        _process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = "7z",
                Arguments = $"h {QuoteArgument("-scrc" + _algorithmStr)} {QuoteArgument(_fullPath)} {Args7Zip}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // we must capture stderr, otherwise it would fight with pwsh output
            },
        };

        string? hash = null;
        try {
            ShowProgress(false, 0);

            _process.Start();
            // forward progress prints and capture the checksum value
            while (_process.StandardOutput.ReadLine() is {} line) {
                var match = ProgressPrintRegex.Match(line);
                if (match.Success) {
                    ShowProgress(false, int.Parse(match.Groups[1].Value));
                } else if (line.StartsWith($"{_algorithmStr.PadRight("SHA256".Length)} for data:")) {
                    // this line contains the hash
                    hash = line.Substring(line.LastIndexOf(' ') + 1).ToUpper();
                } else {
                    // ignore other lines; error prints are shown directly through stderr, and there's
                    //  a lot of garbage output that's hard to filter
                }
            }
            _process.WaitForExit();
        } catch (PipelineClosedException) {
            // ignore
            // this is often (but not always) triggered on Ctrl-c, when the loop above tries to write to a closed pipeline
        } finally {
            ShowProgress(true, 100);
        }

        if (_process.ExitCode != 0) {
            // FIXME: is this ok? aren't we risking a deadlock when the pipe buffer fills up?
            var stderr = _process.StandardError.ReadToEnd();
            var prefix = stderr.StartsWith("\n") ? "" : "\n";
            throw new Failed7ZipHashCalculationException(
                    $"Could not calculate file hash, '7zip' returned exit code {_process.ExitCode}:{prefix}{stderr}");
        }

        if (hash != null) {
            Debug.Assert(hash.Length == GetExpectedHashLength(Algorithm));
            WriteObject(hash);
        } else {
            throw new InternalError($"7zip did not return the calculated hash for '{LiteralPath}'.");
        }
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        // the 7z process also receives Ctrl-c, so we don't need to kill it manually, just wait for exit
        // ideally, we would cancel the ReadLine() call above, but the StreamReader API doesn't support
        //  cancellation until .NET 7; however, 7z seems to exit reliably on receiving Ctrl-c, so this works
        _process!.WaitForExit();
    }

    public void Dispose() {
        _process?.Dispose();
    }
}