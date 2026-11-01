using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.PSAttributes;
using Pog.Utils;

namespace Pog.Commands;

[PublicAPI]
public record ExportedCommand(bool Local, string Path, ImportedPackage Package, string Target) {
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string PackageName => Package.PackageName;
    public override string ToString() => Path;
}

/// <summary>Lists commands exported to PATH by installed packages.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "PogCommand", DefaultParameterSetName = DefaultPS)]
[OutputType(typeof(ExportedCommand))]
public class GetPogCommandCommand : PackageCommandBase {
    private const string PackagePS = "Package";
    private const string PackageNamePS = "PackageName";
    private const string DefaultPS = PackageNamePS;

    /// Command names to search for. If not passed, all exported commands are returned.
    [Parameter(Position = 0, ValueFromPipeline = true)]
    [SupportsWildcards]
    [ArgumentCompleter(typeof(ExportedCommandNameCompleter))]
    public string[]? Name = null;

    /// Names of packages to list the exported commands for.
    [Parameter(Position = 1, ParameterSetName = PackageNamePS)]
    [SupportsWildcards]
    [ArgumentCompleter(typeof(ImportedPackageNameCompleter))]
    public string[]? PackageName;

    /// Packages to list the exported commands for.
    [Parameter(Position = 1, ParameterSetName = PackagePS, ValueFromPipeline = true)]
    public ImportedPackage[]? Package = null;

    protected override void ProcessRecord() {
        WriteObjectEnumerable(ProcessInner());
    }

    private IEnumerable<ExportedCommand> ProcessInner() {
        var packages = GetImportedPackage(Package, PackageName, false);
        if (packages == null) {
            return (Name == null ? EnumerateAllCommands() : Name.SelectMany(EnumerateCommands))
                    .Select(cmd => new ExportedCommand(false, cmd.Path, new(cmd.SourcePackagePath!, false), cmd.Target!));
        }

        if (Name == null) {
            return packages.SelectMany(p => p.EnumerateExportedCommands()
                    .Select(cmd => new ExportedCommand(true, cmd.FullName, p, cmd.FullName)));
        } else {
            var matchers = Name.Select(GetCommandFilterFn).ToArray();
            return packages.SelectMany(p => p.EnumerateExportedCommands()
                    .Where(cmd => matchers.Any(m => m(cmd.FullName)))
                    .Select(cmd => new ExportedCommand(true, cmd.FullName, p, cmd.FullName)));
        }
    }

    private Func<string, bool> GetCommandFilterFn(string namePattern) {
        var pattern = new WildcardPattern(namePattern, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
        return path => pattern.IsMatch(Path.GetFileNameWithoutExtension(path));
    }

    private IEnumerable<GloballyExportedCommand> EnumerateAllCommands() {
        return Directory.EnumerateFiles(InternalState.PathConfig.ExportedCommandDir)
                .Select(path => new GloballyExportedCommand(path));
    }

    private IEnumerable<GloballyExportedCommand> EnumerateCommands(string namePattern) {
        // filter out files with a dot before the extension (e.g. `arm-none-eabi-ld.bfd.exe`)
        return Directory.EnumerateFiles(InternalState.PathConfig.ExportedCommandDir, $"{namePattern}.*")
                .WhereFsWildcard(namePattern, Path.GetFileNameWithoutExtension)
                .Select(path => new GloballyExportedCommand(path));
    }
}
