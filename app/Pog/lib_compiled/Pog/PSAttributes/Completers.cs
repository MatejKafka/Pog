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

public abstract class QuotingArgumentCompleter : IArgumentCompleter {
    protected abstract IEnumerable<string> GetCompletions(string wordToComplete, IDictionary fakeBoundParameters);

    private enum ParameterQuoting { None, Single, Double }

    private static bool ShouldBeQuoted(string str) {
        return str.Any(c => !char.IsLetterOrDigit(c) && !@".-_/\:@!".Contains(c));
    }

    // sigh, why do we have do everything ourselves? (https://github.com/PowerShell/PowerShell/issues/11330)
    private static string QuoteString(string str, ParameterQuoting originalQuoting) {
        if (originalQuoting == ParameterQuoting.None) {
            originalQuoting = ShouldBeQuoted(str) ? ParameterQuoting.Single : ParameterQuoting.None;
        }
        return originalQuoting switch {
            ParameterQuoting.None => str,
            ParameterQuoting.Single => $"'{str.Replace("'", "''")}'",
            // uh, I sure hope this is correct, but I wouldn't bet on it
            ParameterQuoting.Double => $"\"{str.Replace("`", "``").Replace("\"", "`\"")}\"",
            _ => throw new ArgumentOutOfRangeException(nameof(originalQuoting), originalQuoting, null)
        };
    }

    // if the completion result is wrapped in quotes, remove them
    private static (ParameterQuoting, string) StripQuotes(string str) {
        if (str.Length < 2) {
            return (ParameterQuoting.None, str);
        }
        var firstC = str[0];
        var lastC = str[str.Length - 1];
        if (firstC == '"' && lastC == '"') {
            return (ParameterQuoting.Double, str.Substring(1, str.Length - 2));
        } else if (firstC == '\'' && lastC == '\'') {
            return (ParameterQuoting.Single, str.Substring(1, str.Length - 2));
        } else {
            return (ParameterQuoting.None, str);
        }
    }

    public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete,
            CommandAst commandAst, IDictionary fakeBoundParameters) {
        var (wasQuoted, stripped) = StripQuotes(wordToComplete);
        return GetCompletions(stripped, fakeBoundParameters)
                .Select(s => new CompletionResult(QuoteString(s, wasQuoted), s, CompletionResultType.ParameterValue, s));
    }
}

public abstract class DirectoryListingArgumentCompleter : QuotingArgumentCompleter {
    protected abstract IEnumerable<string> GetMatchingItems(string searchPattern, IDictionary fakeBoundParameters);

    protected override IEnumerable<string> GetCompletions(string wordToComplete, IDictionary fakeBoundParameters) {
        if (0 <= wordToComplete.IndexOfAny(Path.GetInvalidFileNameChars())) {
            return Enumerable.Empty<string>();
        }
        return GetMatchingItems($"{wordToComplete}*", fakeBoundParameters);
    }
}

[PublicAPI]
public sealed class ImportedPackageNameCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern, IDictionary _) {
        return InternalState.ImportedPackageManager.EnumeratePackageNames(searchPattern);
    }
}

[PublicAPI]
public sealed class RepositoryPackageNameCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern, IDictionary _) {
        return InternalState.Repository.EnumeratePackageNames(searchPattern);
    }
}

[PublicAPI]
public sealed class RepositoryPackageGeneratorNameCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern, IDictionary _) {
        return InternalState.GeneratorRepository.EnumerateGeneratorNames(searchPattern);
    }
}

[PublicAPI]
public sealed class ValidPackageRootPathCompleter : QuotingArgumentCompleter {
    protected override IEnumerable<string> GetCompletions(string wordToComplete, IDictionary fakeBoundParameters) {
        if (0 <= wordToComplete.IndexOfAny(Path.GetInvalidPathChars())) {
            return Enumerable.Empty<string>();
        }
        // use \ in the searched prefix, so that we match both / and \ (ValidPackageRoots have \)
        wordToComplete = wordToComplete.Replace('/', '\\');
        return InternalState.ImportedPackageManager.PackageRoots.ValidPackageRoots
                .Where(s => s.StartsWith(wordToComplete, StringComparison.InvariantCultureIgnoreCase));
    }
}

/**
 * Argument completer for versions of repository packages.
 * It expects that a parameter called `PackageName` exists (this has to be hardcoded, as there's no supported way to pass
 * arguments to the completer type when used in the [ArgumentCompleter] attribute).
 */
[PublicAPI]
public sealed class RepositoryPackageVersionCompleter : DirectoryListingArgumentCompleter {
    protected override IEnumerable<string> GetMatchingItems(string searchPattern, IDictionary fakeBoundParameters) {
        if (!fakeBoundParameters.Contains("PackageName")) {
            return Enumerable.Empty<string>();
        }

        var packageName = fakeBoundParameters["PackageName"];
        if (packageName == null) {
            return Enumerable.Empty<string>();
        }
        if (packageName is Array {Length: 1} arr) {
            packageName = arr.GetValue(0);
        }

        // package name like `7.1` will be passed as a double, and `7` as an int
        // this filters out unexpected types, and also arrays, which we cannot sensibly auto-complete
        if (packageName is not string && packageName is not int && packageName is not double) {
            return Enumerable.Empty<string>();
        }

        var package = InternalState.Repository.GetPackage(packageName.ToString(), false, false);
        if (!package.Exists) {
            return Enumerable.Empty<string>(); // no such package
        }
        return package.EnumerateVersions(searchPattern).Select(v => v.ToString());
    }
}