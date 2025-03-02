using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Threading;

namespace Pog.InnerCommands.Common;

/// <summary>
/// This class (and subclasses) exist because some internal cmdlets are invoked both from PowerShell,
/// and from other binary cmdlets. Since invoking PSCmdlet from another PSCmdlet is somewhat hard,
/// this class allows supplying a custom PSCmdlet instance. Therefore, instead of a binary cmdlet A
/// invoking a binary cmdlet B, the cmdlet code is executed in the context of the cmdlet A.
/// </summary>
/// <remarks>
/// Note that if a subclass of this invokes `ThrowTerminatingError`, it terminates the calling cmdlet immediately.
/// </remarks>
public abstract class BaseCommand(PogCmdlet cmdlet) {
    protected readonly PogCmdlet Cmdlet = cmdlet;
    protected CancellationToken CancellationToken => Cmdlet.CancellationToken;

    // forward calls to the cmdlet
    protected void InvokePogCommand(VoidCommand cmd) => Cmdlet.InvokePogCommand(cmd);
    protected T InvokePogCommand<T>(ScalarCommand<T> cmd) => Cmdlet.InvokePogCommand(cmd);
    protected IEnumerable<T> InvokePogCommand<T>(EnumerableCommand<T> cmd) => Cmdlet.InvokePogCommand(cmd);

    public virtual void StopProcessing() {}

    protected PSHost Host => Cmdlet.Host;

    protected void WriteDebug(string text) => Cmdlet.WriteDebug(text);
    protected void WriteVerbose(string text) => Cmdlet.WriteVerbose(text);
    protected void WriteWarning(string text) => Cmdlet.WriteWarning(text);
    protected void WriteError(ErrorRecord errorRecord) => Cmdlet.WriteError(errorRecord);
    protected void WriteProgress(ProgressRecord progressRecord) => Cmdlet.WriteProgress(progressRecord);

    protected void WriteHost(string message, bool noNewline = false,
            ConsoleColor? foregroundColor = null, ConsoleColor? backgroundColor = null) {
        Cmdlet.WriteHost(message, noNewline, foregroundColor, backgroundColor);
    }

    protected void WriteInformation(object messageData, string[]? tags = null) {
        Cmdlet.WriteInformation(messageData, tags);
    }

    protected void ThrowArgumentError(object? argumentValue, string errorId, string message) {
        Cmdlet.ThrowArgumentError(argumentValue, errorId, message);
    }

    protected void ThrowTerminatingError(ErrorRecord errorRecord) => Cmdlet.ThrowTerminatingError(errorRecord);

    protected void ThrowTerminatingError(Exception exception, string errorId, ErrorCategory errorCategory,
            object? targetObject) {
        Cmdlet.ThrowTerminatingError(exception, errorId, errorCategory, targetObject);
    }

    protected string GetUnresolvedProviderPathFromPSPath(string path) => Cmdlet.GetUnresolvedProviderPathFromPSPath(path);

    protected Collection<string> GetResolvedProviderPathFromPSPath(string path, out ProviderInfo provider) {
        return Cmdlet.GetResolvedProviderPathFromPSPath(path, out provider);
    }
}
