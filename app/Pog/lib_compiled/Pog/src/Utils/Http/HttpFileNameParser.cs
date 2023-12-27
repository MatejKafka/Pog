using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Web;

namespace Pog.Utils.Http;

/// <summary>
/// Utility class for getting the server-provided file name for a downloaded file.
/// </summary>
internal static class HttpFileNameParser {
    /// <param name="resolvedUri">Resolved URI (after redirects) of the downloaded file.</param>
    /// <param name="contentDisposition">The Content-Disposition header from the server response, if any.</param>
    public static string GetDownloadedFileName(Uri resolvedUri, ContentDispositionHeaderValue? contentDisposition) {
        // link to a similar function in Chromium:
        // https://github.com/chromium/chromium/blob/11a147047d7ed9d429b53e536fe1ead54fad5053/net/base/filename_util_internal.cc#L219
        // Chromium also takes into account the mime type of the response, we're not doing that for now

        // try available names in the order of preference, use the first one found that is valid
        string? fileName = null;

        // if Content-Disposition is set and valid, use the specified filename
        fileName ??= FsUtils.SanitizeFileName(GetRawDispositionFileName(contentDisposition));

        // use the last segment of the resolved URL
        var lastSegment = resolvedUri.Segments.LastOrDefault();
        fileName ??= FsUtils.SanitizeFileName(HttpUtility.UrlDecode(lastSegment));

        if (fileName == null && resolvedUri.Segments.Length == 1 && resolvedUri.Segments[0] == "/") {
            // empty path, assume that the actual file is `index.html`
            fileName = "index.html";
        }

        // use the hostname
        fileName ??= FsUtils.SanitizeFileName(resolvedUri.Host);

        // fallback default name
        fileName ??= "download";

        return fileName;
    }

    private static string? GetRawDispositionFileName(ContentDispositionHeaderValue? contentDisposition) {
        if (contentDisposition is not {DispositionType: "attachment"}) {
            return null;
        }

        var headerVal = contentDisposition switch {
            {FileNameStar: not null} => contentDisposition.FileNameStar,
            {FileName: not null} => contentDisposition.FileName,
            _ => null,
        };

        if (headerVal == null) {
            return null;
        }

        if (headerVal.StartsWith("\"") && headerVal.EndsWith("\"")) {
            // ContentDispositionHeaderValue parser leaves the quotes in the parsed filename, strip them
            headerVal = headerVal.Substring(1, headerVal.Length - 2);
        }

        return headerVal;
    }
}
