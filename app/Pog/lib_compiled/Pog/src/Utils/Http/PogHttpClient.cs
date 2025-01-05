using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    private async Task<HttpResponseMessage?> RetrieveAsync(string url, CancellationToken token = default) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await SendAsync(request, token).ConfigureAwait(false);

        // don't like having to manually dispose, but don't see any better way
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

    private async Task<JsonElement?> RetrieveJsonAsync(string url, CancellationToken token = default) {
        using var response = await RetrieveAsync(url, token).ConfigureAwait(false);
        if (response == null) return null;
        return await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
    }

    internal async Task<ZipArchive?> RetrieveZipArchiveAsync(string url, CancellationToken token = default) {
        // do not dispose, otherwise the returned stream would also get closed: https://github.com/dotnet/runtime/issues/28578
        var response = await RetrieveAsync(url, token).ConfigureAwait(false);
        if (response == null) return null;
        return new ZipArchive(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), ZipArchiveMode.Read);
    }

    // this should be ok (no deadlocks), PowerShell cmdlets internally do it the same way
    internal JsonElement? RetrieveJson(string url) => RetrieveJsonAsync(url).GetAwaiter().GetResult();

    // this should be ok (no deadlocks), PowerShell cmdlets internally do it the same way
    internal ZipArchive? RetrieveZipArchive(string url) => RetrieveZipArchiveAsync(url).GetAwaiter().GetResult();
}
