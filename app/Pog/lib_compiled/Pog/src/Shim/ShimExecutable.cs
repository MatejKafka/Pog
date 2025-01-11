using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pog.Native;
using Pog.Utils;

namespace Pog.Shim;

// TODO: support tags ([noresolve], [resolve], [prepend], [append], [recessive])
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
    //  also, some binaries (e.g. mmc.exe seem to have multiple manifests)
    // TODO: if the Manifest resource is also copied, it should probably be always copied from the target? but the point of
    //  MetadataSource was iirc to allow using a batch file launcher but use metadata of the actual target, in which case
    //  we want the manifest of the MetadataSource?
    /// List of resource types which are copied from target to the shim.
    private static readonly PeResources.ResourceType[] CopiedResourceTypes = {
        PeResources.ResourceType.Icon, PeResources.ResourceType.IconGroup,
        PeResources.ResourceType.Version, /*ResourceType.Manifest,*/
    };
    /// List of supported target extensions. All listed extensions can be invoked directly by `CreateProcess(...)`.
    private static readonly string[] SupportedTargetExtensions = [".exe", ".com", ".cmd", ".bat"];

    /// If true, use argv[0] as the shim target instead of specifying target in a separate `CreateProcess` parameter.
    public readonly bool Argv0AsTarget;
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
    public readonly string? MetadataSource;

    public ShimExecutable(string targetPath, string? workingDirectory = null, string[]? arguments = null,
            IEnumerable<KeyValuePair<string, string[]>>? environmentVariables = null, string? metadataSource = null,
            bool replaceArgv0 = false) {
        Debug.Assert(Path.IsPathRooted(targetPath));

        _targetExtension = Path.GetExtension(targetPath).ToLowerInvariant();
        if (!SupportedTargetExtensions.Contains(_targetExtension)) {
            throw new UnsupportedShimTargetTypeException(
                    $"Shim target '{targetPath}' has an unsupported extension. Supported extensions are: " +
                    string.Join(", ", SupportedTargetExtensions));
        }

        var isBatchFile = _targetExtension is ".cmd" or ".bat";

        // see documentation of ShimFlags.NullTarget, item 2); for an example of where this is necessary, try `Export-Command`
        //  on a batch file inside a package with a space in its name (e.g. `VS Code`) and invoke the shim with a quoted path
        //  that does NOT contain a space (e.g. `cmd /c 'code "D:\test\"'`)
        Argv0AsTarget = isBatchFile;
        // batch file handler seems to use argv[0] as the target passed to `cmd.exe /c` instead of `lpApplicationName`,
        //  which results in an infinite process spawning loop when the original argv[0] is retained
        ReplaceArgv0 = replaceArgv0 || isBatchFile || Argv0AsTarget;
        MetadataSource = metadataSource;

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
    /// <param name="shimPath">Path an existing initialized shim executable to update.</param>
    /// <returns>true if anything changed, false if shim is up-to-date</returns>
    /// <exception cref="OutdatedShimException"></exception>
    public bool UpdateShim(string shimPath) {
        return IsTargetPeBinary() ? UpdateShimExe(shimPath) : UpdateShimOther(shimPath);
    }

    /// <exception cref="OutdatedShimException"></exception>
    private bool UpdateShimExe(string shimPath) {
        // copy subsystem from the target binary
        var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
        // first read the shim subsystem, maybe we don't need to update it at all
        // it would be faster to read and update it in one step, but when the shim is in use,
        //  we cannot open it for writing
        var shimSubsystem = PeSubsystem.GetSubsystem(shimPath);
        var subsystemMatches = targetSubsystem == shimSubsystem;
        if (!subsystemMatches) {
            PeSubsystem.SetSubsystem(shimPath, targetSubsystem);
        }

        // copy resources from either target, or a separate module
        var updatedResources = UpdateShimResources(shimPath, MetadataSource ?? TargetPath);

        return !subsystemMatches || updatedResources;
    }

    /// Updater method for targets that are not PE binaries, and don't have a subsystem and resources.
    /// <exception cref="OutdatedShimException"></exception>
    private bool UpdateShimOther(string shimPath) {
        // assume a console subsystem
        var originalSubsystem = PeSubsystem.SetSubsystem(shimPath, PeSubsystem.WindowsSubsystem.WindowsCui);
        var subsystemChanged = originalSubsystem != PeSubsystem.WindowsSubsystem.WindowsCui;

        // target does not have any resources, since it's not a PE binary; either copy resources
        //  from a separate module, or delete any existing resources, if any
        var updatedResources = UpdateShimResources(shimPath, MetadataSource);

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
    public void WriteNewShim(string shimPath) {
        if (IsTargetPeBinary()) {
            // copy subsystem from target binary
            var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
            PeSubsystem.SetSubsystem(shimPath, targetSubsystem);

            // write shim data and copy resources from either target, or a separate module
            WriteNewShimResources(shimPath, MetadataSource ?? TargetPath);
        } else {
            // write shim data, copy resources from the passed module, if any
            WriteNewShimResources(shimPath, MetadataSource);
        }
    }

    /// Copies resources of given `type`, assumes that the destination binary does not have any resources of type `type`.
    private static void CopyResources(PeResources.ResourceUpdater updater, PeResources.Module src,
            PeResources.ResourceType type) {
        try {
            src.IterateResourceNames(type, name => {
                try {
                    updater.SetResource(new(type, name), src.GetResource(new(type, name)));
                } catch (PeResources.InvalidResourceContentException) {
                    // ignore invalid resource - it's very rare (only seen it with a single project) and we can't really do
                    //  anything reasonable about it (and other programs, including the Shell itself also ignore it)
                }
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
            srcNames = [];
        }

        // copy all non-matching resources
        foreach (var id in srcNames.Select(name => new PeResources.ResourceId(type, name))) {
            var srcResource = src.GetResource(id);
            if (dest.TryGetResource(id, out var destResource) && srcResource.SequenceEqual(destResource)) {
                continue;
            }
            // missing resource, copy it
            updater.Value.SetResource(id, srcResource);
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

        public readonly bool Recessive;
        public readonly List<Segment> Segments = [];

        public EnvVarTemplate(IEnumerable<string> rawValueList, bool recessive = false) {
            Recessive = recessive;

            foreach (var v in rawValueList) {
                ParseAndSetSingleValue(v);
            }

            if (Segments.Count == 0) {
                // set value to empty string
                Segments.Add(new Segment(false, true, ""));
            }
        }

        private void ParseAndSetSingleValue(string value) {
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
