namespace Pog;

internal class DownloadContainerContext(Package package, bool lowPriorityDownload)
        : Container.EnvironmentContext<DownloadContainerContext> {
    public readonly Package Package = package;
    public readonly bool LowPriorityDownload = lowPriorityDownload;
}
