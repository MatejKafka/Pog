using Pog.Commands.Common;
using Pog.InnerCommands.Common;

namespace Pog.InnerCommands;

internal class DisablePog(PogCmdlet cmdlet) : ImportedPackageInnerCommandBase(cmdlet) {
    public override bool Invoke() {
        if (Package.Manifest.Disable == null) {
            WriteVerbose($"Package '{Package.PackageName}' does not have a Disable block.");
            return false;
        }

        WriteInformation($"Disabling '{Package.GetDescriptionString()}'...");

        var it = InvokePogCommand(new InvokeContainer(Cmdlet) {
            WorkingDirectory = Package.Path,
            Modules = [$@"{InternalState.PathConfig.ContainerDir}\Env_Disable.psm1"],
            // $this is used inside the manifest to refer to fields of the manifest itself to emulate class-like behavior
            Variables = [new("this", Package.Manifest.Raw, "Loaded manifest of the processed package")],
            Run = ps => ps.AddCommand("__main").AddArgument(Package.Manifest),
        });

        // Disable scriptblock should not output anything, show a warning
        foreach (var o in it) {
            WriteWarning($"DISABLE: {o}");
        }
        return true;
    }
}
