using System.Collections.Generic;

namespace Pog.InnerCommands.Common;

public abstract class VoidCommand : BaseCommand {
    protected VoidCommand(PogCmdlet cmdlet) : base(cmdlet) {}
    public abstract void Invoke();
}

public abstract class ScalarCommand<T> : BaseCommand {
    protected ScalarCommand(PogCmdlet cmdlet) : base(cmdlet) {}
    public abstract T Invoke();
}

public abstract class EnumerableCommand<T> : BaseCommand {
    protected EnumerableCommand(PogCmdlet cmdlet) : base(cmdlet) {}
    public abstract IEnumerable<T> Invoke();
}
