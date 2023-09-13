using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Pog.Native;
using Pog.Utils;

namespace Pog.Stub;

// TODO: support tags ([noresolve], [resolve], [prepend], [append])
// TODO: switch to stub data format with package-relative paths instead of absolute paths
// TODO: when copying PE resources, should we also enumerate languages?

// Required operations with stubs:
// 1) create a new stub (or overwrite an existing one)
//    - store encoded stub data in RCDATA#1
//    - copy PE resources from target
// 2) TODO: get owning package of a stub
// 3) check if the configuration of a stub matches an expected one
//    - encode stub data and compare with stored stub data
//    - read target PE resources, compare with already copied PE resources in the stub
public class StubExecutable {
    // stub data are stored as an RCDATA resource at index 1
    private static readonly PeResources.ResourceId StubDataResourceId = new(PeResources.ResourceType.RcData, 1);
    // TODO: bring back ResourceType.Manifest, but it will require some amount of parsing
    //  e.g. Firefox declares required assemblies, which the stub doesn't see, so it fails
    /// List of resource types which are copied from target to the stub.
    private static readonly PeResources.ResourceType[] CopiedResourceTypes = {
        PeResources.ResourceType.Icon, PeResources.ResourceType.IconGroup,
        PeResources.ResourceType.Version /*, ResourceType.Manifest*/
    };
    /// List of supported target extensions. All listed extensions can be invoked directly by `CreateProcess(...)`.
    private static readonly string[] SupportedTargetExtensions = {".exe", ".com", ".cmd", ".bat"};

    /// If true, argv[0] is always replaced with TargetPath, otherwise the path to the stub is kept as argv[0].
    public readonly bool ReplaceArgv0;
    // target must be a full path, accepting command names in PATH is not easily doable, because we need to know the target
    //  subsystem to configure the stub; we could try to resolve the path, copy the subsystem and hope that it doesn't
    //  change, but that's kinda fragile
    public readonly string TargetPath;
    public readonly string? WorkingDirectory;
    public readonly string[]? Arguments;
    public readonly (string, EnvVarTemplate)[]? EnvironmentVariables;

    public StubExecutable(string targetPath, string? workingDirectory = null, string[]? arguments = null,
            IEnumerable<KeyValuePair<string, string[]>>? environmentVariables = null, bool replaceArgv0 = false) {
        Debug.Assert(Path.IsPathRooted(targetPath));
        var targetExtension = Path.GetExtension(targetPath).ToLower();
        if (!SupportedTargetExtensions.Contains(targetExtension)) {
            throw new UnsupportedStubTargetTypeException(
                    $"Stub target '{targetPath}' has an unsupported extension. Supported extensions are: " +
                    string.Join(", ", SupportedTargetExtensions));
        }

        // .cmd/.bat file handler seems to use argv[0] as the target passed to `cmd.exe /c` not lpApplicationName,
        //  which results in an infinite process spawning loop when the original argv[0] is retained
        ReplaceArgv0 = replaceArgv0 || targetExtension is ".cmd" or ".bat";
        TargetPath = targetPath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        EnvironmentVariables = environmentVariables?.Select(e => {
            if (e.Key.Contains("=")) {
                throw new InvalidEnvironmentVariableNameException(
                        $"Invalid stub executable environment variable name, contains '=': {e.Key}");
            }
            return (e.Key, new EnvVarTemplate(e.Value));
        }).ToArray();
    }

    private bool IsTargetPeBinary() {
        return Path.GetExtension(TargetPath) is ".exe" or ".com";
    }

    /// Ensures that the stub at stubPath is up-to-date.
    /// <returns>true if anything changed, false if stub is up-to-date</returns>
    /// <exception cref="OutdatedStubException"></exception>
    public bool UpdateStub(string stubPath, string? resourceSrcPath = null) {
        return IsTargetPeBinary() ? UpdateStubExe(stubPath, resourceSrcPath) : UpdateStubOther(stubPath, resourceSrcPath);
    }

    /// <exception cref="OutdatedStubException"></exception>
    private bool UpdateStubExe(string stubPath, string? resourceSrcPath) {
        // copy subsystem from the target binary
        var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
        var originalSubsystem = PeSubsystem.SetSubsystem(stubPath, targetSubsystem);
        var subsystemChanged = targetSubsystem != originalSubsystem;

        // copy resources from either target, or a separate module
        var updatedResources = UpdateStubResources(stubPath, resourceSrcPath ?? TargetPath);

        return subsystemChanged || updatedResources;
    }

    /// Updater method for targets that are not PE binaries, and don't have a subsystem and resources.
    /// <exception cref="OutdatedStubException"></exception>
    private bool UpdateStubOther(string stubPath, string? resourceSrcPath) {
        // assume a console subsystem
        var originalSubsystem = PeSubsystem.SetSubsystem(stubPath, PeSubsystem.WindowsSubsystem.WindowsCui);
        var subsystemChanged = originalSubsystem != PeSubsystem.WindowsSubsystem.WindowsCui;

        // target does not have any resources, since it's not a PE binary; either copy resources
        //  from a separate module, or delete any existing resources, if any
        var updatedResources = UpdateStubResources(stubPath, resourceSrcPath);

        return subsystemChanged || updatedResources;
    }

    /// <exception cref="OutdatedStubException"></exception>
    private bool UpdateStubResources(string stubPath, string? resourceSrcPath) {
        var stubData = StubDataEncoder.EncodeStub(this);

        // updater is somewhat slow, only instantiate it if necessary
        using var stubUpdater = new LazyDisposable<PeResources.ResourceUpdater>(
                () => new PeResources.ResourceUpdater(stubPath));

        // open the module we copy resources from, if any
        using var resourceSrc = resourceSrcPath == null ? null : new PeResources.Module(resourceSrcPath);

        // `stub` must be closed before `.CommitChanges()` is called
        using (var stubModule = new PeResources.Module(stubPath)) {
            // ensure stub data is up to date
            switch (CompareStubData(stubModule, stubData)) {
                case StubDataStatus.Changed:
                case StubDataStatus.NoStubData:
                    stubUpdater.Value.SetResource(StubDataResourceId, stubData);
                    break;
                case StubDataStatus.OldVersion:
                    // if the exe has outdated stub data, the exe itself is outdated
                    throw new OutdatedStubException("Stub executable expects an older version of stub data, " +
                                                    "replace it with an up-to-date version of the stub executable.");
            }

            foreach (var resourceType in CopiedResourceTypes) {
                if (resourceSrc != null) {
                    // ensure copied resources are up-to-date with target
                    UpdateResources(stubUpdater, stubModule, resourceSrc, resourceType);
                } else {
                    // delete any previously copied resources
                    RemoveResources(stubUpdater, stubModule, resourceType);
                }
            }
        }

        // write the changes
        if (stubUpdater.IsValueCreated) {
            stubUpdater.Value.CommitChanges();
        }

        return stubUpdater.IsValueCreated;
    }

    private enum StubDataStatus { Same, Changed, NoStubData, OldVersion }

    private static StubDataStatus CompareStubData(PeResources.Module stub, Span<byte> newStubData) {
        if (!stub.TryGetResource(StubDataResourceId, out var currentStubData)) {
            return StubDataStatus.NoStubData;
        }
        if (StubDataEncoder.ParseVersion(currentStubData) != StubDataEncoder.CurrentStubDataVersion) {
            return StubDataStatus.OldVersion;
        }
        return newStubData.SequenceEqual(currentStubData) ? StubDataStatus.Same : StubDataStatus.Changed;
    }

    private void WriteNewStubResources(string stubPath, string? resourceSrcPath) {
        var stubData = StubDataEncoder.EncodeStub(this);
        using var stubUpdater = new PeResources.ResourceUpdater(stubPath);

        // write stub data
        stubUpdater.SetResource(StubDataResourceId, stubData);

        if (resourceSrcPath != null) {
            using var target = new PeResources.Module(resourceSrcPath);

            // copy resources from target
            foreach (var resourceType in CopiedResourceTypes) {
                CopyResources(stubUpdater, target, resourceType);
            }
        }

        stubUpdater.CommitChanges();
    }

    /// Configures a new stub. The stub binary at `stubPath` should already exist.
    /// Assumes that the stub binary has no existing resources.
    public void WriteNewStub(string stubPath, string? resourceSrcPath = null) {
        if (IsTargetPeBinary()) {
            // copy subsystem from target binary
            var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
            PeSubsystem.SetSubsystem(stubPath, targetSubsystem);

            // write stub data and copy resources from either target, or a separate module
            WriteNewStubResources(stubPath, resourceSrcPath ?? TargetPath);
        } else {
            // write stub data, copy resources from the passed module, if any
            WriteNewStubResources(stubPath, resourceSrcPath);
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

        // check if stub has extra resources
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

    public class UnsupportedStubTargetTypeException : ArgumentException {
        public UnsupportedStubTargetTypeException(string message) : base(message) {}
    }

    public class OutdatedStubException : Exception {
        public OutdatedStubException(string message) : base(message) {}
    }

    public class InvalidEnvironmentVariableNameException : Exception {
        public InvalidEnvironmentVariableNameException(string message) : base(message) {}
    }

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
                            $"Invalid stub executable environment variable name, contains '=': {p}");
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
