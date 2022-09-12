using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using JetBrains.Annotations;

// This module implements a set of argument completers that are used in the public Pog cmdlets.
// Originally, `ValidateSet` was used, which automatically provides autocomplete, but there were a few issues:
//  1) passing a class to ValidateSet is only supported in pwsh.exe, not in powershell.exe
//  2) ValidateSet is inefficient, because it always reads the full set, repeatedly. User typically only invokes autocomplete
//     after typing a few characters, so it should be faster.
//  3) Typically, we do more processing on the argument inside the function (e.g. name resolution, package retrieval,...),
//     and we can integrate validation there quite easily, and it's again much more efficient to check that the argument
//     value is valid than to retrieve the set of all possible argument values.
namespace Pog.PSAttributes;

public abstract class DirectoryListingArgumentCompleter : IArgumentCompleter {
    protected abstract IEnumerable<string> GetMatchingItems(string searchPattern);

    public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete,
            CommandAst commandAst,
            IDictionary fakeBoundParameters) {
        if (0 <= wordToComplete.IndexOfAny(Path.GetInvalidFileNameChars())) {
            return Enumerable.Empty<CompletionResult>();
        }
        return GetMatchingItems($"{wordToComplete}*").Select(n => new CompletionResult(n));
    }
}

[PublicAPI]
public sealed class ImportedPackageNameCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern) {
        return InternalState.PackageRootManager.EnumeratePackageNames(searchPattern);
    }
}

[PublicAPI]
public sealed class RepositoryPackageNameCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern) {
        return InternalState.Repository.EnumeratePackageNames(searchPattern);
    }
}

[PublicAPI]
public sealed class RepositoryPackageGeneratorNameCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern) {
        return InternalState.GeneratorRepository.EnumerateGeneratorNames(searchPattern);
    }
}

[PublicAPI]
public sealed class ValidPackageRootPathCompleter : IArgumentCompleter {
    public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete,
            CommandAst commandAst, IDictionary fakeBoundParameters) {
        return InternalState.PackageRootManager.PackageRoots.ValidPackageRoots.Select(r => new CompletionResult(r));
    }
}

/**
 * Argument completer for versions of repository packages.
 * It expects that a parameter called `PackageName` exists (this has to be hardcoded, as there's no supported way to pass
 * arguments to the completer type when used in the [ArgumentCompleter] attribute).
 */
[PublicAPI]
public sealed class RepositoryPackageVersionCompleter : IArgumentCompleter {
    public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete,
            CommandAst commandAst, IDictionary fakeBoundParameters) {
        if (!fakeBoundParameters.Contains("PackageName")) {
            return Enumerable.Empty<CompletionResult>();
        }

        var packageName = fakeBoundParameters["PackageName"];
        if (packageName == null) {
            return Enumerable.Empty<CompletionResult>();
        }
        if (packageName is Array {Length: 1} arr) {
            packageName = arr.GetValue(0);
        }

        // package name like `7.1` will be passed as a double, and `7` as an int
        // this filters out unexpected types, and also arrays, which we cannot sensibly auto-complete
        if (packageName is not string && packageName is not int && packageName is not double) {
            return Enumerable.Empty<CompletionResult>();
        }

        var package = InternalState.Repository.GetPackage(packageName.ToString(), false, false);
        if (!package.Exists) {
            return Enumerable.Empty<CompletionResult>(); // no such package
        }
        return package.EnumerateVersions($"{wordToComplete}*")
                .Select(v => new CompletionResult(v.ToString()));
    }
}