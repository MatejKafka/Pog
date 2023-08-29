using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Pog.Commands.Common;

public class PogCmdlet : PSCmdlet, IDisposable {
    private HashSet<BaseCommand>? _currentlyExecutingCommands;

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

    protected override void StopProcessing() {
        base.StopProcessing();

        if (_currentlyExecutingCommands == null) {
            return;
        }

        foreach (var cmd in _currentlyExecutingCommands) {
            cmd.StopProcessing();
        }

        _currentlyExecutingCommands = null;
    }

    public void Dispose() {
        if (_currentlyExecutingCommands == null) {
            return;
        }
        foreach (var cmd in _currentlyExecutingCommands) {
            if (cmd is IDisposable disposable) {
                disposable.Dispose();
            }
        }
    }

    protected void WriteInformation(string message) {
        WriteInformation(message, null);
    }

    protected void ThrowTerminatingError(Exception exception, string errorId, ErrorCategory errorCategory,
            object? targetObject) {
        ThrowTerminatingError(new ErrorRecord(exception, errorId, errorCategory, targetObject));
    }

    protected void ThrowTerminatingArgumentError(object? argumentValue, string errorId, string message) {
        ThrowTerminatingError(new ArgumentException(message), errorId, ErrorCategory.InvalidArgument, argumentValue);
    }

    private readonly struct CommandStopContext : IDisposable {
        private readonly PogCmdlet _cmdlet;
        private readonly BaseCommand _command;

        public CommandStopContext(PogCmdlet cmdlet, BaseCommand cmd) {
            _cmdlet = cmdlet;
            _command = cmd;
            _cmdlet._currentlyExecutingCommands ??= new();
            _cmdlet._currentlyExecutingCommands.Add(_command);
        }

        public void Dispose() {
            _cmdlet._currentlyExecutingCommands?.Remove(_command);
        }
    }
}
