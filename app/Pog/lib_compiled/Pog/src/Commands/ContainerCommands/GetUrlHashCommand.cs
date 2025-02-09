using System;
using System.Buffers;
using System.IO;
using System.Management.Automation;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Pog.Commands.Common;
using Pog.InnerCommands;
using Pog.InnerCommands.Common;

namespace Pog.Commands.ContainerCommands;

/// <summary>Retrieves a file from the passed URL and calculates the SHA-256 hash.</summary>
[PublicAPI]
[Cmdlet(VerbsCommon.Get, "UrlHash")]
[OutputType(typeof(string))]
public sealed class GetUrlHashCommand : PogCmdlet {
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] SourceUrl = null!;

    [Parameter(ValueFromPipeline = true)]
    public UserAgentType UserAgent = default;

    protected override void ProcessRecord() {
        base.ProcessRecord();

        foreach (var url in SourceUrl) {
            try {
                // this cmdlet is expected to be used for small files, and Start-BitsTransfer has quite a significant
                //  startup overhead, so this implementation instead uses a streaming implementation based on `HttpClient`
                WriteObject(GetUrlSha256Hash(new(url), UserAgent, CancellationToken));
            } catch (TaskCanceledException) {
                throw new PipelineStoppedException();
            }
        }
    }

    private string GetUrlSha256Hash(Uri requestUri, UserAgentType userAgent, CancellationToken token) {
        using var progressBar = new CmdletProgressBar(this, new ProgressActivity {
            Activity = "Retrieving file hash",
            Description = $"Downloading '{requestUri}'...",
        });

        var (response, stream) = GetUrlStreamAsync(requestUri, userAgent, token).GetAwaiter().GetResult();
        using (response)
        using (stream) {
            var streamSize = response.Content.Headers.ContentLength;
            Action<long> progress = streamSize == null
                    ? _ => {}
                    : bytesProcessed => {
                        // ReSharper disable once AccessToDisposedClosure
                        progressBar.Update(bytesProcessed / (double) streamSize);
                    };

            using var hashAlgorithm = SHA256.Create();
            // we need to run the hash calculation on the PowerShell thread, since we write progress
            return ComputeStreamHashWithProgress(stream, hashAlgorithm, progress);
        }
    }

    private string ComputeStreamHashWithProgress(Stream stream, HashAlgorithm algorithm, Action<long> progressCb) {
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 64);
        long totalBytesRead = 0;
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0) {
            algorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalBytesRead += bytesRead;
            progressCb(totalBytesRead);
        }

        algorithm.TransformFinalBlock(buffer, 0, 0);
        ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        return HashToString(algorithm.Hash);
    }

    private static unsafe string HashToString(byte[] hash) {
        var output = stackalloc char[hash.Length * 2];
        for (int i = 0, t = 0; i < hash.Length; i++, t += 2) {
            var b = hash[i];
            output[t] = "0123456789ABCDEF"[b >> 4];
            output[t + 1] = "0123456789ABCDEF"[b & 0b1111];
        }
        return new string(output, 0, hash.Length * 2);
    }

    private static async Task<(HttpResponseMessage, Stream)> GetUrlStreamAsync(
            Uri requestUri, UserAgentType userAgent, CancellationToken token) {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("User-Agent", userAgent.GetHeaderString());

        var response = await InternalState.HttpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try {
            response.EnsureSuccessStatusCode();
            return (response, await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
        } catch {
            response.Dispose();
            throw;
        }
    }
}
