using System.Collections;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.Commands.Internal;

namespace Pog.Commands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "Container")]
[OutputType(typeof(object))]
public class InvokeContainerCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public Container.ContainerType ContainerType;
    [Parameter(Mandatory = true, Position = 1)] public Package Package = null!;
    [Parameter] public Hashtable? InternalArguments;
    [Parameter] public Hashtable? PackageArguments;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var it = InvokePogCommand(new InvokeContainer(this) {
            ContainerType = ContainerType,
            Package = Package,
            InternalArguments = InternalArguments,
            PackageArguments = PackageArguments,
        });

        foreach (var o in it) {
            WriteObject(o);
        }
    }
}
