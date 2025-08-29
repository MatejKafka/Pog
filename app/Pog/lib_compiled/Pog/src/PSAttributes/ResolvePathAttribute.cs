using System;
using System.Collections;
using System.Linq;
using System.Management.Automation;
using Polyfills.System.Diagnostics.CodeAnalysis;

namespace Pog.PSAttributes;

public abstract class TransformArgumentAttribute : ArgumentTransformationAttribute {
    public bool Array = false;

    public override object? Transform(EngineIntrinsics engineIntrinsics, object? inputData) {
        if (inputData == null) {
            return null;
        }

        if (inputData is PSObject psObject) {
            inputData = psObject.BaseObject;
        }

        if (Array && inputData is IList list) {
            return list.Cast<object?>().Select(p => ProcessSingle(engineIntrinsics, p)).ToArray();
        } else {
            return ProcessSingle(engineIntrinsics, inputData);
        }
    }

    protected abstract object? ProcessSingle(EngineIntrinsics engineIntrinsics, object? item);
}

/// This attribute validates that the passed path is valid and resolves the PSPath into an absolute provider path,
/// without expanding wildcards.
public class ResolvePathAttribute(string targetName = "Path") : TransformArgumentAttribute {
    protected override object? ProcessSingle(EngineIntrinsics engineIntrinsics, object? item) {
        if (item == null) return null;
        // this mirrors what PowerShell does for string args
        var path = item.ToString();

        if (!engineIntrinsics.InvokeProvider.Item.Exists(path)) {
            throw new ArgumentException($"{targetName} does not exist: {path}");
        }
        var resolved = engineIntrinsics.SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        return new UserPath(resolved, path);
    }
}

public class ResolveShellLinkTargetAttribute() : ResolvePathAttribute("Shell link target") {
    protected override object? ProcessSingle(EngineIntrinsics engineIntrinsics, object? item) {
        if (item is string path && path.StartsWith("::{") && path.EndsWith("}")) {
            return path;
        }
        return base.ProcessSingle(engineIntrinsics, item);
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
