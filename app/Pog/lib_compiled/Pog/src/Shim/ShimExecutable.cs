using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pog.Native;
using Pog.Utils;

namespace Pog.Shim;

// TODO: support tags ([noresolve], [resolve], [prepend], [append])
// TODO: switch to shim data format with package-relative paths instead of absolute paths
// TODO: when copying PE resources, should we also enumerate languages?

// Required operations with shims:
// 1) create a new shim (or overwrite an existing one)
//    - store encoded shim data in RCDATA#1
//    - copy PE resources from target
// 2) TODO: get owning package of a shim
// 3) check if the configuration of a shim matches an expected one
//    - encode shim data and compare with stored shim data
//    - read target PE resources, compare with already copied PE resources in the shim
public class ShimExecutable {
    // shim data are stored as an RCDATA resource at index 1
    private static readonly PeResources.ResourceId ShimDataResourceId = new(PeResources.ResourceType.RcData, 1);
    // TODO: bring back ResourceType.Manifest, but it will require some amount of parsing
    //  e.g. Firefox declares required assemblies, which the shim doesn't see, so it fails
    /// List of resource types which are copied from target to the shim.
    private static readonly PeResources.ResourceType[] CopiedResourceTypes = {
        PeResources.ResourceType.Icon, PeResources.ResourceType.IconGroup,
        PeResources.ResourceType.Version /*, ResourceType.Manifest*/
    };
    /// List of supported target extensions. All listed extensions can be invoked directly by `CreateProcess(...)`.
    private static readonly string[] SupportedTargetExtensions = {".exe", ".com", ".cmd", ".bat"};

    /// If true, argv[0] is always replaced with TargetPath, otherwise the path to the shim is kept as argv[0].
    public readonly bool ReplaceArgv0;
    // target must be a full path, accepting command names in PATH is not easily doable, because we need to know the target
    //  subsystem to configure the shim; we could try to resolve the path, copy the subsystem and hope that it doesn't
    //  change, but that's kinda fragile
    public readonly string TargetPath;
    private readonly string _targetExtension;
    public readonly string? WorkingDirectory;
    public readonly string[]? Arguments;
    public readonly (string, EnvVarTemplate)[]? EnvironmentVariables;

    public ShimExecutable(string targetPath, string? workingDirectory = null, string[]? arguments = null,
            IEnumerable<KeyValuePair<string, string[]>>? environmentVariables = null, bool replaceArgv0 = false) {
        Debug.Assert(Path.IsPathRooted(targetPath));

        _targetExtension = Path.GetExtension(targetPath).ToLowerInvariant();
        if (!SupportedTargetExtensions.Contains(_targetExtension)) {
            throw new UnsupportedShimTargetTypeException(
                    $"Shim target '{targetPath}' has an unsupported extension. Supported extensions are: " +
                    string.Join(", ", SupportedTargetExtensions));
        }

        // .cmd/.bat file handler seems to use argv[0] as the target passed to `cmd.exe /c` not lpApplicationName,
        //  which results in an infinite process spawning loop when the original argv[0] is retained
        ReplaceArgv0 = replaceArgv0 || _targetExtension is ".cmd" or ".bat";
        TargetPath = targetPath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        EnvironmentVariables = environmentVariables?.Select(e => {
            if (e.Key.Contains("=")) {
                throw new InvalidEnvironmentVariableNameException(
                        $"Invalid shim executable environment variable name, contains '=': {e.Key}");
            }
            return (e.Key, new EnvVarTemplate(e.Value));
        }).ToArray();
    }

    private bool IsTargetPeBinary() {
        return _targetExtension is ".exe" or ".com";
    }

    /// Ensures that the shim at shimPath is up-to-date.
    /// <returns>true if anything changed, false if shim is up-to-date</returns>
    /// <exception cref="OutdatedShimException"></exception>
    public bool UpdateShim(string shimPath, string? resourceSrcPath = null) {
        return IsTargetPeBinary() ? UpdateShimExe(shimPath, resourceSrcPath) : UpdateShimOther(shimPath, resourceSrcPath);
    }

    /// <exception cref="OutdatedShimException"></exception>
    private bool UpdateShimExe(string shimPath, string? resourceSrcPath) {
        // copy subsystem from the target binary
        var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
        var originalSubsystem = PeSubsystem.SetSubsystem(shimPath, targetSubsystem);
        var subsystemChanged = targetSubsystem != originalSubsystem;

        // copy resources from either target, or a separate module
        var updatedResources = UpdateShimResources(shimPath, resourceSrcPath ?? TargetPath);

        return subsystemChanged || updatedResources;
    }

    /// Updater method for targets that are not PE binaries, and don't have a subsystem and resources.
    /// <exception cref="OutdatedShimException"></exception>
    private bool UpdateShimOther(string shimPath, string? resourceSrcPath) {
        // assume a console subsystem
        var originalSubsystem = PeSubsystem.SetSubsystem(shimPath, PeSubsystem.WindowsSubsystem.WindowsCui);
        var subsystemChanged = originalSubsystem != PeSubsystem.WindowsSubsystem.WindowsCui;

        // target does not have any resources, since it's not a PE binary; either copy resources
        //  from a separate module, or delete any existing resources, if any
        var updatedResources = UpdateShimResources(shimPath, resourceSrcPath);

        return subsystemChanged || updatedResources;
    }

    /// <exception cref="OutdatedShimException"></exception>
    private bool UpdateShimResources(string shimPath, string? resourceSrcPath) {
        var shimData = ShimDataEncoder.EncodeShim(this);

        // updater is somewhat slow, only instantiate it if necessary
        using var shimUpdater = new LazyDisposable<PeResources.ResourceUpdater>(
                () => new PeResources.ResourceUpdater(shimPath));

        // open the module we copy resources from, if any
        using var resourceSrc = resourceSrcPath == null ? null : new PeResources.Module(resourceSrcPath);

        // `shim` must be closed before `.CommitChanges()` is called
        using (var shimModule = new PeResources.Module(shimPath)) {
            // ensure shim data is up to date
            switch (CompareShimData(shimModule, shimData)) {
                case ShimDataStatus.Changed:
                case ShimDataStatus.NoShimData:
                    shimUpdater.Value.SetResource(ShimDataResourceId, shimData);
                    break;
                case ShimDataStatus.OldVersion:
                    // if the exe has outdated shim data, the exe itself is outdated
                    throw new OutdatedShimException("Shim executable expects an older version of shim data, " +
                                                    "replace it with an up-to-date version of the shim executable.");
            }

            foreach (var resourceType in CopiedResourceTypes) {
                if (resourceSrc != null) {
                    // ensure copied resources are up-to-date with target
                    UpdateResources(shimUpdater, shimModule, resourceSrc, resourceType);
                } else {
                    // delete any previously copied resources
                    RemoveResources(shimUpdater, shimModule, resourceType);
                }
            }
        }

        // write the changes
        if (shimUpdater.IsValueCreated) {
            shimUpdater.Value.CommitChanges();
        }

        return shimUpdater.IsValueCreated;
    }

    private enum ShimDataStatus { Same, Changed, NoShimData, OldVersion }

    private static ShimDataStatus CompareShimData(PeResources.Module shim, Span<byte> newShimData) {
        if (!shim.TryGetResource(ShimDataResourceId, out var currentShimData)) {
            return ShimDataStatus.NoShimData;
        }
        if (ShimDataEncoder.ParseVersion(currentShimData) != ShimDataEncoder.CurrentShimDataVersion) {
            return ShimDataStatus.OldVersion;
        }
        return newShimData.SequenceEqual(currentShimData) ? ShimDataStatus.Same : ShimDataStatus.Changed;
    }

    private void WriteNewShimResources(string shimPath, string? resourceSrcPath) {
        var shimData = ShimDataEncoder.EncodeShim(this);
        using var shimUpdater = new PeResources.ResourceUpdater(shimPath);

        // write shim data
        shimUpdater.SetResource(ShimDataResourceId, shimData);

        if (resourceSrcPath != null) {
            using var target = new PeResources.Module(resourceSrcPath);

            // copy resources from target
            foreach (var resourceType in CopiedResourceTypes) {
                CopyResources(shimUpdater, target, resourceType);
            }
        }

        shimUpdater.CommitChanges();
    }

    /// Configures a new shim. The shim binary at `shimPath` should already exist.
    /// Assumes that the shim binary has no existing resources.
    public void WriteNewShim(string shimPath, string? resourceSrcPath = null) {
        if (IsTargetPeBinary()) {
            // copy subsystem from target binary
            var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
            PeSubsystem.SetSubsystem(shimPath, targetSubsystem);

            // write shim data and copy resources from either target, or a separate module
            WriteNewShimResources(shimPath, resourceSrcPath ?? TargetPath);
        } else {
            // write shim data, copy resources from the passed module, if any
            WriteNewShimResources(shimPath, resourceSrcPath);
        }
    }

    /// Copies resources of given `type`, assumes that the destination binary does not have any resources of type `type`.
    private static void CopyResources(PeResources.ResourceUpdater updater, PeResources.Module src,
            PeResources.ResourceType type) {
        try {
            src.IterateResourceNames(type, name => {
                updater.CopyResourceFrom(src, new(type, name));
                return true;
            });
        } catch (PeResources.ResourceNotFoundException) {
            // nothing to copy
        }
    }

    private static void RemoveResources(Lazy<PeResources.ResourceUpdater> updater, PeResources.Module dest,
            PeResources.ResourceType type) {
        try {
            dest.IterateResourceNames(type, name => {
                updater.Value.DeleteResource(new(type, name));
                return true;
            });
        } catch (PeResources.ResourceTypeNotFoundException) {
            // no resources of type `type`, nothing to delete
        }
    }

    /// Ensures that `dest` has the same resources of type `type` as `src`. Copies all missing resources, deletes any extra resources.
    /// Only instantiates `updater` if something needs to be copied.
    private static void UpdateResources(Lazy<PeResources.ResourceUpdater> updater, PeResources.Module dest,
            PeResources.Module src,
            PeResources.ResourceType type) {
        // list resources of `type` in src
        if (!src.TryGetResourceNames(type, out var srcNames)) {
            srcNames = new();
        }

        // copy all non-matching resources
        foreach (var name in srcNames) {
            var srcResource = src.GetResource(new(type, name));
            if (dest.TryGetResource(new(type, name), out var destResource) && srcResource.SequenceEqual(destResource)) {
                continue;
            }
            // missing resource, copy it
            updater.Value.CopyResourceFrom(src, new(type, name));
        }

        // check if shim has extra resources
        try {
            dest.IterateResourceNames(type, name => {
                if (!srcNames.Contains(name)) {
                    // extra resource, delete it
                    updater.Value.DeleteResource(new(type, name));
                }
                return true;
            });
        } catch (PeResources.ResourceNotFoundException) {
            // no extra resources of type `type`, continue
        }
    }

    public class UnsupportedShimTargetTypeException(string message) : ArgumentException(message);

    public class OutdatedShimException(string message) : Exception(message);

    public class InvalidEnvironmentVariableNameException(string message) : Exception(message);

    public class EnvVarTemplate {
        public record struct Segment(bool IsEnvVarName, bool NewSegment, string String);

        public readonly List<Segment> Segments = new();

        public EnvVarTemplate(IEnumerable<string> rawValueList) {
            foreach (var v in rawValueList) {
                ParseSingleValue(v);
            }

            if (Segments.Count == 0) {
                // set value to empty string
                Segments.Add(new Segment(false, true, ""));
            }
        }

        private void ParseSingleValue(string value) {
            var parts = value.Split('%');
            var isEnvVarName = true;
            var first = true;
            foreach (var p in parts) {
                isEnvVarName = !isEnvVarName;
                if (isEnvVarName && p.Contains("=")) {
                    throw new InvalidEnvironmentVariableNameException(
                            $"Invalid shim executable environment variable name, contains '=': {p}");
                }

                if (p == "") {
                    if (isEnvVarName) {
                        // %%, replace with a literal %
                        Segments.Add(new Segment(false, first, "%"));
                        first = false;
                    } else {
                        // empty string, skip (e.g. %HOME%%VAR%)
                    }
                } else {
                    Segments.Add(new Segment(isEnvVarName, first, p));
                    first = false;
                }
            }

            if (isEnvVarName) {
                throw new InvalidEnvironmentVariableNameException(
                        $"Unterminated environment variable name in the following value (odd number of '%'): {value}");
            }
        }
    }
}
