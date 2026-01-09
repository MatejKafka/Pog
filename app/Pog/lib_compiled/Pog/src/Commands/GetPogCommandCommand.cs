using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.PSAttributes;

namespace Pog.Commands;

[PublicAPI]
public record ExportedCommand(string Path, ImportedPackage Package, string Target) {
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string PackageName => Package.PackageName;
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
        WriteObjectEnumerable(ProcessInner()
                .Select(cmd => new ExportedCommand(cmd.Path, new(cmd.SourcePackagePath!, false), cmd.Target!)));
    }

    private IEnumerable<GloballyExportedCommand> ProcessInner() {
        var packages = GetImportedPackage(Package, PackageName, false);
        if (packages == null) {
            return Name == null ? EnumerateAllCommands() : Name.SelectMany(EnumerateCommands);
        } else if (Name == null) {
            return packages.SelectMany(p => p.EnumerateExportedCommands())
                    .Select(localCmd => GloballyExportedCommand.FromLocal(localCmd.FullName))
                    .Where(cmd => cmd.Exists);
        } else {
            var packagesArr = packages.ToArray();
            return Name.SelectMany(EnumerateCommands).Where(cmd => packagesArr.Any(cmd.IsFromPackage));
        }
    }

    private IEnumerable<GloballyExportedCommand> EnumerateAllCommands() {
        return Directory.EnumerateFiles(InternalState.PathConfig.ExportedCommandDir)
                .Select(path => new GloballyExportedCommand(path));
    }

    private IEnumerable<GloballyExportedCommand> EnumerateCommands(string namePattern) {
        // filter out files with a dot before the extension (e.g. `arm-none-eabi-ld.bfd.exe`)
        return Directory.EnumerateFiles(InternalState.PathConfig.ExportedCommandDir, $"{namePattern}.*")
                .Where(GetCommandFilterFn(namePattern))
                .Select(path => new GloballyExportedCommand(path));
    }

    private Func<string, bool> GetCommandFilterFn(string namePattern) {
        if (WildcardPattern.ContainsWildcardCharacters(namePattern)) {
            var pattern = new WildcardPattern(namePattern, WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
            return path => pattern.IsMatch(Path.GetFileNameWithoutExtension(path));
        } else {
            return path =>
                    string.Equals(Path.GetFileNameWithoutExtension(path), namePattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
