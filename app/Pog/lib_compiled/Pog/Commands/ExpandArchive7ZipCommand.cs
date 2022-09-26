﻿using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace Pog.Commands;

public class Failed7ZipArchiveExtractionException : RuntimeException {
    public Failed7ZipArchiveExtractionException(string message) : base(message) {}
}

[PublicAPI]
[Cmdlet(VerbsData.Expand, "Archive7Zip")]
public class ExpandArchive7ZipCommand : PSCmdlet, IDisposable {
    [Parameter(Mandatory = true, Position = 0)] public string ArchivePath = null!;
    [Parameter(Mandatory = true, Position = 1)] public string TargetPath = null!;
    [Parameter] public string? ProgressActivity;
    [Parameter] public string? ProgressDescription;

    private string _fullArchivePath = null!;
    private string _fullTargetPath = null!;

    private Process? _process;

    // 7z.exe progress print pattern
    // e.g. ' 34% 10 - glib-2.dll'
    private static readonly Regex ProgressPrintRegex = new(@"^\s*(\d{1,3})%\s+\S+.*$");

    private void ShowProgress(bool completed, int percentageComplete) {
        WriteProgress(new ProgressRecord(1,
                ProgressActivity ?? "Extracting package with 7zip",
                ProgressDescription ?? $"Extracting package from '{ArchivePath}'...") {
            PercentComplete = percentageComplete,
            RecordType = completed ? ProgressRecordType.Completed : ProgressRecordType.Processing
        });
    }

    protected override void BeginProcessing() {
        base.BeginProcessing();
        _fullArchivePath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ArchivePath);
        _fullTargetPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(TargetPath);
        WriteDebug($"Extracting archive using 7zip... (source: '{_fullArchivePath}', target: '{_fullTargetPath}')");
        _process = new Process {StartInfo = SetupProcessStartInfo(_fullArchivePath, _fullTargetPath)};

        var completed = false;
        var errorStrSb = new StringBuilder();
        try {
            ShowProgress(false, 0);

            _process.Start();
            // FIXME: we should probably be draining stdout somewhere, just in case 7zip decides to write something to it
            // forward progress prints and store errors
            while (_process.StandardError.ReadLine() is {} line) {
                var match = ProgressPrintRegex.Match(line);
                if (match.Success) {
                    ShowProgress(false, int.Parse(match.Groups[1].Value));
                } else if (string.IsNullOrWhiteSpace(line) || line.StartsWith("  0M Scan") || line == "  0%") {
                    // ignore these prints, they are expected
                } else {
                    // during normal operation, no additional output should be printed;
                    //  if there is any, the user should see it
                    errorStrSb.Append("\n" + line);
                }
            }
            _process.WaitForExit();
            completed = true;
        } catch (PipelineClosedException) {
            // ignore
            // this is often (but not always) triggered on Ctrl-c, when the loop above tries to write to a closed pipeline
        } finally {
            if (!completed) {
                CleanupTargetDir();
            }
            ShowProgress(true, 100);
        }

        var stdout = _process.StandardOutput.ReadToEnd();
        if (stdout != "") {
            // there shouldn't be anything written on stdout
            WriteWarning(stdout);
        }

        if (_process.ExitCode != 0) {
            throw new Failed7ZipArchiveExtractionException(
                    $"Could not extract archive, '7zip' returned exit code {_process.ExitCode}:{errorStrSb}");
        } else if (errorStrSb.Length != 0) {
            // TODO: really, just rewrite this shit using P/Invoke, it will be less painful
            // there's some error output; since `cmd.exe` hides the error code of the first 7z invocation for .tar.gz,
            //  we'll throw an error anyway
            throw new Failed7ZipArchiveExtractionException($"Could not extract archive:{errorStrSb}");
        }

        if (!Directory.Exists(_fullTargetPath)) {
            throw new InternalError(
                    "Could not extract archive: '7zip' indicated success, but the extracted directory is missing.");
        }
    }

    private void CleanupTargetDir() {
        try {
            Directory.Delete(_fullTargetPath, true);
        } catch (DirectoryNotFoundException) {
            // ignore
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

    /// Quote argument for passing it to `cmd.exe`.
    private static string QuoteArgumentCmd(string arg) {
        // we're in a world of hurt now
        // https://stackoverflow.com/a/31420292
        // Uh. `cmd.exe` is a bit... special...
        // If it sees e.g. `%PATH%` (or any other variable reference) in an argument, it will expand it. Unfortunately,
        // % is also a valid char to use in a filename. The fucking catch is, there's no way to escape % inside quotes
        // (yeah, WAT?) (see the link above and enjoy the mental suffering). However, `cmd.exe` only expands each
        // variable once; therefore, we can define our own variable Q (defined in `SetupProcessStartInfo`), containing `%`,
        // and replace each occurrence of % with %Q%. This way, we should be safe. Maybe. Hopefully. Dunno. Run.
        // TODO: just stop this pain and write our own pipeline setup using P/Invoke
        //  https://learn.microsoft.com/en-us/windows/win32/procthread/creating-a-child-process-with-redirected-input-and-output
        return "\"" + arg.Replace("\"", "\\\"").Replace("%", "%Q%") + "\"";
    }

    private static string QuoteArgument(string arg, bool cmdExe = false) {
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private ProcessStartInfo SetupProcessStartInfo(string archivePath, string targetPath) {
        if (archivePath.EndsWith(".tar.gz") || archivePath.EndsWith(".tgz")) {
            // 7zip extracts .tar.gz in two steps – first invocation outputs a .tar, which has to be extracted a second time
            // to avoid using a temporary file, we pipe 2 instances of 7zip together
            // however, C# doesn't provide a way to pipeline processes together, and doing it manually through P/Invoke
            //  would be a bit painful, so we instead use cmd.exe to setup the pipes
            WriteDebug("Using pipelined 7z invocation for .tar.gz/.tgz archive.");
            var startInfo = new ProcessStartInfo {
                FileName = "cmd",
                Arguments =
                        "/c"
                        + $" 7z x {QuoteArgumentCmd(archivePath)}"
                        + " -so" // output to stdout instead of a file/directory
                        + " -tgzip" // require input to be .tar.gz
                        + " -bsp2" // print progress prints to stderr, where we can capture them; we must use stderr,
                        //            because stdout is occupied by the actual output
                        + " -spd" // disable wildcard matching for file names
                        + $" | 7z x {QuoteArgumentCmd("-o" + targetPath)}"
                        + " -si" // read from stdin
                        + " -ttar" // assume stdin is .tar
                        + " -aoa" // overwrite existing files
                        + " -bso0" // disable normal output (version, file names,...)
                        + " -bsp0", // disable progress prints (we get them from the first command, where we get
                //                     the percentage; here, we would get processed archive size instead, because
                //                     the second 7zip instance doesn't know the archive size in advance)
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = {
                    ["Q"] = "%", // see QuoteArgumentCmd
                },
            };
            return startInfo;
        } else {
            WriteDebug("Using direct 7z invocation.");
            return new ProcessStartInfo {
                FileName = "7z",
                Arguments =
                        $"x {QuoteArgument(archivePath)} {QuoteArgument("-o" + targetPath)}"
                        + " -bso0" // disable normal output
                        + " -bsp2" // enable progress prints to stderr (cannot use stdout for consistency with .tar.gz extraction above)
                        + " -aoa" // automatically overwrite existing files (should not usually occur, unless
                        //           the archive is a bit malformed, but NSIS installers occasionally do it for some reason)
                        + " -stxPE" // refuse to extract PE binaries, unless they're recognized as a self-contained installer like NSIS;
                        //             otherwise, if a package downloaded the program executable directly and forgot to pass -NoArchive,
                        //             7zip would extract the PE segments, which is not very useful
                        + " -spd", // disable wildcard matching for file names
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // we must capture stderr, otherwise it would fight with pwsh output
            };
        }
    }
}