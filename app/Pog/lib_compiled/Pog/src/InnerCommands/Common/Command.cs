using System.Collections.Generic;
using Pog.Commands.Common;

namespace Pog.InnerCommands.Common;

/// <inheritdoc cref="BaseCommand"/>
internal abstract class VoidCommand(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract void Invoke();
}

/// <inheritdoc cref="BaseCommand"/>
internal abstract class ScalarCommand<T>(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract T Invoke();
}

/// <inheritdoc cref="BaseCommand"/>
internal abstract class EnumerableCommand<T>(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract IEnumerable<T> Invoke();
}
