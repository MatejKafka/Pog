using JetBrains.Annotations;

namespace Pog;

[PublicAPI]
public class DownloadContainerContext(Package package, bool lowPriorityDownload)
        : Container.EnvironmentContext<DownloadContainerContext> {
    public readonly Package Package = package;
    public readonly bool LowPriorityDownload = lowPriorityDownload;
}
