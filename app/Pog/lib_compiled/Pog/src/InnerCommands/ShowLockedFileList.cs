using System;
using System.Linq;
using System.Management.Automation;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

/// Ensures that the existing ./app directory can be removed (no locked files from other processes).
///
/// Prints all processes that hold a lock over a file in an existing .\app directory, then waits until user closes them,
/// in a loop, until there are no locked files in the directory.
internal class ShowLockedFileList(PogCmdlet cmdlet) : VoidCommand(cmdlet) {
    [Parameter] public required string Path;
    [Parameter] public required string MessagePrefix;
    [Parameter] public bool NoList = false;
    [Parameter] public bool Wait = false;

    public override void Invoke() {
        if (!NoList) {
            PrintList();
        }

        if (!Wait) {
            return;
        }

        try {
            // TODO: automatically continue when the listed processes are closed or close the files (might be hard to detect)
            Host.UI.Write(ConsoleColor.Red, ConsoleColor.Black,
                    "Please close the applications listed above, then press Enter to continue...: ");
            Host.UI.ReadLine();
        } catch (PSInvalidOperationException e) {
            // host is not interactive, just throw an exception
            // TODO: shouldn't this be a non-terminating error?
            throw new PSInvalidOperationException(MessagePrefix + " because processes listed in the output above " +
                                                  "are working with files inside the package.", e);
        }
    }

    private void PrintList() {
        // no need to check for admin rights before running OpenedFilesView, it will just report an empty list
        // TODO: probably would be better to detect the situation and suggest to the user that they can re-run
        //  the installation as admin and they'll get more information

        // find out which files are locked, report them to the user
        var procs = InvokePogCommand(new GetLockedFile(Cmdlet) {
            Path = Path,
        })?.ToArray();

        if (procs == null || procs.Length == 0) {
            // some process is locking files in the directory, but we don't which one; this may happen if OFV is not installed,
            // or the offending process exited between the C# check and calling OFV
            Write(MessagePrefix + " as there are file(s) opened by an unknown running process.\nIs it possible that some" +
                  " program from the package is running or that another running program is using a file from this package?");
            if (procs == null) {
                Write("\nTo have Pog automatically identify the processes locking the files," +
                      " install the 'OpenedFilesView' package.");
            }
            return;
        }

        // TODO: print more user-friendly app names
        // TODO: explain to the user what he should do to resolve the issue

        // long error messages are hard to read, because all newlines are removed in some $ErrorView modes;
        //  instead write out the files and then show a short error message
        Write($"\n{MessagePrefix} because the following processes are working with files" +
              $" inside the installation directory:");
        foreach (var p in procs) {
            Write($"  Files locked by '{p.ProcessInfoStr}':");
            foreach (var f in p.LockedFiles.Take(5)) {
                Write($"    {f}");
            }
            if (p.LockedFiles.Length > 5) {
                Write($"   ... ({p.LockedFiles.Length - 5} more)");
            }
        }
        Write("");
    }

    private void Write(string msg) => WriteHost(msg, foregroundColor: ConsoleColor.Red);
}
