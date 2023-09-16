namespace Pog;

/// Static class for tracking statistics relevant for optimization.
public static class InstrumentationCounter {
    public static ulong PackageRootFileReads = 0;
    public static ulong ManifestLoads = 0;
    public static ulong ManifestTemplateSubstitutions = 0;
}
