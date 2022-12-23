using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Pog.Commands.Utils;

namespace Pog.Commands;

public class Failed7ZipHashCalculationException : Exception {
    public Failed7ZipHashCalculationException(string message) : base(message) {}
}

[PublicAPI]
[Cmdlet(VerbsCommon.Get, "FileHash7Zip")]
[OutputType(typeof(string))]
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
    [Parameter(Position = 1)] public HashAlgorithm Algorithm = HashAlgorithm.SHA256;

    [Parameter] public int? ProgressActivityId;
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
    // -ba    = an undocumented flag which hides the printed header and only prints the resulting hash and file info
    private const string Args7Zip = "-bsp1 -spd -ba";

    // 7z.exe progress print pattern
    // e.g. ' 34% glib-2.dll'
    private static readonly Regex ProgressPrintRegex = new(@"^\s*(\d{1,3})%.*$");
    // 7z.exe hash print pattern
    // e.g. '466d43edd41763e4c9f03e6ac79fe9bea1ad6e5afa32779884ae50216aa22ae4    1067143478  CLion-2022.2.4.win.zip'
    private static readonly Regex HashPrintRegex = new(@"^([0-9a-fA-F]+)    .*$");

    protected override void BeginProcessing() {
        base.BeginProcessing();
        _algorithmStr = Algorithm.ToString().ToUpper();
        _fullPath = GetUnresolvedProviderPathFromPSPath(LiteralPath);

        _process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = InternalState.PathConfig.Path7Zip,
                Arguments = $"h {QuoteArgument("-scrc" + _algorithmStr)} {QuoteArgument(_fullPath)} {Args7Zip}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // we must capture stderr, otherwise it would fight with pwsh output
            },
        };

        var progressBar = new CmdletProgressBar(
                this, ProgressActivityId, ProgressActivity ?? "Calculating file hash",
                ProgressDescription ?? $"Calculating {_algorithmStr} hash for '{Path.GetFileName(_fullPath)}'...");

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
        } finally {
            progressBar.Dispose();
        }

        if (_process.ExitCode != 0) {
            // FIXME: is this ok? aren't we risking a deadlock when the pipe buffer fills up?
            var stderr = _process.StandardError.ReadToEnd().Trim();
            throw new Failed7ZipHashCalculationException(
                    $"Could not calculate file hash, '7zip' returned exit code {_process.ExitCode}:\n{stderr}");
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

    public static string Invoke(SessionState ss, string filePath, HashAlgorithm algorithm = HashAlgorithm.SHA256) {
        var result = ss.InvokeCommand.InvokeScript(ss, ScriptBlock.Create("Get-FileHash7Zip $Args[0] -Algorithm $Args[1]"),
                filePath, algorithm);
        Debug.Assert(result.Count == 1);
        return (string) result[0].BaseObject;
    }
}