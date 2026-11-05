using System.Collections;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;

namespace Pog.Commands.UtilCommands;

/// <summary>Runs the provided scriptblock in a fresh runspace.</summary>
/// <para>Creates a fresh PowerShell runspace where only basic built-in modules are available
/// and invokes the provided scriptblock inside it.</para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Invoke, "Container")]
[OutputType(typeof(object))]
public class InvokeContainerCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)] public ScriptBlock Run = null!;
    [Parameter] public string[]? Module;
    [Parameter] public Hashtable? Variables;
    [Parameter] public string? Location;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var it = InvokePogCommand(new InvokeContainer(this) {
            Run = new PSCommand().AddScript(Run.ToString()),
            Modules = Module,
            Variables = Variables?.Cast<DictionaryEntry>()
                    .Select(e => new SessionStateVariableEntry(e.Key.ToString(), e.Value, ""))
                    .ToArray(),
            WorkingDirectory = Location,
        });

        foreach (var o in it) {
            WriteObject(o);
        }
    }
}
