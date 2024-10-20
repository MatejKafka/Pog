﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Native;
using IOPath = System.IO.Path;

namespace Pog.InnerCommands;

/// Lists processes that have a lock (an open handle without allowed sharing) on a file under $Path.
internal class GetLockedFile(PogCmdlet cmdlet) : ScalarCommand<IEnumerable<GetLockedFile.LockingProcessInfo>?>(cmdlet) {
    [Parameter(Mandatory = true)] public string Path = null!;

    public record LockingProcessInfo(string ProcessInfoStr, string[] LockedFiles) {
        public LockingProcessInfo(string procInfoStr, IEnumerable<XElement> fileElems)
                : this(procInfoStr, fileElems.Select(f => f.Element("full_path")!.Value).ToArray()) {}
    }

    public override IEnumerable<LockingProcessInfo>? Invoke() {
        var ofvPath = GetOfvPath();
        if (ofvPath == null) {
            WriteVerbose("Skipping locked file listing, since OpenedFilesView is not installed.");
            return null;
        }

        var resolvedPath = GetUnresolvedProviderPathFromPSPath(Path);
        using var progressBar = new CmdletProgressBar(Cmdlet, new(
                "Locked files", $"Searching for locked files in '{resolvedPath}'..."));
        return ListLockingProcesses(resolvedPath, ofvPath);
    }

    private static IEnumerable<LockingProcessInfo> ListLockingProcesses(string path, string ofvPath) {
        var outFile = IOPath.GetTempFileName();
        XDocument procs;
        try {
            var startInfo = new ProcessStartInfo {
                FileName = ofvPath,
                Arguments = Win32Args.EscapeArguments(["/sxml", outFile, "/nosort", "/filefilter", path]),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(startInfo)!) {
                process.WaitForExit();
                if (process.ExitCode != 0) {
                    throw new Exception($"Could not list processes locking files in '{path}'" +
                                        $" (OpenedFilesView returned exit code '{process.ExitCode}').");
                }
            }

            // the XML generated by OFV contains an invalid XML tag `<%_position>`, replace it
            // FIXME: error-prone, assumes the default UTF8 encoding, otherwise the XML might get mangled
            var outXmlStr = Regex.Replace(File.ReadAllText(outFile), "(<|</)%_position>", "$1percentual_position>");
            procs = XDocument.Parse(outXmlStr);
        } finally {
            File.Delete(outFile);
        }

        var procFileIt = procs.Root!.Elements().GroupBy(item => item.Element("process_path")?.Value);
        foreach (var proc in procFileIt) {
            if (proc.Key == null) {
                foreach (var p in proc.GroupBy(item => item.Element("process_id")!.Value)) {
                    yield return new($"Process #{p.Key}", p);
                }
            } else if (proc.Key.EndsWith("DllHost.exe", StringComparison.OrdinalIgnoreCase)) {
                foreach (var p in proc.GroupBy(item => item.Element("process_id")!.Value)) {
                    yield return new(GetDllhostOwner(uint.Parse(p.Key)), p);
                }
            } else {
                yield return new(proc.Key, proc);
            }
        }
    }

    private static readonly Regex DllhostRegex = new("^.*DllHost.exe\"? /Processid:(\\{[A-Z0-9-]{36}\\})$");
    private static readonly Dictionary<string, string> KnownAppIDs = new() {
        // Plan9FileSystem
        ["{DFB65C4C-B34F-435D-AFE9-A86218684AA8}"] = "WSL2",
        // something related to the previous entry, not sure what it actually does
        ["{72075277-282A-420A-8C25-62BFCB94C71E}"] = "WSL2",
    };

    private static string GetDllhostOwner(uint processId) {
        // if this ever becomes too slow, we could extract the command line from the process PEB instead:
        //  https://stackoverflow.com/a/46006415
        using var searcher = new ManagementObjectSearcher(
                "SELECT ExecutablePath, CommandLine from Win32_Process WHERE ProcessID = " + processId);
        using var coll = searcher.Get();
        using var o = coll.Cast<ManagementObject>().FirstOrDefault();
        if (o == null) {
            return $"Unknown dllhost.exe process #{processId}";
        }

        var match = DllhostRegex.Match(o["CommandLine"].ToString());
        if (!match.Success) {
            return o["ExecutablePath"]!.ToString();
        }

        var appId = match.Groups[1].Value;
        if (KnownAppIDs.TryGetValue(appId, out var resolved)) {
            return resolved + " (dllhost.exe)";
        }

        var appIdDescription = Registry.GetValue(@$"HKEY_CLASSES_ROOT\AppID\{appId}", null, null);
        return appIdDescription as string ?? $"Unknown dllhost.exe process #{processId} (appID: {appId})";
    }

    private static string? GetOfvPath() {
        var path = InternalState.PathConfig.PathOpenedFilesView;
        return File.Exists(path) ? path : null;
    }

    // private void WaitForLockedFiles(string dirPath, int attemptNumber, bool checkDirItself) {
    //     const ConsoleColor lockedFilePrintColor = ConsoleColor.Red;
    //     WriteDebug("The previous app directory seems to be used.");
    //
    //     if (!_lockFileListShown) {
    //         // FIXME: better message
    //         WriteHost("The package seems to be in use, trying to find the offending processes...");
    //         // FIXME: port to C#
    //         Cmdlet.InvokeCommand.InvokeScript($"ShowLockedFileList {lockedFilePrintColor}");
    //     }
    //
    //     // if this gets called a second time, the user did not close everything, print the up-to-date list again
    //     _lockFileListShown = false;
    //
    //     try {
    //         // TODO: automatically continue when the listed processes are closed or close the files (might be hard to detect)
    //         Cmdlet.Host.UI.Write(lockedFilePrintColor, ConsoleColor.Black,
    //                 "\nPlease close the applications listed above, then press Enter to continue...: ");
    //         Cmdlet.Host.UI.ReadLine();
    //     } catch (PSInvalidOperationException e) {
    //         // Host is not interactive, just throw an exception
    //         var exception = new PSInvalidOperationException(
    //                 "Cannot overwrite an existing package installation, because processes listed in the output above " +
    //                 "are working with files inside the package.", e);
    //         // TODO: shouldn't this be a non-terminating error?
    //         Cmdlet.ThrowTerminatingError(exception, "PackageInUse", ErrorCategory.ResourceBusy, dirPath);
    //     }
    // }
    //
    // private void ShowLockedFileInfo(string dirPath) {
    //     // FIXME: port to C#
    //     Cmdlet.InvokeCommand.InvokeScript($"ShowLockedFileList {LockedFilePrintColor}");
    //     _lockFileListShown = true;
    // }
}
