using System.Management.Automation;
using JetBrains.Annotations;
using Pog.InnerCommands.Common;
using Pog.Native;
using Pog.PSAttributes;

namespace Pog.Commands.ContainerCommands;

/// <summary>Disables system display scaling for the specified executable.</summary>
/// <para>
/// By default, Windows apply bitmap scaling to applications that do not explicitly declare that they perform scaling
/// internally. For fractional scaling ratios, this results in blurry text in older apps. This cmdlet changes the application
/// manifest to disable any system display scaling.
/// </para>
[PublicAPI]
[Cmdlet(VerbsLifecycle.Disable, "DisplayScaling")]
public sealed class DisableDisplayScalingCommand : PogCmdlet {
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
