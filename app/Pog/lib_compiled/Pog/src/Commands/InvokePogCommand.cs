using System.Collections;
using System.Management.Automation;
using System.Reflection;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

// TODO: allow wildcards in PackageName and Version arguments for commands where it makes sense
// TODO: to rollback on error or Ctrl-C, we will first need to know 1) which part of the pipeline failed, 2) which package failed
//  seems like there's no way to get that information using public APIs; investigate

// FIXME: when XmlDoc2CmdletDoc syntax is changed to something saner, format the list accordingly
/// <summary>Install a package from the package repository.</summary>
/// <para>Pog installs packages in four discrete steps:</para>
/// <para>1) The package manifest is downloaded from the package repository and placed into the package directory.
/// This step can be invoked separately using the `Import-Pog` cmdlet.</para>
/// <para>2) All package sources are downloaded and extracted to the `app` subdirectory inside the package (`Install-Pog` cmdlet).</para>
/// <para>3) The package setup script is executed. After this step, the package is usable by directly invoking the shortcuts
/// in the top-level package directory or the exported commands in the `.commands` subdirectory. (`Enable-Pog` cmdlet)</para>
/// <para>4) The shortcuts and commands are exported to the Start menu and to a directory on PATH, respectively. (`Export-Pog` cmdlet)</para>
///
/// <para>
/// The `Invoke-Pog` (typically invoked using the alias `pog`) installs a package from the package repository by running
/// all four installation stages in order, accepting the same arguments as <c>Import-Pog</c>.
/// This cmdlet is roughly equivalent to `Invoke-Pog @Args -PassThru | Install-Pog -PassThru | Enable-Pog -PassThru | Export-Pog`.
/// </para>
[PublicAPI]
[Alias("pog")]
// Cmdlet(...) params are manually copied from Import-Pog, there doesn't seem any way to dynamically copy this like with dynamicparam
[Cmdlet(VerbsLifecycle.Invoke, "Pog", DefaultParameterSetName = ImportPogCommand.DefaultPS)]
[OutputType(typeof(ImportedPackage))]
public sealed class InvokePogCommand : PackageCommandBase, IDynamicParameters {
    /// Import and install the package, do not enable and export.
    [Parameter] public SwitchParameter Install;

    /// Import, install and enable the package, do not export it.
    [Parameter] public SwitchParameter Enable;

    // TODO: add an `-Imported` parameter set to allow installing+enabling+exporting an imported package

    private static readonly CommandInfo ImportPogInfo = new CmdletInfo("Import-Pog", typeof(ImportPogCommand));
    private static readonly CommandInfo InstallPogInfo = new CmdletInfo("Install-Pog", typeof(InstallPogCommand));
    private static readonly CommandInfo EnablePogInfo = new CmdletInfo("Enable-Pog", typeof(EnablePogCommand));
    private static readonly CommandInfo ExportPogInfo = new CmdletInfo("Export-Pog", typeof(ExportPogCommand));

    private DynamicCommandParameters? _proxiedParams;
    private PowerShell _ps = PowerShell.Create();
    private SteppablePipeline? _pipeline;

    public object GetDynamicParameters() {
        if (_proxiedParams != null) {
            return _proxiedParams;
        }
        var builder = new DynamicCommandParameters.Builder(UnknownAttributeHandler: (paramName, attr) =>
                throw new InternalError($"Cannot copy parameter '{paramName}', attribute '{attr.GetType()}'."));
        return _proxiedParams = builder.CopyParameters(ImportPogInfo);
    }

    private Hashtable CopyCommonParameters() {
        // TODO: check if there isn't a built-in way to forward common parameters
        var commonParams = new Hashtable();
        foreach (var p in CommonParameters) {
            if (MyInvocation.BoundParameters.TryGetValue(p, out var value)) {
                commonParams[p] = value;
            }
        }
        return commonParams;
    }

    // TODO: rollback on error
    protected override void BeginProcessing() {
        base.BeginProcessing();

        var importParams = _proxiedParams!.Extract();

        // reuse PassThru parameter from Import-Pog for Enable-Pog
        var passThru = (importParams["PassThru"] as SwitchParameter?)?.IsPresent ?? false;
        importParams.Remove("PassThru");
        var commonParams = CopyCommonParameters();

        _ps.AddCommand(ImportPogInfo).AddParameters(commonParams).AddParameter("PassThru").AddParameters(importParams);
        if (Install) {
            _ps.AddCommand(InstallPogInfo).AddParameters(commonParams).AddParameter("PassThru", passThru);
        } else {
            _ps.AddCommand(InstallPogInfo).AddParameters(commonParams).AddParameter("PassThru");
            if (Enable) {
                _ps.AddCommand(EnablePogInfo).AddParameters(commonParams).AddParameter("PassThru", passThru);
            } else {
                _ps.AddCommand(EnablePogInfo).AddParameters(commonParams).AddParameter("PassThru");
                _ps.AddCommand(ExportPogInfo).AddParameters(commonParams).AddParameter("PassThru", passThru);
            }
        }

        _pipeline = (SteppablePipeline) PowerShellGetSteppablePipelineMethod.Invoke(_ps, []);
        _pipeline.Begin(this);
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        // https://github.com/orgs/PowerShell/discussions/21356
        var pipelineInput = CurrentPipelineObjectProperty.GetValue(this);
        _pipeline!.Process(pipelineInput);
    }

    protected override void EndProcessing() {
        base.EndProcessing();
        _pipeline!.End();
    }

    public override void Dispose() {
        _ps.Dispose();
        _pipeline?.Dispose();
    }

    private static readonly MethodInfo PowerShellGetSteppablePipelineMethod = typeof(PowerShell).GetMethod(
            "GetSteppablePipeline", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [], [])!;

    private static readonly PropertyInfo CurrentPipelineObjectProperty = typeof(PSCmdlet).GetProperty(
            "CurrentPipelineObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
}
