using System;
using System.Collections;
using System.Linq;
using System.Management.Automation;
using Polyfills.System.Diagnostics.CodeAnalysis;

namespace Pog.PSAttributes;

/// This attribute validates that the passed path is valid and resolves the PSPath into an absolute provider path,
/// without expanding wildcards.
public class ResolvePathAttribute(string targetName = "Path", bool array = false) : ArgumentTransformationAttribute {
    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData) {
        if (inputData == null) {
            return null;
        }

        if (inputData is PSObject psObject) {
            inputData = psObject.BaseObject;
        }

        if (array && inputData is IList list) {
            return list.Cast<object>().Select(p => ProcessSingle(p.ToString(), engineIntrinsics)).ToArray();
        } else if (inputData is string path) {
            return ProcessSingle(path, engineIntrinsics);
        } else {
            // this mirrors what PowerShell does
            return ProcessSingle(inputData.ToString(), engineIntrinsics);
        }
    }

    private UserPath ProcessSingle(string path, EngineIntrinsics engineIntrinsics) {
        if (!engineIntrinsics.InvokeProvider.Item.Exists(path)) {
            throw new ArgumentException($"{targetName} does not exist: {path}");
        }
        var resolved = engineIntrinsics.SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        return new UserPath(resolved, path);
    }
}

/// Type used for representing a path passed by user that was resolved using ResolvePathAttribute, but still provides
/// the original string for logging/UI purposes.
public readonly record struct UserPath(string Resolved, string Raw) {
    [return: NotNullIfNotNull("path")]
    public static implicit operator string?(UserPath? path) => path?.Resolved;

    // PowerShell type conversion during parameter binding uses .ToString() instead of the cast operator above
    public override string ToString() {
        return Resolved;
    }
}
