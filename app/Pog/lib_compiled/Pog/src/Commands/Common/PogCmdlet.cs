﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using Pog.InnerCommands.Common;

namespace Pog.Commands.Common;

/// <summary>
/// A custom PSCmdlet subclass which adds useful internal methods used by most Pog cmdlets.
/// </summary>
public class PogCmdlet : PSCmdlet, IDisposable {
    private HashSet<BaseCommand>? _currentlyExecutingCommands;

    // newer versions of PowerShell allow progress updates at most every 200 ms and debounce more frequent updates
    internal static TimeSpan DefaultProgressInterval = TimeSpan.FromMilliseconds(200);

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
            // by re-yielding the enumerable, we dispose the stop context only after the whole enumerable
            //  is iterated over (= the command finished)
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

    protected internal void ThrowTerminatingError(Exception exception, string errorId, ErrorCategory errorCategory,
            object? targetObject) {
        ThrowTerminatingError(new ErrorRecord(exception, errorId, errorCategory, targetObject));
    }

    protected internal void ThrowArgumentError(object? argumentValue, string errorId, string message) {
        ThrowTerminatingError(new ArgumentException(message), errorId, ErrorCategory.InvalidArgument, argumentValue);
    }

    // necessary, since modern APIs such as HttpClient do not support SecureString, as it's deprecated
    // PowerShell does it the same way
    protected static string UnprotectSecureString(SecureString secureString) {
        var valuePtr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
        try {
            return Marshal.PtrToStringUni(valuePtr) ?? "";
        } finally {
            Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
        }
    }

    protected override void StopProcessing() {
        base.StopProcessing();
        // internal cmdlets use CancellationToken instead of StopProcessing
        _stopping?.Cancel();
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
