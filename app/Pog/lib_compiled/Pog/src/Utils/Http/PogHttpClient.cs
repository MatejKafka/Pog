using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pog.InnerCommands;

namespace Pog.Utils.Http;

internal class PogHttpClient : HttpClient {
    public string UserAgent => DefaultRequestHeaders.UserAgent.ToString();

    public PogHttpClient() : base(new HttpClientHandler {UseCookies = false}) {
        // configure a User-Agent containing the following components:
        // - project name and version
        DefaultRequestHeaders.UserAgent.Add(new("Pog", AssemblyVersions.GetPogVersion()));
        // - link to the project, in case anyone is curious what the user agent actually is
        DefaultRequestHeaders.UserAgent.Add(new("(https://github.com/MatejKafka/Pog)"));
        // - PowerShell edition and version
        var (isCore, pwshVersion) = AssemblyVersions.GetPowerShellVersion();
        DefaultRequestHeaders.UserAgent.Add(new(isCore ? "PowerShell" : "WindowsPowerShell", pwshVersion));
        // - Windows version
        DefaultRequestHeaders.UserAgent.Add(new("(" + Environment.OSVersion.VersionString + ")"));
    }

    private static HttpResponseMessage? EnsureSuccessOr404(HttpResponseMessage response) {
        if (response.StatusCode == HttpStatusCode.NotFound) {
            response.Dispose();
            return null;
        }
        try {
            response.EnsureSuccessStatusCode();
        } catch {
            response.Dispose();
            throw;
        }
        return response;
    }

    public async Task<JsonElement?> RetrieveJsonAsync(Uri uri, CancellationToken token = default) {
        using var response = EnsureSuccessOr404(await GetAsync(uri, token).ConfigureAwait(false));
        if (response == null) return null;
        return await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
    }

    public async Task<ZipArchive?> RetrieveZipArchiveAsync(Uri uri, CancellationToken token = default) {
        // do not dispose, otherwise the returned stream would also get closed: https://github.com/dotnet/runtime/issues/28578
        var response = EnsureSuccessOr404(await GetAsync(uri, token).ConfigureAwait(false));
        if (response == null) return null;
        return new ZipArchive(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), ZipArchiveMode.Read);
    }

    public async Task<HttpFileStream> GetStreamAsync(Uri uri, UserAgentType userAgent, CancellationToken token = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("User-Agent", userAgent.GetHeaderString());

        var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        try {
            response.EnsureSuccessStatusCode();

            // in theory, we should return the response as well and dispose it, but the authors of HttpClient are claiming
            //  that it should always be safe to just dispose the stream: https://github.com/dotnet/runtime/issues/28578
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return new(stream, response.RequestMessage.RequestUri, response.Content.Headers.ContentLength,
                    response.Content.Headers.ContentDisposition);
        } catch {
            response.Dispose();
            throw;
        }
    }

    public readonly record struct HttpFileStream(
            Stream Stream,
            Uri FinalUri,
            long? ContentLength,
            ContentDispositionHeaderValue? ContentDisposition
    ) : IDisposable {
        public string GenerateFileName() => HttpFileNameParser.GetDownloadedFileName(FinalUri, ContentDisposition);

        public void Dispose() => Stream.Dispose();
    }
}
