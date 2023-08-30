using System;
using System.Management.Automation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Pog.Commands.Common;

namespace Pog.Commands.Internal;

internal class ResolveDownloadTarget : ScalarCommand<ResolveDownloadTarget.DownloadTarget>, IDisposable {
    [Parameter(Mandatory = true)] public Uri OriginalUri = null!;
    [Parameter(Mandatory = true)] public DownloadParameters DownloadParameters = null!;
    [Parameter] public CmdletProgressBar.ProgressActivity ProgressActivity = new();

    private static readonly Lazy<HttpClient> Client = new();
    private readonly CancellationTokenSource _stopping = new();

    public ResolveDownloadTarget(PogCmdlet cmdlet) : base(cmdlet) {}

    public override DownloadTarget Invoke() {
        ProgressActivity.Activity ??= "Resolving URL target...";
        ProgressActivity.Description ??= $"Resolving '{OriginalUri.OriginalString}'...";
        using (new CmdletProgressBar(Cmdlet, ProgressActivity)) {
            return ResolveFinalDownloadTargetAsync(_stopping.Token, OriginalUri, DownloadParameters)
                    .GetAwaiter().GetResult(); // this should be ok, PowerShell cmdlets internally do it the same way
        }
    }

    public override void StopProcessing() {
        base.StopProcessing();
        _stopping.Cancel();
    }

    public void Dispose() => _stopping.Dispose();

    /// Resolves the passed URI and returns the final URI and Content-Disposition header after redirects.
    /// <exception cref="HttpRequestException"></exception>
    public static async Task<DownloadTarget> ResolveFinalDownloadTargetAsync(CancellationToken token, Uri originalUri,
            DownloadParameters downloadParameters, bool useGetMethod = false) {
        using var request = new HttpRequestMessage(useGetMethod ? HttpMethod.Get : HttpMethod.Head, originalUri);
        if (downloadParameters.GetUserAgentHeaderString() is {} userAgentStr) {
            request.Headers.Add("User-Agent", userAgentStr);
        }

        var completion = useGetMethod ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;
        // disposing the response resets the connection, refusing the body of the request
        using var response = await Client.Value.SendAsync(request, completion, token);

        if (response.IsSuccessStatusCode) {
            return new DownloadTarget(!useGetMethod, response.RequestMessage.RequestUri, response.Content.Headers.ContentDisposition);
        } else {
            // if HEAD requests got an error, retry with the GET method to check if it's
            //  an actual issue or the server is just dumb and blocks HEAD requests
            return await ResolveFinalDownloadTargetAsync(token, originalUri, downloadParameters, true);
        }
    }

    public record struct DownloadTarget(bool ServerSupportsHead, Uri FinalUri, ContentDispositionHeaderValue ContentDisposition);
}
