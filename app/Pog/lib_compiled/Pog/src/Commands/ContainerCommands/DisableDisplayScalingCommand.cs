using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.Native;
using Pog.PSAttributes;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Disable, "DisplayScaling")]
public class DisableDisplayScalingCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)]
    [ResolvePath("Cannot disable system display scaling, exe path")]
    public UserPath ExePath = new();

    protected override void BeginProcessing() {
        base.BeginProcessing();

        // display scaling can be disabled using the application manifest of the executable
        var manifest = new PeApplicationManifest(ExePath.Resolved);
        if (manifest.EnsureDpiAware()) {
            manifest.Save();
            WriteInformation($"Disabled system display scaling for '{ExePath.Raw}'.");
        } else {
            WriteVerbose($"System display scaling already disabled for '{ExePath.Raw}'.");
        }
    }
}
