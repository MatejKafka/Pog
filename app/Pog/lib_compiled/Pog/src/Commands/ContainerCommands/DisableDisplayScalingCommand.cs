using System.IO;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.Native;

namespace Pog.Commands.ContainerCommands;

[PublicAPI]
[Cmdlet(VerbsLifecycle.Disable, "DisplayScaling")]
public class DisableDisplayScalingCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0)]
    public string ExePath = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();

        var resolvedExePath = GetUnresolvedProviderPathFromPSPath(ExePath);
        if (!File.Exists(resolvedExePath)) {
            ThrowTerminatingArgumentError(ExePath, "ExeNotFound",
                    $"Cannot disable system display scaling, target does not exist: {ExePath}");
        }

        // display scaling can be disabled using the application manifest of the executable
        var manifest = new PeApplicationManifest(resolvedExePath);
        if (manifest.EnsureDpiAware()) {
            manifest.Save();
            WriteInformation($"Disabled system display scaling for '{ExePath}'.");
        } else {
            WriteVerbose($"System display scaling already disabled for '{ExePath}'.");
        }
    }
}
