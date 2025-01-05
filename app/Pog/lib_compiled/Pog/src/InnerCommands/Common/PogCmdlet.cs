using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace Pog.InnerCommands.Common;

/// <summary>
/// A custom PSCmdlet subclass which adds useful internal methods used by most Pog cmdlets.
/// </summary>
public class PogCmdlet : PSCmdlet, IDisposable {
    private HashSet<BaseCommand>? _currentlyExecutingCommands;

    // lazily-provided cancellation token
    private CancellationTokenSource? _stopping;
    protected internal CancellationToken CancellationToken => (_stopping ??= new CancellationTokenSource()).Token;

    internal void InvokePogCommand(VoidCommand cmd) {
        using (new CommandStopContext(this, cmd)) {
            cmd.Invoke();
        }
    }

    internal T InvokePogCommand<T>(ScalarCommand<T> cmd) {
        using (new CommandStopContext(this, cmd)) {
            return cmd.Invoke();
        }
    }

    internal IEnumerable<T> InvokePogCommand<T>(EnumerableCommand<T> cmd) {
        using (new CommandStopContext(this, cmd)) {
            foreach (var obj in cmd.Invoke()) {
                yield return obj;
            }
        }
    }

    protected internal void WriteHost(string message, bool noNewline = false,
            ConsoleColor? foregroundColor = null, ConsoleColor? backgroundColor = null) {
        // information messages tagged PSHOST are treated specially by the host
        // would be nice if this was documented somewhere, eh?
        WriteInformation(new HostInformationMessage {
            Message = message, NoNewLine = noNewline, ForegroundColor = foregroundColor, BackgroundColor = backgroundColor,
        }, ["PSHOST"]);
    }

    public void WriteInformation(string message) {
        WriteInformation(message, null);
    }

    protected void WriteObjectEnumerable(IEnumerable enumerable) {
        foreach (var o in enumerable) {
            WriteObject(o);
        }
    }

    protected void WriteObjectEnumerable<T>(IEnumerable<T> enumerable) {
        foreach (var o in enumerable) {
            WriteObject(o);
        }
    }

    protected void WriteError(Exception exception, string errorId, ErrorCategory errorCategory,
            object? targetObject) {
        WriteError(new ErrorRecord(exception, errorId, errorCategory, targetObject));
    }

    protected void ThrowTerminatingError(Exception exception, string errorId, ErrorCategory errorCategory,
            object? targetObject) {
        ThrowTerminatingError(new ErrorRecord(exception, errorId, errorCategory, targetObject));
    }

    protected void ThrowArgumentError(object? argumentValue, string errorId, string message) {
        ThrowTerminatingError(new ArgumentException(message), errorId, ErrorCategory.InvalidArgument, argumentValue);
    }

    protected override void StopProcessing() {
        base.StopProcessing();

        // stop any async calls
        _stopping?.Cancel();

        // forward `.StopProcessing()` call to all invoked commands
        if (_currentlyExecutingCommands != null) {
            foreach (var cmd in _currentlyExecutingCommands) {
                cmd.StopProcessing();
            }
        }
    }

    public virtual void Dispose() {
        // unless something leaked, all invoked commands should be disposed by now
        Debug.Assert(_currentlyExecutingCommands is not {Count: not 0});

        _stopping?.Dispose();
    }

    private readonly struct CommandStopContext : IDisposable {
        private readonly PogCmdlet _cmdlet;
        private readonly BaseCommand _command;

        public CommandStopContext(PogCmdlet cmdlet, BaseCommand cmd) {
            _cmdlet = cmdlet;
            _command = cmd;
            // register the command so that we can route StopProcessing calls to it
            _cmdlet._currentlyExecutingCommands ??= [];
            _cmdlet._currentlyExecutingCommands.Add(_command);
        }

        public void Dispose() {
            _cmdlet._currentlyExecutingCommands?.Remove(_command);
            // dispose the command, it finished
            if (_command is IDisposable disposable) {
                disposable.Dispose();
            }
        }
    }
}
