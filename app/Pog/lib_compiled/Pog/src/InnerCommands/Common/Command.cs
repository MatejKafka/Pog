using System.Collections.Generic;

namespace Pog.InnerCommands.Common;

public abstract class VoidCommand(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract void Invoke();
}

public abstract class ScalarCommand<T>(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract T Invoke();
}

public abstract class EnumerableCommand<T>(PogCmdlet cmdlet) : BaseCommand(cmdlet) {
    public abstract IEnumerable<T> Invoke();
}
