using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using Pog.Commands.Common;

namespace Pog.Utils;

internal static class Log {
    public static void Warn(string message) {
        // first try to go through PogCmdlet to give the cmdlet control, fall back to using ICommandRuntime2 directly
        var (cmdlet, cmdRuntime) = GetCurrentCmd();
        cmdlet?.WriteWarning(message);
        cmdRuntime?.WriteWarning(message);
    }

    public static void Verbose(string message) {
        var (cmdlet, cmdRuntime) = GetCurrentCmd();
        cmdlet?.WriteVerbose(message);
        cmdRuntime?.WriteVerbose(message);
    }

    public static void Debug(string message) {
        var (cmdlet, cmdRuntime) = GetCurrentCmd();
        cmdlet?.WriteDebug(message);
        cmdRuntime?.WriteDebug(message);
    }

    public static void Info(object messageData, string[]? tags = null) {
        var (cmdlet, cmdRuntime) = GetCurrentCmd();
        cmdlet?.WriteInformation(messageData, tags);
        if (cmdRuntime != null) {
            // for cmdlets, `source` is typically the cmdlet path/name, but null is also a valid value
            var info = new InformationRecord(messageData, null);
            if (tags != null) info.Tags.AddRange(tags);
            cmdRuntime.WriteInformation(info);
        }
    }

    public static void Host(string message, bool noNewline = false,
            ConsoleColor? foregroundColor = null, ConsoleColor? backgroundColor = null) {
        // information messages tagged PSHOST are treated specially by the host
        // would be nice if this was documented somewhere, eh?
        Info(new HostInformationMessage {
            Message = message, NoNewLine = noNewline, ForegroundColor = foregroundColor, BackgroundColor = backgroundColor,
        }, ["PSHOST"]);
    }

    // uses a bunch of reflection to pull out the executing command and its runtime from the current runspace; I don't think
    //  most of these properties are likely to change, but even if they do, the result will just be a silent loss of some logs
    private static (PogCmdlet?, ICommandRuntime2?) GetCurrentCmd() {
        // this thread-local property always reflects the runspace of the currently executing cmdlet
        // note that it won't work in async context
        // TODO: add a synchronizationcontext for a cmdlet that pumps continuations
        var runspace = Runspace.DefaultRunspace;
        if (runspace == null) {
            throw new InvalidOperationException(
                    "Cannot log a message, there is no associated PowerShell runspace for the current thread. " +
                    "Are you logging from an async method without a SynchronizationContext?");
        }

        var ec = ExecutionContextProp?.GetValue(runspace);
        if (ec == null) return (null, null);

        var cmdProcessor = CurrentCommandProcessorProp?.GetValue(ec);
        if (cmdProcessor == null) return (null, null);

        if (CommandProp?.GetValue(cmdProcessor) is PogCmdlet cmd) {
            return (cmd, null); // if PogCmdlet is accessible, do not look for the command runtime
        }

        // `.CommandRuntime` is set for both cmdlets and non-advanced functions/scripts, unlike `.Cmdlet`
        return (null, CommandRuntimeProp?.GetValue(cmdProcessor) as ICommandRuntime2);
    }

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo? ExecutionContextProp =
            typeof(Runspace).GetProperty("ExecutionContext", Flags);
    private static readonly PropertyInfo? CurrentCommandProcessorProp =
            ExecutionContextProp?.PropertyType.GetProperty("CurrentCommandProcessor", Flags);
    private static readonly PropertyInfo? CommandRuntimeProp =
            CurrentCommandProcessorProp?.PropertyType.GetProperty("CommandRuntime", Flags);
    private static readonly PropertyInfo? CommandProp =
            CurrentCommandProcessorProp?.PropertyType.GetProperty("Command", Flags);
}
