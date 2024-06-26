﻿using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Pog.InnerCommands;

namespace Pog.Utils.Http;

internal static class DownloadTargetResolver {
    /// Resolves the passed URI and returns the final URI and Content-Disposition header after redirects.
    /// <exception cref="HttpRequestException"></exception>
    public static async Task<DownloadTarget> ResolveAsync(CancellationToken token, Uri originalUri,
            UserAgentType userAgent, bool useGetMethod = false) {
        using var request = new HttpRequestMessage(useGetMethod ? HttpMethod.Get : HttpMethod.Head, originalUri);
        request.Headers.Add("User-Agent", userAgent.GetHeaderString());

        var completion = useGetMethod ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;
        // disposing the response resets the connection, refusing the body of the request
        using var response = await InternalState.HttpClient.SendAsync(request, completion, token).ConfigureAwait(false);

        if (response.IsSuccessStatusCode) {
            return new DownloadTarget(!useGetMethod, response.RequestMessage.RequestUri,
                    response.Content.Headers.ContentDisposition);
        } else if (!useGetMethod) {
            // if HEAD requests got an error, retry with the GET method to check if it's
            //  an actual issue or the server is just dumb and blocks HEAD requests
            return await ResolveAsync(token, originalUri, userAgent, true).ConfigureAwait(false);
        } else {
            throw new HttpRequestException(
                    $"Resolution of download file name for '{originalUri}' failed: {response.StatusCode} {response.ReasonPhrase}");
        }
    }

    public record struct DownloadTarget(
            bool ServerSupportsHead,
            Uri FinalUri,
            ContentDispositionHeaderValue ContentDisposition);
}
