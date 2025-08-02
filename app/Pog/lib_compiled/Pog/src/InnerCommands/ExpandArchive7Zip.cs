using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using Pog.Commands.Common;
using Pog.Commands.InternalCommands;
using Pog.InnerCommands.Common;
using Pog.Native;
using Pog.Utils;

namespace Pog.InnerCommands;

internal sealed class ExpandArchive7Zip(PogCmdlet cmdlet) : VoidCommand(cmdlet) {
    [Parameter] public required string ArchivePath;
    [Parameter] public required string TargetPath;
    [Parameter] public string? RawTargetPath;
    /// If passed, only paths inside the archive matching at least one of the filters are extracted.
    [Parameter] public string[]? Filter = null;
    [Parameter] public ProgressActivity ProgressActivity = new();

    // 7z.exe progress print pattern
    // e.g. ' 34% 10 - glib-2.dll'
    private static readonly Regex ProgressPrintRegex = new(@"^\s*(\d{1,3})%\s+\S+.*$", RegexOptions.Compiled);

    public override void Invoke() {
        RawTargetPath ??= TargetPath;
        var filterPatterns = Filter switch {
            null => null,
            // if "." filter is present, result is equivalent to not passing any filter
            // this happens when `Subdirectory = "."` is specified for a package source
            _ when Filter.Any(p => p == ".") => null,
            // normalize slashes
            _ => Filter?.Select(p => p.Replace('/', '\\')).ToArray(),
        };

        // validate filter patterns
        foreach (var p in filterPatterns ?? []) {
            if (p.Split('\\').Any(s => s is "." or "..")) {
                // 7zip does not like patterns with `.` or `..`
                throw new ArgumentException($"Archive filter pattern must not contain '.' or '..', got '{p}'.");
            }
        }

        if (Directory.Exists(TargetPath)) {
            throw new IOException($"Target directory already exists: '{RawTargetPath}'", -2146232800);
        }

        WriteDebug($"Extracting archive using 7zip... (source: '{ArchivePath}', target: '{TargetPath}')");
        ProgressActivity.Activity ??= "Extracting archive with 7zip";
        ProgressActivity.Description ??= $"Extracting archive '{Path.GetFileName(ArchivePath)}'...";

        try {
            Invoke7Zip(filterPatterns);

            // ensure the target directory exists (if the archive was empty or the filter pattern excluded everything,
            //  7zip won't create the target directory)
            Directory.CreateDirectory(TargetPath);
        } catch {
            // ensure the output dir is cleaned up on any error
            // this may cause a slight delay on Ctrl-C, but hopefully not large enough to be annoying
            CleanupTargetDir();
            throw;
        }
    }

    private void Invoke7Zip(string[]? filterPatterns) {
        using var progressBar = new CmdletProgressBar(Cmdlet, ProgressActivity);
        using var process = new Process();
        process.StartInfo = SetupProcessStartInfo(ArchivePath, TargetPath, filterPatterns);
        process.Start();

        // ideally, we should cancel the ReadLine() call below, but the StreamReader API doesn't support
        //  cancellation until .NET 7 and killing 7z interrupts the loop as well
        // ReSharper disable once AccessToDisposedClosure
        using var cancellation = CancellationToken.Register(() => process.Kill());

        var errorStrSb = new StringBuilder();
        // forward progress prints and store errors
        // FIXME: we should probably be draining stdout somewhere, just in case 7zip decides to write something to it
        while (process.StandardError.ReadLine() is {} line) {
            var match = ProgressPrintRegex.Match(line);
            if (match.Success) {
                // sometimes, 7zip reports percentages higher than 100, not sure why
                progressBar.ReportPercent(Math.Min(int.Parse(match.Groups[1].Value), 100));
            } else if (string.IsNullOrWhiteSpace(line) || line.StartsWith("  0M Scan")
                                                       || line == "  0%" || line == "100%") {
                // ignore these prints, they are expected
            } else {
                // during normal operation, no additional output should be printed;
                //  if there is any, the user should see it
                errorStrSb.Append("\n" + line);
            }
        }

        process.WaitForExit();

        var stdout = process.StandardOutput.ReadToEnd();
        if (stdout != "") {
            // there shouldn't be anything written on stdout
            WriteWarning(stdout);
        }

        if (process.ExitCode != 0) {
            throw new Failed7ZipArchiveExtractionException(
                    $"Could not extract archive, '7zip' returned exit code {process.ExitCode}:{errorStrSb}");
        } else if (errorStrSb.Length != 0) {
            // TODO: really, just rewrite this shit using P/Invoke, it will be less painful
            // there's some error output; since `cmd.exe` hides the error code of the first 7z invocation for .tar.gz,
            //  we'll throw an error anyway
            throw new Failed7ZipArchiveExtractionException($"Could not extract archive:{errorStrSb}");
        }

        if (CancellationToken.IsCancellationRequested) {
            // signal that we were stopped by the user
            throw new PipelineStoppedException();
        }
    }

    private void CleanupTargetDir() {
        try {
            FsUtils.EnsureDeleteDirectory(TargetPath);
        } catch {
            WriteWarning($"Failed to clean up output directory due to an error: {TargetPath}");
        }
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
        // TODO: alternatively, use the replacement rules from flatt.tech/research/posts/batbadbut-you-cant-securely-execute-commands-on-windows/
        return "\"" + arg.Replace("\"", "\\\"").Replace("%", "%Q%") + "\"";
    }

    private static readonly Regex TarArchiveNameRegex =
            new(@"\.(tar\.(gz|xz|bz2)|tgz|txz|tbz)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsCompressedTarArchive(string archivePath) {
        // guess based on the extension
        // TODO: parse 7zip output to figure out the actual file type
        return TarArchiveNameRegex.IsMatch(archivePath);
    }

    private ProcessStartInfo SetupProcessStartInfo(string archivePath, string targetPath, string[]? filterPatterns) {
        if (IsCompressedTarArchive(archivePath)) {
            // 7zip extracts .tar.gz in two steps – first invocation outputs a .tar, which has to be extracted a second time
            // to avoid using a temporary file, we pipe 2 instances of 7zip together
            // however, C# doesn't provide a way to pipeline processes together, and doing it manually through P/Invoke
            //  would be a bit painful, so we instead use cmd.exe to setup the pipes
            WriteDebug("Using pipelined 7z invocation for a compressed .tar archive.");
            var path7Z = QuoteArgumentCmd(InternalState.PathConfig.Path7Zip);
            var startInfo = new ProcessStartInfo {
                FileName = "cmd",
                Arguments =
                        "/c \"" // why is this first quote here? read `cmd /?`, paragraph "If /C or /K is specified,...", then cry
                        + $" {path7Z} x {QuoteArgumentCmd(archivePath)}"
                        + " -so" // output to stdout instead of a file/directory
                        + " -bsp2" // print progress prints to stderr, where we can capture them; we must use stderr,
                        //            because stdout is occupied by the actual output
                        + $" | {path7Z} x {QuoteArgumentCmd("-o" + targetPath)}"
                        + (filterPatterns == null ? "" : " " + string.Join(" ", filterPatterns.Select(QuoteArgumentCmd)))
                        + " -si" // read from stdin
                        + " -ttar" // assume stdin is .tar
                        + " -aoa" // overwrite existing files
                        + " -bso0" // disable normal output (version, file names,...)
                        + " -bsp0" // disable progress prints (we get them from the first command, where we get
                        //            the percentage; here, we would get processed archive size instead, because
                        //            the second 7zip instance doesn't know the archive size in advance)
                        + "\"", // this is the ending quote for the one at the beginning
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
                FileName = InternalState.PathConfig.Path7Zip,
                Arguments =
                        $"x {Win32Args.EscapeArgument(archivePath)} {Win32Args.EscapeArgument("-o" + targetPath)}"
                        + (filterPatterns == null ? "" : $" {Win32Args.EscapeArguments(filterPatterns)}")
                        + " -bso0" // disable normal output
                        + " -bsp2" // enable progress prints to stderr (cannot use stdout for consistency with .tar.gz extraction above)
                        + " -aoa" // automatically overwrite existing files (should not usually occur, unless
                        //           the archive is a bit malformed, but NSIS installers occasionally do it for some reason)
                        + " -stxPE", // refuse to extract PE binaries, unless they're recognized as a self-contained installer like NSIS;
                //             otherwise, if a package downloaded the program executable directly and forgot to pass -NoArchive,
                //             7zip would extract the PE segments, which is not very useful
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true, // we must capture stderr, otherwise it would fight with pwsh output
            };
        }
    }
}
