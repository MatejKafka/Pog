using System.Management.Automation;

namespace Pog.Commands.Internal;

/// <summary>
/// This class (and subclasses) exist because some internal cmdlets are invoked both from PowerShell,
/// and from other binary cmdlets. Since invoking PSCmdlet from another PSCmdlet is somewhat hard,
/// this class allows supplying a custom PSCmdlet instance. Therefore, instead of a binary cmdlet A
/// invoking a binary cmdlet B, the cmdlet code is executed in the context of the cmdlet A.
/// </summary>
/// <remarks>
/// Note that if a subclass of this invokes `ThrowTerminatingError`, it terminates the calling cmdlet immediately.
/// </remarks>
public abstract class Command {
    protected readonly PSCmdlet Cmdlet;

    protected Command(PSCmdlet cmdlet) {
        Cmdlet = cmdlet;
    }

    protected void WriteDebug(string text) => Cmdlet.WriteDebug(text);
    protected void WriteVerbose(string text) => Cmdlet.WriteVerbose(text);
    protected void WriteWarning(string text) => Cmdlet.WriteWarning(text);
    protected void WriteError(ErrorRecord errorRecord) => Cmdlet.WriteError(errorRecord);
    protected void WriteProgress(ProgressRecord progressRecord) => Cmdlet.WriteProgress(progressRecord);

    protected void WriteInformation(object messageData, string[]? tags = null) {
        Cmdlet.WriteInformation(messageData, tags);
    }

    protected void ThrowTerminatingError(ErrorRecord errorRecord) => Cmdlet.ThrowTerminatingError(errorRecord);
    protected string GetUnresolvedProviderPathFromPSPath(string path) => Cmdlet.GetUnresolvedProviderPathFromPSPath(path);

    public virtual void StopProcessing() {}
}
