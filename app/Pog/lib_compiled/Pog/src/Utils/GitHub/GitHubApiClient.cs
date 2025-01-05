using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Pog.Utils.GitHub;

public class GitHubRequestException(string message) : HttpRequestException(message);

public class GitHubRateLimitException(string message) : GitHubRequestException(message);

internal class GitHubApiClient(HttpClient httpClient) {
    public IAsyncEnumerable<GitHubRelease> EnumerateReleasesAsync(
            string repo, string? apiToken = null, CancellationToken token = default) {
        return EnumerateFeedAsync<GitHubRelease>(
                $"Cannot list releases for GitHub repository '{repo}'",
                // 100 releases per page is the maximum: https://docs.github.com/en/rest/releases/releases#list-releases
                new($"https://api.github.com/repos/{repo}/releases?per_page=100"),
                // TODO: when System.Text.Json is updated to 9.0.0, add RespectRequiredConstructorParameters and RespectNullableAnnotations
                new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower},
                apiToken, token);
    }

    public IAsyncEnumerable<GitHubTag> EnumerateTagsAsync(
            string repo, string? apiToken = null, CancellationToken token = default) {
        return EnumerateFeedAsync<GitHubTag>(
                $"Cannot list tags for GitHub repository '{repo}'",
                new($"https://api.github.com/repos/{repo}/tags?per_page=100"),
                new() {PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower},
                apiToken, token);
    }

    private static HttpResponseMessage ValidateApiResponse(string errorMsg, HttpResponseMessage response) {
        if (response.IsSuccessStatusCode) {
            return response;
        }
        // ensure we clean up the response
        using var _ = response;

        if (response.StatusCode == HttpStatusCode.NotFound) {
            throw new GitHubRequestException($"{errorMsg}: Not found");
        }

        // give better message for rate limit error
        if (response is {StatusCode: HttpStatusCode.Forbidden, ReasonPhrase: "rate limit exceeded"}) {
            string? rateLimitMsg = null;
            if (response.Headers.TryGetValues("X-RateLimit-Limit", out var rateLimitArr)) {
                rateLimitMsg = $" (at most {rateLimitArr.Single()} requests/hour are allowed)";
            }

            var tokenMsg = response.RequestMessage.Headers.Authorization switch {
                null => null,
                _ => " To increase the limit, pass a GitHub API token.",
            };

            throw new GitHubRateLimitException($"{errorMsg}: GitHub API rate limit exceeded{rateLimitMsg}.{tokenMsg}");
        }

        response.EnsureSuccessStatusCode();
        throw new UnreachableException();
    }

    private async IAsyncEnumerable<T> EnumerateFeedAsync<T>(
            string errorMsg, Uri? uri, JsonSerializerOptions options, string? apiToken = null,
            [EnumeratorCancellation] CancellationToken token = default) {
        // TODO: this could be optimized by querying the "last" rel link and then requesting all pages in between in parallel
        while (uri != null) {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (apiToken != null) {
                request.Headers.Authorization = new("Bearer", apiToken);
            }

            var responseTask = httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            using var response = ValidateApiResponse(errorMsg, await responseTask.ConfigureAwait(false));

            // list all items from the current page
            var parsedIterator = response.Content.ReadFromJsonAsAsyncEnumerable<T>(options, token);
            await foreach (var element in parsedIterator.ConfigureAwait(false)) {
                if (element == null) continue;
                yield return element;
            }

            // go to the next page, if there's any
            // https://docs.github.com/en/rest/using-the-rest-api/using-pagination-in-the-rest-api
            uri = ParseLinkHeader(response, "next");
        }
    }

    // lifted from PowerShell Invoke-RestMethod implementation
    private static Uri? ParseLinkHeader(HttpResponseMessage response, string requestedRel) {
        // we only support the URL in angle brackets and `rel`, other attributes are ignored
        const string pattern = "<(?<url>.*?)>;\\s*rel=(?<quoted>\")?(?<rel>(?(quoted).*?|[^,;]*))(?(quoted)\")";
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string> links)) {
            return null;
        }

        foreach (var linkHeader in links) {
            foreach (Match match in Regex.Matches(linkHeader, pattern)) {
                if (!match.Success) continue;
                var url = match.Groups["url"].Value;
                var rel = match.Groups["rel"].Value;
                if (url != "" && string.Equals(rel, requestedRel, StringComparison.OrdinalIgnoreCase)) {
                    return new Uri(response.RequestMessage.RequestUri, url);
                }
            }
        }
        return null;
    }
}
