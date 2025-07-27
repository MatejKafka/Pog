using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading.Tasks;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;
using Pog.Utils.Http;

namespace Pog.InnerCommands;

internal sealed class InvokeFileDownload(PogCmdlet cmdlet) : ScalarCommand<string>(cmdlet) {
    [Parameter] public required string SourceUrl;
    [Parameter] public required string DestinationDirPath;
    [Parameter] public required DownloadParameters DownloadParameters;
    [Parameter] public ProgressActivity ProgressActivity = new();

    /// <summary>Downloads the file from $SourceUrl to $DestinationDirPath.</summary>
    /// <returns>Full path of the downloaded file.</returns>
    /// <remarks>
    /// BITS transfer (https://docs.microsoft.com/en-us/windows/win32/bits/about-bits) is used for download. Originally,
    /// Invoke-WebRequest was used in some cases, and it was potentially faster for small files for cold downloads (BITS
    /// service takes some time to initialize when not used for a while), but the added complexity does not seem to be worth it.
    /// </remarks>>
    /// <remarks>
    /// I experimented with different download tools: iwr, curl, BITS, aria2
    ///  - iwr is quite slow; internally, it uses HttpClient, so I'm assuming using it directly will also be slow
    ///  - BITS, curl and aria2 are roughly the same speed, with curl being the fastest by ~10%
    ///    (download test with 'VS Code' followed by SHA-256 hash validation, aria2 11.9s, curl 10.6s, BITS 11.3s)
    ///  - BITS should in theory be a bit better mannered on heavily loaded systems, but I haven't experimented with it,
    ///    except for the `-Priority Low` parameter
    ///  - BITS does not support Content-Disposition, and it takes the filename from the original source URL,
    ///    but messes up if it contains a query string and fails while trying to create an invalid file name;
    ///    if the .NET module was used directly instead of the PowerShell cmdlets, this might not be an issue
    ///  - aria2 supports validating the checksum, but either it does so after the file is downloaded or it's
    ///    just slow in general, since curl followed by a separate hash check is faster
    /// </remarks>
    public override string Invoke() {
        // BITS powershell module has issues with resolving URLs with query strings (apparently, it assumes everything after
        //  the last / is a file name) and it uses just the source URL instead of the final URL or Content-Disposition,
        //  so we resolve the file name ourselves

        // originally, filename and final URL was resolved before BITS was invoked, but that causes extra latency,
        //  because BITS has to reopen a new connection for the download, so we now do the resolution and download
        //  in parallel and reconcile them at the end; that's non-ideal, since we're duplicating work, but works
        //  well-enough for now

        var parsedUrl = new Uri(SourceUrl);
        var origUrlFileName = HttpFileNameParser.GetDownloadedFileName(parsedUrl, null);
        var tmpDownloadTarget = $"{DestinationDirPath}\\{origUrlFileName}";

        Task<DownloadTargetResolver.DownloadTarget>? resolverTask = null;
        if (Path.GetExtension(origUrlFileName) is ".zip" or ".7z" or ".exe" or ".tgz" ||
            origUrlFileName.EndsWith(".tar.gz")) {
            // original URL seems to have a sensible filename extension, assume it's correct and do not try
            //  to resolve the final filename
        } else {
            // run the resolver in parallel to the download
            resolverTask = DownloadTargetResolver.ResolveAsync(CancellationToken, parsedUrl, DownloadParameters.UserAgent);
        }

        ProgressActivity.Activity ??= "BITS Transfer";
        ProgressActivity.Description ??= $"Downloading '{SourceUrl}'...";

        var bitsParams = new Hashtable {
            {"Source", SourceUrl},
            // since the BITS service runs under SYSTEM, it does not see user-level `subst` drives;
            //  we must resolve any subst path to the actual drive before passing the path to BITS
            {"Destination", FsUtils.ResolveSubstPath(tmpDownloadTarget)},
            {"DisplayName", ProgressActivity.Activity},
            {"Description", ProgressActivity.Description},
            // passing -Dynamic allows BITS to communicate with badly-mannered servers that don't support HEAD requests,
            //  Content-Length headers,...; see https://docs.microsoft.com/en-us/windows/win32/api/bits5_0/ne-bits5_0-bits_job_property_id),
            //  section BITS_JOB_PROPERTY_DYNAMIC_CONTENT
            {"Dynamic", true},
            {"Priority", DownloadParameters.LowPriorityDownload ? "Low" : "Foreground"},
        };

        var userAgentStr = DownloadParameters.UserAgent.GetHeaderString();
        WriteDebug($"Using a custom user agent: {userAgentStr}");
        bitsParams["CustomHeaders"] = "User-Agent: " + userAgentStr;

        // invoke BITS; it's possible to invoke it directly using the .NET API, but that seems overly complex for now
        StartBitsTransferSb.InvokeReturnAsIs(bitsParams);

        Debug.Assert(File.Exists(tmpDownloadTarget));

        if (resolverTask == null) {
            // original filename is "good enough", use it
            return tmpDownloadTarget;
        } else {
            // retrieve the resolved target
            DownloadTargetResolver.DownloadTarget target;
            try {
                // this should be ok (no deadlocks), PowerShell cmdlets internally do it the same way
                target = resolverTask.GetAwaiter().GetResult();
            } catch (TaskCanceledException) {
                throw new PipelineStoppedException();
            }

            var (httpHeadSupported, resolvedUrl, contentDisposition) = target;
            var resolvedFileName = HttpFileNameParser.GetDownloadedFileName(resolvedUrl, contentDisposition);
            var resolvedTarget = $"{DestinationDirPath}\\{resolvedFileName}";

            if (!httpHeadSupported) {
                WriteDebug("Server seems to not support HEAD requests.");
            }
            WriteDebug($"Resolved target: {resolvedTarget}");

            // rename the downloaded file
            File.Move(tmpDownloadTarget, resolvedTarget);
            return resolvedTarget;
        }
    }

    private static readonly ScriptBlock StartBitsTransferSb =
            ScriptBlock.Create(@"param($p) Import-Module BitsTransfer; BitsTransfer\Start-BitsTransfer @p");
}
