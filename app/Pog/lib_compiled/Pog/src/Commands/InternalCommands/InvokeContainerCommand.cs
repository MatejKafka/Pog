using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;

namespace Pog.Commands.InternalCommands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "Container")]
[OutputType(typeof(object))]
public class InvokeContainerCommand : PogCmdlet {
    [Parameter] public string? WorkingDirectory = null;
    [Parameter] public object? Context = null;

    [Parameter] public string[] Modules = [];
    [Parameter(Mandatory = true)] [AllowNull] public object[] ArgumentList = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var it = InvokePogCommand(new InvokeContainer(this) {
            WorkingDirectory = WorkingDirectory,
            Context = Context,

            Modules = Modules,
            Run = ps => ps.AddCommand("__main").AddParameters(ArgumentList),
        });

        foreach (var o in it) {
            WriteObject(o);
        }
    }
}
