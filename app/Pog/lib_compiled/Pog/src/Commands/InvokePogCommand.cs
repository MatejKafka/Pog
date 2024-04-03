using System.Collections;
using System.Management.Automation;
using System.Reflection;
using JetBrains.Annotations;
using Pog.Commands.Common;

namespace Pog.Commands;

// TODO: allow wildcards in PackageName and Version arguments for commands where it makes sense

/// <summary>
/// <para type="synopsis">Import, install, enable and export a package.</para>
/// <para type="description">
/// Runs all four installation stages in order. This cmdlet accepts the same arguments as <c>Import-Pog</c>.
/// </para>
/// </summary>
[PublicAPI]
[Alias("pog")]
// Cmdlet(...) params are manually copied from Import-Pog, there doesn't seem any way to dynamically copy this like with dynamicparam
[Cmdlet(VerbsLifecycle.Invoke, "Pog", DefaultParameterSetName = ImportPogCommand.DefaultPS)]
[OutputType(typeof(ImportedPackage))]
public sealed class InvokePogCommand : PackageCommandBase, IDynamicParameters {
    /// <summary><para type="description">
    /// Import and install the package, do not enable and export.
    /// </para></summary>
    [Parameter] public SwitchParameter Install;

    /// <summary><para type="description">
    /// Import, install and enable the package, do not export it.
    /// </para></summary>
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
        var logArgs = new Hashtable();
        foreach (var p in CommonParameters) {
            if (MyInvocation.BoundParameters.TryGetValue(p, out var value)) {
                logArgs[p] = value;
            }
        }
        return logArgs;
    }

    // TODO: rollback on error
    protected override void BeginProcessing() {
        base.BeginProcessing();

        var importParams = _proxiedParams!.Extract();

        // reuse PassThru parameter from Import-Pog for Enable-Pog
        var passThru = (importParams["PassThru"] as SwitchParameter?)?.IsPresent ?? false;
        importParams.Remove("PassThru");
        var logArgs = CopyCommonParameters();

        _ps.AddCommand(ImportPogInfo).AddParameters(logArgs).AddParameter("PassThru").AddParameters(importParams);
        if (Install) {
            _ps.AddCommand(InstallPogInfo).AddParameters(logArgs).AddParameter("PassThru", passThru);
        } else {
            _ps.AddCommand(InstallPogInfo).AddParameters(logArgs).AddParameter("PassThru");
            if (Enable) {
                _ps.AddCommand(EnablePogInfo).AddParameters(logArgs).AddParameter("PassThru", passThru);
            } else {
                _ps.AddCommand(EnablePogInfo).AddParameters(logArgs).AddParameter("PassThru");
                _ps.AddCommand(ExportPogInfo).AddParameters(logArgs).AddParameter("PassThru", passThru);
            }
        }

        _pipeline = (SteppablePipeline) PowerShellGetSteppablePipelineMethod.Invoke(_ps, []);
        _pipeline.Begin(this);
    }

    protected override void ProcessRecord() {
        base.ProcessRecord();

        // https://github.com/orgs/PowerShell/discussions/21356
        var value = CurrentPipelineObjectProperty.GetValue(this);
        _pipeline!.Process(value);
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
