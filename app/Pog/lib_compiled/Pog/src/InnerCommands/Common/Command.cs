using System.Collections.Generic;

namespace Pog.InnerCommands.Common;

/// <inheritdoc cref="BaseCommand"/>
public abstract class VoidCommand(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract void Invoke();
}

/// <inheritdoc cref="BaseCommand"/>
public abstract class ScalarCommand<T>(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract T Invoke();
}

/// <inheritdoc cref="BaseCommand"/>
public abstract class EnumerableCommand<T>(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract IEnumerable<T> Invoke();
}
