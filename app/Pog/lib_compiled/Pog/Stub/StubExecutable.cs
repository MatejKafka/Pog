using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Pog.Utils;
using static Pog.Native;
using ResourceType = Pog.Native.PeResources.ResourceType;

namespace Pog.Stub;

// TODO: support tags ([noresolve], [resolve], [prepend], [append])
// TODO: switch to stub data format with package-relative paths instead of absolute paths

// Required operations with stubs:
// 1) create a new stub (or overwrite an existing one)
//    - store encoded stub data in RCDATA#1
// 2) TODO: get owning package of a stub
// 3) check if the configuration of a stub matches an expected one
//    - encode stub data and compare with stored stub data
//    - read target resources, compare with the hash stored in the stub
[PublicAPI]
public class StubExecutable {
    // stub data are stored as an RCDATA resource at index 1
    private static readonly PeResources.ResourceId StubDataResourceId = new(ResourceType.RcData, 1);
    /// List of resource types which are copied from target to the stub.
    private static readonly ResourceType[] CopiedResourceTypes =
            {ResourceType.Icon, ResourceType.IconGroup, ResourceType.Version, ResourceType.Manifest};
    /// List of supported target extensions. All listed extensions can be invoked directly by `CreateProcess(...)`.
    private static readonly string[] SupportedTargetExtensions = {".exe", ".com", ".cmd", ".bat"};

    /// If true, argv[0] is always replaced with TargetPath, otherwise the path to the stub is kept as argv[0].
    public bool ReplaceArgv0;
    public string TargetPath;
    public string? WorkingDirectory;
    public string[]? Arguments;
    public Dictionary<string, string>? EnvironmentVariables;

    public StubExecutable(string targetPath, string? workingDirectory = null,
            string[]? arguments = null, Dictionary<string, string>? environmentVariables = null) {
        var targetExtension = Path.GetExtension(targetPath);
        if (!SupportedTargetExtensions.Contains(targetExtension)) {
            throw new UnsupportedStubTargetTypeException(
                    $"Stub target '{targetPath}' has an unsupported extension. Supported extensions are: " +
                    string.Join(", ", SupportedTargetExtensions));
        }

        // .cmd/.bat file handler seems to use argv[0] as the target passed to `cmd.exe /c` not lpApplicationName,
        //  which results in an infinite process spawning loop when the original argv[0] is retained
        ReplaceArgv0 = targetExtension is ".cmd" or ".bat";
        TargetPath = targetPath;
        WorkingDirectory = workingDirectory;
        Arguments = arguments;
        EnvironmentVariables = environmentVariables;
    }

    private bool IsTargetPeBinary() {
        return Path.GetExtension(TargetPath) is ".exe" or ".com";
    }

    /// Ensures that the stub at stubPath is up-to-date.
    /// <returns>true if anything changed, false if stub is up-to-date</returns>
    public bool UpdateStub(string stubPath) {
        return IsTargetPeBinary() ? UpdateStubExe(stubPath) : UpdateStubOther(stubPath);
    }

    /// Updater method for targets that are not PE binaries, and don't have a subsystem and resources.
    private bool UpdateStubOther(string stubPath) {
        var stubData = StubDataEncoder.EncodeStub(this);

        // assume a console subsystem
        var originalSubsystem = PeSubsystem.SetSubsystem(stubPath, PeSubsystem.WindowsSubsystem.WindowsCui);
        var subsystemChanged = originalSubsystem != PeSubsystem.WindowsSubsystem.WindowsCui;

        // updater is somewhat slow, only instantiate it if necessary
        using var stubUpdater = new LazyDisposable<PeResources.ResourceUpdater>(
                () => new PeResources.ResourceUpdater(stubPath));

        // `stub` must be closed before `.CommitChanges()` is called
        using (var stub = new PeResources.Module(stubPath)) {
            if (!IsStubDataUpToDate(stub, stubData)) {
                stubUpdater.Value.SetResource(StubDataResourceId, stubData);
            }

            // delete any previously copied resources
            foreach (var resourceType in CopiedResourceTypes) {
                RemoveResources(stubUpdater, stub, resourceType);
            }
        }

        // write the changes
        if (stubUpdater.IsValueCreated) {
            stubUpdater.Value.CommitChanges();
        }

        return subsystemChanged || stubUpdater.IsValueCreated;
    }

    private bool UpdateStubExe(string stubPath) {
        var stubData = StubDataEncoder.EncodeStub(this);

        // copy subsystem from the target binary
        var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
        var originalSubsystem = PeSubsystem.SetSubsystem(stubPath, targetSubsystem);
        var subsystemChanged = targetSubsystem != originalSubsystem;

        // open stub and target
        using var target = new PeResources.Module(TargetPath);
        // updater is somewhat slow, only instantiate it if necessary
        using var stubUpdater = new LazyDisposable<PeResources.ResourceUpdater>(
                () => new PeResources.ResourceUpdater(stubPath));

        // `stub` must be closed before `.CommitChanges()` is called
        using (var stub = new PeResources.Module(stubPath)) {
            if (!IsStubDataUpToDate(stub, stubData)) {
                stubUpdater.Value.SetResource(StubDataResourceId, stubData);
            }

            // ensure copied resources are up-to-date with target
            foreach (var resourceType in CopiedResourceTypes) {
                UpdateResources(stubUpdater, stub, target, resourceType);
            }
        }

        // write the changes
        if (stubUpdater.IsValueCreated) {
            stubUpdater.Value.CommitChanges();
        }

        return subsystemChanged || stubUpdater.IsValueCreated;
    }

    private bool IsStubDataUpToDate(PeResources.Module stub, Span<byte> stubData) {
        return stub.TryGetResource(StubDataResourceId, out var currentStubData) && stubData.SequenceEqual(currentStubData);
    }

    /// Configures a new stub. The stub binary at `stubPath` should already exist.
    /// Assumes that the stub binary has no existing resources.
    public void WriteNewStub(string stubPath) {
        if (IsTargetPeBinary()) {
            WriteNewStubExe(stubPath);
        } else {
            WriteNewStubOther(stubPath);
        }
    }

    public void WriteNewStubOther(string stubPath) {
        var stubData = StubDataEncoder.EncodeStub(this);
        using var stubUpdater = new PeResources.ResourceUpdater(stubPath);

        // write stub data
        stubUpdater.SetResource(StubDataResourceId, stubData);
        stubUpdater.CommitChanges();
    }

    public void WriteNewStubExe(string stubPath) {
        var stubData = StubDataEncoder.EncodeStub(this);

        // copy subsystem from target binary
        var targetSubsystem = PeSubsystem.GetSubsystem(TargetPath);
        PeSubsystem.SetSubsystem(stubPath, targetSubsystem);

        // open stub and target
        using var target = new PeResources.Module(TargetPath);
        using var stubUpdater = new PeResources.ResourceUpdater(stubPath);

        // write stub data
        stubUpdater.SetResource(StubDataResourceId, stubData);

        // copy icons and version from target
        foreach (var resourceType in CopiedResourceTypes) {
            CopyResources(stubUpdater, target, resourceType);
        }

        stubUpdater.CommitChanges();
    }

    /// Copies resources of given `type`, assumes that the destination binary does not have any resources of type `type`.
    private void CopyResources(PeResources.ResourceUpdater updater, PeResources.Module src, ResourceType type) {
        try {
            src.IterateResourceNames(type, name => {
                updater.CopyResourceFrom(src, type, name);
                return true;
            });
        } catch (PeResources.ResourceNotFoundException) {
            // nothing to copy
        }
    }

    private void RemoveResources(Lazy<PeResources.ResourceUpdater> updater, PeResources.Module dest, ResourceType type) {
        try {
            dest.IterateResourceNames(type, name => {
                updater.Value.DeleteResource(type, name);
                return true;
            });
        } catch (PeResources.ResourceTypeNotFoundException) {
            // no resources of type `type`, nothing to delete
        }
    }

    /// Ensures that `dest` has the same resources of type `type` as `src`. Copies all missing resources, deletes any extra resources.
    /// Only instantiates `updater` if something needs to be copied.
    private void UpdateResources(Lazy<PeResources.ResourceUpdater> updater, PeResources.Module dest, PeResources.Module src,
            ResourceType type) {
        // list resources of `type` in src
        if (!src.TryGetResourceNames(type, out var srcNames)) {
            return;
        }

        // copy all non-matching resources
        foreach (var name in srcNames) {
            var srcResource = src.GetResource(type, name);
            if (dest.TryGetResource(type, name, out var destResource) && srcResource.SequenceEqual(destResource)) {
                continue;
            }
            // missing resource, copy it
            updater.Value.CopyResourceFrom(src, type, name);
        }

        // check if stub has extra resources
        try {
            dest.IterateResourceNames(type, name => {
                if (!srcNames.Contains(name)) {
                    // extra resource, delete it
                    updater.Value.DeleteResource(type, name);
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
}
