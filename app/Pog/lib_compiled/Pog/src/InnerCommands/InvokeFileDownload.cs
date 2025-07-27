using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Security.Cryptography;
using Pog.Commands.Common;
using Pog.InnerCommands.Common;
using Pog.Utils;

namespace Pog.InnerCommands;

/// Downloads a file and either stores it to a directory using the original name or computes its SHA-256 hash.
internal sealed class InvokeFileDownload(PogCmdlet cmdlet) : ScalarCommand<DownloadedFile>(cmdlet) {
    [Parameter] public required string SourceUrl;
    [Parameter] public required DownloadParameters DownloadParameters;
    [Parameter] public ProgressActivity ProgressActivity = new();
    // one of these two should be set
    [Parameter] public string? DestinationDirPath = null;
    [Parameter] public bool ComputeHash = false;

    /// <summary>Downloads the file from `SourceUrl` to `DestinationDirPath`.</summary>
    /// <returns>Full path of the downloaded file.</returns>
    /// <remarks>
    /// There were multiple iterations of this command: First, it used `Invoke-WebRequest` for small files and BITS
    /// (https://docs.microsoft.com/en-us/windows/win32/bits/about-bits) for larger files. In the second attempt,
    /// I dropped `iwr` and always used BITS, but the integration is somewhat annoying, it's not correctly generating
    /// the output name (Content-Disposition is ignored, and it uses everything after last / in the URL as a file name,
    /// including any query string), there's added latency from starting up the BITS service (which takes around 500 ms
    /// to start up if unused for a short period) and given that it runs under a service account, it doesn't integrate
    /// correctly with per-user features like `subst` mounts.
    ///
    /// After somewhat properly benchmarking a few reasonable options (BITS, curl, aria2, iwr, custom HttpClient impl)
    /// using the scripts in `app\Pog\_scripts\http_client_bench`, it seems that for typical download sizes, iwr, curl
    /// and the custom HttpClient impl were roughly equal and slightly faster than BITS and aria2 (for files over ~400 MB,
    /// BITS and aria2 with parallel download tend to be competitive or slightly faster, but those are rare).
    /// </remarks>
    public override DownloadedFile Invoke() {
        Debug.Assert(ComputeHash || DestinationDirPath != null);

        var description = ProgressActivity.Description;
        ProgressActivity.Activity ??= "HTTP Transfer";
        ProgressActivity.Description ??= $"Downloading '{SourceUrl}'...";
        using var progressBar = new CmdletProgressBar(Cmdlet, ProgressActivity);

        var uri = new Uri(SourceUrl);
        using var downloadStream = InternalState.HttpClient
                .GetStreamAsync(uri, DownloadParameters.UserAgent, CancellationToken).GetAwaiter().GetResult();
        var fileName = downloadStream.GenerateFileName();

        description ??= $"Downloading '{fileName}'...";
        var stream = new ProgressStream(downloadStream.Stream, new DebouncedProgress<long>(PogCmdlet.DefaultProgressInterval,
                position => progressBar.ReportSize(position, downloadStream.ContentLength, description)));

        if (ComputeHash && DestinationDirPath == null) {
            using var hashAlgorithm = SHA256.Create();
            return new(null, hashAlgorithm.ComputeHash(stream).ToHexString());
        }

        var outPath = $"{DestinationDirPath}\\{fileName}";
        WriteDebug($"Output path: {outPath}");

        using var outStream = File.Create(outPath);

        if (ComputeHash) {
            using var hasher = SHA256.Create();
            var cs = new CryptoStream(outStream, hasher, CryptoStreamMode.Write);
            stream.CopyTo(cs);
            cs.FlushFinalBlock();
            return new(outPath, hasher.Hash.ToHexString());
        } else {
            stream.CopyTo(outStream);
            return new(outPath, null);
        }
    }
}

public record struct DownloadedFile(string? Path, string? Hash);
